using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.VideoSkip.Api;

/// <summary>
/// Exposes a single endpoint:
///   GET /api/videoskip/{itemId}
///
/// Resolution order:
///   1. Local sidecar — looks for a .skp file next to the video file
///      with the same base name (e.g. "Movie (2001).skp")
///   2. VideoSkip Exchange — searches by IMDB ID (from Jellyfin metadata),
///      fetches the first available skip file and returns its contents
///
/// The injected JS client calls this endpoint on playback start.
/// </summary>
[ApiController]
[Authorize]
[Route("api/videoskip")]
public class VideoSkipController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<VideoSkipController> _logger;
    private readonly HttpClient _httpClient;

    private const string ExchangeBaseUrl = "https://videoskip.herokuapp.com";

    /// <summary>
    /// Initializes a new instance of the <see cref="VideoSkipController"/> class.
    /// </summary>
    public VideoSkipController(
        ILibraryManager libraryManager,
        ILogger<VideoSkipController> logger,
        IHttpClientFactory httpClientFactory)
    {
        _libraryManager = libraryManager;
        _logger = logger;

        // Configured in PluginServiceRegistrator (User-Agent + 10s timeout)
        _httpClient = httpClientFactory.CreateClient("VideoSkip");
    }

    /// <summary>
    /// Returns the .skp file contents for the given Jellyfin item ID.
    /// Tries local sidecar first, then falls back to the VideoSkip Exchange.
    /// </summary>
    /// <param name="itemId">The Jellyfin item GUID.</param>
    /// <returns>The .skp file as plain text, or 404 if none found.</returns>
    [HttpGet("{itemId}")]
    [Produces("text/plain")]
    public async Task<IActionResult> GetSkipFile([FromRoute] Guid itemId)
    {
        // ── 1. Look up the item in the library ───────────────────────────────

        var item = _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            _logger.LogWarning("[VideoSkip] Item {ItemId} not found in library", itemId);
            return NotFound();
        }

        _logger.LogInformation("[VideoSkip] Looking up skip file for: {ItemName}", item.Name);

        // ── 2. Try local sidecar ─────────────────────────────────────────────

        var localResult = TryLoadLocalSidecar(item);
        if (localResult is not null)
        {
            _logger.LogInformation("[VideoSkip] Serving local sidecar for {ItemName}", item.Name);
            return Content(localResult, "text/plain");
        }

        // ── 3. Try VideoSkip Exchange ─────────────────────────────────────────

        var exchangeResult = await TryLoadFromExchangeAsync(item).ConfigureAwait(false);
        if (exchangeResult is not null)
        {
            _logger.LogInformation("[VideoSkip] Serving Exchange file for {ItemName}", item.Name);
            return Content(exchangeResult, "text/plain");
        }

        _logger.LogInformation("[VideoSkip] No skip file found for {ItemName}", item.Name);
        return NotFound();
    }

    // ── Local sidecar ────────────────────────────────────────────────────────

    private string? TryLoadLocalSidecar(BaseItem item)
    {
        if (string.IsNullOrEmpty(item.Path))
            return null;

        var dir      = Path.GetDirectoryName(item.Path);
        var baseName = Path.GetFileNameWithoutExtension(item.Path);

        if (dir is null || baseName is null)
            return null;

        var skpPath = Path.Combine(dir, baseName + ".skp");

        if (!System.IO.File.Exists(skpPath))
            return null;

        try
        {
            return System.IO.File.ReadAllText(skpPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[VideoSkip] Failed to read local sidecar at {Path}", skpPath);
            return null;
        }
    }

    // ── VideoSkip Exchange ───────────────────────────────────────────────────

    private async Task<string?> TryLoadFromExchangeAsync(BaseItem item)
    {
        try
        {
            // The Exchange search is a *title* search — it does not index IMDB
            // IDs, so querying "tt0111161" reliably returns zero results.
            // Episodes are listed under their series name, as
            // "Game of Thrones S1 E10: Fire and Blood (2011)", so search the
            // series and pick the right episode out of the results below.
            var episode = item as Episode;
            var searchTerm = episode is not null && !string.IsNullOrEmpty(episode.SeriesName)
                ? episode.SeriesName
                : item.Name;

            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                _logger.LogDebug("[VideoSkip] No usable search term for item {ItemId}", item.Id);
                return null;
            }

            // Step 1: Search the Exchange for this title
            var searchUrl = $"{ExchangeBaseUrl}/exchange/search/?q={Uri.EscapeDataString(searchTerm)}";
            _logger.LogDebug("[VideoSkip] Searching Exchange: {Url}", searchUrl);

            var searchHtml = await _httpClient.GetStringAsync(searchUrl).ConfigureAwait(false);

            // Step 2: Pick the right result out of the search page
            var videoId = FindExchangeVideoId(searchHtml, item, episode);
            if (videoId is null)
            {
                _logger.LogDebug(
                    "[VideoSkip] No Exchange result with a skip file for {Name} (searched \"{Term}\")",
                    item.Name,
                    searchTerm);
                return null;
            }

            var videoUrl = $"{ExchangeBaseUrl}/exchange/videos/{videoId}/";
            _logger.LogDebug("[VideoSkip] Found Exchange video page: {Url}", videoUrl);

            // Step 3: Load the video page and find the first skip file link
            var videoHtml = await _httpClient.GetStringAsync(videoUrl).ConfigureAwait(false);
            var skipMatch = Regex.Match(videoHtml, @"/exchange/skip/(\d+)/");
            if (!skipMatch.Success)
            {
                _logger.LogDebug("[VideoSkip] No skip files on Exchange page for {Name}", item.Name);
                return null;
            }

            // Step 4: Fetch the raw .skp content from the download endpoint
            var skipId      = skipMatch.Groups[1].Value;
            var downloadUrl = $"{ExchangeBaseUrl}/exchange/skip/{skipId}/download/";
            _logger.LogDebug("[VideoSkip] Downloading skip file: {Url}", downloadUrl);

            var skpContent = await _httpClient.GetStringAsync(downloadUrl).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(skpContent) ? null : skpContent;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogDebug("[VideoSkip] Exchange returned 404 for {Name}", item.Name);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[VideoSkip] Exchange lookup failed for {Name}", item.Name);
            return null;
        }
    }

    // Search results look like:
    //   <a href="/exchange/videos/437/">Game of Thrones S1 E10: Fire and Blood (2011)</a>
    private static readonly Regex ResultLinkRegex = new(
        @"<a\s+href=""/exchange/videos/(\d+)/"">([^<]*)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Picks the search result that actually corresponds to <paramref name="item"/>.
    /// </summary>
    private string? FindExchangeVideoId(string searchHtml, BaseItem item, Episode? episode)
    {
        // The search page has several sections; only "Videos with skip files"
        // is useful. "Videos with requests" lists titles somebody has *asked*
        // for, which have no skip file to download — matching those is why an
        // unscoped search could pick an entry that then yields nothing.
        var section = ExtractSkipFileSection(searchHtml);
        if (section is null)
        {
            return null;
        }

        var matches = ResultLinkRegex.Matches(section);
        if (matches.Count == 0)
        {
            return null;
        }

        if (episode is not null
            && episode.ParentIndexNumber is int season
            && episode.IndexNumber is int number)
        {
            // Results are ordered lexically, so "S1 E10" sorts before "S1 E1".
            // Taking the first match returns the wrong episode's cuts.
            var tag = $"S{season} E{number}:";
            foreach (Match match in matches)
            {
                if (match.Groups[2].Value.Contains(tag, StringComparison.OrdinalIgnoreCase))
                {
                    return match.Groups[1].Value;
                }
            }

            // Better to return nothing than to blank out the wrong scenes.
            _logger.LogDebug(
                "[VideoSkip] Exchange has {Series} but not {Tag}",
                episode.SeriesName,
                tag);
            return null;
        }

        // For films, disambiguate same-titled releases by production year.
        if (item.ProductionYear is int year)
        {
            var yearTag = $"({year})";
            foreach (Match match in matches)
            {
                if (match.Groups[2].Value.Contains(yearTag, StringComparison.Ordinal))
                {
                    return match.Groups[1].Value;
                }
            }
        }

        return matches[0].Groups[1].Value;
    }

    private static string? ExtractSkipFileSection(string searchHtml)
    {
        var start = searchHtml.IndexOf("Videos with skip files", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        var end = searchHtml.IndexOf("Videos with requests", start, StringComparison.OrdinalIgnoreCase);
        return end < 0 ? searchHtml[start..] : searchHtml[start..end];
    }
}

using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using MediaBrowser.Controller.Entities;
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
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Jellyfin-VideoSkip-Plugin/1.0");
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
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
            // Build a search query using IMDB ID if available, otherwise item name
            var imdbId  = item.ProviderIds.TryGetValue("Imdb", out var id) ? id : null;
            var searchQ = string.IsNullOrEmpty(imdbId)
                ? Uri.EscapeDataString(item.Name)
                : Uri.EscapeDataString(imdbId);

            // Step 1: Search the Exchange for this title
            var searchUrl = $"{ExchangeBaseUrl}/exchange/search/?q={searchQ}";
            _logger.LogDebug("[VideoSkip] Searching Exchange: {Url}", searchUrl);

            var searchHtml = await _httpClient.GetStringAsync(searchUrl).ConfigureAwait(false);

            // Step 2: Find the first video page link in the results
            // Exchange search returns links like /exchange/videos/1234/
            var videoMatch = Regex.Match(searchHtml, @"/exchange/videos/(\d+)/");
            if (!videoMatch.Success)
            {
                _logger.LogDebug("[VideoSkip] No Exchange results for {Name}", item.Name);
                return null;
            }

            var videoUrl = $"{ExchangeBaseUrl}{videoMatch.Value}";
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
}

# VideoSkip-Jellyfin
A videoskip plugin for Jellyfin Servers

For full disclosure, yes, this code was all created using Claude Sonnet 4.6.

## How To Use
Note: This plugin assumes you are on server version 12.0. If you are still on 10.11.x, check out commit `fa76bf7` (the last 10.11-targeted revision) and build from there — the 12.0 and 10.11 builds are not interchangeable.
1. Begin by installing the following plugin to your jellyfin server: https://github.com/n00bcodr/Jellyfin-JavaScript-Injector
   Make sure you add its **Jellyfin 12** repository, not the 10.11 one: `https://raw.githubusercontent.com/n00bcodr/jellyfin-plugins/main/12/manifest.json`
2. Once it is working properly, you should see "JS Injector" beneath Plugins when navigating to "Administration/Dashboard"
3. Go to "JS Injector", select "Add Script", name it something like "Video Skip", and then paste the text from "videoskip-jellyfin.js"
4. Navigate to your local server installation of Jellyfin. Once there, go to "plugins", create a folder named "VideoSkip", and add the "Jellyfin.Plugin.VideoSkip.dll" to it.
5. Reset your server.

The plugin should now be functioning!

## Upgrading to Jellyfin 12.0

Jellyfin 12.0 renumbered the project (what would have been 10.12 became 12.0), moved the server to .NET 10, and rewrote the database on first startup. Before upgrading:

- **Back up your server.** The 12.0 database migration is one-way.
- You must already be on **10.10.7 or any 10.11.x** release to upgrade to 12.0.
- Replace both halves of this plugin at the same time. The DLL must be rebuilt against Jellyfin 12.0 / .NET 10 (a 10.11 build throws `TypeLoadException` and marks the plugin as malfunctioned), and the injected script must be re-pasted.

The injected script change matters even if the DLL loads fine: 12.0 disables the legacy `X-Emby-Token` header by default, so the older script gets a 401 from the plugin and every video reports "No .skp found". The current `videoskip-jellyfin.js` uses the `Authorization: MediaBrowser Token="..."` scheme instead.

## If the plugin doesn't seem to load

Jellyfin writes a `meta.json` into the plugin folder when a plugin fails to
load, and the recorded failure status keeps it disabled **even after you drop
in a fixed DLL**. If you started 12.0 once with the old 10.11 build, that is
almost certainly the state you are in — the symptom is `/api/videoskip/...`
returning 404 while the dashboard looks fine.

Delete `/config/plugins/VideoSkip/meta.json` and restart. See
[the plugin README](Jellyfin.Plugin.VideoSkip/README.md#no-metajson-needed--but-a-bad-one-will-silently-disable-the-plugin)
for the log lines that tell you which failure you have.

## How skp files are found
Skp files are found in the same way that Jellyfin finds subtitle files. In other words, place your skp file in the same directory w/ the same name as your video file.
Support for multiple versions of skp files is not included in this release nor is planned for future releases.

## How to make skp files
Please reference the following from VideoSkip since this entire project is based on their open-source code: https://videoskip.herokuapp.com/exchange/howto/edit/

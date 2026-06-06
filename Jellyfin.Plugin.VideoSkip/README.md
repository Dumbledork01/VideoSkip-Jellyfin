# Jellyfin.Plugin.VideoSkip

A Jellyfin server plugin that automatically resolves `.skp` filter files for the VideoSkip Jellyfin client script.

## What it does

Exposes a single API endpoint:

```
GET /api/videoskip/{itemId}
```

The injected JavaScript client calls this when playback starts. The plugin:

1. **Checks for a local sidecar** — looks for a `.skp` file in the same folder as the video file with the same base name (e.g. `Game of Thrones S06E09.skp` next to `Game of Thrones S06E09.mkv`)
2. **Falls back to the VideoSkip Exchange** — if no local file is found, searches `videoskip.herokuapp.com` by item name and returns the first available skip file

## Requirements

- [.NET SDK 8.0](https://dotnet.microsoft.com/download/dotnet/8.0)
- Jellyfin Server 10.10.x

> **Note:** If your Jellyfin server version differs, update the `Version` attributes in `Jellyfin.Plugin.VideoSkip.csproj` to match. The package version must match your installed Jellyfin version exactly.

## Building

```powershell
# From the root of this repository
dotnet publish Jellyfin.Plugin.VideoSkip\Jellyfin.Plugin.VideoSkip.csproj -c Release
```

The built DLL will be at:
```
Jellyfin.Plugin.VideoSkip\bin\Release\net8.0\publish\Jellyfin.Plugin.VideoSkip.dll
```

## Installing

1. Find your Jellyfin plugin directory. On Windows it is typically:
   ```
   C:\Users\<YourUser>\AppData\Local\jellyfin\plugins\
   ```

2. Create a subfolder for the plugin:
   ```
   C:\Users\<YourUser>\AppData\Local\jellyfin\plugins\VideoSkip\
   ```

3. Copy `Jellyfin.Plugin.VideoSkip.dll` into that folder.

4. Restart your Jellyfin server.

5. Confirm it loaded: go to **Dashboard → Plugins** and look for "VideoSkip" in the list.

## Using local sidecar files

Place `.skp` files in the same folder as your video files with matching names:

```
G:\Jellyfin\TV Shows\Game of Thrones (2011)\Season 06\
    Game of Thrones S06E09.mkv
    Game of Thrones S06E09.skp   ← put it here
```

The plugin picks these up automatically — no library rescanning needed.

## Project structure

```
Jellyfin.Plugin.VideoSkip/
├── Jellyfin.Plugin.VideoSkip.sln
└── Jellyfin.Plugin.VideoSkip/
    ├── Jellyfin.Plugin.VideoSkip.csproj
    ├── Plugin.cs                       # IPlugin entry point
    ├── PluginServiceRegistrator.cs     # Registers HttpClient for Exchange requests
    ├── Configuration/
    │   └── PluginConfiguration.cs      # Plugin config (currently empty)
    └── Api/
        └── VideoSkipController.cs      # GET /api/videoskip/{itemId}
```

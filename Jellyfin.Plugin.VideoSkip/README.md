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

- [.NET SDK 10.0](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- Jellyfin Server 12.0

> **Note:** If your Jellyfin server version differs, update the `Version` attributes in `Jellyfin.Plugin.VideoSkip.csproj` to match. The package version must match your installed Jellyfin version exactly.
>
> Jellyfin 12.0 also changed the target framework — 12.0.x packages require `net10.0`, whereas 10.11.x required `net9.0`. If you retarget to a 10.11.x package version, change `<TargetFramework>` back to `net9.0` as well.

## Building

```powershell
# From the root of this repository
dotnet publish Jellyfin.Plugin.VideoSkip\Jellyfin.Plugin.VideoSkip.csproj -c Release
```

The built DLL will be at:
```
Jellyfin.Plugin.VideoSkip\bin\Release\net10.0\publish\Jellyfin.Plugin.VideoSkip.dll
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

   In Docker, the plugin directory is `/config/plugins`, so the folder is
   `/config/plugins/VideoSkip/`.

   The folder must be a *direct* child of the plugins directory — Jellyfin
   enumerates it with `TopDirectoryOnly`. The DLL itself may sit at any depth
   inside the folder.

3. Copy `Jellyfin.Plugin.VideoSkip.dll` into that folder.

   Copy only that one file, not the whole `publish` output. Every `.dll` under
   the plugin folder is loaded (`SearchOption.AllDirectories`), so stray
   dependency assemblies can conflict with the server's own copies.

4. Restart your Jellyfin server.

5. Confirm it loaded: go to **Dashboard → Plugins** and look for "VideoSkip" in the list.

   The server log is more precise. On success:
   ```
   Loaded assembly Jellyfin.Plugin.VideoSkip, Version=... from .../plugins/VideoSkip/Jellyfin.Plugin.VideoSkip.dll
   ```

### No `meta.json` needed — but a bad one will silently disable the plugin

You do **not** need to write a `meta.json`. When a plugin folder has none,
Jellyfin synthesizes a manifest at startup: the folder name becomes the plugin
name, `TargetAbi` is set to the running server version, and — with no assembly
whitelist — every DLL in the folder is loaded.

The trap is that Jellyfin *writes* a `meta.json` of its own when a plugin fails
to load, recording the failure:

```csharp
plugin.Manifest.Status = state;
return SaveManifest(plugin.Manifest, plugin.Path);
```

That status is permanently disqualifying, because loading requires
`Manifest.Status >= PluginStatus.Active`, and `Active = 0` while
`NotSupported = -2` and `Malfunctioned = -3`.

So if the server ever started with an incompatible build of this DLL — for
example a 10.11/`net9.0` build left in place during the upgrade to 12.0 — the
plugin is stamped `NotSupported` and **stays disabled even after you drop in a
correctly rebuilt DLL**. Every subsequent start logs:

```
Skipping disabled plugin <version> of VideoSkip
```

and the API endpoint 404s because the controller was never registered.

To recover, delete `meta.json` from the plugin folder (or set its `status` back
to `0`) and restart:

```bash
docker exec -it <container> cat /config/plugins/VideoSkip/meta.json
docker exec -it <container> rm /config/plugins/VideoSkip/meta.json
```

Reading the log is the fastest way to tell these apart:

| Log line | Meaning |
| --- | --- |
| `Loaded assembly Jellyfin.Plugin.VideoSkip...` | Loaded; the endpoint should answer |
| `Skipping disabled plugin ... of VideoSkip` | Pinned off by a stale `meta.json` — delete it |
| `Failed to load assembly ... incompatible version of one of the shared libraries` | The DLL is genuinely built against the wrong Jellyfin/.NET version |

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

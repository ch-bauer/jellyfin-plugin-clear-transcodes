<div align="center">
  <img src="images/icon.png" alt="Clear Transcodes" width="128" />
  <h1>Clear Transcodes</h1>
  <p>A Jellyfin plugin that keeps the transcode temp folder from filling up.</p>
</div>

Jellyfin writes every live transcode into its transcode temp directory. When a client
disconnects mid-stream, or the server is restarted while a transcode is running, the
segments that were already written stay behind — and nothing cleans them up. On a busy
server that quietly grows into tens of gigabytes.

This plugin adds one scheduled task, **Clear Transcode Directory**, that deletes any file
in that directory whose last-write time is older than a configurable age (default: 6
hours), and then removes the per-session folders those files leave empty.

## How it works

The task resolves the transcode directory the same way Jellyfin itself does — the path from
**Dashboard → Playback → Transcoding** if one is set, otherwise the server's default temp
path — so it always cleans the folder actually in use.

A file is only deleted when it clears every guard:

- **Its extension is on the allow-list.** Nothing else is ever a candidate, no matter how old
  it is — a stray `.avi` or `.db` in the transcode folder is left alone. The default list
  covers what Jellyfin actually writes there (`.ts`, `.m3u8`, `.m4s`, `.mp4`, `.mkv`,
  `.webm`, `.aac`, `.mp3`, `.ogg`, `.opus`, `.flac`, `.wav`, `.vtt`, `.srt`, `.ass`,
  `.ssa`, `.sub`, `.idx`) and can be narrowed in the settings.
- **It is not a hidden file.** Jellyfin keeps its own bookkeeping in that directory —
  notably the `.jellyfin-transcode` marker that tells the server the folder really is its
  transcode path — and no genuine transcode artefact is a dotfile, so dotfiles are never
  touched.
- **It is older than the cutoff.** This is what keeps in-progress transcodes safe: an active
  session writes segments continuously, so its files never look stale.

Beyond that: a file that can't be deleted (locked by a running `ffmpeg`, for instance) is
logged and skipped instead of aborting the run, and the root transcode directory itself is
never removed — only the per-session subfolders that are left empty.

## Configuration

**Dashboard → Plugins → Clear Transcodes**

| Setting | Default | Description |
|---|---|---|
| Max file age (hours) | 6 | Files older than this are deleted on the next run. |
| File extensions to delete | the list above | Comma-separated allow-list. Only these extensions are ever deleted; an empty list deletes nothing. A "Reset to defaults" button restores the stock list. |

**Dashboard → Scheduled Tasks → Maintenance → Clear Transcode Directory** runs the task on
demand or changes its trigger. The default trigger is every 6 hours.

Each run logs how many files it inspected, deleted, skipped and left untouched, and how much
space it freed — so you can point the task at your server, run it once, and read the log
before trusting it.

## Install

Add the plugin repository in **Dashboard → Plugins → Repositories**:

```
https://raw.githubusercontent.com/ch-bauer/jellyfin-plugin-clear-transcodes/main/manifest.json
```

Then install **Clear Transcodes** from the catalogue and restart the server.

To install manually instead, download the zip from
[Releases](https://github.com/ch-bauer/jellyfin-plugin-clear-transcodes/releases), extract it
into a folder named `Jellyfin.Plugin.ClearTranscodes` inside your server's `plugins/`
directory (`/config/plugins/` in the official Docker image,
`%ProgramData%\Jellyfin\Server\plugins\` on Windows), and restart.

Requires Jellyfin **10.11** or newer.

## Build

Requires the .NET 9 SDK.

```bash
dotnet build src/Jellyfin.Plugin.ClearTranscodes -c Release
dotnet test tests/Jellyfin.Plugin.ClearTranscodes.Tests -c Release
```

The compiled `Jellyfin.Plugin.ClearTranscodes.dll` ends up in
`src/Jellyfin.Plugin.ClearTranscodes/bin/Release/net9.0/`.

Releases are cut by pushing a `v*` tag: the workflow tests, publishes, zips the DLL together
with `meta.json`, creates the GitHub release and prints the MD5 checksum for `manifest.json`.

## License

MIT — see [LICENSE](LICENSE).

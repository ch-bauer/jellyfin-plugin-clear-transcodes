<div align="center">
  <img src="images/icon.png" alt="Clear Transcodes" width="128" />
  <h1>Clear Transcodes (Proof of Concept)</h1>
  <p>A Jellyfin plugin that keeps the transcode temp folder from filling up.</p>
</div>

> [!CAUTION]
> **This is a proof of concept, written with AI.** It is purely for testing, and there are
> many items that are known to be incorrect or broken. It is not advisable to use this on a
> non-test server.
>
> For this reason it is offered as is, with **no guarantee of support, bug fixes, or
> troubleshooting**.
>
> **It is NOT recommended to fork or build on top of this plugin!**

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

Before any of that, the directory itself has to clear two checks: it must be Jellyfin's own
default transcode path (`<cache>/transcodes`) — anything else needs the explicit opt-in
described below — and it must not be a filesystem root, which is refused outright as a
misconfiguration.

A file is then only deleted when it clears every guard:

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

Beyond that: symlinks and junctions are never followed, so the sweep cannot leave the
transcode directory; an unreadable subfolder is skipped rather than failing the run; a file
that can't be deleted (locked by a running `ffmpeg`, for instance) is logged and skipped; and
the transcode directory itself is never removed — only the per-session subfolders left empty.

Nothing is ever written, renamed or modified. The plugin's only destructive operations are
deleting allow-listed files inside the transcode directory and removing subfolders of it that
are already empty.

## Custom transcode directories

If you have pointed **Dashboard → Playback → Transcoding** somewhere other than Jellyfin's
default, the task **refuses to run** and logs the path it would have cleaned. That folder is
the one thing the plugin can't reason about — it might be a dedicated scratch disk, or it
might be somewhere that holds files worth keeping, and video and subtitle extensions are on
the delete list.

To allow it, tick **Allow cleaning a custom transcode directory** in the plugin settings after
checking that the folder really does contain nothing but scratch files. Runs against a custom
directory keep logging a warning naming the path, so it stays visible in the log.

## Configuration

**Dashboard → Plugins → Clear Transcodes**

| Setting | Default | Description |
|---|---|---|
| Max file age (hours) | 6 | Files older than this are deleted on the next run. |
| File extensions to delete | the list above | Comma-separated allow-list. Only these extensions are ever deleted; an empty list deletes nothing. A "Reset to defaults" button restores the stock list. |
| Allow cleaning a custom transcode directory | off | Required before the task will touch a transcode path other than Jellyfin's default. See below. |

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

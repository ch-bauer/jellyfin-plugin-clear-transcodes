using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ClearTranscodes.Cleanup
{
    /// <summary>
    /// The actual sweep, kept free of Jellyfin types so it can be tested against a
    /// throwaway directory. A file is deleted only when it clears every guard: its
    /// extension is on the allow-list, it is not one of Jellyfin's own dotfiles, and
    /// its last write time is older than the cutoff. Directories left empty by that
    /// are then removed.
    /// </summary>
    public class TranscodeCleaner
    {
        /// <summary>
        /// The extensions Jellyfin actually writes into the transcode directory:
        /// HLS segments and playlists, remuxed containers, extracted audio and
        /// subtitles. Anything not on this list is never touched.
        /// </summary>
        public static readonly string[] DefaultExtensions =
        {
            ".ts", ".m3u8", ".m4s", ".mp4", ".mkv", ".webm",
            ".aac", ".mp3", ".ogg", ".opus", ".flac", ".wav",
            ".vtt", ".srt", ".ass", ".ssa", ".sub", ".idx"
        };

        /// <summary>
        /// Never walk through a symlink or junction: following one would take the sweep
        /// outside the transcode directory entirely, and a linked-in media folder is full
        /// of allow-listed extensions. Inaccessible subfolders are skipped rather than
        /// aborting the whole run.
        /// </summary>
        private static readonly EnumerationOptions RecursiveOptions = new()
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        private readonly ILogger _logger;

        public TranscodeCleaner(ILogger logger)
        {
            _logger = logger;
        }

        public CleanupResult Clean(
            string root,
            DateTime cutoffUtc,
            IEnumerable<string> allowedExtensions,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            var allowed = NormalizeExtensions(allowedExtensions);

            if (allowed.Count == 0)
            {
                _logger.LogWarning(
                    "No file extensions are configured for deletion, so nothing will be removed from {Path}. Set them under Plugins -> Clear Transcodes.",
                    root);
                progress?.Report(100);
                return new CleanupResult(0, 0, 0, 0);
            }

            if (!IsSaneRoot(root))
            {
                progress?.Report(100);
                return new CleanupResult(0, 0, 0, 0);
            }

            if (!Directory.Exists(root))
            {
                _logger.LogInformation("Transcode directory {Path} does not exist, nothing to clean.", root);
                progress?.Report(100);
                return new CleanupResult(0, 0, 0, 0);
            }

            var all = Directory.GetFiles(root, "*", RecursiveOptions);
            var files = all.Where(f => IsDeletionCandidate(f, allowed)).ToArray();
            var preserved = all.Length - files.Length;
            var deleted = 0;
            var failed = 0;
            long bytes = 0;

            for (var i = 0; i < files.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = files[i];

                try
                {
                    var info = new FileInfo(file);
                    if (info.LastWriteTimeUtc < cutoffUtc)
                    {
                        var size = info.Length;
                        info.Delete();
                        deleted++;
                        bytes += size;
                        _logger.LogDebug("Deleted stale transcode file {File}", file);
                    }
                }
                catch (Exception ex)
                {
                    // A file that's mid-transcode is locked; skip it instead of
                    // aborting the whole run.
                    failed++;
                    _logger.LogWarning(ex, "Could not delete transcode file {File}", file);
                }

                progress?.Report((i + 1) * 95.0 / files.Length);
            }

            var removedDirectories = RemoveEmptyDirectories(root, cancellationToken);
            progress?.Report(100);

            return new CleanupResult(files.Length, deleted, failed, bytes)
            {
                RemovedDirectories = removedDirectories,
                Preserved = preserved
            };
        }

        /// <summary>
        /// Compares two filesystem paths for identity, tolerating trailing separators
        /// and the different spellings of the same directory. Case matters on Unix and
        /// does not on Windows.
        /// </summary>
        public static bool IsSamePath(string? left, string? right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            try
            {
                var comparison = OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;

                return string.Equals(
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
                    comparison);
            }
            catch (Exception)
            {
                // An unusable path is never "the same as" anything, which keeps the
                // caller on its cautious branch.
                return false;
            }
        }

        /// <summary>
        /// A last line of defence against a misconfigured transcode path. Sweeping a
        /// filesystem root would be catastrophic and is never what anyone meant, so
        /// refuse rather than trust the configuration.
        /// </summary>
        private bool IsSaneRoot(string root)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                _logger.LogWarning("No transcode directory is configured, nothing to clean.");
                return false;
            }

            string full;
            try
            {
                full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Transcode directory {Path} is not a usable path, skipping cleanup.", root);
                return false;
            }

            var pathRoot = Path.TrimEndingDirectorySeparator(Path.GetPathRoot(full) ?? string.Empty);
            if (full.Length == 0 || string.Equals(full, pathRoot, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError(
                    "Refusing to clean {Path}: that is a filesystem root, not a transcode directory. Check Dashboard -> Playback -> Transcoding.",
                    root);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Accepts the extensions as typed — with or without a leading dot, in any
        /// case — and drops blanks, so a stray "ts, .MP4," in the settings still
        /// does what the user meant.
        /// </summary>
        public static HashSet<string> NormalizeExtensions(IEnumerable<string>? extensions)
        {
            var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var raw in extensions ?? Enumerable.Empty<string>())
            {
                var trimmed = raw?.Trim().TrimStart('*');
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed == ".")
                {
                    continue;
                }

                normalized.Add(trimmed.StartsWith('.') ? trimmed : "." + trimmed);
            }

            return normalized;
        }

        /// <summary>
        /// Two guards, both deliberately conservative. Jellyfin keeps its own
        /// bookkeeping in the transcode directory — notably the <c>.jellyfin-transcode</c>
        /// marker that tells the server the folder really is its transcode path — and
        /// no genuine transcode artefact is a dotfile, so hidden files are never
        /// candidates. Everything else has to match the configured allow-list.
        /// </summary>
        private static bool IsDeletionCandidate(string path, HashSet<string> allowedExtensions)
        {
            var name = Path.GetFileName(path);

            if (name.StartsWith('.'))
            {
                return false;
            }

            return allowedExtensions.Contains(Path.GetExtension(name));
        }

        /// <summary>
        /// Jellyfin puts HLS segments in per-session subfolders; once their files are
        /// gone the folders are just noise. The root itself is never removed.
        /// </summary>
        private int RemoveEmptyDirectories(string root, CancellationToken cancellationToken)
        {
            var removed = 0;

            // Deepest first, so a folder that only held now-empty folders goes too.
            foreach (var dir in Directory.GetDirectories(root, "*", RecursiveOptions)
                         .OrderByDescending(d => d.Length))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                    {
                        Directory.Delete(dir);
                        removed++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not remove empty transcode directory {Directory}", dir);
                }
            }

            return removed;
        }
    }

    public record CleanupResult(int Inspected, int Deleted, int Failed, long BytesFreed)
    {
        public int RemovedDirectories { get; init; }

        /// <summary>
        /// Files that were never candidates: Jellyfin's own dotfiles, and anything
        /// whose extension is not on the allow-list.
        /// </summary>
        public int Preserved { get; init; }
    }
}

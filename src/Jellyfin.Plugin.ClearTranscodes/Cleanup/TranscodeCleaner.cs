using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ClearTranscodes.Cleanup
{
    /// <summary>
    /// The actual sweep, kept free of Jellyfin types so it can be tested against a
    /// throwaway directory. Deletes every file under <c>root</c> whose last write
    /// time is older than <c>cutoff</c>, then removes the directories that this
    /// left empty.
    /// </summary>
    public class TranscodeCleaner
    {
        private readonly ILogger _logger;

        public TranscodeCleaner(ILogger logger)
        {
            _logger = logger;
        }

        public CleanupResult Clean(string root, DateTime cutoffUtc, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            if (!Directory.Exists(root))
            {
                _logger.LogInformation("Transcode directory {Path} does not exist, nothing to clean.", root);
                progress?.Report(100);
                return new CleanupResult(0, 0, 0, 0);
            }

            var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
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

            return new CleanupResult(files.Length, deleted, failed, bytes) { RemovedDirectories = removedDirectories };
        }

        /// <summary>
        /// Jellyfin puts HLS segments in per-session subfolders; once their files are
        /// gone the folders are just noise. The root itself is never removed.
        /// </summary>
        private int RemoveEmptyDirectories(string root, CancellationToken cancellationToken)
        {
            var removed = 0;

            // Deepest first, so a folder that only held now-empty folders goes too.
            foreach (var dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories)
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
    }
}

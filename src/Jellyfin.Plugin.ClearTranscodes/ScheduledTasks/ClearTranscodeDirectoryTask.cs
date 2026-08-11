using Jellyfin.Plugin.ClearTranscodes.Cleanup;
using Jellyfin.Plugin.ClearTranscodes.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ClearTranscodes.ScheduledTasks
{
    /// <summary>
    /// Deletes any file under the server's transcode temp directory whose last
    /// write time is older than the configured MaxAgeHours. Runs on the built-in
    /// scheduled task infrastructure, so it shows up next to Jellyfin's own
    /// maintenance tasks and can be triggered manually or on a schedule.
    /// </summary>
    public class ClearTranscodeDirectoryTask : IScheduledTask
    {
        private readonly IServerConfigurationManager _config;
        private readonly ILogger<ClearTranscodeDirectoryTask> _logger;

        public ClearTranscodeDirectoryTask(IServerConfigurationManager config, ILogger<ClearTranscodeDirectoryTask> logger)
        {
            _config = config;
            _logger = logger;
        }

        public string Name => "Clear Transcode Directory";

        public string Key => "ClearTranscodeDirectory";

        public string Description => "Deletes files in the transcode temp folder older than the configured age.";

        public string Category => "Maintenance";

        public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            var configuration = Plugin.Instance?.Configuration ?? new PluginConfiguration();
            var maxAgeHours = configuration.MaxAgeHours;
            var cutoff = DateTime.UtcNow - TimeSpan.FromHours(maxAgeHours);

            // Same resolution Jellyfin itself uses: the encoding option if the user
            // set one, otherwise the server's default transcode temp directory. This
            // can throw — Jellyfin creates the directory and its marker file here, so
            // a transcode path the server cannot write to fails before we see it. That
            // is a misconfiguration to report, not a reason to fail the task.
            string path;
            try
            {
                path = _config.GetTranscodePath();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Could not determine the transcode directory, so nothing has been deleted. Jellyfin itself cannot use this path either — check Dashboard -> Playback -> Transcoding.");
                progress.Report(100);
                return Task.CompletedTask;
            }

            if (!IsAllowedDirectory(path, configuration.AllowCustomDirectory))
            {
                progress.Report(100);
                return Task.CompletedTask;
            }

            CleanupResult result;
            try
            {
                result = new TranscodeCleaner(_logger)
                    .Clean(path, cutoff, configuration.FileExtensions, progress, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // A cancelled task is a cancelled task, not a failure.
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Transcode cleanup of {Path} could not be completed.", path);
                progress.Report(100);
                return Task.CompletedTask;
            }

            _logger.LogInformation(
                "Transcode cleanup complete. Deleted {Deleted} of {Inspected} files older than {Hours}h ({Megabytes:F1} MB freed), removed {Directories} empty folders, skipped {Failed} locked files, left {Preserved} Jellyfin files untouched.",
                result.Deleted,
                result.Inspected,
                maxAgeHours,
                result.BytesFreed / 1024d / 1024d,
                result.RemovedDirectories,
                result.Failed,
                result.Preserved);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Jellyfin's own default is <c>&lt;cache&gt;/transcodes</c>, and a path that has
        /// been changed from it is the one case the plugin cannot reason about: it might
        /// be a dedicated SSD scratch folder, or it might be a directory that holds things
        /// worth keeping. So a custom path is never swept until it has been ticked off in
        /// the plugin settings.
        /// </summary>
        private bool IsAllowedDirectory(string path, bool allowCustomDirectory)
        {
            var defaultPath = Path.Combine(_config.CommonApplicationPaths.CachePath, "transcodes");

            if (TranscodeCleaner.IsSamePath(path, defaultPath))
            {
                return true;
            }

            if (!allowCustomDirectory)
            {
                _logger.LogWarning(
                    "Skipping cleanup: the transcode directory is {Path}, which is not Jellyfin's default ({DefaultPath}). Nothing has been deleted. If that path really is scratch space, tick \"Allow cleaning a custom transcode directory\" under Plugins -> Clear Transcodes.",
                    path,
                    defaultPath);
                return false;
            }

            _logger.LogWarning(
                "Cleaning {Path}, which is not Jellyfin's default transcode directory ({DefaultPath}). This was explicitly allowed in the plugin settings.",
                path,
                defaultPath);
            return true;
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            // Runs every 6 hours by default; adjust or add triggers from the
            // Scheduled Tasks page in the Jellyfin dashboard.
            yield return new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.IntervalTrigger,
                IntervalTicks = TimeSpan.FromHours(6).Ticks
            };
        }
    }
}

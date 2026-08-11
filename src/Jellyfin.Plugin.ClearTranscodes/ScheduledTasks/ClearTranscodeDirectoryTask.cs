using Jellyfin.Plugin.ClearTranscodes.Cleanup;
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
            var maxAgeHours = Plugin.Instance?.Configuration.MaxAgeHours ?? 24;
            var cutoff = DateTime.UtcNow - TimeSpan.FromHours(maxAgeHours);

            // Same resolution Jellyfin itself uses: the encoding option if the user
            // set one, otherwise the server's default transcode temp directory.
            var path = _config.GetTranscodePath();

            var result = new TranscodeCleaner(_logger).Clean(path, cutoff, progress, cancellationToken);

            _logger.LogInformation(
                "Transcode cleanup complete. Deleted {Deleted} of {Inspected} files older than {Hours}h ({Megabytes:F1} MB freed), removed {Directories} empty folders, skipped {Failed} locked files.",
                result.Deleted,
                result.Inspected,
                maxAgeHours,
                result.BytesFreed / 1024d / 1024d,
                result.RemovedDirectories,
                result.Failed);

            return Task.CompletedTask;
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

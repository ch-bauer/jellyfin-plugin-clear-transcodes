using Jellyfin.Plugin.ClearTranscodes.Cleanup;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.ClearTranscodes.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        /// <summary>
        /// Files in the transcode temp directory whose last-write time is older
        /// than this many hours get deleted when the scheduled task runs.
        /// </summary>
        public int MaxAgeHours { get; set; } = 6;

        /// <summary>
        /// Allow-list of extensions eligible for deletion. A file is only ever
        /// deleted when its extension appears here, so an empty list means the
        /// task deletes nothing at all.
        /// </summary>
        public string[] FileExtensions { get; set; } = (string[])TranscodeCleaner.DefaultExtensions.Clone();
    }
}

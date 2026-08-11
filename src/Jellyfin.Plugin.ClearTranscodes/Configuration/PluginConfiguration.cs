using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.ClearTranscodes.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        /// <summary>
        /// Files in the transcode temp directory whose last-write time is older
        /// than this many hours get deleted when the scheduled task runs.
        /// </summary>
        public int MaxAgeHours { get; set; } = 24;
    }
}

using Jellyfin.Plugin.ClearTranscodes.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.ClearTranscodes
{
    /// <summary>
    /// Plugin entry point. Registers the config page; the actual work happens
    /// in ScheduledTasks/ClearTranscodeDirectoryTask.cs.
    /// </summary>
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        /// <summary>
        /// Static accessor so the scheduled task can read the configured max age
        /// without needing DI for the plugin itself.
        /// </summary>
        public static Plugin? Instance { get; private set; }

        public override string Name => "Clear Transcodes";

        public override Guid Id => Guid.Parse("c3cbb73c-59e6-4ec6-9cba-a86ba70e73c0");

        public override string Description =>
            "Periodically deletes stale files from the transcode temp directory.";

        public IEnumerable<PluginPageInfo> GetPages()
        {
            yield return new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format("{0}.Configuration.configPage.html", GetType().Namespace)
            };
        }
    }
}

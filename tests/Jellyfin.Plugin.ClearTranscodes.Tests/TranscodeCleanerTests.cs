using Jellyfin.Plugin.ClearTranscodes.Cleanup;

using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ClearTranscodes.Tests
{
    public class TranscodeCleanerTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "cleartranscodes-tests-" + Guid.NewGuid().ToString("N"));
        private readonly TranscodeCleaner _cleaner = new(NullLogger.Instance);

        public TranscodeCleanerTests() => Directory.CreateDirectory(_root);

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }

            GC.SuppressFinalize(this);
        }

        private string WriteFile(string relativePath, TimeSpan age, int bytes = 16)
        {
            var path = Path.Combine(_root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[bytes]);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow - age);
            return path;
        }

        private CleanupResult Clean(int maxAgeHours = 24, params string[] extensions) =>
            _cleaner.Clean(
                _root,
                DateTime.UtcNow - TimeSpan.FromHours(maxAgeHours),
                extensions.Length > 0 ? extensions : TranscodeCleaner.DefaultExtensions,
                null,
                CancellationToken.None);

        [Fact]
        public void DeletesFilesOlderThanCutoff()
        {
            var stale = WriteFile("old.ts", TimeSpan.FromHours(48));

            var result = Clean();

            Assert.False(File.Exists(stale));
            Assert.Equal(1, result.Deleted);
        }

        [Fact]
        public void KeepsFilesNewerThanCutoff()
        {
            var fresh = WriteFile("new.ts", TimeSpan.FromHours(1));

            var result = Clean();

            Assert.True(File.Exists(fresh));
            Assert.Equal(0, result.Deleted);
            Assert.Equal(1, result.Inspected);
        }

        [Fact]
        public void RecursesIntoSubdirectoriesAndReportsFreedBytes()
        {
            WriteFile(Path.Combine("session-a", "segment0.ts"), TimeSpan.FromHours(48), bytes: 1024);
            WriteFile(Path.Combine("session-a", "nested", "segment1.ts"), TimeSpan.FromHours(48), bytes: 1024);
            var fresh = WriteFile(Path.Combine("session-b", "segment0.ts"), TimeSpan.FromMinutes(5));

            var result = Clean();

            Assert.Equal(3, result.Inspected);
            Assert.Equal(2, result.Deleted);
            Assert.Equal(2048, result.BytesFreed);
            Assert.True(File.Exists(fresh));
        }

        [Fact]
        public void RemovesDirectoriesLeftEmptyButKeepsTheRoot()
        {
            WriteFile(Path.Combine("session-a", "nested", "segment0.ts"), TimeSpan.FromHours(48));
            WriteFile(Path.Combine("session-b", "segment0.ts"), TimeSpan.FromMinutes(5));

            var result = Clean();

            Assert.False(Directory.Exists(Path.Combine(_root, "session-a")));
            Assert.True(Directory.Exists(Path.Combine(_root, "session-b")));
            Assert.True(Directory.Exists(_root));
            Assert.Equal(2, result.RemovedDirectories);
        }

        [Fact]
        public void KeepsJellyfinsTranscodeMarkerHoweverOldItIs()
        {
            var marker = WriteFile(".jellyfin-transcode", TimeSpan.FromDays(90));
            var stale = WriteFile("old.ts", TimeSpan.FromHours(48));

            var result = Clean();

            Assert.True(File.Exists(marker));
            Assert.False(File.Exists(stale));
            Assert.Equal(1, result.Inspected);
            Assert.Equal(1, result.Deleted);
            Assert.Equal(1, result.Preserved);
        }

        [Fact]
        public void KeepsHiddenFilesInSubdirectoriesAndTheirFolders()
        {
            var marker = WriteFile(Path.Combine("session-a", ".keep"), TimeSpan.FromDays(90));
            WriteFile(Path.Combine("session-a", "segment0.ts"), TimeSpan.FromHours(48));

            var result = Clean();

            Assert.True(File.Exists(marker));
            Assert.True(Directory.Exists(Path.Combine(_root, "session-a")));
            Assert.Equal(0, result.RemovedDirectories);
            Assert.Equal(1, result.Preserved);
        }

        [Fact]
        public void NeverTouchesExtensionsOutsideTheAllowList()
        {
            var library = WriteFile("holiday-video.avi", TimeSpan.FromDays(365));
            var database = WriteFile("library.db", TimeSpan.FromDays(365));
            var noExtension = WriteFile("README", TimeSpan.FromDays(365));
            var segment = WriteFile("segment0.ts", TimeSpan.FromHours(48));

            var result = Clean();

            Assert.True(File.Exists(library));
            Assert.True(File.Exists(database));
            Assert.True(File.Exists(noExtension));
            Assert.False(File.Exists(segment));
            Assert.Equal(1, result.Inspected);
            Assert.Equal(3, result.Preserved);
        }

        [Fact]
        public void HonoursANarrowedAllowList()
        {
            var segment = WriteFile("segment0.ts", TimeSpan.FromHours(48));
            var remux = WriteFile("remux.mp4", TimeSpan.FromHours(48));

            var result = Clean(24, ".ts");

            Assert.False(File.Exists(segment));
            Assert.True(File.Exists(remux));
            Assert.Equal(1, result.Deleted);
            Assert.Equal(1, result.Preserved);
        }

        [Fact]
        public void AnEmptyAllowListDeletesNothing()
        {
            var segment = WriteFile("segment0.ts", TimeSpan.FromDays(365));

            var result = _cleaner.Clean(_root, DateTime.UtcNow, Array.Empty<string>(), null, CancellationToken.None);

            Assert.True(File.Exists(segment));
            Assert.Equal(0, result.Deleted);
        }

        [Theory]
        [InlineData("ts")]
        [InlineData(".TS")]
        [InlineData("  .ts  ")]
        [InlineData("*.ts")]
        public void ExtensionsAreAcceptedHoweverTheyAreTyped(string configured)
        {
            var segment = WriteFile("segment0.ts", TimeSpan.FromHours(48));

            var result = Clean(24, configured);

            Assert.False(File.Exists(segment));
            Assert.Equal(1, result.Deleted);
        }

        [Fact]
        public void ExtensionMatchingIsCaseInsensitive()
        {
            var segment = WriteFile("SEGMENT0.TS", TimeSpan.FromHours(48));

            var result = Clean(24, ".ts");

            Assert.False(File.Exists(segment));
            Assert.Equal(1, result.Deleted);
        }

        [Fact]
        public void MissingDirectoryIsNotAnError()
        {
            var result = _cleaner.Clean(
                Path.Combine(_root, "does-not-exist"),
                DateTime.UtcNow,
                TranscodeCleaner.DefaultExtensions,
                null,
                CancellationToken.None);

            Assert.Equal(0, result.Inspected);
            Assert.Equal(0, result.Deleted);
        }

        [Fact]
        public void LockedFileIsSkippedWithoutAbortingTheRun()
        {
            // Only Windows enforces the share mode on unlink; on Unix an open file
            // deletes just fine, so there is nothing to simulate there.
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            var locked = WriteFile("locked.ts", TimeSpan.FromHours(48));
            var other = WriteFile("also-old.ts", TimeSpan.FromHours(48));

            using (File.Open(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var result = Clean();

                Assert.False(File.Exists(other));
                Assert.True(File.Exists(locked));
                Assert.Equal(1, result.Deleted);
                Assert.Equal(1, result.Failed);
            }
        }

        [Fact]
        public void ProgressReachesOneHundred()
        {
            WriteFile("old.ts", TimeSpan.FromHours(48));
            var reported = new RecordingProgress();

            _cleaner.Clean(
                _root,
                DateTime.UtcNow,
                TranscodeCleaner.DefaultExtensions,
                reported,
                CancellationToken.None);

            Assert.Contains(100, reported.Values);
        }

        /// <summary>
        /// Synchronous recorder — <see cref="Progress{T}"/> posts its callbacks
        /// asynchronously, which would race the assertion.
        /// </summary>
        private sealed class RecordingProgress : IProgress<double>
        {
            public List<double> Values { get; } = new();

            public void Report(double value) => Values.Add(value);
        }
    }
}

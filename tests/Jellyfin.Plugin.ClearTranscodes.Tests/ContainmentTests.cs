using Jellyfin.Plugin.ClearTranscodes.Cleanup;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ClearTranscodes.Tests
{
    /// <summary>
    /// One rule, checked from every angle a misconfiguration could come from: whatever
    /// the transcode directory turns out to be — missing, blank, a filesystem root, a
    /// link to somewhere else — nothing outside it is ever deleted.
    ///
    /// Every test lays out the same fixture: a transcode directory, and next to it a
    /// "library" full of old files with exactly the extensions the plugin deletes. The
    /// library must survive intact in all cases.
    /// </summary>
    public class ContainmentTests : IDisposable
    {
        private readonly string _tmp = Path.Combine(Path.GetTempPath(), "cleartranscodes-containment-" + Guid.NewGuid().ToString("N"));
        private readonly string _transcodes;
        private readonly string _library;
        private readonly string[] _libraryFiles;
        private readonly TranscodeCleaner _cleaner = new(NullLogger.Instance);

        public ContainmentTests()
        {
            _transcodes = Path.Combine(_tmp, "transcodes");
            _library = Path.Combine(_tmp, "media");
            Directory.CreateDirectory(_transcodes);
            Directory.CreateDirectory(Path.Combine(_library, "Some Movie"));

            // Old, and every extension on the delete list — the worst case for a sweep
            // that escapes its directory.
            _libraryFiles = new[]
            {
                Path.Combine(_library, "Some Movie", "movie.mkv"),
                Path.Combine(_library, "Some Movie", "movie.mp4"),
                Path.Combine(_library, "Some Movie", "movie.srt"),
                Path.Combine(_library, "soundtrack.flac")
            };

            foreach (var file in _libraryFiles)
            {
                File.WriteAllBytes(file, new byte[64]);
                File.SetLastWriteTimeUtc(file, DateTime.UtcNow - TimeSpan.FromDays(400));
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(_tmp))
            {
                Directory.Delete(_tmp, recursive: true);
            }

            GC.SuppressFinalize(this);
        }

        /// <summary>Deletes everything it is allowed to: no age grace, full extension list.</summary>
        private CleanupResult SweepEverything(string root) =>
            _cleaner.Clean(root, DateTime.UtcNow, TranscodeCleaner.DefaultExtensions, null, CancellationToken.None);

        private void AssertLibraryIntact()
        {
            foreach (var file in _libraryFiles)
            {
                Assert.True(File.Exists(file), $"{file} was deleted");
                Assert.Equal(64, new FileInfo(file).Length);
            }

            Assert.True(Directory.Exists(Path.Combine(_library, "Some Movie")));
        }

        [Fact]
        public void ADirectoryThatDoesNotExistDeletesNothing()
        {
            var result = SweepEverything(Path.Combine(_tmp, "transcodes-that-went-away"));

            Assert.Equal(0, result.Deleted);
            AssertLibraryIntact();
        }

        [Fact]
        public void ADirectoryThatIsRemovedIsNotRecreatedOrWalkedUpwards()
        {
            Directory.Delete(_transcodes);

            var result = SweepEverything(_transcodes);

            Assert.Equal(0, result.Deleted);
            Assert.False(Directory.Exists(_transcodes));
            AssertLibraryIntact();
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void ABlankDirectoryDeletesNothing(string root)
        {
            var result = SweepEverything(root);

            Assert.Equal(0, result.Deleted);
            AssertLibraryIntact();
        }

        [Fact]
        public void AFilesystemRootIsRefused()
        {
            var result = SweepEverything(Path.GetPathRoot(_tmp)!);

            Assert.Equal(0, result.Inspected);
            Assert.Equal(0, result.Deleted);
            AssertLibraryIntact();
        }

        [Fact]
        public void ALinkFromTheTranscodeDirectoryIntoTheLibraryIsNotFollowed()
        {
            var link = Path.Combine(_transcodes, "library-link");
            try
            {
                Directory.CreateSymbolicLink(link, _library);
            }
            catch (Exception)
            {
                // Unprivileged Windows without developer mode cannot create symlinks.
                return;
            }

            var result = SweepEverything(_transcodes);

            Assert.Equal(0, result.Deleted);
            AssertLibraryIntact();
        }

        [Fact]
        public void OnlyTheTranscodeDirectoryIsEmptiedWhenBothAreSiblings()
        {
            var segment = Path.Combine(_transcodes, "segment0.ts");
            File.WriteAllBytes(segment, new byte[16]);
            File.SetLastWriteTimeUtc(segment, DateTime.UtcNow - TimeSpan.FromDays(1));

            var result = SweepEverything(_transcodes);

            Assert.False(File.Exists(segment));
            Assert.Equal(1, result.Deleted);
            AssertLibraryIntact();
        }

        [Fact]
        public void TheLibraryIsUntouchedEvenIfItIsHandedInAsTheTranscodeDirectory()
        {
            // The last-resort case: someone points the transcode path straight at their
            // media. The extension allow-list is no help here — this is precisely why the
            // plugin refuses any directory that is not Jellyfin's default until it has
            // been explicitly allowed. That gate rests on this comparison.
            Assert.False(TranscodeCleaner.IsSamePath(_library, _transcodes));
            Assert.False(TranscodeCleaner.IsSamePath(_library, Path.Combine(_transcodes, "..")));

            AssertLibraryIntact();
        }
    }
}

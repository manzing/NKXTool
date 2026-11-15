namespace NKXTool.Core.Tests;

using NKXTool.Core;
using NKXTool.Core.Common;
using NKXTool.Core.Containers.Nkx;
using Xunit;

public class LongRootNameTests
{
    [Fact]
    public async Task ContainerWhoseRootNameExceedsTheHeaderProbeOpens()
    {
        // "Additional samples" is 38 bytes once NUL-terminated, so the root header is 68 bytes and
        // the name no longer fits in the 64-byte probe read at open time.
        string root = Path.Combine(Path.GetTempPath(), "nkxtool_root_" + Guid.NewGuid().ToString("N"));
        string drumsDirectory = Path.Combine(root, "Additional samples", "Drums");
        string outputNkx = Path.Combine(root, "out.nkx");
        Directory.CreateDirectory(drumsDirectory);

        try
        {
            File.WriteAllBytes(Path.Combine(drumsDirectory, "kick.ncw"), new byte[] { 1, 2, 3, 4 });

            await NkxArchiveWriter.PackAsync(new NkxArchiveWriter.PackOptions
            {
                SourcePathOrFileList = root,
                OutputFilePath = outputNkx
            });

            using var archive = NkxArchive.Open(outputNkx);
            ArchiveEntry entry = Assert.Single(archive.Entries, e => !e.IsDirectory);
            Assert.Equal(@"Additional samples\Drums\kick.ncw", entry.RelativePath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

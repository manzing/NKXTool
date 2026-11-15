namespace NKXTool.Core.Tests;

using NKXTool.Core;
using NKXTool.Core.Common;
using NKXTool.Core.Containers.Nkx;
using Xunit;

public class PackNameDecodingTests
{
    [Fact]
    public async Task PackingDecodesEncodedNames()
    {
        string root = Path.Combine(Path.GetTempPath(), "nkxtool_pack_" + Guid.NewGuid().ToString("N"));
        string drumsDirectory = Path.Combine(root, "Samples", "Drums");
        string outputNkx = Path.Combine(root, "out.nkx");
        Directory.CreateDirectory(drumsDirectory);

        try
        {
            File.WriteAllBytes(Path.Combine(drumsDirectory, "kick%2A.ncw"), new byte[] { 1, 2, 3, 4 });

            var result = await NkxArchiveWriter.PackAsync(new NkxArchiveWriter.PackOptions
            {
                SourcePathOrFileList = root,
                OutputFilePath = outputNkx
            });

            Assert.Equal(1, result.DecodedNameCount);

            using var archive = NkxArchive.Open(outputNkx);
            ArchiveEntry entry = Assert.Single(archive.Entries, e => !e.IsDirectory);
            Assert.Equal(@"Samples\Drums\kick*.ncw", entry.RelativePath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PackingTheSamplesSubfolderKeepsTheContainerRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "nkxtool_pack_" + Guid.NewGuid().ToString("N"));
        string samplesDirectory = Path.Combine(root, "Extract", "Samples", "Drums");
        string outputNkx = Path.Combine(root, "out.nkx");
        Directory.CreateDirectory(samplesDirectory);

        try
        {
            File.WriteAllBytes(Path.Combine(samplesDirectory, "kick%2A.ncw"), new byte[] { 1, 2, 3, 4 });

            await NkxArchiveWriter.PackAsync(new NkxArchiveWriter.PackOptions
            {
                SourcePathOrFileList = Path.Combine(root, "Extract", "Samples"),
                OutputFilePath = outputNkx
            });

            using var archive = NkxArchive.Open(outputNkx);
            ArchiveEntry entry = Assert.Single(archive.Entries, e => !e.IsDirectory);
            Assert.Equal(@"Samples\Drums\kick*.ncw", entry.RelativePath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

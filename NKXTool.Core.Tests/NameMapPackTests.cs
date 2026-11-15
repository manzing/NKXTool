namespace NKXTool.Core.Tests;

using NKXTool.Core;
using NKXTool.Core.Common;
using NKXTool.Core.Containers.Nkx;
using Xunit;

public class NameMapPackTests
{
    [Fact]
    public async Task PackingASubfolderFindsTheMapAboveAndRebasesItsKeys()
    {
        string root = Path.Combine(Path.GetTempPath(), "nkxtool_pack_" + Guid.NewGuid().ToString("N"));
        string extractionRoot = Path.Combine(root, "Extract");
        string samplesDirectory = Path.Combine(extractionRoot, "Samples", "Drums");
        string outputNkx = Path.Combine(root, "out.nkx");
        Directory.CreateDirectory(samplesDirectory);

        try
        {
            File.WriteAllBytes(Path.Combine(samplesDirectory, "kick_.ncw"), new byte[] { 1, 2, 3, 4 });

            var map = new ArchiveNameMap();
            map.Add(@"Samples\Drums\kick_.ncw", @"Samples\Drums\kick*.ncw");
            Assert.True(map.TrySave(extractionRoot));

            var result = await NkxArchiveWriter.PackAsync(new NkxArchiveWriter.PackOptions
            {
                SourcePathOrFileList = Path.Combine(extractionRoot, "Samples"),
                OutputFilePath = outputNkx
            });

            Assert.Equal(1, result.RestoredNameCount);
            Assert.Equal(Path.Combine(extractionRoot, ArchiveNameMap.DefaultFileName), result.NameMapPath);

            using var archive = NkxArchive.Open(outputNkx);
            ArchiveEntry entry = Assert.Single(archive.Entries.Where(e => !e.IsDirectory));
            Assert.Equal(@"Samples\Drums\kick*.ncw", entry.RelativePath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PackingAFolderWithoutMapKeepsTheNamesAsTheyAreOnDisk()
    {
        string root = Path.Combine(Path.GetTempPath(), "nkxtool_pack_" + Guid.NewGuid().ToString("N"));
        string samplesDirectory = Path.Combine(root, "Plain", "Samples", "Drums");
        string outputNkx = Path.Combine(root, "out.nkx");
        Directory.CreateDirectory(samplesDirectory);

        try
        {
            File.WriteAllBytes(Path.Combine(samplesDirectory, "kick_.ncw"), new byte[] { 1, 2, 3, 4 });

            var result = await NkxArchiveWriter.PackAsync(new NkxArchiveWriter.PackOptions
            {
                SourcePathOrFileList = Path.Combine(root, "Plain"),
                OutputFilePath = outputNkx
            });

            Assert.Equal(0, result.RestoredNameCount);
            Assert.Null(result.NameMapPath);

            using var archive = NkxArchive.Open(outputNkx);
            ArchiveEntry entry = Assert.Single(archive.Entries.Where(e => !e.IsDirectory));
            Assert.Equal(@"Samples\Drums\kick_.ncw", entry.RelativePath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

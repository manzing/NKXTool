namespace NKXTool.Core.Tests;

using NKXTool.Core.Common;
using Xunit;

public class ArchiveNameMapTests
{
    [Fact]
    public void DiskNameResolvesToArchiveNameWithEitherSeparator()
    {
        var map = new ArchiveNameMap();
        map.Add(@"Samples\Drums\xc_.ncw", @"Samples\Drums\xc*.ncw");

        Assert.Equal(@"Samples\Drums\xc*.ncw", map.Resolve(@"Samples\Drums\xc_.ncw"));
        Assert.Equal(@"Samples\Drums\xc*.ncw", map.Resolve("Samples/Drums/xc_.ncw"));
        Assert.Equal(@"Samples\Drums\other.ncw", map.Resolve(@"Samples\Drums\other.ncw"));
    }

    [Fact]
    public void SavingASecondExtractionKeepsTheEarlierEntries()
    {
        string directory = Path.Combine(Path.GetTempPath(), "nkxtool_names_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var first = new ArchiveNameMap();
            first.Add(@"Samples\a_.ncw", @"Samples\a*.ncw");
            Assert.True(first.TrySave(directory));

            var second = new ArchiveNameMap();
            second.Add(@"Samples\b_.ncw", @"Samples\b?.ncw");
            Assert.True(second.TrySave(directory));

            ArchiveNameMap? merged = ArchiveNameMap.Load(directory);
            Assert.NotNull(merged);
            Assert.Equal(2, merged!.Count);
            Assert.Equal(@"Samples\a*.ncw", merged.Resolve(@"Samples\a_.ncw"));
            Assert.Equal(@"Samples\b?.ncw", merged.Resolve(@"Samples\b_.ncw"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DeleteRemovesTheMapSoPackKeepsDiskNames()
    {
        string directory = Path.Combine(Path.GetTempPath(), "nkxtool_names_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            Assert.False(ArchiveNameMap.Delete(directory)); // nothing to delete

            var map = new ArchiveNameMap();
            map.Add(@"Samples\a_.ncw", @"Samples\a*.ncw");
            Assert.True(map.TrySave(directory));
            Assert.True(ArchiveNameMap.Delete(directory));

            Assert.Null(ArchiveNameMap.Load(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MapSurvivesSaveAndLoad()
    {
        string directory = Path.Combine(Path.GetTempPath(), "nkxtool_names_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var map = new ArchiveNameMap();
            Assert.False(map.TrySave(directory)); // nothing recorded: no file left behind
            Assert.Null(ArchiveNameMap.Load(directory));

            map.Add(@"Samples\Drums\xc_.ncw", @"Samples\Drums\xc*.ncw");
            Assert.True(map.TrySave(directory));

            ArchiveNameMap? reloaded = ArchiveNameMap.Load(directory);
            Assert.NotNull(reloaded);
            Assert.Equal(1, reloaded!.Count);
            Assert.Equal(@"Samples\Drums\xc*.ncw", reloaded.Resolve(@"Samples\Drums\xc_.ncw"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

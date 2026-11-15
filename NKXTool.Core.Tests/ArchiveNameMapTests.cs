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

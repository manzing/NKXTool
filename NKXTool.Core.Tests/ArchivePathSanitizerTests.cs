namespace NKXTool.Core.Tests;

using NKXTool.Core.Common;
using Xunit;

public class ArchivePathSanitizerTests
{
    [Fact]
    public void EntryNameWithStarIsMapped()
    {
        const string archivePath =
            @"Samples\Jazz Drums\Fills\100bpm\XC\sam_drums_loop_100bpm_breaks_short_14_xc*.ncw";

        Assert.False(ArchivePathSanitizer.IsWindowsSafe(archivePath));
        Assert.Equal(
            @"Samples\Jazz Drums\Fills\100bpm\XC\sam_drums_loop_100bpm_breaks_short_14_xc_.ncw",
            ArchivePathSanitizer.ToWindowsSafeRelativePath(archivePath));
    }

    [Fact]
    public void OrdinaryEntryNameIsLeftUntouched()
    {
        const string archivePath = @"Samples\Jazz Drums\Kick 01.ncw";

        Assert.True(ArchivePathSanitizer.IsWindowsSafe(archivePath));
        Assert.Equal(archivePath, ArchivePathSanitizer.ToWindowsSafeRelativePath(archivePath));
    }

    [Theory]
    [InlineData(@"Samples\a<b.ncw", @"Samples\a_b.ncw")]
    [InlineData(@"Samples\a>b.ncw", @"Samples\a_b.ncw")]
    [InlineData(@"Samples\a:b.ncw", @"Samples\a_b.ncw")]
    [InlineData("Samples\\a\"b.ncw", "Samples\\a_b.ncw")]
    [InlineData(@"Samples\a|b.ncw", @"Samples\a_b.ncw")]
    [InlineData(@"Samples\a?b.ncw", @"Samples\a_b.ncw")]
    [InlineData(@"Samples\a*b.ncw", @"Samples\a_b.ncw")]
    [InlineData("Samples\\a\u0001b.ncw", "Samples\\a_b.ncw")]
    public void EveryCharacterWindowsForbidsIsMapped(string archivePath, string diskPath)
    {
        Assert.False(ArchivePathSanitizer.IsWindowsSafe(archivePath));
        Assert.Equal(diskPath, ArchivePathSanitizer.ToWindowsSafeRelativePath(archivePath));
    }

    [Fact]
    public void ReservedDeviceNamesAreMapped()
    {
        Assert.Equal("_CON.ncw", ArchivePathSanitizer.ToWindowsSafeRelativePath("CON.ncw"));
        Assert.Equal(@"Samples\_aux.txt", ArchivePathSanitizer.ToWindowsSafeRelativePath(@"Samples\aux.txt"));
    }
}

namespace NKXTool.Core.Tests;

using NKXTool.Core.Common;
using Xunit;

public class ArchivePathCodecTests
{
    [Theory]
    [InlineData(@"Samples\a*b.ncw", @"Samples\a%2Ab.ncw")]
    [InlineData(@"Samples\a?b.ncw", @"Samples\a%3Fb.ncw")]
    [InlineData(@"Samples\a:b.ncw", @"Samples\a%3Ab.ncw")]
    [InlineData("Samples\\a\"b.ncw", "Samples\\a%22b.ncw")]
    [InlineData(@"Samples\a<b.ncw", @"Samples\a%3Cb.ncw")]
    [InlineData(@"Samples\a>b.ncw", @"Samples\a%3Eb.ncw")]
    [InlineData(@"Samples\a|b.ncw", @"Samples\a%7Cb.ncw")]
    [InlineData("Samples\\a\u0001b.ncw", "Samples\\a%01b.ncw")]
    [InlineData(@"Samples\100%.ncw", @"Samples\100%25.ncw")]
    public void EveryForbiddenCharacterIsEncoded(string archivePath, string diskPath)
    {
        Assert.True(ArchivePathCodec.NeedsEncoding(archivePath));
        Assert.Equal(diskPath, ArchivePathCodec.Encode(archivePath));
        Assert.Equal(archivePath, ArchivePathCodec.Decode(diskPath));
    }

    [Fact]
    public void EncodingIsExactEvenForNamesThatLookEncoded()
    {
        const string archivePath = @"Samples\x%2A_.ncw";
        string diskPath = ArchivePathCodec.Encode(archivePath);

        Assert.Equal(@"Samples\x%252A_.ncw", diskPath);
        Assert.Equal(archivePath, ArchivePathCodec.Decode(diskPath));
    }

    [Fact]
    public void ReservedDeviceNamesAreEncodedAndDecoded()
    {
        Assert.Equal("%43ON.ncw", ArchivePathCodec.Encode("CON.ncw"));
        Assert.Equal(@"Samples\%61ux.txt", ArchivePathCodec.Encode(@"Samples\aux.txt"));
        Assert.Equal(@"Samples\aux.txt", ArchivePathCodec.Decode(@"Samples\%61ux.txt"));
        Assert.Equal("CON.ncw", ArchivePathCodec.Decode("%43ON.ncw"));
    }

    [Fact]
    public void DecodingLeavesForeignPercentSequencesAlone()
    {
        Assert.Equal("a%ZZ.ncw", ArchivePathCodec.Decode("a%ZZ.ncw"));
        Assert.Equal("a%.ncw", ArchivePathCodec.Decode("a%.ncw"));
        Assert.Equal("a%4.ncw", ArchivePathCodec.Decode("a%4.ncw"));
    }

    [Fact]
    public void OrdinaryNamesAndSeparatorsAreUntouched()
    {
        const string archivePath = @"Samples\Jazz Drums\Kick 01.ncw";

        Assert.False(ArchivePathCodec.NeedsEncoding(archivePath));
        Assert.Equal(archivePath, ArchivePathCodec.Encode(archivePath));
        Assert.Equal(archivePath, ArchivePathCodec.Decode(archivePath));
    }
}

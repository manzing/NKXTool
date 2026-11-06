namespace NKXTool.Core.Tests;

using NKXTool.Core.Cryptography;
using Xunit;

public class NkxRegistryKeysTests
{
    // Values taken from a real registry export (HKLM\SOFTWARE\Native Instruments\All Saints Choir).
    private const string Hu = "7267BA9662B565CEFBA0C3D9BB870997";
    private const string Jdx = "06B203D38BED651C4F5A2FD2F0A0ADF010A04AA958290A8E44758AE97DC67B19";
    private const string HuTooShort = "7267BA9662B565CEFBA0C3D9BB8709";
    private const string JdxTooShort = "06B203D38BED651C4F5A2FD2F0A0ADF010A04AA958290A8E44758AE97DC67B";
    private const string HuNotHex = "7267BA9662B565CEFBA0C3D9BB87099Z";
    private const string JdxNotHex = "ZZB203D38BED651C4F5A2FD2F0A0ADF010A04AA958290A8E44758AE97DC67B19";

    [Fact]
    public void TestCreateEntryFromRegistryExport()
    {
        var entry = NkxRegistryKeys.CreateEntry("All Saints Choir", Hu, Jdx);

        Assert.NotNull(entry);
        Assert.Equal(Hu, entry.HuHex);
        Assert.Equal(Jdx, entry.JdxHex);
        Assert.Equal(16, entry.HuBytes.Length);
        Assert.Equal(32, entry.JdxBytes.Length);
        // The export has no SNPID/RegKey/Company values.
        Assert.Equal(string.Empty, entry.Snpid);
        Assert.Equal("All Saints Choir", entry.RegKey);
        Assert.Equal("Unknown", entry.Company);
    }

    [Fact]
    public void TestCreateEntryReadsOptionalValues()
    {
        var entry = NkxRegistryKeys.CreateEntry(" Some Library ", " " + Hu + " ", Jdx.ToLowerInvariant(), "144", "LibRegKey", "Native Instruments");

        Assert.NotNull(entry);
        Assert.Equal(Hu, entry.HuHex);
        Assert.Equal(Jdx, entry.JdxHex);
        Assert.Equal("144", entry.Snpid);
        Assert.Equal("LibRegKey", entry.RegKey);
        Assert.Equal("Native Instruments", entry.Company);
    }

    [Theory]
    [InlineData(null, Jdx)]                       // HU missing
    [InlineData("", Jdx)]
    [InlineData(Hu, null)]                        // JDX missing
    [InlineData(Hu, "")]
    [InlineData(HuTooShort, Jdx)]                                // HU too short
    [InlineData(Hu, JdxTooShort)]                                // JDX too short
    [InlineData(HuNotHex, Jdx)]                                  // HU not hexadecimal
    [InlineData(Hu, JdxNotHex)]                                  // JDX not hexadecimal
    public void TestCreateEntryRejectsInvalidKeyMaterial(string? hu, string? jdx)
    {
        Assert.Null(NkxRegistryKeys.CreateEntry("Library", hu, jdx));
    }

    [Fact]
    public void TestGetAliasesUsesLibraryNameAndFolders()
    {
        var entry = NkxRegistryKeys.CreateEntry("All Saints Choir", Hu, Jdx);
        Assert.NotNull(entry);

        var aliases = NkxRegistryKeys.GetAliases(
            "All Saints Choir",
            entry,
            contentDir: @"G:\Libs\All Saints Choir LiTE",
            installDir: @"C:\Program Files\Native Instruments\All Saints Choir\",
            displayName: "All Saints Choir");

        Assert.Equal(new[] { "All Saints Choir", "All Saints Choir LiTE" }, aliases);
    }

    [Fact]
    public void TestGetAliasesSkipsEmptyCandidatesAndDuplicates()
    {
        var entry = NkxRegistryKeys.CreateEntry("Koto", Hu, Jdx, snpid: "090");
        Assert.NotNull(entry);

        // libraryName, RegKey and the ContentDir leaf are all "Koto": kept once.
        var aliases = NkxRegistryKeys.GetAliases(null, entry, contentDir: @"G:\Libs\Koto", displayName: "  ");

        Assert.Equal(new[] { "Koto", "090" }, aliases);
    }

    [Fact]
    public void TestRegistryLookupNeverThrows()
    {
        // A runtime library installed while the tool is running, a missing NI key or a denied access
        // must never break extraction: the lookup just answers false.
        Assert.False(NkxRegistryKeys.TryGet("NKXTool_NoSuchLibrary_" + Guid.NewGuid().ToString("N"), out var missing));
        Assert.Null(missing);
        Assert.False(NkxRegistryKeys.TryGet(null, out _));
        Assert.False(NkxRegistryKeys.TryGet("   ", out _));
        Assert.True(NkxRegistryKeys.Count >= 0);
    }
}

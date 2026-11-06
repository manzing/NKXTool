namespace NKXTool.Core.Tests;

using System.Text;
using NKXTool.Core;
using NKXTool.Core.Common;
using Xunit;

public class NkxTests
{
    private const string PercussionNkx = @"C:\Dev\AI-Workspaces\REA\innkx\Percussion.nkx";
    private const string BawuNkr = @"C:\Dev\AI-Workspaces\REA\innkx\Kontakt_files\Bawu.nkr";
    private const string BrassEnsembleNicnt = @"C:\Dev\AI-Workspaces\REA\innkx\Kontakt_files\Brass Ensemble.nicnt";
    private const string EthnicWindsNicnt = @"C:\Dev\AI-Workspaces\REA\innkx\Kontakt_files\Ethnic Winds.nicnt";
    private const string EwqlsoNks = @"C:\Dev\AI-Workspaces\REA\innkx\Kontakt_files\EWQLSO_Gold_lib_part8.nks";
    private const string BrassEnsembleNkx = @"C:\temp\source\Brass Ensemble\Samples\Brass Ensemble.nkx";
    private const string BrassEnsembleNkr = @"C:\temp\source\Brass Ensemble\Samples\BP Brass Ensemble.nkr";
    private const string BrassEnsembleRefNcw = @"C:\temp\test_v11_extract\Samples\FSSoloBrass_FrenchHorn_CLOSEStacRR1_1_41_A#1.ncw";
    private const string ThePulseProcessedNkx = @"C:\Dev\AI-Workspaces\REA\innkx\Kontakt_files\The Pulse_1_processed.nkx";
    private const string PlainModernVersionNkr = @"G:\Libs\All Saints Choir LiTE\Samples\SC All Saints Choir.nkr";
    private const string EmbertoneMonolithNkx = @"G:\Libs\Embertone - Joshua Bell Violin\Samples\Contour.nkx";
    private const string LegacyEncryptedNks = @"D:\Apps\Musique\Librairies\EWQLSO Gold Edition\EWQLSO Gold Library\EWQLSO_Gold_lib_part8.nks";

    [Fact]
    public void TestOpenThePulseProcessedNkx()
    {
        if (!File.Exists(ThePulseProcessedNkx)) return;

        using var archive = NkxArchive.Open(ThePulseProcessedNkx);
        Assert.Equal(ArchiveType.Nkx, archive.ArchiveType);
        Assert.Equal(378, archive.Entries.Count(e => !e.IsDirectory));
        Assert.Contains(archive.Entries, e => e.RelativePath == @"Samples\SusAirySynthf_C3.ncw");

        var entry = archive.Entries.First(e => !e.IsDirectory);
        using var stream = archive.OpenEntryStream(entry);
        byte[] magic = new byte[8];
        int read = stream.Read(magic, 0, 8);
        Assert.Equal(8, read);
        Assert.Equal((byte)0x01, magic[0]);
        Assert.Equal((byte)0xa8, magic[1]);
        Assert.Equal((byte)0x9e, magic[2]);
        Assert.Equal((byte)0xd6, magic[3]);
        Assert.Equal((byte)0x31, magic[4]);
        Assert.Equal((byte)0x01, magic[5]);
    }

    [Fact]
    public void TestMonolithContainerWithProprietaryPrologue()
    {
        // Embertone monoliths start with a proprietary sample header (0x7FA89012) instead of the
        // standard root header: the root directory chunk sits at offset 222 and the records hold
        // absolute chunk offsets, regular files being stored with an unmasked offset.
        if (!File.Exists(EmbertoneMonolithNkx)) return;

        using var archive = NkxArchive.Open(EmbertoneMonolithNkx);

        var entries = archive.Entries.Where(e => !e.IsDirectory).ToList();
        Assert.Equal(121, entries.Count);
        Assert.Contains(entries, e => e.RelativePath == "Contour.nki");
        Assert.Contains(entries, e => e.RelativePath == @"Samples\JBell_CLOSE.CrescLong_A2_MP-F_rr1.ncw");

        // The patch is stored as a regular (unmasked) file chunk right before the trailing block.
        var patch = entries.First(e => e.RelativePath == "Contour.nki");
        Assert.Equal(5629, patch.Size);

        using var patchStream = archive.OpenEntryStream(patch);
        byte[] patchHead = new byte[4];
        Assert.Equal(4, patchStream.Read(patchHead, 0, 4));
        Assert.Equal(new byte[] { 0x12, 0x90, 0xa8, 0x7f }, patchHead);

        // Samples are stored in clear: the NCW header is copied verbatim.
        var sample = entries.First(e => e.RelativePath.EndsWith(".ncw", StringComparison.OrdinalIgnoreCase));
        Assert.True(sample.Size > 1024);
        using var sampleStream = archive.OpenEntryStream(sample);
        byte[] sampleHead = new byte[4];
        Assert.Equal(4, sampleStream.Read(sampleHead, 0, 4));
        Assert.Equal(new byte[] { 0x01, 0xa8, 0x9e, 0xd6 }, sampleHead);
    }

    [Fact]
    public void TestLegacyContainerWithStaticKey()
    {
        // Kontakt 1 monolith: fixed-size Latin1 directory records, samples protected with static key 9.
        if (!File.Exists(LegacyEncryptedNks)) return;

        using var archive = NkxArchive.Open(LegacyEncryptedNks);

        var entries = archive.Entries.Where(e => !e.IsDirectory).ToList();
        Assert.Equal(811, entries.Count);

        var sample = entries.First(e => e.RelativePath == @"CBScrF F\CBScrF24 F.wav");
        Assert.True(sample.IsEncrypted);
        Assert.Equal(9, sample.KeyIndex);

        using var stream = archive.OpenEntryStream(sample);
        byte[] head = new byte[16];
        Assert.Equal(16, stream.Read(head, 0, 16));
        Assert.Equal(new byte[] { 0x52, 0x49, 0x46, 0x46 }, head[..4]);  // "RIFF"
        Assert.Equal(new byte[] { 0x57, 0x41, 0x56, 0x45 }, head[8..12]); // "WAVE"
    }

    [Fact]
    public void TestLegacyDbAutoMigrationAndOnlyNklibsDbGenerated()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "NKXTool_DbTest_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            string legacyPath = Path.Combine(tempDir, Cryptography.NkxKeyDatabase.LegacyDbFileName);
            string modernPath = Path.Combine(tempDir, Cryptography.NkxKeyDatabase.DefaultDbFileName);

            // 1. Create a legacy database file
            File.WriteAllText(legacyPath, "[TST]\nJDX=0102030405060708091011121314151617181920212223242526272829303132\nHU=01020304050607080910111213141516\nRegKey=TestLib\nCompany=TestCo\n");

            Assert.True(File.Exists(legacyPath));
            Assert.False(File.Exists(modernPath));

            // 2. Load with base directory pointing to tempDir -> should migrate legacy to modern
            var db = Cryptography.NkxKeyDatabase.Load(null, tempDir);
            Assert.Equal(1, db.Count);
            Assert.True(db.TryGetKey("TST", out var entry));
            Assert.NotNull(entry);
            Assert.Equal("TestLib", entry.RegKey);

            // Modern nklibs.db should now exist
            Assert.True(File.Exists(modernPath));

            // 3. Delete legacy file, load again from modern
            File.Delete(legacyPath);
            var db2 = Cryptography.NkxKeyDatabase.Load(null, tempDir);
            Assert.True(db2.TryGetKey("TST", out var entry2));
            Assert.NotNull(entry2);

            // Legacy file must NOT be recreated
            Assert.False(File.Exists(legacyPath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TestOpenEntryStreamEncrypted()
    {
        if (!File.Exists(BrassEnsembleNkx)) return;

        using var archive = NkxArchive.Open(BrassEnsembleNkx);
        var entry = archive.Entries.First(e => !e.IsDirectory);

        using var stream = archive.OpenEntryStream(entry);
        byte[] magic = new byte[8];
        int read = stream.Read(magic, 0, 8);

        Assert.Equal(8, read);
        Assert.Equal((byte)0x01, magic[0]);
        Assert.Equal((byte)0xa8, magic[1]);
        Assert.Equal((byte)0x9e, magic[2]);
        Assert.Equal((byte)0xd6, magic[3]);
        Assert.Equal((byte)0x31, magic[4]);
        Assert.Equal((byte)0x01, magic[5]);
        Assert.Equal((byte)0x00, magic[6]);
        Assert.Equal((byte)0x00, magic[7]);
    }

    [Fact]
    public void TestNkxKeystreamExactVector()
    {
        byte[] jdx = Convert.FromHexString("844555DAC4952999298662712FE2FF0A3711CB8299D006C432BAEAD914F4D0E6");
        byte[] hu = Convert.FromHexString("1EE59C8EC4BCEA9BA917F4091B835DC4");

        byte[] keystream = Cryptography.NkxKeystream.CreateKeystream(jdx, hu);
        Assert.Equal(0x10000, keystream.Length);

        byte[] expectedFirst16 = Convert.FromHexString("55FA99727A37D4362B32019F11D3D5A7");
        Assert.Equal(expectedFirst16, keystream.AsSpan(0, 16).ToArray());

        byte[] ciphertext = Convert.FromHexString("545207A44B36D4362A32199F9168D5A7");
        byte[] decrypted = new byte[16];
        ciphertext.CopyTo(decrypted, 0);

        Cryptography.NkxKeystream.ApplyKeystream(keystream, 0, decrypted);

        // Expected Kontakt NCW v1 header: magic 01 A8 9E D6 31 01 00 00, mono 24-bit 48kHz
        byte[] expectedNcwHdr = Convert.FromHexString("01A89ED6310100000100180080BB0000");
        Assert.Equal(expectedNcwHdr, decrypted);
    }

    [Fact]
    public async Task TestExtractEncryptedBrassEnsembleNkxSingle()
    {
        if (!File.Exists(BrassEnsembleNkx)) return;

        string outDir = Path.Combine(Path.GetTempPath(), "NKXTool_Test_BrassNkx_" + Guid.NewGuid().ToString("N"));
        try
        {
            using var archive = NkxArchive.Open(BrassEnsembleNkx);
            Assert.True(archive.Entries.Count > 0);

            const string sampleName = @"Samples\FSSoloBrass_FrenchHorn_CLOSEStacRR1_1_41_A#1.ncw";
            var result = await archive.ExtractAllAsync(new ExtractionOptions
            {
                DestinationDirectory = outDir,
                SelectedFiles = new HashSet<string> { sampleName },
                Overwrite = true
            });

            Assert.True(result.IsSuccess);
            Assert.Equal(1, result.ExtractedCount);

            string extractedFile = Path.Combine(outDir, sampleName);
            Assert.True(File.Exists(extractedFile));

            byte[] extractedBytes = File.ReadAllBytes(extractedFile);
            Assert.True(extractedBytes.Length > 16);

            // Verify valid NCW magic
            Assert.Equal((byte)0x01, extractedBytes[0]);
            Assert.Equal((byte)0xa8, extractedBytes[1]);
            Assert.Equal((byte)0x9e, extractedBytes[2]);
            Assert.Equal((byte)0xd6, extractedBytes[3]);

            // Compare bit-for-bit against reference extraction if available
            if (File.Exists(BrassEnsembleRefNcw))
            {
                byte[] refBytes = File.ReadAllBytes(BrassEnsembleRefNcw);
                Assert.Equal(refBytes.Length, extractedBytes.Length);
                Assert.Equal(refBytes, extractedBytes);
            }
        }
        finally
        {
            if (Directory.Exists(outDir))
                Directory.Delete(outDir, recursive: true);
        }
    }

    [Fact]
    public void TestModernVersionContainerWithoutKeyIdIsNotEncrypted()
    {
        if (!File.Exists(PlainModernVersionNkr)) return;

        using var opened = NkxArchive.Open(PlainModernVersionNkr);
        var archive = Assert.IsType<Containers.Nkx.NkxArchiveReader>(opened);
        Assert.Equal(ArchiveType.Nkr, archive.ArchiveType);
        Assert.False(archive.IsEncrypted);

        var textEntry = archive.Entries.First(e => !e.IsDirectory && e.RelativePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
        using var stream = archive.OpenEntryStream(textEntry);
        using var textReader = new StreamReader(stream, Encoding.UTF8);
        string text = textReader.ReadToEnd();
        Assert.StartsWith("Has Alpha Channel:", text);
    }

    [Fact]
    public async Task TestExtractEncryptedNkrResource()
    {
        if (!File.Exists(BrassEnsembleNkr)) return;

        string outDir = Path.Combine(Path.GetTempPath(), "NKXTool_Test_BrassNkr_" + Guid.NewGuid().ToString("N"));
        try
        {
            using var archive = NkxArchive.Open(BrassEnsembleNkr);
            Assert.True(archive.Entries.Count > 0);

            // Extract first entry (resource txt)
            var firstEntry = archive.Entries.First(e => !e.IsDirectory);
            var result = await archive.ExtractAllAsync(new ExtractionOptions
            {
                DestinationDirectory = outDir,
                SelectedFiles = new HashSet<string> { firstEntry.RelativePath },
                Overwrite = true
            });

            Assert.True(result.IsSuccess);
            Assert.Equal(1, result.ExtractedCount);

            string extractedFile = Path.Combine(outDir, firstEntry.RelativePath);
            Assert.True(File.Exists(extractedFile));

            string text = File.ReadAllText(extractedFile);
            Assert.StartsWith("Has Alpha Channel:", text);
        }
        finally
        {
            if (Directory.Exists(outDir))
                Directory.Delete(outDir, recursive: true);
        }
    }

    [Fact]
    public void TestOpenEwqlsoNks()
    {
        if (!File.Exists(EwqlsoNks)) return;
        using var archive = NkxArchive.Open(EwqlsoNks);
        Assert.Equal(ArchiveType.Nks, archive.ArchiveType);
        Assert.True(archive.Entries.Count > 0);
        Assert.Contains(archive.Entries, e => e.RelativePath.Contains("CBScrF"));
    }

    [Fact]
    public void TestOpenPercussionNkx()
    {
        Assert.True(File.Exists(PercussionNkx));
        using var archive = NkxArchive.Open(PercussionNkx);
        Assert.Equal(ArchiveType.Nkx, archive.ArchiveType);
        Assert.Equal(148, archive.Entries.Count(e => !e.IsDirectory));
        Assert.Contains(archive.Entries, e => e.RelativePath.Contains("Clap"));
    }

    [Fact]
    public void TestOpenBawuNkr()
    {
        Assert.True(File.Exists(BawuNkr));
        using var archive = NkxArchive.Open(BawuNkr);
        Assert.Equal(ArchiveType.Nkr, archive.ArchiveType);
        Assert.True(archive.Entries.Count > 0);
        Assert.Contains(archive.Entries, e => e.RelativePath.Contains("bawu"));
    }

    [Fact]
    public void TestOpenBrassEnsembleNicnt()
    {
        Assert.True(File.Exists(BrassEnsembleNicnt));
        using var archive = NkxArchive.Open(BrassEnsembleNicnt);
        Assert.Equal(ArchiveType.Nicnt, archive.ArchiveType);
        Assert.Equal(19, archive.Entries.Count);
        Assert.Contains(archive.Entries, e => e.RelativePath == "ContentVersion.txt");
        Assert.Contains(archive.Entries, e => e.RelativePath == "Brass Ensemble.xml");
        Assert.Contains(archive.Entries, e => e.RelativePath == @"Resources\.LibBrowser.png");
    }

    [Fact]
    public void TestOpenEthnicWindsNicnt()
    {
        Assert.True(File.Exists(EthnicWindsNicnt));
        using var archive = NkxArchive.Open(EthnicWindsNicnt);
        Assert.Equal(ArchiveType.Nicnt, archive.ArchiveType);
        Assert.Equal(3, archive.Entries.Count);
        Assert.Contains(archive.Entries, e => e.RelativePath == "ContentVersion.txt");
        Assert.Contains(archive.Entries, e => e.RelativePath == "Ethnic Winds.xml");
        Assert.Contains(archive.Entries, e => e.RelativePath == @"Resources\.LibBrowser.png");
    }

    [Fact]
    public async Task TestExtractPercussionNkx()
    {
        string outDir = Path.Combine(Path.GetTempPath(), "NKXTool_Test_Percussion_" + Guid.NewGuid().ToString("N"));
        try
        {
            using var archive = NkxArchive.Open(PercussionNkx);
            var result = await archive.ExtractAllAsync(new ExtractionOptions
            {
                DestinationDirectory = outDir,
                Overwrite = true
            });

            Assert.True(result.IsSuccess);
            Assert.Equal(148, result.ExtractedCount);
            Assert.Empty(result.Errors);

            var files = Directory.GetFiles(outDir, "*.ncw", SearchOption.AllDirectories);
            Assert.Equal(148, files.Length);
        }
        finally
        {
            if (Directory.Exists(outDir))
                Directory.Delete(outDir, recursive: true);
        }
    }

    [Fact]
    public async Task TestExtractBrassEnsembleNicnt()
    {
        string outDir = Path.Combine(Path.GetTempPath(), "NKXTool_Test_Nicnt_" + Guid.NewGuid().ToString("N"));
        try
        {
            using var archive = NkxArchive.Open(BrassEnsembleNicnt);
            var result = await archive.ExtractAllAsync(new ExtractionOptions
            {
                DestinationDirectory = outDir,
                Overwrite = true
            });

            Assert.True(result.IsSuccess);
            Assert.Equal(19, result.ExtractedCount);
            Assert.Empty(result.Errors);

            string xmlPath = Path.Combine(outDir, "Brass Ensemble.xml");
            Assert.True(File.Exists(xmlPath));
            string xml = File.ReadAllText(xmlPath);
            Assert.Contains("<Name>Brass Ensemble</Name>", xml);
        }
        finally
        {
            if (Directory.Exists(outDir))
                Directory.Delete(outDir, recursive: true);
        }
    }

    [Fact]
    public async Task TestExtractBawuNkr()
    {
        string outDir = Path.Combine(Path.GetTempPath(), "NKXTool_Test_Bawu_" + Guid.NewGuid().ToString("N"));
        try
        {
            using var archive = NkxArchive.Open(BawuNkr);
            var result = await archive.ExtractAllAsync(new ExtractionOptions
            {
                DestinationDirectory = outDir,
                Overwrite = true
            });

            Assert.True(result.IsSuccess);
            Assert.Equal(55, result.ExtractedCount);
            Assert.Empty(result.Errors);

            // Verify both PNG and WAV files exist
            var pngFiles = Directory.GetFiles(outDir, "*.PNG", SearchOption.AllDirectories);
            var wavFiles = Directory.GetFiles(outDir, "*.wav", SearchOption.AllDirectories);
            Assert.True(pngFiles.Length > 0);
            Assert.Single(wavFiles);
        }
        finally
        {
            if (Directory.Exists(outDir))
                Directory.Delete(outDir, recursive: true);
        }
    }

    [Fact]
    public async Task TestPackAndExtractRoundtrip()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "NKXTool_PackTest_" + Guid.NewGuid().ToString("N"));
        string srcDir = Path.Combine(tempDir, "src");
        string outNkx = Path.Combine(tempDir, "packed.nkx");
        string unpackDir = Path.Combine(tempDir, "unpacked");

        try
        {
            Directory.CreateDirectory(srcDir);
            string file1 = Path.Combine(srcDir, "test1.txt");
            string file2 = Path.Combine(srcDir, "sub", "test2.bin");
            Directory.CreateDirectory(Path.GetDirectoryName(file2)!);

            byte[] b1 = Encoding.UTF8.GetBytes("Hello World from Native NKXTool!");
            byte[] b2 = new byte[100_000];
            Random.Shared.NextBytes(b2);

            File.WriteAllBytes(file1, b1);
            File.WriteAllBytes(file2, b2);

            // Pack
            var packResult = await Containers.Nkx.NkxArchiveWriter.PackAsync(new Containers.Nkx.NkxArchiveWriter.PackOptions
            {
                OutputFilePath = outNkx,
                SourcePathOrFileList = srcDir
            });

            Assert.True(packResult.IsSuccess);
            Assert.True(File.Exists(outNkx));

            // Extract with Reader
            using var reader = NkxArchive.Open(outNkx);
            var extractResult = await reader.ExtractAllAsync(new ExtractionOptions
            {
                DestinationDirectory = unpackDir
            });

            Assert.True(extractResult.IsSuccess);
            Assert.Equal(2, extractResult.ExtractedCount);

            // Verify content equality
            string unpackedFile1 = Path.Combine(unpackDir, "Samples", "test1.txt");
            string unpackedFile2 = Path.Combine(unpackDir, "Samples", "sub", "test2.bin");

            Assert.True(File.Exists(unpackedFile1));
            Assert.True(File.Exists(unpackedFile2));

            Assert.Equal(b1, File.ReadAllBytes(unpackedFile1));
            Assert.Equal(b2, File.ReadAllBytes(unpackedFile2));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}

namespace NKXTool.Core.Containers.Nicnt;

using System.Diagnostics;
using System.Text;
using System.Xml;
using NKXTool.Core.Common;

/// <summary>
/// High-performance native reader and extractor for Native Instruments NICNT monolith containers.
/// </summary>
public sealed class NicntArchiveReader : IArchiveReader
{
    private const string NicntMtdMagic = "/\\ NI FC MTD  /\\";
    private const string NicntTocMagic = "/\\ NI FC TOC  /\\";
    private const int TocRecordSize = 0x280; // 640 bytes per entry
    private const int ResourcePayloadHeaderOffset = 0x260;

    private readonly string _filePath;
    private readonly List<ArchiveEntry> _entries = new();
    private FileStream? _fileStream;

    public ArchiveType ArchiveType => ArchiveType.Nicnt;
    public string FilePath => _filePath;
    public IReadOnlyList<ArchiveEntry> Entries => _entries;

    public NicntArchiveReader(string filePath)
    {
        _filePath = Path.GetFullPath(filePath);
        if (!File.Exists(_filePath))
            throw new FileNotFoundException($"NICNT container not found: {_filePath}", _filePath);

        ParseContainer();
    }

    private void ParseContainer()
    {
        using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024);
        var fileSize = stream.Length;

        // 1. Verify header
        byte[] mtdBuf = new byte[16];
        int read = stream.Read(mtdBuf, 0, 16);
        string magic = Encoding.ASCII.GetString(mtdBuf, 0, read);
        if (!magic.StartsWith("/\\ NI FC MTD"))
            throw new InvalidDataException("Invalid NICNT archive: missing NI FC MTD header magic.");

        // 2. Read ContentVersion string at 0x42
        stream.Seek(0x42, SeekOrigin.Begin);
        byte[] verBytes = new byte[64];
        stream.ReadAtLeast(verBytes, Math.Min(64, (int)(fileSize - 0x42)), throwOnEndOfStream: false);
        string versionStr = ReadNullTerminatedUtf16(verBytes);
        int versionByteLen = (versionStr.Length + 1) * 2;
        if (string.IsNullOrWhiteSpace(versionStr))
            versionStr = "1.0.0";

        // 3. Read Product XML at offset 0x100
        stream.Seek(0x100, SeekOrigin.Begin);
        byte[] xmlProbe = new byte[64 * 1024];
        int xmlRead = stream.Read(xmlProbe, 0, xmlProbe.Length);
        string xmlText = Encoding.UTF8.GetString(xmlProbe, 0, xmlRead);
        
        string productName = "Product";
        int xmlLength = 0;
        int endTagPos = xmlText.IndexOf("</ProductHints>", StringComparison.OrdinalIgnoreCase);
        if (endTagPos > 0)
        {
            endTagPos += "</ProductHints>".Length;
            xmlLength = endTagPos;
            var fullXml = xmlText.Substring(0, endTagPos);
            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(fullXml);
                var nameNode = doc.SelectSingleNode("//Product/Name");
                if (nameNode != null && !string.IsNullOrWhiteSpace(nameNode.InnerText))
                    productName = nameNode.InnerText.Trim();
            }
            catch
            {
                // Fallback to filename without extension
                productName = Path.GetFileNameWithoutExtension(_filePath);
            }
        }
        else
        {
            xmlLength = 4096;
            productName = Path.GetFileNameWithoutExtension(_filePath);
        }

        int entryId = 1;

        // Entry: ContentVersion.txt
        _entries.Add(new ArchiveEntry
        {
            Id = entryId++,
            RelativePath = "ContentVersion.txt",
            Size = versionByteLen,
            PackedSize = versionByteLen,
            DataOffset = 0x42,
            IsDirectory = false,
            IsEncrypted = false,
            RawType = 2
        });

        // Entry: <ProductName>.xml
        _entries.Add(new ArchiveEntry
        {
            Id = entryId++,
            RelativePath = $"{productName}.xml",
            Size = xmlLength,
            PackedSize = xmlLength,
            DataOffset = 0x100,
            IsDirectory = false,
            IsEncrypted = false,
            RawType = 2
        });

        // 4. Locate TOC headers in file
        // Search TOC headers by scanning from 0x70000 onwards
        long toc1Offset = -1;
        long toc2Offset = -1;

        byte[] searchBuf = new byte[1024 * 1024];
        stream.Seek(0, SeekOrigin.Begin);
        int totalRead = stream.Read(searchBuf, 0, searchBuf.Length);
        byte[] tocPattern = Encoding.ASCII.GetBytes(NicntTocMagic);

        int pos = 0;
        while (pos <= totalRead - 16)
        {
            if (searchBuf.AsSpan(pos, 16).SequenceEqual(tocPattern))
            {
                if (toc1Offset == -1)
                    toc1Offset = pos;
                else if (toc2Offset == -1)
                {
                    toc2Offset = pos;
                    break;
                }
            }
            pos += 4;
        }

        if (toc1Offset != -1 && toc2Offset != -1)
        {
            long payloadBaseOffset = toc2Offset + ResourcePayloadHeaderOffset;
            long currentRecordOffset = toc1Offset + 0x268; // first record starts at +0x268
            long currentStartOffset = 0;

            while (currentRecordOffset + TocRecordSize <= toc2Offset + 16)
            {
                stream.Seek(currentRecordOffset, SeekOrigin.Begin);
                byte[] recBuf = new byte[TocRecordSize];
                int recRead = stream.Read(recBuf, 0, TocRecordSize);
                if (recRead < TocRecordSize) break;

                // Stop if end-of-records marker (0xF1) is reached
                if (recBuf[0] == 0xF1) break;

                long recId = BitConverter.ToInt64(recBuf, 0);
                string rawName = ReadNullTerminatedUtf16(recBuf.AsSpan(24));
                long endOffset = BitConverter.ToInt64(recBuf, TocRecordSize - 8);

                if (!string.IsNullOrWhiteSpace(rawName))
                {
                    // Translate '|' to '[pipe]'
                    string safeName = rawName.Replace("|", "[pipe]");
                    string archiveRelPath = Path.Combine("Resources", safeName);

                    long itemStart = currentStartOffset;
                    long itemSize;
                    if (endOffset > currentStartOffset)
                    {
                        itemSize = endOffset - currentStartOffset;
                        currentStartOffset = endOffset;
                    }
                    else
                    {
                        itemSize = 0;
                    }

                    _entries.Add(new ArchiveEntry
                    {
                        Id = entryId++,
                        RelativePath = archiveRelPath,
                        Size = itemSize,
                        PackedSize = itemSize,
                        DataOffset = payloadBaseOffset + itemStart,
                        IsDirectory = false,
                        IsEncrypted = false,
                        RawType = 2
                    });
                }

                currentRecordOffset += TocRecordSize;
            }
        }
    }

    private static string ReadNullTerminatedUtf16(ReadOnlySpan<byte> bytes)
    {
        int nullIdx = -1;
        for (int i = 0; i < bytes.Length - 1; i += 2)
        {
            if (bytes[i] == 0 && bytes[i + 1] == 0)
            {
                nullIdx = i;
                break;
            }
        }

        if (nullIdx == 0) return string.Empty;
        int length = nullIdx > 0 ? nullIdx : bytes.Length;
        return Encoding.Unicode.GetString(bytes[..length]).Trim();
    }

    public Stream OpenEntryStream(ArchiveEntry entry)
    {
        _fileStream ??= new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024);
        return new SubReadStream(_fileStream, entry.DataOffset, entry.Size);
    }

    public async Task<ExtractionResult> ExtractAllAsync(
        ExtractionOptions options,
        IProgress<ExtractionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var errors = new List<ExtractionError>();
        int extracted = 0;
        int skipped = 0;

        Directory.CreateDirectory(options.DestinationDirectory);

        var targetEntries = _entries.Where(e => !e.IsDirectory).ToList();
        if (options.SelectedFiles != null)
        {
            targetEntries = targetEntries
                .Where(e => options.SelectedFiles.Contains(e.RelativePath))
                .ToList();
        }

        int totalCount = targetEntries.Count;
        long totalBytes = targetEntries.Sum(e => e.Size);
        int processedCount = 0;
        long processedBytes = 0;

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, options.MaxDegreeOfParallelism),
            CancellationToken = cancellationToken
        };

        // Thread-safe extraction across files
        await Parallel.ForEachAsync(targetEntries, parallelOptions, async (entry, ct) =>
        {
            string outPath = Path.Combine(options.DestinationDirectory, entry.RelativePath);
            string? outDir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(outDir))
                Directory.CreateDirectory(outDir);

            if (!options.Overwrite && File.Exists(outPath))
            {
                Interlocked.Increment(ref skipped);
                Interlocked.Increment(ref processedCount);
                Interlocked.Add(ref processedBytes, entry.Size);
                progress?.Report(new ExtractionProgress
                {
                    TotalFiles = totalCount,
                    ProcessedFiles = processedCount,
                    SuccessFiles = extracted,
                    SkippedFiles = skipped,
                    FailedFiles = errors.Count,
                    TotalBytes = totalBytes,
                    ProcessedBytes = processedBytes,
                    CurrentFile = entry.RelativePath
                });
                return;
            }

            try
            {
                // Open independent file stream per thread to maximize parallel I/O throughput
                using var sourceStream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read, options.StreamBufferSize);
                sourceStream.Seek(entry.DataOffset, SeekOrigin.Begin);

                using var subStream = new SubReadStream(sourceStream, entry.DataOffset, entry.Size);
                using var destStream = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None, options.StreamBufferSize, FileOptions.Asynchronous);

                await subStream.CopyToAsync(destStream, options.StreamBufferSize, ct).ConfigureAwait(false);

                Interlocked.Increment(ref extracted);
            }
            catch (Exception ex)
            {
                lock (errors)
                {
                    errors.Add(new ExtractionError
                    {
                        FileId = entry.Id,
                        ArchivePath = entry.RelativePath,
                        DestinationPath = outPath,
                        DataOffset = entry.DataOffset,
                        ExpectedSize = entry.Size,
                        ErrorMessage = $"Failed to extract file '{entry.RelativePath}': {ex.Message}",
                        Exception = ex
                    });
                }
            }
            finally
            {
                Interlocked.Increment(ref processedCount);
                Interlocked.Add(ref processedBytes, entry.Size);

                progress?.Report(new ExtractionProgress
                {
                    TotalFiles = totalCount,
                    ProcessedFiles = processedCount,
                    SuccessFiles = extracted,
                    SkippedFiles = skipped,
                    FailedFiles = errors.Count,
                    TotalBytes = totalBytes,
                    ProcessedBytes = processedBytes,
                    CurrentFile = entry.RelativePath
                });
            }
        }).ConfigureAwait(false);

        sw.Stop();

        return new ExtractionResult
        {
            TotalCount = totalCount,
            ExtractedCount = extracted,
            SkippedCount = skipped,
            Elapsed = sw.Elapsed,
            Errors = errors
        };
    }

    public void Dispose()
    {
        _fileStream?.Dispose();
        _fileStream = null;
    }
}

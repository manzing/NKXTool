namespace NKXTool.Core.Containers.Nkx;

using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using NKXTool.Core.Common;
using NKXTool.Core.Cryptography;

/// <summary>
/// High-performance native reader and extractor for Kontakt containers (.nkx, .nkr, .nks).
/// Supports modern containers (0x0110/0x0401) with recursive directory chunk trees and legacy NKS (0x0100).
/// </summary>
public sealed class NkxArchiveReader : IArchiveReader
{
    public const uint ChunkContainerMagic = 0x5e70ac54; // Big chunk: root and directory tree
    public const uint ChunkAudioDataMagic = 0x16ccf80a; // Audio sample data
    public const uint ChunkResourceDataMagic = 0x2ae905fa; // Pictures, fonts, text resources
    public const uint ChunkScriptDataMagic = 0x4916e63c; // Script data

    public const uint VersionModern = 0x0110;
    public const uint VersionLegacy = 0x0100;

    private readonly string _filePath;
    private readonly ArchiveType _archiveType;
    private readonly List<ArchiveEntry> _entries = new();
    private readonly NkxKeyDatabase _keyDatabase;
    private FileStream? _fileStream;
    private int _nextEntryId = 1;

    public ArchiveType ArchiveType => _archiveType;
    public string FilePath => _filePath;
    public IReadOnlyList<ArchiveEntry> Entries => _entries;

    public NkxArchiveReader(string filePath, NkxKeyDatabase? keyDatabase = null)
    {
        _filePath = Path.GetFullPath(filePath);
        if (!File.Exists(_filePath))
            throw new FileNotFoundException($"Archive container not found: {_filePath}", _filePath);

        _keyDatabase = keyDatabase ?? NkxKeyDatabase.Load();

        string ext = Path.GetExtension(_filePath).ToLowerInvariant();
        _archiveType = ext switch
        {
            ".nkr" => ArchiveType.Nkr,
            ".nks" => ArchiveType.Nks,
            _ => ArchiveType.Nkx
        };

        ParseContainer();
    }

    private void ParseContainer()
    {
        using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024);
        var fileSize = stream.Length;
        if (fileSize < 32)
            throw new InvalidDataException("Archive container file is too small.");

        byte[] hdr = new byte[32];
        stream.ReadExactly(hdr);

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(hdr.AsSpan(0, 4));
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(hdr.AsSpan(4, 4));

        if (magic != ChunkContainerMagic)
            throw new InvalidDataException($"Unsupported archive magic: 0x{magic:X8} (expected 0x{ChunkContainerMagic:X8}).");

        if (version == VersionModern || version == 0x0401)
        {
            ParseModernContainer(stream);
        }
        else if (version == VersionLegacy)
        {
            ParseLegacyContainer(stream);
        }
        else
        {
            ParseModernContainer(stream);
        }
    }

    private void ParseModernContainer(FileStream stream)
    {
        // 1. Root Chunk: read root directory name and pointer to first directory chunk
        stream.Seek(0, SeekOrigin.Begin);
        byte[] rootHdr = new byte[64];
        stream.ReadExactly(rootHdr);

        uint dirChunkOffset = BinaryPrimitives.ReadUInt32LittleEndian(rootHdr.AsSpan(0x18, 4));
        int rootNameByteLen = (int)dirChunkOffset - 0x1e;
        string rootName = string.Empty;
        if (rootNameByteLen > 0 && rootNameByteLen < 64)
        {
            rootName = Encoding.Unicode.GetString(rootHdr.AsSpan(0x1e, rootNameByteLen)).TrimEnd('\0');
        }

        ParseDirectoryRecursive(stream, dirChunkOffset, rootName);
    }

    private long ParseDirectoryRecursive(FileStream stream, long chunkOffset, string currentPath)
    {
        if (chunkOffset >= stream.Length) return chunkOffset;

        stream.Seek(chunkOffset, SeekOrigin.Begin);
        byte[] dirHdr = new byte[22];
        int read = stream.Read(dirHdr, 0, 22);
        if (read < 22) return chunkOffset;

        uint dirMagic = BinaryPrimitives.ReadUInt32LittleEndian(dirHdr.AsSpan(0, 4));
        if (dirMagic != ChunkContainerMagic)
            return chunkOffset;

        ushort entryCount = BinaryPrimitives.ReadUInt16LittleEndian(dirHdr.AsSpan(14, 2));

        var dirRecords = new List<(uint id, ushort type, string name)>();

        for (int i = 0; i < entryCount; i++)
        {
            byte[] sizeBuf = new byte[2];
            stream.ReadExactly(sizeBuf);
            ushort recSize = BinaryPrimitives.ReadUInt16LittleEndian(sizeBuf);

            int dataLen = recSize - 2;
            if (dataLen < 6) break;

            byte[] recBuf = new byte[dataLen];
            stream.ReadExactly(recBuf);

            uint recId = BinaryPrimitives.ReadUInt32LittleEndian(recBuf.AsSpan(0, 4));
            ushort recType = BinaryPrimitives.ReadUInt16LittleEndian(recBuf.AsSpan(4, 2));
            string recName = Encoding.Unicode.GetString(recBuf.AsSpan(6)).TrimEnd('\0');

            dirRecords.Add((recId, recType, recName));
        }

        // Sequential data chunks for file entries in this directory
        long dataCur = stream.Position;
        var fileRecords = dirRecords.Where(r => r.type != 1).ToList();

        foreach (var fileRec in fileRecords)
        {
            stream.Seek(dataCur, SeekOrigin.Begin);
            byte[] chunkProbe = new byte[32];
            int probeRead = stream.Read(chunkProbe, 0, 32);
            if (probeRead < 22) break;

            uint chunkMagic = BinaryPrimitives.ReadUInt32LittleEndian(chunkProbe.AsSpan(0, 4));
            long itemSize = 0;
            int headerLen = 0;

            if (chunkMagic == ChunkResourceDataMagic) // 0x2ae905fa
            {
                itemSize = BinaryPrimitives.ReadUInt32LittleEndian(chunkProbe.AsSpan(14, 4));
                headerLen = 22;
            }
            else if (chunkMagic == ChunkAudioDataMagic) // 0x16ccf80a
            {
                itemSize = (long)BinaryPrimitives.ReadUInt64LittleEndian(chunkProbe.AsSpan(19, 8));
                headerLen = 31;
            }
            else if (chunkMagic == ChunkScriptDataMagic) // 0x4916e63c
            {
                itemSize = BinaryPrimitives.ReadUInt32LittleEndian(chunkProbe.AsSpan(14, 4));
                headerLen = 22;
            }
            else
            {
                // Unknown or unaligned chunk
                break;
            }

            string relativePath = string.IsNullOrEmpty(currentPath)
                ? fileRec.name
                : Path.Combine(currentPath, fileRec.name);

            _entries.Add(new ArchiveEntry
            {
                Id = _nextEntryId++,
                RelativePath = relativePath,
                Size = itemSize,
                PackedSize = itemSize,
                DataOffset = dataCur + headerLen,
                IsDirectory = false,
                IsEncrypted = false,
                RawType = fileRec.type
            });

            dataCur += headerLen + itemSize;
        }

        // Subdirectories
        var subdirs = dirRecords.Where(r => r.type == 1).ToList();
        foreach (var sub in subdirs)
        {
            string subPath = string.IsNullOrEmpty(currentPath)
                ? sub.name
                : Path.Combine(currentPath, sub.name);

            dataCur = ParseDirectoryRecursive(stream, dataCur, subPath);
        }

        return dataCur;
    }

    private void ParseLegacyContainer(FileStream stream)
    {
        // Version 0x0100 (NKS)
        stream.Seek(0, SeekOrigin.Begin);
        long streamLen = stream.Length;

        // Collect names from table
        byte[] probe = new byte[Math.Min(streamLen, 2 * 1024 * 1024)];
        stream.ReadExactly(probe);

        var nameEntries = new List<string>();
        int cur = 0x16;

        while (cur < probe.Length - 128)
        {
            if (probe[cur] == 0x54 && probe[cur + 1] == 0xac && probe[cur + 2] == 0x70 && probe[cur + 3] == 0x5e)
            {
                cur += 22;
                continue;
            }

            int end = Array.IndexOf(probe, (byte)0, cur);
            if (end > cur && end - cur < 120)
            {
                string name = Encoding.Latin1.GetString(probe, cur, end - cur);
                if (name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".ncw", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains(" "))
                {
                    nameEntries.Add(name);
                }
            }
            cur += 128;
        }

        // Scan sequential 0x16ccf80a data chunks
        stream.Seek(0, SeekOrigin.Begin);
        byte[] scanBuf = new byte[1024 * 1024];
        long offset = 0;
        int fileIdx = 0;

        while (offset < streamLen - 32)
        {
            stream.Seek(offset, SeekOrigin.Begin);
            int read = stream.Read(scanBuf, 0, scanBuf.Length);
            if (read < 32) break;

            int chunkPos = -1;
            for (int i = 0; i <= read - 4; i++)
            {
                if (scanBuf[i] == 0x0a && scanBuf[i + 1] == 0xf8 && scanBuf[i + 2] == 0xcc && scanBuf[i + 3] == 0x16)
                {
                    chunkPos = i;
                    break;
                }
            }

            if (chunkPos == -1)
            {
                offset += read - 4;
                continue;
            }

            long chunkFileOffset = offset + chunkPos;
            stream.Seek(chunkFileOffset, SeekOrigin.Begin);
            byte[] chunkHdr = new byte[31];
            stream.ReadExactly(chunkHdr);

            uint fileSize = BinaryPrimitives.ReadUInt32LittleEndian(chunkHdr.AsSpan(19, 4));
            long payloadOffset = chunkFileOffset + 31;

            string name = fileIdx < nameEntries.Count ? nameEntries[fileIdx] : $"Sample_{fileIdx + 1:D4}.wav";

            _entries.Add(new ArchiveEntry
            {
                Id = _nextEntryId++,
                RelativePath = name,
                Size = fileSize,
                PackedSize = fileSize,
                DataOffset = payloadOffset,
                IsDirectory = false,
                IsEncrypted = false,
                RawType = 2
            });

            fileIdx++;
            offset = payloadOffset + fileSize;
        }
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
                        ErrorMessage = $"Error extracting '{entry.RelativePath}': {ex.Message}",
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

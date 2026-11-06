namespace NKXTool.Core.Containers.Nkx;

using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using NKXTool.Core.Common;

/// <summary>
/// High-performance native generator and packer for Kontakt containers (.nkx).
/// Generates 100% standards-compliant modern Kontakt container chunks (0x5e70ac54, 0x16ccf80a).
/// Supports automatic multi-volume splitting at configurable size boundaries (default ~1.95 GB).
/// </summary>
public static class NkxArchiveWriter
{
    public const long DefaultMaxVolumeBytes = 2_090_000_000L; // ~1.95 GB

    /// <summary>
    /// Configuration options for packing files into an NKX archive.
    /// </summary>
    public sealed class PackOptions
    {
        public string OutputFilePath { get; set; } = string.Empty;
        public string SourcePathOrFileList { get; set; } = string.Empty;
        public string? RootDirectoryName { get; set; }
        public string? SourceBaseDirectory { get; set; }
        public long MaxVolumeSize { get; set; } = DefaultMaxVolumeBytes;
        public int StreamBufferSize { get; set; } = 1024 * 1024;
    }

    /// <summary>
    /// Result summary of a pack operation.
    /// </summary>
    public sealed class PackResult
    {
        public bool IsSuccess => VolumeFilePaths.Count > 0 && FailedFiles.Count == 0;
        public int TotalPackedFiles { get; init; }
        public long TotalBytesPacked { get; init; }
        public TimeSpan Elapsed { get; init; }
        public IReadOnlyList<string> VolumeFilePaths { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> FailedFiles { get; init; } = Array.Empty<string>();
    }

    public static async Task<PackResult> PackAsync(
        PackOptions options,
        IProgress<ExtractionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        string outputNkxFilePath = Path.GetFullPath(options.OutputFilePath);
        string ext = Path.GetExtension(outputNkxFilePath);
        if (string.IsNullOrWhiteSpace(ext))
            outputNkxFilePath += ".nkx";

        string? outDir = Path.GetDirectoryName(outputNkxFilePath);
        if (!string.IsNullOrEmpty(outDir))
            Directory.CreateDirectory(outDir);

        // 1. Resolve source files and base path
        var filesToPack = new List<string>();
        string srcBasePath;

        if (options.SourcePathOrFileList.StartsWith("@"))
        {
            string listFile = options.SourcePathOrFileList.Substring(1);
            if (!File.Exists(listFile))
                throw new FileNotFoundException($"Filelist file not found: {listFile}", listFile);

            srcBasePath = !string.IsNullOrEmpty(options.SourceBaseDirectory)
                ? Path.GetFullPath(options.SourceBaseDirectory)
                : Path.GetDirectoryName(Path.GetFullPath(listFile)) ?? "";

            foreach (var line in File.ReadAllLines(listFile))
            {
                var trimmed = line.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                    filesToPack.Add(trimmed);
            }
        }
        else
        {
            srcBasePath = Path.GetFullPath(options.SourcePathOrFileList);
            if (!Directory.Exists(srcBasePath))
                throw new DirectoryNotFoundException($"Source directory not found: {srcBasePath}");

            foreach (var f in Directory.GetFiles(srcBasePath, "*", SearchOption.AllDirectories))
            {
                filesToPack.Add(Path.GetRelativePath(srcBasePath, f));
            }
        }

        if (filesToPack.Count == 0)
            throw new InvalidOperationException("No files to pack.");

        // 2. Batch files into volumes
        var batches = new List<List<string>>();
        var currentBatch = new List<string>();
        long currentSize = 0;

        foreach (var file in filesToPack)
        {
            string fullPath = Path.Combine(srcBasePath, file);
            if (!File.Exists(fullPath)) continue;

            long len = new FileInfo(fullPath).Length;
            if (currentSize + len > options.MaxVolumeSize && currentBatch.Count > 0)
            {
                batches.Add(currentBatch);
                currentBatch = new List<string>();
                currentSize = 0;
            }
            currentBatch.Add(file);
            currentSize += len;
        }
        if (currentBatch.Count > 0)
            batches.Add(currentBatch);

        var volumePaths = new List<string>();
        var failedFiles = new List<string>();
        int totalPacked = 0;
        long totalBytesPacked = 0;

        // 3. Write each volume
        for (int b = 0; b < batches.Count; b++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string volumePath = outputNkxFilePath;
            if (batches.Count > 1)
            {
                string dir = Path.GetDirectoryName(outputNkxFilePath) ?? "";
                string nameWithoutExt = Path.GetFileNameWithoutExtension(outputNkxFilePath);
                string outExt = Path.GetExtension(outputNkxFilePath);
                volumePath = Path.Combine(dir, $"{nameWithoutExt}_{b:D2}{outExt}");
            }

            var batchFiles = batches[b];

            // Determine rootFolder name (e.g. "Samples" or "Resources")
            string rootFolder = "Samples";
            if (!string.IsNullOrWhiteSpace(options.RootDirectoryName))
            {
                rootFolder = options.RootDirectoryName;
            }
            else
            {
                string? commonPrefix = null;
                foreach (var f in batchFiles)
                {
                    int slashIdx = f.IndexOfAny(['\\', '/']);
                    if (slashIdx > 0)
                    {
                        string prefix = f.Substring(0, slashIdx);
                        if (commonPrefix == null)
                            commonPrefix = prefix;
                        else if (!string.Equals(commonPrefix, prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            commonPrefix = null;
                            break;
                        }
                    }
                    else
                    {
                        commonPrefix = null;
                        break;
                    }
                }
                if (!string.IsNullOrEmpty(commonPrefix))
                {
                    rootFolder = commonPrefix;
                }
            }

            using var outStream = new FileStream(volumePath, FileMode.Create, FileAccess.Write, FileShare.None, options.StreamBufferSize, FileOptions.Asynchronous);

            // 1. Root Chunk Header (Magic: 0x5e70ac54, Version: 0x0110)
            byte[] rootNameBytes = Encoding.Unicode.GetBytes(rootFolder + "\0");
            uint dirOffset = (uint)(0x1e + rootNameBytes.Length);
            ushort remainingHdrLen = (ushort)(dirOffset - 0x16);

            byte[] rootHdr = new byte[dirOffset];
            BinaryPrimitives.WriteUInt32LittleEndian(rootHdr.AsSpan(0, 4), NkxArchiveReader.ChunkContainerMagic);
            BinaryPrimitives.WriteUInt16LittleEndian(rootHdr.AsSpan(4, 2), NkxArchiveReader.VersionModern);
            BinaryPrimitives.WriteUInt16LittleEndian(rootHdr.AsSpan(6, 2), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(rootHdr.AsSpan(8, 4), 0x00ff0000);
            BinaryPrimitives.WriteUInt32LittleEndian(rootHdr.AsSpan(12, 4), 0x00010000);
            BinaryPrimitives.WriteUInt32LittleEndian(rootHdr.AsSpan(16, 4), 0x00010000);
            BinaryPrimitives.WriteUInt16LittleEndian(rootHdr.AsSpan(20, 2), 0);
            BinaryPrimitives.WriteUInt16LittleEndian(rootHdr.AsSpan(22, 2), remainingHdrLen);
            BinaryPrimitives.WriteUInt32LittleEndian(rootHdr.AsSpan(24, 4), dirOffset);
            BinaryPrimitives.WriteUInt16LittleEndian(rootHdr.AsSpan(28, 2), 1); // type = 1 (Directory)
            rootNameBytes.CopyTo(rootHdr.AsSpan(30));

            await outStream.WriteAsync(rootHdr, cancellationToken).ConfigureAwait(false);

            // Precompute file records and their lengths for directory records
            var fileItems = new List<(string relFile, string cleanName, byte[] nameBytes, ushort recLen, long fileLen)>();
            int totalDirRecordsBytes = 0;

            foreach (var relFile in batchFiles)
            {
                string sourceFileFullPath = Path.Combine(srcBasePath, relFile);
                if (!File.Exists(sourceFileFullPath))
                {
                    failedFiles.Add(relFile);
                    continue;
                }

                long fileLen = new FileInfo(sourceFileFullPath).Length;

                string cleanName = relFile;
                if (cleanName.StartsWith(rootFolder + "\\", StringComparison.OrdinalIgnoreCase) ||
                    cleanName.StartsWith(rootFolder + "/", StringComparison.OrdinalIgnoreCase))
                {
                    cleanName = cleanName.Substring(rootFolder.Length + 1);
                }

                cleanName = cleanName.Replace('/', '\\');

                byte[] nameUtf16 = Encoding.Unicode.GetBytes(cleanName + "\0");
                ushort recLen = (ushort)(2 + 4 + 2 + nameUtf16.Length);

                fileItems.Add((relFile, cleanName, nameUtf16, recLen, fileLen));
                totalDirRecordsBytes += recLen;
            }

            // 2. Directory Chunk Header (22 bytes)
            byte[] dirHdr = new byte[22];
            BinaryPrimitives.WriteUInt32LittleEndian(dirHdr.AsSpan(0, 4), NkxArchiveReader.ChunkContainerMagic);
            BinaryPrimitives.WriteUInt16LittleEndian(dirHdr.AsSpan(4, 2), NkxArchiveReader.VersionModern);
            BinaryPrimitives.WriteUInt16LittleEndian(dirHdr.AsSpan(6, 2), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(dirHdr.AsSpan(8, 4), 0x00ff0000);
            BinaryPrimitives.WriteUInt16LittleEndian(dirHdr.AsSpan(12, 2), 0);
            BinaryPrimitives.WriteUInt16LittleEndian(dirHdr.AsSpan(14, 2), (ushort)fileItems.Count);
            // Remaining 6 bytes at 16..21 are 0

            await outStream.WriteAsync(dirHdr, cancellationToken).ConfigureAwait(false);

            // 3. Directory Records
            // In Kontakt container format, file recId is the byte offset of its audio data chunk
            // XORed with FileRecordIdXorMask (0x1F4E0C8D).
            long runningChunkOffset = dirOffset + 22 + totalDirRecordsBytes;

            foreach (var item in fileItems)
            {
                uint recId = (uint)runningChunkOffset ^ NkxArchiveReader.FileRecordIdXorMask;

                byte[] recBuf = new byte[item.recLen];
                BinaryPrimitives.WriteUInt16LittleEndian(recBuf.AsSpan(0, 2), item.recLen);
                BinaryPrimitives.WriteUInt32LittleEndian(recBuf.AsSpan(2, 4), recId);
                BinaryPrimitives.WriteUInt16LittleEndian(recBuf.AsSpan(6, 2), 2); // file type = 2
                item.nameBytes.CopyTo(recBuf.AsSpan(8));

                await outStream.WriteAsync(recBuf, cancellationToken).ConfigureAwait(false);

                runningChunkOffset += 31 + item.fileLen;
            }

            // 4. Sequential File Data Chunks (Magic: 0x16ccf80a, 31 bytes header)
            foreach (var item in fileItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string sourceFileFullPath = Path.Combine(srcBasePath, item.relFile);

                try
                {
                    using var inStream = new FileStream(sourceFileFullPath, FileMode.Open, FileAccess.Read, FileShare.Read, options.StreamBufferSize);
                    long fileLen = inStream.Length;

                    byte[] chunkHdr = new byte[31];
                    BinaryPrimitives.WriteUInt32LittleEndian(chunkHdr.AsSpan(0, 4), NkxArchiveReader.ChunkAudioDataMagic);
                    BinaryPrimitives.WriteUInt16LittleEndian(chunkHdr.AsSpan(4, 2), NkxArchiveReader.VersionModern);
                    BinaryPrimitives.WriteUInt16LittleEndian(chunkHdr.AsSpan(6, 2), 0);
                    BinaryPrimitives.WriteUInt32LittleEndian(chunkHdr.AsSpan(8, 4), 0x00ff0000);
                    BinaryPrimitives.WriteUInt16LittleEndian(chunkHdr.AsSpan(12, 2), 0);
                    chunkHdr[14] = 0x03; // Chunk type 3 (Audio data)
                    // Offset 15..18 are 0
                    BinaryPrimitives.WriteUInt64LittleEndian(chunkHdr.AsSpan(19, 8), (ulong)fileLen);
                    // Offset 27..30 are 0

                    await outStream.WriteAsync(chunkHdr, cancellationToken).ConfigureAwait(false);
                    await inStream.CopyToAsync(outStream, options.StreamBufferSize, cancellationToken).ConfigureAwait(false);

                    totalPacked++;
                    totalBytesPacked += fileLen;
                }
                catch
                {
                    failedFiles.Add(item.relFile);
                }
            }

            volumePaths.Add(volumePath);
        }

        sw.Stop();

        return new PackResult
        {
            TotalPackedFiles = totalPacked,
            TotalBytesPacked = totalBytesPacked,
            Elapsed = sw.Elapsed,
            VolumeFilePaths = volumePaths,
            FailedFiles = failedFiles
        };
    }
}

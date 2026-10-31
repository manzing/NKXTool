namespace NKXTool.Core.Containers.Nkx;

using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using NKXTool.Core.Common;

/// <summary>
/// High-performance native generator and packer for Kontakt containers (.nkx).
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

        // 1. Resolve source files
        var filesToPack = new List<string>();
        string srcBasePath;

        if (options.SourcePathOrFileList.StartsWith("@"))
        {
            string listFile = options.SourcePathOrFileList.Substring(1);
            if (!File.Exists(listFile))
                throw new FileNotFoundException($"Filelist file not found: {listFile}", listFile);

            srcBasePath = string.IsNullOrEmpty(options.RootDirectoryName)
                ? Path.GetDirectoryName(Path.GetFullPath(listFile)) ?? ""
                : options.RootDirectoryName;

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
            string rootFolder = "Samples";

            using var outStream = new FileStream(volumePath, FileMode.Create, FileAccess.Write, FileShare.None, options.StreamBufferSize, FileOptions.Asynchronous);

            // 1. Root Chunk Header (Magic: 0x5e70ac54, Version: 0x0110)
            byte[] rootNameBytes = Encoding.Unicode.GetBytes(rootFolder + "\0");
            uint dirOffset = (uint)(0x1e + rootNameBytes.Length);

            byte[] rootHdr = new byte[dirOffset];
            BinaryPrimitives.WriteUInt32LittleEndian(rootHdr.AsSpan(0, 4), NkxArchiveReader.ChunkContainerMagic);
            BinaryPrimitives.WriteUInt32LittleEndian(rootHdr.AsSpan(4, 4), NkxArchiveReader.VersionModern);
            BinaryPrimitives.WriteUInt32LittleEndian(rootHdr.AsSpan(8, 4), 0x00ff0000);
            BinaryPrimitives.WriteUInt32LittleEndian(rootHdr.AsSpan(12, 4), 0x00010000);
            BinaryPrimitives.WriteUInt32LittleEndian(rootHdr.AsSpan(16, 4), 0x00000001);
            BinaryPrimitives.WriteUInt32LittleEndian(rootHdr.AsSpan(20, 4), 0x00000001);
            BinaryPrimitives.WriteUInt32LittleEndian(rootHdr.AsSpan(24, 4), dirOffset);
            BinaryPrimitives.WriteUInt16LittleEndian(rootHdr.AsSpan(28, 2), 1); // type directory
            rootNameBytes.CopyTo(rootHdr.AsSpan(30));

            await outStream.WriteAsync(rootHdr, cancellationToken).ConfigureAwait(false);

            // 2. Directory Chunk Header (22 bytes)
            byte[] dirHdr = new byte[22];
            BinaryPrimitives.WriteUInt32LittleEndian(dirHdr.AsSpan(0, 4), NkxArchiveReader.ChunkContainerMagic);
            BinaryPrimitives.WriteUInt32LittleEndian(dirHdr.AsSpan(4, 4), NkxArchiveReader.VersionModern);
            BinaryPrimitives.WriteUInt32LittleEndian(dirHdr.AsSpan(8, 4), 0x00ff0000);
            BinaryPrimitives.WriteUInt16LittleEndian(dirHdr.AsSpan(14, 2), (ushort)batchFiles.Count);

            await outStream.WriteAsync(dirHdr, cancellationToken).ConfigureAwait(false);

            // Directory Records
            uint fileIdCounter = 1;
            foreach (var relFile in batchFiles)
            {
                byte[] nameUtf16 = Encoding.Unicode.GetBytes(relFile + "\0");
                ushort recLen = (ushort)(2 + 4 + 2 + nameUtf16.Length);

                byte[] recBuf = new byte[recLen];
                BinaryPrimitives.WriteUInt16LittleEndian(recBuf.AsSpan(0, 2), recLen);
                BinaryPrimitives.WriteUInt32LittleEndian(recBuf.AsSpan(2, 4), fileIdCounter++);
                BinaryPrimitives.WriteUInt16LittleEndian(recBuf.AsSpan(6, 2), 2); // file type = 2
                nameUtf16.CopyTo(recBuf.AsSpan(8));

                await outStream.WriteAsync(recBuf, cancellationToken).ConfigureAwait(false);
            }

            // 3. Sequential File Data Chunks (Magic: 0x16ccf80a)
            foreach (var relFile in batchFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string sourceFileFullPath = Path.Combine(srcBasePath, relFile);

                try
                {
                    using var inStream = new FileStream(sourceFileFullPath, FileMode.Open, FileAccess.Read, FileShare.Read, options.StreamBufferSize);
                    long fileLen = inStream.Length;

                    byte[] chunkHdr = new byte[31];
                    BinaryPrimitives.WriteUInt32LittleEndian(chunkHdr.AsSpan(0, 4), NkxArchiveReader.ChunkAudioDataMagic);
                    BinaryPrimitives.WriteUInt32LittleEndian(chunkHdr.AsSpan(4, 4), NkxArchiveReader.VersionModern);
                    BinaryPrimitives.WriteUInt32LittleEndian(chunkHdr.AsSpan(8, 4), 3); // type 3
                    BinaryPrimitives.WriteUInt64LittleEndian(chunkHdr.AsSpan(19, 8), (ulong)fileLen);

                    await outStream.WriteAsync(chunkHdr, cancellationToken).ConfigureAwait(false);
                    await inStream.CopyToAsync(outStream, options.StreamBufferSize, cancellationToken).ConfigureAwait(false);

                    totalPacked++;
                    totalBytesPacked += fileLen;
                }
                catch
                {
                    failedFiles.Add(relFile);
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

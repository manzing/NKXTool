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

    /// <summary>Parents searched for a name map above the packed folder.</summary>
    private const int NameMapSearchDepth = 3;

    /// <summary>
    /// Configuration options for packing files into an NKX archive.
    /// </summary>
    public sealed class PackOptions
    {
        public string OutputFilePath { get; set; } = string.Empty;
        public string SourcePathOrFileList { get; set; } = string.Empty;
        public string? RootDirectoryName { get; set; }
        public string? SourceBaseDirectory { get; set; }

        /// <summary>
        /// Optional path of an <see cref="ArchiveNameMap"/> file. When omitted, the map written at
        /// the root of the source folder by an extraction is used, if present.
        /// </summary>
        public string? NameMapPath { get; set; }

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

        /// <summary>Entries whose original archive name was restored from the name map.</summary>
        public int RestoredNameCount { get; init; }

        /// <summary>Map file used to restore original names, or null when none was found.</summary>
        public string? NameMapPath { get; init; }
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
            ExtendedPath.CreateDirectory(outDir);

        // 1. Resolve source files and base path
        var filesToPack = new List<string>();
        string srcBasePath;

        if (options.SourcePathOrFileList.StartsWith("@"))
        {
            string listFile = options.SourcePathOrFileList.Substring(1);
            if (!ExtendedPath.Exists(listFile))
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
            if (!ExtendedPath.DirectoryExists(srcBasePath))
                throw new DirectoryNotFoundException($"Source directory not found: {srcBasePath}");

            foreach (var f in Directory.GetFiles(srcBasePath, "*", SearchOption.AllDirectories))
            {
                filesToPack.Add(Path.GetRelativePath(srcBasePath, f));
            }
        }

        if (filesToPack.Count == 0)
            throw new InvalidOperationException("No files to pack.");

        // A folder produced by an extraction carries the original name of every entry whose name
        // Windows cannot store. Those names go back into the directory records, so the container
        // keeps exactly the names the instrument patches reference; the payload is still read
        // through the disk name, only the record differs.
        //
        // The map sits at the extraction root, which is not always the packed folder: a library keeps
        // its samples in a "Samples" subfolder one level below. The map is therefore looked up in the
        // source folder and its parents, and its keys are rebased on the packed folder.
        string? nameMapPath;
        if (!string.IsNullOrWhiteSpace(options.NameMapPath))
        {
            nameMapPath = Path.GetFullPath(options.NameMapPath);
            if (!ExtendedPath.Exists(nameMapPath))
                throw new FileNotFoundException($"Name map not found: {nameMapPath}", nameMapPath);
        }
        else
        {
            nameMapPath = FindNameMapPath(srcBasePath, NameMapSearchDepth);
        }

        // Keys of a map are relative to the folder that holds it.
        string nameMapBaseDirectory = nameMapPath != null
            ? Path.GetDirectoryName(nameMapPath) ?? srcBasePath
            : srcBasePath;

        ArchiveNameMap? nameMap = nameMapPath != null ? ArchiveNameMap.LoadFile(nameMapPath) : null;

        // Path from the map root down to the packed folder, e.g. "Samples": the map records names
        // from the extraction root, so every key is rebuilt with this prefix before being resolved.
        string mapPrefix = string.Empty;
        if (nameMap != null)
        {
            string relativeBase = Path.GetRelativePath(nameMapBaseDirectory, srcBasePath);
            if (relativeBase != "." && !relativeBase.StartsWith("..", StringComparison.Ordinal))
                mapPrefix = relativeBase;
        }

        // Container root name implied by the map: the first folder below the extraction root.
        string? mapRootFolder = null;
        if (mapPrefix.Length > 0)
        {
            string firstSegment = mapPrefix.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            if (firstSegment.Length > 0) mapRootFolder = firstSegment;
        }

        if (nameMap != null)
        {
            string fullNameMapPath = Path.GetFullPath(nameMapPath!);
            filesToPack.RemoveAll(f => string.Equals(
                Path.GetFullPath(Path.Combine(srcBasePath, f)), fullNameMapPath, StringComparison.OrdinalIgnoreCase));
        }

        // Key of a packed file in the map, i.e. its path relative to the extraction root.
        string MapKey(string diskRelativePath) => mapPrefix.Length == 0
            ? diskRelativePath
            : mapPrefix + "\\" + diskRelativePath.Replace('/', '\\');

        // Name a file must carry inside the archive, as stored in the map, i.e. relative to the
        // extraction root. The root-folder strip below then turns it into the directory segments.
        string ArchivedRelativePath(string diskRelativePath) => nameMap == null
            ? diskRelativePath
            : nameMap.Resolve(MapKey(diskRelativePath));

        int restoredNameCount = 0;
        if (nameMap != null)
        {
            foreach (string file in filesToPack)
            {
                if (!string.Equals(nameMap.Resolve(MapKey(file)), MapKey(file), StringComparison.Ordinal))
                    restoredNameCount++;
            }
        }

        // 2. Batch files into volumes
        var batches = new List<List<string>>();
        var currentBatch = new List<string>();
        long currentSize = 0;

        foreach (var file in filesToPack)
        {
            string fullPath = Path.Combine(srcBasePath, file);
            if (!ExtendedPath.Exists(fullPath)) continue;

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
            else if (mapRootFolder != null)
            {
                // The map was written above the packed folder: that prefix is the container root.
                rootFolder = mapRootFolder;
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

            // Build the directory tree of this volume: Kontakt stores one directory chunk per
            // folder, so the packed container must mirror the folder structure. Writing a flat list
            // whose file names carry backslashes leaves hosts (and Kontakt itself) with a single
            // folder full of impossible names instead of real subfolders.
            var rootNode = new PackDirNode { Name = rootFolder };

            foreach (var relFile in batchFiles)
            {
                string sourceFileFullPath = Path.Combine(srcBasePath, relFile);
                if (!ExtendedPath.Exists(sourceFileFullPath))
                {
                    failedFiles.Add(relFile);
                    continue;
                }

                // Directory records carry the archive name; the disk name only locates the payload.
                string cleanName = ArchivedRelativePath(relFile);
                if (cleanName.StartsWith(rootFolder + "\\", StringComparison.OrdinalIgnoreCase) ||
                    cleanName.StartsWith(rootFolder + "/", StringComparison.OrdinalIgnoreCase))
                {
                    cleanName = cleanName.Substring(rootFolder.Length + 1);
                }

                string[] segments = cleanName.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length == 0) continue;

                PackDirNode parent = rootNode;
                for (int i = 0; i < segments.Length - 1; i++)
                {
                    PackDirNode? child = null;
                    foreach (PackDirNode candidate in parent.Dirs)
                    {
                        if (string.Equals(candidate.Name, segments[i], StringComparison.Ordinal))
                        {
                            child = candidate;
                            break;
                        }
                    }

                    if (child == null)
                    {
                        child = new PackDirNode { Name = segments[i] };
                        parent.Dirs.Add(child);
                    }

                    parent = child;
                }

                parent.Files.Add(new PackFileNode
                {
                    RelativePath = relFile,
                    LeafName = segments[^1],
                    Length = new FileInfo(sourceFileFullPath).Length
                });
            }

            rootNode.ChunkOffset = dirOffset;
            LayoutDirectoryNode(rootNode);

            int volumeFiles = 0;
            long volumeBytes = 0;
            await WriteDirectoryChunkAsync(
                outStream, rootNode, srcBasePath, options.StreamBufferSize, cancellationToken,
                () => volumeFiles++, length => volumeBytes += length).ConfigureAwait(false);

            totalPacked += volumeFiles;
            totalBytesPacked += volumeBytes;

            volumePaths.Add(volumePath);
        }

        sw.Stop();

        return new PackResult
        {
            TotalPackedFiles = totalPacked,
            TotalBytesPacked = totalBytesPacked,
            Elapsed = sw.Elapsed,
            VolumeFilePaths = volumePaths,
            FailedFiles = failedFiles,
            RestoredNameCount = restoredNameCount,
            NameMapPath = nameMapPath
        };
    }

    /// <summary>
    /// First folder, starting at <paramref name="sourceDirectory"/>, that holds an
    /// <see cref="ArchiveNameMap"/>; the search stops after <paramref name="maxParentLevels"/> parents.
    /// </summary>
    private static string? FindNameMapPath(string sourceDirectory, int maxParentLevels)
    {
        string? current = sourceDirectory;

        for (int level = 0; level <= maxParentLevels && !string.IsNullOrEmpty(current); level++)
        {
            string candidate = Path.Combine(current, ArchiveNameMap.DefaultFileName);
            if (ExtendedPath.Exists(candidate)) return candidate;

            current = Path.GetDirectoryName(current);
        }

        return null;
    }

    /// <summary>
    /// One file of the packed tree: the leaf name goes into the directory record, the payload
    /// follows in an audio data chunk.
    /// </summary>
    private sealed class PackFileNode
    {
        public string RelativePath = string.Empty;
        public string LeafName = string.Empty;
        public long Length;
        public long ChunkOffset;

        public long RecordBytes => 8 + Encoding.Unicode.GetByteCount(LeafName + "\0");
    }

    /// <summary>
    /// One folder of the packed tree, mirroring a Kontakt directory chunk.
    /// </summary>
    private sealed class PackDirNode
    {
        public string Name = string.Empty;
        public readonly List<PackFileNode> Files = new();
        public readonly List<PackDirNode> Dirs = new();
        public long ChunkOffset;

        public long RecordsBytes
        {
            get
            {
                long total = 0;
                foreach (PackFileNode file in Files) total += file.RecordBytes;
                foreach (PackDirNode dir in Dirs) total += 8 + Encoding.Unicode.GetByteCount(dir.Name + "\0");
                return total;
            }
        }

        public long DirectoryBytes
        {
            get
            {
                long total = 22 + RecordsBytes;
                foreach (PackFileNode file in Files) total += 31 + file.Length;
                return total;
            }
        }

        public long ChunkSize
        {
            get
            {
                long total = DirectoryBytes;
                foreach (PackDirNode dir in Dirs) total += dir.ChunkSize;
                return total;
            }
        }
    }

    /// <summary>
    /// Assigns the absolute chunk offset of every folder, file record and payload, so directory
    /// records can point at the chunks that follow them.
    /// </summary>
    private static void LayoutDirectoryNode(PackDirNode node)
    {
        long cursor = node.ChunkOffset + 22 + node.RecordsBytes;

        foreach (PackFileNode file in node.Files)
        {
            file.ChunkOffset = cursor;
            cursor += 31 + file.Length;
        }

        foreach (PackDirNode dir in node.Dirs)
        {
            dir.ChunkOffset = cursor;
            LayoutDirectoryNode(dir);
            cursor += dir.ChunkSize;
        }
    }

    private static async Task WriteDirectoryChunkAsync(
        Stream outStream,
        PackDirNode node,
        string srcBasePath,
        int streamBufferSize,
        CancellationToken cancellationToken,
        Action onFileWritten,
        Action<long> onBytesWritten)
    {
        int entryCount = node.Files.Count + node.Dirs.Count;

        byte[] dirHdr = new byte[22];
        BinaryPrimitives.WriteUInt32LittleEndian(dirHdr.AsSpan(0, 4), NkxArchiveReader.ChunkContainerMagic);
        BinaryPrimitives.WriteUInt16LittleEndian(dirHdr.AsSpan(4, 2), NkxArchiveReader.VersionModern);
        BinaryPrimitives.WriteUInt16LittleEndian(dirHdr.AsSpan(6, 2), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(dirHdr.AsSpan(8, 4), 0x00ff0000);
        BinaryPrimitives.WriteUInt16LittleEndian(dirHdr.AsSpan(12, 2), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(dirHdr.AsSpan(14, 2), (ushort)entryCount);

        await outStream.WriteAsync(dirHdr, cancellationToken).ConfigureAwait(false);

        // File records first: they point at the payload chunks that immediately follow.
        foreach (PackFileNode file in node.Files)
        {
            byte[] nameBytes = Encoding.Unicode.GetBytes(file.LeafName + "\0");
            byte[] recBuf = new byte[8 + nameBytes.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(recBuf.AsSpan(0, 2), (ushort)recBuf.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(recBuf.AsSpan(2, 4), (uint)file.ChunkOffset ^ NkxArchiveReader.FileRecordIdXorMask);
            BinaryPrimitives.WriteUInt16LittleEndian(recBuf.AsSpan(6, 2), 2); // encrypted-file style record
            nameBytes.CopyTo(recBuf.AsSpan(8));

            await outStream.WriteAsync(recBuf, cancellationToken).ConfigureAwait(false);
        }

        // Then directory records, whose id is the plain offset of the subfolder chunk.
        foreach (PackDirNode dir in node.Dirs)
        {
            byte[] nameBytes = Encoding.Unicode.GetBytes(dir.Name + "\0");
            byte[] recBuf = new byte[8 + nameBytes.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(recBuf.AsSpan(0, 2), (ushort)recBuf.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(recBuf.AsSpan(2, 4), (uint)dir.ChunkOffset);
            BinaryPrimitives.WriteUInt16LittleEndian(recBuf.AsSpan(6, 2), 1); // directory
            nameBytes.CopyTo(recBuf.AsSpan(8));

            await outStream.WriteAsync(recBuf, cancellationToken).ConfigureAwait(false);
        }

        foreach (PackFileNode file in node.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string sourceFileFullPath = Path.Combine(srcBasePath, file.RelativePath);
            using var inStream = new FileStream(
                ExtendedPath.ForFileSystem(sourceFileFullPath),
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                streamBufferSize);

            long fileLen = inStream.Length;

            byte[] chunkHdr = new byte[31];
            BinaryPrimitives.WriteUInt32LittleEndian(chunkHdr.AsSpan(0, 4), NkxArchiveReader.ChunkAudioDataMagic);
            BinaryPrimitives.WriteUInt16LittleEndian(chunkHdr.AsSpan(4, 2), NkxArchiveReader.VersionModern);
            BinaryPrimitives.WriteUInt16LittleEndian(chunkHdr.AsSpan(6, 2), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(chunkHdr.AsSpan(8, 4), 0x00ff0000);
            BinaryPrimitives.WriteUInt16LittleEndian(chunkHdr.AsSpan(12, 2), 0);
            chunkHdr[14] = 0x03; // Chunk type 3 (Audio data)
            BinaryPrimitives.WriteUInt64LittleEndian(chunkHdr.AsSpan(19, 8), (ulong)fileLen);

            await outStream.WriteAsync(chunkHdr, cancellationToken).ConfigureAwait(false);
            await inStream.CopyToAsync(outStream, streamBufferSize, cancellationToken).ConfigureAwait(false);

            onFileWritten();
            onBytesWritten(fileLen);
        }

        foreach (PackDirNode dir in node.Dirs)
        {
            await WriteDirectoryChunkAsync(
                outStream, dir, srcBasePath, streamBufferSize, cancellationToken, onFileWritten, onBytesWritten)
                .ConfigureAwait(false);
        }
    }
}

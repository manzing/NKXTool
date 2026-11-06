namespace NKXTool.Core.Containers.Nkx;

using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using System.Xml;
using NKXTool.Core.Common;
using NKXTool.Core.Cryptography;

/// <summary>
/// High-performance native reader and extractor for Kontakt containers (.nkx, .nkr, .nks and the
/// monolith patch containers .nki/.nkm/.nkb/.nkg).
/// Supports modern containers (0x0110, 0x0111, 0x0401) with recursive directory chunk trees,
/// monoliths prefixed by a proprietary sample header, legacy 0x0100 containers (fixed-size Latin1
/// records, optional static keys), and automatic transparent AES-256 CTR / LCG decryption.
/// </summary>
public sealed class NkxArchiveReader : IArchiveReader
{
    public const uint ChunkContainerMagic = 0x5e70ac54; // Big chunk: root and directory tree
    public const uint ChunkAudioDataMagic = 0x16ccf80a; // Audio sample data (encrypted file chunk)
    public const uint ChunkResourceDataMagic = 0x2ae905fa; // Pictures, fonts, text resources
    public const uint ChunkScriptDataMagic = 0x4916e63c; // Script data (plain file chunk)

    public const uint FileRecordIdXorMask = 0x1F4E0C8D; // Directory record file ID XOR mask

    public const ushort VersionModern = 0x0110;
    public const ushort VersionModernEncrypted = 0x0111;
    public const ushort VersionLegacy = 0x0100;

    /// <summary>Key index of a payload that is stored in clear.</summary>
    public const int KeyIndexNone = 0xFF;

    /// <summary>Highest key index of the static key table used by legacy (0x0100) containers.</summary>
    public const int LegacyKeyIndexMax = 0x1F;

    /// <summary>
    /// Size of a legacy (0x0100) directory record: a 128-character Latin1 name padded with zeros,
    /// one unused byte, a 32-bit offset and a 16-bit type hint.
    /// </summary>
    private const int LegacyRecordStride = 135;

    private const ushort RecordTypeDirectory = 1;
    private const ushort RecordTypeEncryptedFile = 2;
    private const ushort RecordTypeFile = 3;

    private readonly string _filePath;
    private readonly ArchiveType _archiveType;
    private readonly List<ArchiveEntry> _entries = new();
    private readonly NkxKeyDatabase _keyDatabase;
    private FileStream? _fileStream;
    private int _nextEntryId = 1;
    private bool _isContainerEncrypted;

    private byte[]? _keystream;
    private bool _keystreamResolved;
    private readonly object _keystreamLock = new();

    public ArchiveType ArchiveType => _archiveType;
    public string FilePath => _filePath;
    public IReadOnlyList<ArchiveEntry> Entries => _entries;
    public bool IsEncrypted => _isContainerEncrypted || _entries.Any(e => e.IsEncrypted);

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
        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(hdr.AsSpan(4, 2));
        ushort keyId = BinaryPrimitives.ReadUInt16LittleEndian(hdr.AsSpan(6, 2));

        if (magic == ChunkContainerMagic)
        {
            // Encryption is signalled by the key id, not by the version: containers of version 0x0111
            // with key id 0 store their payloads in clear (e.g. All Saints Choir LiTE, Amati Viola).
            // Treating those as encrypted XORs the keystream onto plaintext and silently corrupts output.
            _isContainerEncrypted = keyId != 0;

            if (version == VersionLegacy)
            {
                // Legacy containers hold their whole directory tree from offset 0 and use
                // fixed-size, Latin1 directory records.
                ParseDirectoryRecursive(stream, 0, string.Empty, version);
            }
            else
            {
                ParseModernContainer(stream, version);
            }

            return;
        }

        // Some libraries ship monolith containers whose file begins with a proprietary sample
        // header (magic 0x7FA89012 for Embertone, for instance) followed by the root directory
        // chunk at an absolute offset, without the usual 64-byte root header.
        long embeddedRoot = FindEmbeddedRootDirectory(stream);
        if (embeddedRoot >= 0)
        {
            ParseDirectoryRecursive(stream, embeddedRoot, string.Empty, VersionModern);
            return;
        }

        throw new InvalidDataException($"Unsupported archive magic: 0x{magic:X8} (expected 0x{ChunkContainerMagic:X8}).");
    }

    private void ParseModernContainer(FileStream stream, ushort version)
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

        ParseDirectoryRecursive(stream, dirChunkOffset, rootName, version);
    }

    private long ParseDirectoryRecursive(FileStream stream, long chunkOffset, string currentPath, ushort version)
    {
        if (chunkOffset < 0 || chunkOffset >= stream.Length) return chunkOffset;

        stream.Seek(chunkOffset, SeekOrigin.Begin);
        byte[] dirHdr = new byte[22];
        int read = stream.Read(dirHdr, 0, 22);
        if (read < 22) return chunkOffset;

        uint dirMagic = BinaryPrimitives.ReadUInt32LittleEndian(dirHdr.AsSpan(0, 4));
        if (dirMagic != ChunkContainerMagic)
            return chunkOffset;

        // The entry count is a 32-bit field in both layouts.
        uint entryCount = BinaryPrimitives.ReadUInt32LittleEndian(dirHdr.AsSpan(14, 4));
        if (entryCount > 1_000_000) return chunkOffset;

        long entriesStart = chunkOffset + 22;
        var dirRecords = new List<(uint id, ushort type, string name)>(entryCount > 4096 ? 4096 : (int)entryCount);

        if (version == VersionLegacy)
        {
            ReadLegacyDirectoryRecords(stream, entriesStart, entryCount, dirRecords);
        }
        else
        {
            ReadModernDirectoryRecords(stream, entriesStart, entryCount, dirRecords);
        }

        // Sequential data chunks for file entries in this directory
        long dataCur = stream.Position;
        var fileRecords = dirRecords.Where(r => r.type != RecordTypeDirectory).ToList();

        foreach (var fileRec in fileRecords)
        {
            // Directory records hold the plain chunk offset for regular files (type 3, as written by
            // monolith containers) or the offset XOR-masked with FileRecordIdXorMask for encrypted
            // files (type 2). Native writers emit payloads sequentially, so the running cursor is
            // kept as the last resort.
            long chunkStartOffset = ResolveFileChunkOffset(stream, fileRec.id ^ FileRecordIdXorMask, fileRec.id, dataCur);
            if (chunkStartOffset <= 0 || chunkStartOffset >= stream.Length - 22)
                break;

            stream.Seek(chunkStartOffset, SeekOrigin.Begin);
            byte[] chunkProbe = new byte[32];
            int probeRead = stream.Read(chunkProbe, 0, 32);
            if (probeRead < 22) break;

            uint chunkMagic = BinaryPrimitives.ReadUInt32LittleEndian(chunkProbe.AsSpan(0, 4));

            // Every chunk header carries the set id and the key index: 0xFF means "stored in clear",
            // 0x00-0x1F a static legacy key and 0x100 the modern per-library keystream.
            int chunkKeyIndex = (int)BinaryPrimitives.ReadUInt32LittleEndian(chunkProbe.AsSpan(10, 4));
            bool isChunkEncrypted = _isContainerEncrypted || chunkKeyIndex != KeyIndexNone;

            long itemSize;
            int headerLen;

            if (chunkMagic == ChunkResourceDataMagic) // 0x2ae905fa
            {
                itemSize = BinaryPrimitives.ReadUInt32LittleEndian(chunkProbe.AsSpan(14, 4));
                headerLen = 22;
            }
            else if (chunkMagic == ChunkAudioDataMagic) // 0x16ccf80a
            {
                // magic, version, set id, key index, 5 unknown, u32 size, 8 unknown
                itemSize = BinaryPrimitives.ReadUInt32LittleEndian(chunkProbe.AsSpan(19, 4));
                headerLen = 31;
            }
            else if (chunkMagic == ChunkScriptDataMagic) // 0x4916e63c
            {
                // magic, version, set id, key index, 5 unknown, u32 size, 4 unknown
                itemSize = BinaryPrimitives.ReadUInt32LittleEndian(chunkProbe.AsSpan(19, 4));
                headerLen = 27;
            }
            else
            {
                // Unknown or unaligned chunk
                break;
            }

            if (itemSize < 0 || chunkStartOffset + headerLen + itemSize > stream.Length)
                break;

            string relativePath;
            if (string.IsNullOrEmpty(currentPath))
            {
                relativePath = fileRec.name;
            }
            else if (fileRec.name.StartsWith(currentPath + "\\", StringComparison.OrdinalIgnoreCase) ||
                     fileRec.name.StartsWith(currentPath + "/", StringComparison.OrdinalIgnoreCase))
            {
                relativePath = fileRec.name;
            }
            else
            {
                relativePath = Path.Combine(currentPath, fileRec.name);
            }

            _entries.Add(new ArchiveEntry
            {
                Id = _nextEntryId++,
                RelativePath = relativePath,
                Size = itemSize,
                PackedSize = itemSize,
                DataOffset = chunkStartOffset + headerLen,
                IsDirectory = false,
                IsEncrypted = isChunkEncrypted,
                RawType = fileRec.type,
                KeyIndex = chunkKeyIndex
            });

            dataCur = chunkStartOffset + headerLen + itemSize;
        }

        // Subdirectories
        var subdirs = dirRecords.Where(r => r.type == RecordTypeDirectory).ToList();
        foreach (var sub in subdirs)
        {
            string subPath;
            if (string.IsNullOrEmpty(currentPath))
            {
                subPath = sub.name;
            }
            else if (sub.name.StartsWith(currentPath + "\\", StringComparison.OrdinalIgnoreCase) ||
                     sub.name.StartsWith(currentPath + "/", StringComparison.OrdinalIgnoreCase))
            {
                subPath = sub.name;
            }
            else if (currentPath.EndsWith("\\" + sub.name, StringComparison.OrdinalIgnoreCase) ||
                     currentPath.Equals(sub.name, StringComparison.OrdinalIgnoreCase))
            {
                subPath = currentPath;
            }
            else
            {
                subPath = Path.Combine(currentPath, sub.name);
            }

            dataCur = ParseDirectoryRecursive(stream, ResolveDirectoryChunkOffset(stream, sub.id, dataCur), subPath, version);
        }

        return dataCur;
    }

    /// <summary>
    /// Reads modern (0x0110/0x0111/0x0401) directory records: a 16-bit record size followed by the
    /// 32-bit id, the 16-bit type and a UTF-16 name.
    /// </summary>
    private static void ReadModernDirectoryRecords(
        FileStream stream,
        long entriesStart,
        uint entryCount,
        List<(uint id, ushort type, string name)> records)
    {
        stream.Seek(entriesStart, SeekOrigin.Begin);

        for (uint i = 0; i < entryCount; i++)
        {
            byte[] sizeBuf = new byte[2];
            if (stream.Read(sizeBuf, 0, 2) < 2) break;

            ushort recSize = BinaryPrimitives.ReadUInt16LittleEndian(sizeBuf);
            int dataLen = recSize - 2;
            if (dataLen < 6) break;

            byte[] recBuf = new byte[dataLen];
            if (stream.Read(recBuf, 0, dataLen) < dataLen) break;

            uint recId = BinaryPrimitives.ReadUInt32LittleEndian(recBuf.AsSpan(0, 4));
            ushort recType = BinaryPrimitives.ReadUInt16LittleEndian(recBuf.AsSpan(4, 2));
            string recName = Encoding.Unicode.GetString(recBuf.AsSpan(6)).TrimEnd('\0');

            records.Add((recId, recType, recName));
        }
    }

    /// <summary>
    /// Reads legacy (0x0100) directory records: fixed-size entries holding a NUL-padded Latin1 name,
    /// one unused byte, the offset and the type hint.
    /// </summary>
    private static void ReadLegacyDirectoryRecords(
        FileStream stream,
        long entriesStart,
        uint entryCount,
        List<(uint id, ushort type, string name)> records)
    {
        int stride = DetectLegacyRecordStride(stream, entriesStart, entryCount);
        byte[] record = new byte[stride];

        for (uint i = 0; i < entryCount; i++)
        {
            stream.Seek(entriesStart + i * stride, SeekOrigin.Begin);
            if (stream.Read(record, 0, stride) < stride) break;

            string recName = ReadLatin1Name(record);
            uint recId = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(stride - 6, 4));
            ushort recType = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(stride - 2, 2));

            records.Add((recId, recType, recName));
        }

        stream.Seek(entriesStart + (long)entryCount * stride, SeekOrigin.Begin);
    }

    /// <summary>
    /// Legacy records are 135 bytes (128-byte name field) in Kontakt 1 files and 136 bytes (129-byte
    /// name field) in files written by later versions. The type hint and the target offset of the
    /// first records tell the two apart.
    /// </summary>
    private static int DetectLegacyRecordStride(FileStream stream, long entriesStart, uint entryCount)
    {
        if (entryCount == 0) return LegacyRecordStride;

        byte[] probe = new byte[2 * (LegacyRecordStride + 1) + 8];
        stream.Seek(entriesStart, SeekOrigin.Begin);
        int read = stream.Read(probe, 0, probe.Length);

        foreach (int candidate in new[] { LegacyRecordStride, LegacyRecordStride + 1 })
        {
            if (read < candidate) continue;

            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(probe.AsSpan(candidate - 2, 2));
            if (!IsKnownRecordType(type)) continue;

            uint rawOffset = BinaryPrimitives.ReadUInt32LittleEndian(probe.AsSpan(candidate - 6, 4));
            long offset = type == RecordTypeEncryptedFile ? rawOffset ^ FileRecordIdXorMask : rawOffset;
            if (offset <= 0 || offset >= stream.Length) continue;

            if (entryCount > 1 && read >= 2 * candidate)
            {
                ushort nextType = BinaryPrimitives.ReadUInt16LittleEndian(probe.AsSpan(2 * candidate - 2, 2));
                if (!IsKnownRecordType(nextType)) continue;
            }

            return candidate;
        }

        return LegacyRecordStride;
    }

    private static bool IsKnownRecordType(ushort type) =>
        type is RecordTypeDirectory or RecordTypeEncryptedFile or RecordTypeFile;

    /// <summary>
    /// Reads the NUL-terminated Latin1 name of a legacy record, never reaching into the offset and
    /// type fields that follow the fixed-size name area.
    /// </summary>
    private static string ReadLatin1Name(ReadOnlySpan<byte> record)
    {
        int nameArea = record.Length - 7;
        if (nameArea <= 0) return string.Empty;

        ReadOnlySpan<byte> nameBytes = record[..nameArea];
        int terminator = nameBytes.IndexOf((byte)0);
        if (terminator >= 0)
            nameBytes = nameBytes[..terminator];

        return Encoding.Latin1.GetString(nameBytes).Trim();
    }

    /// <summary>
    /// Returns true when the file is a Kontakt container, either a standard one (root header magic)
    /// or a monolith one whose root directory chunk follows a proprietary file header.
    /// </summary>
    public static bool IsSupportedContainer(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024);
            if (stream.Length < 22) return false;

            byte[] head = new byte[4];
            stream.ReadExactly(head);
            if (BinaryPrimitives.ReadUInt32LittleEndian(head) == ChunkContainerMagic)
                return true;

            return FindEmbeddedRootDirectory(stream) >= 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Locates the root directory chunk of a monolith container that starts with a proprietary
    /// file header instead of the standard 64-byte root header.
    /// </summary>
    private static long FindEmbeddedRootDirectory(FileStream stream)
    {
        const int ScanLength = 64 * 1024;
        int length = (int)Math.Min(ScanLength, stream.Length);
        if (length < 22) return -1;

        byte[] buffer = new byte[length];
        stream.Seek(0, SeekOrigin.Begin);
        int read = stream.Read(buffer, 0, length);
        if (read < 22) return -1;

        for (int i = 0; i <= read - 22; i++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(i, 4)) != ChunkContainerMagic)
                continue;

            ushort candidateVersion = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(i + 4, 2));
            if (candidateVersion != VersionModern && candidateVersion != VersionModernEncrypted &&
                candidateVersion != VersionLegacy && candidateVersion != 0x0401)
                continue;

            if (BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(i + 14, 4)) == 0)
                continue;

            return i;
        }

        return -1;
    }

    private static long ResolveFileChunkOffset(FileStream stream, long xorMaskedOffset, long plainOffset, long sequentialOffset)
    {
        if (IsDataChunkAt(stream, xorMaskedOffset)) return xorMaskedOffset;
        if (IsDataChunkAt(stream, plainOffset)) return plainOffset;
        return sequentialOffset;
    }

    private static long ResolveDirectoryChunkOffset(FileStream stream, long recordedOffset, long sequentialOffset)
    {
        return IsDirectoryChunkAt(stream, recordedOffset) ? recordedOffset : sequentialOffset;
    }

    private static bool IsDataChunkAt(FileStream stream, long offset)
    {
        if (offset <= 0 || offset >= stream.Length - 22) return false;

        stream.Seek(offset, SeekOrigin.Begin);
        byte[] probe = new byte[4];
        if (stream.Read(probe, 0, 4) != 4) return false;

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(probe);
        return magic == ChunkAudioDataMagic || magic == ChunkResourceDataMagic || magic == ChunkScriptDataMagic;
    }

    private static bool IsDirectoryChunkAt(FileStream stream, long offset)
    {
        if (offset <= 0 || offset >= stream.Length - 22) return false;

        stream.Seek(offset, SeekOrigin.Begin);
        byte[] probe = new byte[4];
        if (stream.Read(probe, 0, 4) != 4) return false;

        return BinaryPrimitives.ReadUInt32LittleEndian(probe) == ChunkContainerMagic;
    }

    /// <summary>
    /// Obtains or creates the cached 64 KB keystream for decrypting protected container entries.
    /// </summary>
    public byte[]? GetOrCreateKeystream()
    {
        if (_keystreamResolved) return _keystream;

        lock (_keystreamLock)
        {
            if (_keystreamResolved) return _keystream;

            var keyEntry = ResolveKeyEntry();
            if (keyEntry != null && keyEntry.JdxBytes.Length == 32 && keyEntry.HuBytes.Length == 16)
            {
                _keystream = NkxKeystream.CreateKeystream(keyEntry.JdxBytes, keyEntry.HuBytes);
            }

            _keystreamResolved = true;
            return _keystream;
        }
    }

    private NkxKeyEntry? ResolveKeyEntry()
    {
        string archiveBaseName = Path.GetFileNameWithoutExtension(_filePath);
        string? archiveDir = Path.GetDirectoryName(_filePath);
        List<string> lookupNames = BuildLibraryLookupNames(archiveBaseName, archiveDir);

        // 1. Native Instruments registry: an installed, working library necessarily carries the right
        //    keys there, so the registry wins over every other source.
        foreach (string name in lookupNames)
        {
            if (NkxRegistryKeys.TryGet(name, out var registryEntry))
                return registryEntry;
        }

        // 2. nklibs.db, imported from NativeAccess.xml (local or online); it also carries the keys of
        //    the Service Center, so a nearby .nicnt file is only the last resort.
        foreach (string name in lookupNames)
        {
            if (_keyDatabase.TryGetKey(name, out var foundEntry))
                return foundEntry;
        }

        // 3. The .nicnt file shipped next to the archive (or in the folder above it).
        if (string.IsNullOrEmpty(archiveDir))
            return null;

        foreach (string nicnt in FindNearbyNicntFiles(archiveDir, lookupNames))
        {
            var nicntEntry = TryExtractKeyFromNicnt(nicnt);
            if (nicntEntry != null) return nicntEntry;
        }

        return null;
    }

    /// <summary>
    /// Names under which the library owning the archive may be known, most specific first: the
    /// archive name, that name without its volume suffix (e.g. "part3"), the containing folder and
    /// the folder above it.
    /// </summary>
    private static List<string> BuildLibraryLookupNames(string archiveBaseName, string? archiveDir)
    {
        var names = new List<string>(4);

        void Add(string? candidate)
        {
            string name = candidate?.Trim() ?? string.Empty;
            if (name.Length == 0) return;
            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase)) names.Add(name);
        }

        Add(archiveBaseName);
        Add(System.Text.RegularExpressions.Regex.Replace(
            archiveBaseName, @"[_\s]*(?:part|\d)+$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase));

        if (string.IsNullOrEmpty(archiveDir)) return names;

        Add(Path.GetFileName(archiveDir));

        string? parent = Path.GetDirectoryName(archiveDir);
        if (!string.IsNullOrEmpty(parent))
            Add(Path.GetFileName(parent));

        return names;
    }

    /// <summary>
    /// Sibling (and parent folder) <c>.nicnt</c> files whose name matches one of the lookup names.
    /// Unrelated <c>.nicnt</c> files are ignored: guessing a key would silently corrupt the output.
    /// </summary>
    private static IEnumerable<string> FindNearbyNicntFiles(string archiveDir, List<string> lookupNames)
    {
        var directories = new List<string> { archiveDir };
        string? parent = Path.GetDirectoryName(archiveDir);
        if (!string.IsNullOrEmpty(parent))
            directories.Add(parent);

        foreach (string dir in directories)
        {
            string[] nicntFiles;
            try
            {
                nicntFiles = Directory.GetFiles(dir, "*.nicnt");
            }
            catch (Exception)
            {
                continue;
            }

            foreach (string nicnt in nicntFiles)
            {
                if (lookupNames.Contains(Path.GetFileNameWithoutExtension(nicnt), StringComparer.OrdinalIgnoreCase))
                    yield return nicnt;
            }
        }
    }

    private NkxKeyEntry? TryExtractKeyFromNicnt(string nicntPath)
    {
        try
        {
            using var fs = new FileStream(nicntPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            byte[] probe = new byte[64 * 1024];
            int read = fs.Read(probe, 0, probe.Length);
            string text = Encoding.UTF8.GetString(probe, 0, read);

            int startIdx = text.IndexOf("<ProductHints", StringComparison.OrdinalIgnoreCase);
            int endIdx = text.IndexOf("</ProductHints>", StringComparison.OrdinalIgnoreCase);
            if (startIdx >= 0 && endIdx > startIdx)
            {
                endIdx += "</ProductHints>".Length;
                string xml = text.Substring(startIdx, endIdx - startIdx);
                var doc = new XmlDocument();
                doc.LoadXml(xml);

                string? hu = doc.SelectSingleNode("//HU")?.InnerText?.Trim();
                string? jdx = doc.SelectSingleNode("//JDX")?.InnerText?.Trim();
                string? snpid = doc.SelectSingleNode("//SNPID")?.InnerText?.Trim();
                string? regKey = doc.SelectSingleNode("//RegKey")?.InnerText?.Trim() ?? doc.SelectSingleNode("//Name")?.InnerText?.Trim();
                string? company = doc.SelectSingleNode("//Company")?.InnerText?.Trim();

                if (!string.IsNullOrEmpty(hu) && !string.IsNullOrEmpty(jdx))
                {
                    return new NkxKeyEntry
                    {
                        Snpid = snpid ?? Path.GetFileNameWithoutExtension(nicntPath),
                        JdxHex = jdx,
                        HuHex = hu,
                        RegKey = regKey ?? Path.GetFileNameWithoutExtension(nicntPath),
                        Company = company ?? "Unknown"
                    };
                }

                if (!string.IsNullOrEmpty(snpid) && _keyDatabase.TryGetKey(snpid, out var snpEntry))
                    return snpEntry;

                if (!string.IsNullOrEmpty(regKey) && _keyDatabase.TryGetKey(regKey, out var regEntry))
                    return regEntry;
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Wraps an entry payload in the stream that yields its clear content: a plain slice for clear
    /// entries, the legacy static key stream for old containers, and the library keystream otherwise.
    /// </summary>
    private Stream CreatePayloadStream(FileStream source, ArchiveEntry entry)
    {
        Stream payload = new SubReadStream(source, entry.DataOffset, entry.Size);

        if (!entry.IsEncrypted)
            return payload;

        if (entry.KeyIndex >= 0 && entry.KeyIndex <= LegacyKeyIndexMax)
        {
            // Legacy (0x0100) payloads use one of 32 static 16-byte keys, applied from the absolute
            // file offset of the payload rather than from its start.
            byte[] legacyKey = NkxKeystream.CreateLegacyKey(entry.KeyIndex);
            return new NkxDecryptReadStream(payload, legacyKey, leaveOpen: true, phase: entry.DataOffset);
        }

        byte[]? keystream = GetOrCreateKeystream();
        if (keystream == null)
        {
            throw new InvalidOperationException(
                $"Cannot decrypt entry '{entry.RelativePath}'. Decryption key (HU/JDX) not found in the Native Instruments registry, nklibs.db or an associated .nicnt file. " +
                $"Please run 'NkxTool update' to import keys from NativeAccess.xml or add the key to nklibs.db.");
        }

        return new NkxDecryptReadStream(payload, keystream, leaveOpen: true);
    }

    public Stream OpenEntryStream(ArchiveEntry entry)
    {
        _fileStream ??= new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024);
        return CreatePayloadStream(_fileStream, entry);
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
                using var readStream = CreatePayloadStream(sourceStream, entry);

                using (var destStream = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None, options.StreamBufferSize, FileOptions.Asynchronous))
                {
                    await readStream.CopyToAsync(destStream, options.StreamBufferSize, ct).ConfigureAwait(false);
                }

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

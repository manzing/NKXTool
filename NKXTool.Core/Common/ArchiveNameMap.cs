namespace NKXTool.Core.Common;

using System.Text.Json;

/// <summary>
/// Side-car file that records, for every archive entry written under another name on disk, the name
/// it must keep inside the container.
///
/// A name Windows cannot store - a "*" in a library built on macOS, for instance - is written with
/// a replacement character by <see cref="ArchivePathSanitizer"/>, because no Windows API can create
/// the original. The instrument patches reference the stored name exactly, so the name may not be
/// altered inside the archive: extraction writes this map next to the extracted files, and packing
/// reads it back to restore the original name in the directory records, without writing that name
/// to disk. The payload is read through the disk name, only the record carries the archive name.
/// </summary>
public sealed class ArchiveNameMap
{
    /// <summary>Name of the map file, written at the root of an extraction folder.</summary>
    public const string DefaultFileName = "nkxtool-names.json";

    /// <summary>Version of the on-disk layout, so an older tool never misreads a newer file.</summary>
    public const int FormatVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Dictionary<string, string> _archiveNamesByDiskPath = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Number of entries whose disk name differs from the archive name.</summary>
    public int Count => _archiveNamesByDiskPath.Count;

    public bool IsEmpty => _archiveNamesByDiskPath.Count == 0;

    /// <summary>Disk path to archive path, both relative to the extraction root.</summary>
    public IReadOnlyDictionary<string, string> Mappings => _archiveNamesByDiskPath;

    /// <summary>
    /// Records that <paramref name="diskRelativePath"/> stands for the entry stored as
    /// <paramref name="archiveRelativePath"/>.
    /// </summary>
    public void Add(string diskRelativePath, string archiveRelativePath)
    {
        string key = Normalize(diskRelativePath);
        if (key.Length == 0) return;

        _archiveNamesByDiskPath[key] = Normalize(archiveRelativePath);
    }

    /// <summary>
    /// Name the entry must carry inside the archive: the original one when the given disk path was
    /// derived from it, the path itself otherwise.
    /// </summary>
    public string Resolve(string diskRelativePath) =>
        _archiveNamesByDiskPath.TryGetValue(Normalize(diskRelativePath), out string? archivePath)
            ? archivePath
            : diskRelativePath;

    /// <summary>
    /// Writes the map at the root of <paramref name="directory"/>, merging it with the map already
    /// there. Returns false when there is nothing to record, so a plain extraction leaves no extra
    /// file behind.
    /// </summary>
    public bool TrySave(string directory)
    {
        if (IsEmpty || string.IsNullOrEmpty(directory)) return false;

        string path = Path.Combine(directory, DefaultFileName);

        // The folder may already hold the map of an earlier extraction: those entries are kept,
        // otherwise extracting a second archive into the same folder would silently lose the
        // original name of the entries extracted first. An entry recorded now wins over an older
        // one carrying the same disk path; when the folder is reused for something else, the whole
        // map is removed with 'NkxTool namemap <folder> --clear'.
        var entries = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        ArchiveNameMap? existing = LoadFile(path);
        if (existing != null)
        {
            foreach (var pair in existing._archiveNamesByDiskPath)
                entries[pair.Key] = pair.Value;
        }

        foreach (var pair in _archiveNamesByDiskPath)
            entries[pair.Key] = pair.Value;

        var model = new FileModel { Version = FormatVersion };
        foreach (var pair in entries)
        {
            model.Entries.Add(new EntryModel { Disk = pair.Key, Archive = pair.Value });
        }
        try
        {
            File.WriteAllText(ExtendedPath.ForFileSystem(path), JsonSerializer.Serialize(model, SerializerOptions));
            return true;
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
    /// Removes the map of an extraction folder, so a pack keeps the names as they are on disk.
    /// Returns false when the folder has no map.
    /// </summary>
    public static bool Delete(string directory)
    {
        if (string.IsNullOrEmpty(directory)) return false;

        string path = ExtendedPath.ForFileSystem(Path.Combine(directory, DefaultFileName));
        if (!File.Exists(path)) return false;

        try
        {
            File.Delete(path);
            return true;
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
    /// Loads the map of an extraction folder, or null when the folder has none.
    /// </summary>
    public static ArchiveNameMap? Load(string directory)
    {
        if (string.IsNullOrEmpty(directory)) return null;
        return LoadFile(Path.Combine(directory, DefaultFileName));
    }

    /// <summary>
    /// Loads a map file, or null when it does not exist or is not readable.
    /// </summary>
    public static ArchiveNameMap? LoadFile(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return null;

        string fullPath = ExtendedPath.ForFileSystem(filePath);
        if (!File.Exists(fullPath)) return null;

        string json;
        try
        {
            json = File.ReadAllText(fullPath);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        try
        {
            FileModel? model = JsonSerializer.Deserialize<FileModel>(json, SerializerOptions);
            if (model?.Entries == null) return null;

            var map = new ArchiveNameMap();
            foreach (EntryModel entry in model.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Disk) || string.IsNullOrWhiteSpace(entry.Archive)) continue;
                map.Add(entry.Disk, entry.Archive);
            }

            return map.IsEmpty ? null : map;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Normalize(string path) =>
        string.IsNullOrEmpty(path) ? string.Empty : path.Replace('/', '\\').Trim('\\');

    private sealed class FileModel
    {
        public int Version { get; set; }
        public List<EntryModel> Entries { get; set; } = new();
    }

    private sealed class EntryModel
    {
        public string Disk { get; set; } = string.Empty;
        public string Archive { get; set; } = string.Empty;
    }
}

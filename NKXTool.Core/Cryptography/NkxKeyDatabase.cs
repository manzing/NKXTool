namespace NKXTool.Core.Cryptography;

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

/// <summary>
/// Fast, human-readable key database storing Kontakt library encryption keys (nklibs.db).
/// Backward-compatible with legacy nklibs_info.userdb files.
/// </summary>
public sealed class NkxKeyDatabase
{
    public const string DefaultDbFileName = "nklibs.db";
    public const string LegacyDbFileName = "nklibs_info.userdb";

    /// <summary>
    /// Online product hints document published by Native Instruments.
    /// It uses the same &lt;Product&gt; structure as the local NativeAccess.xml.
    /// </summary>
    public const string OnlineSourceUrl = "https://api.native-instruments.com/static/phints";

    private const string OnlineUserAgent = "NKXTool/1.0 NativeAccess";
    private const long MaxOnlineDocumentBytes = 64L * 1024 * 1024;

    private readonly Dictionary<string, NkxKeyEntry> _bySnpid = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, NkxKeyEntry> _byRegKey = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _bySnpid.Count;

    public static string ResolveDefaultDbPath(string? baseDirectory = null)
    {
        baseDirectory ??= AppDomain.CurrentDomain.BaseDirectory;
        string modernPath = Path.Combine(baseDirectory, DefaultDbFileName);
        if (File.Exists(modernPath)) return modernPath;

        string legacyPath = Path.Combine(baseDirectory, LegacyDbFileName);
        if (File.Exists(legacyPath))
        {
            // Convert / migrate legacy database to nklibs.db
            try
            {
                File.Copy(legacyPath, modernPath, overwrite: false);
                return modernPath;
            }
            catch
            {
                return legacyPath;
            }
        }

        return modernPath;
    }

    /// <summary>
    /// Opens the key database that applies to an archive: the database next to the host
    /// executable - the one <c>NkxTool update</c> writes - then the ones found beside the archive
    /// and up to three folders above it. When several files define the same entry the most local
    /// one wins, so a database copied along with a library is authoritative for that library.
    /// </summary>
    public static NkxKeyDatabase LoadForArchive(string archivePath)
    {
        var directories = new List<string>();

        void Add(string? directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) return;
            if (directories.Contains(directory, StringComparer.OrdinalIgnoreCase)) return;
            directories.Add(directory);
        }

        Add(AppContext.BaseDirectory);
        Add(Directory.GetCurrentDirectory());

        string? archiveDirectory = Path.GetDirectoryName(Path.GetFullPath(archivePath));
        for (int level = 0; level < 4 && !string.IsNullOrEmpty(archiveDirectory); level++)
        {
            Add(archiveDirectory);
            archiveDirectory = Path.GetDirectoryName(archiveDirectory);
        }

        var database = new NkxKeyDatabase();
        foreach (string directory in directories)
        {
            string path = ResolveDefaultDbPath(directory);
            if (File.Exists(path)) database.LoadFile(path);
        }

        return database;
    }

    /// <summary>
    /// Default location of the NativeAccess.xml file installed by Native Instruments.
    /// </summary>
    public static string ResolveDefaultNativeAccessXmlPath()
    {
        string commonFiles = Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles);
        return Path.Combine(commonFiles, "Native Instruments", "Service Center", "NativeAccess.xml");
    }

    public static NkxKeyDatabase Load(string? dbPath = null, string? baseDirectory = null)
    {
        var db = new NkxKeyDatabase();
        string resolvedPath = dbPath ?? ResolveDefaultDbPath(baseDirectory);

        if (File.Exists(resolvedPath))
        {
            db.LoadFile(resolvedPath);
        }

        return db;
    }

    /// <summary>
    /// Saves the database entries to a specified file path in INI format.
    /// </summary>
    public void SaveFile(string path)
    {
        File.WriteAllText(path, Serialize(), Encoding.UTF8);
    }

    public bool TryGetKey(string snpidOrKey, out NkxKeyEntry? entry)
    {
        if (_bySnpid.TryGetValue(snpidOrKey, out entry))
            return true;

        if (_byRegKey.TryGetValue(snpidOrKey, out entry))
            return true;

        entry = null;
        return false;
    }

    /// <summary>
    /// Keys of libraries whose registered name resembles one of <paramref name="names"/>, closest
    /// match first. Used when the archive is stored under a folder whose name differs from the
    /// library name registered in the database, typically through a version number or a suffix.
    /// These are candidates only: the caller must confirm one actually decrypts the container
    /// before using it.
    /// </summary>
    public IReadOnlyList<NkxKeyEntry> FindSimilarKeys(IEnumerable<string> names)
    {
        var wanted = names
            .Select(NormalizeLibraryName)
            .Where(name => name.Length >= 4)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (wanted.Count == 0) return Array.Empty<NkxKeyEntry>();

        var scored = new List<(NkxKeyEntry Entry, int Score)>();
        var seen = new HashSet<NkxKeyEntry>(ReferenceEqualityComparer.Instance);

        foreach (NkxKeyEntry entry in _bySnpid.Values)
        {
            if (!seen.Add(entry)) continue;

            int score = 0;
            foreach (string candidate in wanted)
            {
                score = Math.Max(score, ScoreLibraryName(candidate, entry.Snpid));
                score = Math.Max(score, ScoreLibraryName(candidate, entry.RegKey));
            }

            if (score > 0) scored.Add((entry, score));
        }

        return scored
            .OrderByDescending(match => match.Score)
            .Select(match => match.Entry)
            .ToList();
    }

    /// <summary>
    /// Rates how closely a registered library name matches a wanted name: 2 for the same library
    /// once normalized, 1 when one name contains the other ("... Ultimate" and "... Ultimate
    /// Library"), 0 when they are unrelated.
    /// </summary>
    private static int ScoreLibraryName(string wanted, string? registered)
    {
        string normalized = NormalizeLibraryName(registered);
        if (normalized.Length < 4) return 0;

        if (string.Equals(normalized, wanted, StringComparison.Ordinal)) return 2;
        if (normalized.Contains(wanted, StringComparison.Ordinal) ||
            wanted.Contains(normalized, StringComparison.Ordinal))
        {
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// Reduces a library name to lower-case letters and digits without accents, so that
    /// "Sunset Choir Ultimate 1.0.1" and "Sunset Choir Ultimate" compare equal: a folder name
    /// carries the version number that the registered name does not have.
    /// </summary>
    private static string NormalizeLibraryName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        var builder = new StringBuilder(name.Length);
        string decomposed = name.Normalize(NormalizationForm.FormD);
        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) builder.Append(char.ToLowerInvariant(c));
        }

        string result = builder.ToString();
        int end = result.Length;
        while (end > 0 && char.IsDigit(result[end - 1])) end--;

        return end == result.Length ? result : result[..end];
    }

    public void AddOrUpdate(NkxKeyEntry entry)
    {
        if (!string.IsNullOrEmpty(entry.Snpid))
            _bySnpid[entry.Snpid] = entry;

        if (!string.IsNullOrEmpty(entry.RegKey))
            _byRegKey[entry.RegKey] = entry;
    }

    public void LoadFile(string path)
    {
        if (!File.Exists(path)) return;

        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string? currentSnpid = null;
        string? jdx = null;
        string? hu = null;
        string? regKey = null;
        string? company = null;

        void CommitCurrent()
        {
            if (!string.IsNullOrEmpty(currentSnpid) && !string.IsNullOrEmpty(jdx) && !string.IsNullOrEmpty(hu))
            {
                var entry = new NkxKeyEntry
                {
                    Snpid = currentSnpid,
                    JdxHex = jdx,
                    HuHex = hu,
                    RegKey = regKey ?? "Unknown",
                    Company = company ?? "Unknown"
                };
                AddOrUpdate(entry);
            }
            currentSnpid = null;
            jdx = null;
            hu = null;
            regKey = null;
            company = null;
        }

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var span = line.AsSpan().Trim();
            if (span.IsEmpty || span.StartsWith("#") || span.StartsWith(";"))
                continue;

            if (span.StartsWith("[") && span.EndsWith("]"))
            {
                CommitCurrent();
                currentSnpid = span[1..^1].Trim().ToString();
                continue;
            }

            int eqIndex = span.IndexOf('=');
            if (eqIndex <= 0) continue;

            var key = span[..eqIndex].Trim();
            var val = span[(eqIndex + 1)..].Trim();

            if (key.Equals("JDX", StringComparison.OrdinalIgnoreCase))
                jdx = val.ToString();
            else if (key.Equals("HU", StringComparison.OrdinalIgnoreCase))
                hu = val.ToString();
            else if (key.Equals("RegKey", StringComparison.OrdinalIgnoreCase))
                regKey = val.ToString();
            else if (key.Equals("Company", StringComparison.OrdinalIgnoreCase))
                company = val.ToString();
        }

        CommitCurrent();
    }

    /// <summary>
    /// Updates or creates the nklibs.db database file by parsing the local NativeAccess.xml.
    /// Existing entries that are not present in the XML are preserved.
    /// </summary>
    public static NkxKeyDatabaseUpdateResult UpdateFromNativeAccessXml(
        string? customXmlPath = null,
        string? customDbPath = null)
    {
        string xmlPath = string.IsNullOrEmpty(customXmlPath)
            ? ResolveDefaultNativeAccessXmlPath()
            : Path.GetFullPath(customXmlPath);

        if (!File.Exists(xmlPath))
            throw new FileNotFoundException($"NativeAccess.xml file not found at: {xmlPath}", xmlPath);

        XmlDocument xmlDoc = LoadXml(xmlPath);
        return ImportProductHints(xmlDoc, xmlPath, customDbPath);
    }

    /// <summary>
    /// Updates or creates the nklibs.db database file from the online product hints published
    /// by Native Instruments. Existing entries that are not present online are preserved.
    /// </summary>
    public static async Task<NkxKeyDatabaseUpdateResult> UpdateFromOnlinePhintsAsync(
        string? customDbPath = null,
        CancellationToken cancellationToken = default)
    {
        byte[] payload;

        using (var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
        using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(120) })
        {
            // The static host only serves this document to Native Instruments clients:
            // requests with an unknown user agent (including a bare "NKXTool/1.0") get HTTP 403.
            client.DefaultRequestHeaders.UserAgent.ParseAdd(OnlineUserAgent);

            using var response = await client
                .GetAsync(OnlineSourceUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Downloading '{OnlineSourceUrl}' failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
            }

            long? declaredLength = response.Content.Headers.ContentLength;
            if (declaredLength.HasValue && declaredLength.Value > MaxOnlineDocumentBytes)
            {
                throw new InvalidDataException(
                    $"The document at '{OnlineSourceUrl}' is unexpectedly large ({declaredLength.Value:N0} bytes).");
            }

            using var networkStream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

            payload = await ReadBoundedAsync(networkStream, cancellationToken).ConfigureAwait(false);
        }

        XmlDocument xmlDoc = new() { XmlResolver = null };
        try
        {
            using var memoryStream = new MemoryStream(payload, writable: false);
            xmlDoc.Load(memoryStream);
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException(
                $"The document downloaded from '{OnlineSourceUrl}' is not valid XML: {ex.Message}", ex);
        }

        return ImportProductHints(xmlDoc, OnlineSourceUrl, customDbPath);
    }

    private static XmlDocument LoadXml(string path)
    {
        var xmlDoc = new XmlDocument { XmlResolver = null };
        xmlDoc.Load(path);
        return xmlDoc;
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream source, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[81920];
        int read;

        while ((read = await source.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxOnlineDocumentBytes)
            {
                throw new InvalidDataException(
                    $"The document at '{OnlineSourceUrl}' exceeds the {MaxOnlineDocumentBytes / (1024 * 1024)} MB limit.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Merges the &lt;Product&gt; nodes of a product hints document into the database file.
    /// Entries already present keep their keys unless the document provides newer ones, and
    /// entries absent from the document are never removed.
    /// </summary>
    private static NkxKeyDatabaseUpdateResult ImportProductHints(
        XmlDocument document,
        string source,
        string? customDbPath)
    {
        string targetDbPath = customDbPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DefaultDbFileName);
        NkxKeyDatabase database = Load(targetDbPath);

        int entriesBefore = database.Count;
        var existingSnpids = new HashSet<string>(database._bySnpid.Keys, StringComparer.OrdinalIgnoreCase);
        var countedSnpids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int productsScanned = 0;
        int productsSkipped = 0;
        int entriesAdded = 0;
        int entriesUpdated = 0;
        int entriesPreserved = 0;
        int aliasEntriesAdded = 0;

        var productNodes = document.SelectNodes("//Product");
        if (productNodes == null || productNodes.Count == 0)
            throw new InvalidDataException($"No <Product> entry found in '{source}'. Existing database left untouched.");

        foreach (XmlNode node in productNodes)
        {
            productsScanned++;

            string? snpid = node.SelectSingleNode("SNPID")?.InnerText?.Trim();
            XmlNode? productSpecific = node.SelectSingleNode("ProductSpecific");
            string? jdx = productSpecific?.SelectSingleNode("JDX")?.InnerText?.Trim();
            string? hu = productSpecific?.SelectSingleNode("HU")?.InnerText?.Trim();

            if (string.IsNullOrEmpty(snpid) || string.IsNullOrEmpty(jdx) || string.IsNullOrEmpty(hu))
            {
                productsSkipped++;
                continue;
            }

            string regKey = node.SelectSingleNode("RegKey")?.InnerText?.Trim() ?? "Unknown";
            string company = node.SelectSingleNode("Company")?.InnerText?.Trim() ?? "Unknown";

            foreach ((string id, bool isAlias) in BuildSnpidVariants(snpid))
            {
                var entry = new NkxKeyEntry
                {
                    Snpid = id,
                    JdxHex = jdx,
                    HuHex = hu,
                    RegKey = regKey,
                    Company = company
                };

                bool alreadyInSource = !countedSnpids.Add(id);
                if (!alreadyInSource)
                {
                    bool isNew = !existingSnpids.Contains(id);
                    if (isNew)
                    {
                        if (isAlias) aliasEntriesAdded++;
                        else entriesAdded++;
                    }
                    else
                    {
                        entriesUpdated++;
                    }
                }

                database.AddOrUpdate(entry);
            }
        }

        if (database.Count == 0)
            throw new InvalidDataException($"No usable key entry found in '{source}'. Existing database left untouched.");

        entriesPreserved = existingSnpids.Count - entriesUpdated;
        WriteDatabase(database, targetDbPath);

        return new NkxKeyDatabaseUpdateResult(
            Source: source,
            TargetPath: targetDbPath,
            ProductsScanned: productsScanned,
            ProductsSkipped: productsSkipped,
            EntriesAdded: entriesAdded,
            AliasEntriesAdded: aliasEntriesAdded,
            EntriesUpdated: entriesUpdated,
            EntriesPreserved: entriesPreserved < 0 ? 0 : entriesPreserved,
            TotalEntries: database.Count);
    }

    /// <summary>
    /// A 3-character SNPID is ambiguous: the archive may reference the decimal spelling or the
    /// hexadecimal spelling of the same number (e.g. "144" / "090"). Both are therefore stored,
    /// which is what the historical decimal/hex duplication in nklibs.db has always done.
    /// </summary>
    private static IEnumerable<(string Snpid, bool IsAlias)> BuildSnpidVariants(string snpid)
    {
        yield return (snpid, false);

        if (!Regex.IsMatch(snpid, @"^\d+$"))
            yield break;

        if (!int.TryParse(snpid, NumberStyles.None, CultureInfo.InvariantCulture, out int numericSnpid))
            yield break;

        string hexSnpid = numericSnpid.ToString("X3", CultureInfo.InvariantCulture);
        if (!string.Equals(hexSnpid, snpid, StringComparison.OrdinalIgnoreCase))
            yield return (hexSnpid, true);
    }

    private static void WriteDatabase(NkxKeyDatabase database, string targetDbPath)
    {
        string fullPath = Path.GetFullPath(targetDbPath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string tempPath = fullPath + ".tmp";
        try
        {
            File.WriteAllText(tempPath, database.Serialize(), Encoding.UTF8);
            File.Move(tempPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* best effort cleanup */ }
            }
        }
    }

    private string Serialize()
    {
        var sb = new StringBuilder();
        foreach (var entry in _bySnpid.Values)
        {
            sb.AppendLine($"[{entry.Snpid}]");
            sb.AppendLine($"JDX={entry.JdxHex}");
            sb.AppendLine($"HU={entry.HuHex}");
            sb.AppendLine($"RegKey={entry.RegKey}");
            sb.AppendLine($"Company={entry.Company}");
            sb.AppendLine();
        }
        return sb.ToString();
    }
}

/// <summary>
/// Outcome of a key database update, including merge statistics.
/// </summary>
public sealed record NkxKeyDatabaseUpdateResult(
    string Source,
    string TargetPath,
    int ProductsScanned,
    int ProductsSkipped,
    int EntriesAdded,
    int AliasEntriesAdded,
    int EntriesUpdated,
    int EntriesPreserved,
    int TotalEntries);

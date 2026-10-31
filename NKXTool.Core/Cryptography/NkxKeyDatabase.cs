namespace NKXTool.Core.Cryptography;

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
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
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
    /// Updates or creates the nklibs.db database file by parsing NativeAccess.xml.
    /// </summary>
    public static (int entryCount, int duplicatedCount, string targetPath) UpdateFromNativeAccessXml(
        string? customXmlPath = null,
        string? customDbPath = null)
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string targetDbPath = customDbPath ?? Path.Combine(baseDir, DefaultDbFileName);

        string xmlPath;
        if (!string.IsNullOrEmpty(customXmlPath))
        {
            xmlPath = Path.GetFullPath(customXmlPath);
        }
        else
        {
            string commonFiles = Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles);
            xmlPath = Path.Combine(commonFiles, "Native Instruments", "Service Center", "NativeAccess.xml");
        }

        if (!File.Exists(xmlPath))
            throw new FileNotFoundException($"NativeAccess.xml file not found at: {xmlPath}", xmlPath);

        var sb = new StringBuilder();
        int entryCount = 0;
        int duplicatedHexCount = 0;

        var xmlDoc = new XmlDocument();
        xmlDoc.Load(xmlPath);
        var productNodes = xmlDoc.SelectNodes("//Product");

        if (productNodes != null)
        {
            foreach (XmlNode node in productNodes)
            {
                string? snpid = node.SelectSingleNode("SNPID")?.InnerText?.Trim();
                XmlNode? psNode = node.SelectSingleNode("ProductSpecific");
                if (string.IsNullOrEmpty(snpid) || psNode == null) continue;

                string? jdx = psNode.SelectSingleNode("JDX")?.InnerText?.Trim();
                string? hu = psNode.SelectSingleNode("HU")?.InnerText?.Trim();
                string regKey = node.SelectSingleNode("RegKey")?.InnerText?.Trim() ?? "Unknown";
                string company = node.SelectSingleNode("Company")?.InnerText?.Trim() ?? "Unknown";

                if (string.IsNullOrEmpty(jdx) || string.IsNullOrEmpty(hu)) continue;

                void AppendEntry(string id)
                {
                    sb.AppendLine($"[{id}]");
                    sb.AppendLine($"JDX={jdx}");
                    sb.AppendLine($"HU={hu}");
                    sb.AppendLine($"RegKey={regKey}");
                    sb.AppendLine($"Company={company}");
                    sb.AppendLine();
                    entryCount++;
                }

                // 1. Raw original value
                AppendEntry(snpid);

                // 2. Hex duplicate if numeric
                if (Regex.IsMatch(snpid, @"^\d+$") && int.TryParse(snpid, out int snpidInt))
                {
                    string hexSnpid = snpidInt.ToString("X3");
                    if (!string.Equals(hexSnpid, snpid, StringComparison.OrdinalIgnoreCase))
                    {
                        AppendEntry(hexSnpid);
                        duplicatedHexCount++;
                    }
                }
            }
        }

        File.WriteAllText(targetDbPath, sb.ToString(), Encoding.UTF8);
        return (entryCount, duplicatedHexCount, targetDbPath);
    }
}

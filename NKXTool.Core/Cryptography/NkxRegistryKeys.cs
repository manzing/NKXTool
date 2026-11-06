namespace NKXTool.Core.Cryptography;

using Microsoft.Win32;

/// <summary>
/// Read-only view of the Kontakt library keys that Native Instruments stores under
/// <c>HKLM\SOFTWARE\Native Instruments\&lt;Library name&gt;</c>.
/// <para>
/// A library subkey carries <c>HU</c> (16 bytes, 32 hex chars) and <c>JDX</c> (32 bytes, 64 hex chars),
/// plus optionally <c>SNPID</c>, <c>RegKey</c>, <c>Company</c>, <c>ContentDir</c>, <c>InstallDir</c> and <c>Name</c>.
/// This is the first key source: an installed, working library necessarily carries its keys here,
/// so the registry is consulted before nklibs.db and nearby .nicnt files.
/// </para>
/// <para>
/// Both the 64-bit and the 32-bit (WOW6432Node) views are read, because the original 32-bit tooling writes
/// to the 32-bit view. The registry is only ever read, never written, and a missing key or denied access
/// simply yields an empty index.
/// </para>
/// </summary>
public static class NkxRegistryKeys
{
    /// <summary>Registry path below <c>HKLM</c> holding one subkey per Kontakt library.</summary>
    public const string RegistrySubKeyPath = @"SOFTWARE\Native Instruments";

    private static readonly Lazy<IReadOnlyDictionary<string, NkxKeyEntry>> Index =
        new(BuildIndex, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Number of lookup names (library names, content/install folder names, <c>Name</c> values, SNPID and RegKey)
    /// currently indexed. The registry is enumerated at most once per process.
    /// </summary>
    public static int Count => Index.Value.Count;

    /// <summary>
    /// Looks up a key entry by library name, content/install folder name, <c>Name</c> value, SNPID or RegKey.
    /// Comparison is case-insensitive. Returns <c>false</c> when the registry holds no usable entry.
    /// </summary>
    public static bool TryGet(string? libraryName, out NkxKeyEntry? entry)
    {
        if (string.IsNullOrWhiteSpace(libraryName))
        {
            entry = null;
            return false;
        }

        return Index.Value.TryGetValue(libraryName.Trim(), out entry);
    }

    /// <summary>
    /// Builds a key entry from the raw values of one library subkey.
    /// Returns <c>null</c> when HU/JDX are missing, not hexadecimal, or not 16/32 bytes long:
    /// a malformed key would silently corrupt decrypted data.
    /// </summary>
    public static NkxKeyEntry? CreateEntry(
        string? libraryName,
        string? huHex,
        string? jdxHex,
        string? snpid = null,
        string? regKey = null,
        string? company = null)
    {
        string hu = (huHex ?? string.Empty).Trim();
        string jdx = (jdxHex ?? string.Empty).Trim();
        if (!IsValidHex(hu, 16) || !IsValidHex(jdx, 32))
            return null;

        string name = libraryName?.Trim() ?? string.Empty;
        return new NkxKeyEntry
        {
            Snpid = snpid?.Trim() ?? string.Empty,
            JdxHex = jdx.ToUpperInvariant(),
            HuHex = hu.ToUpperInvariant(),
            RegKey = string.IsNullOrWhiteSpace(regKey) ? name : regKey.Trim(),
            Company = string.IsNullOrWhiteSpace(company) ? "Unknown" : company.Trim()
        };
    }

    /// <summary>
    /// Names under which one registry entry should be reachable, in priority order.
    /// Empty or duplicate candidates are dropped.
    /// </summary>
    public static IReadOnlyList<string> GetAliases(
        string? libraryName,
        NkxKeyEntry entry,
        string? contentDir = null,
        string? installDir = null,
        string? displayName = null)
    {
        var aliases = new List<string>(6);
        AddAlias(libraryName);
        AddAlias(entry.RegKey);
        AddAlias(entry.Snpid);
        AddAlias(LastPathSegment(contentDir));
        AddAlias(LastPathSegment(installDir));
        AddAlias(displayName);
        return aliases;

        void AddAlias(string? candidate)
        {
            string alias = candidate?.Trim() ?? string.Empty;
            if (alias.Length == 0) return;
            if (!aliases.Contains(alias, StringComparer.OrdinalIgnoreCase)) aliases.Add(alias);
        }
    }

    private static IReadOnlyDictionary<string, NkxKeyEntry> BuildIndex()
    {
        var index = new Dictionary<string, NkxKeyEntry>(StringComparer.OrdinalIgnoreCase);
        if (!OperatingSystem.IsWindows())
            return index;

        // 64-bit view first: it is the primary location, the 32-bit view only fills the gaps.
        ReadView(RegistryView.Registry64, index);
        ReadView(RegistryView.Registry32, index);
        return index;
    }

    private static void ReadView(RegistryView view, Dictionary<string, NkxKeyEntry> index)
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using RegistryKey? niKey = baseKey.OpenSubKey(RegistrySubKeyPath);
            if (niKey == null)
                return;

            foreach (string subKeyName in niKey.GetSubKeyNames())
            {
                try
                {
                    using RegistryKey? libraryKey = niKey.OpenSubKey(subKeyName);
                    if (libraryKey == null)
                        continue;

                    var entry = CreateEntry(
                        subKeyName,
                        ReadString(libraryKey, "HU"),
                        ReadString(libraryKey, "JDX"),
                        ReadString(libraryKey, "SNPID"),
                        ReadString(libraryKey, "RegKey"),
                        ReadString(libraryKey, "Company"));
                    if (entry == null)
                        continue;

                    foreach (string alias in GetAliases(
                                 subKeyName,
                                 entry,
                                 ReadString(libraryKey, "ContentDir"),
                                 ReadString(libraryKey, "InstallDir"),
                                 ReadString(libraryKey, "Name")))
                    {
                        // First writer wins: the 64-bit view and the library name take precedence.
                        index.TryAdd(alias, entry);
                    }
                }
                catch (Exception)
                {
                    // One unreadable or oddly typed subkey must not hide the other libraries.
                }
            }
        }
        catch (Exception)
        {
            // No Native Instruments key, access denied, or unsupported platform: no registry keys available.
        }
    }

    private static string? ReadString(RegistryKey key, string valueName)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            // Registry values are not always strings (Visibility is a DWORD, for instance).
            return key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsValidHex(string value, int byteCount)
    {
        if (value.Length != byteCount * 2)
            return false;

        foreach (char c in value)
        {
            if (!char.IsAsciiHexDigit(c))
                return false;
        }

        return true;
    }

    private static string? LastPathSegment(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        string trimmed = path.Trim().TrimEnd('\\', '/');
        int separator = trimmed.LastIndexOfAny(['\\', '/']);
        return separator >= 0 ? trimmed[(separator + 1)..] : trimmed;
    }
}

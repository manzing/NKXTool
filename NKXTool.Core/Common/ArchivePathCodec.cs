namespace NKXTool.Core.Common;

using System.Text;

/// <summary>
/// Encodes archive entry names into names Windows can store, and decodes them back.
///
/// A library built on macOS may hold samples whose name contains a character Windows forbids - APFS
/// allows "*", so the Swing library ships entries such as
/// "Samples\Jazz Drums\Fills\100bpm\XC\sam_drums_loop_100bpm_breaks_short_14_xc*.ncw". No Windows
/// API can create that file, even under the extended-length prefix, so an extraction writes an
/// encoded name instead: every forbidden character becomes "%" followed by its two hexadecimal
/// digits ("*" to "%2A"), and a literal "%" becomes "%25" so that decoding stays exact. Packing
/// decodes every name it finds, so the container keeps the exact names the instrument patches
/// reference, whatever folder is packed and wherever that folder was moved to.
/// </summary>
public static class ArchivePathCodec
{
    /// <summary>Device names Windows reserves, with or without an extension.</summary>
    private static readonly string[] ReservedDeviceNames =
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>
    /// Returns the name to write on disk for an archive entry. Both separators are kept as they are;
    /// the encoding only touches the characters a single path segment cannot hold.
    /// </summary>
    public static string Encode(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return relativePath;

        var encoded = new StringBuilder(relativePath.Length + 8);
        int segmentStart = 0;

        for (int i = 0; i < relativePath.Length; i++)
        {
            char c = relativePath[i];

            if (c is '\\' or '/')
            {
                EscapeReservedDeviceName(encoded, segmentStart);
                encoded.Append(c);
                segmentStart = encoded.Length;
                continue;
            }

            if (NeedsEscape(c))
            {
                encoded.Append('%').Append(((int)c).ToString("X2"));
                continue;
            }

            encoded.Append(c);
        }

        EscapeReservedDeviceName(encoded, segmentStart);
        return encoded.ToString();
    }

    /// <summary>
    /// Returns the archive name stored by <see cref="Encode"/>. A "%" that is not followed by two
    /// hexadecimal digits is left untouched, so a name this tool did not write is never mangled.
    /// </summary>
    public static string Decode(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return relativePath;

        var decoded = new StringBuilder(relativePath.Length);

        for (int i = 0; i < relativePath.Length; i++)
        {
            char c = relativePath[i];

            if (c == '%' &&
                i + 2 < relativePath.Length &&
                TryParseHex(relativePath[i + 1], out int high) &&
                TryParseHex(relativePath[i + 2], out int low))
            {
                decoded.Append((char)((high << 4) | low));
                i += 2;
                continue;
            }

            decoded.Append(c);
        }

        return decoded.ToString();
    }

    /// <summary>
    /// True when the name cannot be written as it is on Windows and would be encoded.
    /// </summary>
    public static bool NeedsEncoding(string relativePath) =>
        !string.Equals(Encode(relativePath), relativePath, StringComparison.Ordinal);

    private static bool NeedsEscape(char c) =>
        c < 0x20 || c is '<' or '>' or ':' or '"' or '|' or '?' or '*' or '%';

    private static bool TryParseHex(char c, out int value)
    {
        if (c >= '0' && c <= '9') { value = c - '0'; return true; }
        if (c >= 'A' && c <= 'F') { value = c - 'A' + 10; return true; }
        if (c >= 'a' && c <= 'f') { value = c - 'a' + 10; return true; }

        value = 0;
        return false;
    }

    /// <summary>
    /// A segment whose stem is a reserved device name cannot be created whatever its extension, so
    /// its first character is escaped: "CON.ncw" is written "%43ON.ncw" and decoded back to "CON.ncw".
    /// </summary>
    private static void EscapeReservedDeviceName(StringBuilder encoded, int start)
    {
        int length = encoded.Length - start;
        if (length <= 0) return;

        string segment = encoded.ToString(start, length);
        int dot = segment.IndexOf('.');
        string stem = (dot < 0 ? segment : segment[..dot]).TrimEnd(' ');

        foreach (string reserved in ReservedDeviceNames)
        {
            if (!stem.Equals(reserved, StringComparison.OrdinalIgnoreCase)) continue;

            char first = encoded[start];
            encoded.Remove(start, 1);
            encoded.Insert(start, '%' + ((int)first).ToString("X2"));
            return;
        }
    }
}

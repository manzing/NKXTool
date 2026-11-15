namespace NKXTool.Core.Common;

using System.Text;

/// <summary>
/// Maps archive entry names to names Windows can actually store.
///
/// A library built on macOS may hold samples whose name contains a character Windows forbids -
/// APFS allows "*", so the Swing library ships entries such as
/// "Samples\Jazz Drums\Fills\100bpm\XC\sam_drums_loop_100bpm_breaks_short_14_xc*.ncw". The payload
/// is perfectly valid, but no Windows API can create that file: NTFS rejects the name, even under
/// the extended-length prefix used by <see cref="ExtendedPath"/>, and the write fails with
/// "The filename, directory name, or volume label syntax is incorrect". Such characters are
/// therefore replaced, as archivers do on Windows, so that the entry can still be extracted.
/// </summary>
public static class ArchivePathSanitizer
{
    /// <summary>Character written in place of one Windows cannot store.</summary>
    public const char DefaultReplacement = '_';

    /// <summary>Device names Windows reserves, with or without an extension.</summary>
    private static readonly string[] ReservedDeviceNames =
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>
    /// Returns the relative path unchanged when every segment of it can be created on Windows, and
    /// its mapped form otherwise. Both separators are kept as they are.
    /// </summary>
    public static string ToWindowsSafeRelativePath(string relativePath, char replacement = DefaultReplacement)
    {
        if (string.IsNullOrEmpty(relativePath)) return relativePath;

        var mapped = new StringBuilder(relativePath.Length + 8);
        int segmentStart = 0;

        for (int i = 0; i < relativePath.Length; i++)
        {
            char c = relativePath[i];

            if (c is '\\' or '/')
            {
                FinishSegment(mapped, segmentStart, replacement);
                mapped.Append(c);
                segmentStart = mapped.Length;
                continue;
            }

            mapped.Append(IsForbidden(c) ? replacement : c);
        }

        FinishSegment(mapped, segmentStart, replacement);
        return mapped.ToString();
    }

    /// <summary>
    /// True when the relative path can be written as it is on Windows.
    /// </summary>
    public static bool IsWindowsSafe(string relativePath) =>
        string.Equals(ToWindowsSafeRelativePath(relativePath), relativePath, StringComparison.Ordinal);

    private static bool IsForbidden(char c) =>
        c < 0x20 || c is '<' or '>' or ':' or '"' or '|' or '?' or '*';

    /// <summary>
    /// Applies the rules that depend on the segment as a whole: a name whose stem is a reserved
    /// device name ("CON.ncw" is as unusable as "CON") is prefixed with the replacement character.
    /// </summary>
    private static void FinishSegment(StringBuilder mapped, int start, char replacement)
    {
        int length = mapped.Length - start;
        if (length <= 0) return;

        string segment = mapped.ToString(start, length);
        int dot = segment.IndexOf('.');
        string stem = (dot < 0 ? segment : segment[..dot]).TrimEnd(' ');

        foreach (string reserved in ReservedDeviceNames)
        {
            if (stem.Equals(reserved, StringComparison.OrdinalIgnoreCase))
            {
                mapped.Insert(start, replacement);
                return;
            }
        }
    }
}

namespace NKXTool.Core.Common;

/// <summary>
/// Windows strips trailing spaces and dots from a path segment as part of Win32 normalization.
/// A library whose entries are named "Samples\\_Attack Converge \\x.ncw" therefore cannot be
/// extracted with the usual APIs: the folder is created as "_Attack Converge" and the write then
/// fails with "Could not find a part of the path". Prefixing the path with "\\?\" disables that
/// normalization, so the name is written to disk verbatim - which is also what the instrument
/// patch files expect, since they reference the stored names exactly.
/// </summary>
public static class ExtendedPath
{
    private const string Prefix = @"\\?\";
    private const string UncPrefix = @"\\?\UNC\";

    /// <summary>
    /// Returns the extended-length form of an absolute path on Windows and the path unchanged
    /// elsewhere. Relative paths are made absolute first, because the prefix requires it.
    /// </summary>
    public static string ForFileSystem(string path)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(path)) return path;

        if (path.StartsWith(UncPrefix, StringComparison.Ordinal) || path.StartsWith(Prefix, StringComparison.Ordinal))
            return path;

        // Path.GetFullPath trims a trailing space or dot, which is exactly the name that must be
        // preserved, so the path is completed with a dummy segment that is removed afterwards: the
        // trailing space then sits inside the path and survives normalization.
        if (path.Length <= 3) return path; // drive root: nothing to normalize

        string probe = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                       + Path.DirectorySeparatorChar + "x";
        string fullProbe = Path.GetFullPath(probe);
        string full = fullProbe[..^2];

        // UNC paths ("\\server\share") use the dedicated \\?\UNC\ form.
        if (full.StartsWith(@"\\", StringComparison.Ordinal))
            return UncPrefix + full[2..];

        return Prefix + full;
    }

    public static void CreateDirectory(string path) => Directory.CreateDirectory(ForFileSystem(path));

    public static bool Exists(string path) => File.Exists(ForFileSystem(path));

    public static bool DirectoryExists(string path) => Directory.Exists(ForFileSystem(path));

    public static void WriteAllBytes(string path, byte[] bytes) => File.WriteAllBytes(ForFileSystem(path), bytes);

    public static Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default) =>
        File.WriteAllBytesAsync(ForFileSystem(path), bytes, cancellationToken);

    public static void DeleteDirectory(string path, bool recursive = false) =>
        Directory.Delete(ForFileSystem(path), recursive);

    public static FileStream OpenWrite(string path) => OpenWrite(path, 64 * 1024, asynchronous: false);

    public static FileStream OpenWrite(string path, int bufferSize) => OpenWrite(path, bufferSize, asynchronous: false);

    public static FileStream OpenWrite(string path, int bufferSize, bool asynchronous) =>
        new(
            ForFileSystem(path),
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize,
            asynchronous ? FileOptions.Asynchronous : FileOptions.None);
}

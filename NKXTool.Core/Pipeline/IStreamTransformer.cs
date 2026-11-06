namespace NKXTool.Core.Pipeline;

using NKXTool.Core.Common;

/// <summary>
/// Defines an extensible stream transformation step in an extraction or conversion pipeline
/// (e.g., Decryption, NCW decompressor via conncw-NG, WAV to FLAC encoder, etc.).
/// </summary>
public interface IStreamTransformer
{
    /// <summary>
    /// Checks if this transformer applies to the given archive entry (e.g. file ends with .ncw).
    /// </summary>
    bool CanTransform(ArchiveEntry entry);

    /// <summary>
    /// Transforms the source stream into an output stream, with an optional modified target file name.
    /// </summary>
    /// <param name="entry">Archive metadata entry.</param>
    /// <param name="sourceStream">Raw data stream from archive.</param>
    /// <param name="transformedPath">New target path if format is converted (e.g. .ncw -> .wav or .flac).</param>
    /// <returns>Transformed stream ready for writing to disk.</returns>
    Stream Transform(ArchiveEntry entry, Stream sourceStream, out string transformedPath);
}

/// <summary>
/// Default 1:1 pass-through transformer that leaves streams untouched.
/// </summary>
public sealed class PassThroughTransformer : IStreamTransformer
{
    public static readonly PassThroughTransformer Instance = new();

    public bool CanTransform(ArchiveEntry entry) => true;

    public Stream Transform(ArchiveEntry entry, Stream sourceStream, out string transformedPath)
    {
        transformedPath = entry.RelativePath;
        return sourceStream;
    }
}

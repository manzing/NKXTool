namespace NKXTool.Core.Containers.Nicnt;

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Native generator and packer for Native Instruments NICNT monolith containers.
/// Produces 100% compliant NI FC MTD / NI FC TOC monolith structures with embedded XML and resource payload.
/// </summary>
public static class NicntArchiveWriter
{
    private const string NicntMtdMagic = "/\\ NI FC MTD  /\\";
    private const string NicntTocMagic = "/\\ NI FC TOC  /\\";
    private const int TocRecordSize = 0x280; // 640 bytes per entry
    private const int MtdPartitionSize = 512256; // 0x7D100
    private const int TocHeaderOffset = 512544;  // 0x7D220
    private const int ResourcePayloadHeaderOffset = 0x260;

    /// <summary>
    /// Packs an XML ProductHints document and associated resource files into a standard NICNT monolith container.
    /// </summary>
    public static async Task PackAsync(
        string outputNicntPath,
        string xmlContent,
        string? version = "1.0.0",
        string? resourcesDirectory = null,
        CancellationToken cancellationToken = default)
    {
        string fullPath = Path.GetFullPath(outputNicntPath);
        string? dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // 1. Collect resources
        var resourceFiles = new List<(string relName, string diskPath, long size)>();
        if (!string.IsNullOrEmpty(resourcesDirectory) && Directory.Exists(resourcesDirectory))
        {
            foreach (var f in Directory.GetFiles(resourcesDirectory, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(resourcesDirectory, f);
                // Strip "Resources" prefix if already present in the folder structure
                if (rel.StartsWith("Resources\\", StringComparison.OrdinalIgnoreCase) ||
                    rel.StartsWith("Resources/", StringComparison.OrdinalIgnoreCase))
                {
                    rel = rel.Substring("Resources/".Length);
                }

                // Restore [pipe] -> '|' in filename for NI compliance if needed
                string rawName = rel.Replace("[pipe]", "|");
                resourceFiles.Add((rawName, f, new FileInfo(f).Length));
            }
        }

        // If no resources, write simple/pure XML format or minimal monolith
        using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous);

        // Header 1: NI FC MTD
        byte[] mtdMagicBytes = Encoding.ASCII.GetBytes(NicntMtdMagic);
        await fs.WriteAsync(mtdMagicBytes, cancellationToken).ConfigureAwait(false);

        // Pad up to 0x42
        byte[] padTo42 = new byte[0x42 - mtdMagicBytes.Length];
        await fs.WriteAsync(padTo42, cancellationToken).ConfigureAwait(false);

        // Version string at 0x42 in UTF-16
        string ver = string.IsNullOrWhiteSpace(version) ? "1.0.0" : version;
        byte[] verBytes = Encoding.Unicode.GetBytes(ver + "\0");
        await fs.WriteAsync(verBytes, cancellationToken).ConfigureAwait(false);

        // Pad up to 0x100
        int curPos = 0x42 + verBytes.Length;
        if (curPos < 0x100)
        {
            byte[] padTo100 = new byte[0x100 - curPos];
            await fs.WriteAsync(padTo100, cancellationToken).ConfigureAwait(false);
        }

        // Write XML text at 0x100
        byte[] xmlBytes = Encoding.UTF8.GetBytes(xmlContent);
        await fs.WriteAsync(xmlBytes, cancellationToken).ConfigureAwait(false);

        // Pad up to 512256 (0x7D100)
        curPos = 0x100 + xmlBytes.Length;
        if (curPos < MtdPartitionSize)
        {
            byte[] padToMtd2 = new byte[MtdPartitionSize - curPos];
            await fs.WriteAsync(padToMtd2, cancellationToken).ConfigureAwait(false);
        }

        // If there are no resources, container ends here
        if (resourceFiles.Count == 0)
        {
            await fs.FlushAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        // Header 2: NI FC MTD
        await fs.WriteAsync(mtdMagicBytes, cancellationToken).ConfigureAwait(false);

        // Pad up to TocHeaderOffset (512544)
        curPos = MtdPartitionSize + mtdMagicBytes.Length;
        if (curPos < TocHeaderOffset)
        {
            byte[] padToToc = new byte[TocHeaderOffset - curPos];
            await fs.WriteAsync(padToToc, cancellationToken).ConfigureAwait(false);
        }

        // TOC 1: NI FC TOC
        byte[] tocMagicBytes = Encoding.ASCII.GetBytes(NicntTocMagic);
        await fs.WriteAsync(tocMagicBytes, cancellationToken).ConfigureAwait(false);

        // Pad to start of records at +0x268
        byte[] padToRec1 = new byte[0x268 - tocMagicBytes.Length];
        await fs.WriteAsync(padToRec1, cancellationToken).ConfigureAwait(false);

        // Write TOC records (640 bytes each)
        long runningPayloadRelOffset = 0;
        var recordList = new List<(long relOffset, int id, string name, string diskPath, long size)>();
        int idCounter = 0;

        foreach (var rf in resourceFiles)
        {
            recordList.Add((runningPayloadRelOffset, idCounter++, rf.relName, rf.diskPath, rf.size));
            runningPayloadRelOffset += rf.size;
        }

        foreach (var rec in recordList)
        {
            byte[] recBuf = new byte[TocRecordSize];
            BitConverter.GetBytes(rec.relOffset).CopyTo(recBuf, 0);
            BitConverter.GetBytes(rec.id).CopyTo(recBuf, 8);

            byte[] nameBytes = Encoding.Unicode.GetBytes(rec.name + "\0");
            int copyLen = Math.Min(nameBytes.Length, TocRecordSize - 24);
            Array.Copy(nameBytes, 0, recBuf, 24, copyLen);

            await fs.WriteAsync(recBuf, cancellationToken).ConfigureAwait(false);
        }

        // Pad to 16-byte boundary or at least 24 bytes before TOC 2
        byte[] toc1Pad = new byte[24];
        await fs.WriteAsync(toc1Pad, cancellationToken).ConfigureAwait(false);

        // TOC 2: NI FC TOC
        await fs.WriteAsync(tocMagicBytes, cancellationToken).ConfigureAwait(false);

        // Pad up to ResourcePayloadHeaderOffset (0x260)
        byte[] padPayloadHdr = new byte[ResourcePayloadHeaderOffset - tocMagicBytes.Length];
        await fs.WriteAsync(padPayloadHdr, cancellationToken).ConfigureAwait(false);

        // Write resource payload contents
        foreach (var rec in recordList)
        {
            using var inStream = new FileStream(rec.diskPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024);
            await inStream.CopyToAsync(fs, cancellationToken).ConfigureAwait(false);
        }

        await fs.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}

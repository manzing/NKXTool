using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace NkiTool
{
    /// <summary>
    /// Représente une région de données identifiée dans le fichier NKI :
    /// soit un chunk brut (tag + taille + payload), soit un bloc obtenu
    /// après décompression ZLIB d'un chunk parent.
    /// </summary>
    public class NkiRegion
    {
        public string Tag = "";
        public long Offset;
        public int Size;
        public byte[] Payload = Array.Empty<byte>();
        public bool IsInflated;
        public List<NkiRegion> Children = new();
    }

    /// <summary>
    /// Scanner générique et défensif : il ne présuppose PAS la structure exacte
    /// du format NKI (tags, tailles de champs). Il tente une lecture de type
    /// "conteneur de chunks" (tag ASCII 4 octets + taille UInt32 LE + payload),
    /// et détecte automatiquement les blocs ZLIB imbriqués pour les décompresser
    /// et les ré-analyser récursivement. Le but est de VALIDER la structure
    /// réelle sur vos fichiers avant d'écrire la logique de patch.
    /// </summary>
    public static class NkiChunkScanner
    {
        public static List<NkiRegion> Scan(byte[] data)
        {
            var regions = new List<NkiRegion>();
            TryScanAsChunks(data, 0, data.Length, regions);
            return regions;
        }

        private static void TryScanAsChunks(byte[] data, int start, int end, List<NkiRegion> outRegions)
        {
            int pos = start;
            while (pos + 8 <= end)
            {
                string tag = SafeAscii(data, pos, 4);
                uint size = BitConverter.ToUInt32(data, pos + 4);

                // Garde-fou : si la taille annoncée est aberrante, on abandonne
                // l'hypothèse "chunk" à partir d'ici et on traite le reste comme
                // un bloc opaque (permet de ne pas planter sur un format différent).
                if (size == 0 || pos + 8 + size > end || size > 200_000_000)
                {
                    var opaque = new NkiRegion
                    {
                        Tag = "RAW",
                        Offset = pos,
                        Size = end - pos,
                        Payload = Slice(data, pos, end - pos)
                    };
                    outRegions.Add(opaque);
                    return;
                }

                var region = new NkiRegion
                {
                    Tag = tag,
                    Offset = pos,
                    Size = (int)size,
                    Payload = Slice(data, pos + 8, (int)size)
                };

                TryInflateAndRecurse(region);
                outRegions.Add(region);

                pos += 8 + (int)size;
            }

            if (pos < end)
            {
                outRegions.Add(new NkiRegion
                {
                    Tag = "TAIL",
                    Offset = pos,
                    Size = end - pos,
                    Payload = Slice(data, pos, end - pos)
                });
            }
        }

        private static void TryInflateAndRecurse(NkiRegion region)
        {
            var inflated = TryZlibInflate(region.Payload);
            if (inflated != null)
            {
                region.IsInflated = true;
                TryScanAsChunks(inflated, 0, inflated.Length, region.Children);
                // Remplace le payload affiché par la version décompressée pour
                // que le scan de chaînes (StringScanner) l'exploite aussi.
                region.Payload = inflated;
            }
        }

        /// <summary>
        /// Tente une décompression ZLIB à partir de n'importe quel offset où
        /// l'en-tête ZLIB (0x78 ..) est détecté, pas uniquement au début du buffer.
        /// </summary>
        public static byte[]? TryZlibInflate(byte[] buffer)
        {
            for (int offset = 0; offset < Math.Min(buffer.Length, 16); offset++)
            {
                if (buffer[offset] != 0x78) continue;

                try
                {
                    using var input = new MemoryStream(buffer, offset, buffer.Length - offset);
                    using var zlib = new ZLibStream(input, CompressionMode.Decompress);
                    using var output = new MemoryStream();
                    zlib.CopyTo(output);
                    var result = output.ToArray();
                    if (result.Length > 0) return result;
                }
                catch
                {
                    // Pas un flux ZLIB valide à cet offset, on continue.
                }
            }
            return null;
        }

        private static string SafeAscii(byte[] data, int offset, int len)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < len; i++)
            {
                byte b = data[offset + i];
                sb.Append(b >= 32 && b < 127 ? (char)b : '.');
            }
            return sb.ToString();
        }

        private static byte[] Slice(byte[] data, int offset, int len)
        {
            var result = new byte[len];
            Array.Copy(data, offset, result, 0, len);
            return result;
        }
    }
}

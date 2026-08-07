using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace NkiTool
{
    public class NkiRegion
    {
        public string Tag = "";
        public long Offset;
        public int Size;
        public byte[] Payload = Array.Empty<byte>();
        public bool IsInflated;
        public List<NkiRegion> Children = new();
        public List<long> ZlibCandidateOffsets = new();
    }

    public static class NkiChunkScanner
    {
        // Fenêtre de recherche d'en-tête ZLIB (0x78 ..) à l'intérieur d'un buffer.
        // 234 Ko de fichier -> on peut se permettre de scanner large sans souci de perf.
        private const int ZlibSearchWindow = 1_000_000;

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

                if (size == 0 || pos + 8 + size > end || size > 200_000_000)
                {
                    var opaque = new NkiRegion
                    {
                        Tag = "RAW",
                        Offset = pos,
                        Size = end - pos,
                        Payload = Slice(data, pos, end - pos)
                    };

                    // Correctif : on tente quand même de trouver et décompresser
                    // un flux ZLIB n'importe où dans ce bloc opaque, plutôt que
                    // d'abandonner silencieusement.
                    TryInflateAndRecurse(opaque);
                    ScanForKnownMarkers(opaque);

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
                var tail = new NkiRegion
                {
                    Tag = "TAIL",
                    Offset = pos,
                    Size = end - pos,
                    Payload = Slice(data, pos, end - pos)
                };
                TryInflateAndRecurse(tail);
                outRegions.Add(tail);
            }
        }

        private static void TryInflateAndRecurse(NkiRegion region)
        {
            var (inflated, foundAtOffset) = TryZlibInflateAnywhere(region.Payload);
            if (inflated != null)
            {
                region.IsInflated = true;
                region.ZlibCandidateOffsets.Add(region.Offset + foundAtOffset);
                TryScanAsChunks(inflated, 0, inflated.Length, region.Children);
                region.Payload = inflated;
            }
        }

        /// <summary>
        /// Recherche un en-tête ZLIB (0x78 suivi d'un second octet plausible)
        /// n'importe où dans le buffer (pas seulement au début), sur une fenêtre
        /// raisonnable, et tente une décompression à chaque candidat trouvé.
        /// </summary>
        public static (byte[]? data, int offset) TryZlibInflateAnywhere(byte[] buffer)
        {
            int limit = Math.Min(buffer.Length - 2, ZlibSearchWindow);
            for (int offset = 0; offset < limit; offset++)
            {
                if (buffer[offset] != 0x78) continue;

                byte second = buffer[offset + 1];
                // Bytes valides usuels après 0x78 pour un flux zlib : 0x01, 0x5E, 0x9C, 0xDA
                if (second != 0x01 && second != 0x5E && second != 0x9C && second != 0xDA) continue;

                try
                {
                    using var input = new MemoryStream(buffer, offset, buffer.Length - offset);
                    using var zlib = new ZLibStream(input, CompressionMode.Decompress);
                    using var output = new MemoryStream();
                    zlib.CopyTo(output);
                    var result = output.ToArray();
                    if (result.Length > 0) return (result, offset);
                }
                catch
                {
                    // pas un flux valide à cet offset, on continue
                }
            }
            return (null, -1);
        }

        private static readonly string[] KnownMarkers = { "hsin", "DSIN", "2SAM", "PRES", "PROG", "PLST", "FNTB", "PARS" };

        /// <summary>
        /// Recherche des tags/marqueurs connus (issus de la documentation
        /// communautaire du format NI DSIN) n'importe où dans un bloc, pour
        /// aider à localiser la structure même sans specs officielles.
        /// </summary>
        private static void ScanForKnownMarkers(NkiRegion region)
        {
            foreach (var marker in KnownMarkers)
            {
                var markerBytes = Encoding.ASCII.GetBytes(marker);
                for (int i = 0; i + markerBytes.Length <= region.Payload.Length; i++)
                {
                    bool match = true;
                    for (int j = 0; j < markerBytes.Length; j++)
                    {
                        if (region.Payload[i + j] != markerBytes[j]) { match = false; break; }
                    }
                    if (match)
                    {
                        region.ZlibCandidateOffsets.Add(-(region.Offset + i)); // négatif = marqueur texte, pas zlib
                    }
                }
            }
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

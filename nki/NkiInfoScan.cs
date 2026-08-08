using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace NkiTool
{
    public static class NkiVersionScan
    {
        private static readonly Regex VersionPattern =
            new Regex(@"\b\d{1,3}\.\d{1,3}(\.\d{1,5}){0,2}\b", RegexOptions.Compiled);

        public static int Run(string path, int scanLength, string? outPath)
        {
            byte[] data = File.ReadAllBytes(path);
            int limit = Math.Min(scanLength, data.Length);
            var sb = new StringBuilder();

            sb.AppendLine($"Fichier: {path}");
            sb.AppendLine($"Taille totale: {data.Length} octets");
            sb.AppendLine($"Zone scannée: 0 à {limit} (sur {scanLength} demandés)");
            sb.AppendLine();

            sb.AppendLine("=== Chaînes candidates de version (ASCII) ===");
            var asciiHits = ScanAscii(data, limit);
            foreach (var hit in asciiHits)
                sb.AppendLine($"  offset={hit.Offset,-6} valeur=\"{hit.Value}\"  (contexte: \"{hit.Context}\")");
            if (asciiHits.Count == 0) sb.AppendLine("  (aucune)");

            sb.AppendLine();
            sb.AppendLine("=== Chaînes candidates de version (UTF-16LE) ===");
            var utf16Hits = ScanUtf16Le(data, limit);
            foreach (var hit in utf16Hits)
                sb.AppendLine($"  offset={hit.Offset,-6} valeur=\"{hit.Value}\"  (contexte: \"{hit.Context}\")");
            if (utf16Hits.Count == 0) sb.AppendLine("  (aucune)");

            sb.AppendLine();
            sb.AppendLine("=== Toutes les chaînes lisibles trouvées (>=4 caractères), pour contexte manuel ===");
            foreach (var s in ExtractAllPrintableStrings(data, limit, "ASCII"))
                sb.AppendLine($"  [ASCII]   offset={s.Offset,-6} \"{s.Value}\"");
            foreach (var s in ExtractAllPrintableStrings(data, limit, "UTF16LE"))
                sb.AppendLine($"  [UTF16LE] offset={s.Offset,-6} \"{s.Value}\"");

            string report = sb.ToString();
            Console.WriteLine(report);

            if (outPath != null)
            {
                File.WriteAllText(outPath, report);
                Console.WriteLine($"\nRapport écrit dans: {outPath}");
            }

            return 0;
        }

        private record Hit(long Offset, string Value, string Context);

        private static List<Hit> ScanAscii(byte[] data, int limit)
        {
            var hits = new List<Hit>();
            var sb = new StringBuilder();
            long start = 0;

            for (int i = 0; i < limit; i++)
            {
                byte b = data[i];
                bool printable = b >= 32 && b < 127;
                if (printable)
                {
                    if (sb.Length == 0) start = i;
                    sb.Append((char)b);
                }
                else
                {
                    FlushAsciiCandidate(sb, start, hits);
                    sb.Clear();
                }
            }
            FlushAsciiCandidate(sb, start, hits);
            return hits;
        }

        private static void FlushAsciiCandidate(StringBuilder sb, long start, List<Hit> hits)
        {
            if (sb.Length < 3) return;
            string s = sb.ToString();
            foreach (Match m in VersionPattern.Matches(s))
            {
                hits.Add(new Hit(start + m.Index, m.Value, s));
            }
        }

        private static List<Hit> ScanUtf16Le(byte[] data, int limit)
        {
            var hits = new List<Hit>();
            var sb = new StringBuilder();
            long start = 0;
            int i = 0;

            while (i + 1 < limit)
            {
                char c = (char)(data[i] | (data[i + 1] << 8));
                bool printable = c >= 32 && c < 127;
                if (printable)
                {
                    if (sb.Length == 0) start = i;
                    sb.Append(c);
                    i += 2;
                }
                else
                {
                    FlushUtf16Candidate(sb, start, hits);
                    sb.Clear();
                    i += 1;
                }
            }
            FlushUtf16Candidate(sb, start, hits);
            return hits;
        }

        private static void FlushUtf16Candidate(StringBuilder sb, long start, List<Hit> hits)
        {
            if (sb.Length < 3) return;
            string s = sb.ToString();
            foreach (Match m in VersionPattern.Matches(s))
            {
                hits.Add(new Hit(start + m.Index * 2, m.Value, s));
            }
        }

        private record StringHit(long Offset, string Value);

        private static List<StringHit> ExtractAllPrintableStrings(byte[] data, int limit, string encoding)
        {
            var results = new List<StringHit>();
            var sb = new StringBuilder();
            long start = 0;

            if (encoding == "ASCII")
            {
                for (int i = 0; i < limit; i++)
                {
                    byte b = data[i];
                    bool printable = b >= 32 && b < 127;
                    if (printable)
                    {
                        if (sb.Length == 0) start = i;
                        sb.Append((char)b);
                    }
                    else
                    {
                        if (sb.Length >= 4) results.Add(new StringHit(start, sb.ToString()));
                        sb.Clear();
                    }
                }
                if (sb.Length >= 4) results.Add(new StringHit(start, sb.ToString()));
            }
            else
            {
                int i = 0;
                while (i + 1 < limit)
                {
                    char c = (char)(data[i] | (data[i + 1] << 8));
                    bool printable = c >= 32 && c < 127;
                    if (printable)
                    {
                        if (sb.Length == 0) start = i;
                        sb.Append(c);
                        i += 2;
                    }
                    else
                    {
                        if (sb.Length >= 4) results.Add(new StringHit(start, sb.ToString()));
                        sb.Clear();
                        i += 1;
                    }
                }
                if (sb.Length >= 4) results.Add(new StringHit(start, sb.ToString()));
            }

            return results;
        }
    }
}
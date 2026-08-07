using System;
using System.Collections.Generic;
using System.Text;

namespace NkiTool
{
    public record FoundString(long Offset, string Encoding, string Value);

    /// <summary>
    /// Extrait toutes les chaînes plausibles (ASCII et UTF-16LE) contenant
    /// ".wav" ou ".ncw" dans un buffer, avec leur offset absolu.
    /// Objectif : localiser empiriquement, sur vos vrais fichiers, où et
    /// comment sont stockés les noms d'échantillons (encodage, longueur de
    /// champ fixe ou variable, présence d'un chemin complet ou du nom seul).
    /// </summary>
    public static class StringExtractor
    {
        private static readonly string[] Needles = { ".wav", ".WAV", ".ncw", ".NCW" };

        public static List<FoundString> FindSampleReferences(byte[] data, long baseOffset = 0)
        {
            var results = new List<FoundString>();
            ScanAscii(data, baseOffset, results);
            ScanUtf16Le(data, baseOffset, results);
            return results;
        }

        private static void ScanAscii(byte[] data, long baseOffset, List<FoundString> results)
        {
            var sb = new StringBuilder();
            long stringStart = 0;
            for (int i = 0; i < data.Length; i++)
            {
                byte b = data[i];
                if (b >= 32 && b < 127)
                {
                    if (sb.Length == 0) stringStart = i;
                    sb.Append((char)b);
                }
                else
                {
                    FlushIfMatch(sb, stringStart, baseOffset, "ASCII", results);
                    sb.Clear();
                }
            }
            FlushIfMatch(sb, stringStart, baseOffset, "ASCII", results);
        }

        private static void ScanUtf16Le(byte[] data, long baseOffset, List<FoundString> results)
        {
            var sb = new StringBuilder();
            long stringStart = 0;
            int i = 0;
            while (i + 1 < data.Length)
            {
                char c = (char)(data[i] | (data[i + 1] << 8));
                bool printable = c >= 32 && c < 127;
                if (printable)
                {
                    if (sb.Length == 0) stringStart = i;
                    sb.Append(c);
                    i += 2;
                }
                else
                {
                    FlushIfMatch(sb, stringStart, baseOffset, "UTF16LE", results);
                    sb.Clear();
                    i += 1; // décalage impair pour ne pas rater un flux mal aligné
                }
            }
            FlushIfMatch(sb, stringStart, baseOffset, "UTF16LE", results);
        }

        private static void FlushIfMatch(StringBuilder sb, long stringStart, long baseOffset, string encoding, List<FoundString> results)
        {
            if (sb.Length < 5) return;
            string s = sb.ToString();
            foreach (var needle in Needles)
            {
                if (s.Contains(needle))
                {
                    results.Add(new FoundString(baseOffset + stringStart, encoding, s));
                    break;
                }
            }
        }
    }
}

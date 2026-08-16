using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace NkiTool
{
    /// <summary>
    /// Commande dédiée : extrait et affiche UNIQUEMENT le numéro de version
    /// minimale requise, tel que trouvé en UTF-16LE dans la zone claire
    /// de l'en-tête du NKI (empiriquement observé à l'offset 389 sur les
    /// deux échantillons testés, mais recherché dynamiquement pour rester
    /// robuste si la position varie légèrement selon les métadonnées).
    ///
    /// Sortie sur stdout : uniquement la valeur (ex: "8.0.0.0"), rien d'autre,
    /// pour un usage direct dans un script PowerShell :
    ///   $version = & NkxTool.exe nki-version "Instrument.nki"
    ///
    /// Code de retour : 0 si trouvé, 1 si non trouvé ou erreur.
    /// </summary>
    public static class NkiVersionCommand
    {
        private static readonly Regex VersionPattern =
            new Regex(@"\b\d{1,3}\.\d{1,3}(\.\d{1,5}){0,2}\b", RegexOptions.Compiled);

        private const int ScanLength = 4096;
        private const int PreferredOffsetMin = 300;
        private const int PreferredOffsetMax = 500;

        public static int Run(string path, bool verbose)
        {
            byte[] data;
            try
            {
                data = File.ReadAllBytes(path);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: impossible de lire le fichier ({ex.Message})");
                return 1;
            }

            int limit = Math.Min(ScanLength, data.Length);
            var candidates = ScanUtf16Le(data, limit);

            if (candidates.Count == 0)
            {
                Console.Error.WriteLine("Error: aucune chaîne de version trouvée dans les 4096 premiers octets.");
                return 1;
            }

            // Priorité : un candidat situé dans la fenêtre 300-500, cohérente
            // avec les deux échantillons observés (offset 389 dans les deux cas).
            (long Offset, string Value)? best = null;
            foreach (var c in candidates)
            {
                if (c.Offset >= PreferredOffsetMin && c.Offset <= PreferredOffsetMax)
                {
                    best = c;
                    break;
                }
            }

            // Repli : premier candidat trouvé, si rien dans la fenêtre préférée.
            best ??= candidates[0];

            if (verbose)
            {
                Console.Error.WriteLine($"[diagnostic] {candidates.Count} candidat(s) trouvé(s) au total.");
                foreach (var c in candidates)
                {
                    string marker = (c.Offset == best.Value.Offset) ? "  <-- retenu" : "";
                    Console.Error.WriteLine($"[diagnostic]   offset={c.Offset,-6} valeur=\"{c.Value}\"{marker}");
                }
            }

            // Seule ligne envoyée sur stdout : la valeur, pour capture facile en script.
            Console.WriteLine(best.Value.Value);
            return 0;
        }

        private static System.Collections.Generic.List<(long Offset, string Value)> ScanUtf16Le(byte[] data, int limit)
        {
            var hits = new System.Collections.Generic.List<(long, string)>();
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
                    Flush(sb, start, hits);
                    sb.Clear();
                    i += 1;
                }
            }
            Flush(sb, start, hits);
            return hits;
        }

        private static void Flush(StringBuilder sb, long start, System.Collections.Generic.List<(long, string)> hits)
        {
            if (sb.Length < 3) return;
            string s = sb.ToString();
            foreach (Match m in VersionPattern.Matches(s))
            {
                hits.Add((start + m.Index * 2, m.Value));
            }
        }
    }
}

using System;
using System.IO;
using System.Text;

namespace NkiTool
{
    /// <summary>
    /// Implémentation de la commande "dump" appelée depuis le dispatch
    /// principal de program.cs. Lecture seule, ne modifie jamais le fichier.
    /// </summary>
    public static class NkiDumpCommand
    {
        public static int Run(string path, string? outPath)
        {
            byte[] data = File.ReadAllBytes(path);
            var sb = new StringBuilder();

            sb.AppendLine($"Fichier: {path}");
            sb.AppendLine($"Taille: {data.Length} octets");
            sb.AppendLine($"En-tête (16 premiers octets, hex): {BitConverter.ToString(data, 0, Math.Min(16, data.Length))}");
            sb.AppendLine();

            sb.AppendLine("=== Arborescence de chunks détectée ===");
            var regions = NkiChunkScanner.Scan(data);
            DumpRegions(regions, 0, sb);

            sb.AppendLine();
            sb.AppendLine("=== Références d'échantillons trouvées (.wav / .ncw) ===");
            var found = StringExtractor.FindSampleReferences(data);
            if (found.Count == 0)
            {
                sb.AppendLine("Aucune référence trouvée en clair au niveau racine.");
                sb.AppendLine("(Normal si les données sont compressées en ZLIB : voir les sous-blocs 'inflated' ci-dessus.)");
            }
            foreach (var f in found)
            {
                sb.AppendLine($"  offset={f.Offset,-10} encodage={f.Encoding,-8} valeur=\"{f.Value}\"");
            }

            sb.AppendLine();
            sb.AppendLine("=== Scan récursif complémentaire dans chaque région ===");
            ScanRegionsForStrings(regions, sb);

            string report = sb.ToString();
            Console.WriteLine(report);

            if (outPath != null)
            {
                File.WriteAllText(outPath, report);
                Console.WriteLine($"\nRapport écrit dans: {outPath}");
            }

            return 0;
        }

        private static void DumpRegions(System.Collections.Generic.List<NkiRegion> regions, int depth, StringBuilder sb)
        {
            string indent = new string(' ', depth * 2);
            foreach (var r in regions)
            {
                string flag = r.IsInflated ? " [ZLIB -> décompressé]" : "";
                sb.AppendLine($"{indent}- tag=\"{r.Tag}\" offset={r.Offset} taille={r.Size}{flag}");
                if (r.Children.Count > 0)
                    DumpRegions(r.Children, depth + 1, sb);
            }
        }

        private static void ScanRegionsForStrings(System.Collections.Generic.List<NkiRegion> regions, StringBuilder sb)
        {
            foreach (var r in regions)
            {
                var found = StringExtractor.FindSampleReferences(r.Payload, r.Offset);
                foreach (var f in found)
                {
                    sb.AppendLine($"  [chunk \"{r.Tag}\"] offset={f.Offset,-10} encodage={f.Encoding,-8} valeur=\"{f.Value}\"");
                }
                if (r.Children.Count > 0)
                    ScanRegionsForStrings(r.Children, sb);
            }
        }
    }
}

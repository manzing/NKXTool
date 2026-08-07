using System;
using System.IO;
using System.Text;

namespace NkiTool
{
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
            foreach (var f in found)
            {
                sb.AppendLine($"  offset={f.Offset,-10} encodage={f.Encoding,-8} valeur=\"{f.Value}\"");
            }

            sb.AppendLine();
            sb.AppendLine("=== Scan récursif complémentaire dans chaque région (y compris blocs décompressés) ===");
            ScanRegionsForStrings(regions, sb);

            int totalFound = found.Count + CountNestedStrings(regions);
            if (totalFound == 0)
            {
                sb.AppendLine();
                sb.AppendLine("AUCUNE référence .wav/.ncw trouvée nulle part (racine + sous-blocs décompressés).");
                sb.AppendLine("Voir la liste des marqueurs/offsets ZLIB candidats ci-dessus pour diagnostiquer.");
            }

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

                foreach (var candidate in r.ZlibCandidateOffsets)
                {
                    if (candidate >= 0)
                        sb.AppendLine($"{indent}    [candidat ZLIB trouvé à l'offset absolu {candidate}]");
                    else
                        sb.AppendLine($"{indent}    [marqueur texte connu trouvé à l'offset absolu {-candidate}]");
                }

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
                    sb.AppendLine($"  [chunk \"{r.Tag}\"{(r.IsInflated ? " décompressé" : "")}] offset={f.Offset,-10} encodage={f.Encoding,-8} valeur=\"{f.Value}\"");
                }
                if (r.Children.Count > 0)
                    ScanRegionsForStrings(r.Children, sb);
            }
        }

        private static int CountNestedStrings(System.Collections.Generic.List<NkiRegion> regions)
        {
            int count = 0;
            foreach (var r in regions)
            {
                count += StringExtractor.FindSampleReferences(r.Payload, r.Offset).Count;
                count += CountNestedStrings(r.Children);
            }
            return count;
        }
    }
}
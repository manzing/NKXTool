using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
using System.Threading;
using System.Xml;
using System.Text.RegularExpressions;
using NkiTool;
using NKXTool.Core;
using NKXTool.Core.Common;
using NKXTool.Core.Cryptography;
using NKXTool.Core.Containers.Nkx;

public class Program
{
    public const int E_SUCCESS = 0;

    // Supported formats
    private static readonly HashSet<string> SupportedArchiveExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".nkx", ".nkr", ".nicnt", ".nks"
        };

    private static string NormalizeArchivePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        return path.Trim()
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
    }
    
    [STAThread]
    public static int Main(string[] args)
    {
        int exitCode = 1;

#pragma warning disable CA1416
        Thread staThread = new Thread(() =>
        {
            exitCode = RunTool(args);
        });

        staThread.SetApartmentState(ApartmentState.STA);
        staThread.Start();
        staThread.Join();
#pragma warning restore CA1416

        return exitCode;
    }

    private static int RunTool(string[] args)
    {
        string exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
        List<string> argsList = new List<string>(args);
        
        bool overwrite = argsList.Remove("-y") || argsList.Remove("-Y");

        if (argsList.Count < 1)
        {
            ShowUsage();
            return 1;
        }

        string operation = argsList[0].ToLowerInvariant();

        // Update command
        if (operation == "update")
        {
            string? customXmlPath = null;
            int fIndex = argsList.IndexOf("-f");
            if (fIndex >= 0)
            {
                if (fIndex + 1 >= argsList.Count)
                {
                    Console.WriteLine("Error: Missing file path after -f");
                    return 1;
                }

                string rawPath = argsList[fIndex + 1].Trim();
                if (string.IsNullOrWhiteSpace(rawPath))
                {
                    Console.WriteLine("Error: Empty file path after -f");
                    return 1;
                }

                customXmlPath = Path.GetFullPath(rawPath);
            }

            return UpdateUserDb(customXmlPath);
        }

        // Dump command for NKI
        if (operation == "dump")
        {
            if (argsList.Count < 2)
            {
                Console.WriteLine("Usage: NkxTool dump <instrument.nki> [--out report.txt]");
                return 1;
            }

            string nkiPath = Path.GetFullPath(argsList[1]);
            if (!File.Exists(nkiPath))
            {
                Console.WriteLine($"Error: The source file '{nkiPath}' does not exist.");
                return 1;
            }

            string? outReport = null;
            int outIndex = argsList.IndexOf("--out");
            if (outIndex >= 0 && outIndex + 1 < argsList.Count)
            {
                outReport = Path.GetFullPath(argsList[outIndex + 1]);
            }

            return NkiDumpCommand.Run(nkiPath, outReport);
        }

        if (operation == "nki-infoscan")
        {
            if (argsList.Count < 2)
            {
                Console.WriteLine("Usage: NkxTool nki-infoscan <instrument.nki> [--length N] [--out report.txt]");
                return 1;
            }

            string nkiPath = Path.GetFullPath(argsList[1]);
            if (!File.Exists(nkiPath))
            {
                Console.WriteLine($"Error: The source file '{nkiPath}' does not exist.");
                return 1;
            }

            int scanLength = 4096;
            int lenIndex = argsList.IndexOf("--length");
            if (lenIndex >= 0 && lenIndex + 1 < argsList.Count)
            {
                int.TryParse(argsList[lenIndex + 1], out scanLength);
            }

            string? outReport = null;
            int outIndex = argsList.IndexOf("--out");
            if (outIndex >= 0 && outIndex + 1 < argsList.Count)
            {
                outReport = Path.GetFullPath(argsList[outIndex + 1]);
            }

            return NkiInfoScan.Run(nkiPath, scanLength, outReport);
        }

        if (operation == "nki-version")
        {
            if (argsList.Count < 2)
            {
                Console.WriteLine("Usage: NkxTool nki-version <instrument.nki> [-v]");
                return 1;
            }

            string nkiPath = Path.GetFullPath(argsList[1]);
            if (!File.Exists(nkiPath))
            {
                Console.Error.WriteLine($"Error: The source file '{nkiPath}' does not exist.");
                return 1;
            }

            bool verbose = argsList.Contains("-v") || argsList.Contains("--verbose");
            return NkiVersionCommand.Run(nkiPath, verbose);
        }

        if (argsList.Count < 2)
        {
            ShowUsage();
            return 1;
        }

        string path1 = Path.GetFullPath(argsList[1]);

        if ((operation == "list" || operation == "unpack") && !File.Exists(path1))
        {
            Console.WriteLine($"Error: The source file '{path1}' does not exist.");
            return 1;
        }

        try
        {
            if (operation == "list")
            {
                string? outList = argsList.Count >= 3 ? Path.GetFullPath(argsList[2]) : null;
                return ListArchive(path1, outList);
            }
            else if (operation == "unpack" && argsList.Count >= 3)
            {
                string destinationFolder = Path.GetFullPath(argsList[2]);
                HashSet<string>? selectedFiles = null;

                if (argsList.Count >= 4 && argsList[3].StartsWith("@"))
                {
                    string listFile = argsList[3].Substring(1);
                    if (File.Exists(listFile))
                    {
                        var lines = File.ReadAllLines(listFile);
                        selectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var line in lines)
                        {
                            if (!string.IsNullOrWhiteSpace(line))
                                selectedFiles.Add(line.Trim());
                        }
                    }
                }
                
                return DecompressArchive(path1, destinationFolder, selectedFiles, overwrite);
            }
            else if (operation == "pack" && argsList.Count >= 3)
            {
                string rootPath = argsList.Count >= 4 ? Path.GetFullPath(argsList[3]) : "";
                return CompressFolder(argsList[2], path1, rootPath);
            }
            else
            {
                Console.WriteLine("Invalid operation or missing arguments.");
                ShowUsage();
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Critical Error: {ex.Message}");
            return 1;
        }
    }

    private static void ShowUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  NkxTool unpack <source_file> <destinationFolder> [@filelist.txt] [-y]");
        Console.WriteLine("  NkxTool pack <destination_file> <sourceFolder_OR_@filelist.txt> [rootPath]");
        Console.WriteLine("  NkxTool list <source_file> [outputList.txt]");
        Console.WriteLine("  NkxTool update [-f <NativeAccess.xml path>]");
        Console.WriteLine("  NkxTool dump <instrument.nki> [--out report.txt]");
        Console.WriteLine("  NkxTool nki-infoscan <instrument.nki> [--length N] [--out report.txt]");
        Console.WriteLine("  NkxTool nki-version <instrument.nki> [-v]");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  NkxTool unpack archive.nkx output_folder");
        Console.WriteLine("  NkxTool update");
        Console.WriteLine("  NkxTool update -f \"C:\\Program Files\\Common Files\\Native Instruments\\Service Center\\NativeAccess.xml\"");
        Console.WriteLine("  NkxTool nki-infoscan \"Piano.nki\" --length 4096 --out version_report.txt");
        Console.WriteLine("  NkxTool nki-version \"Piano.nki\"");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -y : Overwrite existing files without skipping (unpack only)");
        Console.WriteLine("  -f : Specify a custom path to NativeAccess.xml (update only)");
        Console.WriteLine("  --length N : Number of bytes to scan from the start of the file (nki-infoscan only, default 4096)");
        Console.WriteLine("  --out : Write the report to a file instead of (or in addition to) the console");
        Console.WriteLine("  -v : Verbose mode, lists all version candidates found (nki-version only)");
        Console.WriteLine();
        Console.WriteLine("Supported extensions: .nkx, .nkr, .nicnt, .nks");
    }

    // --- UPDATE KEY DATABASE ---
    private static int UpdateUserDb(string? customXmlPath = null)
    {
        Console.WriteLine("Updating nklibs.db from NativeAccess.xml...");

        try
        {
            string exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string dbPath = Path.Combine(exeDirectory, NkxKeyDatabase.DefaultDbFileName);

            var (entryCount, duplicates, targetPath) = NkxKeyDatabase.UpdateFromNativeAccessXml(customXmlPath, dbPath);

            Console.WriteLine($"Success. Generated database: {targetPath}");
            Console.WriteLine($"Total entries written: {entryCount} (including {duplicates} decimal/hex duplicates).");
            return E_SUCCESS;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error updating database: {ex.Message}");
            return 1;
        }
    }

    // --- LIST ARCHIVE ---
    private static int ListArchive(string sourceNkxPath, string? outputListPath)
    {
        try
        {
            using var archive = NkxArchive.Open(sourceNkxPath);
            var fileList = archive.Entries
                .Where(e => !e.IsDirectory)
                .Select(e => e.RelativePath)
                .ToList();

            if (!string.IsNullOrEmpty(outputListPath))
            {
                File.WriteAllLines(outputListPath, fileList, Encoding.UTF8);
                Console.WriteLine($"List saved to: {outputListPath} ({fileList.Count} files)");
            }
            else
            {
                foreach (string file in fileList)
                {
                    Console.WriteLine(file);
                }
            }

            return E_SUCCESS;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading archive: {ex.Message}");
            return 1;
        }
    }

    // --- COMPRESS / PACK ---
    private static int CompressFolder(string sourceOrList, string outputNkxFilePath, string rootPath)
    {
        try
        {
            var options = new NkxArchiveWriter.PackOptions
            {
                OutputFilePath = outputNkxFilePath,
                SourcePathOrFileList = sourceOrList,
                RootDirectoryName = string.IsNullOrEmpty(rootPath) ? null : rootPath
            };

            var result = NkxArchiveWriter.PackAsync(options).GetAwaiter().GetResult();
            if (result.IsSuccess)
            {
                Console.WriteLine($"Success. Packed {result.TotalPackedFiles} file(s) ({result.TotalBytesPacked:N0} bytes) in {result.Elapsed.TotalSeconds:F2}s.");
                foreach (var vol in result.VolumeFilePaths)
                {
                    Console.WriteLine($"  Created archive volume: {vol}");
                }
                return E_SUCCESS;
            }
            else
            {
                Console.WriteLine($"Packing completed with errors: {result.FailedFiles.Count} file(s) failed.");
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error packing archive: {ex.Message}");
            return 1;
        }
    }

    // --- DECOMPRESS / UNPACK ---
    private static int DecompressArchive(string sourceNkxPath, string destinationDirPath, HashSet<string>? selectedFiles, bool overwrite)
    {
        try
        {
            using var archive = NkxArchive.Open(sourceNkxPath);
            var options = new ExtractionOptions
            {
                DestinationDirectory = destinationDirPath,
                Overwrite = overwrite,
                SelectedFiles = selectedFiles
            };

            var lastReport = DateTime.UtcNow;
            var progress = new Progress<ExtractionProgress>(p =>
            {
                if ((DateTime.UtcNow - lastReport).TotalMilliseconds >= 200 || p.ProcessedFiles == p.TotalFiles)
                {
                    lastReport = DateTime.UtcNow;
                    Console.Write($"\rUnpacking: {p.ProcessedFiles}/{p.TotalFiles} files ({p.PercentComplete:F1}%) - {p.CurrentFile}              ");
                }
            });

            var result = archive.ExtractAllAsync(options, progress).GetAwaiter().GetResult();
            Console.WriteLine();

            if (result.Errors.Count > 0)
            {
                Console.WriteLine($"\nCompleted with {result.Errors.Count} error(s):");
                foreach (var err in result.Errors)
                {
                    Console.WriteLine($"  [File #{err.FileId}] {err.ArchivePath}: {err.ErrorMessage}");
                }
            }

            Console.WriteLine($"Success. Extracted {result.ExtractedCount}/{result.TotalCount} file(s) in {result.Elapsed.TotalSeconds:F2}s ({result.SkippedCount} skipped).");
            return result.IsSuccess ? E_SUCCESS : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error extracting archive: {ex.Message}");
            return 1;
        }
    }
}

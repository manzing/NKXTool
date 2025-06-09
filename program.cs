// (Le même code C# que la réponse précédente pour Program.cs)
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Linq;
using System.Collections.Generic;

public class Program
{
    // --- P/Invoke Definitions for WCX Plugin Functions ---
    // Make sure inNKX.wcx is in the same directory as the executable.
    private const string PluginDllName = "inNKX.wcx"; // This will be copied next to the EXE

    // Constants for PackFilesW (Flags parameter) - Based on WCX SDK
    public const int PK_DEFAULT = 0x0000;
    public const int PK_PACK_MOVE_FILES = 0x0001;
    public const int PK_PACK_SAVE_PATHS = 0x0002;
    public const int PK_PACK_ATTRIBUTES = 0x0004;

    // Constants for UnpackFilesW (Mode parameter) - Based on WCX SDK
    public const int PK_EXTRACT = 0x0000;
    public const int PK_TEST = 0x0001;
    public const int PK_LIST = 0x0002;
    public const int PK_OVERWRITE = 0x0008;
    public const int PK_RESUME = 0x0010;
    public const int PK_PASSWORD = 0x0080;

    [DllImport(PluginDllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private static extern int PackFilesW(string PackedFile, string SubPath, int Flags, string FileList);

    [DllImport(PluginDllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private static extern int UnpackFilesW(string PackedFile, string Path, string FileList, int Mode);

    private static string JoinNullSeparated(IEnumerable<string> items)
    {
        if (items == null || !items.Any())
            return "\0\0";

        StringBuilder sb = new StringBuilder();
        foreach (string item in items)
        {
            if (item != null)
                sb.Append(item);
            sb.Append('\0');
        }
        sb.Append('\0');
        return sb.ToString();
    }

    public static int Main(string[] args)
    {
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine("NKX Direct Utility (C# Edition)");
        Console.WriteLine("--------------------------------------------------");

        if (args.Length < 3)
        {
            ShowUsage();
            return 1;
        }

        string operation = args[0].ToLowerInvariant();
        string sourcePath = Path.GetFullPath(args[1]);
        string destinationPath = Path.GetFullPath(args[2]);

        Console.WriteLine($"Operation: {operation}");
        Console.WriteLine($"Source: {sourcePath}");
        Console.WriteLine($"Destination: {destinationPath}");
        Console.WriteLine($"Using plugin: {PluginDllName} (expected in executable directory)");
        Console.WriteLine("--------------------------------------------------");

        // The plugin existence check is less critical here because it's handled by the DllImport,
        // which will throw an exception if the DLL is not found at runtime.
        // We'll rely on the exception handling for DLL not found.

        int resultCode = 0; // 0 for success, non-zero for error

        try
        {
            switch (operation)
            {
                case "compress":
                    resultCode = CompressFolder(sourcePath, destinationPath);
                    break;
                case "decompress":
                    resultCode = DecompressArchive(sourcePath, destinationPath);
                    break;
                default:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine("Error: Invalid operation. Use 'compress' or 'decompress'.");
                    Console.ResetColor();
                    resultCode = 1;
                    break;
            }
        }
        catch (DllNotFoundException dnfe)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"Error: The plugin '{PluginDllName}' could not be found. Ensure it is in the same directory as the executable.");
            Console.Error.WriteLine($"Details: {dnfe.Message}");
            Console.ResetColor();
            resultCode = 1;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"An unexpected error occurred: {ex.Message}");
            Console.ResetColor();
            resultCode = 1;
        }

        Console.WriteLine("--------------------------------------------------");
        if (resultCode == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Operation completed successfully.");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Operation failed with exit code {resultCode}.");
        }
        Console.ResetColor();
        return resultCode;
    }

    private static int CompressFolder(string sourceFolderPath, string destinationDirPath)
    {
        if (!Directory.Exists(sourceFolderPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"Error: Source folder not found for compression: '{sourceFolderPath}'");
            Console.ResetColor();
            return 1;
        }

        Directory.CreateDirectory(destinationDirPath); // Ensure destination exists

        string folderName = new DirectoryInfo(sourceFolderPath).Name;
        string outputNkxFileName = Path.Combine(destinationDirPath, $"{folderName}.nkx");

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"Compressing '{sourceFolderPath}' into '{outputNkxFileName}'...");
        Console.ResetColor();

        // Get all files relative to the source folder for PackFilesW.
        List<string> filesToPack = new List<string>();
        foreach (string file in Directory.GetFiles(sourceFolderPath, "*", SearchOption.AllDirectories))
        {
            filesToPack.Add(Path.GetRelativePath(sourceFolderPath, file));
        }

        string fileListString = JoinNullSeparated(filesToPack);

        int packFlags = PK_PACK_SAVE_PATHS; // Maintain folder structure

        // PackFilesW expects the *current directory* to be the base for relative paths in FileList.
        string originalCurrentDirectory = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = sourceFolderPath;
            int result = PackFilesW(outputNkxFileName, "", packFlags, fileListString); // SubPath is empty ""

            if (result == 0)
            {
                if (File.Exists(outputNkxFileName))
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"Compression successful: '{outputNkxFileName}'");
                    Console.ResetColor();
                    return 0;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine($"Error: Plugin reported success (0) but NKX file not found at '{outputNkxFileName}'.");
                    Console.ResetColor();
                    return 1;
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"Error: Plugin failed during compression. Return Code: {result} (Check WCX SDK for meaning).");
                Console.ResetColor();
                return result;
            }
        }
        finally
        {
            Environment.CurrentDirectory = originalCurrentDirectory;
        }
    }

    private static int DecompressArchive(string sourceNkxPath, string destinationDirPath)
    {
        if (!File.Exists(sourceNkxPath) || !sourceNkxPath.EndsWith(".nkx", StringComparison.OrdinalIgnoreCase))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"Error: Source file not found or is not a .nkx archive: '{sourceNkxPath}'");
            Console.ResetColor();
            return 1;
        }

        Directory.CreateDirectory(destinationDirPath); // Ensure destination exists

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"Decompressing '{sourceNkxPath}' to '{destinationDirPath}'...");
        Console.ResetColor();

        int unpackMode = PK_EXTRACT | PK_OVERWRITE; // Extract all, overwrite existing

        int result = UnpackFilesW(sourceNkxPath, destinationDirPath, "", unpackMode); // FileList empty for all files

        if (result == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Decompression successful: '{sourceNkxPath}' contents extracted to '{destinationDirPath}'");
            Console.ResetColor();
            return 0;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"Error: Plugin failed during decompression. Return Code: {result} (Check WCX SDK for meaning).");
            Console.ResetColor();
            return result;
        }
    }

    private static void ShowUsage()
    {
        Console.WriteLine("Usage: NkxTool <operation> <sourcePath> <destinationPath>");
        Console.WriteLine("Operations:");
        Console.WriteLine("  compress   - Compresses a folder into an NKX archive.");
        Console.WriteLine("             - <sourcePath>: Path to the folder to compress.");
        Console.WriteLine("             - <destinationPath>: Directory where the NKX will be created.");
        Console.WriteLine("  decompress - Decompresses an NKX archive.");
        Console.WriteLine("             - <sourcePath>: Path to the .nkx file.");
        Console.WriteLine("             - <destinationPath>: Directory where contents will be extracted.");
        Console.WriteLine("\nExamples:");
        Console.WriteLine("  NkxTool compress \"C:\\MySamples\\Pianos\" \"C:\\MyNkxArchives\"");
        Console.WriteLine("  NkxTool decompress \"C:\\MyNkxArchives\\Pianos.nkx\" \"C:\\ExtractedSamples\"");
    }
}

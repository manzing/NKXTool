using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Linq;
using System.Collections.Generic;

public class Program
{
    // --- P/Invoke Definitions for WCX Plugin Functions ---
    private const string PluginDllName = "inNKX.wcx";

    // Constants for PackFilesW (Flags parameter) - Based on WCX SDK
    public const int PK_DEFAULT = 0x0000;
    public const int PK_PACK_MOVE_FILES = 0x0001;
    public const int PK_PACK_SAVE_PATHS = 0x0002;
    public const int PK_PACK_ATTRIBUTES = 0x0004;

    // Constants for UnpackFilesW / ProcessFileW (Mode parameter) - Based on WCX SDK
    public const int PK_EXTRACT = 0x0000;       // Extract file (for ProcessFileW)
    public const int PK_TEST = 0x0001;          // Test file (for ProcessFileW)
    public const int PK_OVERWRITE = 0x0008;     // Overwrite existing file (for ProcessFileW)

    // WCX SDK Return Codes
    public const int PK_OK = 0;              // Success
    public const int E_END_ARCHIVE = 10;     // End of archive (for ReadHeader)
    public const int E_NO_MEMORY = 1;        // Out of memory
    public const int E_BAD_ARCHIVE = 2;      // Bad archive file
    public const int E_UNKNOWN_FORMAT = 3;   // Unknown archive format
    public const int E_BAD_DATA = 4;         // CRC error in data
    public const int E_NO_FILES = 6;         // No files matching pattern
    public const int E_TOO_MANY_FILES = 7;   // Too many files to add
    public const int E_NOT_SUPPORTED = 8;    // Function not supported by plugin
    public const int E_WRITE_ERROR = 9;      // Disk write error
    public const int E_ABORT = 11;           // User abort

    // Structure for ReadHeaderExW (Unicode version)
    // tHeaderDataExW definition from WCX SDK (plugin.h)
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct tHeaderDataExW
    {
        public UInt32 Crc32;
        public UInt32 FileTime;
        public UInt32 PackSize;
        public UInt32 UnPackSize;
        public UInt32 FileAttr;
        public int Ratio;
        public UInt32 Flags;

        // ANSI fields - must be present for correct struct layout, but not used by Wide char APIs
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 260)] // 260 bytes
        public byte[] FileNameA;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 260)] // 260 bytes
        public byte[] PathA;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]  // 16 bytes
        public byte[] ReservedA;

        public UInt32 Crc32_low;
        public UInt32 UnPackSize_low;

        // Unicode fields
        // SizeConst is number of WIDE characters (wchar_t), not bytes. 1024 bytes = 512 wide chars.
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)] // For wchar_t FileName[512]
        public string FileNameW;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)] // For wchar_t Path[512]
        public string PathW;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] // For BYTE ReservedW[256]
        public byte[] ReservedW; // Use byte[] for raw byte array in C

        // Public properties to easily get full path from the struct
        public string FullPathW => PathW + (string.IsNullOrEmpty(PathW) ? "" : "\\") + FileNameW;
    }

    // --- P/Invoke Declarations (Updated/Added) ---

    // Original PackFilesW for compression
    [DllImport(PluginDllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private static extern int PackFilesW(string PackedFile, string SubPath, int Flags, string FileList);

    // New functions for decompression (Open/Read/Process/Close)
    // HANDLE __stdcall OpenArchiveW(char* ArchiveName, int OpenMode)
    [DllImport(PluginDllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenArchiveW(string ArchiveName, int OpenMode); // Returns a handle (IntPtr)

    // int __stdcall ReadHeaderExW(HANDLE hArc, tHeaderDataExW *HeaderData)
    // Note: When passing a struct by reference to a C function, use 'ref' in C#
    [DllImport(PluginDllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private static extern int ReadHeaderExW(IntPtr hArc, ref tHeaderDataExW HeaderData);

    // int __stdcall ProcessFileW(HANDLE hArc, int Operation, char* DestPath, char* DestName)
    [DllImport(PluginDllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private static extern int ProcessFileW(IntPtr hArc, int Operation, string DestPath, string DestName);

    // int __stdcall CloseArchive(HANDLE hArc)
    [DllImport(PluginDllName, CallingConvention = CallingConvention.StdCall)] // CharSet is not relevant for handle only
    private static extern int CloseArchive(IntPtr hArc);

    // --- Helper Methods (unchanged) ---
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

    // --- Main Entry Point (unchanged) ---
    public static int Main(string[] args)
    {
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine("NKX Direct Utility by Manzing (C# Edition)");
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

        int resultCode = 0; // 0 for success, non-zero for error

        try
        {
            switch (operation)
            {
                case "compress":
                    resultCode = CompressFolder(sourcePath, destinationPath);
                    break;
                case "decompress":
                    // Call the NEW decompression logic
                    resultCode = DecompressArchiveViaOpenReadProcess(sourcePath, destinationPath);
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
        catch (EntryPointNotFoundException epnfe)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"Error: A required function was not found in the plugin '{PluginDllName}'. This plugin might be incomplete or a different version.");
            Console.Error.WriteLine($"Details: {epnfe.Message}");
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

    // --- CompressFolder (unchanged) ---
    private static int CompressFolder(string sourceFolderPath, string destinationDirPath)
    {
        if (!Directory.Exists(sourceFolderPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"Error: Source folder not found for compression: '{sourceFolderPath}'");
            Console.ResetColor();
            return 1;
        }

        Directory.CreateDirectory(destinationDirPath);

        string folderName = new DirectoryInfo(sourceFolderPath).Name;
        string outputNkxFileName = Path.Combine(destinationDirPath, $"{folderName}.nkx");

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"Compressing '{sourceFolderPath}' into '{outputNkxFileName}'...");
        Console.ResetColor();

        List<string> filesToPack = new List<string>();
        foreach (string file in Directory.GetFiles(sourceFolderPath, "*", SearchOption.AllDirectories))
        {
            filesToPack.Add(Path.GetRelativePath(sourceFolderPath, file));
        }

        string fileListString = JoinNullSeparated(filesToPack);
        int packFlags = PK_PACK_SAVE_PATHS;

        string originalCurrentDirectory = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = sourceFolderPath;
            int result = PackFilesW(outputNkxFileName, "", packFlags, fileListString);

            if (result == PK_OK)
            {
                if (File.Exists(outputNkxFileName))
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"Compression successful: '{outputNkxFileName}'");
                    Console.ResetColor();
                    return PK_OK;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine($"Error: Plugin reported success (0) but NKX file not found at '{outputNkxFileName}'.");
                    Console.ResetColor();
                    return E_WRITE_ERROR; // Indicate failure
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

    // --- NEW DecompressArchiveViaOpenReadProcess (replaces previous DecompressArchive) ---
    private static int DecompressArchiveViaOpenReadProcess(string sourceNkxPath, string destinationDirPath)
    {
        if (!File.Exists(sourceNkxPath) || !sourceNkxPath.EndsWith(".nkx", StringComparison.OrdinalIgnoreCase))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"Error: Source file not found or is not a .nkx archive: '{sourceNkxPath}'");
            Console.ResetColor();
            return E_BAD_ARCHIVE;
        }

        Directory.CreateDirectory(destinationDirPath); // Ensure destination exists

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"Decompressing '{sourceNkxPath}' to '{destinationDirPath}' using Open/Read/Process...");
        Console.ResetColor();

        IntPtr hArc = IntPtr.Zero; // Handle to the archive
        int result = PK_OK;

        try
        {
            // Open the archive
            // OpenMode = 0 for normal opening (for reading)
            hArc = OpenArchiveW(sourceNkxPath, 0);
            if (hArc == IntPtr.Zero)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"Error: Could not open archive '{sourceNkxPath}'. Plugin returned invalid handle.");
                Console.ResetColor();
                return E_BAD_ARCHIVE; // Or more specific error if OpenArchiveW returned an error code directly
            }

            tHeaderDataExW headerData = new tHeaderDataExW();
            int fileCount = 0;

            // Loop through files in the archive
            while (true)
            {
                result = ReadHeaderExW(hArc, ref headerData);

                if (result == E_END_ARCHIVE)
                {
                    Console.WriteLine("End of archive.");
                    break; // No more files
                }
                else if (result != PK_OK)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine($"Error: Failed to read header. Return Code: {result} (Check WCX SDK for meaning).");
                    Console.ResetColor();
                    return result; // Error reading header
                }

                // Construct the full destination path for the current file
                // headerData.PathW contains the subfolders, headerData.FileNameW is the file name
                string relativeFilePath = Path.Combine(headerData.PathW, headerData.FileNameW).Replace('/', Path.DirectorySeparatorChar); // Ensure correct path separators

                string fullDestinationPath = Path.Combine(destinationDirPath, relativeFilePath);
                string fileDirectory = Path.GetDirectoryName(fullDestinationPath);

                // Create subdirectories if they don't exist
                if (!string.IsNullOrEmpty(fileDirectory) && !Directory.Exists(fileDirectory))
                {
                    Directory.CreateDirectory(fileDirectory);
                }

                Console.WriteLine($"  Extracting: {relativeFilePath}");

                // Process (extract) the current file
                int processResult = ProcessFileW(hArc, PK_EXTRACT | PK_OVERWRITE, destinationDirPath, relativeFilePath); // DestPath is base, DestName is relative
                // Note: Some WCX plugins expect DestPath to be the final directory, and DestName to be just the filename.
                // Others expect DestPath as the root and DestName as the relative path.
                // The current implementation passes destinationDirPath as DestPath and relativeFilePath as DestName.
                // This is a common pattern for ProcessFileW when extracting based on ReadHeaderExW.

                if (processResult != PK_OK)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine($"Error: Failed to process file '{relativeFilePath}'. Return Code: {processResult}.");
                    Console.ResetColor();
                    return processResult; // Error processing file
                }
                fileCount++;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Decompression successful. Extracted {fileCount} files from '{sourceNkxPath}'.");
            Console.ResetColor();
            return PK_OK;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"An unexpected error occurred during decompression: {ex.Message}");
            Console.ResetColor();
            return E_ABORT; // Indicate generic abort/failure
        }
        finally
        {
            // Always close the archive handle
            if (hArc != IntPtr.Zero)
            {
                CloseArchive(hArc);
                hArc = IntPtr.Zero;
            }
        }
    }

    // --- ShowUsage (unchanged) ---
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

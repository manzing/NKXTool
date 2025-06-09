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

    // Constants for PackFilesW (Flags parameter) - Based on WCX SDK (from wcxplugin.pas)
    public const int PK_DEFAULT = 0x0000;
    public const int PK_PACK_MOVE_FILES = 1;     // From wcxplugin.pas: PK_PACK_MOVE_FILES= 1
    public const int PK_PACK_SAVE_PATHS = 2;     // From wcxplugin.pas: PK_PACK_SAVE_PATHS= 2
    public const int PK_PACK_ENCRYPT = 4;        // From wcxplugin.pas: PK_PACK_ENCRYPT= 4

    // Constants for UnpackFilesW / ProcessFileW (Mode parameter) - Based on wcxplugin.pas
    public const int PK_SKIP = 0;           // From wcxplugin.pas: PK_SKIP= 0
    public const int PK_TEST = 1;           // From wcxplugin.pas: PK_TEST= 1
    public const int PK_EXTRACT = 2;        // From wcxplugin.pas: PK_EXTRACT= 2 (CRITICAL CHANGE!)

    // Unpacking flags for OpenArchiveW - Based on wcxplugin.pas
    public const int PK_OM_LIST = 0;
    public const int PK_OM_EXTRACT = 1;     // From wcxplugin.pas: PK_OM_EXTRACT= 1

    // WCX SDK Return Codes - BASED ON wcxplugin.pas (CRITICAL CHANGES!)
    public const int E_SUCCESS = 0;          // Success (formerly PK_OK)
    public const int E_END_ARCHIVE = 10;     // No more files in archive
    public const int E_NO_MEMORY = 11;       // Not enough memory
    public const int E_BAD_DATA = 12;        // Data is bad
    public const int E_BAD_ARCHIVE = 13;     // CRC error in archive data
    public const int E_UNKNOWN_FORMAT = 14;  // Archive format unknown
    public const int E_EOPEN = 15;           // Cannot open existing file
    public const int E_ECREATE = 16;         // Cannot create file
    public const int E_ECLOSE = 17;          // Error closing file
    public const int E_EREAD = 18;           // Error reading from file
    public const int E_EWRITE = 19;          // Error writing to file (formerly E_WRITE_ERROR)
    public const int E_SMALL_BUF = 20;       // Buffer too small
    public const int E_EABORTED = 21;        // Function aborted by user (formerly E_ABORT)
    public const int E_NO_FILES = 22;        // No files found
    public const int E_TOO_MANY_FILES = 23;  // Too many files to pack
    public const int E_NOT_SUPPORTED = 24;   // Function not supported
    public const int E_UNKNOWN = 32768;      // Unknown error (Added from wcxplugin.pas)


    // Structure for ReadHeaderExW based on wcxplugin.pas THeaderDataExW
    // CRITICAL: LayoutKind.Sequential, Pack = 1 to match Pascal's 'packed record'
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
    public struct tHeaderDataExW_WCXPlugin
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)]
        public string hdArcNameW;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)]
        public string hdFileNameW;

        public Int32 hdFlags; // longint is Int32
        public UInt32 hdPackSize;
        public UInt32 hdPackSizeHigh;
        public UInt32 hdUnpSize;
        public UInt32 hdUnpSizeHigh;
        public Int32 hdHostOS;
        public Int32 hdFileCRC;
        public Int32 hdFileTime;
        public Int32 hdUnpVer;
        public Int32 hdMethod;
        public Int32 hdFileAttr;

        public IntPtr hdCmtBuf; // pchar (pointer to char, ANSI string)
        public Int32 hdCmtBufSize;
        public Int32 hdCmtSize;
        public Int32 hdCmtState;

        // Reserved:array[0..1023] of char; - This is 1024 bytes
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1024)]
        public byte[] hdReserved;

        public UInt64 MfileTime; // CRITICAL: Missing field from previous version, added from wcxplugin.pas
    }


    // Structure for OpenArchiveW based on wcxplugin.pas tOpenArchiveDataW
    // CRITICAL: LayoutKind.Sequential, Pack = 1 to match Pascal's 'packed record'
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
    public struct tOpenArchiveDataW_WCXPlugin
    {
        public IntPtr ArcName; // pwidechar (pointer to widechar, Unicode string)
        public Int32 OpenMode;
        public Int32 OpenResult;

        public IntPtr CmtBuf; // pwidechar (pointer to widechar, Unicode string)
        public Int32 CmtBufSize;
        public Int32 CmtSize;
        public Int32 CmtState;
    }


    // --- P/Invoke Declarations ---

    [DllImport(PluginDllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private static extern int PackFilesW(
        [MarshalAs(UnmanagedType.LPWStr)] string PackedFile,
        [MarshalAs(UnmanagedType.LPWStr)] string? SubPath,
        int Flags,
        [MarshalAs(UnmanagedType.LPWStr)] string FileList);

    [DllImport(PluginDllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenArchiveW(ref tOpenArchiveDataW_WCXPlugin OpenArchiveData);

    [DllImport(PluginDllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private static extern int ReadHeaderExW(IntPtr hArc, ref tHeaderDataExW_WCXPlugin HeaderData);

    // MODIFIED: ProcessFileW takes IntPtr for string parameters for explicit memory management
    [DllImport(PluginDllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private static extern int ProcessFileW(IntPtr hArc, int Operation,
        IntPtr DestPath, // Changed from string?
        IntPtr DestName); // Changed from string?

    [DllImport(PluginDllName, CallingConvention = CallingConvention.StdCall)]
    private static extern int CloseArchive(IntPtr hArc);

    // --- Helper Methods ---
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

    // --- Main Entry Point ---
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

        int resultCode = 0;

        try
        {
            switch (operation)
            {
                case "compress":
                    resultCode = CompressFolder(sourcePath, destinationPath);
                    break;
                case "decompress":
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
        catch (BadImageFormatException bife)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"Error: The plugin '{PluginDllName}' is not compatible with this application. Ensure you are using the correct 32-bit or 64-bit version of the plugin.");
            Console.Error.WriteLine($"Details: {bife.Message}");
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

    // --- CompressFolder ---
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
            // Get relative path for packing
            filesToPack.Add(Path.GetRelativePath(sourceFolderPath, file));
        }

        string fileListString = JoinNullSeparated(filesToPack);
        int packFlags = PK_PACK_SAVE_PATHS; // Save paths in the archive

        string originalCurrentDirectory = Environment.CurrentDirectory;
        try
        {
            // The plugin often expects the current directory to be the root of the files being packed
            Environment.CurrentDirectory = sourceFolderPath;
            int result = PackFilesW(outputNkxFileName, null, packFlags, fileListString);

            if (result == E_SUCCESS) // Changed from PK_OK
            {
                if (File.Exists(outputNkxFileName))
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"Compression successful: '{outputNkxFileName}'");
                    Console.ResetColor();
                    return E_SUCCESS; // Changed from PK_OK
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine($"Error: Plugin reported success ({E_SUCCESS}) but NKX file not found at '{outputNkxFileName}'.");
                    Console.ResetColor();
                    return E_EWRITE; // Use new error code
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

    // --- DecompressArchiveViaOpenReadProcess ---
    private static int DecompressArchiveViaOpenReadProcess(string sourceNkxPath, string destinationDirPath)
    {
        if (!File.Exists(sourceNkxPath) || !sourceNkxPath.EndsWith(".nkx", StringComparison.OrdinalIgnoreCase))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"Error: Source file not found or is not a .nkx archive: '{sourceNkxPath}'");
            Console.ResetColor();
            return E_BAD_ARCHIVE; // Use new error code
        }

        Directory.CreateDirectory(destinationDirPath); // Ensure destination exists

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"Decompressing '{sourceNkxPath}' to '{destinationDirPath}' using Open/Read/Process...");
        Console.ResetColor();

        IntPtr hArc = IntPtr.Zero; // Handle to the archive
        int result = E_SUCCESS; // Use new success code

        // Prepare tOpenArchiveDataW_WCXPlugin struct for OpenArchiveW call
        tOpenArchiveDataW_WCXPlugin openArcData = new tOpenArchiveDataW_WCXPlugin();
        openArcData.OpenMode = PK_OM_EXTRACT; // Use constant from wcxplugin.pas

        // Marshal the archive name string to an unmanaged pointer for ArcName field
        IntPtr pArcName = Marshal.StringToHGlobalUni(sourceNkxPath);
        openArcData.ArcName = pArcName;

        try
        {
            // Open the archive using the struct
            hArc = OpenArchiveW(ref openArcData);

            // Free the unmanaged string memory after the call
            Marshal.FreeHGlobal(pArcName);
            pArcName = IntPtr.Zero;

            if (hArc == IntPtr.Zero)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"Error: Could not open archive '{sourceNkxPath}'. Plugin returned invalid handle. OpenResult: {openArcData.OpenResult}");
                Console.ResetColor();
                return E_BAD_ARCHIVE; // Use new error code
            }

            tHeaderDataExW_WCXPlugin headerData = new tHeaderDataExW_WCXPlugin();
            int fileCount = 0;

            // Loop through files in the archive
            while (true)
            {
                result = ReadHeaderExW(hArc, ref headerData);

                if (result == E_END_ARCHIVE) // Use new error code
                {
                    Console.WriteLine("End of archive.");
                    break;
                }
                else if (result != E_SUCCESS) // Use new success code
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine($"Error: Failed to read header. Return Code: {result} (Check WCX SDK for meaning).");
                    Console.ResetColor();
                    return result;
                }

                string relativeFilePath = headerData.hdFileNameW.Replace('/', Path.DirectorySeparatorChar);
                string fullDestinationFilePath = Path.Combine(destinationDirPath, relativeFilePath);
                string fileDirectory = Path.GetDirectoryName(fullDestinationFilePath);

                // Create subdirectories if they don't exist
                if (!string.IsNullOrEmpty(fileDirectory) && !Directory.Exists(fileDirectory))
                {
                    Directory.CreateDirectory(fileDirectory);
                }

                Console.WriteLine($"  Extracting: {relativeFilePath}");

                int processResult;
                IntPtr pDestPath = IntPtr.Zero;
                IntPtr pDestName = IntPtr.Zero;

                try
                {
                    // For directories, cmdTotal passes DestPath=NULL, DestName=NULL and PK_SKIP.
                    // For files, cmdTotal passes DestPath=NULL, DestName=fullFilePath and PK_EXTRACT.
                    if ((headerData.hdFileAttr & 0x10) != 0) // Check if it's a directory (FILE_ATTRIBUTE_DIRECTORY)
                    {
                        processResult = ProcessFileW(hArc, PK_SKIP, IntPtr.Zero, IntPtr.Zero);
                    }
                    else // It's a file
                    {
                        // Allocate memory for the destination path string explicitly
                        pDestName = Marshal.StringToHGlobalUni(fullDestinationFilePath);
                        // CRITICAL CHANGE: Use PK_EXTRACT=2, remove PK_OVERWRITE
                        processResult = ProcessFileW(hArc, PK_EXTRACT, IntPtr.Zero, pDestName);
                    }
                }
                finally
                {
                    // Ensure the explicitly allocated memory is freed after the call
                    if (pDestName != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(pDestName);
                    }
                }


                if (processResult != E_SUCCESS) // Use new success code
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine($"Error: Failed to process file '{relativeFilePath}'. Return Code: {processResult}.");
                    Console.ResetColor();
                    return processResult;
                }
                fileCount++;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Decompression successful. Extracted {fileCount} files from '{sourceNkxPath}'.");
            Console.ResetColor();
            return E_SUCCESS; // Use new success code
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"An unexpected error occurred during decompression: {ex.Message}");
            Console.ResetColor();
            return E_UNKNOWN; // Use new error code
        }
        finally
        {
            // Always ensure unmanaged memory is freed and archive handle is closed
            if (pArcName != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(pArcName);
            }
            if (hArc != IntPtr.Zero)
            {
                CloseArchive(hArc);
                hArc = IntPtr.Zero;
            }
        }
    }

    // --- ShowUsage ---
    private static void ShowUsage()
    {
        Console.WriteLine("Usage: NkxTool <operation> <sourcePath> <destinationPath>");
        Console.WriteLine("Operations:");
        Console.WriteLine("  compress    - Compresses a folder into an NKX archive.");
        Console.WriteLine("              - <sourcePath>: Path to the folder to compress.");
        Console.WriteLine("              - <destinationPath>: Directory where the NKX will be created.");
        Console.WriteLine("  decompress - Decompresses an NKX archive.");
        Console.WriteLine("              - <sourcePath>: Path to the .nkx file.");
        Console.WriteLine("              - <destinationPath>: Directory where contents will be extracted.");
        Console.WriteLine("\nExamples:");
        Console.WriteLine("  NkxTool compress \"C:\\MySamples\\Pianos\" \"C:\\MyNkxArchives\"");
        Console.WriteLine("  NkxTool decompress \"C:\\MyNkxArchives\\Pianos.nkx\" \"C:\\ExtractedSamples\"");
    }
}

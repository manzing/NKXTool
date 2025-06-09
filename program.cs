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
    public const int PK_EXTRACT = 0x0000;       // For ProcessFileW: Extract file (value 0 according to SDK)
    public const int PK_TEST = 0x0001;          // For ProcessFileW: Test file
    public const int PK_SKIP = 0x0002;          // For ProcessFileW: Skip file (cmdTotal.asm uses this for directories)
    public const int PK_OVERWRITE = 0x0008;     // Overwrite existing file (for ProcessFileW)

    // WCX SDK Return Codes
    public const int PK_OK = 0;              // Success
    public const int E_END_ARCHIVE = 10;     // End of archive (for ReadHeader)
    public const int E_NO_MEMORY = 1;        // Out of memory
    public const int E_BAD_ARCHIVE = 2;      // Bad archive file
    public const int E_UNKNOWN_FORMAT = 3;   // Unknown archive format
    public const int E_BAD_DATA = 4;         // CRC error in data
    public const int E_NO_FILES = 6;         // No files matching pattern
    public const int E_TOO_MANY_FILES = 7;   // Too many files to add (FIXED TYPO)
    public const int E_NOT_SUPPORTED = 8;    // Function not supported by plugin
    public const int E_WRITE_ERROR = 9;      // Disk write error
    public const int E_ABORT = 11;           // User abort

    // NEW: Structure for ReadHeaderExW based on cmdTotal.asm's HEADERDATAEXW definition
    // This is a custom struct layout that does NOT match the official WCX SDK's tHeaderDataExW.
    // We are adopting this because cmdTotal.asm successfully interacts with inNKX.wcx.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct tHeaderDataExW_cmdTotal_Style
    {
        // Sizes from cmdTotal.asm's HEADERDATAEXW (1024 words = 2048 bytes for strings)
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)] // wchar_t[1024] - ArcName, but in this struct context is likely ArcName within header
        public string hdArcNameW; // Not in standard SDK tHeaderDataExW, but first field in cmdTotal's HEADERDATAEXW

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)] // wchar_t[1024] - FileName
        public string hdFileNameW;

        public UInt32 hdFlags;
        public UInt32 hdPackSizeLow;
        public UInt32 hdPackSizeHigh;
        public UInt32 hdUnpSizeLow;
        public UInt32 hdUnpSizeHigh;
        public UInt32 hdHostOS;
        public UInt32 hdFileCRC;
        public UInt32 hdFileTime;
        public UInt32 hdUnpVer;
        public UInt32 hdMethod;
        public UInt32 hdFileAttr;
        public IntPtr hdCmtBuf; // dd ? -> pointer
        public UInt32 hdCmtBufSize;
        public UInt32 hdCmtSize;
        public UInt32 hdCmtState;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1024)] // 1024 bytes for Reserved
        public byte[] hdReserved;

        // Note: cmdTotal's HEADERDATAEXW does NOT contain a separate PathW field.
        // The hdFileNameW field will likely contain the full relative path + filename (e.g., "SubDir\File.wav").
    }


    // NEW: Structure for OpenArchiveW based on cmdTotal.asm's OPENARCHIVEDATAAW
    // This differs from the standard WCX SDK's OpenArchiveW signature
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct tOpenArchiveDataExW
    {
        public IntPtr ArcName;      // Pointer to the archive name string (wchar_t*)
        public int OpenMode;        // Open mode (0 for list/test, 1 for extract)
        public int OpenResult;      // Result code from plugin (usually 0 for success)
        public IntPtr CmtBuf;       // Pointer to comment buffer (not used in our case)
        public int CmtBufSize;
        public int CmtSize;
        public int CmtState;
    }


    // --- P/Invoke Declarations ---

    // Original PackFilesW for compression (SubPath made nullable to fix warning)
    [DllImport(PluginDllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private static extern int PackFilesW(
        [MarshalAs(UnmanagedType.LPWStr)] string PackedFile,
        [MarshalAs(UnmanagedType.LPWStr)] string? SubPath, // Made nullable as per warning fix
        int Flags,
        [MarshalAs(UnmanagedType.LPWStr)] string FileList);

    // NEW: OpenArchiveW, accepting the tOpenArchiveDataExW struct as seen in cmdTotal.asm
    [DllImport(PluginDllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenArchiveW(ref tOpenArchiveDataExW OpenArchiveData);

    // ReadHeaderExW now uses the custom struct based on cmdTotal.asm
    [DllImport(PluginDllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private static extern int ReadHeaderExW(IntPtr hArc, ref tHeaderDataExW_cmdTotal_Style HeaderData);

    // ProcessFileW remains the same (parameters are handled in the call site)
    [DllImport(PluginDllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private static extern int ProcessFileW(IntPtr hArc, int Operation,
        [MarshalAs(UnmanagedType.LPWStr)] string? DestPath, // Made nullable
        [MarshalAs(UnmanagedType.LPWStr)] string? DestName); // Made nullable

    // CloseArchive remains the same
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

    // --- DecompressArchiveViaOpenReadProcess ---
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

        // Prepare tOpenArchiveDataExW struct for OpenArchiveW call
        tOpenArchiveDataExW openArcData = new tOpenArchiveDataExW();
        // cmdTotal uses 1 for extraction mode, 0 for list/test
        openArcData.OpenMode = 1; // PK_OM_EXTRACT based on cmdTotal.asm

        // Marshal the archive name string to an unmanaged pointer for ArcName field
        IntPtr pArcName = Marshal.StringToHGlobalUni(sourceNkxPath);
        openArcData.ArcName = pArcName; // Assign the pointer to the struct field

        try
        {
            // Open the archive using the struct
            hArc = OpenArchiveW(ref openArcData);

            // Free the unmanaged string memory after the call
            Marshal.FreeHGlobal(pArcName);
            pArcName = IntPtr.Zero; // Important: set to null after freeing

            if (hArc == IntPtr.Zero)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"Error: Could not open archive '{sourceNkxPath}'. Plugin returned invalid handle. OpenResult: {openArcData.OpenResult}");
                Console.ResetColor();
                return E_BAD_ARCHIVE;
            }

            // NEW: Use the custom header data struct
            tHeaderDataExW_cmdTotal_Style headerData = new tHeaderDataExW_cmdTotal_Style();
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

                // NEW: Construct the full destination path using hdFileNameW directly
                // (Assuming hdFileNameW now contains the full relative path including directory)
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
                // Implement cmdTotal.asm's logic for ProcessFileW parameters
                // For directories, cmdTotal passes DestPath=NULL, DestName=NULL and PK_SKIP.
                // For files, cmdTotal passes DestPath=NULL, DestName=fullFilePath and PK_EXTRACT.
                if ((headerData.FileAttr & 0x10) != 0) // Check if it's a directory (FILE_ATTRIBUTE_DIRECTORY)
                {
                    processResult = ProcessFileW(hArc, PK_SKIP, null, null);
                }
                else // It's a file
                {
                    processResult = ProcessFileW(hArc, PK_EXTRACT | PK_OVERWRITE, null, fullDestinationFilePath);
                }

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

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Linq;
using System.Collections.Generic;

public class Program
{
    private const string PluginDllName = "inNKX.wcx";

    public const int PK_DEFAULT = 0x0000;
    public const int PK_PACK_MOVE_FILES = 1;
    public const int PK_PACK_SAVE_PATHS = 2;
    public const int PK_PACK_ENCRYPT = 4;

    public const int PK_SKIP = 0;
    public const int PK_TEST = 1;
    public const int PK_EXTRACT = 2;
    public const int PK_OVERWRITE = 0x0008;

    public const int PK_OM_LIST = 0;
    public const int PK_OM_EXTRACT = 1;

    public const int E_SUCCESS = 0;
    public const int E_END_ARCHIVE = 10;
    public const int E_NO_MEMORY = 11;
    public const int E_BAD_DATA = 12;
    public const int E_BAD_ARCHIVE = 13;
    public const int E_UNKNOWN_FORMAT = 14;
    public const int E_EOPEN = 15;
    public const int E_ECREATE = 16;
    public const int E_ECLOSE = 17;
    public const int E_EREAD = 18;
    public const int E_EWRITE = 19;
    public const int E_SMALL_BUF = 20;
    public const int E_EABORTED = 21;
    public const int E_NO_FILES = 22;
    public const int E_TOO_MANY_FILES = 23;
    public const int E_NOT_SUPPORTED = 24;
    public const int E_UNKNOWN = 32768;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
    public struct tHeaderDataExW_WCXPlugin
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)]
        public string hdArcNameW;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)]
        public string hdFileNameW;

        public Int32 hdFlags;
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

        public IntPtr hdCmtBuf;
        public Int32 hdCmtBufSize;
        public Int32 hdCmtSize;
        public Int32 hdCmtState;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1024)]
        public byte[] hdReserved;

        public UInt64 MfileTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
    public struct tOpenArchiveDataW_WCXPlugin
    {
        public IntPtr ArcName;
        public Int32 OpenMode;
        public Int32 OpenResult;
        public IntPtr CmtBuf;
        public Int32 CmtBufSize;
        public Int32 CmtSize;
        public Int32 CmtState;
    }

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

    [DllImport(PluginDllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private static extern int ProcessFileW(IntPtr hArc, int Operation,
        IntPtr DestPath, IntPtr DestName);

    [DllImport(PluginDllName, CallingConvention = CallingConvention.StdCall)]
    private static extern int CloseArchive(IntPtr hArc);

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
                    string folderToCompress = sourcePath;
                    string outputNkxFilePath = destinationPath;

                    if (!outputNkxFilePath.EndsWith(".nkx", StringComparison.OrdinalIgnoreCase))
                    {
                        string folderNameForArchive = new DirectoryInfo(folderToCompress).Name;
                        outputNkxFilePath = Path.Combine(outputNkxFilePath, $"{folderNameForArchive}.nkx");
                    }
                    resultCode = CompressFolder(folderToCompress, outputNkxFilePath);
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

    private static int CompressFolder(string sourceFolderPath, string outputNkxFilePath)
    {
        if (!Directory.Exists(sourceFolderPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"Error: Source folder not found for compression: '{sourceFolderPath}'");
            Console.ResetColor();
            return 1;
        }

        string outputDirectory = Path.GetDirectoryName(outputNkxFilePath);
        if (!string.IsNullOrEmpty(outputDirectory) && !Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"Compressing '{sourceFolderPath}' into '{outputNkxFilePath}'...");
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
            int result = PackFilesW(outputNkxFilePath, null, packFlags, fileListString);

            if (result == E_SUCCESS)
            {
                if (File.Exists(outputNkxFilePath))
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"Compression successful: '{outputNkxFilePath}'");
                    Console.ResetColor();
                    return E_SUCCESS;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine($"Error: Plugin reported success ({E_SUCCESS}) but NKX file not found at '{outputNkxFilePath}'.");
                    Console.ResetColor();
                    return E_EWRITE;
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

    private static int DecompressArchiveViaOpenReadProcess(string sourceNkxPath, string destinationDirPath)
    {
        if (!File.Exists(sourceNkxPath) || !sourceNkxPath.EndsWith(".nkx", StringComparison.OrdinalIgnoreCase))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"Error: Source file not found or is not a .nkx archive: '{sourceNkxPath}'");
            Console.ResetColor();
            return E_BAD_ARCHIVE;
        }

        Directory.CreateDirectory(destinationDirPath);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"Decompressing '{sourceNkxPath}' to '{destinationDirPath}' using Open/Read/Process...");
        Console.ResetColor();

        IntPtr hArc = IntPtr.Zero;
        int result = E_SUCCESS;

        tOpenArchiveDataW_WCXPlugin openArcData = new tOpenArchiveDataW_WCXPlugin();
        openArcData.OpenMode = PK_OM_EXTRACT;

        IntPtr pArcName = Marshal.StringToHGlobalUni(sourceNkxPath);
        openArcData.ArcName = pArcName;

        try
        {
            hArc = OpenArchiveW(ref openArcData);

            Marshal.FreeHGlobal(pArcName);
            pArcName = IntPtr.Zero;

            if (hArc == IntPtr.Zero)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"Error: Could not open archive '{sourceNkxPath}'. Plugin returned invalid handle. OpenResult: {openArcData.OpenResult}");
                Console.ResetColor();
                return E_BAD_ARCHIVE;
            }

            tHeaderDataExW_WCXPlugin headerData = new tHeaderDataExW_WCXPlugin();
            int fileCount = 0;

            while (true)
            {
                result = ReadHeaderExW(hArc, ref headerData);

                if (result == E_END_ARCHIVE)
                {
                    Console.WriteLine("End of archive.");
                    break;
                }
                else if (result != E_SUCCESS)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine($"Error: Failed to read header. Return Code: {result} (Check WCX SDK for meaning).");
                    Console.ResetColor();
                    return result;
                }

                string relativeFilePath = headerData.hdFileNameW.Replace('/', Path.DirectorySeparatorChar);
                string fullDestinationFilePath = Path.Combine(destinationDirPath, relativeFilePath);
                string fileDirectory = Path.GetDirectoryName(fullDestinationFilePath);

                if (!string.IsNullOrEmpty(fileDirectory) && !Directory.Exists(fileDirectory))
                {
                    Directory.CreateDirectory(fileDirectory);
                }

                Console.WriteLine($"  Extracting: {relativeFilePath}");
                Console.WriteLine($"  Debugging: hdFileNameW = '{headerData.hdFileNameW}'");
                Console.WriteLine($"  Debugging: relativeFilePath = '{relativeFilePath}'");
                Console.WriteLine($"  Debugging: fullDestinationFilePath = '{fullDestinationFilePath}'");
                Console.WriteLine($"  Debugging: hdFileAttr = {headerData.hdFileAttr}");

                int processResult;
                IntPtr pDestName = IntPtr.Zero;

                try
                {
                    if ((headerData.hdFileAttr & 0x10) != 0)
                    {
                        Console.WriteLine($"  Debugging: Operation for ProcessFileW = PK_SKIP (Directory)");
                        processResult = ProcessFileW(hArc, PK_SKIP, IntPtr.Zero, IntPtr.Zero);
                    }
                    else
                    {
                        pDestName = Marshal.StringToHGlobalUni(fullDestinationFilePath);
                        Console.WriteLine($"  Debugging: Operation for ProcessFileW = PK_EXTRACT (File)");
                        processResult = ProcessFileW(hArc, PK_EXTRACT | PK_OVERWRITE, IntPtr.Zero, pDestName);
                    }
                }
                finally
                {
                    if (pDestName != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(pDestName);
                    }
                }

                if (processResult != E_SUCCESS)
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
            return E_SUCCESS;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"An unexpected error occurred during decompression: {ex.Message}");
            Console.ResetColor();
            return E_UNKNOWN;
        }
        finally
        {
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

    private static void ShowUsage()
    {
        Console.WriteLine("Usage: NkxTool <operation> <sourcePath> <destinationPath>");
        Console.WriteLine("Operations:");
        Console.WriteLine("  compress    - Compresses a folder into an NKX archive.");
        Console.WriteLine("              - <sourcePath>: Path to the folder to compress.");
        Console.WriteLine("              - <destinationPath>: Directory or file for the NKX archive.");
        Console.WriteLine("  decompress - Decompresses an NKX archive.");
        Console.WriteLine("              - <sourcePath>: Path to the .nkx file.");
        Console.WriteLine("              - <destinationPath>: Directory where contents will be extracted.");
        Console.WriteLine("\nExamples:");
        Console.WriteLine("  NkxTool compress \"C:\\MySamples\\Pianos\" \"C:\\MyNkxArchives\"");
        Console.WriteLine("  NkxTool compress \"C:\\MySamples\\Pianos\" \"C:\\MyArchives\\PianoBackup.nkx\"");
        Console.WriteLine("  NkxTool decompress \"C:\\MyNkxArchives\\Pianos.nkx\" \"C:\\ExtractedSamples\"");
    }
}

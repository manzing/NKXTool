using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;

public class Program
{
    private const string PluginDllName = "inNKX.wcx64";

    public const int PK_PACK_SAVE_PATHS = 2;
    public const int PK_SKIP = 0;
    public const int PK_EXTRACT = 2;
    public const int PK_OVERWRITE = 0x0008;
    public const int PK_OM_EXTRACT = 1;

    public const int E_SUCCESS = 0;
    public const int E_END_ARCHIVE = 10;
    
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
    public struct tHeaderDataExW_WCXPlugin
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)] public string hdArcNameW;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)] public string hdFileNameW;
        public int hdFlags;
        public uint hdPackSize;
        public uint hdPackSizeHigh;
        public uint hdUnpSize;
        public uint hdUnpSizeHigh;
        public int hdHostOS;
        public int hdFileCRC;
        public int hdFileTime;
        public int hdUnpVer;
        public int hdMethod;
        public int hdFileAttr;
        public IntPtr hdCmtBuf;
        public int hdCmtBufSize;
        public int hdCmtSize;
        public int hdCmtState;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1024)] public byte[] hdReserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
    public struct tOpenArchiveDataW_WCXPlugin
    {
        public IntPtr ArcName;
        public int OpenMode;
        public int OpenResult;
        public IntPtr CmtBuf;
        public int CmtBufSize;
        public int CmtSize;
        public int CmtState;
    }

    // Signature corrigée (5 paramètres au lieu de 4)
    [DllImport(PluginDllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private static extern int PackFilesW(string PackedFile, string SubPath, string SrcPath, IntPtr AddList, int Flags);

    [DllImport(PluginDllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenArchiveW(ref tOpenArchiveDataW_WCXPlugin OpenArchiveData);

    [DllImport(PluginDllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private static extern int ReadHeaderExW(IntPtr hArc, ref tHeaderDataExW_WCXPlugin HeaderData);

    [DllImport(PluginDllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private static extern int ProcessFileW(IntPtr hArc, int Operation, IntPtr DestPath, IntPtr DestName);

    [DllImport(PluginDllName, CallingConvention = CallingConvention.StdCall)]
    private static extern int CloseArchive(IntPtr hArc);

    public static int Main(string[] args)
    {
        if (args.Length < 3)
        {
            Console.WriteLine("Usage: NkxTool <compress|decompress> <sourcePath> <destinationPath>");
            return 1;
        }

        string operation = args[0].ToLowerInvariant();
        string sourcePath = Path.GetFullPath(args[1]);
        string destinationPath = Path.GetFullPath(args[2]);

        Console.WriteLine($"[NkxTool x64] Operation: {operation}");

        try
        {
            if (operation == "compress")
                return CompressFolder(sourcePath, destinationPath);
            else if (operation == "decompress")
                return DecompressArchive(sourcePath, destinationPath);
            else
                Console.WriteLine("Invalid operation.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Critical Error: {ex.Message}");
        }
        return 1;
    }

    private static int CompressFolder(string sourceFolderPath, string outputNkxFilePath)
    {
        if (!outputNkxFilePath.EndsWith(".nkx", StringComparison.OrdinalIgnoreCase))
            outputNkxFilePath = Path.Combine(outputNkxFilePath, new DirectoryInfo(sourceFolderPath).Name + ".nkx");

        Directory.CreateDirectory(Path.GetDirectoryName(outputNkxFilePath));

        // Génération de la liste de fichiers avec chemins relatifs
        List<string> filesToPack = new List<string>();
        foreach (string file in Directory.GetFiles(sourceFolderPath, "*", SearchOption.AllDirectories))
        {
            filesToPack.Add(Path.GetRelativePath(sourceFolderPath, file));
        }

        // Sécurisation de la mémoire pour la liste double-null-terminated
        string listStr = string.Join("\0", filesToPack) + "\0\0";
        byte[] listBytes = Encoding.Unicode.GetBytes(listStr);
        IntPtr pAddList = Marshal.AllocHGlobal(listBytes.Length);
        Marshal.Copy(listBytes, 0, pAddList, listBytes.Length);

        try
        {
            // SrcPath doit se terminer par un séparateur pour le plugin WCX
            string srcPath = sourceFolderPath;
            if (!srcPath.EndsWith(Path.DirectorySeparatorChar)) srcPath += Path.DirectorySeparatorChar;

            Console.WriteLine($"Compressing {filesToPack.Count} files...");
            int result = PackFilesW(outputNkxFilePath, null, srcPath, pAddList, PK_PACK_SAVE_PATHS);

            if (result == E_SUCCESS)
                Console.WriteLine($"Success: {outputNkxFilePath}");
            else
                Console.WriteLine($"Plugin failed with code: {result}");
            
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(pAddList);
        }
    }

    private static int DecompressArchive(string sourceNkxPath, string destinationDirPath)
    {
        Directory.CreateDirectory(destinationDirPath);
        
        tOpenArchiveDataW_WCXPlugin openArcData = new tOpenArchiveDataW_WCXPlugin { OpenMode = PK_OM_EXTRACT };
        IntPtr pArcName = Marshal.StringToHGlobalUni(sourceNkxPath);
        openArcData.ArcName = pArcName;

        IntPtr hArc = OpenArchiveW(ref openArcData);
        Marshal.FreeHGlobal(pArcName);

        if (hArc == IntPtr.Zero)
        {
            Console.WriteLine($"Failed to open archive. OpenResult: {openArcData.OpenResult}");
            return 1;
        }

        try
        {
            tHeaderDataExW_WCXPlugin headerData = new tHeaderDataExW_WCXPlugin();
            while (ReadHeaderExW(hArc, ref headerData) != E_END_ARCHIVE)
            {
                string relativePath = headerData.hdFileNameW.Replace('/', Path.DirectorySeparatorChar);
                string fullDestPath = Path.Combine(destinationDirPath, relativePath);
                
                if ((headerData.hdFileAttr & 0x10) != 0) // C'est un dossier
                {
                    ProcessFileW(hArc, PK_SKIP, IntPtr.Zero, IntPtr.Zero);
                }
                else // C'est un fichier
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(fullDestPath));
                    IntPtr pDestName = Marshal.StringToHGlobalUni(fullDestPath);
                    int processResult = ProcessFileW(hArc, PK_EXTRACT, IntPtr.Zero, pDestName);
                    Marshal.FreeHGlobal(pDestName);
                    
                    if (processResult != E_SUCCESS)
                        Console.WriteLine($"Error extracting {relativePath} (Code: {processResult})");
                }
            }
            Console.WriteLine("Decompression successful.");
            return E_SUCCESS;
        }
        finally
        {
            CloseArchive(hArc);
        }
    }
}
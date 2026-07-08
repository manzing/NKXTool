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
    public const int PK_OM_EXTRACT = 1;
    public const int E_SUCCESS = 0;
    public const int E_END_ARCHIVE = 10;
    
    // --- STRUCTURES ---
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct PackDefaultParamStruct
    {
        public int size;
        public uint PluginInterfaceVersionLow;
        public uint PluginInterfaceVersionHi;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DefaultIniName;
    }

    // --- DELEGATES ---
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    public delegate int ProcessDataProcWDelegate(string FileName, int Size);

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    public delegate int ChangeVolProcWDelegate(string ArcName, int Mode);

    private static ProcessDataProcWDelegate _processDataProc = new ProcessDataProcWDelegate(OnProcessData);
    private static ChangeVolProcWDelegate _changeVolProc = new ChangeVolProcWDelegate(OnChangeVol);

    private static int OnProcessData(string FileName, int Size) { return 1; }
    private static int OnChangeVol(string ArcName, int Mode) { return 1; }

    // --- IMPORTS ---
    [DllImport(PluginDllName, CharSet = CharSet.Ansi)]
    private static extern void PackSetDefaultParams(ref PackDefaultParamStruct dps);

    [DllImport(PluginDllName, CharSet = CharSet.Unicode)]
    private static extern int PackFilesW(string PackedFile, string? SubPath, string SrcPath, IntPtr AddList, int Flags);

    [DllImport(PluginDllName, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenArchiveW(ref tOpenArchiveDataW_WCXPlugin OpenArchiveData);

    [DllImport(PluginDllName, CharSet = CharSet.Unicode)]
    private static extern int ReadHeaderExW(IntPtr hArc, ref tHeaderDataExW_WCXPlugin HeaderData);

    [DllImport(PluginDllName, CharSet = CharSet.Unicode)]
    private static extern int ProcessFileW(IntPtr hArc, int Operation, string? DestPath, string? DestName);

    [DllImport(PluginDllName, CharSet = CharSet.Unicode)]
    private static extern void SetProcessDataProcW(IntPtr hArc, ProcessDataProcWDelegate pProcessDataProc);

    [DllImport(PluginDllName, CharSet = CharSet.Unicode)]
    private static extern void SetChangeVolProcW(IntPtr hArc, ChangeVolProcWDelegate pChangeVolProc);

    [DllImport(PluginDllName)]
    private static extern int CloseArchive(IntPtr hArc);

    [STAThread]
    public static int Main(string[] args)
    {
        string exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
        Directory.SetCurrentDirectory(exeDirectory);

        if (args.Length < 2)
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  NkxTool unpack <source.nkx> <destinationFolder>");
            Console.WriteLine("  NkxTool pack <destination.nkx> <sourceFolder_OR_@filelist.txt> [rootPath]");
            Console.WriteLine("  NkxTool list <source.nkx>");
            return 1;
        }

        string operation = args[0].ToLowerInvariant();
        string path1 = Path.GetFullPath(args[1]);

        try
        {
            PackDefaultParamStruct dps = new PackDefaultParamStruct();
            dps.size = Marshal.SizeOf(typeof(PackDefaultParamStruct));
            dps.PluginInterfaceVersionLow = 1;
            dps.PluginInterfaceVersionHi = 2;
            dps.DefaultIniName = Path.Combine(exeDirectory, "inNKX.ini");
            PackSetDefaultParams(ref dps);
        }
        catch { }

        try
        {
            if (operation == "list")
            {
                return ListArchive(path1);
            }
            else if (operation == "unpack" && args.Length >= 3)
            {
                return DecompressArchive(path1, Path.GetFullPath(args[2]));
            }
            else if (operation == "pack" && args.Length >= 3)
            {
                // Pour filelist, on peut avoir besoin d'un chemin de base (rootPath)
                string rootPath = args.Length >= 4 ? Path.GetFullPath(args[3]) : "";
                return CompressFolder(args[2], path1, rootPath);
            }
            else
            {
                Console.WriteLine("Invalid operation or missing arguments.");
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Critical Error: {ex.Message}");
        }
        return 1;
    }

    private static int ListArchive(string sourceNkxPath)
    {
        tOpenArchiveDataW_WCXPlugin openArcData = new tOpenArchiveDataW_WCXPlugin { OpenMode = PK_OM_EXTRACT };
        IntPtr pArcName = Marshal.StringToHGlobalUni(sourceNkxPath);
        openArcData.ArcName = pArcName;

        IntPtr hArc = OpenArchiveW(ref openArcData);
        Marshal.FreeHGlobal(pArcName);

        if (hArc == IntPtr.Zero) return 1;

        try
        {
            tHeaderDataExW_WCXPlugin headerData = new tHeaderDataExW_WCXPlugin();
            while (ReadHeaderExW(hArc, ref headerData) != E_END_ARCHIVE)
            {
                string relativePath = headerData.hdFileNameW.Replace('/', Path.DirectorySeparatorChar);
                if ((headerData.hdFileAttr & 0x10) == 0) // N'afficher que les fichiers, pas les dossiers
                {
                    Console.WriteLine(relativePath);
                }
                ProcessFileW(hArc, PK_SKIP, null, null); // Avancer sans extraire
            }
            return E_SUCCESS;
        }
        finally
        {
            CloseArchive(hArc);
        }
    }

    private static int CompressFolder(string sourceOrList, string outputNkxFilePath, string rootPath)
    {
        if (!outputNkxFilePath.EndsWith(".nkx", StringComparison.OrdinalIgnoreCase))
            outputNkxFilePath += ".nkx";

        Directory.CreateDirectory(Path.GetDirectoryName(outputNkxFilePath) ?? string.Empty);

        List<string> filesToPack = new List<string>();
        string srcPath = "";

        if (sourceOrList.StartsWith("@"))
        {
            // Mode FileList : sourceOrList = @c:\temp\list.txt
            string listFile = sourceOrList.Substring(1);
            if (!File.Exists(listFile)) return 1;
            
            // Le rootPath sert de préfixe "SrcPath" pour le plugin (dossier de base)
            srcPath = string.IsNullOrEmpty(rootPath) ? Path.GetDirectoryName(listFile) ?? "" : rootPath;
            filesToPack.AddRange(File.ReadAllLines(listFile));
        }
        else
        {
            // Mode Dossier classique
            srcPath = Path.GetFullPath(sourceOrList);
            foreach (string file in Directory.GetFiles(sourceOrList, "*", SearchOption.AllDirectories))
            {
                filesToPack.Add(Path.GetRelativePath(sourceOrList, file));
            }
        }

        if (!srcPath.EndsWith(Path.DirectorySeparatorChar.ToString())) 
            srcPath += Path.DirectorySeparatorChar;

        if (filesToPack.Count == 0) return 1;

        List<byte> listBytes = new List<byte>();
        foreach (string file in filesToPack)
        {
            if (string.IsNullOrWhiteSpace(file)) continue;
            listBytes.AddRange(Encoding.Unicode.GetBytes(file));
            listBytes.Add(0); listBytes.Add(0);
        }
        listBytes.Add(0); listBytes.Add(0); 

        byte[] finalBytes = listBytes.ToArray();
        IntPtr pAddList = Marshal.AllocHGlobal(finalBytes.Length);
        Marshal.Copy(finalBytes, 0, pAddList, finalBytes.Length);

        try
        {
            Console.WriteLine($"Packing {filesToPack.Count} files into '{outputNkxFilePath}'...");
            
            try { SetProcessDataProcW(IntPtr.Zero, _processDataProc); } catch { }
            try { SetChangeVolProcW(IntPtr.Zero, _changeVolProc); } catch { }
            
            int result = PackFilesW(outputNkxFilePath, null, srcPath, pAddList, PK_PACK_SAVE_PATHS);

            if (result == E_SUCCESS && File.Exists(outputNkxFilePath))
                Console.WriteLine("Success.");
            else
                Console.WriteLine($"Failed. Code: {result}");
            
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
        
        string destPathW = destinationDirPath;
        if (!destPathW.EndsWith(Path.DirectorySeparatorChar.ToString()))
            destPathW += Path.DirectorySeparatorChar;

        tOpenArchiveDataW_WCXPlugin openArcData = new tOpenArchiveDataW_WCXPlugin { OpenMode = PK_OM_EXTRACT };
        IntPtr pArcName = Marshal.StringToHGlobalUni(sourceNkxPath);
        openArcData.ArcName = pArcName;

        IntPtr hArc = OpenArchiveW(ref openArcData);
        Marshal.FreeHGlobal(pArcName);

        if (hArc == IntPtr.Zero) return 1;

        try
        {
            try { SetProcessDataProcW(hArc, _processDataProc); } catch { }
            try { SetChangeVolProcW(hArc, _changeVolProc); } catch { }

            tHeaderDataExW_WCXPlugin headerData = new tHeaderDataExW_WCXPlugin();
            while (ReadHeaderExW(hArc, ref headerData) != E_END_ARCHIVE)
            {
                string relativePath = headerData.hdFileNameW.Replace('/', Path.DirectorySeparatorChar);
                string fullDestPath = Path.Combine(destinationDirPath, relativePath);
                
                if ((headerData.hdFileAttr & 0x10) != 0)
                {
                    ProcessFileW(hArc, PK_SKIP, null, null);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(fullDestPath));
                    Console.WriteLine($"Unpacking: {relativePath}");
                    ProcessFileW(hArc, PK_EXTRACT, destPathW, relativePath);
                }
            }
            Console.WriteLine("Success.");
            return E_SUCCESS;
        }
        finally
        {
            CloseArchive(hArc);
        }
    }
}

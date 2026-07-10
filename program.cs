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
    private static string NormalizeArchivePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        return path.Trim()
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
    }
    public static int Main(string[] args)
    {
        string exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
        Directory.SetCurrentDirectory(exeDirectory);

        if (args.Length < 2)
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  NkxTool unpack <source_file> <destinationFolder> [@filelist.txt | file1 file2 ...]");
            Console.WriteLine("  NkxTool pack <destination_file> <sourceFolder_OR_@filelist.txt> [rootPath]");
            Console.WriteLine("  NkxTool list <source_file> [outputList.txt]");
            Console.WriteLine("\nSupported extensions: .nkx, .nkr, .nicnt");
            return 1;
        }
        
        string operation = args[0].ToLowerInvariant();
        string path1 = Path.GetFullPath(args[1]);

        // Vérification de l'existence du fichier source pour les commandes de lecture
        if ((operation == "list" || operation == "unpack") && !File.Exists(path1))
        {
            Console.WriteLine($"Error: The source file '{path1}' does not exist.");
            return 1;
        }

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
                string? outList = args.Length >= 3 ? Path.GetFullPath(args[2]) : null;
                return ListArchive(path1, outList);
            }
            else if (operation == "unpack" && args.Length >= 3)
            {
                HashSet<string>? selectedFiles = null;

                if (args.Length >= 4)
                {
                    selectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    for (int i = 3; i < args.Length; i++)
                    {
                        string arg = args[i];

                        if (arg.StartsWith("@"))
                        {
                            string listFile = Path.GetFullPath(arg.Substring(1));
                            if (!File.Exists(listFile))
                            {
                                Console.WriteLine($"Error: File list '{listFile}' does not exist.");
                                return 1;
                            }

                            foreach (string line in File.ReadAllLines(listFile))
                            {
                                string normalized = NormalizeArchivePath(line);
                                if (!string.IsNullOrWhiteSpace(normalized))
                                    selectedFiles.Add(normalized);
                            }
                        }
                        else
                        {
                            string normalized = NormalizeArchivePath(arg);
                            if (!string.IsNullOrWhiteSpace(normalized))
                                selectedFiles.Add(normalized);
                        }
                    }

                    if (selectedFiles.Count == 0)
                    {
                        Console.WriteLine("Error: No files selected for extraction.");
                        return 1;
                    }
                }

                return DecompressArchive(path1, Path.GetFullPath(args[2]), selectedFiles);
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

    private static int ListArchive(string sourceNkxPath, string? outputListPath)
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
            List<string> fileList = new List<string>();

            while (ReadHeaderExW(hArc, ref headerData) != E_END_ARCHIVE)
            {
                string relativePath = headerData.hdFileNameW.Replace('/', Path.DirectorySeparatorChar);
                
                if ((headerData.hdFileAttr & 0x10) == 0) // N'ajouter que les fichiers
                {
                    fileList.Add(relativePath);
                }
                ProcessFileW(hArc, PK_SKIP, null, null);
            }

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
        finally
        {
            CloseArchive(hArc);
        }
    }

    private static int CompressFolder(string sourceOrList, string outputNkxFilePath, string rootPath)
    {
        // 1. Support des extensions multiples (nkx, nkr, nicnt)
        string ext = Path.GetExtension(outputNkxFilePath).ToLowerInvariant();
        if (ext != ".nkx" && ext != ".nkr" && ext != ".nicnt")
            outputNkxFilePath += ".nkx";

        Directory.CreateDirectory(Path.GetDirectoryName(outputNkxFilePath) ?? string.Empty);

        List<string> filesToPack = new List<string>();
        string srcPath = "";

        if (sourceOrList.StartsWith("@"))
        {
            string listFile = sourceOrList.Substring(1);
            if (!File.Exists(listFile)) return 1;
            
            srcPath = string.IsNullOrEmpty(rootPath) ? Path.GetDirectoryName(listFile) ?? "" : rootPath;
            filesToPack.AddRange(File.ReadAllLines(listFile));
        }
        else
        {
            srcPath = Path.GetFullPath(sourceOrList);
            foreach (string file in Directory.GetFiles(sourceOrList, "*", SearchOption.AllDirectories))
            {
                filesToPack.Add(Path.GetRelativePath(sourceOrList, file));
            }
        }

        if (!srcPath.EndsWith(Path.DirectorySeparatorChar.ToString())) 
            srcPath += Path.DirectorySeparatorChar;

        if (filesToPack.Count == 0) return 1;

        // 2. Logique de découpage automatique (Limite à ~1.95 Go)
        long maxSize = 2090000000L; 
        List<List<string>> batches = new List<List<string>>();
        List<string> currentBatch = new List<string>();
        long currentSize = 0;

        foreach (string file in filesToPack)
        {
            if (string.IsNullOrWhiteSpace(file)) continue;
            
            string fullFilePath = Path.Combine(srcPath, file);
            long fileSize = new FileInfo(fullFilePath).Length;

            if (currentSize + fileSize > maxSize && currentBatch.Count > 0)
            {
                batches.Add(currentBatch);
                currentBatch = new List<string>();
                currentSize = 0;
            }
            currentBatch.Add(file);
            currentSize += fileSize;
        }
        if (currentBatch.Count > 0) batches.Add(currentBatch);

        // 3. Compression par lot
        int overallResult = E_SUCCESS;

        for (int i = 0; i < batches.Count; i++)
        {
            string batchOutputPath = outputNkxFilePath;
            
            // Si on a plusieurs lots, on ajoute le suffixe _00, _01...
            if (batches.Count > 1)
            {
                string dir = Path.GetDirectoryName(outputNkxFilePath) ?? "";
                string name = Path.GetFileNameWithoutExtension(outputNkxFilePath);
                string batchExt = Path.GetExtension(outputNkxFilePath);
                batchOutputPath = Path.Combine(dir, $"{name}_{i:D2}{batchExt}");
            }

            List<byte> listBytes = new List<byte>();
            foreach (string file in batches[i])
            {
                listBytes.AddRange(Encoding.Unicode.GetBytes(file));
                listBytes.Add(0); listBytes.Add(0);
            }
            listBytes.Add(0); listBytes.Add(0); 

            byte[] finalBytes = listBytes.ToArray();
            IntPtr pAddList = Marshal.AllocHGlobal(finalBytes.Length);
            Marshal.Copy(finalBytes, 0, pAddList, finalBytes.Length);

            try
            {
                Console.WriteLine($"Packing batch {i + 1}/{batches.Count} ({batches[i].Count} files) into '{batchOutputPath}'...");
                
                try { SetProcessDataProcW(IntPtr.Zero, _processDataProc); } catch { }
                try { SetChangeVolProcW(IntPtr.Zero, _changeVolProc); } catch { }
                
                int result = PackFilesW(batchOutputPath, null, srcPath, pAddList, PK_PACK_SAVE_PATHS);

                if (result == E_SUCCESS && File.Exists(batchOutputPath))
                {
                    Console.WriteLine($"Batch {i + 1} success.");
                }
                else
                {
                    Console.WriteLine($"Batch {i + 1} failed. Code: {result}");
                    overallResult = result;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pAddList);
            }
        }
        return overallResult;
    }
    private static int DecompressArchive(string sourceNkxPath, string destinationDirPath, HashSet<string>? selectedFiles = null)
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
                
                bool isDirectory = (headerData.hdFileAttr & 0x10) != 0;
                string normalizedRelativePath = NormalizeArchivePath(relativePath);

                if (isDirectory)
                {
                    ProcessFileW(hArc, PK_SKIP, null, null);
                }
                else if (selectedFiles != null && !selectedFiles.Contains(normalizedRelativePath))
                {
                    ProcessFileW(hArc, PK_SKIP, null, null);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(fullDestPath)!);
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

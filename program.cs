using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
using System.Threading;
using System.Xml;
using System.Text.RegularExpressions;

public class Program
{
    private const string PluginDllName = "inNKX.wcx64";
    public const int PK_PACK_SAVE_PATHS = 2;
    public const int PK_SKIP = 0;
    public const int PK_EXTRACT = 2;
    public const int PK_OM_EXTRACT = 1;
    public const int E_SUCCESS = 0;
    public const int E_END_ARCHIVE = 10;
    // Formats supportés
    private static readonly HashSet<string> SupportedArchiveExtensions =
    new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".nkx", ".nkr", ".nicnt", ".nks"
    };
    
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
        Directory.SetCurrentDirectory(exeDirectory);

        // Transformation des arguments en liste pour extraire facilement le flag -y
        List<string> argsList = new List<string>(args);
        bool overwrite = argsList.Remove("-y") || argsList.Remove("-Y");

        if (argsList.Count < 1)
        {
            ShowUsage();
            return 1;
        }

        string operation = argsList[0].ToLowerInvariant();

        // Commande update
        if (operation == "update")
        {
            string defaultXmlPath = @"C:\Program Files\Common Files\Native Instruments\Service Center";
            return UpdateUserDb(defaultXmlPath);
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
        Console.WriteLine("  NkxTool update");
        Console.WriteLine("\nOptions:");
        Console.WriteLine("  -y : Overwrite existing files without skipping (unpack only)");
        Console.WriteLine("\nSupported extensions: .nkx, .nkr, .nicnt, .nks");
    }

    // --- METHODE UPDATE ---
    private static int UpdateUserDb()
    {
        string commonFiles = Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles);
        string xmlPath = Path.Combine(commonFiles, "Native Instruments", "Service Center", "NativeAccess.xml");

        if (!File.Exists(xmlPath))
        {
            Console.WriteLine($"Error: NativeAccess.xml not found at {xmlPath}");
            return 1;
        }

        string exeFolder = AppDomain.CurrentDomain.BaseDirectory;
        string dbPath = Path.Combine(exeFolder, "nklibs_info.userdb");

        Console.WriteLine($"Parsing XML file: {xmlPath}...");

        StringBuilder sb = new StringBuilder();

        try
        {
            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.Load(xmlPath);

            XmlNodeList? productNodes = xmlDoc.SelectNodes("//Product");
            
            if (productNodes != null)
            {
                foreach (XmlNode node in productNodes)
                {
                    string? snpid = node.SelectSingleNode("SNPID")?.InnerText?.Trim();
                    XmlNode? psNode = node.SelectSingleNode("ProductSpecific");
                    
                    if (string.IsNullOrEmpty(snpid) || psNode == null) continue;

                    string? jdx = psNode.SelectSingleNode("JDX")?.InnerText?.Trim();
                    string? hu = psNode.SelectSingleNode("HU")?.InnerText?.Trim();
                    string regKey = node.SelectSingleNode("RegKey")?.InnerText?.Trim() ?? "Unknown";
                    string company = node.SelectSingleNode("Company")?.InnerText?.Trim() ?? "Unknown";

                    if (string.IsNullOrEmpty(jdx) || string.IsNullOrEmpty(hu)) continue;

                    // Fonction locale pour écrire un bloc facilement
                    void AppendEntry(string id)
                    {
                        sb.AppendLine($"[{id}]");
                        sb.AppendLine($"JDX={jdx}");
                        sb.AppendLine($"HU={hu}");
                        sb.AppendLine($"RegKey={regKey}");
                        sb.AppendLine($"Company={company}");
                        sb.AppendLine();
                    }

                    // 1. On écrit TOUJOURS la valeur brute originale (ex: "8H3", "224", ou "540")
                    AppendEntry(snpid);

                    // 2. Si c'est purement numérique, on calcule l'Hexa sur 3 caractères (X3) et on l'ajoute
                    if (Regex.IsMatch(snpid, @"^\d+$") && int.TryParse(snpid, out int snpidInt))
                    {
                        // Le "X3" force un format hexadécimal avec des zéros à gauche (ex: 224 -> "0E0")
                        string hexSnpid = snpidInt.ToString("X3");
                        
                        // On ajoute le bloc seulement s'il est différent de l'original
                        if (hexSnpid != snpid)
                        {
                            AppendEntry(hexSnpid);
                        }
                    }
                }
            }

            // Force ANSI (Windows-1252) encoding
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Encoding ansiEncoding = Encoding.GetEncoding(1252);
            
            File.WriteAllText(dbPath, sb.ToString(), ansiEncoding);
            
            Console.WriteLine($"Success. Generated file: {dbPath}");
            return E_SUCCESS;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Update Error: {ex.Message}");
            return 1;
        }
    }

    private static int ListArchive(string sourceNkxPath, string? outputListPath)
    {
        // ... (code inchangé)
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
        // ... (code inchangé)
        string ext = Path.GetExtension(outputNkxFilePath);

        if (string.IsNullOrWhiteSpace(ext))
        {
            outputNkxFilePath += ".nkx";
        }
        else if (!SupportedArchiveExtensions.Contains(ext))
        {
            Console.WriteLine($"Warning: Unsupported output extension '{ext}', defaulting to .nkx");
            outputNkxFilePath = Path.ChangeExtension(outputNkxFilePath, ".nkx");
        }

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

        int overallResult = E_SUCCESS;

        for (int i = 0; i < batches.Count; i++)
        {
            string batchOutputPath = outputNkxFilePath;
            
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

    private static int DecompressArchive(string sourceNkxPath, string destinationDirPath, HashSet<string>? selectedFiles, bool overwrite)
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
            int extractedCount = 0;
            int matchedCount = 0;
            int archiveFileCount = 0;
            HashSet<string> foundFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (ReadHeaderExW(hArc, ref headerData) != E_END_ARCHIVE)
            {
                string relativePath = headerData.hdFileNameW.Replace('/', Path.DirectorySeparatorChar);
                string fullDestPath = Path.Combine(destinationDirPath, relativePath);
                
                if ((headerData.hdFileAttr & 0x10) != 0)
                {
                    // C'est un dossier, on l'ignore (le plugin avance le curseur)
                    ProcessFileW(hArc, PK_SKIP, null, null);
                    continue;
                }

                archiveFileCount++;
                foundFiles.Add(relativePath);

                if (selectedFiles != null && !selectedFiles.Contains(relativePath))
                {
                    ProcessFileW(hArc, PK_SKIP, null, null);
                    continue;
                }

                matchedCount++;
                string fullDestDir = Path.GetDirectoryName(fullDestPath) ?? "";
                if (!string.IsNullOrEmpty(fullDestDir))
                    Directory.CreateDirectory(fullDestDir);

                // --- GESTION DE LA COLLISION ---
                if (!overwrite && File.Exists(fullDestPath))
                {
                    Console.WriteLine($"Skipping (already exists): {relativePath}");
                    ProcessFileW(hArc, PK_SKIP, null, null);
                    continue;
                }

                Console.WriteLine($"Unpacking: {relativePath}");

                int result = ProcessFileW(hArc, PK_EXTRACT, destPathW, relativePath);
                if (result == E_SUCCESS)
                {
                    extractedCount++;
                }
                else
                {
                    Console.WriteLine($"Warning: extraction failed for '{relativePath}' (code {result}).");
                }
            }

            if (selectedFiles == null)
            {
                Console.WriteLine($"Success. Extracted {extractedCount}/{archiveFileCount} file(s).");
                return E_SUCCESS;
            }

            List<string> missingFiles = new List<string>();
            foreach (string selected in selectedFiles)
            {
                if (!foundFiles.Contains(selected))
                    missingFiles.Add(selected);
            }

            Console.WriteLine($"Success. Extracted {extractedCount} selected file(s) out of {matchedCount} matched file(s).");

            if (missingFiles.Count > 0)
            {
                Console.WriteLine($"Warning: {missingFiles.Count} requested file(s) were not found in archive:");
                foreach (string missing in missingFiles)
                {
                    Console.WriteLine($"  MISSING: {missing}");
                }
            }

            return E_SUCCESS;
        }
        finally
        {
            CloseArchive(hArc);
        }
    }
}

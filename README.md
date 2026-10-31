# NkxTool - Native Kontakt Archive CLI Utility & Engine

NkxTool is a high-performance, 100% native 64-bit .NET 10 utility and library for managing Native Instruments Kontakt archives. Built entirely in modern C# (`NKXTool.Core`), it operates completely standalone without requiring any external C++ plugin or Total Commander WCX dependencies.

## 🚀 Key Features

- **Multi-Format Support:** Native reading, parsing, and extraction for `.nkx` (Audio Samples), `.nkr` (Resources/Scripts/Pictures), `.nicnt` (Monolith Library Info & Artwork), and legacy `.nks` (Kontakt 1/2 containers).
- **Multi-Threaded Extraction:** High-speed parallel extraction engine utilizing modern thread pooling, with per-file progress reporting and detailed error tracking.
- **Smart Packing (Auto-Split):** Generates compliant Kontakt archives with automatic splitting at the 1.95 GB (`2,090,000,000` bytes) volume threshold (`_00.nkx`, `_01.nkx`, etc.).
- **Filelist Packing:** Pack archives dynamically using a text file containing relative paths (`@filelist.txt`), ideal for automated batch pipelines and audio conversions.
- **Decryption Database (`nklibs.db`):** Fully integrated AES-ECB hardware-accelerated decryption engine powered by an INI key database. Keys can be populated or synchronized automatically from `NativeAccess.xml` using `nkxtool update`.
- **Pure Standalone Distribution:** Compiles to a single executable (`NKXTool.exe`). No WCX DLLs, no external dependencies.

## 📋 Requirements & Runtime Files

### For Usage:
1. **`NKXTool.exe`** - Standalone executable (incorporates `NKXTool` and `NKXTool.Core`).
2. **`nklibs.db`** (Optional / Recommended) - Key database for decrypting protected commercial Kontakt libraries. Can be automatically created/updated with `NKXTool update`.

### For Development / Building from source:
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)

## 🛠️ Usage

Use `NkxTool.exe` with one of the main commands: `list`, `unpack`, `pack`, `update`, `dump`, `nki-infoscan`, or `nki-version`.

### 1. List Archive Contents
Outputs the relative paths of all files contained inside an archive:
```cmd
NkxTool list <source_file> [outputList.txt]

# Examples:
NkxTool list "C:\Library\Samples_1.nkx"
NkxTool list "C:\Library\Samples_1.nkx" "C:\temp\filelist.txt"
```

### 2. Unpack (Decompress)
Extracts the entire content of an archive into the specified destination folder, recreating the original directory structure:
```cmd
NkxTool unpack <source_file> <destinationFolder> [-y]

# Examples:
NkxTool unpack "C:\Library\Samples_1.nkx" "C:\ExtractedSamples\"
NkxTool unpack "C:\Library\Samples_1.nkx" "C:\ExtractedSamples\" -y
```

### 3. Pack (Compress)
Creates a new archive from a source folder or a text file containing a list of relative paths:
```cmd
NkxTool pack <destination_file> <sourceFolder_OR_@filelist.txt> [rootPath]

# Example A: Pack an entire folder
NkxTool pack "C:\NewLibrary\Piano.nkx" "C:\ExtractedSamples\"

# Example B: Pack using a file list
NkxTool pack "C:\NewLibrary\Piano.nkx" "@C:\temp\filelist.txt" "C:\ExtractedSamples\"
```

### 4. Update Key Database
Synchronize or generate `nklibs.db` by scanning the system's `NativeAccess.xml` file (or a specified custom XML):
```cmd
NkxTool update
NkxTool update -f "C:\Path\To\NativeAccess.xml"
```

## 🏗️ Compilation

To compile into a single standalone executable:
```bash
dotnet publish NKXTool.csproj -c Release -r win-x64 /p:PublishSingleFile=true
```
The resulting `NKXTool.exe` is located in `bin\Release\net10.0\win-x64\publish\`.

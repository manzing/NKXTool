# NkxTool - Native Kontakt Archive CLI Utility & Engine

NkxTool is a high-performance, 100% native 64-bit .NET 10 utility and library for managing Native Instruments Kontakt archives. Built entirely in modern C# (`NKXTool.Core`), it operates completely standalone without requiring any external C++ plugin or Total Commander WCX dependencies.

## 🚀 Key Features

- **Multi-Format Support:** Native reading, parsing, and extraction for `.nkx` (Audio Samples), `.nkr` (Resources/Scripts/Pictures), `.nicnt` (Monolith Library Info & Artwork), legacy `.nks` (Kontakt 1/2 containers) and Kontakt monolith patches/containers (`.nki`, `.nkm`, `.nkb`, `.nkg`).
- **Multi-Threaded Extraction:** High-speed parallel extraction engine utilizing modern thread pooling, with per-file progress reporting and detailed error tracking.
- **Smart Packing (Auto-Split):** Generates compliant Kontakt archives with automatic splitting at the 1.95 GB (`2,090,000,000` bytes) volume threshold (`_00.nkx`, `_01.nkx`, etc.).
- **Filelist Packing:** Pack archives dynamically using a text file containing relative paths (`@filelist.txt`), ideal for automated batch pipelines and audio conversions.
- **Decryption Database (`nklibs.db`):** Fully integrated AES-ECB hardware-accelerated decryption engine powered by an INI key database. Keys can be populated or synchronized automatically from `NativeAccess.xml` using `nkxtool update`.
- **Registry Keys:** Library keys are read (read-only) from `HKLM\SOFTWARE\Native Instruments\<Library name>` in both the 64-bit and 32-bit registry views, and are the first source consulted: an installed, working library necessarily carries the right keys.
- **Monolith Containers:** Containers whose file starts with a proprietary sample header (`0x7FA89012`, as shipped by Embertone) are read too: the root directory chunk is located after that header and directory/file entries are resolved from their recorded absolute offsets.
- **Legacy Containers:** Kontakt 1 `.nks` monoliths are indexed with fixed-size Latin1 directory records, and protected ones use one of 32 static 16-byte keys applied from the payload's absolute offset; both layouts are handled natively.
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
Synchronize or generate `nklibs.db` by scanning the system's `NativeAccess.xml` file (or a specified custom XML), or by downloading the current product hints directly from Native Instruments:
```cmd
NkxTool update
NkxTool update --online
NkxTool update -f "C:\Path\To\NativeAccess.xml"
```

`update` merges into the existing database: keys found in the source are added or refreshed, while entries that are absent from the source (for example keys added by hand) are kept. Each ambiguous 3-character SNPID is also stored in its hexadecimal spelling, so archives referencing either notation are decrypted.

When decrypting, key resolution is layered: the Native Instruments registry first (`HKLM\SOFTWARE\Native Instruments\<Library name>`, looked up by archive name, parent folder name, `ContentDir`/`InstallDir` folder name, `Name`, `SNPID` or `RegKey`), then `nklibs.db` (by archive name, then by parent/grand-parent folder name), then the `.nicnt` file shipped next to the archive. The registry is never modified.

## 🏗️ Compilation

To compile into a single standalone executable:
```bash
dotnet publish NKXTool.csproj -c Release -r win-x64 /p:PublishSingleFile=true
```
The resulting `NKXTool.exe` is located in `bin\Release\net10.0\win-x64\publish\`.

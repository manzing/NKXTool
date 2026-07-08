# NkxTool - Advanced Kontakt Archive CLI Utility

NkxTool is a robust, 64-bit command-line utility for managing Native Instruments Kontakt archives. It acts as a specialized wrapper around the `inNKX.wcx64` plugin, allowing you to list files, extract, and seamlessly pack libraries.

## 🚀 Key Features

- **Multi-Format Support:** Read and write `.nkx` (Audio/Samples), `.nkr` (Resources/Scripts), and `.nicnt` (Library Info) containers.
- **Smart Packing (Auto-Split):** Automatically handles the strict 1.95 GB (`2,090,000,000` bytes) size limit of the NKX format. If a source folder exceeds this limit, NkxTool automatically splits the output into `_00.nkx`, `_01.nkx`, etc.
- **Filelist Support:** Pack archives dynamically using a text file containing relative paths (`@filelist.txt`), perfect for scripted audio conversions or selective repacking.
- **COM STA Threading:** Fully initializes Windows COM components natively, ensuring the plugin can properly search for decryption keys and generate `.userdb` files.
- **Modern Architecture:** Built with C# on .NET 10 (win-x64), completely standalone.

## 📋 Requirements

-   [.NET 10.0 SDK](https://dotnet.microsoft.com/download) (For building from source)
-   `inNKX.wcx64` 64-bit plugin file (Must be placed in the project root before building, or in the same directory as the compiled executable).

## 🛠️ Usage

Open your terminal and use `NkxTool.exe` with one of the three main commands: `list`, `unpack`, or `pack`.

### 1. List Archive Contents
Outputs the relative paths of all files contained inside an archive. You can optionally save this output to a text file.
```cmd
NkxTool list <source_file> [outputList.txt]

# Examples:
NkxTool list "C:\Library\Samples_1.nkx"
NkxTool list "C:\Library\Samples_1.nkx" "C:\temp\filelist.txt"
```

### 2. Unpack (Decompress)
Extracts the entire content of an archive into the specified destination folder, recreating the original directory structure.
```cmd
NkxTool unpack <source_file> <destinationFolder>

# Example:
NkxTool unpack "C:\Library\Samples_1.nkx" "C:\ExtractedSamples\"
```

### 3. Pack (Compress)
Creates a new archive from a source folder or a text file containing a list of relative paths.
```cmd
NkxTool pack <destination_file> <sourceFolder_OR_@filelist.txt> [rootPath]

# Example A: Pack an entire folder
NkxTool pack "C:\NewLibrary\Piano.nkx" "C:\ExtractedSamples\"

# Example B: Pack using a file list (requires '@')
# If the text file is located alongside the source samples, rootPath is optional.
NkxTool pack "C:\NewLibrary\Piano.nkx" "@C:\temp\filelist.txt" "C:\ExtractedSamples\"
```

## 🏗️ Compilation

To compile the project into a single, optimized executable:

1. Place your 64-bit `inNKX.wcx` file in the root directory next to `NkxTool.csproj`.
2. Run the following .NET CLI command:
```bash
dotnet publish -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true
```
The compiled `NkxTool.exe` will be located in the `bin\Release\net10.0\win-x64\publish\` directory.

## ⚠️ Notes on Decryption
NkxTool acts as a bridge. If an archive is protected, the underlying WCX plugin will attempt to locate the decryption key on your Windows system using standard Registry/COM calls. If the library is not properly registered on your system, the files may still extract, but they could be unreadable or empty, depending on the plugin's internal behavior.

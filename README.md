# NkxTool - A Direct NKX Compression/Decompression Utility

This is a command-line utility for compressing folders into Kontakt NKX archives and decompressing NKX archives,
by directly interacting with the `inNKX.wcx` plugin. 

## Features

- **Compress:** Pack a folder (including its subdirectories and files) into a `.nkx` archive.
- **Decompress:** Extract the contents of a `.nkx` archive to a specified directory.

## Requirements

-   [.NET SDK](https://dotnet.microsoft.com/download) (Version 6.0 or newer recommended, e.g., .NET 8.0)
-   `inNKX.wcx` plugin file (must be placed in the project root before building).

## Setup and Compilation

1.  **Clone this repository:**
    ```bash
    git clone [https://github.com/YourUsername/NkxTool.git](https://github.com/YourUsername/NkxTool.git)
    cd NkxTool
    ```

2.  **Place `inNKX.wcx`:**
    Download or copy your `inNKX.wcx` plugin file into the **root directory of this project** (where `NkxTool.csproj` is located).

3.  **Compile the project:**
    Open your terminal (Command Prompt, PowerShell, or Git Bash) in the `NkxTool` project root and run:
    ```bash
    dotnet publish -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true
    ```
    This command will:
    -   `publish`: Create a ready-to-deploy output.
    -   `-c Release`: Build in Release mode (optimized, smaller).
    -   `-r win-x64`: Target Windows 64-bit runtime.
    -   `--self-contained false`: Assume the .NET runtime is installed on the target machine (makes the executable smaller).
    -   `/p:PublishSingleFile=true`: Create a single executable file.

4.  **Find the executable:**
    The compiled executable (`NkxTool.exe`) and the `inNKX.wcx` plugin (copied automatically by the build process) will be located in the `bin\Release\netX.0\win-x64\publish\` directory (where `X.0` is your .NET target framework version, e.g., `net6.0`).

## Usage

Navigate to the directory where `NkxTool.exe` was published (e.g., `bin\Release\net8.0\win-x64\publish\`) in your terminal.

```bash
# Compress a folder into an NKX archive
NkxTool.exe compress "<path_to_source_folder>" "<path_to_output_directory>"

# Example:
NkxTool.exe compress "C:\MySamples\AwesomeSynth" "C:\MyNkxArchives"
# This will create "AwesomeSynth.nkx" inside "C:\MyNkxArchives"

# Decompress an NKX archive
NkxTool.exe decompress "<path_to_nkx_file>" "<path_to_output_directory>"

# Example:
NkxTool.exe decompress "C:\MyNkxArchives\AwesomeSynth.nkx" "C:\ExtractedSamples"
# This will extract the contents of "AwesomeSynth.nkx" into "C:\ExtractedSamples"

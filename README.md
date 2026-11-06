# NKXTool - Kontakt Archive Tool

**Version 2.0.0** - fully native engine.

NKXTool is a standalone command-line tool for Windows that works with the archive files shipped with Native Instruments Kontakt libraries: it lists what a library contains, extracts the content to ordinary files, and rebuilds archives from a folder or a file list. It ships as a single executable - no installer, no plugin, no external dependency.

---

## What it can open

| Extension | Content |
|---|---|
| `.nkx` | Audio samples of a library |
| `.nkr` | Resources: scripts, images, impulse responses, documentation |
| `.nicnt` | Library information and artwork |
| `.nks` | Older, small Kontakt monoliths |
| `.nki`, `.nkm`, `.nkb`, `.nkg` | Instrument, multiscript, database and group files |

Archives that are split across several volumes (`_00.nkx`, `_01.nkx`, ...) are recognised: point the tool at any volume and the whole set is read as one library.

---

## Features

- **List** - show every file contained in an archive, on screen or in a text file.
- **Extract** - unpack a whole archive, or a selection of its files, into a folder while recreating the original directory tree.
- **Pack** - build a new archive from a folder, or from a text file listing the files to include; volumes are split automatically at the Kontakt size limit.
- **Key database** - keep `nklibs.db` up to date so that protected libraries are opened without any manual step. Keys are collected from the libraries installed on the machine and can also be refreshed from Native Instruments.
- **Inspect patches** - report the Kontakt version a `.nki` instrument was saved with, and scan a patch for its readable text.
- **Reusable core** - the same engine is exposed as `NKXTool.Core` for use from other .NET programs.

---

## Requirements

- Windows 64-bit.
- `NKXTool.exe`, nothing else.
- `nklibs.db` (optional but recommended): the key database used for protected libraries. Create or update it with `NKXTool update`.

---

## Usage

```
NKXTool <command> [arguments]
```

| Command | Purpose |
|---|---|
| `list` | list the contents of an archive |
| `unpack` | extract an archive |
| `pack` | create an archive |
| `update` | create or refresh the key database |
| `nki-infoscan` | print the readable text of a `.nki` patch |
| `nki-version` | print only the Kontakt version of a `.nki` patch |

### List the contents

```cmd
NKXTool list <archive> [output.txt]

NKXTool list "C:\Library\Samples_00.nkx"
NKXTool list "C:\Library\Samples_00.nkx" "C:\temp\filelist.txt"
```

### Extract

```cmd
NKXTool unpack <archive> <destination> [-y]

NKXTool unpack "C:\Library\Samples_00.nkx" "C:\Extracted\"
NKXTool unpack "C:\Library\Samples_00.nkx" "C:\Extracted\" -y
```

`-y` overwrites existing files without asking. The destination folder is created if needed.

### Create an archive

```cmd
NKXTool pack <archive> <folder or @filelist.txt> [root]

NKXTool pack "C:\New\Piano.nkx" "C:\Extracted\"
NKXTool pack "C:\New\Piano.nkx" "@C:\temp\filelist.txt" "C:\Extracted\"
```

A file list contains one relative path per line, which makes it easy to build an archive from a selection produced by `list`.

### Update the key database

```cmd
NKXTool update
NKXTool update --online
NKXTool update -f "C:\Path\To\NativeAccess.xml"
```

`update` merges into the existing database: keys that are found are added or refreshed, and entries that are not in the current source - keys added by hand, for instance - are kept. Once a library is installed on the machine, `update` is all that is needed before extracting it.

### Inspect a patch

```cmd
NKXTool nki-infoscan "C:\Library\Instrument.nki"
NKXTool nki-version "C:\Library\Instrument.nki"
```

`nki-version` prints nothing but the version number, so it can be used in scripts.

---

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```cmd
dotnet publish NKXTool.csproj -c Release -r win-x64 -p:PublishSingleFile=true
```

The executable is written to `bin\Release\net10.0\win-x64\publish\`.

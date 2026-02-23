# Apple Music RPC

A modern, highly resilient, and zero-footprint Discord Rich Presence integration for Apple Music on Windows.

## Overview

Apple Music RPC bridges the gap between the native Windows Apple Music application and Discord. It securely extracts current playback metadata directly from the Windows Runtime (WinRT) API and broadcasting it to your Discord profile in real-time.

Built from the ground up to replace outdated polling scripts, this v2.0 release introduces a robust C# `.NET` extractor paired with a secure Node.js (TypeScript) core, ensuring zero CPU overhead, flawless metadata extraction, and immunity against malicious process injection.

## Features

*   **Zero-Click Updates**: Features a smart C# Launcher that automatically applies updates in the background.
*   **Native Windows Integration**: Utilizes the modern `Windows.Media.Control` API via a lightweight C# background process. No PowerShell execution, no VBScript injection, and no temporary files.
*   **Zero-Dependency Execution**: The entire stack is compiled into standalone executables.
*   **Resilient API Strategy**: Implements the Circuit Breaker pattern (via `opossum`) to prevent rate-limiting bans.
*   **Algorithmic Fallbacks**: Ensures a 100% cover art success rate through a multi-tier fallback system:
    1.  iTunes Search API (Primary)
    2.  Deezer Public API (Secondary)
    3.  Algorithmic avatar generation (Tertiary)
*   **Native "Spotify-like" UI**: Presents a clean, full-screen album art interface on Discord without intrusive application icons.

---

## ⚡ For Users: Installation Guide

If you just want to use the application without dealing with code, installation is extremely simple.

1.  Go to the [Releases page](../../releases/latest) of this repository.
2.  Download the **`Apple Music RPC Installer.exe`** file.
3.  Double-click on the installer.

**That's it.** The installer will invisibly:
*   Add the application to your Windows startup registry.
*   Download the latest package and install it locally in your `%APPDATA%`.
*   Keep your application up to date automatically every time you start your computer. You never have to download another file again.

---

## 💻 For Developers: Development Workflow

Prerequisites for compiling from source:
*   Node.js (v20+)
*   .NET 9.0 SDK
*   `@yao-pkg/pkg` (for final bundling of the core)

The application architecture relies on three distinct programs:
1.  **Updater (`/src/AppleMusicRPC_Updater/`)**: The C# launcher querying Github's API to download and patch files.
2.  **Core (`/src/core.ts`)**: The Node.js worker communicating with Discord IPC and APIs.
3.  **MediaExtractor (`/src/MediaExtractor/`)**: The C# script extracting WinRT metadata.

Here is the complete workflow to fork, modify, and build the project from scratch:

**1. Clone the repository**
```bash
git clone git@github.com:Kendral444/Apple-Music-RPC.git
cd Apple-Music-RPC
```

**2. Install Node dependencies**
```bash
npm install
```

**3. Modify and Compile the Native Extractor (C#)**
Before testing the core, ensure the C# extractor `.exe` is built to be spawned by the TypeScript application:
```bash
dotnet publish src/MediaExtractor/MediaExtractor.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

**4. Start in Development Mode**
Test your changes in real-time before packaging:
```bash
npx tsc
node dist/core.js
```

**5. Package the Final Core Executable**
Bundle everything into a single transportable `core-bin.exe`:
```bash
npm run build
npx @yao-pkg/pkg . --output core-bin.exe
```

---

## Contributing

Contributions, issues, and feature requests are highly welcome!
Feel free to check the [issues page](https://github.com/Kendral444/Apple-Music-RPC/issues) if you want to contribute.

If you plan to implement major changes, please open an issue first to discuss what you would like to change. 

## Contact & Support

If you need help, have suggestions, or just want to chat:
*   **Discord User**: `kendrxl444`
*   **Discord Server**: [Join the Community Server](https://discord.gg/PPJPS5MWT3)

## License
MIT License.

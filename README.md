# Apple Music RPC

A modern, highly resilient, and zero-footprint Discord Rich Presence integration for Apple Music on Windows.

## Overview

Apple Music RPC bridges the gap between the native Windows Apple Music application and Discord. It securely extracts current playback metadata directly from the Windows Runtime (WinRT) API and broadcasting it to your Discord profile in real-time.

Built from the ground up to replace outdated polling scripts, this v2.0 release introduces a robust C# `.NET` extractor paired with a secure Node.js (TypeScript) core, ensuring zero CPU overhead, flawless metadata extraction, and immunity against malicious process injection.

## Features

*   **Native Windows Integration**: Utilizes the modern `Windows.Media.Control` API via a lightweight C# background process. No PowerShell execution, no VBScript injection, and no temporary files.
*   **Zero-Dependency Execution**: The entire stack is compiled into a single, standalone executable (`core-bin.exe`) encapsulating both the Node.js runtime and the C# extractor.
*   **Resilient API Strategy**: Implements the Circuit Breaker pattern (via `opossum`) to prevent rate-limiting bans.
*   **Algorithmic Fallbacks**: Ensures a 100% cover art success rate through a multi-tier fallback system:
    1.  iTunes Search API (Primary)
    2.  Deezer Public API (Secondary)
    3.  Algorithmic avatar generation (Tertiary)
*   **Clean Architecture**: Structured in strict TypeScript with Zod validation for IPC payloads, ensuring crash-free deserialization.
*   **Native "Spotify-like" UI**: Presents a clean, full-screen album art interface on Discord without intrusive application icons.

## Architecture

The application runs two localized processes:
1.  **MediaExtractor (C# / .NET 9)**: Subscribes to WinRT media events and outputs strict JSON payloads via standard output (`stdout`).
2.  **Core (Node.js / TypeScript)**: Parses the IPC stream, handles remote HTTP requests for cover art with caching and circuit breaking, and maintains the Discord RPC WebSocket connection.

## Development Step-by-Step

Prerequisites:
*   Node.js (v20+)
*   .NET 9.0 SDK
*   `@yao-pkg/pkg` (for final bundling)

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
If you make changes inside `/src/MediaExtractor/`, compile it first so the core has access to the updated `.exe`:
```bash
dotnet publish src/MediaExtractor/MediaExtractor.csproj -c Release -r win-x64 --self-contained true
```

**4. Modify and Compile the Core (TypeScript)**
If you make changes to the Discord formatting or the APIs inside `/src/`, run the TypeScript compiler:
```bash
npm run build
```

**5. Start in Development Mode**
Test your changes in real-time before packaging:
```bash
node dist/core.js
```

**6. Package the Final Executable**
Bundle everything into a single transportable `core-bin.exe`:
```bash
npx @yao-pkg/pkg . --output core-bin.exe
```

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

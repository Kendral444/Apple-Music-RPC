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

## Development

Prerequisites:
*   Node.js (v20+)
*   .NET 9.0 SDK
*   `@yao-pkg/pkg` (for final bundling)

### Building from source

1.  **Compile the Native Extractor**:
    ```bash
    dotnet publish src/MediaExtractor/MediaExtractor.csproj -c Release -r win-x64 --self-contained true
    ```
2.  **Compile the Core**:
    ```bash
    npm install
    npm run build
    ```
3.  **Package the Executable**:
    ```bash
    npx @yao-pkg/pkg . --output core-bin.exe
    ```

## License
MIT License.

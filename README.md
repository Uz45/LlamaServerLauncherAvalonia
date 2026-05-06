# LlamaServerLauncher

![LlamaServerLauncher](docs/images/preview.png)

A cross-platform desktop application for launching and managing [llama.cpp](https://github.com/ggerganov/llama.cpp) server instances with an intuitive graphical interface.

Built with [Avalonia UI](https://avaloniaui.net/) and .NET 8.

## Features

### Server Configuration
- **Executable Path** — Select the `llama-server` binary, or download llama.cpp directly from the app
- **Model Selection** — Choose a specific model file (.gguf) or set a models directory
- **Network Settings** — Configure host address (default: 127.0.0.1) and port (default: 8080)

### Model Parameters
- Context size (`-c`, `--ctx-size`)
- Number of threads (`-t`, `--threads`)
- GPU layers (`-ngl`, `--gpu-layers`, `--n-gpu-layers`)
- Batch size (`-b`, `--batch-size`)
- UBatch size (`-ub`, `--ubatch-size`)
- MMProj path (`-mm`, `--mmproj`)
- Cache type K (`-ctk`, `--cache-type-k`)
- Cache type V (`-ctv`, `--cache-type-v`)
- Parallel slots (`-np`, `--parallel`)
- Timeout (`-to`, `--timeout`)
- Seed (`-s`, `--seed`)

### Generation Parameters
- Temperature (`--temp`, `--temperature`)
- Max tokens (`-n`, `--predict`, `--n-predict`)
- Min-P sampling (`--min-p`)
- Top-K sampling (`--top-k`)
- Top-P sampling (`--top-p`)
- Repeat penalty (`--repeat-penalty`)
- Presence penalty (`--presence-penalty`)
- Frequency penalty (`--frequency-penalty`)
- Reasoning mode (`-rea`, `--reasoning`)
- Reasoning budget (`--reasoning-budget`)

### Advanced Options
- Flash Attention (`-fa`, `--flash-attn`)
- Continuous batching (`-cb`, `--cont-batching`)
- WebUI (`--webui`, `--no-webui`)
- Embedding mode (`--embedding`, `--embeddings`)
- Slots management (`--slots`, `--no-slots`)
- Metrics endpoint (`--metrics`)
- Cache prompt (`--cache-prompt`, `--no-cache-prompt`)
- Context shift (`--context-shift`, `--no-context-shift`)
- Memory lock (`--mlock`)
- Memory map (`--mmap`, `--no-mmap`)
- API key authentication (`--api-key`)
- Alias (`-a`, `--alias`)
- Custom command-line arguments (with toggleable enable/disable per argument)

### Feature Detection
The app automatically parses `llama-server --help` to detect which flags your binary supports. Unsupported options are visually indicated in the UI.

### Logging & Monitoring
- Log file output (`--log-file`)
- Verbose logging (`-v`, `--verbose`)
- Real-time log viewer with auto-scroll
- Server status display with process ID
- Auto-restart on crash

### llama.cpp Integration
- **One-click download** — Download llama.cpp releases directly from GitHub
- **Update notifications** — Automatically checks for new llama.cpp releases
- **Version management** — Install and switch between different versions

### Profile Management
- Save, load, rename, and delete configuration profiles
- Export profiles to JSON, Windows batch (.bat), Linux shell (.sh), or macOS script (.command)
- Import profiles from JSON
- Export/import all profiles as a ZIP archive
- Unsaved changes tracking

### Drag & Drop
Drop files onto the window to import configurations:
- `.json` — Profile import
- `.bat` / `.cmd` — Windows batch file parsing
- `.sh` — Linux shell script parsing
- `.command` — macOS script parsing

### System Tray
- Minimize to system tray on window minimize
- Tray icon menu with server controls (start, stop, unload model, open in browser)
- Double-click tray icon to restore window

### Localization
- English
- Russian

### UI Customization
- Adjustable font size (S, M, L, XL)
- Auto-fit height mode (window auto-sizes to content)
- Collapsible log panel and tab panel
- Window position and size persistence

## Requirements

- .NET 8.0 Runtime or self-contained build
- [llama.cpp](https://github.com/ggerganov/llama.cpp/releases) server binary (`llama-server`), or download it from within the app

## Installation

1. Download the latest release from the [releases page](https://github.com/pytraveler/LlamaServerLauncherAvalonia/releases) for your platform
2. Put executable file to your desired location
3. Run `LlamaServerLauncher`

## Build from Source

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Build Commands

```bash
# Debug build
dotnet build LlamaServerLauncher.csproj

# Linux
dotnet publish LlamaServerLauncher.csproj -c Release -r linux-x64 -o ./publish/linux-x64

# Windows
dotnet publish LlamaServerLauncher.csproj -c Release -r win-x64 -o ./publish/win-x64

# macOS
dotnet publish LlamaServerLauncher.csproj -c Release -r osx-x64 -o ./publish/osx-x64
```

## Usage

1. Click **Download llama.cpp** to download the binary, or click **Browse** next to **Executable** and select your `llama-server`
2. Click **Browse** next to **Model** and select your model file (.gguf), or set a models directory
3. Configure additional parameters as needed
4. Click **Start Server** to launch llama-server
5. Monitor logs in the **Log Output** section
6. Use **Open in Browser** to open the llama-server WebUI

### Managing Profiles

To save current settings as a profile:
1. Enter a name in the profile input field or select an existing profile from the dropdown
2. Click **Save**

To load a saved profile:
1. Select the profile from the dropdown
2. Click **Load**

To export configurations:
- Use **Export** to save as JSON, batch file (.bat), shell script (.sh), or macOS script (.command)
- Use **Export All** to save all profiles as a ZIP archive
- Use **Import** to load a single profile from JSON
- Use **Import All** to load all profiles from a ZIP archive
- Drag and drop `.json`, `.bat`, `.cmd`, `.sh`, or `.command` files onto the window

## Architecture

- **Framework**: Avalonia 12.0.1 (.NET 8.0)
- **Pattern**: MVVM (Model-View-ViewModel)
- **Build**: Self-contained single-file executable

### Project Structure
```
LlamaServerLauncher/
├── Models/                   # Data models and command-line building
│   ├── ServerConfiguration   # All llama-server parameters + KnownArguments mapping
│   ├── CommandLineBuilder    # Constructs full llama-server command line
│   ├── CommandLineParser     # Tokenizes and parses arguments (handles quotes, JSON, arrays)
│   ├── AppSettings           # Persistent application settings
│   └── ProfileInfo           # Profile metadata
├── ViewModels/               # MVVM view models
│   ├── MainViewModel         # Main application logic and state
│   ├── DownloadDialogViewModel
│   ├── RelayCommand          # Custom ICommand implementation
│   └── AsyncRelayCommand
├── Services/                 # Business logic services
│   ├── LlamaServerService    # Process management, HTTP slots/model queries
│   ├── ILlamaServerService   # Service interface
│   ├── ConfigurationService  # Profile and settings persistence (JSON)
│   ├── LlamaCppDownloadService # Downloads llama.cpp releases from GitHub
│   ├── LlamaHelpParserService  # Parses --help output for feature detection
│   ├── LogService            # Application and server log management
│   └── WindowsFileDialogs    # File/folder picker abstractions
├── Converters/               # UI value converters
├── Resources/                # Localization (Strings.resx, Strings.ru.resx)
├── MainWindow.axaml          # Main window with drag-and-drop support
├── DownloadDialogWindow.axaml
├── AboutDialogWindow.axaml
└── App.axaml                 # App entry point, tray icon, culture handling
```

## License

MIT License - See LICENSE file for details.

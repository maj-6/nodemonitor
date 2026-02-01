# NodeMonitor

WPF-based serial monitor and PlatformIO frontend for embedded development.

## Features

- **4 Serial Monitors** - Monitor up to 4 devices simultaneously in a 2x2 grid
- **Auto Board Detection** - Devices self-identify via JSON boot messages
- **PlatformIO Integration** - Build and upload with hotkeys
- **Board Management** - Associate boards with PlatformIO projects
- **Dark Theme** - Developer-focused UI

## Requirements

- Windows 10/11
- .NET 8.0 SDK
- PlatformIO Core (CLI)

## Build

```bash
dotnet build
dotnet run
```

Or publish as single-file executable:
```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

## Keyboard Shortcuts

| Key | Action |
|-----|--------|
| F5 | Build selected board's project |
| F6 | Upload to selected board |
| F7 | Build all configured projects |
| F8 | Upload to all configured boards |
| Esc | Cancel current operation |

## Board Identification

Devices can self-identify by sending this JSON on boot:

```json
{"id":"CubeCell","board":"HTCC-AB01"}
```

The monitor will automatically detect this and update the board label.

## Configuration

Settings are stored in `%APPDATA%\NodeMonitor\config.json`:
- PlatformIO path
- Board configurations (ID, type, port, baud, project path, environment)

## Tabs

### Serial
Main monitor view with 4 serial terminals and build output.

### Boards
Configure board associations with PlatformIO projects.

### Settings
Configure PlatformIO path and view keyboard shortcuts.

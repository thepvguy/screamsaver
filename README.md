# Screamsaver

A Windows parental-control application that monitors microphone volume and blacks out the screen when a child yells too loud during gaming. Configurable via a PIN-protected system tray icon.

## How It Works

1. A Windows Service runs in the background as `LocalSystem`, continuously sampling the default microphone.
2. When the RMS volume exceeds the configured threshold (default: -20 dBFS), the service sends a trigger over a named pipe.
3. The system tray app receives the trigger and instantly covers all monitors with a configurable overlay (color, image, opacity).
4. The overlay fades back out over a configurable duration. A cooldown period prevents repeated triggers.
5. All settings and the stop/pause function are locked behind a parent-set PIN.

## Prerequisites

| Tool | Version | Notes |
|---|---|---|
| Windows 10/11 | 64-bit | Required |
| .NET 8 SDK | 8.0+ | [Download](https://dotnet.microsoft.com/download) |
| WiX Toolset CLI | v6 | See below |

Install the WiX toolset once:

```powershell
dotnet tool install --global wix
```

## Building

### Quick build (Debug, no installer)

```powershell
dotnet build
```

### Build the MSI installer (Release)

The build script publishes all projects as self-contained single-file executables, then compiles the MSI:

```powershell
powershell -ExecutionPolicy Bypass -File Screamsaver.Installer/Build.ps1
```

The MSI is written to `Screamsaver.Installer/Screamsaver.msi`.

> **Before shipping:** Change the compiled-in recovery password in `Screamsaver.Core/Security/RecoveryPassword.cs`. Run `RecoveryPassword.GenerateArrays("YourNewPassword")` in a debug session, replace the `_encoded` and `_key` arrays with the output, then delete the helper method. Keep the plaintext password in a secure location — it cannot be recovered from the binary without effort.

## Installation

1. Run `Screamsaver.msi` as an administrator.
2. The installer will:
   - Install the `ScreamsaverService` Windows Service (auto-start, LocalSystem).
   - Install the tray app to `%ProgramFiles%\Screamsaver\`.
   - Add the tray app to `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` so it starts for all users.
   - Lock the install directory and registry key so standard users cannot modify or delete them.
3. After installation, the tray icon appears in the system tray.
4. **Immediately set a PIN:** right-click the tray icon → **Settings** → **Change PIN…** Enter the recovery password (`ScreamsaverAdmin1!` by default) as the current PIN, then set a new one. Store the PIN securely.

## Uninstallation

Open **Add or Remove Programs**, find Screamsaver, and click Uninstall. A PIN prompt will appear — the correct PIN or recovery password is required to proceed. If the wrong PIN is entered, the uninstall is cancelled.

## Settings

Right-click the tray icon → **Settings** (requires PIN).

| Setting | Default | Description |
|---|---|---|
| Volume threshold (dBFS) | -20.0 | Loudness level that triggers the overlay. Higher = less sensitive. |
| Cooldown (seconds) | 30 | Minimum time between triggers. |
| Fade-in duration (ms) | 0 | How fast the overlay appears (0 = instant). |
| Fade-out duration (ms) | 5000 | How long the overlay takes to disappear. |
| Max opacity (%) | 100 | Peak darkness of the overlay. |
| Overlay color | #000000 | Fill color (click to choose). |
| Overlay image | _(none)_ | Optional image displayed over the color fill. |

Click **Preview Overlay** to test the current settings without triggering from audio.

## Hardening

The app is designed to resist a child attempting to disable it:

| Threat | Defense |
|---|---|
| Killing the tray app | Service watchdog relaunches it within 5 seconds |
| Stopping the service | Service runs as LocalSystem; requires admin rights |
| Editing registry settings | `HKLM\SOFTWARE\Screamsaver` is read-only for standard users |
| Deleting the install folder | Install directory is read+execute only for standard users |
| Removing the autostart entry | HKLM Run key requires admin to modify |
| Opening settings | All tray menu actions require PIN |
| Uninstalling the app | MSI uninstall requires correct PIN |
| Forgetting the PIN | Compiled-in recovery password provides last-resort access |

## Development

Run the service and tray app together without installing:

```powershell
# Terminal 1 — service runs as console app in debug mode
dotnet run --project Screamsaver.Service

# Terminal 2 — tray app
dotnet run --project Screamsaver.TrayApp
```

To test with the service registered locally:

```powershell
# Must be run as Administrator
$exe = "$(pwd)\Screamsaver.Service\bin\Debug\net8.0-windows\Screamsaver.Service.exe"
sc.exe create ScreamsaverService binPath="$exe" start=auto
sc.exe start ScreamsaverService

# Remove when done
sc.exe stop ScreamsaverService
sc.exe delete ScreamsaverService
```

## Project Structure

```
Screamsaver.sln
├── Screamsaver.Core/          # Settings model, IPC contracts, PIN validation, registry I/O
├── Screamsaver.Service/       # Windows Service: microphone monitor, tray watchdog, pipe server
├── Screamsaver.TrayApp/       # System tray UI: overlay, settings form, pipe listener
├── Screamsaver.UninstallHelper/  # PIN prompt exe invoked by MSI before uninstall
└── Screamsaver.Installer/     # WiX v6 source (.wxs) and build script (Build.ps1)
```

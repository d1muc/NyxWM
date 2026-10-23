# nyxinstaller

Terminal installer for **nyxWM**. It does not build the application on the user's machine:
it downloads a prebuilt archive from GitHub Releases, extracts it, and optionally configures it.

## Install flow

1. Language (English / Russian).
2. Install folder — the installer appends `nyxWM` automatically.
3. Preflight — 64-bit Windows, internet, free space, write access, existing installation.
4. Download `nyxWM-win-x64.zip` from the latest GitHub release (progress bar, optional SHA-256).
5. Extract to a staging folder next to the target.
6. Optional configuration wizard: sensitivity, camera button, auto-center, main modifier, hotkeys.
   Pressing Enter on any question keeps the default.
7. Optional autostart (`HKCU\...\Run`) and Start Menu shortcut.
8. Move staging into the target, write `install.json`, copy `uninstall.exe`.
9. Completion screen with the install path and log location.

Uninstall: `nyxinstaller --uninstall` (or the installed `uninstall.exe`).

## Requirements on the target machine

Nothing but Windows x64 and internet access for the first run. Both the installer and the
application are published self-contained. No git, no .NET SDK, no administrator rights.

## Architecture

```
Program.cs ─ InstallerArguments ─ InstallerHost ─ StepRunner ─ IInstallStep[]
                                                     ├── Services (GitHub, filesystem, autostart, config)
                                                     └── Ui (Spectre.Console, FolderPicker)
```

- `InstallSession` is an immutable record; every step returns a new one.
- Each step returns `StepResult` (`Success` / `Cancelled` / `Failed`); the runner stops on the first
  failure and performs rollback.
- UI is behind `IInstallerUi`, so the flow can be tested with a scripted implementation.
- Strings live in `Localization/Strings-en.json` and `Strings-ru.json` (embedded resources).

## Running the simulation

The installer currently runs as a **visual simulation**: it shows the whole interface (banner,
language, path picker, config wizard, progress bars, completion screen) but does not download,
extract, or install anything.

```powershell
# Interactive: full prompts with arrow-key selection
dotnet run --project nyxinstaller/NyxInstaller.csproj

# Autoplay: the whole flow with default answers, no input (quick preview)
dotnet run --project nyxinstaller/NyxInstaller.csproj -- --autoplay

# Force a language
dotnet run --project nyxinstaller/NyxInstaller.csproj -- --lang ru
```

Colors and animated progress bars require a real terminal (Windows Terminal); with redirected
output they degrade to plain text. The folder dialog (`Browse...`) is the native Windows picker.

## Build

```powershell
dotnet build nyxinstaller/NyxInstaller.csproj
```

Publish the installer (self-contained, single file):

```powershell
dotnet publish nyxinstaller/NyxInstaller.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -o artifacts/installer
```

## Release contract

The installer expects a GitHub release on `d1muc/NyxWM` with this asset:

```
nyxWM-win-x64.zip
├── nyxWM.exe
├── config/config.jsonc
└── Res/Pictures/logo.ico
```

Create it from the application project:

```powershell
dotnet publish nyxWM.csproj -c Release -r win-x64 --self-contained true -o artifacts/nyxWM-win-x64
Compress-Archive -Path artifacts/nyxWM-win-x64/* -DestinationPath nyxWM-win-x64.zip
```

Optionally attach `nyxWM-win-x64.zip.sha256`; the installer verifies it when present.

> The repository has no releases yet, so the download step cannot be tested end-to-end until the
> first release is published.

## Safety

- The staging folder is created next to the target so the final move stays on one volume.
- Zip entries are checked against zip-slip before extraction.
- Uninstall deletes a folder only when it contains `install.json`; protected system paths are refused.
- Everything is logged to `%TEMP%\nyxinstaller\install-<timestamp>.log`.

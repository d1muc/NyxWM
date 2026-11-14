# nyxWM

**A camera for your Windows desktop.** Hold one mouse button and drag — every visible window moves together as a single, smoothly animated canvas. Release, and the desktop glides to a stop. Focus a window, and the whole canvas slides until that window is centered.

Instead of tiling or stacking windows one by one, nyxWM treats the desktop as one big surface you can grab and pan around, like a camera moving over a scene.

On top of the canvas it adds a small desktop shell: a quick-action panel, a Rofi-style application menu, a settings window, toast notifications and borderless terminal windows that live on the same canvas.

![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6?logo=windows&logoColor=white)
![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![License](https://img.shields.io/badge/license-BSD%203--Clause-blue)

---

## Features

### Canvas

- **Canvas panning** — hold the camera button (default: middle mouse) and drag. All visible top-level windows move as one. While dragging, the cursor is re-anchored to its start position, so you can pan indefinitely in any direction.
- **Raw mouse input** — movement bypasses the system pointer acceleration curve, so panning speed matches physical mouse travel. Falls back to cursor polling if raw input is unavailable.
- **Smooth, frame-rate independent motion** — exponential Lerp smoothing gives the canvas inertia instead of jarring jumps, and behaves consistently at any refresh rate. Can be disabled for 1:1 tracking.
- **Refresh-rate locked** — a dedicated high-resolution timer keeps updates in sync with your primary monitor. The refresh rate is detected automatically, with a configurable fallback.
- **Auto-center on focus** — when a window becomes the foreground window, the canvas smoothly shifts so that window sits in the middle of the screen. Can be toggled on and off.
- **Configurable hotkeys** — reset the canvas, center the active window, close the active window, toggle auto-center, toggle Game Mode, and open the panel, menu and terminal.
- **Game Mode** — pauses nyxWM entirely so it never interferes with a game or its anti-cheat. It can also pause automatically when a fullscreen game is detected and resume when you go back to a normal window.
- **Game cache and blacklist** — detected game executables are remembered for instant recognition, while a built-in blacklist prevents browsers, editors, terminals and other desktop apps from being mistaken for games.

### Desktop shell

- **Quick-action panel** — a centred row of three tiles (settings, search, terminal) that fades and scales in. Closes itself in Game Mode.
- **Rofi-style application menu** — glass-backed overlay with a focused search field. Indexes Start Menu shortcuts and UWP / Microsoft Store apps, ranks prefix matches above word-start and mid-name matches, and keeps pinned apps on top. Enter launches, Ctrl+Enter runs as administrator, right-click pins or unpins.
- **Settings window** — every option from the config file in a tabbed UI: Core behaviour, Camera, Windows, Hotkeys, Launch apps, Visuals, Notifications, Terminal. Saving rewrites `config.jsonc` (comments preserved) and applies changes live without restarting.
- **Terminal windows** — borderless windows backed by a Windows pseudo console (ConPTY). ANSI colours, 2000-line scrollback, mouse selection, configurable font, cursor, palette, opacity and shell (`zsh`, `bash`, `powershell`, `cmd`).
- **Toast notifications** — centred or corner-anchored cards for hotkey actions, settings saves, startup and keyboard-layout changes. Suppressed in Game Mode.
- **System tray control** — view status, toggle Game Mode, or exit without editing any files.
- **No dependencies** — plain C# on .NET with WinForms for the UI and raw Win32 P/Invoke for hooks and window management.

## How it works

nyxWM does not reparent windows or change their styles, so applications keep behaving normally.

1. A low-level mouse and keyboard hook watches for the configured **camera button** (for example, the middle mouse button).
2. When the button is pressed, nyxWM snapshots the position of every eligible top-level window.
3. On each timer tick, it applies the same offset to all of those windows using `BeginDeferWindowPos` / `DeferWindowPos` / `EndDeferWindowPos`, which batches the moves into a single window-manager operation.
4. The pointer is warped back to its starting point during the drag, which makes panning unbounded.
5. When the button is released, the remaining distance is eased out with Lerp smoothing until the canvas comes to rest.

**Eligible windows** are visible, not minimized, have a non-empty title, and are not listed in `IgnoredWindowClassNames` (taskbar, desktop, overlays, and similar system surfaces are excluded by default).

Auto-centering uses a `SetWinEventHook` foreground-window hook: when focus changes, nyxWM computes how far the new window's center is from the screen center and moves the entire canvas by that amount.

The quick-action panel, settings window, menu and terminals are ordinary top-level windows, so they join the canvas like any other window. While the settings window or app menu is open, camera dragging is suspended.

## Requirements

- **Windows 10 or 11** (x64). Terminal windows use ConPTY, which needs Windows 10 version 1809 or newer.
- **.NET 10 SDK** to build, or the **.NET 10 Desktop Runtime** to run a framework-dependent build
- **MSYS2** (installed at `C:\msys64`, or pointed to by the `MSYS2_ROOT` environment variable) only if you want the terminal `zsh` or `bash` shells. `powershell` and `cmd` work without it.
- No administrator rights are required for normal use. Windows started elevated can only be moved if nyxWM itself is running elevated.

## Getting started

```powershell
git clone https://github.com/d1muc/NyxWM.git
cd NyxWM
dotnet build -c Release
dotnet run -c Release
```

On first launch, nyxWM creates a default configuration next to the executable:

```
bin/Release/net10.0-windows/config/config.jsonc
```

The program starts minimized to the system tray. Look for the nyxWM icon in the notification area.

Press `Ctrl + Alt + D` to open the quick-action panel, or `Ctrl + Alt + Q` for the application menu.

## Installation

The `nyxinstaller` project is a self-contained GUI installer, published as a single `nyxinstaller.exe`.

- Ten interface languages, with a language picker on the first page.
- Resolves the latest GitHub release and downloads `nyxWM-win-x64.zip` (SHA-256 is verified when a `.sha256` asset is attached).
- Optional configuration wizard: camera sensitivity, camera button, auto-center, main modifier and all hotkeys.
- Optional HKCU autostart entry and Start Menu shortcut.
- Installs to `%LOCALAPPDATA%\Programs\nyxWM` by default and drops `uninstall.exe` next to the application.
- Requires internet access on first run; no git, .NET SDK or administrator rights.

```
nyxinstaller.exe                  # install
nyxinstaller.exe --lang ru        # force UI language (ru or en)
nyxinstaller.exe --local-zip <path>  # install from a local zip instead of GitHub
nyxinstaller.exe --uninstall      # uninstall
```

## Default controls

| Action | Config entry | Default |
| --- | --- | --- |
| Pan the desktop | `CameraMoveButton` | Hold **middle mouse** and drag |
| Quick-action panel | `Hotkeys.ToggleLauncher` | `Ctrl + Alt + D` |
| Application menu | `Hotkeys.ToggleRofiMenu` | `Ctrl + Alt + Q` |
| New terminal window | `Hotkeys.OpenTerminal` | `Ctrl + Alt + Enter` |
| Reset canvas to origin | `Hotkeys.ResetCamera` | `Ctrl + Alt + Space` |
| Toggle auto-center on focus | `Hotkeys.ToggleAutoCenter` | `Ctrl + Alt + T` |
| Close the active window | `Hotkeys.CloseWindow` | `Ctrl + Alt + C` |
| Center the active window | `Hotkeys.CenterActiveWindow` | `Ctrl + Alt + F` |
| Toggle Game Mode | `Hotkeys.ToggleGameMode` | `Ctrl + Alt + G` |

The default `MainMod` is `CONTROL+ALT`; change it to move every default hotkey at once.

> **Note:** "Close active window" sends a standard `WM_CLOSE`, so applications may prompt you to save unsaved work.

## Interface

### Quick-action panel

`Hotkeys.ToggleLauncher` (default `Ctrl + Alt + D`) opens three tiles in the centre of the primary screen:

| Tile | Action |
| --- | --- |
| Sliders | Open the settings window |
| Search | Open the application menu |
| Terminal | Open a new terminal window |

The tiles fade, slide and scale in with a short stagger. Clicking the hotkey again, opening the menu, opening the settings window, or entering Game Mode dismisses them.

### Application menu

`Hotkeys.ToggleRofiMenu` (default `Ctrl + Alt + Q`) opens a Rofi-style overlay. It scans the Start Menu (`.lnk` shortcuts, excluding uninstallers, readmes and similar entries) and UWP / Microsoft Store apps in the background at startup, so the first open is instant.

| Input | Action |
| --- | --- |
| Type | Filter by name; exact, prefix, word-start and mid-name matches rank in that order |
| `↑` / `↓` / `Tab` / `Shift + Tab` | Move selection |
| `Page Up` / `Page Down` | Move a page |
| `Enter` | Launch |
| `Ctrl + Enter` | Launch as administrator (UAC prompt) |
| Right-click a row | Pin to top / unpin |
| `Esc` | Close |

Pinned apps are stored in `config/pinned.json` and sorted to the top of the list. The desktop behind the menu is captured and blurred when `GlassEnabled` is true.

### Settings window

Open it from the quick-action panel. Tabs: **Core behaviour**, **Camera**, **Windows**, **Hotkeys**, **Launch apps**, **Visuals**, **Notifications**, **Terminal**.

- **Save** rewrites `config.jsonc`, regenerating it from the documented template so every option keeps its comment, then reloads it. Hotkeys, camera bind, smoothing, theme and terminal settings apply immediately.
- **Cancel** closes without writing.
- **Reset** restores every setting to its default after a confirmation dialog.
- Camera dragging is suspended while the window is open; hotkeys and normal mouse input keep working.

### Terminal windows

`Hotkeys.OpenTerminal` (default `Ctrl + Alt + Enter`) opens a terminal on the canvas; every press creates another, cascaded from the previous one.

- Backed by a Windows pseudo console (ConPTY) with full ANSI support, 120 × 30 starting grid and 2000 lines of scrollback.
- Mouse wheel scrolls the history; scrolling back suspends auto-scroll until you return to the bottom.
- Drag over text to select; drag the title area to move the window; drag an edge to resize.
- `Ctrl + Shift + C` or `Ctrl + Insert` copies the selection. `Ctrl + Shift + V` or `Shift + Insert` pastes. Right-click copies the selection. `Ctrl + C` copies when text is selected, otherwise it is sent to the shell as an interrupt.
- Font, cursor shape and blink, palette, opacity and shell can all be changed live from the settings window.

### Notifications

Toast cards stack at the top or bottom of the screen (maximum four at once) and show:

- Startup: "nyxWM — Running and active".
- Hotkey feedback: camera reset, auto-center toggled, window closed, window centered, app launched.
- Settings saved or reset.
- Keyboard layout switch, for example English to Russian.

Game Mode hides every visible card and suspends the notification ticker.

### Tray

The tray menu shows the current status (`Running` / `Paused`), toggles Game Mode, and exits. Exiting closes every terminal window and unregisters all hotkeys.

## Configuration

Configuration lives in `config/config.jsonc` next to `nyxWM.exe`. It is JSON with comments and trailing commas allowed. Invalid or missing values are replaced with safe defaults in memory, and hotkeys are validated at startup. These files are created next to the executable:

```
config/config.jsonc     Settings (created on first run)
config/pinned.json      Pinned application-menu entries
cache/gamecache.json    Executables remembered as games
cache/blacklist.json    Executables never treated as games
```

### Settings reference

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `FallbackRefreshRate` | int | `60` | Refresh rate used if monitor detection fails. |
| `LerpRatePerSecond` | float | `8.0` | How quickly windows catch up to the target offset. Higher is snappier, lower is floatier. |
| `SmoothingEnabled` | bool | `true` | Smooth the camera while dragging and ease out on release. `false` tracks the pointer 1:1. |
| `AutoCenterOnFocusDefault` | bool | `true` | Whether auto-center-on-focus is enabled at startup. |
| `PauseOnFullscreen` | bool | `true` | Automatically pause when a fullscreen or borderless game gains focus. |
| `MainMod` | string | `"CONTROL+ALT"` | Modifier combination substituted for the `mainmod` placeholder. |
| `CameraMoveButton` | string | `"MBUTTON"` | Button (or button + key combination) held to pan. |
| `HideCursorWhileMovingCamera` | bool | `true` | Hide the pointer while the camera gesture is active. |
| `CameraSensitivity` | float | `1.0` | Multiplier applied to mouse movement while panning. |
| `IgnoredWindowClassNames` | string[] | Taskbar / desktop / overlay classes | Window classes that are never moved. |
| `Hotkeys` | object | See controls table | Action hotkeys. |
| `LaunchApps` | map | `{}` | Hotkey → program, document, folder or URL to open. |
| `ThemeMode` | string | `"Dark"` | UI mode: `Dark` or `Light`. |
| `Theme` | string | `"Blue"` | Accent colour: `White`, `Black`, `Red`, `Blue`, `Orange`, `Green`, `Purple`, `Cyan`, `Pink`. |
| `GlassEnabled` | bool | `true` | Blur the desktop behind the application menu. `false` uses a solid surface. |
| `WindowOpacity` | float | `0.95` | Opacity of overlays from `0.4` to `1.0`. |
| `UiScale` | float | `1.0` | Interface scale from `0.8` to `1.4`. |
| `SmoothAnimations` | bool | `true` | Fade / slide / scale animations for overlays and notifications. |
| `CornerStyle` | string | `"Rounded"` | Window corners: `Sharp`, `Rounded` or `Smooth`. |
| `Notifications` | object | See below | Toast notification behaviour. |
| `Terminal` | object | See below | Terminal appearance and behaviour. |

#### `Notifications`

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `Enabled` | bool | `true` | Enable or disable all notifications. |
| `Position` | string | `"TopCenter"` | `TopCenter`, `TopLeft`, `TopRight`, `BottomCenter`, `BottomLeft`, `BottomRight`. Also accepts `top-center`, `top_center` and `topcenter`. |
| `DurationMs` | int | `3200` | How long a card stays visible, `800`–`20000` ms. |

#### `Terminal`

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `FontFamily` | string | `"Cascadia Code"` | `Cascadia Code`, `JetBrains Mono`, `Fira Code` or `Consolas`. Missing fonts fall back to the next installed family. |
| `FontSize` | float | `11` | Font size in points, `11`–`18`. |
| `CursorStyle` | string | `"Block"` | `Block`, `Line` or `Underline`. |
| `CursorBlink` | bool | `true` | Blink the cursor while the terminal has focus. |
| `ColorScheme` | string | `"Theme"` | `Theme`, `Custom Accent`, `Dracula`, `Matrix Green`, `Cyberpunk Neon`, `Monokai` or `Nord`. |
| `Opacity` | float | `0.98` | Terminal opacity from `0.4` to `1.0`, independent of `WindowOpacity`. |
| `Shell` | string | `"zsh"` | `zsh` and `bash` come from MSYS2; `powershell` and `cmd` are built into Windows. |
| `Prompt` | string | `""` | Custom prompt. zsh: `PROMPT` (% escapes). bash: `PS1` (\ escapes). cmd: `PROMPT`. PowerShell ignores it. Empty keeps the shell's own prompt. Applies to terminals opened after the change. |
| `AutoScroll` | bool | `true` | Follow new output while the view is at the bottom. |
| `CopyOnSelect` | bool | `false` | Copy a mouse selection to the clipboard on release. |

### Hotkey syntax

Hotkeys are written as `modifier + modifier + key`, for example `mainmod + T`.

- **Modifiers:** `CONTROL`, `ALT`, `SHIFT`, `WIN` (also `LWIN`, `RWIN`)
- **Keys:** `A`–`Z`, `0`–`9`, `F1`–`F12`, `LEFT`, `UP`, `RIGHT`, `DOWN`, `ENTER`, `ESC`, `SPACE`, `TAB`, `BACKSPACE`, `DELETE`, `INSERT`, `HOME`, `END`, `PAGEUP`, `PAGEDOWN`
- **`mainmod`** is replaced with the value of `MainMod` (case-insensitive)

`CameraMoveButton` uses the same syntax and additionally accepts mouse buttons: `LEFT`, `RIGHT`, `MIDDLE`, `MBUTTON`, `XBUTTON1`, `XBUTTON2`. For example: `MBUTTON`, `ALT+LEFT`, `mainmod+MIDDLE`.

### Example

```jsonc
{
  // Core behaviour

  // Refresh rate (Hz) used when the monitor refresh rate cannot be detected.
  "FallbackRefreshRate": 60,

  // Smoothing speed of camera movement. Higher = snappier, lower = smoother.
  "LerpRatePerSecond": 8.0,

  // Smooth the camera while dragging, with inertia on release.
  "SmoothingEnabled": true,

  // Initial state of auto-center-on-focus at startup.
  "AutoCenterOnFocusDefault": true,

  // Pause nyxWM automatically while a fullscreen app is focused.
  "PauseOnFullscreen": true,

  // The combination substituted for "mainmod" in hotkey strings.
  "MainMod": "CONTROL+ALT",

  // Camera

  // Hold this button to pan the desktop.
  "CameraMoveButton": "MBUTTON",

  // Hide the mouse pointer while the camera gesture is active.
  "HideCursorWhileMovingCamera": true,

  // Mouse movement multiplier while panning.
  "CameraSensitivity": 1.0,

  // Windows

  // Windows with these class names are never moved.
  "IgnoredWindowClassNames": [
    "Shell_TrayWnd",
    "Progman",
    "WorkerW",
    "CEF-OSC-WIDGET",
    "NvOverlayWindow"
  ],

  // Hotkeys

  "Hotkeys": {
    "ResetCamera": "mainmod + space",
    "ToggleAutoCenter": "mainmod + t",
    "CloseWindow": "mainmod + c",
    "CenterActiveWindow": "mainmod + f",
    "ToggleGameMode": "mainmod + g",
    "ToggleLauncher": "mainmod + d",
    "ToggleRofiMenu": "mainmod + q",
    "OpenTerminal": "mainmod + enter"
  },

  // Bind hotkeys to programs, documents or URLs.
  "LaunchApps": {
    "mainmod + 1": "C:\\Windows\\notepad.exe",
    "mainmod + 2": "https://github.com/d1muc/NyxWM"
  },

  // Visuals

  "ThemeMode": "Dark",
  "Theme": "Blue",
  "GlassEnabled": true,
  "WindowOpacity": 0.95,
  "UiScale": 1.0,
  "SmoothAnimations": true,
  "CornerStyle": "Rounded",

  // Notifications

  "Notifications": {
    "Enabled": true,
    "Position": "TopCenter",
    "DurationMs": 3200
  },

  // Terminal

  "Terminal": {
    "FontFamily": "Cascadia Code",
    "FontSize": 11,
    "CursorStyle": "Block",
    "CursorBlink": true,
    "ColorScheme": "Theme",
    "Opacity": 0.98,
    "Shell": "zsh",
    "Prompt": "",
    "AutoScroll": true,
    "CopyOnSelect": false
  }
}
```

## Game Mode

Game Mode exists so that nyxWM can coexist with fullscreen games and their anti-cheat software.

- **Manual:** press the Game Mode hotkey or use the tray menu. nyxWM uninstalls its input hooks and drops raw-input registration, stops the render timer, hides notifications and closes the application menu.
- **Automatic:** with `PauseOnFullscreen` enabled, focusing a window that covers at least 90% of the screen *and* has no title bar, or that scores above 40 in the game detector, pauses nyxWM. The detector scores known cached executables, game engine window classes (Unity, Unreal, SDL, GLFW, …), borderless-popup styles, near-fullscreen geometry and game-related path keywords.
- Detected executables are remembered in `cache/gamecache.json`. Known non-game applications are listed in `cache/blacklist.json`, which you can edit freely.
- With automatic pause enabled, focusing a regular window resumes normal operation. To resume manually, press the hotkey again or click the status item in the tray menu.

## Project structure

```
Core/
  Program.cs                 Entry point: tray icon, hotkey registration, message filter
  ConfigManager.cs           JSONC configuration loading, validation and saving
  ConfigTemplate.cs          Documented default config and comment-preserving renderer
  Launcher/                  Quick-action panel, Rofi-style menu, app scanning, pins
  Notifications/             Toast cards, positions, themes, keyboard-layout watcher
  Settings/                  Tabbed settings window and its custom controls
  Terminal/                  ConPTY session, ANSI screen buffer, palette, preferences
Services/
  WindowManager.cs           Canvas offset, Lerp smoothing, auto-center, window movement
  MouseHook.cs               Low-level mouse/keyboard hooks and drag detection
  RawMouseInput.cs           Raw Input device for unaccelerated camera movement
  CursorHider.cs             Hides and restores the pointer during a drag
  GameCacheManager.cs        Game detection heuristics, cache and blacklist
  HighResolutionTimer.cs     Dedicated thread timer for frame-accurate updates
Native/
  WinApi.cs                  Win32 P/Invoke declarations
  MonitorUtils.cs            Refresh-rate and primary-monitor queries
Res/
  Pictures/logo.ico          Application and tray icon
nyxinstaller/                Self-contained GUI installer (separate project)
```

## Building a release

Framework-dependent publish:

```powershell
dotnet publish -c Release -o publish
```

Self-contained single file (no .NET runtime required on the target machine):

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

For day-to-day development, `dotnet build` must complete with **0 errors**.

### Release archive

The installer downloads `nyxWM-win-x64.zip` from the latest GitHub release:

```powershell
dotnet publish nyxWM.csproj -c Release -r win-x64 --self-contained true -o artifacts/nyxWM-win-x64
Compress-Archive -Path artifacts/nyxWM-win-x64/* -DestinationPath nyxWM-win-x64.zip
```

Attach the zip (and optionally `nyxWM-win-x64.zip.sha256`) to a GitHub release.

### Installer

```powershell
dotnet build nyxinstaller/NyxInstaller.csproj
dotnet publish nyxinstaller/NyxInstaller.csproj -c Release -o artifacts/installer
```

The installer project is already configured as self-contained, single-file and win-x64. See [nyxinstaller/README.md](nyxinstaller/README.md) for its internal design.

## Limitations and notes

- Window manipulation is best-effort. UWP apps, elevated processes, some fullscreen games and a few custom-drawn applications may ignore or fight the moves.
- Auto-centering and fullscreen detection use the **primary monitor**.
- Only visible, titled, non-ignored windows take part in panning. Windows opened mid-drag join on the next drag.
- The terminal `zsh` and `bash` shells require MSYS2; without it only `powershell` and `cmd` start.
- nyxWM is a canvas panner, not a tiling window manager: it has no layouts, snapping or per-window rules.
- Because it installs low-level input hooks, some anti-cheat systems may still flag any background input hook. Keep Game Mode enabled (or automatic pause on) when gaming.

## Contributing

Issues and pull requests are welcome. Please keep changes focused, follow the existing code style, and make sure `dotnet build` succeeds before opening a pull request.

## License

Released under the **BSD 3-Clause License**. See [LICENSE.md](LICENSE.md).

In short: you are free to view, use, modify and redistribute this project, including in your own work, as long as you keep the original copyright notice and credit the author. You may not claim the original work as your own, and you may not use the author's name to endorse derivative products.

## Author

**d1muc** — [github.com/d1muc](https://github.com/d1muc)

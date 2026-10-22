# nyxWM

**A camera for your Windows desktop.** Hold one mouse button and drag — every visible window moves together as a single, smoothly animated canvas. Release, and the desktop glides to a stop. Focus a window, and the whole canvas slides until that window is centered.

Instead of tiling or stacking windows one by one, nyxWM treats the desktop as one big surface you can grab and pan around, like a camera moving over a scene.

![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6?logo=windows&logoColor=white)
![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![License](https://img.shields.io/badge/license-BSD%203--Clause-blue)

---

## Features

- **Canvas panning** — hold the camera button (default: middle mouse) and drag. All visible top-level windows move as one. While dragging, the cursor is re-anchored to its start position, so you can pan indefinitely in any direction.
- **Smooth, frame-rate independent motion** — exponential Lerp smoothing gives the canvas inertia instead of jarring jumps, and behaves consistently at any refresh rate.
- **Refresh-rate locked** — a dedicated high-resolution timer keeps updates in sync with your primary monitor. The refresh rate is detected automatically, with a configurable fallback.
- **Auto-center on focus** — when a window becomes the foreground window, the canvas smoothly shifts so that window sits in the middle of the screen. Can be toggled on and off.
- **Configurable hotkeys** — reset the canvas, center the active window, close the active window, toggle auto-center, and toggle Game Mode.
- **Game Mode** — pauses nyxWM entirely so it never interferes with a game or its anti-cheat. It can also pause automatically when a fullscreen game is detected and resume when you go back to a normal window.
- **Game cache and blacklist** — detected game executables are remembered for instant recognition, while a built-in blacklist prevents browsers, editors, terminals and other desktop apps from being mistaken for games.
- **Launch apps with hotkeys** — bind any hotkey to a program, document, folder or URL directly from the config file.
- **System tray control** — view status, toggle Game Mode, or exit without editing any files.
- **No dependencies** — plain C# on .NET with WinForms for the tray and raw Win32 P/Invoke for hooks and window management.

## How it works

nyxWM does not reparent windows or change their styles, so applications keep behaving normally.

1. A low-level mouse and keyboard hook watches for the configured **camera button** (for example, the middle mouse button).
2. When the button is pressed, nyxWM snapshots the position of every eligible top-level window.
3. On each timer tick, it applies the same offset to all of those windows using `BeginDeferWindowPos` / `DeferWindowPos` / `EndDeferWindowPos`, which batches the moves into a single window-manager operation.
4. The pointer is warped back to its starting point during the drag, which makes panning unbounded.
5. When the button is released, the remaining distance is eased out with Lerp smoothing until the canvas comes to rest.

**Eligible windows** are visible, not minimized, have a non-empty title, and are not listed in `IgnoredWindowClassNames` (taskbar, desktop, overlays, and similar system surfaces are excluded by default).

Auto-centering uses a `SetWinEventHook` foreground-window hook: when focus changes, nyxWM computes how far the new window's center is from the screen center and moves the entire canvas by that amount.

## Requirements

- **Windows 10 or 11** (x64)
- **.NET 10 SDK** to build, or the **.NET 10 Desktop Runtime** to run a framework-dependent build
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

## Default controls

| Action | Config entry | Default |
| --- | --- | --- |
| Pan the desktop | `CameraMoveButton` | Hold **middle mouse** and drag |
| Reset canvas to origin | `Hotkeys.ResetCamera` | `Ctrl + Alt + Space` |
| Toggle auto-center on focus | `Hotkeys.ToggleAutoCenter` | `Ctrl + Alt + T` |
| Close the active window | `Hotkeys.CloseWindow` | `Ctrl + Alt + C` |
| Center the active window | `Hotkeys.CenterActiveWindow` | `Ctrl + Alt + F` |
| Toggle Game Mode | `Hotkeys.ToggleGameMode` | `Ctrl + Alt + G` |

The default `mainmod` is `CONTROL+ALT`; change `MainMod` to move every default hotkey at once.

> **Note:** "Close active window" sends a standard `WM_CLOSE`, so applications may prompt you to save unsaved work.

## Configuration

Configuration lives in `config/config.jsonc` next to `nyxWM.exe`. It is JSON with comments and trailing commas allowed. Invalid or missing values are replaced with safe defaults, and hotkeys are validated at startup.

### Settings reference

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `FallbackRefreshRate` | int | `60` | Refresh rate used if monitor detection fails. |
| `LerpRatePerSecond` | float | `8.0` | How quickly windows catch up to the target offset. Higher is snappier, lower is floatier. |
| `AutoCenterOnFocusDefault` | bool | `true` | Whether auto-center-on-focus is enabled at startup. |
| `PauseOnFullscreen` | bool | `true` | Automatically pause when a fullscreen or borderless game gains focus. |
| `MainMod` | string | `"CONTROL+ALT"` | Modifier combination substituted for the `mainmod` placeholder. |
| `CameraMoveButton` | string | `"MBUTTON"` | Button (or button + key combination) held to pan. |
| `CameraSensitivity` | float | `1.0` | Multiplier applied to mouse movement while panning. |
| `IgnoredWindowClassNames` | string[] | Taskbar / desktop / overlay classes | Window classes that are never moved. |
| `Hotkeys` | object | See controls table | Action hotkeys. |
| `LaunchApps` | map | `{}` | Hotkey → program, document, folder or URL to open. |

### Hotkey syntax

Hotkeys are written as `modifier + modifier + key`, for example `mainmod + T`.

- **Modifiers:** `CONTROL`, `ALT`, `SHIFT`, `WIN` (also `LWIN`, `RWIN`)
- **Keys:** `A`–`Z`, `0`–`9`, `F1`–`F12`, `LEFT`, `UP`, `RIGHT`, `DOWN`, `ENTER`, `ESC`, `SPACE`, `TAB`, `BACKSPACE`, `DELETE`, `INSERT`, `HOME`, `END`, `PAGEUP`, `PAGEDOWN`
- **`mainmod`** is replaced with the value of `MainMod`

`CameraMoveButton` uses the same syntax and additionally accepts mouse buttons: `LEFT`, `RIGHT`, `MIDDLE`, `MBUTTON`, `XBUTTON1`, `XBUTTON2`. For example: `MBUTTON`, `ALT+LEFT`, `mainmod+MIDDLE`.

### Example

```jsonc
{
  // Used only if the monitor's refresh rate cannot be detected.
  "FallbackRefreshRate": 60,

  // Window smoothing speed.
  "LerpRatePerSecond": 8,

  // Enable auto-center-on-focus at startup.
  "AutoCenterOnFocusDefault": true,

  // Pause automatically when a fullscreen game is focused.
  "PauseOnFullscreen": true,

  // The combination substituted for "mainmod" in hotkey strings.
  "MainMod": "CONTROL+ALT",

  // Hold this button to pan the desktop.
  "CameraMoveButton": "MBUTTON",

  // Mouse movement multiplier while panning.
  "CameraSensitivity": 1.0,

  // Windows with these class names are never moved.
  "IgnoredWindowClassNames": [
    "Shell_TrayWnd",
    "Progman",
    "WorkerW",
    "CEF-OSC-WIDGET",
    "NvOverlayWindow"
  ],

  "Hotkeys": {
    "ResetCamera": "mainmod + space",
    "ToggleAutoCenter": "mainmod + t",
    "CloseWindow": "mainmod + c",
    "CenterActiveWindow": "mainmod + f",
    "ToggleGameMode": "mainmod + g"
  },

  // Bind hotkeys to programs, documents or URLs.
  "LaunchApps": {
    "mainmod + 1": "C:\\Windows\\notepad.exe",
    "mainmod + 2": "https://github.com/d1muc/NyxWM"
  }
}
```

## Game Mode

Game Mode exists so that nyxWM can coexist with fullscreen games and their anti-cheat software.

- **Manual:** press the Game Mode hotkey or use the tray menu. nyxWM ignores all hotkeys and stops moving windows while paused.
- **Automatic:** with `PauseOnFullscreen` enabled, focusing a window that covers at least 90% of the screen *and* has no title bar, or that scores highly on the game detector, pauses nyxWM. nyxWM also stops filtering input through its low-level hooks.
- Detected executables are remembered in `cache/gamecache.json`. Known non-game applications are listed in `cache/blacklist.json`, which you can edit freely.
- With automatic pause enabled, focusing a regular window resumes normal operation. To resume manually, open the tray menu and click the status item.

## Project structure

```
Core/
  Program.cs          Entry point: tray icon, hotkey registration, message filter
  ConfigManager.cs    JSONC configuration loading, validation and defaults
Services/
  WindowManager.cs    Canvas offset, Lerp smoothing, auto-center, window movement
  MouseHook.cs        Low-level mouse/keyboard hooks and drag detection
  GameCacheManager.cs Game detection heuristics, cache and blacklist
  HighResolutionTimer.cs  Dedicated thread timer for frame-accurate updates
Native/
  WinApi.cs           Win32 P/Invoke declarations
  MonitorUtils.cs     Refresh-rate and primary-monitor queries
Res/
  Pictures/logo.ico   Application and tray icon
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

## Limitations and notes

- Window manipulation is best-effort. UWP apps, elevated processes, some fullscreen games and a few custom-drawn applications may ignore or fight the moves.
- Auto-centering and fullscreen detection use the **primary monitor**.
- Only visible, titled, non-ignored windows take part in panning. Windows opened mid-drag join on the next drag.
- nyxWM is a canvas panner, not a tiling window manager: it has no layouts, snapping or per-window rules.
- Because it installs low-level input hooks, some anti-cheat systems may still flag any background input hook. Keep Game Mode enabled (or automatic pause on) when gaming.

## Contributing

Issues and pull requests are welcome. Please keep changes focused, follow the existing code style, and make sure `dotnet build` succeeds before opening a pull request.

## License

Released under the **BSD 3-Clause License**. See [LICENSE.md](LICENSE.md).

In short: you are free to view, use, modify and redistribute this project, including in your own work, as long as you keep the original copyright notice and credit the author. You may not claim the original work as your own, and you may not use the author's name to endorse derivative products.

## Author

**d1muc** — [github.com/d1muc](https://github.com/d1muc)

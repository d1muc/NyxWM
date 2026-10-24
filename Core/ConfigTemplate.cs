namespace nyxWM
{
    /// <summary>
    /// Documented default contents of config.jsonc.
    /// Written to disk on first run so the file explains itself.
    /// JSONC is supported: // and /* */ comments and trailing commas are allowed.
    /// </summary>
    internal static class ConfigTemplate
    {
        internal const string Json = """
{
  // Core behaviour 

  // Refresh rate (Hz) used when the monitor refresh rate cannot be detected.
  // Type: integer > 0. Default: 60.
  "FallbackRefreshRate": 60,

  // Smoothing speed of camera movement. Higher = snappier, lower = smoother.
  // Type: number > 0. Default: 8.0.
  "LerpRatePerSecond": 8.0,

  // Smooth the camera while dragging, with inertia on release.
  // false = canvas follows the pointer 1:1 with no smoothing.
  // Type: true | false. Default: true.
  "SmoothingEnabled": true,

  // Initial state of auto-center-on-focus at startup.
  // Type: true | false. Default: true.
  "AutoCenterOnFocusDefault": true,

  // Pause nyxWM automatically while a fullscreen app is focused
  // (helps avoid anti-cheat false positives).
  // Type: true | false. Default: true.
  "PauseOnFullscreen": true,

  // Main modifier combo. The token "mainmod" inside every hotkey string is
  // replaced with this value. Modifiers: CONTROL, ALT, SHIFT, WIN (LWIN, RWIN).
  // Example: "CONTROL+ALT".
  "MainMod": "CONTROL+ALT",

  // Camera 

  // Mouse button or key combo that grabs and moves the camera.
  // Mouse buttons: MBUTTON, LEFT, RIGHT, MIDDLE, XBUTTON1, XBUTTON2.
  // Modifiers and keys are allowed, e.g. "ALT+LEFT", "mainmod+MBUTTON".
  // Default: "MBUTTON".
  "CameraMoveButton": "MBUTTON",

  // Multiplier for camera movement (higher = faster). Type: number > 0. Default: 1.0.
  "CameraSensitivity": 1.0,

  // Windows 

  // Window class names nyxWM ignores (never tiles these).
  // Find a class name with Spy++ or AutoHotkey's Window Spy.
  "IgnoredWindowClassNames": [
    "Shell_TrayWnd",
    "Progman",
    "WorkerW",
    "CEF-OSC-WIDGET",
    "NvOverlayWindow"
  ],

  // Hotkeys 

  // Syntax: "mainmod + key" or "CONTROL+ALT+key".
  // Modifiers: CONTROL, ALT, SHIFT, WIN (LWIN, RWIN).
  // Keys: A-Z, 0-9, F1-F12, LEFT, UP, RIGHT, DOWN, ENTER, ESC, SPACE, TAB,
  //       BACKSPACE, DELETE, INSERT, HOME, END, PAGEUP, PAGEDOWN.
  // Invalid entries fall back to their default.
  "Hotkeys": {
    // Reset all windows to their saved camera position.
    "ResetCamera": "mainmod + space",
    // Toggle auto-center-on-focus on/off.
    "ToggleAutoCenter": "mainmod + t",
    // Close the focused window.
    "CloseWindow": "mainmod + c",
    // Center the focused window on screen.
    "CenterActiveWindow": "mainmod + f",
    // Toggle game mode (pause nyxWM).
    "ToggleGameMode": "mainmod + g",
    // Show or hide the quick-action panel (settings, search, terminal).
    "ToggleLauncher": "mainmod + d"
  },

  // Extra hotkeys that launch programs, documents or URLs.
  // Format: "hotkey": "path or URL", same hotkey syntax as above.
  // Example: "mainmod + 1": "C:\\Windows\\System32\\notepad.exe"
  // Entries with an invalid hotkey or empty path are skipped.
  "LaunchApps": {},

  // Visuals 

  // Accent colour of notification cards. Applies live, no restart needed.
  // Values: Blue, Purple (Violet), White, Green, Cyan (LightBlue), Orange,
  //         Red, Yellow. Case-insensitive. Unknown value falls back to Blue.
  "Theme": "Blue",


  // Notifications 
  // Toast notifications.
  "Notifications": {
    // Enable or disable all notifications. Type: true | false.
    "Enabled": true,
    // Screen anchor of the notification stack.
    // Values: TopCenter, TopLeft, TopRight, BottomCenter, BottomLeft, BottomRight.
    // Also accepts "top-center", "top_center" and "topcenter".
    "Position": "TopCenter",
    // How long a notification stays visible, in milliseconds.
    // Type: integer 800-20000. Default: 3200.
    "DurationMs": 3200
  }
}
""";
    }
}

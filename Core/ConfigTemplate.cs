using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

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

        /// <summary>
        /// Renders the live settings back into the documented JSONC document used
        /// by config.jsonc. Comments and section layout are preserved so a save
        /// does not strip the file's self-documentation.
        /// </summary>
        internal static string Render(ConfigSettings settings)
        {
            HotkeySettings hotkeys = settings.Hotkeys ?? new HotkeySettings();
            NotificationSettings notifications = settings.Notifications ?? new NotificationSettings();

            static string Str(string? value) => JsonSerializer.Serialize(value ?? string.Empty);
            static string Num(float value) => value.ToString(CultureInfo.InvariantCulture);
            static string Bool(bool value) => value ? "true" : "false";

            string ignored = BuildIgnoredArray(settings.IgnoredWindowClassNames);
            string launchApps = BuildLaunchApps(settings.LaunchApps);

            var sb = new StringBuilder(4096);

            sb.AppendLine("{");
            sb.AppendLine("  // Core behaviour ");
            sb.AppendLine();
            sb.AppendLine("  // Refresh rate (Hz) used when the monitor refresh rate cannot be detected.");
            sb.AppendLine("  // Type: integer > 0. Default: 60.");
            sb.AppendLine($"  \"FallbackRefreshRate\": {settings.FallbackRefreshRate},");
            sb.AppendLine();
            sb.AppendLine("  // Smoothing speed of camera movement. Higher = snappier, lower = smoother.");
            sb.AppendLine("  // Type: number > 0. Default: 8.0.");
            sb.AppendLine($"  \"LerpRatePerSecond\": {Num(settings.LerpRatePerSecond)},");
            sb.AppendLine();
            sb.AppendLine("  // Smooth the camera while dragging, with inertia on release.");
            sb.AppendLine("  // false = canvas follows the pointer 1:1 with no smoothing.");
            sb.AppendLine("  // Type: true | false. Default: true.");
            sb.AppendLine($"  \"SmoothingEnabled\": {Bool(settings.SmoothingEnabled)},");
            sb.AppendLine();
            sb.AppendLine("  // Initial state of auto-center-on-focus at startup.");
            sb.AppendLine("  // Type: true | false. Default: true.");
            sb.AppendLine($"  \"AutoCenterOnFocusDefault\": {Bool(settings.AutoCenterOnFocusDefault)},");
            sb.AppendLine();
            sb.AppendLine("  // Pause nyxWM automatically while a fullscreen app is focused");
            sb.AppendLine("  // (helps avoid anti-cheat false positives).");
            sb.AppendLine("  // Type: true | false. Default: true.");
            sb.AppendLine($"  \"PauseOnFullscreen\": {Bool(settings.PauseOnFullscreen)},");
            sb.AppendLine();
            sb.AppendLine("  // Main modifier combo. The token \"mainmod\" inside every hotkey string is");
            sb.AppendLine("  // replaced with this value. Modifiers: CONTROL, ALT, SHIFT, WIN (LWIN, RWIN).");
            sb.AppendLine("  // Example: \"CONTROL+ALT\".");
            sb.AppendLine($"  \"MainMod\": {Str(settings.MainMod)},");
            sb.AppendLine();
            sb.AppendLine("  // Camera ");
            sb.AppendLine();
            sb.AppendLine("  // Mouse button or key combo that grabs and moves the camera.");
            sb.AppendLine("  // Mouse buttons: MBUTTON, LEFT, RIGHT, MIDDLE, XBUTTON1, XBUTTON2.");
            sb.AppendLine("  // Modifiers and keys are allowed, e.g. \"ALT+LEFT\", \"mainmod+MBUTTON\".");
            sb.AppendLine("  // Default: \"MBUTTON\".");
            sb.AppendLine($"  \"CameraMoveButton\": {Str(settings.CameraMoveButton)},");
            sb.AppendLine();
            sb.AppendLine("  // Multiplier for camera movement (higher = faster). Type: number > 0. Default: 1.0.");
            sb.AppendLine($"  \"CameraSensitivity\": {Num(settings.CameraSensitivity)},");
            sb.AppendLine();
            sb.AppendLine("  // Windows ");
            sb.AppendLine();
            sb.AppendLine("  // Window class names nyxWM ignores (never tiles these).");
            sb.AppendLine("  // Find a class name with Spy++ or AutoHotkey's Window Spy.");
            sb.Append("  \"IgnoredWindowClassNames\": ");
            sb.Append(ignored);
            sb.AppendLine(",");
            sb.AppendLine();
            sb.AppendLine("  // Hotkeys ");
            sb.AppendLine();
            sb.AppendLine("  // Syntax: \"mainmod + key\" or \"CONTROL+ALT+key\".");
            sb.AppendLine("  // Modifiers: CONTROL, ALT, SHIFT, WIN (LWIN, RWIN).");
            sb.AppendLine("  // Keys: A-Z, 0-9, F1-F12, LEFT, UP, RIGHT, DOWN, ENTER, ESC, SPACE, TAB,");
            sb.AppendLine("  //       BACKSPACE, DELETE, INSERT, HOME, END, PAGEUP, PAGEDOWN.");
            sb.AppendLine("  // Invalid entries fall back to their default.");
            sb.AppendLine("  \"Hotkeys\": {");
            sb.AppendLine("    // Reset all windows to their saved camera position.");
            sb.AppendLine($"    \"ResetCamera\": {Str(hotkeys.ResetCamera)},");
            sb.AppendLine("    // Toggle auto-center-on-focus on/off.");
            sb.AppendLine($"    \"ToggleAutoCenter\": {Str(hotkeys.ToggleAutoCenter)},");
            sb.AppendLine("    // Close the focused window.");
            sb.AppendLine($"    \"CloseWindow\": {Str(hotkeys.CloseWindow)},");
            sb.AppendLine("    // Center the focused window on screen.");
            sb.AppendLine($"    \"CenterActiveWindow\": {Str(hotkeys.CenterActiveWindow)},");
            sb.AppendLine("    // Toggle game mode (pause nyxWM).");
            sb.AppendLine($"    \"ToggleGameMode\": {Str(hotkeys.ToggleGameMode)},");
            sb.AppendLine("    // Show or hide the quick-action panel (settings, search, terminal).");
            sb.AppendLine($"    \"ToggleLauncher\": {Str(hotkeys.ToggleLauncher)}");
            sb.AppendLine("  },");
            sb.AppendLine();
            sb.AppendLine("  // Extra hotkeys that launch programs, documents or URLs.");
            sb.AppendLine("  // Format: \"hotkey\": \"path or URL\", same hotkey syntax as above.");
            sb.AppendLine("  // Example: \"mainmod + 1\": \"C:\\\\Windows\\\\System32\\\\notepad.exe\"");
            sb.AppendLine("  // Entries with an invalid hotkey or empty path are skipped.");
            sb.Append("  \"LaunchApps\": ");
            sb.Append(launchApps);
            sb.AppendLine(",");
            sb.AppendLine();
            sb.AppendLine("  // Visuals ");
            sb.AppendLine();
            sb.AppendLine("  // Accent colour of notification cards. Applies live, no restart needed.");
            sb.AppendLine("  // Values: Blue, Purple (Violet), White, Green, Cyan (LightBlue), Orange,");
            sb.AppendLine("  //         Red, Yellow. Case-insensitive. Unknown value falls back to Blue.");
            sb.AppendLine($"  \"Theme\": {Str(settings.Theme)},");
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("  // Notifications ");
            sb.AppendLine("  // Toast notifications.");
            sb.AppendLine("  \"Notifications\": {");
            sb.AppendLine("    // Enable or disable all notifications. Type: true | false.");
            sb.AppendLine($"    \"Enabled\": {Bool(notifications.Enabled)},");
            sb.AppendLine("    // Screen anchor of the notification stack.");
            sb.AppendLine("    // Values: TopCenter, TopLeft, TopRight, BottomCenter, BottomLeft, BottomRight.");
            sb.AppendLine("    // Also accepts \"top-center\", \"top_center\" and \"topcenter\".");
            sb.Append($"    \"Position\": {Str(notifications.Position)},");
            sb.AppendLine();
            sb.AppendLine("    // How long a notification stays visible, in milliseconds.");
            sb.AppendLine("    // Type: integer 800-20000. Default: 3200.");
            sb.AppendLine($"    \"DurationMs\": {notifications.DurationMs}");
            sb.AppendLine("  }");
            sb.Append("}");

            return sb.ToString();
        }

        private static string BuildIgnoredArray(string[]? classNames)
        {
            string[] names = classNames ?? Array.Empty<string>();
            var sb = new StringBuilder(names.Length == 0 ? "[]" : "[\n");

            for (int i = 0; i < names.Length; i++)
            {
                sb.Append("    ").Append(JsonSerializer.Serialize(names[i]));
                if (i < names.Length - 1)
                    sb.Append(",");

                sb.Append('\n');
            }

            if (names.Length > 0)
                sb.Append("  ]");

            return sb.ToString();
        }

        private static string BuildLaunchApps(Dictionary<string, string>? apps)
        {
            var items = apps ?? new Dictionary<string, string>();

            if (items.Count == 0)
                return "{}";

            var sb = new StringBuilder("{\n");
            int index = 0;

            foreach (KeyValuePair<string, string> entry in items)
            {
                sb.Append("    ")
                  .Append(JsonSerializer.Serialize(entry.Key))
                  .Append(": ")
                  .Append(JsonSerializer.Serialize(entry.Value));

                if (++index < items.Count)
                    sb.Append(',');

                sb.Append('\n');
            }

            sb.Append("  }");
            return sb.ToString();
        }
    }
}

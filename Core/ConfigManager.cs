using System;
using System.IO;
using System.Text.Json;
using System.Text.Encodings.Web;

namespace nyxWM
{
    /// <summary>
    /// Manages loading and accessing application configuration from config.jsonc.
    /// </summary>
    public static class ConfigManager
    {
        private static readonly string ConfigFilePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "config", "config.jsonc");

        private static ConfigSettings? _settings;
        private static readonly object _lock = new();

        /// <summary>
        /// Gets the current configuration settings.
        /// If settings have not been loaded, attempts to load them from file.
        /// </summary>
        public static ConfigSettings Current
        {
            get
            {
                if (_settings == null)
                {
                    lock (_lock)
                    {
                        if (_settings == null)
                        {
                            _settings = LoadSettings();
                        }
                    }
                }
                return _settings!;
            }
        }

        /// <summary>
        /// Loads configuration from config.jsonc.
        /// If the file is missing, creates the config directory and writes a default config file.
        /// If the file is invalid or missing required fields, returns default settings (and may overwrite the file with defaults?).
        /// For missing file, we create the default file on disk.
        /// </summary>
        /// <returns>The loaded configuration settings.</returns>
        private static ConfigSettings LoadSettings()
        {
            try
            {
                if (!File.Exists(ConfigFilePath))
                {
                    Console.WriteLine($"Configuration file not found at {ConfigFilePath}. Creating default configuration.");
                    // Ensure the config directory exists
                    string configDir = Path.GetDirectoryName(ConfigFilePath)!;
                    if (!Directory.Exists(configDir))
                    {
                        Directory.CreateDirectory(configDir);
                    }

                    // Create and save default settings
                    var defaultSettings = CreateDefaultSettings();
                    SaveSettings(defaultSettings);
                    return defaultSettings;
                }

                var options = new JsonSerializerOptions
                {
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                };

                string json = File.ReadAllText(ConfigFilePath);
                var settings = JsonSerializer.Deserialize<ConfigSettings>(json, options);
                if (settings == null)
                {
                    Console.WriteLine($"Failed to deserialize configuration from {ConfigFilePath}. Using default settings.");
                    var defaultSettings = CreateDefaultSettings();
                    // Optionally, we could overwrite the file with defaults, but we'll just return defaults in memory.
                    return defaultSettings;
                }

                // Validate settings and apply defaults for any missing properties.
                ValidateAndApplyDefaults(settings);
                return settings;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading configuration: {ex.Message}. Using default settings.");
                return CreateDefaultSettings();
            }
        }

        /// <summary>
        /// Saves the given settings to the config file.
        /// </summary>
        private static void SaveSettings(ConfigSettings settings)
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                string json = JsonSerializer.Serialize(settings, options);
                File.WriteAllText(ConfigFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving configuration: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates default configuration settings.
        /// </summary>
        private static ConfigSettings CreateDefaultSettings()
        {
            return new ConfigSettings
            {
                FallbackRefreshRate = 60,
                LerpRatePerSecond = 8.0f,
                AutoCenterOnFocusDefault = true,
                PauseOnFullscreen = true,
                MainMod = "CONTROL+ALT",
                CameraMoveButton = "MBUTTON",
                IgnoredWindowClassNames = new[] { "Shell_TrayWnd", "Progman", "WorkerW", "CEF-OSC-WIDGET", "NvOverlayWindow" },
                Hotkeys = new HotkeySettings
                {
                    ResetCamera = "mainmod + space",
                    ToggleAutoCenter = "mainmod + t",
                    CloseWindow = "mainmod + c",
                    CenterActiveWindow = "mainmod + f",
                    ToggleGameMode = "mainmod + g"
                }
            };
        }

        /// <summary>
        /// Validates the settings and applies defaults for any missing or invalid values.
        /// </summary>
        private static void ValidateAndApplyDefaults(ConfigSettings settings)
        {
            bool changed = false;

            if (settings.FallbackRefreshRate <= 0)
            {
                Console.WriteLine($"Invalid fallbackRefreshRate: {settings.FallbackRefreshRate}. Using default 60.");
                settings.FallbackRefreshRate = 60;
                changed = true;
            }

            if (settings.LerpRatePerSecond <= 0)
            {
                Console.WriteLine($"Invalid lerpRatePerSecond: {settings.LerpRatePerSecond}. Using default 8.0.");
                settings.LerpRatePerSecond = 8.0f;
                changed = true;
            }

            // MainMod should not be empty; if empty, set to default.
            if (string.IsNullOrWhiteSpace(settings.MainMod))
            {
                Console.WriteLine("MainMod is empty. Using default CONTROL+ALT.");
                settings.MainMod = "CONTROL+ALT";
                changed = true;
            }

            if (settings.IgnoredWindowClassNames == null || settings.IgnoredWindowClassNames.Length == 0)
            {
                Console.WriteLine("IgnoredWindowClassNames is empty. Using default list.");
                settings.IgnoredWindowClassNames = new[] { "Shell_TrayWnd", "Progman", "WorkerW" };
                changed = true;
            }

            // Validate CameraSensitivity
            if (settings.CameraSensitivity <= 0)
            {
                Console.WriteLine($"Invalid cameraSensitivity: {settings.CameraSensitivity}. Using default 1.0.");
                settings.CameraSensitivity = 1.0f;
                changed = true;
            }

            if (settings.Hotkeys == null)
            {
                Console.WriteLine("Hotkeys section missing. Using default hotkeys.");
                settings.Hotkeys = new HotkeySettings
                {
                    ResetCamera = "mainmod + space",
                    ToggleAutoCenter = "mainmod + t",
                    CloseWindow = "mainmod + c",
                    CenterActiveWindow = "mainmod + f"
                };
                changed = true;
            }
            else
            {
                // Validate each hotkey string by attempting to parse it (after replacing mainmod).
                // We'll create a helper function to validate a hotkey string.
                if (!IsValidHotkeyString(settings.Hotkeys.ResetCamera, settings.MainMod))
                {
                    Console.WriteLine($"Invalid resetCamera hotkey: {settings.Hotkeys.ResetCamera}. Using default.");
                    settings.Hotkeys.ResetCamera = "mainmod + space";
                    changed = true;
                }

                if (!IsValidHotkeyString(settings.Hotkeys.ToggleAutoCenter, settings.MainMod))
                {
                    Console.WriteLine($"Invalid toggleAutoCenter hotkey: {settings.Hotkeys.ToggleAutoCenter}. Using default.");
                    settings.Hotkeys.ToggleAutoCenter = "mainmod + t";
                    changed = true;
                }

                if (!IsValidHotkeyString(settings.Hotkeys.CloseWindow, settings.MainMod))
                {
                    Console.WriteLine($"Invalid closeWindow hotkey: {settings.Hotkeys.CloseWindow}. Using default.");
                    settings.Hotkeys.CloseWindow = "mainmod + c";
                    changed = true;
                }

                if (!IsValidHotkeyString(settings.Hotkeys.CenterActiveWindow, settings.MainMod))
                {
                    Console.WriteLine($"Invalid centerActiveWindow hotkey: {settings.Hotkeys.CenterActiveWindow}. Using default.");
                    settings.Hotkeys.CenterActiveWindow = "mainmod + f";
                    changed = true;
                }

                if (!IsValidHotkeyString(settings.Hotkeys.ToggleGameMode, settings.MainMod))
                {
                    Console.WriteLine($"Invalid toggleGameMode hotkey: {settings.Hotkeys.ToggleGameMode}. Using default.");
                    settings.Hotkeys.ToggleGameMode = "mainmod + g";
                    changed = true;
                }

                // Validate CameraMoveButton
                if (!IsValidCameraMoveButtonString(settings.CameraMoveButton, settings.MainMod))
                {
                    Console.WriteLine($"Invalid cameraMoveButton: {settings.CameraMoveButton}. Using default MBUTTON.");
                    settings.CameraMoveButton = "MBUTTON";
                    changed = true;
                }
            }

            // Validate LaunchApps: each key must be a valid hotkey string and each value a non-empty path.
            if (settings.LaunchApps == null)
            {
                settings.LaunchApps = new Dictionary<string, string>();
            }
            else
            {
                var validated = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in settings.LaunchApps)
                {
                    string hotkey = kvp.Key ?? string.Empty;
                    if (!IsValidHotkeyString(hotkey, settings.MainMod))
                    {
                        Console.WriteLine($"Invalid launch app hotkey skipped: '{kvp.Key}'.");
                        changed = true;
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(kvp.Value))
                    {
                        Console.WriteLine($"Launch app for hotkey '{kvp.Key}' has an empty path and was skipped.");
                        changed = true;
                        continue;
                    }

                    if (validated.ContainsKey(hotkey))
                    {
                        Console.WriteLine($"Duplicate launch app hotkey skipped: '{kvp.Key}'.");
                        changed = true;
                        continue;
                    }

                    validated.Add(hotkey, kvp.Value.Trim());
                }

                settings.LaunchApps = validated;
            }

            if (changed)
            {
                Console.WriteLine("Applied default values for invalid or missing configuration settings.");
                // Optionally save the corrected settings back to file.
                SaveSettings(settings);
            }
        }

        /// <summary>
        /// Checks if a hotkey string is valid after replacing mainmod with the actual mainmod value.
        /// </summary>
        private static bool IsValidHotkeyString(string hotkeyString, string mainmod)
        {
            if (string.IsNullOrWhiteSpace(hotkeyString))
                return false;

            // Replace mainmod placeholder
            string replaced = hotkeyString.Replace("mainmod", mainmod, StringComparison.InvariantCultureIgnoreCase);
            // Split by '+'
            string[] parts = replaced.Split('+', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return false;

            // The last part should be the key, the rest are modifiers.
            string keyPart = parts[parts.Length - 1].Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(keyPart))
                return false;

            // Validate modifier parts (all except last)
            for (int i = 0; i < parts.Length - 1; i++)
            {
                string modifier = parts[i].Trim().ToUpperInvariant();
                if (!IsValidModifier(modifier))
                    return false;
            }

            // Validate key part
            return IsValidKey(keyPart);
        }

        private static bool IsValidModifier(string modifier)
        {
            return modifier switch
            {
                "CONTROL" => true,
                "ALT" => true,
                "SHIFT" => true,
                "WIN" or "LWIN" or "RWIN" => true,
                _ => false
            };
        }

        private static bool IsValidKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;

            // Single character keys (A-Z, 0-9)
            if (key.Length == 1)
            {
                char c = key[0];
                return (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
            }

            // Special keys
            return key switch
            {
                "F1" => true,
                "F2" => true,
                "F3" => true,
                "F4" => true,
                "F5" => true,
                "F6" => true,
                "F7" => true,
                "F8" => true,
                "F9" => true,
                "F10" => true,
                "F11" => true,
                "F12" => true,
                "LEFT" => true,
                "UP" => true,
                "RIGHT" => true,
                "DOWN" => true,
                "ENTER" => true,
                "ESC" => true,
                "SPACE" => true,
                "TAB" => true,
                "BACKSPACE" => true,
                "DELETE" => true,
                "INSERT" => true,
                "HOME" => true,
                "END" => true,
                "PAGEUP" => true,
                "PAGEDOWN" => true,
                _ => false
            };
        }

        /// <summary>
        /// Checks if a camera move button string is valid after replacing mainmod with the actual mainmod value.
        /// Supports single keys and combinations with + (e.g., MBUTTON, alt+left, mainmod+middle, ctrl+m).
        /// </summary>
        private static bool IsValidCameraMoveButtonString(string cameraMoveButtonString, string mainmod)
        {
            if (string.IsNullOrWhiteSpace(cameraMoveButtonString))
                return false;

            // Replace mainmod placeholder
            string replaced = cameraMoveButtonString.Replace("mainmod", mainmod, StringComparison.InvariantCultureIgnoreCase);
            // Split by '+'
            string[] parts = replaced.Split('+', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return false;

            // Validate each part (can be modifier or key)
            foreach (string part in parts)
            {
                string trimmed = part.Trim().ToUpperInvariant();
                if (string.IsNullOrEmpty(trimmed))
                    return false;

                // Check if it's a valid modifier
                if (IsValidModifier(trimmed))
                    continue;

                // Check if it's a valid key
                if (IsValidKey(trimmed))
                    continue;

                // Check for mouse buttons
                if (IsValidMouseButton(trimmed))
                    continue;

                return false;
            }

            return true;
        }

        /// <summary>
        /// Checks if a string represents a valid mouse button.
        /// </summary>
        private static bool IsValidMouseButton(string button)
        {
            return button switch
            {
                "LEFT" => true,
                "RIGHT" => true,
                "MIDDLE" => true,
                "MBUTTON" => true,
                "XBUTTON1" => true,
                "XBUTTON2" => true,
                _ => false
            };
        }
    }

    /// <summary>
    /// Represents the application configuration settings.
    /// </summary>
    public class ConfigSettings
    {
        /// <summary>
        /// Default refresh rate (in Hz) to use if monitor refresh rate detection fails.
        /// </summary>
        public int FallbackRefreshRate { get; set; }

        /// <summary>
        /// Speed of the smoothing animation (lerp) in per second.
        /// </summary>
        public float LerpRatePerSecond { get; set; }

        /// <summary>
        /// Default value for the auto-center-on-focus feature.
        /// </summary>
        public bool AutoCenterOnFocusDefault { get; set; }

        /// <summary>
        /// Whether to automatically pause when a fullscreen application is detected to avoid anti-cheat triggers.
        /// </summary>
        public bool PauseOnFullscreen { get; set; } = true;

        /// <summary>
        /// The main modifier string (e.g., "CONTROL+ALT") that is replaced in hotkey strings.
        /// </summary>
        public string MainMod { get; set; } = string.Empty;

        /// <summary>
        /// The button or key combination used to move the camera (e.g., "MBUTTON", "ALT+LEFT").
        /// </summary>
        public string CameraMoveButton { get; set; } = "MBUTTON";

        /// <summary>
        /// Sensitivity of camera movement (higher = faster movement). Must be > 0.
        /// </summary>
        public float CameraSensitivity { get; set; } = 1.0f;

        /// <summary>
        /// List of window class names to ignore when tiling/moving windows.
        /// </summary>
        public string[] IgnoredWindowClassNames { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Hotkey configurations as strings (e.g., "mainmod + space").
        /// </summary>
        public HotkeySettings Hotkeys { get; set; } = new HotkeySettings();

        /// <summary>
        /// Map of custom hotkeys to the program/document path each one launches.
        /// Keys are hotkey strings (e.g., "mainmod + 1") using the same syntax as <see cref="Hotkeys"/>;
        /// values are executable or document paths (can also be a URL).
        /// </summary>
        public Dictionary<string, string> LaunchApps { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Represents hotkey configurations.
    /// </summary>
    public class HotkeySettings
    {
        /// <summary>
        /// Hotkey for resetting camera position.
        /// </summary>
        public string ResetCamera { get; set; } = string.Empty;

        /// <summary>
        /// Hotkey for toggling auto-center-on-focus.
        /// </summary>
        public string ToggleAutoCenter { get; set; } = string.Empty;

        /// <summary>
        /// Hotkey for closing the active window.
        /// </summary>
        public string CloseWindow { get; set; } = string.Empty;

        /// <summary>
        /// Hotkey for centering the active window.
        /// </summary>
        public string CenterActiveWindow { get; set; } = string.Empty;

        /// <summary>
        /// Hotkey for toggling game mode (pause) for anti-cheat purposes.
        /// </summary>
        public string ToggleGameMode { get; set; } = string.Empty;
    }
}
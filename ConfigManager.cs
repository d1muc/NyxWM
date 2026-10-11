using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace nyxWM
{
    /// <summary>
    /// Manages loading and accessing application configuration from config.json.
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
        /// If the file is missing, invalid, or missing required fields, returns default settings.
        /// </summary>
        /// <returns>The loaded configuration settings.</returns>
        private static ConfigSettings LoadSettings()
        {
            try
            {
                if (!File.Exists(ConfigFilePath))
                {
                    Console.WriteLine($"Configuration file not found at {ConfigFilePath}. Using default settings.");
                    return CreateDefaultSettings();
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
                    return CreateDefaultSettings();
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
        /// Creates default configuration settings.
        /// </summary>
        private static ConfigSettings CreateDefaultSettings()
        {
            return new ConfigSettings
            {
                FallbackRefreshRate = 60,
                LerpRatePerSecond = 8.0f,
                AutoCenterOnFocusDefault = true,
                IgnoredWindowClassNames = new[] { "Shell_TrayWnd", "Progman", "WorkerW" },
                Hotkeys = new HotkeySettings
                {
                    ResetCamera = new Hotkey { Modifiers = "CONTROL", Key = "R" },
                    ToggleAutoCenter = new Hotkey { Modifiers = "CONTROL", Key = "T" }
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

            if (settings.IgnoredWindowClassNames == null || settings.IgnoredWindowClassNames.Length == 0)
            {
                Console.WriteLine("IgnoredWindowClassNames is empty. Using default list.");
                settings.IgnoredWindowClassNames = new[] { "Shell_TrayWnd", "Progman", "WorkerW" };
                changed = true;
            }

            if (settings.Hotkeys == null)
            {
                Console.WriteLine("Hotkeys section missing. Using default hotkeys.");
                settings.Hotkeys = new HotkeySettings
                {
                    ResetCamera = new Hotkey { Modifiers = "CONTROL", Key = "R" },
                    ToggleAutoCenter = new Hotkey { Modifiers = "CONTROL", Key = "T" }
                };
                changed = true;
            }
            else
            {
                if (settings.Hotkeys.ResetCamera == null ||
                    string.IsNullOrWhiteSpace(settings.Hotkeys.ResetCamera.Modifiers) ||
                    string.IsNullOrWhiteSpace(settings.Hotkeys.ResetCamera.Key))
                {
                    Console.WriteLine("Invalid resetCamera hotkey. Using default.");
                    settings.Hotkeys.ResetCamera = new Hotkey { Modifiers = "CONTROL", Key = "R" };
                    changed = true;
                }

                if (settings.Hotkeys.ToggleAutoCenter == null ||
                    string.IsNullOrWhiteSpace(settings.Hotkeys.ToggleAutoCenter.Modifiers) ||
                    string.IsNullOrWhiteSpace(settings.Hotkeys.ToggleAutoCenter.Key))
                {
                    Console.WriteLine("Invalid toggleAutoCenter hotkey. Using default.");
                    settings.Hotkeys.ToggleAutoCenter = new Hotkey { Modifiers = "CONTROL", Key = "T" };
                    changed = true;
                }
            }

            if (changed)
            {
                Console.WriteLine("Applied default values for invalid or missing configuration settings.");
            }
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
        /// List of window class names to ignore when tiling/moving windows.
        /// </summary>
        public string[] IgnoredWindowClassNames { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Hotkey configurations.
        /// </summary>
        public HotkeySettings Hotkeys { get; set; } = new HotkeySettings();
    }

    /// <summary>
    /// Represents hotkey configurations.
    /// </summary>
    public class HotkeySettings
    {
        /// <summary>
        /// Hotkey for resetting camera position.
        /// </>
        public Hotkey ResetCamera { get; set; } = new Hotkey();

        /// <summary>
        /// Hotkey for toggling auto-center-on-focus.
        /// </summary>
        public Hotkey ToggleAutoCenter { get; set; } = new Hotkey();
    }

    /// <summary>
    /// Represents a single hotkey configuration.
    /// </summary>
    public class Hotkey
    {
        /// <summary>
        /// Modifier flags (e.g., "CONTROL", "ALT", "SHIFT", "WIN", or combinations like "CONTROL+SHIFT").
        /// </summary>
        public string Modifiers { get; set; } = string.Empty;

        /// <summary>
        /// Virtual key (e.g., "A", "Z", "F1", etc.).
        /// </summary>
        public string Key { get; set; } = string.Empty;
    }
}
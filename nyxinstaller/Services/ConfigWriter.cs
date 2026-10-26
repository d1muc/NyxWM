using System.Text;
using System.Text.Json;
using NyxInstaller.Models;

namespace NyxInstaller.Services;

/// <summary>
/// Serializes <see cref="ConfigOptions"/> into the exact JSON shape read by
/// <c>nyxWM.ConfigManager</c> (PascalCase property names, no comments).
/// </summary>
public sealed class ConfigWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null
    };

    /// <summary>
    /// Builds the JSON document for <c>config/config.jsonc</c>.
    /// </summary>
    public string BuildJson(ConfigOptions options)
    {
        var document = new ConfigDocument
        {
            AutoCenterOnFocusDefault = options.AutoCenterOnFocusDefault,
            MainMod = options.MainMod,
            CameraMoveButton = options.CameraMoveButton,
            CameraSensitivity = options.CameraSensitivity,
            Hotkeys = new HotkeyDocument
            {
                ResetCamera = options.Hotkeys.ResetCamera,
                ToggleAutoCenter = options.Hotkeys.ToggleAutoCenter,
                CloseWindow = options.Hotkeys.CloseWindow,
                CenterActiveWindow = options.Hotkeys.CenterActiveWindow,
                ToggleGameMode = options.Hotkeys.ToggleGameMode,
                ToggleLauncher = options.Hotkeys.ToggleLauncher,
                ToggleRofiMenu = options.Hotkeys.ToggleRofiMenu,
                OpenTerminal = options.Hotkeys.OpenTerminal
            }
        };

        return JsonSerializer.Serialize(document, SerializerOptions) + Environment.NewLine;
    }

    /// <summary>
    /// Writes <c>config/config.jsonc</c> below <paramref name="installRoot"/>.
    /// </summary>
    public void Write(string installRoot, ConfigOptions options)
    {
        var configDirectory = Path.Combine(installRoot, "config");
        Directory.CreateDirectory(configDirectory);

        var path = Path.Combine(configDirectory, "config.jsonc");
        var temporaryPath = path + ".tmp";

        File.WriteAllText(temporaryPath, BuildJson(options), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaryPath, path, overwrite: true);
    }

    private sealed class ConfigDocument
    {
        public int FallbackRefreshRate { get; set; } = 60;

        public float LerpRatePerSecond { get; set; } = 8.0f;

        public bool SmoothingEnabled { get; set; } = true;

        public bool AutoCenterOnFocusDefault { get; set; } = true;

        public bool PauseOnFullscreen { get; set; } = true;

        public string MainMod { get; set; } = "CONTROL+ALT";

        public string CameraMoveButton { get; set; } = "MBUTTON";

        public float CameraSensitivity { get; set; } = 1.0f;

        public string[] IgnoredWindowClassNames { get; set; } =
        {
            "Shell_TrayWnd",
            "Progman",
            "WorkerW",
            "CEF-OSC-WIDGET",
            "NvOverlayWindow"
        };

        public HotkeyDocument Hotkeys { get; set; } = new();

        public Dictionary<string, string> LaunchApps { get; set; } = new();

        public string ThemeMode { get; set; } = "Dark";

        public string Theme { get; set; } = "Blue";

        public bool GlassEnabled { get; set; } = true;

        public float WindowOpacity { get; set; } = 0.95f;

        public float UiScale { get; set; } = 1.0f;

        public bool SmoothAnimations { get; set; } = true;

        public string CornerStyle { get; set; } = "Rounded";
    }

    private sealed class HotkeyDocument
    {
        public string ResetCamera { get; set; } = "mainmod + space";

        public string ToggleAutoCenter { get; set; } = "mainmod + t";

        public string CloseWindow { get; set; } = "mainmod + c";

        public string CenterActiveWindow { get; set; } = "mainmod + f";

        public string ToggleGameMode { get; set; } = "mainmod + g";

        public string ToggleLauncher { get; set; } = "mainmod + d";

        public string ToggleRofiMenu { get; set; } = "mainmod + q";

        public string OpenTerminal { get; set; } = "mainmod + enter";
    }
}

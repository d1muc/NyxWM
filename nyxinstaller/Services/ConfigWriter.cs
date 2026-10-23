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
        // TODO(step 5): project ConfigOptions into the ConfigSettings shape
        // (CameraSensitivity, CameraMoveButton, AutoCenterOnFocusDefault, MainMod, Hotkeys...)
        // and include FallbackRefreshRate, LerpRatePerSecond, PauseOnFullscreen,
        // IgnoredWindowClassNames, LaunchApps with their defaults.
        throw new NotImplementedException();
    }

    /// <summary>
    /// Writes <c>config/config.jsonc</c> below <paramref name="installRoot"/>.
    /// </summary>
    public void Write(string installRoot, ConfigOptions options)
    {
        // TODO(step 5): Directory.CreateDirectory(installRoot/config), File.WriteAllText.
        throw new NotImplementedException();
    }
}

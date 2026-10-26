namespace NyxInstaller.Models;

/// <summary>
/// Values written into <c>config/config.jsonc</c>. Property names follow
/// <c>nyxWM.ConfigSettings</c> and must not be renamed.
/// </summary>
public sealed record ConfigOptions(
    float CameraSensitivity,
    string CameraMoveButton,
    bool AutoCenterOnFocusDefault,
    string MainMod,
    HotkeyOptions Hotkeys);

/// <summary>
/// Hotkey strings in the same syntax the application parses (for example <c>mainmod + space</c>).
/// </summary>
public sealed record HotkeyOptions(
    string ResetCamera,
    string ToggleAutoCenter,
    string CloseWindow,
    string CenterActiveWindow,
    string ToggleGameMode,
    string ToggleLauncher,
    string ToggleRofiMenu,
    string OpenTerminal);

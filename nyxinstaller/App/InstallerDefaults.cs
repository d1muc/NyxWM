using NyxInstaller.Models;

namespace NyxInstaller.App;

/// <summary>
/// Constants shared by the installer. Change the repository coordinates here only.
/// </summary>
public static class InstallerDefaults
{
    public const string RepositoryOwner = "d1muc";

    public const string RepositoryName = "NyxWM";

    public const string ReleaseAssetName = "nyxWM-win-x64.zip";

    public const string ReleaseAssetPattern = "nyxWM-*-win-x64.zip";

    public const string InstallFolderName = "nyxWM";

    public const string AutostartValueName = "nyxWM";

    public const string MarkerFileName = "install.json";

    public const string UninstallerFileName = "uninstall.exe";

    public const int RequiredFreeSpaceMb = 250;

    /// <summary>
    /// Default install root: <c>%LOCALAPPDATA%\Programs</c>. The installer appends <c>nyxWM</c>.
    /// </summary>
    public static string DefaultInstallRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs");

    /// <summary>
    /// Default configuration offered by the wizard. Mirrors <c>nyxWM.ConfigManager.CreateDefaultSettings</c>.
    /// </summary>
    public static ConfigOptions DefaultConfig { get; } = new(
        CameraSensitivity: 1.0f,
        CameraMoveButton: "MBUTTON",
        AutoCenterOnFocusDefault: true,
        MainMod: "CONTROL+ALT",
        Hotkeys: new HotkeyOptions(
            ResetCamera: "mainmod + space",
            ToggleAutoCenter: "mainmod + t",
            CloseWindow: "mainmod + c",
            CenterActiveWindow: "mainmod + f",
            ToggleGameMode: "mainmod + g",
            ToggleLauncher: "mainmod + d",
            ToggleRofiMenu: "mainmod + q",
            OpenTerminal: "mainmod + enter"));
}

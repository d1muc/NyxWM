namespace NyxInstaller.Models;

/// <summary>
/// Answers collected from the user before installation starts.
/// </summary>
/// <param name="Language">Installer UI language.</param>
/// <param name="InstallPath">Target directory, already including the <c>nyxWM</c> folder name.</param>
/// <param name="RegisterAutostart">Whether to add the HKCU Run entry.</param>
/// <param name="CreateShortcut">Whether to create a Start Menu shortcut.</param>
/// <param name="KeepExistingConfig">Whether an already installed config must be preserved.</param>
/// <param name="LocalZipPath">Optional local release zip; <c>null</c> means download.</param>
public sealed record InstallOptions(
    InstallerLanguage Language,
    string InstallPath,
    bool RegisterAutostart,
    bool CreateShortcut,
    bool KeepExistingConfig,
    string? LocalZipPath);

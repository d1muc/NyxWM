using NyxInstaller.Models;

namespace NyxInstaller.App;

/// <summary>
/// Command line of the installer.
/// </summary>
/// <param name="Uninstall">Run the uninstall flow instead of the install flow.</param>
/// <param name="Language">Language forced by <c>--lang</c>; <c>null</c> asks the user.</param>
/// <param name="LocalZipPath">Release zip taken from disk instead of GitHub.</param>
public sealed record InstallerArguments(
    bool Uninstall,
    InstallerLanguage? Language,
    string? LocalZipPath);

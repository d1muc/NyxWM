namespace NyxInstaller.Models;

/// <summary>
/// Immutable state carried through the install steps. Each step returns a new session.
/// </summary>
/// <param name="Options">User answers collected so far.</param>
/// <param name="Config">Configuration values for the wizard; <c>null</c> until answered.</param>
/// <param name="StagingPath">Temporary extraction directory next to the target.</param>
/// <param name="DownloadedZipPath">Downloaded release archive; <c>null</c> when installing from disk.</param>
/// <param name="TargetPath">Final install directory.</param>
/// <param name="LogPath">Path of the installer log file.</param>
/// <param name="InstalledVersion">Release tag that was installed.</param>
public sealed record InstallSession(
    InstallOptions Options,
    ConfigOptions? Config,
    string? StagingPath,
    string? DownloadedZipPath,
    string TargetPath,
    string LogPath,
    string? InstalledVersion);

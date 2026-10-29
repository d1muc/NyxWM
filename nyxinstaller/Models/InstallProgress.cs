namespace NyxInstaller.Models;

/// <summary>
/// Stage of the installation currently running.
/// </summary>
public enum InstallPhase
{
    Checking,
    Downloading,
    Extracting,
    Configuring,
    Finishing
}

/// <summary>
/// Progress reported by <c>InstallService</c>; <c>Fraction</c> is in the 0..1 range.
/// </summary>
public sealed record InstallProgress(
    double Fraction,
    InstallPhase Phase,
    string? Detail = null);

/// <summary>
/// Successful installation result shown on the completion page.
/// </summary>
public sealed record InstallResult(
    string TargetPath,
    string Version,
    string LogPath,
    bool WasUpdated);

/// <summary>
/// Blocking installation problem with a message ready for the UI.
/// </summary>
public sealed class InstallException : Exception
{
    public InstallException(string message)
        : base(message)
    {
    }

    public InstallException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

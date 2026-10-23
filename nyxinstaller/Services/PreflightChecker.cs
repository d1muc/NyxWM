namespace NyxInstaller.Services;

/// <summary>
/// Result of the environment checks performed before downloading anything.
/// </summary>
/// <param name="IsSupportedOs">64-bit Windows.</param>
/// <param name="HasInternet">GitHub API is reachable.</param>
/// <param name="FreeSpaceMb">Free space on the target volume.</param>
/// <param name="IsTargetWritable">The installer can create the target directory.</param>
/// <param name="ExistingInstallPath">Existing installation, or <c>null</c>.</param>
/// <param name="IsNyxWmRunning">A nyxWM process is currently running.</param>
/// <param name="FailureMessage">First blocking problem, or <c>null</c> when everything passed.</param>
public sealed record PreflightReport(
    bool IsSupportedOs,
    bool HasInternet,
    long FreeSpaceMb,
    bool IsTargetWritable,
    string? ExistingInstallPath,
    bool IsNyxWmRunning,
    string? FailureMessage);

/// <summary>
/// Checks the machine before installation. No dependency installation happens here: the release is
/// self-contained, so nothing needs to be downloaded except the release zip itself.
/// </summary>
public sealed class PreflightChecker
{
    public Task<PreflightReport> CheckAsync(string targetPath, CancellationToken cancellationToken)
    {
        // TODO(step 3): OS/arch, HEAD api.github.com, disk space, write test, marker detection,
        // running process detection.
        throw new NotImplementedException();
    }
}

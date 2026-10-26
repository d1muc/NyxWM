using NyxInstaller.App;

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
    private readonly FileSystemService _fileSystem;
    private readonly ProcessService _processes;

    public PreflightChecker()
        : this(new FileSystemService(), new ProcessService())
    {
    }

    public PreflightChecker(FileSystemService fileSystem, ProcessService processes)
    {
        _fileSystem = fileSystem;
        _processes = processes;
    }

    public async Task<PreflightReport> CheckAsync(string targetPath, CancellationToken cancellationToken)
    {
        var isSupportedOs = Environment.Is64BitOperatingSystem;
        var hasInternet = await HasInternetAsync(cancellationToken).ConfigureAwait(false);
        var freeSpaceMb = _fileSystem.GetFreeSpaceMb(targetPath);
        var pathError = _fileSystem.ValidateTargetPath(targetPath);
        var isWritable = pathError is null;

        var existingInstallPath = File.Exists(
            Path.Combine(targetPath, InstallerDefaults.MarkerFileName))
            ? targetPath
            : null;

        var isRunning = _processes.FindRunning().Count > 0;

        string? failure = null;

        if (!isSupportedOs)
            failure = "nyxWM requires 64-bit Windows.";
        else if (!hasInternet)
            failure = "Cannot reach GitHub. Check your internet connection.";
        else if (freeSpaceMb < InstallerDefaults.RequiredFreeSpaceMb)
            failure = $"Not enough free space. Need at least {InstallerDefaults.RequiredFreeSpaceMb} MB.";
        else if (!isWritable)
            failure = pathError;

        return new PreflightReport(
            isSupportedOs,
            hasInternet,
            freeSpaceMb,
            isWritable,
            existingInstallPath,
            isRunning,
            failure);
    }

    private static async Task<bool> HasInternetAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            using var response = await client
                .GetAsync(
                    "https://api.github.com/",
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);

            // Any HTTP response, including 403 from rate limiting, proves reachability.
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }
}

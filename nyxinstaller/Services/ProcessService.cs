using System.Diagnostics;

namespace NyxInstaller.Services;

/// <summary>
/// Detects and stops a running <c>nyxWM.exe</c> before install, update, or uninstall.
/// </summary>
public sealed class ProcessService
{
    private const string ProcessName = "nyxWM";

    /// <summary>
    /// Returns running nyxWM processes, or an empty list.
    /// </summary>
    public IReadOnlyList<Process> FindRunning()
    {
        // TODO(step 6): Process.GetProcessesByName(ProcessName).
        throw new NotImplementedException();
    }

    /// <summary>
    /// Asks the user, then stops the process with a graceful close and a forced fallback.
    /// </summary>
    public Task StopAsync(IReadOnlyList<Process> processes, CancellationToken cancellationToken)
    {
        // TODO(step 6): CloseMainWindow, wait, Kill, wait.
        throw new NotImplementedException();
    }
}

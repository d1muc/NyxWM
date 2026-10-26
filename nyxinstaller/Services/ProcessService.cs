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
        try
        {
            return Process.GetProcessesByName(ProcessName);
        }
        catch
        {
            return Array.Empty<Process>();
        }
    }

    /// <summary>
    /// Asks the process to close gracefully, then stops it with a forced fallback.
    /// </summary>
    public async Task StopAsync(
        IReadOnlyList<Process> processes,
        CancellationToken cancellationToken)
    {
        foreach (var process in processes)
        {
            try
            {
                if (process.HasExited)
                    continue;

                if (process.CloseMainWindow())
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(5));

                    try
                    {
                        await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        // Graceful close timed out; fall through to Kill.
                    }
                }

                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (InvalidOperationException)
            {
                // Process exited between checks.
            }
            finally
            {
                process.Dispose();
            }
        }
    }
}

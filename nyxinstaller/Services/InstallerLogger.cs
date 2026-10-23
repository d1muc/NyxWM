namespace NyxInstaller.Services;

/// <summary>
/// Append-only installer log at <c>%TEMP%\nyxinstaller\install-&lt;timestamp&gt;.log</c>.
/// </summary>
public sealed class InstallerLogger : IDisposable
{
    private readonly StreamWriter _writer;

    public InstallerLogger(string path)
    {
        LogPath = path;
        _writer = new StreamWriter(path, append: true) { AutoFlush = true };
    }

    public string LogPath { get; }

    /// <summary>
    /// Creates the logger for a new run. Failures to create the log must not stop the installer.
    /// </summary>
    public static InstallerLogger CreateDefault()
    {
        // TODO(step 2): %TEMP%\nyxinstaller, create directory, timestamped file name.
        throw new NotImplementedException();
    }

    public void Info(string message)
    {
        // TODO(step 2): timestamped line.
        throw new NotImplementedException();
    }

    public void Error(string message, Exception? exception = null)
    {
        // TODO(step 2): timestamped line plus exception.
        throw new NotImplementedException();
    }

    public void Dispose() => _writer.Dispose();
}

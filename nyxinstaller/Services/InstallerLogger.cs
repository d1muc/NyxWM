namespace NyxInstaller.Services;

/// <summary>
/// Append-only installer log at <c>%TEMP%\nyxinstaller\install-&lt;timestamp&gt;.log</c>.
/// </summary>
public sealed class InstallerLogger : IDisposable
{
    private readonly object _sync = new();
    private readonly TextWriter _writer;
    private bool _disposed;

    public InstallerLogger(string path)
        : this(CreateWriter(path), path)
    {
    }

    private InstallerLogger(TextWriter writer, string path)
    {
        _writer = writer;
        LogPath = path;
    }

    public string LogPath { get; }

    /// <summary>
    /// Creates the logger for a new run. Failures to create the log must not stop the installer.
    /// </summary>
    public static InstallerLogger CreateDefault()
    {
        try
        {
            var directory = Path.Combine(Path.GetTempPath(), "nyxinstaller");
            Directory.CreateDirectory(directory);

            var fileName = $"install-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log";
            return new InstallerLogger(Path.Combine(directory, fileName));
        }
        catch
        {
            return new InstallerLogger(TextWriter.Null, string.Empty);
        }
    }

    public void Info(string message) => Write("INFO", message, null);

    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static TextWriter CreateWriter(string path)
    {
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        return new StreamWriter(stream) { AutoFlush = true };
    }

    private void Write(string level, string message, Exception? exception)
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            _writer.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {level} {message}");

            if (exception != null)
                _writer.WriteLine(exception);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            _writer.Dispose();
        }
    }
}

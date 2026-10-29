using System.Reflection;
using System.Text.Json;
using NyxInstaller.App;
using NyxInstaller.Models;

namespace NyxInstaller.Services;

/// <summary>
/// Runs the installation: preflight, release download, extraction, configuration and registration.
/// </summary>
public sealed class InstallService : IDisposable
{
    private static readonly JsonSerializerOptions MarkerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null
    };

    private readonly FileSystemService _fileSystem = new();
    private readonly PreflightChecker _preflight = new();
    private readonly ProcessService _processes = new();
    private readonly AutostartService _autostart = new();
    private readonly ShortcutService _shortcuts = new();
    private readonly ConfigWriter _configWriter = new();
    private readonly GitHubReleaseService _releases = new();
    private readonly InstallerLogger _logger = InstallerLogger.CreateDefault();

    public async Task<InstallResult> InstallAsync(
        InstallOptions options,
        ConfigOptions config,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(progress);

        var targetPath = Path.GetFullPath(options.InstallPath);
        var pathError = _fileSystem.ValidateTargetPath(targetPath);

        if (pathError != null)
            throw new InstallException(pathError);

        _logger.Info($"Install started. Target: {targetPath}");
        Report(progress, 0.01, InstallPhase.Checking);

        var preflight = await _preflight
            .CheckAsync(targetPath, cancellationToken)
            .ConfigureAwait(false);

        if (preflight.FailureMessage != null)
        {
            _logger.Error($"Preflight failed: {preflight.FailureMessage}");
            throw new InstallException(preflight.FailureMessage);
        }

        if (preflight.IsNyxWmRunning)
        {
            Report(progress, 0.03, InstallPhase.Checking, "Stopping nyxWM");
            await _processes
                .StopAsync(_processes.FindRunning(), cancellationToken)
                .ConfigureAwait(false);
        }

        var wasUpdated = preflight.ExistingInstallPath != null || Directory.Exists(targetPath);
        var source = options.LocalZipPath is null ? "github" : "local";
        var version = "local";
        var zipPath = options.LocalZipPath;
        var downloadedZip = false;

        if (string.IsNullOrWhiteSpace(zipPath))
        {
            var asset = await _releases
                .GetLatestAsync(cancellationToken)
                .ConfigureAwait(false);

            version = asset.Version;
            zipPath = Path.Combine(
                Path.GetTempPath(),
                "nyxinstaller",
                "downloads",
                Path.GetFileName(asset.Name));

            var downloadProgress = new Progress<double>(value =>
                Report(progress, 0.05 + value * 0.55, InstallPhase.Downloading, asset.Name));

            Report(progress, 0.05, InstallPhase.Downloading, asset.Name);
            await _releases
                .DownloadAsync(asset, zipPath, downloadProgress, cancellationToken)
                .ConfigureAwait(false);

            downloadedZip = true;
        }
        else
        {
            zipPath = Path.GetFullPath(zipPath);

            if (!File.Exists(zipPath))
                throw new InstallException($"Release archive not found: {zipPath}");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var parent = Path.GetDirectoryName(targetPath)
                     ?? throw new InstallException("Install path has no parent folder.");

        Directory.CreateDirectory(parent);

        var stagingPath = Path.Combine(parent, $".nyxWM-staging-{Guid.NewGuid():N}");
        var backupPath = Path.Combine(parent, $".nyxWM-backup-{Guid.NewGuid():N}");
        var staged = false;

        try
        {
            Report(progress, 0.62, InstallPhase.Extracting);
            _fileSystem.ExtractReleaseZip(zipPath, stagingPath);
            EnsureRuntimeAvailable(stagingPath);

            Report(progress, 0.78, InstallPhase.Configuring);

            if (options.KeepExistingConfig)
            {
                TryPreserveExistingConfig(targetPath, stagingPath, config);
            }
            else
            {
                BackupExistingConfig(targetPath, stagingPath);
                _configWriter.Write(stagingPath, config);
            }

            WriteMarker(stagingPath, version, source);

            cancellationToken.ThrowIfCancellationRequested();

            Report(progress, 0.86, InstallPhase.Finishing);

            if (Directory.Exists(targetPath))
                _fileSystem.MoveDirectory(targetPath, backupPath);

            try
            {
                _fileSystem.MoveDirectory(stagingPath, targetPath);
                staged = true;
            }
            catch
            {
                if (Directory.Exists(backupPath) && !Directory.Exists(targetPath))
                    _fileSystem.MoveDirectory(backupPath, targetPath);

                throw;
            }

            ApplyRegistration(targetPath, options);

            try
            {
                if (Directory.Exists(backupPath))
                    _fileSystem.DeleteSafely(backupPath);
            }
            catch (Exception exception)
            {
                _logger.Error("Could not delete the backup folder. It can be removed manually.", exception);
            }

            TryCopyUninstaller(targetPath);

            _logger.Info($"Install completed. Version: {version}");
            Report(progress, 1d, InstallPhase.Finishing);

            return new InstallResult(targetPath, version, _logger.LogPath, wasUpdated);
        }
        catch (OperationCanceledException)
        {
            _logger.Info("Install cancelled.");
            throw;
        }
        catch (InstallException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Error("Install failed.", exception);
            throw new InstallException(exception.Message, exception);
        }
        finally
        {
            if (!staged && Directory.Exists(stagingPath))
                _fileSystem.DeleteSafely(stagingPath);

            if (downloadedZip && !string.IsNullOrWhiteSpace(zipPath))
                TryDelete(zipPath);
        }
    }

    private void ApplyRegistration(string targetPath, InstallOptions options)
    {
        var executablePath = Path.Combine(targetPath, "nyxWM.exe");

        try
        {
            if (options.RegisterAutostart)
                _autostart.Enable(executablePath);
            else
                _autostart.Disable();
        }
        catch (Exception exception)
        {
            _logger.Error("Could not update the autostart entry.", exception);
        }

        try
        {
            if (options.CreateShortcut)
                _shortcuts.CreateStartMenuShortcut(executablePath, InstallerDefaults.InstallFolderName);
            else
                _shortcuts.RemoveStartMenuShortcut(InstallerDefaults.InstallFolderName);
        }
        catch (Exception exception)
        {
            _logger.Error("Could not update the Start Menu shortcut.", exception);
        }
    }

    private void TryPreserveExistingConfig(
        string targetPath,
        string stagingPath,
        ConfigOptions config)
    {
        var existing = Path.Combine(targetPath, "config", "config.jsonc");
        if (!File.Exists(existing))
        {
            _configWriter.Write(stagingPath, config);
            return;
        }

        var destinationDirectory = Path.Combine(stagingPath, "config");
        Directory.CreateDirectory(destinationDirectory);
        File.Copy(existing, Path.Combine(destinationDirectory, "config.jsonc"), overwrite: true);
        _logger.Info("Existing config preserved.");
    }

    private void BackupExistingConfig(string targetPath, string stagingPath)
    {
        var existing = Path.Combine(targetPath, "config", "config.jsonc");
        if (!File.Exists(existing))
            return;

        try
        {
            var directory = Path.Combine(stagingPath, "config");
            Directory.CreateDirectory(directory);
            File.Copy(existing, Path.Combine(directory, "config.jsonc.bak"), overwrite: true);
            _logger.Info("Existing config backed up to config.jsonc.bak.");
        }
        catch (Exception exception)
        {
            _logger.Error("Could not back up the existing config.", exception);
        }
    }

    private static void EnsureRuntimeAvailable(string stagingPath)
    {
        if (!RequiresDesktopRuntime(stagingPath))
            return;

        if (IsDesktopRuntimeInstalled())
            return;

        throw new InstallException(
            "nyxWM requires the .NET 10 Desktop Runtime. " +
            "Install it from https://dotnet.microsoft.com/download/dotnet/10.0 and run the installer again.");
    }

    private static bool RequiresDesktopRuntime(string stagingPath)
    {
        // Self-contained releases ship the runtime next to the application.
        if (File.Exists(Path.Combine(stagingPath, "hostfxr.dll")))
            return false;

        var runtimeConfigPath = Path.Combine(stagingPath, "nyxWM.runtimeconfig.json");
        if (!File.Exists(runtimeConfigPath))
            return false;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(runtimeConfigPath));

            if (!document.RootElement.TryGetProperty("runtimeOptions", out var options))
                return false;

            if (!options.TryGetProperty("frameworks", out var frameworks) ||
                frameworks.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var framework in frameworks.EnumerateArray())
            {
                if (framework.TryGetProperty("name", out var name) &&
                    string.Equals(
                        name.GetString(),
                        "Microsoft.WindowsDesktop.App",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
            // Unreadable metadata is not a reason to block the installation.
        }

        return false;
    }

    private static bool IsDesktopRuntimeInstalled()
    {
        var roots = new[]
        {
            Environment.GetEnvironmentVariable("DOTNET_ROOT"),
            Environment.GetEnvironmentVariable("DOTNET_ROOT_X64"),
            CombineIfPresent(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet"),
            CombineIfPresent(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "dotnet"),
            CombineIfPresent(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft",
                "dotnet")
        };

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;

            var shared = Path.Combine(root, "shared", "Microsoft.WindowsDesktop.App");
            if (!Directory.Exists(shared))
                continue;

            foreach (var directory in Directory.EnumerateDirectories(shared))
            {
                if (Version.TryParse(Path.GetFileName(directory), out var version) &&
                    version.Major >= 10)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string? CombineIfPresent(string root, params string[] parts)
    {
        if (string.IsNullOrWhiteSpace(root))
            return null;

        var combined = root;
        foreach (var part in parts)
            combined = Path.Combine(combined, part);

        return combined;
    }

    private static void WriteMarker(string installRoot, string version, string source)
    {
        var marker = new InstallMarker
        {
            Version = version,
            InstalledAt = DateTimeOffset.Now,
            TargetPath = installRoot,
            Source = source,
            InstallerVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.2.0"
        };

        var path = Path.Combine(installRoot, InstallerDefaults.MarkerFileName);
        File.WriteAllText(path, JsonSerializer.Serialize(marker, MarkerOptions));
    }

    private void TryCopyUninstaller(string targetPath)
    {
        try
        {
            var processPath = Environment.ProcessPath;

            if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
                return;

            var name = Path.GetFileNameWithoutExtension(processPath);
            if (!string.Equals(name, "nyxinstaller", StringComparison.OrdinalIgnoreCase))
                return;

            var destination = Path.Combine(targetPath, InstallerDefaults.UninstallerFileName);
            if (string.Equals(
                    Path.GetFullPath(processPath),
                    Path.GetFullPath(destination),
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            File.Copy(processPath, destination, overwrite: true);
        }
        catch (Exception exception)
        {
            _logger.Error("Could not copy the uninstaller.", exception);
        }
    }

    private static void Report(
        IProgress<InstallProgress> progress,
        double fraction,
        InstallPhase phase,
        string? detail = null)
    {
        progress.Report(new InstallProgress(Math.Clamp(fraction, 0d, 1d), phase, detail));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Cleanup only.
        }
    }

    public void Dispose()
    {
        _releases.Dispose();
        _logger.Dispose();
    }

    private sealed class InstallMarker
    {
        public string Version { get; set; } = string.Empty;

        public DateTimeOffset InstalledAt { get; set; }

        public string TargetPath { get; set; } = string.Empty;

        public string Source { get; set; } = string.Empty;

        public string InstallerVersion { get; set; } = string.Empty;
    }
}

using Microsoft.Win32;
using NyxInstaller.App;

namespace NyxInstaller.Services;

/// <summary>
/// Manages the per-user autostart entry under <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>.
/// No administrator rights are required.
/// </summary>
public sealed class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// Returns <c>true</c> when the Run value exists and points to <paramref name="executablePath"/>.
    /// </summary>
    public bool IsEnabled(string executablePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var value = key?.GetValue(InstallerDefaults.AutostartValueName) as string;

        return !string.IsNullOrWhiteSpace(value) &&
               string.Equals(
                   NormalizeExecutable(value),
                   NormalizeExecutable(executablePath),
                   StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Creates or overwrites the Run value with a quoted executable path.
    /// </summary>
    public void Enable(string executablePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key?.SetValue(InstallerDefaults.AutostartValueName, Quote(executablePath), RegistryValueKind.String);
    }

    /// <summary>
    /// Removes the Run value. Missing values are not an error.
    /// </summary>
    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(InstallerDefaults.AutostartValueName, throwOnMissingValue: false);
    }

    private static string Quote(string path) => $"\"{path.Trim().Trim('"')}\"";

    private static string NormalizeExecutable(string value)
    {
        var trimmed = value.Trim();

        if (trimmed.StartsWith('"'))
        {
            var closing = trimmed.IndexOf('"', 1);
            return closing > 0 ? trimmed[1..closing] : trimmed.Trim('"');
        }

        return trimmed;
    }
}

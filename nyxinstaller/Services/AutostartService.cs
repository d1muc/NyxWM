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
        // TODO(step 6): open the key, read InstallerDefaults.AutostartValueName, compare paths.
        throw new NotImplementedException();
    }

    /// <summary>
    /// Creates or overwrites the Run value with a quoted executable path.
    /// </summary>
    public void Enable(string executablePath)
    {
        // TODO(step 6): Registry.CurrentUser.CreateSubKey(RunKeyPath)?.SetValue(...).
        throw new NotImplementedException();
    }

    /// <summary>
    /// Removes the Run value. Missing values are not an error.
    /// </summary>
    public void Disable()
    {
        // TODO(step 6): DeleteValue(name, throwOnMissingValue: false).
        throw new NotImplementedException();
    }
}

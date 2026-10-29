using System.Runtime.InteropServices;

namespace NyxInstaller.Services;

/// <summary>
/// Creates the per-user Start Menu shortcut through the Windows Script Host COM object.
/// </summary>
public sealed class ShortcutService
{
    private const string ShellProgId = "WScript.Shell";

    /// <summary>
    /// Creates (or replaces) <c>%APPDATA%\Microsoft\Windows\Start Menu\Programs\&lt;appName&gt;.lnk</c>
    /// and returns the shortcut path.
    /// </summary>
    public string CreateStartMenuShortcut(string executablePath, string appName)
    {
        if (!File.Exists(executablePath))
            throw new FileNotFoundException("Application executable not found.", executablePath);

        if (string.IsNullOrWhiteSpace(appName))
            throw new ArgumentException("Shortcut name must not be empty.", nameof(appName));

        var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        if (string.IsNullOrWhiteSpace(programs))
            throw new InvalidOperationException("The Start Menu folder is not available.");

        Directory.CreateDirectory(programs);
        var linkPath = Path.Combine(programs, appName + ".lnk");

        var shellType = Type.GetTypeFromProgID(ShellProgId)
                        ?? throw new InvalidOperationException("Windows Script Host is not available.");

        object? shell = null;
        object? shortcut = null;

        try
        {
            shell = Activator.CreateInstance(shellType)
                    ?? throw new InvalidOperationException("Cannot create the Windows Script Host object.");

            dynamic shellDynamic = shell;
            shortcut = shellDynamic.CreateShortcut(linkPath);

            dynamic link = shortcut;
            link.TargetPath = executablePath;
            link.WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty;
            link.IconLocation = executablePath + ",0";
            link.Description = "nyxWM";
            link.WindowStyle = 1;
            link.Save();
        }
        finally
        {
            if (shortcut != null)
                Marshal.FinalReleaseComObject(shortcut);

            if (shell != null)
                Marshal.FinalReleaseComObject(shell);
        }

        return linkPath;
    }

    /// <summary>
    /// Removes the Start Menu shortcut when it exists.
    /// </summary>
    public void RemoveStartMenuShortcut(string appName)
    {
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        if (string.IsNullOrWhiteSpace(programs))
            return;

        var linkPath = Path.Combine(programs, appName + ".lnk");
        if (File.Exists(linkPath))
            File.Delete(linkPath);
    }
}

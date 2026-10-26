using System.Windows.Forms;
using NyxInstaller.App;
using NyxInstaller.Localization;
using NyxInstaller.Models;
using NyxInstaller.Ui;

namespace NyxInstaller;

/// <summary>
/// Entry point of the nyxWM installer GUI.
/// Supported arguments: <c>--lang ru|en</c>, <c>--local-zip &lt;path&gt;</c>, <c>--uninstall</c>.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Application.ThreadException += (_, e) => WriteCrashLog(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => WriteCrashLog(e.ExceptionObject as Exception);

        var localizer = Localizer.Load();
        var arguments = ParseArguments(args);

        if (arguments.Language is not null)
            localizer.SetLanguage(arguments.Language.Value);

        try
        {
            Application.Run(new MainForm(localizer, arguments));
            return 0;
        }
        catch (Exception ex)
        {
            WriteCrashLog(ex);
            MessageBox.Show(
                ex.Message,
                "nyxWM Installer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
    }

    private static InstallerArguments ParseArguments(string[] args)
    {
        var uninstall = false;
        InstallerLanguage? language = null;
        string? localZipPath = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--uninstall":
                    uninstall = true;
                    break;

                case "--lang" when i + 1 < args.Length:
                    language = args[++i].Equals("ru", StringComparison.OrdinalIgnoreCase)
                        ? InstallerLanguage.Russian
                        : InstallerLanguage.English;
                    break;

                case "--local-zip" when i + 1 < args.Length:
                    localZipPath = args[++i];
                    break;
            }
        }

        return new InstallerArguments(uninstall, language, localZipPath);
    }

    private static void WriteCrashLog(Exception? exception)
    {
        if (exception is null)
            return;

        try
        {
            var path = Path.Combine(Path.GetTempPath(), "nyxinstaller-crash.log");
            File.AppendAllText(path, $"[{DateTime.Now:O}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics only.
        }
    }
}

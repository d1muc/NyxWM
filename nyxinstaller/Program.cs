using System.Windows.Forms;
using NyxInstaller.Localization;
using NyxInstaller.Models;
using NyxInstaller.Ui;

namespace NyxInstaller;

/// <summary>
/// Entry point of the nyxWM installer GUI.
/// Supported arguments: <c>--lang ru|en</c> to preselect the installer language.
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
        var language = ParseLanguage(args);
        if (language is not null)
            localizer.SetLanguage(language.Value);

        try
        {
            Application.Run(new MainForm(localizer));
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

    private static InstallerLanguage? ParseLanguage(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (!args[i].Equals("--lang", StringComparison.OrdinalIgnoreCase))
                continue;

            return args[i + 1].Equals("ru", StringComparison.OrdinalIgnoreCase)
                ? InstallerLanguage.Russian
                : InstallerLanguage.English;
        }

        return null;
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

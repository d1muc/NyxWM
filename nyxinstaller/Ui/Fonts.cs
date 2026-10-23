using System.Drawing;
using System.Drawing.Text;
using System.Linq;

namespace NyxInstaller.Ui;

/// <summary>
/// Font set of the installer. Sizes are in points, so Windows scales them with DPI.
/// </summary>
public static class Fonts
{
    private static readonly string DisplayFamily =
        Resolve("Segoe UI Variable Display", "Segoe UI", "Tahoma");

    private static readonly string UiFamily =
        Resolve("Segoe UI Variable Text", "Segoe UI", "Tahoma");

    private static readonly string MonoFamily =
        Resolve("Cascadia Mono", "Consolas", "Courier New");

    public static readonly Font Logo = new(DisplayFamily, 12f, FontStyle.Bold);
    public static readonly Font TitleBar = new(DisplayFamily, 11f, FontStyle.Bold);
    public static readonly Font Header = new(DisplayFamily, 17f, FontStyle.Bold);
    public static readonly Font Card = new(DisplayFamily, 12.5f, FontStyle.Bold);
    public static readonly Font Section = new(DisplayFamily, 10.5f, FontStyle.Bold);
    public static readonly Font Heading = new(DisplayFamily, 15f, FontStyle.Bold);
    public static readonly Font Body = new(UiFamily, 9.75f);
    public static readonly Font BodyStrong = new(UiFamily, 9.75f, FontStyle.Bold);
    public static readonly Font Small = new(UiFamily, 8.75f);
    public static readonly Font Tiny = new(UiFamily, 8.25f);
    public static readonly Font Mono = new(MonoFamily, 9.25f);

    private static string Resolve(params string[] candidates)
    {
        using var installed = new InstalledFontCollection();
        var names = installed.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return candidates.FirstOrDefault(names.Contains)
               ?? SystemFonts.MessageBoxFont?.FontFamily.Name
               ?? "Segoe UI";
    }
}

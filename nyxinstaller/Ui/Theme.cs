using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NyxInstaller.Ui;

/// <summary>
/// Dark palette and size helpers shared by every custom control.
/// </summary>
public static class Theme
{
    public static readonly Color Background = Color.FromArgb(21, 21, 30);
    public static readonly Color Surface = Color.FromArgb(30, 30, 42);
    public static readonly Color SurfaceHover = Color.FromArgb(39, 39, 55);
    public static readonly Color Card = Color.FromArgb(35, 35, 50);
    public static readonly Color Border = Color.FromArgb(52, 52, 76);
    public static readonly Color BorderSoft = Color.FromArgb(40, 40, 57);
    public static readonly Color Text = Color.FromArgb(236, 236, 245);
    public static readonly Color Muted = Color.FromArgb(150, 150, 175);
    public static readonly Color Dim = Color.FromArgb(108, 108, 132);
    public static readonly Color Accent = Color.FromArgb(180, 120, 255);
    public static readonly Color AccentHover = Color.FromArgb(198, 150, 255);
    public static readonly Color AccentPressed = Color.FromArgb(158, 98, 236);
    public static readonly Color AccentSoft = Color.FromArgb(56, 42, 88);
    public static readonly Color AccentText = Color.FromArgb(25, 14, 44);
    public static readonly Color Success = Color.FromArgb(120, 220, 160);
    public static readonly Color Error = Color.FromArgb(240, 120, 120);
    public static readonly Color Track = Color.FromArgb(43, 43, 62);

    /// <summary>
    /// Scales a 96-DPI design value to the DPI of <paramref name="control"/>.
    /// </summary>
    public static int S(Control control, int value) =>
        (int)Math.Round(value * control.DeviceDpi / 96.0);

    public static GraphicsPath RoundedPath(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0.5f || rect.Width <= 0 || rect.Height <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }

        var diameter = Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height));
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180f, 90f);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270f, 90f);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0f, 90f);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90f, 90f);
        path.CloseFigure();
        return path;
    }
}

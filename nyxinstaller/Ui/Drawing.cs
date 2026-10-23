using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NyxInstaller.Ui;

/// <summary>
/// Painting and window helper routines.
/// </summary>
public static class Drawing
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void FillRounded(Graphics graphics, RectangleF rect, float radius, Color color)
    {
        using var path = Theme.RoundedPath(rect, radius);
        using var brush = new SolidBrush(color);
        graphics.FillPath(brush, path);
    }

    public static void StrokeRounded(
        Graphics graphics,
        RectangleF rect,
        float radius,
        Color color,
        float width = 1f)
    {
        using var path = Theme.RoundedPath(rect, radius);
        using var pen = new Pen(color, width);
        graphics.DrawPath(pen, path);
    }

    public static void FillRoundedGradient(Graphics graphics, RectangleF rect, float radius, Color from, Color to)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        using var path = Theme.RoundedPath(rect, radius);
        using var brush = new LinearGradientBrush(
            new RectangleF(rect.X, rect.Y, Math.Max(rect.Width, 1f), Math.Max(rect.Height, 1f)),
            from,
            to,
            LinearGradientMode.Horizontal);
        graphics.FillPath(brush, path);
    }

    /// <summary>
    /// Enables the Windows 11 rounded window corners; silently does nothing on older builds.
    /// </summary>
    public static void EnableRoundedCorners(Form form)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            return;

        try
        {
            var preference = DwmwcpRound;
            DwmSetWindowAttribute(form.Handle, DwmwaWindowCornerPreference, ref preference, sizeof(int));
        }
        catch
        {
            // Visual nicety only: older systems keep square corners.
        }
    }
}

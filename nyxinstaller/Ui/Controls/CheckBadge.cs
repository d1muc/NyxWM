using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NyxInstaller.Ui.Controls;

/// <summary>
/// Circular check mark shown on the completion page.
/// </summary>
public sealed class CheckBadge : Control
{
    public CheckBadge()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = Theme.Background;
        Size = new Size(72, 72);
        TabStop = false;
    }

    private int S(int value) => Theme.S(this, value);

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Theme.Background);

        var size = Math.Min(Width, Height) - S(2);
        var rect = new RectangleF((Width - size) / 2f, (Height - size) / 2f, size, size);

        Drawing.FillRounded(graphics, rect, size / 2f, Theme.AccentSoft);
        Drawing.StrokeRounded(graphics, rect, size / 2f, Theme.Accent, Math.Max(1f, S(2)));

        using var pen = new Pen(Theme.Accent, Math.Max(2f, S(3)));
        pen.StartCap = LineCap.Round;
        pen.EndCap = LineCap.Round;

        var cx = Width / 2f;
        var cy = Height / 2f;
        var unit = size / 2f;

        graphics.DrawLines(pen, new[]
        {
            new PointF(cx - unit * 0.42f, cy + unit * 0.02f),
            new PointF(cx - unit * 0.10f, cy + unit * 0.32f),
            new PointF(cx + unit * 0.44f, cy - unit * 0.30f)
        });
    }
}

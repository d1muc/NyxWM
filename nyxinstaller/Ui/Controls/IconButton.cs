using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NyxInstaller.Ui.Controls;

public enum IconKind
{
    Minimize,
    Close
}

/// <summary>
/// Small caption button of the custom title bar.
/// </summary>
public sealed class IconButton : Control
{
    private bool _hover;

    public IconButton(IconKind kind)
    {
        Kind = kind;

        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = Theme.Background;
        Size = new Size(38, 28);
        TabStop = false;
        Cursor = Cursors.Hand;
    }

    public IconKind Kind { get; }

    private int S(int value) => Theme.S(this, value);

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hover = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Theme.Background);

        if (_hover)
        {
            var fill = Kind == IconKind.Close
                ? Color.FromArgb(220, 72, 77)
                : Theme.SurfaceHover;
            Drawing.FillRounded(graphics, new RectangleF(1f, 1f, Width - 2f, Height - 2f), S(6), fill);
        }

        var glyph = _hover && Kind == IconKind.Close ? Color.White : Theme.Muted;
        using var pen = new Pen(glyph, Math.Max(1f, S(2)));

        var cx = Width / 2f;
        var cy = Height / 2f;
        var arm = S(5);

        if (Kind == IconKind.Minimize)
        {
            graphics.DrawLine(pen, cx - arm, cy, cx + arm, cy);
        }
        else
        {
            graphics.DrawLine(pen, cx - arm, cy - arm, cx + arm, cy + arm);
            graphics.DrawLine(pen, cx - arm, cy + arm, cx + arm, cy - arm);
        }
    }
}

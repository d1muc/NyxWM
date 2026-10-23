using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NyxInstaller.Ui.Controls;

/// <summary>
/// Rounded progress bar with a subtle accent gradient.
/// </summary>
public sealed class AccentProgressBar : Control
{
    private double _value;

    public AccentProgressBar()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = Theme.Background;
        Height = 10;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, 0d, 1d);
            Invalidate();
        }
    }

    private int S(int value) => Theme.S(this, value);

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Theme.Background);

        var trackRect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        var radius = Height / 2f;
        Drawing.FillRounded(graphics, trackRect, radius, Theme.Track);

        var fillWidth = (float)(_value * Width);
        if (fillWidth <= 0f)
            return;

        var fillRect = new RectangleF(0f, 0f, Math.Max(fillWidth, Height), Height);
        Drawing.FillRoundedGradient(graphics, fillRect, radius, Color.FromArgb(138, 92, 255), Theme.AccentHover);
    }
}

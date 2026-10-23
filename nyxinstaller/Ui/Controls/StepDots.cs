using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NyxInstaller.Ui.Controls;

/// <summary>
/// Small dot trail showing the current wizard step.
/// </summary>
public sealed class StepDots : Control
{
    private int _current;

    public StepDots()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = Theme.Background;
    }

    public int Count { get; } = 6;

    /// <summary>
    /// Index of the active step; completed steps are every index below it.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Current
    {
        get => _current;
        set
        {
            _current = Math.Clamp(value, 0, Count);
            Invalidate();
        }
    }

    /// <summary>
    /// Recomputes the control size for the current DPI. Call from the owner's layout pass.
    /// </summary>
    public void UpdateMetrics()
    {
        var diameter = Theme.S(this, 7);
        var gap = Theme.S(this, 12);
        var width = Count * diameter + (Count - 1) * gap;

        if (Width != width)
            Width = width;
        if (Height != diameter)
            Height = diameter;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Theme.Background);

        var diameter = Theme.S(this, 7);
        var gap = Theme.S(this, 12);
        var radius = diameter / 2f;
        var centerY = diameter / 2f;
        var ringWidth = Math.Max(1f, Theme.S(this, 2));

        for (var i = 0; i < Count; i++)
        {
            var centerX = i * (diameter + gap) + radius;

            if (i < _current)
            {
                using var brush = new SolidBrush(Theme.Accent);
                graphics.FillEllipse(brush, centerX - radius, centerY - radius, diameter, diameter);
            }
            else if (i == _current)
            {
                using var brush = new SolidBrush(Theme.Accent);
                graphics.FillEllipse(brush, centerX - radius, centerY - radius, diameter, diameter);

                using var ring = new Pen(Color.FromArgb(90, Theme.Accent), ringWidth);
                var ringDiameter = diameter + Theme.S(this, 9);
                graphics.DrawEllipse(
                    ring,
                    centerX - ringDiameter / 2f,
                    centerY - ringDiameter / 2f,
                    ringDiameter,
                    ringDiameter);
            }
            else
            {
                using var brush = new SolidBrush(Theme.Track);
                graphics.FillEllipse(brush, centerX - radius, centerY - radius, diameter, diameter);

                using var pen = new Pen(Theme.Border);
                graphics.DrawEllipse(pen, centerX - radius, centerY - radius, diameter, diameter);
            }
        }
    }
}

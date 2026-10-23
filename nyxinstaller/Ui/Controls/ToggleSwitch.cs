using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NyxInstaller.Ui.Controls;

/// <summary>
/// A dark on/off toggle switch.
/// </summary>
public sealed class ToggleSwitch : Control
{
    private bool _checked;
    private bool _hover;

    public ToggleSwitch()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = Theme.Background;
        Size = new Size(46, 26);
        TabStop = false;
        Cursor = Cursors.Hand;
    }

    public event EventHandler? CheckedChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
                return;

            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Checked = !Checked;
        Focus();
    }

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

    private int S(int value) => Theme.S(this, value);

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Theme.Background);

        var width = Width;
        var height = Height;
        var trackRadius = height / 2f;
        var trackRect = new RectangleF(0.5f, 0.5f, width - 1f, height - 1f);

        var track = _checked ? Theme.Accent : (_hover ? Theme.Border : Theme.Track);
        Drawing.FillRounded(graphics, trackRect, trackRadius, track);

        var knobDiameter = height - S(6);
        var knobTop = (height - knobDiameter) / 2f;
        var knobLeft = _checked
            ? width - knobDiameter - knobTop
            : knobTop;

        var knobRect = new RectangleF(knobLeft, knobTop, knobDiameter, knobDiameter);
        using var knobBrush = new SolidBrush(_checked ? Color.White : Theme.Muted);
        graphics.FillEllipse(knobBrush, knobRect);
    }
}

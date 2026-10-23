using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NyxInstaller.Ui.Controls;

/// <summary>
/// A dark on/off toggle switch whose knob glides between positions.
/// </summary>
public sealed class ToggleSwitch : Control
{
    private bool _checked;
    private bool _hover;
    private double _position;
    private readonly string _knobKey = Animator.NextKey();

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
            AnimateKnob();
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

    private void AnimateKnob()
    {
        var to = _checked ? 1d : 0d;

        if (!IsHandleCreated)
        {
            _position = to;
            return;
        }

        var from = _position;
        Animator.Instance.Tween(_knobKey, from, to, Motion.Toggle, Easing.EaseOutCubic, v =>
        {
            if (IsDisposed || Disposing)
                return;

            _position = v;
            Invalidate();
        });
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
        var knobInset = (height - knobDiameter) / 2f;
        var travel = width - knobDiameter - knobInset * 2f;
        var knobLeft = knobInset + (float)(travel * _position);

        var knobRect = new RectangleF(knobLeft, knobInset, knobDiameter, knobDiameter);
        using var knobBrush = new SolidBrush(_checked ? Color.White : Theme.Muted);
        graphics.FillEllipse(knobBrush, knobRect);
    }
}

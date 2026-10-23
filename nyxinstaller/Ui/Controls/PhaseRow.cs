using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NyxInstaller.Ui.Controls;

public enum PhaseState
{
    Pending,
    Active,
    Done
}

/// <summary>
/// One line of the simulated install phase list.
/// </summary>
public sealed class PhaseRow : Control
{
    private PhaseState _state;
    private float _pulse;
    private string _label = string.Empty;

    public PhaseRow()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = Theme.Background;
        Height = 34;
        TabStop = false;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Label
    {
        get => _label;
        set
        {
            _label = value ?? string.Empty;
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public PhaseState State
    {
        get => _state;
        set
        {
            if (_state == value)
                return;

            _state = value;
            Invalidate();
        }
    }

    /// <summary>
    /// 0..1 animation value used by the active phase dot.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float Pulse
    {
        set
        {
            _pulse = Math.Clamp(value, 0f, 1f);
            if (_state == PhaseState.Active)
                Invalidate();
        }
    }

    private int S(int value) => Theme.S(this, value);

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Theme.Background);

        var diameter = S(18);
        var left = S(1);
        var top = (Height - diameter) / 2f;
        var rect = new RectangleF(left, top, diameter, diameter);

        switch (_state)
        {
            case PhaseState.Done:
                Drawing.FillRounded(graphics, rect, diameter / 2f, Theme.Success);
                using (var check = new Pen(Theme.Background, Math.Max(1.5f, S(2))))
                {
                    check.StartCap = LineCap.Round;
                    check.EndCap = LineCap.Round;
                    var cx = rect.X + diameter / 2f;
                    var cy = rect.Y + diameter / 2f;
                    var unit = diameter / 2f;
                    graphics.DrawLines(check, new[]
                    {
                        new PointF(cx - unit * 0.40f, cy + unit * 0.02f),
                        new PointF(cx - unit * 0.08f, cy + unit * 0.32f),
                        new PointF(cx + unit * 0.42f, cy - unit * 0.30f)
                    });
                }
                break;

            case PhaseState.Active:
                Drawing.FillRounded(graphics, rect, diameter / 2f, Theme.AccentSoft);
                using (var ring = new Pen(Theme.Accent, Math.Max(1.5f, S(2))))
                    graphics.DrawEllipse(ring, rect);

                var dot = diameter * (0.22f + 0.14f * _pulse);
                using (var brush = new SolidBrush(Theme.Accent))
                {
                    var cx = rect.X + diameter / 2f;
                    var cy = rect.Y + diameter / 2f;
                    graphics.FillEllipse(brush, cx - dot / 2f, cy - dot / 2f, dot, dot);
                }
                break;

            default:
                Drawing.FillRounded(graphics, rect, diameter / 2f, Theme.Track);
                using (var pen = new Pen(Theme.Border))
                    graphics.DrawEllipse(pen, rect);
                break;
        }

        var textLeft = left + diameter + S(14);
        var color = _state == PhaseState.Pending ? Theme.Dim : Theme.Text;
        TextRenderer.DrawText(
            graphics,
            _label,
            Fonts.Body,
            new Rectangle(textLeft, 0, Math.Max(0, Width - textLeft), Height),
            color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }
}

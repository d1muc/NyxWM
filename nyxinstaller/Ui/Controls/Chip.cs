using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NyxInstaller.Ui.Controls;

/// <summary>
/// A single pill-shaped choice used inside a <see cref="ChipGroup"/>.
/// </summary>
public sealed class Chip : Control
{
    private bool _hover;
    private bool _selected;

    public Chip(string value, string label)
    {
        Value = value;

        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = Theme.Background;
        Font = Fonts.Small;
        Height = 32;
        Text = label;
        Cursor = Cursors.Hand;
        TabStop = false;
    }

    public string Value { get; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            Invalidate();
        }
    }

    public void SetLabel(string label)
    {
        Text = label;
    }

    public void RecalculateSize(int height, int horizontalPadding)
    {
        var width = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPrefix).Width;
        Size = new Size(width + horizontalPadding * 2, height);
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

        var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        var radius = Height / 2f;

        Color fill;
        Color border;
        Color text;

        if (_selected)
        {
            fill = Theme.AccentSoft;
            border = Theme.Accent;
            text = Theme.Accent;
        }
        else if (_hover)
        {
            fill = Theme.SurfaceHover;
            border = Theme.Border;
            text = Theme.Text;
        }
        else
        {
            fill = Theme.Surface;
            border = Theme.Border;
            text = Theme.Muted;
        }

        Drawing.FillRounded(graphics, rect, radius, fill);
        Drawing.StrokeRounded(graphics, rect, radius, border);

        TextRenderer.DrawText(
            graphics,
            Text,
            Font,
            new Rectangle(0, 0, Width, Height),
            text,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPrefix);
    }
}

using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NyxInstaller.Ui.Controls;

/// <summary>
/// Large selectable card used by the language page.
/// </summary>
public sealed class SelectableCard : Control
{
    private bool _hover;
    private bool _selected;
    private string _title = string.Empty;
    private string _subtitle = string.Empty;
    private string _badge = string.Empty;
    private readonly TweenColor _fill;

    public SelectableCard()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = Theme.Background;
        Size = new Size(300, 136);
        Cursor = Cursors.Hand;
        TabStop = false;

        _fill = new TweenColor(this, Theme.Card);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Title
    {
        get => _title;
        set
        {
            _title = value ?? string.Empty;
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Subtitle
    {
        get => _subtitle;
        set
        {
            _subtitle = value ?? string.Empty;
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Badge
    {
        get => _badge;
        set
        {
            _badge = value ?? string.Empty;
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
                return;

            _selected = value;
            UpdateFill();
            Invalidate();
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hover = true;
        UpdateFill();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = false;
        UpdateFill();
    }

    private void UpdateFill() => _fill.Set(TargetFill());

    private Color TargetFill() =>
        _selected ? Blend(Theme.Card, Theme.AccentSoft, 0.55)
        : _hover ? Theme.SurfaceHover
        : Theme.Card;

    private int S(int value) => Theme.S(this, value);

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Theme.Background);

        var rect = new RectangleF(0.75f, 0.75f, Width - 1.5f, Height - 1.5f);
        var radius = S(14);

        Drawing.FillRounded(graphics, rect, radius, _fill.Value);
        Drawing.StrokeRounded(graphics, rect, radius, _selected ? Theme.Accent : Theme.Border, _selected ? 1.6f : 1f);

        if (!string.IsNullOrEmpty(_badge))
            DrawBadge(graphics);

        var titleRect = new Rectangle(S(22), S(54), Width - S(44), S(26));
        TextRenderer.DrawText(
            graphics,
            _title,
            Fonts.Card,
            titleRect,
            Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

        var subtitleRect = new Rectangle(S(22), S(82), Width - S(44), S(22));
        TextRenderer.DrawText(
            graphics,
            _subtitle,
            Fonts.Small,
            subtitleRect,
            Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

        if (_selected)
            DrawCheck(graphics);
    }

    private void DrawBadge(Graphics graphics)
    {
        var textWidth = TextRenderer.MeasureText(_badge, Fonts.Tiny, Size.Empty, TextFormatFlags.NoPrefix).Width;
        var badgeWidth = textWidth + S(18);
        var badgeHeight = S(22);
        var badgeRect = new RectangleF(S(22), S(20), badgeWidth, badgeHeight);

        Drawing.FillRounded(graphics, badgeRect, S(6), _selected ? Theme.Accent : Theme.AccentSoft);
        TextRenderer.DrawText(
            graphics,
            _badge,
            Fonts.Tiny,
            Rectangle.Round(badgeRect),
            _selected ? Theme.AccentText : Theme.Accent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    private void DrawCheck(Graphics graphics)
    {
        var diameter = S(20);
        var centerX = Width - S(24) - diameter / 2f;
        var centerY = S(20) + diameter / 2f;

        using var brush = new SolidBrush(Theme.Accent);
        graphics.FillEllipse(brush, centerX - diameter / 2f, centerY - diameter / 2f, diameter, diameter);

        using var pen = new Pen(Theme.AccentText, Math.Max(1.5f, S(2)));
        var half = diameter / 2f;
        graphics.DrawLines(pen, new[]
        {
            new PointF(centerX - half * 0.42f, centerY + half * 0.02f),
            new PointF(centerX - half * 0.10f, centerY + half * 0.34f),
            new PointF(centerX + half * 0.44f, centerY - half * 0.30f)
        });
    }

    private static Color Blend(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0d, 1d);
        return Color.FromArgb(
            (int)Math.Round(from.R + (to.R - from.R) * amount),
            (int)Math.Round(from.G + (to.G - from.G) * amount),
            (int)Math.Round(from.B + (to.B - from.B) * amount));
    }
}

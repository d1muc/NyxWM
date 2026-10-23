using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NyxInstaller.Ui.Controls;

/// <summary>
/// Rounded row with a title, a description and a toggle on the right.
/// </summary>
public sealed class OptionCard : Control
{
    private readonly ToggleSwitch _toggle;
    private bool _hover;
    private string _title = string.Empty;
    private string _description = string.Empty;

    public OptionCard()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = Theme.Background;
        Size = new Size(820, 88);
        Cursor = Cursors.Hand;
        TabStop = false;

        _toggle = new ToggleSwitch();
        _toggle.CheckedChanged += (_, _) =>
        {
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        };
        Controls.Add(_toggle);
    }

    public event EventHandler? CheckedChanged;

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
    public string Description
    {
        get => _description;
        set
        {
            _description = value ?? string.Empty;
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _toggle.Checked;
        set => _toggle.Checked = value;
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        _toggle.Checked = !_toggle.Checked;
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

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        LayoutToggle();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        LayoutToggle();
    }

    private void LayoutToggle()
    {
        if (_toggle is null)
            return;

        _toggle.Size = new Size(S(46), S(26));
        _toggle.Location = new Point(Width - S(24) - _toggle.Width, (Height - _toggle.Height) / 2);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Theme.Background);

        var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        var radius = S(12);

        Drawing.FillRounded(graphics, rect, radius, _hover ? Theme.SurfaceHover : Theme.Card);
        Drawing.StrokeRounded(graphics, rect, radius, _hover ? Theme.Border : Theme.BorderSoft);

        var textWidth = Width - S(24) - _toggle.Width - S(32);

        TextRenderer.DrawText(
            graphics,
            _title,
            Fonts.Section,
            new Rectangle(S(24), S(18), textWidth, S(24)),
            Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

        TextRenderer.DrawText(
            graphics,
            _description,
            Fonts.Small,
            new Rectangle(S(24), S(46), textWidth, S(24)),
            Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }
}

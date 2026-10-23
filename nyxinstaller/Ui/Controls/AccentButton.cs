using System.Drawing;
using System.Windows.Forms;

namespace NyxInstaller.Ui.Controls;

/// <summary>
/// Primary action button: filled with the accent color, with a smooth hover / press blend.
/// </summary>
public sealed class AccentButton : RoundedButton
{
    private readonly TweenColor _fill;

    public AccentButton()
    {
        _fill = new TweenColor(this, Theme.Accent);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        UpdateFill();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        UpdateFill();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        UpdateFill();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        UpdateFill();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        UpdateFill();
    }

    private void UpdateFill() => _fill.Set(TargetFill());

    private Color TargetFill() =>
        !Enabled ? Theme.Track
        : Pressed ? Theme.AccentPressed
        : Hovered ? Theme.AccentHover
        : Theme.Accent;

    protected override void PaintButton(Graphics graphics)
    {
        var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        var radius = S(10);

        Drawing.FillRounded(graphics, rect, radius, _fill.Value);
        DrawLabel(graphics, Enabled ? Theme.AccentText : Theme.Dim);
    }
}

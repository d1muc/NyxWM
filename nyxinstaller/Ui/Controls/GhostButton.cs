using System.Drawing;
using System.Windows.Forms;

namespace NyxInstaller.Ui.Controls;

/// <summary>
/// Secondary action button: outlined surface with a soft hover fill.
/// </summary>
public sealed class GhostButton : RoundedButton
{
    private readonly TweenColor _fill;

    public GhostButton()
    {
        _fill = new TweenColor(this, Theme.Background);
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
        !Enabled || (!Hovered && !Pressed) ? Theme.Background
        : Pressed ? Theme.Surface
        : Theme.SurfaceHover;

    protected override void PaintButton(Graphics graphics)
    {
        var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        var radius = S(10);

        Drawing.FillRounded(graphics, rect, radius, _fill.Value);
        Drawing.StrokeRounded(graphics, rect, radius, Enabled ? Theme.Border : Theme.BorderSoft);
        DrawLabel(graphics, Enabled ? Theme.Text : Theme.Dim);
    }
}

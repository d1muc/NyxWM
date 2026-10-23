using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NyxInstaller.Ui.Controls;

/// <summary>
/// Secondary action button: outlined surface.
/// </summary>
public sealed class GhostButton : RoundedButton
{
    protected override void PaintButton(Graphics graphics)
    {
        var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        var radius = S(10);

        if (Enabled && (Hovered || Pressed))
            Drawing.FillRounded(graphics, rect, radius, Pressed ? Theme.Surface : Theme.SurfaceHover);

        Drawing.StrokeRounded(graphics, rect, radius, Enabled ? Theme.Border : Theme.BorderSoft);
        DrawLabel(graphics, Enabled ? Theme.Text : Theme.Dim);
    }
}

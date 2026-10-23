using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NyxInstaller.Ui.Controls;

/// <summary>
/// Primary action button: filled with the accent color.
/// </summary>
public sealed class AccentButton : RoundedButton
{
    protected override void PaintButton(Graphics graphics)
    {
        var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        var radius = S(10);

        Color fill;
        if (!Enabled)
            fill = Theme.Track;
        else if (Pressed)
            fill = Theme.AccentPressed;
        else if (Hovered)
            fill = Theme.AccentHover;
        else
            fill = Theme.Accent;

        Drawing.FillRounded(graphics, rect, radius, fill);
        DrawLabel(graphics, Enabled ? Theme.AccentText : Theme.Dim);
    }
}

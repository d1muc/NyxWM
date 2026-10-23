using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NyxInstaller.Ui.Controls;

/// <summary>
/// Rounded surface panel; children should use <see cref="FillColor"/> as their background.
/// </summary>
public class CardPanel : Panel
{
    public CardPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Theme.Background;
        FillColor = Theme.Surface;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color FillColor { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Radius { get; set; } = 14;

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Theme.Background);

        var radius = Theme.S(this, Radius);
        var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);

        Drawing.FillRounded(graphics, rect, radius, FillColor);
        Drawing.StrokeRounded(graphics, rect, radius, Theme.BorderSoft);
    }
}

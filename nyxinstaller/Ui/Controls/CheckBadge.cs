using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NyxInstaller.Ui.Controls;

/// <summary>
/// Circular check mark shown on the completion page. Pops in once when <see cref="Play"/> is called.
/// </summary>
public sealed class CheckBadge : Control
{
    private double _progress = 1d;
    private readonly string _animKey = Animator.NextKey();

    public CheckBadge()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = Theme.Background;
        Size = new Size(72, 72);
        TabStop = false;
    }

    private int S(int value) => Theme.S(this, value);

    /// <summary>
    /// Replays the pop-in animation of the badge.
    /// </summary>
    public void Play()
    {
        _progress = 0d;
        Invalidate();

        Animator.Instance.Tween(_animKey, 0d, 1d, Motion.Pop, Easing.EaseOutBack, value =>
        {
            if (IsDisposed || Disposing)
                return;

            _progress = value;
            Invalidate();
        });
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Theme.Background);

        var cx = Width / 2f;
        var cy = Height / 2f;
        var scale = 0.55f + 0.45f * (float)_progress;

        graphics.TranslateTransform(cx, cy);
        graphics.ScaleTransform(scale, scale);
        graphics.TranslateTransform(-cx, -cy);

        var size = Math.Min(Width, Height) - S(2);
        var rect = new RectangleF((Width - size) / 2f, (Height - size) / 2f, size, size);

        Drawing.FillRounded(graphics, rect, size / 2f, Theme.AccentSoft);
        Drawing.StrokeRounded(graphics, rect, size / 2f, Theme.Accent, Math.Max(1f, S(2)));

        using var pen = new Pen(Theme.Accent, Math.Max(2f, S(3)));
        pen.StartCap = LineCap.Round;
        pen.EndCap = LineCap.Round;

        var unit = size / 2f;

        graphics.DrawLines(pen, new[]
        {
            new PointF(cx - unit * 0.42f, cy + unit * 0.02f),
            new PointF(cx - unit * 0.10f, cy + unit * 0.32f),
            new PointF(cx + unit * 0.44f, cy - unit * 0.30f)
        });
    }
}

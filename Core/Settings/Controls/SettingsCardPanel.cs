using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace nyxWM
{
    /// <summary>
    /// Rounded section card used by the Visuals page. It paints its own surface
    /// and section title, and hosts the row controls as children.
    /// </summary>
    internal sealed class SettingsCardPanel : Panel
    {
        private const float RadiusDip = 12f;
        private const float TitleLeftDip = 14f;
        private const float TitleTopDip = 11f;

        private readonly NotificationTheme _theme;
        private readonly float _scale;
        private readonly string _title;
        private readonly Font _titleFont;

        internal SettingsCardPanel(NotificationTheme theme, float scale, string title)
        {
            _theme = theme ?? throw new ArgumentNullException(nameof(theme));
            _scale = Math.Clamp(scale, 0.75f, 3f);
            _title = title ?? string.Empty;
            _titleFont = VisualPreferences.CreateUiFont("Segoe UI", 9.5f, FontStyle.Bold);

            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);

            BackColor = _theme.CardSurface;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);

            using (GraphicsPath path = CreateRoundedPath(rect, RadiusDip * _scale))
            using (var border = new Pen(_theme.CardBorder, MathF.Max(1f, _scale)))
            {
                graphics.DrawPath(border, path);
            }

            float barWidth = 3f * _scale;
            float barHeight = 14f * _scale;
            var bar = new RectangleF(
                TitleLeftDip * _scale,
                TitleTopDip * _scale + 2f * _scale,
                barWidth,
                barHeight);

            using (GraphicsPath barPath = CreateRoundedPath(bar, barWidth / 2f))
            using (var accent = new SolidBrush(_theme.Accent))
            {
                graphics.FillPath(accent, barPath);
            }

            using var titleBrush = new SolidBrush(_theme.TextPrimary);
            graphics.DrawString(
                _title,
                _titleFont,
                titleBrush,
                new PointF(
                    bar.Right + 8f * _scale,
                    TitleTopDip * _scale - 1f * _scale));
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);

            if (Width <= 0 || Height <= 0)
                return;

            using GraphicsPath path = CreateRoundedPath(
                new RectangleF(0f, 0f, Width, Height),
                RadiusDip * _scale);

            Region? previous = Region;
            Region = new Region(path);
            previous?.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _titleFont.Dispose();

            base.Dispose(disposing);
        }

        private static GraphicsPath CreateRoundedPath(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            float diameter = Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height));

            if (diameter <= 0f)
            {
                path.AddRectangle(rect);
                return path;
            }

            path.AddArc(rect.Left, rect.Top, diameter, diameter, 180f, 90f);
            path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270f, 90f);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0f, 90f);
            path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90f, 90f);
            path.CloseFigure();
            return path;
        }
    }
}

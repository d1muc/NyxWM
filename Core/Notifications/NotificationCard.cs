using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace nyxWM
{
    /// <summary>
    /// A single rounded notification card. Each card is its own borderless,
    /// click-through, topmost window so fades and slides stay smooth and
    /// independent from the rest of the application.
    /// </summary>
    internal sealed class NotificationCard : Form
    {
        private const int BaseWidth = 380;
        private const int BaseHeight = 66;
        private const float BaseRadius = 14f;

        private const float FadeInMs = 220f;
        private const float FadeOutMs = 180f;
        private const float PositionSmoothingPerSecond = 13f;

        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        private readonly float _scale;
        private readonly string _title;
        private readonly string _body;
        private readonly NotificationTheme _theme;
        private readonly Font _titleFont;
        private readonly Font _bodyFont;

        private float _x;
        private float _y;
        private bool _closing;
        private float _closeElapsedMs;
        private float _opacityAtClose = 1f;

        internal double AgeMs { get; private set; }
        internal double LifetimeMs { get; }
        internal bool IsClosing => _closing;
        internal bool Finished => _closing && _closeElapsedMs >= FadeOutMs;

        internal float TargetX;
        internal float TargetY;

        internal NotificationCard(
            string title,
            string body,
            int durationMs,
            float scale,
            float startX,
            float startY,
            float targetX,
            float targetY,
            NotificationTheme theme)
        {
            _title = title ?? string.Empty;
            _body = body ?? string.Empty;
            _scale = scale;
            _theme = theme;
            LifetimeMs = Math.Max(800, durationMs);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = _theme.BackgroundTop;
            DoubleBuffered = true;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint,
                true);

            Size = Measure(scale);

            _titleFont = new Font("Segoe UI", 10.5f, FontStyle.Bold, GraphicsUnit.Point);
            _bodyFont = new Font("Segoe UI", 9.25f, FontStyle.Regular, GraphicsUnit.Point);

            TargetX = targetX;
            TargetY = targetY;
            _x = startX;
            _y = startY;

            Location = new Point((int)MathF.Round(_x), (int)MathF.Round(_y));
            Opacity = 0.0;

            ApplyRoundedRegion();
        }

        internal static Size Measure(float scale)
        {
            return new Size(
                (int)MathF.Round(BaseWidth * scale),
                (int)MathF.Round(BaseHeight * scale));
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
                return parameters;
            }
        }

        protected override bool ShowWithoutActivation => true;

        /// <summary>
        /// Advances entry/exit fade and position smoothing by <paramref name="dtMs"/> milliseconds.
        /// </summary>
        internal void Advance(float dtMs)
        {
            AgeMs += dtMs;

            if (_closing)
            {
                _closeElapsedMs += dtMs;
                float closeProgress = Math.Clamp(_closeElapsedMs / FadeOutMs, 0f, 1f);
                Opacity = _opacityAtClose * (1f - closeProgress);
            }
            else
            {
                float entryProgress = Math.Clamp((float)(AgeMs / FadeInMs), 0f, 1f);
                float eased = 1f - MathF.Pow(1f - entryProgress, 3f);
                Opacity = eased;
            }

            float factor = 1f - MathF.Exp(-PositionSmoothingPerSecond * dtMs / 1000f);
            _x += (TargetX - _x) * factor;
            _y += (TargetY - _y) * factor;

            if (MathF.Abs(TargetX - _x) < 0.5f)
                _x = TargetX;
            if (MathF.Abs(TargetY - _y) < 0.5f)
                _y = TargetY;

            Location = new Point((int)MathF.Round(_x), (int)MathF.Round(_y));
            Invalidate();
        }

        internal void BeginClose()
        {
            if (_closing)
                return;

            _closing = true;
            _closeElapsedMs = 0f;
            _opacityAtClose = (float)Opacity;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            float inset = MathF.Max(1f, _scale);
            var cardRect = new RectangleF(
                inset,
                inset,
                Width - inset * 2f,
                Height - inset * 2f);

            using (GraphicsPath cardPath = CreateRoundedPath(cardRect, BaseRadius * _scale))
            using (var fill = new LinearGradientBrush(
                       cardRect,
                       _theme.BackgroundTop,
                       _theme.BackgroundBottom,
                       LinearGradientMode.Vertical))
            {
                graphics.FillPath(fill, cardPath);

                using var border = new Pen(_theme.Border, MathF.Max(1f, _scale));
                graphics.DrawPath(border, cardPath);
            }

            DrawAccent(graphics);
            DrawText(graphics);
            DrawLifetimeBar(graphics, cardRect);
        }

        private void DrawAccent(Graphics graphics)
        {
            float barWidth = 3f * _scale;
            float barHeight = 28f * _scale;
            var barRect = new RectangleF(
                14f * _scale,
                (Height - barHeight) / 2f,
                barWidth,
                barHeight);

            using GraphicsPath barPath = CreateRoundedPath(barRect, barWidth / 2f);
            using var accent = new SolidBrush(_theme.Accent);
            graphics.FillPath(accent, barPath);

            float glowSize = 10f * _scale;
            using var glow = new SolidBrush(Color.FromArgb(36, _theme.Accent));
            graphics.FillEllipse(
                glow,
                14f * _scale - glowSize / 2f + barWidth / 2f,
                Height / 2f - glowSize / 2f,
                glowSize,
                glowSize);
        }

        private void DrawText(Graphics graphics)
        {
            float textLeft = 30f * _scale;
            float textWidth = Width - textLeft - 16f * _scale;
            bool hasBody = !string.IsNullOrWhiteSpace(_body);

            float titleY = hasBody ? 10f * _scale : 20f * _scale;

            using var titleBrush = new SolidBrush(Color.FromArgb(245, 245, 248));
            using var bodyBrush = new SolidBrush(Color.FromArgb(158, 163, 174));
            using var format = new StringFormat
            {
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap,
                LineAlignment = StringAlignment.Near
            };

            graphics.DrawString(
                _title,
                _titleFont,
                titleBrush,
                new RectangleF(textLeft, titleY, textWidth, 22f * _scale),
                format);

            if (hasBody)
            {
                graphics.DrawString(
                    _body,
                    _bodyFont,
                    bodyBrush,
                    new RectangleF(textLeft, 33f * _scale, textWidth, 20f * _scale),
                    format);
            }
        }

        private void DrawLifetimeBar(Graphics graphics, RectangleF cardRect)
        {
            if (_closing)
                return;

            float progress = Math.Clamp((float)(1.0 - AgeMs / Math.Max(1.0, LifetimeMs)), 0f, 1f);
            if (progress <= 0f)
                return;

            float padding = BaseRadius * _scale;
            float available = cardRect.Width - padding * 2f;
            var barRect = new RectangleF(
                cardRect.Left + padding,
                cardRect.Bottom - 4f * _scale,
                available * progress,
                MathF.Max(1.5f, _scale));

            using var brush = new SolidBrush(Color.FromArgb(120, _theme.Accent));
            graphics.FillRectangle(brush, barRect);
        }

        private void ApplyRoundedRegion()
        {
            using GraphicsPath path = CreateRoundedPath(
                new RectangleF(0f, 0f, Width, Height),
                BaseRadius * _scale);

            Region? previous = Region;
            Region = new Region(path);
            previous?.Dispose();
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _titleFont.Dispose();
                _bodyFont.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

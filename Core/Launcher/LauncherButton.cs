using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace nyxWM
{
    /// <summary>
    /// One rounded quick-action button. Each button is its own borderless,
    /// topmost window so every tile can fade and scale independently, using the
    /// same card surface and accent palette as the notification toasts.
    /// </summary>
    internal sealed class LauncherButton : Form
    {
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        private const float BaseSize = 116f;
        private const float RadiusRatio = 0.38f;
        private const float EntryDurationMs = 260f;
        private const float ExitDurationMs = 270f;
        private const float EntryStartScale = 0.82f;
        private const float HoverScale = 0.06f;
        private const float HoverSmoothingPerSecond = 16f;

        private readonly float _scale;
        private readonly LauncherIcon _icon;
        private readonly NotificationTheme _theme;
        private readonly Action _onActivated;
        private readonly bool _animations;
        private readonly float _windowOpacity;
        private readonly float _cornerRatio;

        private float _elapsedMs;
        private readonly float _entryDelayMs;
        private float _closeElapsedMs;
        private float _exitDelayMs;
        private float _hoverAmount;
        private float _visualScale = EntryStartScale;
        private bool _closing;
        private bool _hovered;

        private PointF _center;

        private float _opacity = 1f;
        private Bitmap? _buffer;
        private int _renderStamp = -1;

        internal LauncherButton(
            LauncherIcon icon,
            float scale,
            float entryDelayMs,
            NotificationTheme theme,
            Action onActivated)
        {
            _icon = icon;
            _scale = Math.Clamp(scale, 0.5f, 3f);
            _entryDelayMs = MathF.Max(0f, entryDelayMs);
            _theme = theme ?? throw new ArgumentNullException(nameof(theme));
            _onActivated = onActivated ?? throw new ArgumentNullException(nameof(onActivated));
            _animations = VisualPreferences.SmoothAnimations;
            _windowOpacity = VisualPreferences.WindowOpacity;
            _cornerRatio = VisualPreferences.CornerRadiusRatio(RadiusRatio);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = _theme.BackgroundTop;
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint,
                true);

            int initialSize = Math.Max(1, (int)MathF.Round(BaseSize * _scale * EntryStartScale));
            Size = new Size(initialSize, initialSize);
            _opacity = _animations ? 0f : _windowOpacity;
        }

        internal bool Finished => _closing && _closeElapsedMs >= _exitDelayMs + ExitDurationMs;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | LayeredWindow.WS_EX_LAYERED;
                return parameters;
            }
        }

        protected override bool ShowWithoutActivation => true;

        internal void SetCenter(float x, float y)
        {
            _center = new PointF(x, y);
        }

        /// <summary>
        /// Advances entry/exit fade, scale and hover smoothing by <paramref name="dtMs"/>.
        /// </summary>
        internal void Advance(float dtMs)
        {
            _elapsedMs += dtMs;

            float entryProgress = _animations
                ? Math.Clamp((_elapsedMs - _entryDelayMs) / EntryDurationMs, 0f, 1f)
                : 1f;
            float easedEntry = EaseOutCubic(entryProgress);

            float hoverTarget = _hovered ? 1f : 0f;

            if (_animations)
            {
                float hoverFactor = 1f - MathF.Exp(-HoverSmoothingPerSecond * dtMs / 1000f);
                _hoverAmount += (hoverTarget - _hoverAmount) * hoverFactor;
            }
            else
            {
                _hoverAmount = hoverTarget;
            }

            float closeProgress = 0f;
            if (_closing)
            {
                _closeElapsedMs += dtMs;
                closeProgress = Math.Clamp(
                    (_closeElapsedMs - _exitDelayMs) / ExitDurationMs,
                    0f,
                    1f);
            }

            float easedClose = EaseOutCubic(closeProgress);
            float opacity = _closing
                ? easedEntry * (1f - easedClose)
                : easedEntry;

            _opacity = Math.Clamp(opacity * _windowOpacity, 0f, 1f);

            float entryScale = _animations
                ? EntryStartScale + (1f - EntryStartScale) * easedEntry
                : 1f;

            _visualScale = entryScale
                           * (1f - closeProgress)
                           * (1f + HoverScale * _hoverAmount);
        }

        /// <summary>
        /// Applies the frame's scale, bounds, opacity and repaint. Must run on the
        /// UI thread: <see cref="Advance"/> only computes, this method touches the
        /// window.
        /// </summary>
        internal void ApplyFrame()
        {
            ApplyLayout();
            ApplyLayered();
        }

        internal void BeginClose(float delayMs)
        {
            if (_closing)
                return;

            _closing = true;
            _closeElapsedMs = 0f;
            _exitDelayMs = MathF.Max(0f, delayMs);

            if (!_animations)
            {
                _closeElapsedMs = _exitDelayMs + ExitDurationMs;
                _opacity = 0f;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyLayered();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);

            if (IsHandleCreated)
                ApplyLayered();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hovered = true;
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hovered = false;
            base.OnMouseLeave(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);

            if (e.Button == MouseButtons.Left)
            {
                _onActivated();
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // The window's pixels come from UpdateLayeredWindow, never from GDI
            // background painting: erasing here would punch an opaque hole in it.
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            ApplyLayered();
        }

        /// <summary>Recreates the ARGB buffer when the window size changed.</summary>
        private void EnsureBufferSize()
        {
            if (Width <= 0 || Height <= 0)
                return;

            if (_buffer != null && _buffer.Width == Width && _buffer.Height == Height)
                return;

            Bitmap? previous = _buffer;
            _buffer = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
            previous?.Dispose();
            _renderStamp = -1;
        }

        /// <summary>Paints the buffer only when the visible content actually changed.</summary>
        private void RenderIfNeeded()
        {
            if (_buffer == null)
                return;

            int stamp = RenderStamp();

            if (stamp == _renderStamp)
                return;

            _renderStamp = stamp;

            using Graphics graphics = Graphics.FromImage(_buffer);
            Render(graphics);
        }

        /// <summary>
        /// Fingerprint of everything that changes the pixels: size, scale and the
        /// hover glow. The buffer is only repainted when this changes.
        /// </summary>
        private int RenderStamp()
        {
            int hover = (int)Math.Round(Math.Clamp(_hoverAmount, 0f, 1f) * 120f);
            int visual = (int)Math.Round(Math.Clamp(_visualScale, 0f, 3f) * 400f);
            return (Width * 397 + Height) * 1009 + hover * 31 + visual;
        }

        /// <summary>Pushes the buffer to the window with the current fade alpha.</summary>
        private void ApplyLayered()
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            EnsureBufferSize();
            RenderIfNeeded();

            if (_buffer == null)
                return;

            LayeredWindow.Apply(Handle, _buffer, Left, Top, _opacity);
        }

        private void Render(Graphics graphics)
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            float visual = MathF.Max(0.05f, _visualScale);
            float borderWidth = MathF.Max(1f, 1.1f * _scale * visual);
            var cardRect = new RectangleF(
                borderWidth / 2f,
                borderWidth / 2f,
                Width - borderWidth,
                Height - borderWidth);

            if (cardRect.Width <= 0f || cardRect.Height <= 0f)
                return;

            float radius = Width * _cornerRatio;

            using (GraphicsPath cardPath = CreateRoundedPath(cardRect, radius))
            using (var fill = new LinearGradientBrush(
                       cardRect,
                       _theme.BackgroundTop,
                       _theme.BackgroundBottom,
                       LinearGradientMode.Vertical))
            {
                graphics.FillPath(fill, cardPath);

                using var border = new Pen(_theme.Border, borderWidth);
                graphics.DrawPath(border, cardPath);
            }

            DrawAccentGlow(graphics);
            DrawIcon(graphics);
        }

        private void DrawAccentGlow(Graphics graphics)
        {
            float glowSize = Width * (0.62f + 0.12f * _hoverAmount);
            int alpha = (int)Math.Clamp(18f + 40f * _hoverAmount, 0f, 255f);

            using var glow = new SolidBrush(Color.FromArgb(alpha, _theme.Accent));
            graphics.FillEllipse(
                glow,
                (Width - glowSize) / 2f,
                (Height - glowSize) / 2f,
                glowSize,
                glowSize);
        }

        private void DrawIcon(Graphics graphics)
        {
            float radius = Width * 0.30f;
            float centerX = Width / 2f;
            float centerY = Height / 2f;
            float penWidth = MathF.Max(1f, 2.1f * _scale * MathF.Max(0.05f, _visualScale));

            Color iconColor = _theme.TextPrimary;

            using var pen = new Pen(iconColor, penWidth)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };

            switch (_icon)
            {
                case LauncherIcon.Sliders:
                    DrawSliders(graphics, pen, centerX, centerY, radius, iconColor);
                    break;
                case LauncherIcon.Search:
                    DrawSearch(graphics, pen, centerX, centerY, radius);
                    break;
                case LauncherIcon.Terminal:
                    DrawTerminal(graphics, pen, centerX, centerY, radius);
                    break;
            }
        }

        private static void DrawSliders(
            Graphics graphics,
            Pen pen,
            float cx,
            float cy,
            float radius,
            Color iconColor)
        {
            float left = cx - radius * 0.92f;
            float right = cx + radius * 0.92f;
            float rowStep = radius * 0.46f;
            float knobRadius = radius * 0.16f;

            using var trackPen = new Pen(Color.FromArgb(150, iconColor), pen.Width * 0.86f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            using var knobBrush = new SolidBrush(iconColor);

            float[] knobOffsets = { 0.18f, 0.62f, -0.42f };

            for (int i = 0; i < knobOffsets.Length; i++)
            {
                float y = cy + (i - 1) * rowStep;

                graphics.DrawLine(trackPen, left, y, right, y);

                float knobX = cx + radius * knobOffsets[i];
                graphics.FillEllipse(
                    knobBrush,
                    knobX - knobRadius,
                    y - knobRadius,
                    knobRadius * 2f,
                    knobRadius * 2f);
            }
        }

        private static void DrawSearch(Graphics graphics, Pen pen, float cx, float cy, float radius)
        {
            float lens = radius * 0.64f;
            float offset = radius * 0.12f;
            float lensX = cx - offset;
            float lensY = cy - offset;

            graphics.DrawEllipse(pen, lensX - lens, lensY - lens, lens * 2f, lens * 2f);

            const float diagonal = 0.70710678f;
            graphics.DrawLine(
                pen,
                lensX + lens * diagonal,
                lensY + lens * diagonal,
                lensX + lens * diagonal + radius * 0.52f,
                lensY + lens * diagonal + radius * 0.52f);
        }

        private static void DrawTerminal(Graphics graphics, Pen pen, float cx, float cy, float radius)
        {
            float width = radius * 1.6f;
            float height = radius * 1.24f;
            var frame = new RectangleF(cx - width / 2f, cy - height / 2f, width, height);

            using (GraphicsPath framePath = CreateRoundedPath(frame, radius * 0.28f))
            {
                graphics.DrawPath(pen, framePath);
            }

            graphics.DrawLines(
                pen,
                new[]
                {
                    new PointF(cx - width * 0.26f, cy - height * 0.20f),
                    new PointF(cx - width * 0.08f, cy),
                    new PointF(cx - width * 0.26f, cy + height * 0.20f)
                });

            graphics.DrawLine(
                pen,
                cx + width * 0.02f,
                cy + height * 0.24f,
                cx + width * 0.26f,
                cy + height * 0.24f);
        }

        private void ApplyLayout()
        {
            float side = BaseSize * _scale * MathF.Max(0.05f, _visualScale);
            int size = Math.Max(1, (int)MathF.Round(side));

            if (Size.Width != size || Size.Height != size)
            {
                Size = new Size(size, size);
            }

            Location = new Point(
                (int)MathF.Round(_center.X - size / 2f),
                (int)MathF.Round(_center.Y - size / 2f));
        }

        private static float EaseOutCubic(float progress)
        {
            float inverse = 1f - Math.Clamp(progress, 0f, 1f);
            return 1f - inverse * inverse * inverse;
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
                _buffer?.Dispose();
                _buffer = null;
            }

            base.Dispose(disposing);
        }
    }
}

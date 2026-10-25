using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WinFormsTimer = System.Windows.Forms.Timer;

namespace nyxWM
{
    /// <summary>
    /// Borderless canvas window opened by the terminal launcher tile. It is an
    /// ordinary top-level window, so the workspace camera pans and centres it
    /// like any other application. The card shares the gradient, accent glow and
    /// rounded surface of the Rofi menu and settings window. Several instances
    /// may coexist.
    /// </summary>
    internal sealed class TerminalWindow : Form
    {
        private const int CS_DROPSHADOW = 0x00020000;
        private const int WM_NCHITTEST = 0x0084;
        private const int HTCLIENT = 1;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;

        private const float BaseWidthDip = 980f;
        private const float BaseHeightDip = 620f;
        private const float CornerRadiusDip = 18f;
        private const float PaddingDip = 16f;
        private const float HeaderTopDip = 8f;
        private const float HeaderHeightDip = 26f;
        private const float ThemeTransitionMs = 280f;
        private const float ConsoleRadiusDip = 12f;
        private const float ConsolePaddingDip = 16f;
        private const float CloseButtonSizeDip = 26f;
        private const float ResizeBorderDip = 8f;
        private const float CascadeStepDip = 28f;
        private const float EntryDurationMs = 240f;

        private NotificationTheme _theme;
        private NotificationTheme _themeFrom;
        private float _themeProgress = 1f;
        private readonly float _scale;
        private readonly Font _titleFont;
        private readonly Font _monoFont;
        private readonly Action<IntPtr>? _requestCenter;
        private readonly float _windowOpacity;
        private readonly bool _animations;
        private readonly float _cornerRadiusDip;
        private readonly WinFormsTimer _timer;
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        private RectangleF _consoleRect;
        private RectangleF _closeRect;
        private float _headerBottom;

        private bool _closeHovered;
        private bool _dragging;
        private Point _dragOffset;

        private Color _surfaceTop;
        private Color _surfaceBottom;
        private Color _accentColor;
        private Color _mutedColor;
        private Color _fieldBorderColor;
        private Color _consoleFill;

        private double _lastTickMs;
        private float _elapsedMs;

        internal TerminalWindow(
            NotificationTheme theme,
            float scale,
            int cascadeIndex,
            Action<IntPtr>? requestCenter)
        {
            _theme = theme ?? throw new ArgumentNullException(nameof(theme));
            _scale = Math.Clamp(scale, 0.75f, 3f);
            _requestCenter = requestCenter;
            _windowOpacity = VisualPreferences.WindowOpacity;
            _animations = VisualPreferences.SmoothAnimations;
            _cornerRadiusDip = VisualPreferences.CornerRadius(CornerRadiusDip);
            _titleFont = VisualPreferences.CreateUiFont("Segoe UI", 9.5f, FontStyle.Bold);
            _monoFont = CreateMonoFont(11f);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = true;
            TopMost = false;
            BackColor = _theme.BackgroundTop;
            DoubleBuffered = true;
            Text = "Terminal";
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw,
                true);

            int width = Math.Max(1, (int)MathF.Round(BaseWidthDip * _scale));
            int height = Math.Max(1, (int)MathF.Round(BaseHeightDip * _scale));

            Rectangle work = Screen.PrimaryScreen?.WorkingArea
                             ?? new Rectangle(0, 0, 1920, 1080);
            int cascade = (int)MathF.Round(Math.Max(0, cascadeIndex) * CascadeStepDip * _scale);
            int left = work.Left + (work.Width - width) / 2 + cascade;
            int top = work.Top + (work.Height - height) / 2 + cascade;

            Location = new Point(
                Math.Clamp(left, work.Left, Math.Max(work.Left, work.Right - width)),
                Math.Clamp(top, work.Top, Math.Max(work.Top, work.Bottom - height)));
            Size = new Size(width, height);

            _themeFrom = _theme;
            RefreshPalette();

            Opacity = _animations ? 0.0 : _windowOpacity;

            ApplyLayout();
            ApplyRoundedRegion();

            _timer = new WinFormsTimer { Interval = 15 };
            _timer.Tick += OnTick;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ClassStyle |= CS_DROPSHADOW;
                return parameters;
            }
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            Activate();
            SetForegroundWindow(Handle);

            if (!_animations)
            {
                _elapsedMs = EntryDurationMs;
                return;
            }

            _clock.Restart();
            _lastTickMs = 0.0;
            _timer.Start();
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);

            // A click on the header starts a drag, so it must not also recentre
            // the workspace underneath the pointer.
            if (_dragging)
                return;

            Point cursor = PointToClient(System.Windows.Forms.Cursor.Position);
            if (cursor.Y <= _headerBottom && !_closeRect.Contains(cursor))
                return;

            _requestCenter?.Invoke(Handle);
        }

        private void OnTick(object? sender, EventArgs e)
        {
            double now = _clock.Elapsed.TotalMilliseconds;
            float dtMs = (float)Math.Clamp(now - _lastTickMs, 0.0, 50.0);
            _lastTickMs = now;

            if (dtMs <= 0f)
                return;

            _elapsedMs += dtMs;

            float entryProgress = Math.Clamp(_elapsedMs / EntryDurationMs, 0f, 1f);
            Opacity = Math.Clamp(EaseOutCubic(entryProgress) * _windowOpacity, 0.0, 1.0);

            if (_themeProgress < 1f)
            {
                _themeProgress = Math.Clamp(
                    _themeProgress + dtMs / ThemeTransitionMs,
                    0f,
                    1f);
                RefreshPalette();
            }

            if (entryProgress >= 1f && _themeProgress >= 1f)
                _timer.Stop();
        }

        /// <summary>
        /// Switches this window to a new resolved theme, optionally smoothing the
        /// colour change so a settings save or config edit fades the already open
        /// windows into their new palette.
        /// </summary>
        internal void ApplyTheme(NotificationTheme theme, bool animate)
        {
            if (theme == null)
                throw new ArgumentNullException(nameof(theme));

            if (ReferenceEquals(theme, _theme))
                return;

            _themeFrom = _theme;
            _theme = theme;

            if (!animate || !_animations)
            {
                _themeProgress = 1f;
                RefreshPalette();
                return;
            }

            _themeProgress = 0f;
            RefreshPalette();
            _timer.Start();
        }

        private void RefreshPalette()
        {
            float t = EaseOutCubic(Math.Clamp(_themeProgress, 0f, 1f));

            _surfaceTop = Blend(_themeFrom.BackgroundTop, _theme.BackgroundTop, t);
            _surfaceBottom = Blend(_themeFrom.BackgroundBottom, _theme.BackgroundBottom, t);
            _accentColor = Blend(_themeFrom.Accent, _theme.Accent, t);
            _mutedColor = Blend(_themeFrom.TextMuted, _theme.TextMuted, t);
            _fieldBorderColor = Blend(_themeFrom.FieldBorder, _theme.FieldBorder, t);
            _consoleFill = Blend(ConsoleFillFor(_themeFrom), ConsoleFillFor(_theme), t);

            Invalidate();
        }

        private static Color ConsoleFillFor(NotificationTheme theme)
        {
            return theme.IsLight
                ? Color.FromArgb(150, theme.FieldBackground)
                : Color.FromArgb(170, theme.FieldBackground);
        }

        private static Color Blend(Color from, Color to, float t)
        {
            return Color.FromArgb(
                (int)MathF.Round(from.A + (to.A - from.A) * t),
                (int)MathF.Round(from.R + (to.R - from.R) * t),
                (int)MathF.Round(from.G + (to.G - from.G) * t),
                (int)MathF.Round(from.B + (to.B - from.B) * t));
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST && WindowState == FormWindowState.Normal)
            {
                base.WndProc(ref m);

                if (m.Result != (IntPtr)HTCLIENT)
                    return;

                int packed = m.LParam.ToInt32();
                var screen = new Point((short)(packed & 0xFFFF), (short)((packed >> 16) & 0xFFFF));
                Point client = PointToClient(screen);

                int edge = Math.Max(1, (int)MathF.Round(ResizeBorderDip * _scale));
                bool left = client.X <= edge;
                bool right = client.X >= ClientSize.Width - edge;
                bool top = client.Y <= edge;
                bool bottom = client.Y >= ClientSize.Height - edge;

                m.Result = (IntPtr)(left && top ? HTTOPLEFT
                    : right && top ? HTTOPRIGHT
                    : left && bottom ? HTBOTTOMLEFT
                    : right && bottom ? HTBOTTOMRIGHT
                    : left ? HTLEFT
                    : right ? HTRIGHT
                    : top ? HTTOP
                    : bottom ? HTBOTTOM
                    : HTCLIENT);
                return;
            }

            base.WndProc(ref m);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button != MouseButtons.Left)
                return;

            if (_closeRect.Contains(e.Location))
            {
                Close();
                return;
            }

            if (e.Y <= _headerBottom)
            {
                _dragging = true;
                _dragOffset = new Point(
                    System.Windows.Forms.Cursor.Position.X - Left,
                    System.Windows.Forms.Cursor.Position.Y - Top);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (_dragging)
            {
                Point cursor = System.Windows.Forms.Cursor.Position;
                Location = new Point(cursor.X - _dragOffset.X, cursor.Y - _dragOffset.Y);
                return;
            }

            bool hovered = _closeRect.Contains(e.Location);
            if (hovered != _closeHovered)
            {
                _closeHovered = hovered;
                Cursor = hovered ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (e.Button == MouseButtons.Left)
                _dragging = false;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);

            if (_closeHovered)
            {
                _closeHovered = false;
                Invalidate();
            }
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);

            ApplyLayout();
            ApplyRoundedRegion();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            float borderWidth = MathF.Max(1f, _scale);
            var cardRect = new RectangleF(
                borderWidth / 2f,
                borderWidth / 2f,
                Width - borderWidth,
                Height - borderWidth);

            if (cardRect.Width <= 0f || cardRect.Height <= 0f)
                return;

            float radius = WindowState == FormWindowState.Maximized
                ? 0f
                : _cornerRadiusDip * _scale;

            using GraphicsPath cardPath = CreateRoundedPath(cardRect, radius);

            using (var fill = new LinearGradientBrush(
                       cardRect,
                       _surfaceTop,
                       _surfaceBottom,
                       LinearGradientMode.Vertical))
            {
                graphics.FillPath(fill, cardPath);
            }

            DrawAccentGlow(graphics);
            DrawConsole(graphics);
            DrawHeader(graphics);

            Color borderColor = _theme.IsLight
                ? Color.FromArgb(28, 20, 22, 28)
                : Color.FromArgb(20, 255, 255, 255);

            using var border = new Pen(borderColor, borderWidth);
            graphics.DrawPath(border, cardPath);
        }

        private void DrawAccentGlow(Graphics graphics)
        {
            float glowWidth = Width * 0.54f;
            float glowHeight = Height * 0.30f;

            using var glow = new SolidBrush(Color.FromArgb(18, _accentColor));
            graphics.FillEllipse(
                glow,
                (Width - glowWidth) / 2f,
                -glowHeight * 0.55f,
                glowWidth,
                glowHeight);
        }

        private void DrawConsole(Graphics graphics)
        {
            if (_consoleRect.Width <= 1f || _consoleRect.Height <= 1f)
                return;

            using GraphicsPath consolePath = CreateRoundedPath(
                _consoleRect,
                ConsoleRadiusDip * _scale);

            using (var fill = new SolidBrush(_consoleFill))
            {
                graphics.FillPath(fill, consolePath);
            }

            using (var border = new Pen(_fieldBorderColor, MathF.Max(1f, _scale)))
            {
                graphics.DrawPath(border, consolePath);
            }

            float pad = ConsolePaddingDip * _scale;
            float lineHeight = _monoFont.GetHeight(graphics);
            var promptRect = new RectangleF(
                _consoleRect.Left + pad,
                _consoleRect.Top + pad,
                MathF.Max(1f, _consoleRect.Width - pad * 2f),
                lineHeight);

            using var accentBrush = new SolidBrush(_accentColor);
            using var mutedBrush = new SolidBrush(_mutedColor);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter
            };

            graphics.DrawString(">", _monoFont, accentBrush, promptRect, format);

            float promptWidth = graphics.MeasureString(">", _monoFont).Width;
            var hintRect = new RectangleF(
                promptRect.Left + promptWidth + 10f * _scale,
                promptRect.Top,
                MathF.Max(1f, promptRect.Width - promptWidth - 10f * _scale),
                lineHeight);

            graphics.DrawString("type a command", _monoFont, mutedBrush, hintRect, format);
        }

        private void DrawHeader(Graphics graphics)
        {
            float pad = PaddingDip * _scale;
            float centerY = _headerBottom / 2f;
            float glyphRadius = 8f * _scale;

            DrawTerminalGlyph(
                graphics,
                pad + glyphRadius,
                centerY,
                glyphRadius);

            var titleRect = new RectangleF(
                pad + glyphRadius * 2f + 8f * _scale,
                centerY - HeaderHeightDip * _scale / 2f,
                240f * _scale,
                HeaderHeightDip * _scale);

            using var titleBrush = new SolidBrush(_mutedColor);
            using var titleFormat = new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter
            };

            graphics.DrawString("Terminal", _titleFont, titleBrush, titleRect, titleFormat);

            Color dividerColor = _theme.IsLight
                ? Color.FromArgb(20, 0, 0, 0)
                : Color.FromArgb(24, 255, 255, 255);

            using var divider = new Pen(dividerColor, MathF.Max(1f, _scale));
            graphics.DrawLine(divider, pad, _headerBottom + 5f * _scale, Width - pad, _headerBottom + 5f * _scale);

            DrawCloseButton(graphics);
        }

        private void DrawTerminalGlyph(Graphics graphics, float cx, float cy, float radius)
        {
            float width = radius * 1.9f;
            float height = radius * 1.5f;
            var frame = new RectangleF(cx - width / 2f, cy - height / 2f, width, height);

            using (GraphicsPath framePath = CreateRoundedPath(frame, radius * 0.30f))
            using (var pen = new Pen(Color.FromArgb(190, _accentColor), MathF.Max(1.1f, 1.3f * _scale)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                graphics.DrawPath(pen, framePath);
            }

            using var glyphPen = new Pen(_accentColor, MathF.Max(1.1f, 1.4f * _scale))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };

            graphics.DrawLines(
                glyphPen,
                new[]
                {
                    new PointF(cx - width * 0.24f, cy - height * 0.16f),
                    new PointF(cx - width * 0.06f, cy),
                    new PointF(cx - width * 0.24f, cy + height * 0.16f)
                });

            graphics.DrawLine(
                glyphPen,
                cx + width * 0.02f,
                cy + height * 0.20f,
                cx + width * 0.26f,
                cy + height * 0.20f);
        }

        private void DrawCloseButton(Graphics graphics)
        {
            float centerX = _closeRect.Left + _closeRect.Width / 2f;
            float centerY = _closeRect.Top + _closeRect.Height / 2f;

            float diameter = (_closeHovered ? 12.5f : 11f) * _scale;
            var dot = new RectangleF(
                centerX - diameter / 2f,
                centerY - diameter / 2f,
                diameter,
                diameter);

            Color dotColor = _closeHovered
                ? Color.FromArgb(235, _mutedColor)
                : Color.FromArgb(175, _mutedColor);

            if (_closeHovered)
            {
                float glowDiameter = diameter + 8f * _scale;
                using var glow = new SolidBrush(Color.FromArgb(46, _mutedColor));
                graphics.FillEllipse(
                    glow,
                    centerX - glowDiameter / 2f,
                    centerY - glowDiameter / 2f,
                    glowDiameter,
                    glowDiameter);
            }

            using var dotBrush = new SolidBrush(dotColor);
            graphics.FillEllipse(dotBrush, dot);
        }

        private void ApplyLayout()
        {
            if (Width <= 0 || Height <= 0)
                return;

            float pad = PaddingDip * _scale;
            float headerHeight = HeaderHeightDip * _scale;
            float buttonSize = CloseButtonSizeDip * _scale;

            _headerBottom = HeaderTopDip * _scale + headerHeight;

            float rowTop = (_headerBottom - buttonSize) / 2f;
            _closeRect = new RectangleF(Width - pad - buttonSize, rowTop, buttonSize, buttonSize);

            float consoleTop = _headerBottom + 14f * _scale;
            float consoleBottom = Height - pad;

            _consoleRect = new RectangleF(
                pad,
                consoleTop,
                MathF.Max(1f, Width - pad * 2f),
                MathF.Max(1f, consoleBottom - consoleTop));
        }

        private void ApplyRoundedRegion()
        {
            if (Width <= 0 || Height <= 0)
                return;

            float radius = WindowState == FormWindowState.Maximized
                ? 0f
                : _cornerRadiusDip * _scale;

            using GraphicsPath path = CreateRoundedPath(
                new RectangleF(0f, 0f, Width, Height),
                radius);

            Region? previous = Region;
            Region = new Region(path);
            previous?.Dispose();
        }

        private static Font CreateMonoFont(float sizePoints)
        {
            string[] families = { "Cascadia Code", "Cascadia Mono", "JetBrains Mono", "Fira Code", "Consolas" };

            foreach (string family in families)
            {
                try
                {
                    return new Font(family, sizePoints * VisualPreferences.UiScale, FontStyle.Regular, GraphicsUnit.Point);
                }
                catch (ArgumentException)
                {
                    // Family is not installed; try the next candidate.
                }
            }

            return new Font(FontFamily.GenericMonospace, sizePoints * VisualPreferences.UiScale, FontStyle.Regular, GraphicsUnit.Point);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Stop();
                _timer.Dispose();
                _titleFont.Dispose();
                _monoFont.Dispose();
            }

            base.Dispose(disposing);
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
    }
}

using System;
using System.ComponentModel;
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
    /// Borderless Rofi-style application menu. The desktop behind the window is
    /// captured and blurred to fake a glass backdrop, the card fades and scales
    /// in, and the selection pill glides between rows. The search field keeps
    /// focus so typing filters immediately.
    /// </summary>
    internal sealed class RofiMenuWindow : Form
    {
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int CS_DROPSHADOW = 0x00020000;

        private const int VK_LBUTTON = 0x01;
        private const int VK_RBUTTON = 0x02;
        private const int VK_MBUTTON = 0x04;

        private const float BaseWidthDip = 540f;
        private const float BaseHeightDip = 560f;
        private const float CornerRadiusDip = 18f;
        private const float PaddingDip = 18f;
        private const float SearchHeightDip = 50f;
        private const float GapDip = 10f;
        private const float FooterHeightDip = 52f;

        private const float EntryDurationMs = 300f;
        private const float ExitDurationMs = 200f;
        private const float EntryStartScale = 0.95f;
        private const float ExitEndScale = 0.985f;
        private const float EntrySlideDip = 16f;
        private const float ExitSlideDip = 8f;

        private readonly NotificationTheme _theme;
        private readonly float _scale;
        private readonly int _baseWidth;
        private readonly int _baseHeight;
        private readonly int _baseX;
        private readonly int _baseY;

        private readonly Font _searchFont;
        private readonly Font _badgeFont;
        private readonly TextBox _search;
        private readonly RofiAppList _list;
        private readonly WinFormsTimer _timer;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly Color _fieldColor;
        private readonly bool _glass;
        private readonly bool _animations;
        private readonly float _windowOpacity;
        private readonly float _cornerRadiusDip;

        private RectangleF _searchRect;
        private RectangleF _footerRect;

        private Bitmap? _backdrop;
        private double _lastTickMs;
        private float _elapsedMs;
        private float _closeElapsedMs;
        private bool _closing;

        internal RofiMenuWindow(NotificationTheme theme, float scale)
        {
            _theme = theme ?? throw new ArgumentNullException(nameof(theme));
            _scale = Math.Clamp(scale, 0.75f, 3f);

            Rectangle work = Screen.PrimaryScreen?.WorkingArea
                             ?? new Rectangle(0, 0, 1920, 1080);

            _baseWidth = Math.Max(1, (int)MathF.Round(MathF.Min(
                BaseWidthDip * _scale,
                work.Width - 40f * _scale)));
            _baseHeight = Math.Max(1, (int)MathF.Round(MathF.Min(
                BaseHeightDip * _scale,
                work.Height - 60f * _scale)));

            _baseX = work.Left + (work.Width - _baseWidth) / 2;
            _baseY = work.Top + (work.Height - _baseHeight) / 2;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = _theme.BackgroundTop;
            DoubleBuffered = true;
            Text = "Rofi menu";
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint,
                true);

            Size = new Size(_baseWidth, _baseHeight);
            Location = new Point(_baseX, _baseY);

            _glass = VisualPreferences.GlassEnabled;
            _animations = VisualPreferences.SmoothAnimations;
            _windowOpacity = VisualPreferences.WindowOpacity;
            _cornerRadiusDip = VisualPreferences.CornerRadius(CornerRadiusDip);

            Opacity = _animations ? 0.0 : _windowOpacity;

            _fieldColor = _theme.FieldBackground;

            _searchFont = VisualPreferences.CreateUiFont("Segoe UI", 11f, FontStyle.Regular);
            _badgeFont = VisualPreferences.CreateUiFont("Segoe UI", 7.75f, FontStyle.Regular);

            _search = new TextBox
            {
                AutoSize = false,
                BorderStyle = BorderStyle.None,
                BackColor = _fieldColor,
                ForeColor = _theme.TextPrimary,
                Font = _searchFont,
                PlaceholderText = "Search apps or type command...",
                ShortcutsEnabled = true
            };

            _search.TextChanged += (_, _) =>
            {
                ApplyFilter();
                Invalidate();
            };

            _search.KeyDown += OnSearchKeyDown;
            _search.GotFocus += (_, _) => Invalidate();
            _search.LostFocus += (_, _) => Invalidate();

            _list = new RofiAppList(_theme, _scale);
            _list.ItemActivated += OnItemActivated;
            _list.PinsChanged += () => ApplyFilter(preserveSelection: true);

            Controls.Add(_list);
            Controls.Add(_search);

            ApplyFilter();
            ApplyLayout(_animations ? EntryStartScale : 1f, 0f);
            ApplyRoundedRegion();

            _timer = new WinFormsTimer { Interval = 15 };
            _timer.Tick += OnTick;
        }

        internal bool IsClosing => _closing;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= WS_EX_TOOLWINDOW;
                parameters.ClassStyle |= CS_DROPSHADOW;
                return parameters;
            }
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            _backdrop = _glass ? CaptureBackdrop() : null;

            _clock.Restart();
            _lastTickMs = 0.0;

            if (!_animations)
                _elapsedMs = EntryDurationMs;

            _timer.Start();

            Activate();
            SetForegroundWindow(Handle);
            _search.Focus();
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);

            if (!_closing)
                _search.Focus();

            Invalidate();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);

            // Clicks outside the menu dismiss it, like Rofi. Keyboard-driven
            // focus changes, such as switching the keyboard layout, must not
            // close the menu. The short guard ignores the deactivation that can
            // happen while the window is still appearing. The pin popup is part
            // of the menu, so it must not dismiss it either.
            if (!_closing
                && _elapsedMs > 140f
                && !_list.IsPinMenuOpen
                && IsMouseButtonDown())
                BeginClose();
        }

        private static bool IsMouseButtonDown()
        {
            return (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0
                   || (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0
                   || (GetAsyncKeyState(VK_MBUTTON) & 0x8000) != 0;
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

            using GraphicsPath cardPath = CreateRoundedPath(cardRect, _cornerRadiusDip * _scale);

            DrawGlass(graphics, cardRect, cardPath);
            DrawAccentGlow(graphics);
            DrawSearchField(graphics);
            DrawFooter(graphics);

            Color borderColor = _theme.IsLight
                ? Color.FromArgb(28, 20, 22, 28)
                : Color.FromArgb(20, 255, 255, 255);

            using var border = new Pen(borderColor, borderWidth);
            graphics.DrawPath(border, cardPath);
        }

        private void DrawGlass(Graphics graphics, RectangleF cardRect, GraphicsPath cardPath)
        {
            if (_backdrop != null)
            {
                GraphicsState state = graphics.Save();
                graphics.SetClip(cardPath);
                graphics.DrawImage(_backdrop, new Rectangle(0, 0, Width, Height));
                graphics.Restore(state);

                Color tintColor = _theme.IsLight
                    ? Color.FromArgb(226, _theme.BackgroundTop)
                    : Color.FromArgb(214, _theme.BackgroundTop);

                using (var tint = new SolidBrush(tintColor))
                {
                    graphics.FillPath(tint, cardPath);
                }

                Color shadeBottom = _theme.IsLight
                    ? Color.FromArgb(18, 0, 0, 0)
                    : Color.FromArgb(40, 0, 0, 0);

                using var shade = new LinearGradientBrush(
                    cardRect,
                    Color.FromArgb(0, 0, 0, 0),
                    shadeBottom,
                    LinearGradientMode.Vertical);

                graphics.FillPath(shade, cardPath);
            }
            else
            {
                using var fill = new LinearGradientBrush(
                    cardRect,
                    _theme.BackgroundTop,
                    _theme.BackgroundBottom,
                    LinearGradientMode.Vertical);

                graphics.FillPath(fill, cardPath);
            }
        }

        private void DrawAccentGlow(Graphics graphics)
        {
            float glowWidth = Width * 0.54f;
            float glowHeight = Height * 0.30f;
            int alpha = Math.Clamp((int)(18f + 12f * EntryProgress()), 0, 255);

            using var glow = new SolidBrush(Color.FromArgb(alpha, _theme.Accent));
            graphics.FillEllipse(
                glow,
                (Width - glowWidth) / 2f,
                -glowHeight * 0.55f,
                glowWidth,
                glowHeight);
        }

        private void DrawSearchField(Graphics graphics)
        {
            bool hasText = _search.TextLength > 0;

            using (GraphicsPath fieldPath = CreateRoundedPath(_searchRect, 12f * _scale))
            {
                Color fieldFill = _theme.IsLight
                    ? Color.FromArgb(150, 255, 255, 255)
                    : Color.FromArgb(18, 255, 255, 255);

                using (var fill = new SolidBrush(fieldFill))
                {
                    graphics.FillPath(fill, fieldPath);
                }

                if (_search.Focused)
                {
                    using var accentBorder = new Pen(
                        Color.FromArgb(hasText ? 110 : 64, _theme.Accent),
                        MathF.Max(1f, 1.1f * _scale));

                    graphics.DrawPath(accentBorder, fieldPath);
                }
            }

            float radius = 7.5f * _scale;
            float centerX = _searchRect.Left + 22f * _scale;
            float centerY = _searchRect.Top + _searchRect.Height / 2f;

            Color iconColor = hasText
                ? _theme.Accent
                : _theme.TextMuted;

            using var pen = new Pen(iconColor, MathF.Max(1.2f, 1.6f * _scale))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };

            graphics.DrawEllipse(pen, centerX - radius, centerY - radius, radius * 2f, radius * 2f);

            const float diagonal = 0.70710678f;
            graphics.DrawLine(
                pen,
                centerX + radius * diagonal,
                centerY + radius * diagonal,
                centerX + radius * diagonal + radius * 0.85f,
                centerY + radius * diagonal + radius * 0.85f);

            if (hasText)
            {
                float lineInset = 16f * _scale;
                float lineY = _searchRect.Bottom - 5f * _scale;

                using var underline = new Pen(
                    Color.FromArgb(140, _theme.Accent),
                    MathF.Max(1f, 1.5f * _scale))
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round
                };

                graphics.DrawLine(
                    underline,
                    _searchRect.Left + lineInset,
                    lineY,
                    _searchRect.Right - lineInset,
                    lineY);
            }
        }

        private void DrawFooter(Graphics graphics)
        {
            DrawBadgeRow(
                graphics,
                new[] { ("↑↓", "Navigate"), ("↵", "Select"), ("RMB", "Pin"), ("ESC", "Close") },
                _footerRect.Top + 10f * _scale);

            DrawBadgeRow(
                graphics,
                new[] { ("Ctrl + Enter", "Open as administrator") },
                _footerRect.Top + 32f * _scale);
        }

        private void DrawBadgeRow(Graphics graphics, (string Key, string Label)[] items, float y)
        {
            float padX = 7f * _scale;
            float badgeHeight = 20f * _scale;
            float keyGap = 7f * _scale;
            float itemGap = 18f * _scale;

            var keyWidths = new float[items.Length];
            var labelWidths = new float[items.Length];
            float total = itemGap * (items.Length - 1);

            for (int i = 0; i < items.Length; i++)
            {
                keyWidths[i] = graphics.MeasureString(items[i].Key, _badgeFont).Width + padX * 2f;
                labelWidths[i] = graphics.MeasureString(items[i].Label, _badgeFont).Width;
                total += keyWidths[i] + keyGap + labelWidths[i];
            }

            float x = _footerRect.Left + (_footerRect.Width - total) / 2f;

            using var keyBrush = new SolidBrush(_theme.TextPrimary);
            using var labelBrush = new SolidBrush(_theme.TextMuted);
            using var keyFormat = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            using var labelFormat = new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center
            };

            for (int i = 0; i < items.Length; i++)
            {
                var badge = new RectangleF(x, y, keyWidths[i], badgeHeight);

                Color badgeFill = _theme.IsLight
                    ? Color.FromArgb(10, 0, 0, 0)
                    : Color.FromArgb(16, 255, 255, 255);
                Color badgeBorder = _theme.IsLight
                    ? Color.FromArgb(26, 0, 0, 0)
                    : Color.FromArgb(26, 255, 255, 255);

                using (GraphicsPath path = CreateRoundedPath(badge, 6f * _scale))
                using (var fill = new SolidBrush(badgeFill))
                {
                    graphics.FillPath(fill, path);

                    using var border = new Pen(badgeBorder, MathF.Max(1f, _scale));
                    graphics.DrawPath(border, path);
                }

                graphics.DrawString(items[i].Key, _badgeFont, keyBrush, badge, keyFormat);

                x += keyWidths[i] + keyGap;

                var labelRect = new RectangleF(x, y, labelWidths[i], badgeHeight);
                graphics.DrawString(items[i].Label, _badgeFont, labelBrush, labelRect, labelFormat);

                x += labelWidths[i] + itemGap;
            }
        }

        private void OnSearchKeyDown(object? sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Down:
                    _list.MoveSelection(1);
                    break;
                case Keys.Up:
                    _list.MoveSelection(-1);
                    break;
                case Keys.PageDown:
                    _list.MoveSelection(_list.VisibleRowCount);
                    break;
                case Keys.PageUp:
                    _list.MoveSelection(-_list.VisibleRowCount);
                    break;
                case Keys.Tab:
                    _list.MoveSelection(e.Shift ? -1 : 1);
                    break;
                case Keys.Enter:
                    if (_list.HasSelection)
                        _list.ActivateSelected(e.Control);
                    break;
                case Keys.Escape:
                    BeginClose();
                    break;
                default:
                    return;
            }

            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private void OnItemActivated(RofiApp app, bool asAdministrator)
        {
            // A single click and a follow-up double click can both land while
            // the close animation runs; only the first request may launch.
            if (_closing)
                return;

            TryLaunch(app, asAdministrator);
            BeginClose();
        }

        private static void TryLaunch(RofiApp app, bool asAdministrator)
        {
            if (string.IsNullOrWhiteSpace(app.Path))
                return;

            try
            {
                ProcessStartInfo startInfo = app.Kind == RofiAppKind.Uwp
                    ? CreateUwpStartInfo(app)
                    : CreateDesktopStartInfo(app, asAdministrator);

                Process.Start(startInfo);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // The user declined the UAC prompt; that is not an error.
                Console.WriteLine($"Rofi menu: elevation cancelled for '{app.Name}'.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Rofi menu: failed to launch '{app.Name}': {ex.Message}");
            }
        }

        private static ProcessStartInfo CreateDesktopStartInfo(RofiApp app, bool asAdministrator)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = app.Path,
                UseShellExecute = true
            };

            if (!string.IsNullOrWhiteSpace(app.Arguments))
                startInfo.Arguments = app.Arguments;

            if (!string.IsNullOrWhiteSpace(app.WorkingDirectory))
                startInfo.WorkingDirectory = app.WorkingDirectory;

            if (asAdministrator)
                startInfo.Verb = "runas";

            return startInfo;
        }

        private static ProcessStartInfo CreateUwpStartInfo(RofiApp app)
        {
            // Store apps cannot be elevated, so Ctrl+Enter launches them
            // normally instead of failing on an impossible runas request.
            return new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"shell:AppsFolder\\{app.Path}",
                UseShellExecute = true
            };
        }

        private void ApplyFilter(bool preserveSelection = false)
        {
            _list.SetItems(
                RofiSearch.Filter(RofiAppCatalog.GetApps(), _search.Text),
                preserveSelection);
        }

        internal void RequestClose()
        {
            BeginClose();
        }

        private void BeginClose()
        {
            if (_closing)
                return;

            if (!_animations)
            {
                _timer.Stop();
                Close();
                return;
            }

            _closing = true;
            _closeElapsedMs = 0f;
        }

        private void OnTick(object? sender, EventArgs e)
        {
            double now = _clock.Elapsed.TotalMilliseconds;
            float dtMs = (float)Math.Clamp(now - _lastTickMs, 0.0, 50.0);
            _lastTickMs = now;

            if (dtMs <= 0f)
                return;

            Advance(dtMs);
        }

        private void Advance(float dtMs)
        {
            _elapsedMs += dtMs;

            float entryProgress = Math.Clamp(_elapsedMs / EntryDurationMs, 0f, 1f);
            float easedEntry = EaseOutCubic(entryProgress);

            float closeProgress = 0f;
            if (_closing)
            {
                _closeElapsedMs += dtMs;
                closeProgress = Math.Clamp(_closeElapsedMs / ExitDurationMs, 0f, 1f);
            }

            float easedClose = EaseInCubic(closeProgress);

            float fade = _animations
                ? easedEntry * (1f - easedClose)
                : 1f - easedClose;

            Opacity = Math.Clamp(fade * _windowOpacity, 0.0, 1.0);

            float visual = _animations
                ? EntryStartScale + (1f - EntryStartScale) * easedEntry
                : 1f;
            visual *= 1f - (1f - ExitEndScale) * easedClose;

            float slide = _animations
                ? (1f - easedEntry) * EntrySlideDip * _scale
                  + easedClose * ExitSlideDip * _scale
                : 0f;

            ApplyLayout(visual, slide);
            _list.Advance(dtMs);

            // Past the entry animation the window content is static; repainting
            // the blurred backdrop every tick would only burn CPU.
            if (_closing || entryProgress < 1f)
                Invalidate();

            if (_closing && closeProgress >= 1f)
            {
                _timer.Stop();
                Close();
            }
        }

        private void ApplyLayout(float visualScale, float slide)
        {
            int width = Math.Max(1, (int)MathF.Round(_baseWidth * visualScale));
            int height = Math.Max(1, (int)MathF.Round(_baseHeight * visualScale));
            int left = _baseX + (_baseWidth - width) / 2;
            int top = _baseY + (_baseHeight - height) / 2 + (int)MathF.Round(slide);

            var bounds = new Rectangle(left, top, width, height);
            if (Bounds != bounds)
                Bounds = bounds;

            float s = _scale * visualScale;
            float pad = PaddingDip * s;
            float searchHeight = SearchHeightDip * s;
            float footerHeight = FooterHeightDip * s;

            _searchRect = new RectangleF(
                pad,
                pad,
                MathF.Max(1f, width - pad * 2f),
                searchHeight);

            float listTop = _searchRect.Bottom + GapDip * s;
            float footerTop = height - pad - footerHeight;

            _footerRect = new RectangleF(
                pad,
                footerTop,
                MathF.Max(1f, width - pad * 2f),
                footerHeight);

            int listHeight = Math.Max(1, (int)MathF.Round(footerTop - listTop - GapDip * s));
            _list.Bounds = new Rectangle(
                (int)MathF.Round(pad),
                (int)MathF.Round(listTop),
                Math.Max(1, (int)MathF.Round(width - pad * 2f)),
                listHeight);

            float textHeight = MathF.Max(1f, 26f * s);
            _search.Bounds = new Rectangle(
                (int)MathF.Round(_searchRect.Left + 44f * s),
                (int)MathF.Round(_searchRect.Top + (searchHeight - textHeight) / 2f),
                Math.Max(1, (int)MathF.Round(_searchRect.Width - 58f * s)),
                Math.Max(1, (int)MathF.Round(textHeight)));
        }

        private Bitmap? CaptureBackdrop()
        {
            try
            {
                var source = new Bitmap(_baseWidth, _baseHeight);

                using (Graphics graphics = Graphics.FromImage(source))
                {
                    graphics.CopyFromScreen(
                        _baseX,
                        _baseY,
                        0,
                        0,
                        new Size(_baseWidth, _baseHeight),
                        CopyPixelOperation.SourceCopy);
                }

                return Blur(source);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Rofi menu: backdrop capture failed: {ex.Message}");
                return null;
            }
        }

        private static Bitmap Blur(Bitmap source)
        {
            try
            {
                int smallWidth = Math.Max(1, source.Width / 9);
                int smallHeight = Math.Max(1, source.Height / 9);

                using var small = new Bitmap(smallWidth, smallHeight);

                using (Graphics graphics = Graphics.FromImage(small))
                {
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.DrawImage(source, 0, 0, smallWidth, smallHeight);
                }

                var blurred = new Bitmap(source.Width, source.Height);

                using (Graphics graphics = Graphics.FromImage(blurred))
                {
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.DrawImage(small, 0, 0, source.Width, source.Height);
                }

                return blurred;
            }
            finally
            {
                source.Dispose();
            }
        }

        private float EntryProgress()
        {
            return Math.Clamp(_elapsedMs / EntryDurationMs, 0f, 1f);
        }

        private void ApplyRoundedRegion()
        {
            if (Width <= 0 || Height <= 0)
                return;

            using GraphicsPath path = CreateRoundedPath(
                new RectangleF(0f, 0f, Width, Height),
                _cornerRadiusDip * _scale);

            Region? previous = Region;
            Region = new Region(path);
            previous?.Dispose();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            ApplyRoundedRegion();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer?.Stop();
                _timer?.Dispose();
                _searchFont.Dispose();
                _badgeFont.Dispose();
                _backdrop?.Dispose();
                _backdrop = null;
            }

            base.Dispose(disposing);
        }

        private static float EaseOutCubic(float progress)
        {
            float inverse = 1f - Math.Clamp(progress, 0f, 1f);
            return 1f - inverse * inverse * inverse;
        }

        private static float EaseInCubic(float progress)
        {
            float clamped = Math.Clamp(progress, 0f, 1f);
            return clamped * clamped * clamped;
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

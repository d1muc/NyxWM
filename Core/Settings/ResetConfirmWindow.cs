using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using WinFormsTimer = System.Windows.Forms.Timer;

namespace nyxWM
{
    /// <summary>
    /// Small confirmation dialog for the Reset action. Yes/No only; the reset
    /// itself is not wired to config yet, so both buttons just close the dialog.
    /// </summary>
    internal sealed class ResetConfirmWindow : Form
    {
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        private const float BaseWidthDip = 400f;
        private const float BaseHeightDip = 170f;
        private const float CornerRadiusDip = 16f;
        private const float PaddingDip = 24f;

        private const float EntryDurationMs = 200f;
        private const float ExitDurationMs = 170f;
        private const float EntrySlideDip = 9f;
        private const float ExitSlideDip = 7f;

        private static ResetConfirmWindow? _current;

        private readonly NotificationTheme _theme;
        private readonly float _scale;
        private readonly Font _titleFont;
        private readonly Font _bodyFont;
        private readonly Font _buttonFont;
        private readonly WinFormsTimer _timer;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly Point _targetLocation;

        private double _lastTickMs;
        private float _elapsedMs;
        private float _closeElapsedMs;
        private bool _closing;

        private ResetConfirmWindow(NotificationTheme theme, float scale)
        {
            _theme = theme ?? throw new ArgumentNullException(nameof(theme));
            _scale = Math.Clamp(scale, 0.75f, 3f);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = _theme.BackgroundTop;
            DoubleBuffered = true;
            Text = "Reset settings";
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint,
                true);

            int width = Math.Max(1, (int)MathF.Round(BaseWidthDip * _scale));
            int height = Math.Max(1, (int)MathF.Round(BaseHeightDip * _scale));
            Size = new Size(width, height);

            _targetLocation = ResolveCenterLocation(width, height);
            Location = _targetLocation;

            _titleFont = new Font("Segoe UI", 13f, FontStyle.Bold, GraphicsUnit.Point);
            _bodyFont = new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
            _buttonFont = new Font("Segoe UI", 9.75f, FontStyle.Bold, GraphicsUnit.Point);

            BuildContent(width, height);

            Opacity = 0.0;
            ApplyRoundedRegion();

            _timer = new WinFormsTimer { Interval = 15 };
            _timer.Tick += OnTick;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= WS_EX_TOOLWINDOW;
                return parameters;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            _clock.Restart();
            _lastTickMs = 0.0;
            _timer.Start();
            Activate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            float borderWidth = MathF.Max(1f, 1.15f * _scale);
            var cardRect = new RectangleF(
                borderWidth / 2f,
                borderWidth / 2f,
                Width - borderWidth,
                Height - borderWidth);

            if (cardRect.Width <= 0f || cardRect.Height <= 0f)
                return;

            using (GraphicsPath cardPath = CreateRoundedPath(cardRect, CornerRadiusDip * _scale))
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
        }

        private void BuildContent(int width, int height)
        {
            float pad = PaddingDip * _scale;
            float labelWidth = width - pad * 2f;

            var title = new Label
            {
                Text = "Reset settings?",
                AutoSize = false,
                BackColor = Color.Transparent,
                ForeColor = SettingsPalette.TextPrimary,
                Font = _titleFont,
                TextAlign = ContentAlignment.MiddleLeft,
                Bounds = new Rectangle(
                    (int)MathF.Round(pad),
                    (int)MathF.Round(24f * _scale),
                    Math.Max(1, (int)MathF.Round(labelWidth)),
                    Math.Max(1, (int)MathF.Round(28f * _scale)))
            };

            var message = new Label
            {
                Text = "Reset all settings to their default values? This cannot be undone.",
                AutoSize = false,
                BackColor = Color.Transparent,
                ForeColor = SettingsPalette.TextMuted,
                Font = _bodyFont,
                TextAlign = ContentAlignment.TopLeft,
                Bounds = new Rectangle(
                    (int)MathF.Round(pad),
                    (int)MathF.Round(58f * _scale),
                    Math.Max(1, (int)MathF.Round(labelWidth)),
                    Math.Max(1, (int)MathF.Round(44f * _scale)))
            };

            float buttonWidth = 96f * _scale;
            float buttonHeight = 36f * _scale;
            float gap = 10f * _scale;
            float bottomPad = 22f * _scale;
            float buttonTop = height - bottomPad - buttonHeight;

            var noButton = new SettingsRoundedButton(_theme.Accent, accented: false)
            {
                Text = "No",
                Font = _buttonFont,
                Bounds = new Rectangle(
                    (int)MathF.Round(width - pad - buttonWidth - gap - buttonWidth),
                    (int)MathF.Round(buttonTop),
                    Math.Max(1, (int)MathF.Round(buttonWidth)),
                    Math.Max(1, (int)MathF.Round(buttonHeight)))
            };

            var yesButton = new SettingsRoundedButton(_theme.Accent, accented: true)
            {
                Text = "Yes",
                Font = _buttonFont,
                Bounds = new Rectangle(
                    (int)MathF.Round(width - pad - buttonWidth),
                    (int)MathF.Round(buttonTop),
                    Math.Max(1, (int)MathF.Round(buttonWidth)),
                    Math.Max(1, (int)MathF.Round(buttonHeight)))
            };

            noButton.Click += (_, _) => BeginClose();
            yesButton.Click += (_, _) => BeginClose();

            Controls.Add(title);
            Controls.Add(message);
            Controls.Add(noButton);
            Controls.Add(yesButton);
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
            float easedEntry = EaseOutCubic(entryProgress);

            float closeProgress = 0f;
            if (_closing)
            {
                _closeElapsedMs += dtMs;
                closeProgress = Math.Clamp(_closeElapsedMs / ExitDurationMs, 0f, 1f);
            }

            float easedClose = EaseOutCubic(closeProgress);
            Opacity = Math.Clamp(easedEntry * (1f - easedClose), 0.0, 1.0);

            float slide = (1f - easedEntry) * EntrySlideDip * _scale
                          + easedClose * ExitSlideDip * _scale;

            var location = new Point(
                _targetLocation.X,
                _targetLocation.Y + (int)MathF.Round(slide));

            if (Location != location)
                Location = location;

            Invalidate();

            if (_closing && closeProgress >= 1f)
            {
                _timer.Stop();
                Close();
            }
        }

        private void BeginClose()
        {
            if (_closing)
                return;

            _closing = true;
            _closeElapsedMs = 0f;
        }

        private void ApplyRoundedRegion()
        {
            if (Width <= 0 || Height <= 0)
                return;

            using GraphicsPath path = CreateRoundedPath(
                new RectangleF(0f, 0f, Width, Height),
                CornerRadiusDip * _scale);

            Region? previous = Region;
            Region = new Region(path);
            previous?.Dispose();
        }

        private Point ResolveCenterLocation(int width, int height)
        {
            Rectangle work;
            try
            {
                work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ResetConfirmWindow: screen probe failed: {ex.Message}");
                work = new Rectangle(0, 0, 1920, 1080);
            }

            int x = work.Left + Math.Max(0, (work.Width - width) / 2);
            int y = work.Top + Math.Max(0, (work.Height - height) / 2);
            return new Point(x, y);
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
                _timer.Tick -= OnTick;
                _timer.Dispose();
                _titleFont.Dispose();
                _bodyFont.Dispose();
                _buttonFont.Dispose();
            }

            base.Dispose(disposing);
        }

        internal static void Open()
        {
            if (_current is { IsDisposed: false })
            {
                _current.Activate();
                return;
            }

            _current = null;

            try
            {
                NotificationTheme theme = NotificationThemeResolver.Resolve(ConfigManager.GetLiveTheme());
                var window = new ResetConfirmWindow(theme, GetDpiScale());

                _current = window;
                window.FormClosed += (_, _) =>
                {
                    if (ReferenceEquals(_current, window))
                        _current = null;
                };

                window.Show();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ResetConfirmWindow: failed to open: {ex.Message}");
            }
        }

        private static float GetDpiScale()
        {
            try
            {
                using Graphics graphics = Graphics.FromHwnd(IntPtr.Zero);
                return Math.Clamp(graphics.DpiX / 96f, 0.75f, 3f);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ResetConfirmWindow: DPI probe failed: {ex.Message}");
                return 1f;
            }
        }
    }
}

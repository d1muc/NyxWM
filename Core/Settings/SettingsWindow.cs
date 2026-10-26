using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using WinFormsTimer = System.Windows.Forms.Timer;

namespace nyxWM
{
    /// <summary>
    /// Borderless settings window. It opens in the centre of the screen with a
    /// fade/slide animation and closes with the reverse animation. Tabs slide
    /// their pages horizontally and the accent underline glides between them.
    /// </summary>
    internal sealed class SettingsWindow : Form, IMessageFilter
    {
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WM_MOUSEWHEEL = 0x020A;

        private const float BaseWidthDip = 880f;
        private const float BaseHeightDip = 640f;
        private const float CornerRadiusDip = 18f;
        private const float PaddingDip = 28f;
        private const float TabStripTopDip = 64f;
        private const float TabHeightDip = 34f;
        private const float TabGapDip = 6f;
        private const float TabPaddingDip = 18f;
        private const float TabRadiusDip = 9f;
        private const float ButtonWidthDip = 108f;
        private const float ButtonHeightDip = 38f;
        private const float ButtonGapDip = 12f;
        private const float ButtonRadiusDip = 10f;

        private const float EntryDurationMs = 260f;
        private const float ExitDurationMs = 220f;
        private const float EntrySlideDip = 12f;
        private const float ExitSlideDip = 10f;
        private const float HoverSmoothingPerSecond = 16f;
        private const float TabTransitionMs = 220f;
        private const float TabSlideDip = 44f;
        private const float IndicatorSmoothingPerSecond = 20f;

        private const int ButtonNone = -1;
        private const int ButtonCancel = 0;
        private const int ButtonSave = 1;
        private const int ButtonReset = 2;

        private static readonly string[] TabTitles =
        {
            "Core behaviour",
            "Camera",
            "Windows",
            "Hotkeys",
            "Launch apps",
            "Visuals",
            "Notifications",
            "Terminal"
        };

        private readonly NotificationTheme _theme;
        private readonly float _scale;
        private readonly Action<ConfigSettings>? _onSave;
        private readonly Action? _onReset;
        private readonly float _windowOpacity;
        private readonly bool _animations;
        private readonly float _cornerRadiusDip;
        private readonly Font _titleFont;
        private readonly Font _buttonFont;
        private readonly WinFormsTimer _timer;
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        private readonly RectangleF _cancelRect;
        private readonly RectangleF _saveRect;
        private readonly RectangleF _resetRect;
        private readonly RectangleF[] _tabRects;
        private readonly float[] _tabHover;

        private readonly SettingsContentBuilder _contentBuilder;
        private readonly SettingsContentPanel _viewport;
        private readonly SettingsContentPanel[] _tabPanels;
        private readonly int[] _scrollOffsets;
        private readonly Point _targetLocation;
        private bool _wheelFilterInstalled;

        private double _lastTickMs;
        private float _elapsedMs;
        private float _closeElapsedMs;
        private bool _closing;
        private float _hoverCancel;
        private float _hoverSave;
        private float _hoverReset;
        private bool _cancelHovered;
        private bool _saveHovered;
        private bool _resetHovered;
        private int _pressedButton = ButtonNone;
        private int _pressedTab = -1;
        private int _activeTab;
        private int _hoveredTab = -1;

        private float _indicatorLeft;
        private float _indicatorRight;

        private SettingsContentPanel? _transitionFrom;
        private SettingsContentPanel? _transitionTo;
        private int _transitionDirection;
        private float _transitionElapsedMs;

        internal SettingsWindow(
            NotificationTheme theme,
            float scale,
            Action<ConfigSettings>? onSave,
            Action? onReset)
        {
            _theme = theme ?? throw new ArgumentNullException(nameof(theme));
            _scale = Math.Clamp(scale, 0.75f, 3f);
            _onSave = onSave;
            _onReset = onReset;
            _windowOpacity = VisualPreferences.WindowOpacity;
            _animations = VisualPreferences.SmoothAnimations;
            _cornerRadiusDip = VisualPreferences.CornerRadius(CornerRadiusDip);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = _theme.BackgroundTop;
            DoubleBuffered = true;
            Text = "Settings";
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

            _titleFont = VisualPreferences.CreateUiFont("Segoe UI", 15f, FontStyle.Bold);
            _buttonFont = VisualPreferences.CreateUiFont("Segoe UI", 9.75f, FontStyle.Bold);

            float pad = PaddingDip * _scale;
            float buttonWidth = ButtonWidthDip * _scale;
            float buttonHeight = ButtonHeightDip * _scale;
            float gap = ButtonGapDip * _scale;
            float buttonTop = Height - pad - buttonHeight;

            _saveRect = new RectangleF(Width - pad - buttonWidth, buttonTop, buttonWidth, buttonHeight);
            _cancelRect = new RectangleF(
                _saveRect.Left - gap - buttonWidth,
                buttonTop,
                buttonWidth,
                buttonHeight);
            _resetRect = new RectangleF(pad, buttonTop, buttonWidth, buttonHeight);

            _tabRects = BuildTabLayout();
            _tabHover = new float[_tabRects.Length];

            float contentTopDip = TabStripTopDip + TabHeightDip + 22f;
            int contentLeft = (int)MathF.Round(pad);
            int contentTop = (int)MathF.Round(contentTopDip * _scale);
            int contentWidth = (int)MathF.Round(Width - pad * 2f);
            int contentBottom = (int)MathF.Round(Height - pad - buttonHeight - 18f * _scale);
            int contentHeight = Math.Max(1, contentBottom - contentTop);

            _viewport = new SettingsContentPanel(_theme)
            {
                Bounds = new Rectangle(contentLeft, contentTop, contentWidth, contentHeight)
            };

            _contentBuilder = new SettingsContentBuilder(_theme, _scale, ConfigManager.Current);
            _tabPanels = _contentBuilder.Build(contentWidth, contentHeight);

            foreach (SettingsContentPanel page in _tabPanels)
            {
                page.Visible = false;
                page.MinimumHeight = contentHeight;
                page.ContentPadding = (int)MathF.Round(16f * _scale);
                page.FitToContent();
                _viewport.Controls.Add(page);
            }

            _scrollOffsets = new int[_tabPanels.Length];

            _tabPanels[_activeTab].Visible = true;
            Controls.Add(_viewport);

            _indicatorLeft = _tabRects[_activeTab].Left;
            _indicatorRight = _tabRects[_activeTab].Right;

            Opacity = _animations ? 0.0 : _windowOpacity;
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

            if (!_wheelFilterInstalled)
            {
                Application.AddMessageFilter(this);
                _wheelFilterInstalled = true;
            }

            _clock.Restart();
            _lastTickMs = 0.0;
            _timer.Start();
            Activate();
        }

        /// <summary>
        /// Scrolls the active page on wheel over the content area, including
        /// when the pointer rests on a label or a card that would otherwise
        /// swallow the message.
        /// </summary>
        public bool PreFilterMessage(ref Message message)
        {
            if (message.Msg != WM_MOUSEWHEEL || !Visible || _closing || IsDisposed)
                return false;

            if (!_viewport.IsHandleCreated)
                return false;

            int x = unchecked((short)(long)message.LParam);
            int y = unchecked((short)((long)message.LParam >> 16));

            if (!_viewport.RectangleToScreen(_viewport.ClientRectangle).Contains(x, y))
                return false;

            int delta = unchecked((short)((long)message.WParam >> 16));
            if (delta == 0)
                return false;

            ScrollActivePage(delta);
            return true;
        }

        private void ScrollActivePage(int wheelDelta)
        {
            int lines = SystemInformation.MouseWheelScrollLines;
            if (lines == 0)
                return;

            SettingsContentPanel page = _tabPanels[_activeTab];
            int viewportHeight = _viewport.ClientSize.Height;

            if (page.Height <= viewportHeight)
                return;

            int notches = wheelDelta / 120;
            if (notches == 0)
                notches = Math.Sign(wheelDelta);

            int step = lines < 0
                ? viewportHeight
                : (int)MathF.Round(lines * 44f * _scale);

            int offset = Math.Clamp(
                _scrollOffsets[_activeTab] - notches * step,
                0,
                page.Height - viewportHeight);

            _scrollOffsets[_activeTab] = offset;
            page.Top = -offset;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            float borderWidth = MathF.Max(1f, 1.15f * _scale);
            var cardRect = new RectangleF(
                borderWidth / 2f,
                borderWidth / 2f,
                Width - borderWidth,
                Height - borderWidth);

            if (cardRect.Width <= 0f || cardRect.Height <= 0f)
                return;

            using (GraphicsPath cardPath = CreateRoundedPath(cardRect, _cornerRadiusDip * _scale))
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
            DrawHeader(graphics);
            DrawTabs(graphics);
            DrawFooterButton(graphics, _resetRect, "Reset", false, _hoverReset, _pressedButton == ButtonReset, danger: true);
            DrawFooterButton(graphics, _cancelRect, "Cancel", false, _hoverCancel, _pressedButton == ButtonCancel);
            DrawFooterButton(graphics, _saveRect, "Save", true, _hoverSave, _pressedButton == ButtonSave);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            UpdateHover(e.Location);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _cancelHovered = false;
            _saveHovered = false;
            _resetHovered = false;
            _hoveredTab = -1;
            Cursor = Cursors.Default;
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button != MouseButtons.Left || _closing)
                return;

            _pressedButton = HitTestButton(e.Location);
            _pressedTab = _pressedButton == ButtonNone ? HitTestTab(e.Location) : -1;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (e.Button != MouseButtons.Left)
                return;

            int buttonHit = HitTestButton(e.Location);
            int tabHit = buttonHit == ButtonNone ? HitTestTab(e.Location) : -1;

            bool activateButton = buttonHit != ButtonNone && buttonHit == _pressedButton;
            bool activateTab = tabHit >= 0 && tabHit == _pressedTab;
            bool save = buttonHit == ButtonSave;

            _pressedButton = ButtonNone;
            _pressedTab = -1;
            Invalidate();

            if (activateTab)
            {
                SetActiveTab(tabHit);
                return;
            }

            if (!activateButton)
                return;

            if (buttonHit == ButtonReset)
            {
                OpenResetConfirm();
                return;
            }

            if (save)
                _onSave?.Invoke(_contentBuilder.CollectConfig());

            BeginClose();
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

            float entryProgress = _animations
                ? Math.Clamp(_elapsedMs / EntryDurationMs, 0f, 1f)
                : 1f;
            float easedEntry = EaseOutCubic(entryProgress);

            float closeProgress = 0f;
            if (_closing && _animations)
            {
                _closeElapsedMs += dtMs;
                closeProgress = Math.Clamp(_closeElapsedMs / ExitDurationMs, 0f, 1f);
            }

            float easedClose = EaseOutCubic(closeProgress);
            Opacity = Math.Clamp(easedEntry * (1f - easedClose) * _windowOpacity, 0.0, 1.0);

            float slide = _animations
                ? (1f - easedEntry) * EntrySlideDip * _scale
                  + easedClose * ExitSlideDip * _scale
                : 0f;

            var location = new Point(
                _targetLocation.X,
                _targetLocation.Y + (int)MathF.Round(slide));

            if (Location != location)
                Location = location;

            float hoverFactor = 1f - MathF.Exp(-HoverSmoothingPerSecond * dtMs / 1000f);
            _hoverCancel += ((_cancelHovered ? 1f : 0f) - _hoverCancel) * hoverFactor;
            _hoverSave += ((_saveHovered ? 1f : 0f) - _hoverSave) * hoverFactor;
            _hoverReset += ((_resetHovered ? 1f : 0f) - _hoverReset) * hoverFactor;

            for (int i = 0; i < _tabHover.Length; i++)
            {
                float target = i == _hoveredTab ? 1f : 0f;
                _tabHover[i] += (target - _tabHover[i]) * hoverFactor;
            }

            float indicatorFactor = 1f - MathF.Exp(-IndicatorSmoothingPerSecond * dtMs / 1000f);
            _indicatorLeft += (_tabRects[_activeTab].Left - _indicatorLeft) * indicatorFactor;
            _indicatorRight += (_tabRects[_activeTab].Right - _indicatorRight) * indicatorFactor;

            AdvanceTabTransition(dtMs);

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
            _cancelHovered = false;
            _saveHovered = false;
            _resetHovered = false;
            _pressedButton = ButtonNone;
            _pressedTab = -1;
            Cursor = Cursors.Default;

            if (!_animations)
            {
                _timer.Stop();
                Close();
            }
        }

        private void OpenResetConfirm()
        {
            ResetConfirmWindow.Open(PerformReset);
        }

        private void PerformReset()
        {
            _onReset?.Invoke();
            BeginClose();
        }

        private void SetActiveTab(int index)
        {
            if (index < 0 || index >= _tabPanels.Length || index == _activeTab)
                return;

            if (_transitionTo != null)
                FinishTransition();

            int previous = _activeTab;
            _activeTab = index;

            SettingsContentPanel from = _tabPanels[previous];
            SettingsContentPanel to = _tabPanels[index];

            _transitionDirection = index > previous ? 1 : -1;
            _transitionElapsedMs = 0f;
            _transitionFrom = from;
            _transitionTo = to;

            int offset = (int)MathF.Round(TabSlideDip * _scale);
            to.Left = _transitionDirection * offset;
            to.Visible = true;
            to.BringToFront();
            from.Enabled = false;
            to.Enabled = true;

            if (!_animations)
            {
                _transitionFrom = null;
                _transitionTo = null;
                _transitionDirection = 0;
                from.Visible = false;
                from.Enabled = true;
                to.Left = 0;
                Invalidate();
            }
        }

        private void AdvanceTabTransition(float dtMs)
        {
            if (_transitionFrom == null || _transitionTo == null)
                return;

            _transitionElapsedMs += dtMs;
            float progress = Math.Clamp(_transitionElapsedMs / TabTransitionMs, 0f, 1f);
            float eased = EaseInOutCubic(progress);
            int offset = (int)MathF.Round(TabSlideDip * _scale);

            _transitionFrom.Left = (int)MathF.Round(-_transitionDirection * offset * eased);
            _transitionTo.Left = (int)MathF.Round(_transitionDirection * offset * (1f - eased));

            if (progress >= 1f)
                FinishTransition();
        }

        private void FinishTransition()
        {
            if (_transitionFrom != null)
            {
                _transitionFrom.Visible = false;
                _transitionFrom.Left = 0;
                _transitionFrom.Enabled = true;
            }

            if (_transitionTo != null)
            {
                _transitionTo.Left = 0;
                _transitionTo.Enabled = true;
                _transitionTo.BringToFront();
            }

            _transitionFrom = null;
            _transitionTo = null;
            _transitionDirection = 0;
        }

        private void UpdateHover(Point location)
        {
            int buttonHit = _closing ? ButtonNone : HitTestButton(location);
            int tabHit = buttonHit == ButtonNone && !_closing ? HitTestTab(location) : -1;

            _cancelHovered = buttonHit == ButtonCancel;
            _saveHovered = buttonHit == ButtonSave;
            _resetHovered = buttonHit == ButtonReset;
            _hoveredTab = tabHit;
            Cursor = buttonHit == ButtonNone && tabHit < 0 ? Cursors.Default : Cursors.Hand;
        }

        private int HitTestButton(Point location)
        {
            if (_cancelRect.Contains(location))
                return ButtonCancel;
            if (_saveRect.Contains(location))
                return ButtonSave;
            if (_resetRect.Contains(location))
                return ButtonReset;

            return ButtonNone;
        }

        private int HitTestTab(Point location)
        {
            for (int i = 0; i < _tabRects.Length; i++)
            {
                if (_tabRects[i].Contains(location))
                    return i;
            }

            return -1;
        }

        private void DrawAccentGlow(Graphics graphics)
        {
            float size = 240f * _scale;
            using var glow = new SolidBrush(Color.FromArgb(14, _theme.Accent));
            graphics.FillEllipse(glow, PaddingDip * _scale - size / 2f, -size / 2f, size, size);
        }

        private void DrawHeader(Graphics graphics)
        {
            float pad = PaddingDip * _scale;
            float barWidth = 4f * _scale;
            float barHeight = 20f * _scale;
            var barRect = new RectangleF(pad, pad + 3f * _scale, barWidth, barHeight);

            using (GraphicsPath barPath = CreateRoundedPath(barRect, barWidth / 2f))
            using (var accent = new SolidBrush(_theme.Accent))
            {
                graphics.FillPath(accent, barPath);
            }

            using var titleBrush = new SolidBrush(_theme.TextPrimary);
            graphics.DrawString(
                "Settings",
                _titleFont,
                titleBrush,
                new PointF(pad + barWidth + 12f * _scale, pad - 4f * _scale));
        }

        private void DrawTabs(Graphics graphics)
        {
            for (int i = 0; i < _tabRects.Length; i++)
            {
                RectangleF rect = _tabRects[i];
                bool active = i == _activeTab;
                float hover = _tabHover[i];

                using GraphicsPath path = CreateRoundedPath(rect, TabRadiusDip * _scale);

                if (active)
                {
                    using var fill = new SolidBrush(Color.FromArgb(30, _theme.Accent));
                    graphics.FillPath(fill, path);

                    using var border = new Pen(
                        Color.FromArgb(84, _theme.Accent),
                        MathF.Max(1f, _scale));
                    graphics.DrawPath(border, path);
                }
                else if (hover > 0.01f)
                {
                    int alpha = (int)Math.Clamp(12f * hover, 0f, 255f);
                    using var fill = new SolidBrush(Color.FromArgb(alpha, _theme.Overlay));
                    graphics.FillPath(fill, path);
                }

                int textAlpha = active
                    ? 245
                    : (int)Math.Clamp(140f + 70f * hover, 0f, 255f);

                using var textBrush = new SolidBrush(Color.FromArgb(textAlpha, _theme.TextPrimary));
                using var format = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center,
                    Trimming = StringTrimming.EllipsisCharacter,
                    FormatFlags = StringFormatFlags.NoWrap
                };

                graphics.DrawString(TabTitles[i], _buttonFont, textBrush, rect, format);
            }

            float underlineY = _tabRects[0].Bottom + 4f * _scale;
            using var underline = new Pen(
                _theme.Accent,
                MathF.Max(1.5f, 2f * _scale))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };

            graphics.DrawLine(
                underline,
                _indicatorLeft + 10f * _scale,
                underlineY,
                _indicatorRight - 10f * _scale,
                underlineY);

            float pad = PaddingDip * _scale;
            float dividerY = (TabStripTopDip + TabHeightDip + 10f) * _scale;
            using var divider = new Pen(
                Color.FromArgb(38, _theme.Overlay),
                MathF.Max(1f, 0.9f * _scale));
            graphics.DrawLine(divider, pad, dividerY, Width - pad, dividerY);
        }

        private void DrawFooterButton(
            Graphics graphics,
            RectangleF rect,
            string label,
            bool accent,
            float hover,
            bool pressed,
            bool danger = false)
        {
            float radius = ButtonRadiusDip * _scale;

            Color ghostTint = danger
                ? Color.FromArgb(242, 255, 128, 134)
                : Color.FromArgb(242, _theme.TextPrimary);

            using GraphicsPath path = CreateRoundedPath(rect, radius);

            if (accent)
            {
                if (hover > 0.01f)
                {
                    using var glow = new SolidBrush(Color.FromArgb((int)(30f * hover), _theme.Accent));
                    using GraphicsPath glowPath = CreateRoundedPath(
                        RectangleF.Inflate(rect, 2f * _scale, 2f * _scale),
                        radius + 2f * _scale);
                    graphics.FillPath(glow, glowPath);
                }

                Color fill = Adjust(_theme.Accent, hover * 0.10f - (pressed ? 0.08f : 0f));
                using var brush = new SolidBrush(fill);
                graphics.FillPath(brush, path);
            }
            else
            {
                int alpha = (int)Math.Clamp(14f + 18f * hover, 0f, 255f);
                using var brush = new SolidBrush(Color.FromArgb(
                    Math.Min(alpha, ghostTint.A),
                    ghostTint.R,
                    ghostTint.G,
                    ghostTint.B));
                graphics.FillPath(brush, path);

                int borderAlpha = (int)Math.Clamp(48f + 58f * hover, 0f, 255f);
                using var border = new Pen(
                    Color.FromArgb(Math.Min(borderAlpha, ghostTint.A), ghostTint.R, ghostTint.G, ghostTint.B),
                    MathF.Max(1f, _scale));
                graphics.DrawPath(border, path);
            }

            Color textColor = accent
                ? _theme.OnAccent
                : danger
                    ? Color.FromArgb(255, 168, 174)
                    : _theme.TextPrimary;

            using var textBrush = new SolidBrush(textColor);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap
            };

            graphics.DrawString(label, _buttonFont, textBrush, rect, format);
        }

        private RectangleF[] BuildTabLayout()
        {
            float pad = PaddingDip * _scale;
            float top = TabStripTopDip * _scale;
            float height = TabHeightDip * _scale;
            float gap = TabGapDip * _scale;
            float available = Width - pad * 2f;

            var widths = new float[TabTitles.Length];

            using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero))
            using (var format = new StringFormat(StringFormat.GenericTypographic))
            {
                for (int i = 0; i < TabTitles.Length; i++)
                {
                    float textWidth = graphics.MeasureString(
                        TabTitles[i],
                        _buttonFont,
                        int.MaxValue,
                        format).Width;

                    widths[i] = MathF.Ceiling(textWidth) + TabPaddingDip * 2f * _scale;
                }
            }

            float total = gap * (TabTitles.Length - 1);
            for (int i = 0; i < widths.Length; i++)
                total += widths[i];

            if (total > available && widths.Length > 0)
            {
                float equalWidth = MathF.Max(
                    1f,
                    (available - gap * (widths.Length - 1)) / widths.Length);

                for (int i = 0; i < widths.Length; i++)
                    widths[i] = equalWidth;
            }

            var rects = new RectangleF[widths.Length];
            float left = pad;

            for (int i = 0; i < widths.Length; i++)
            {
                rects[i] = new RectangleF(left, top, widths[i], height);
                left += widths[i] + gap;
            }

            return rects;
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

        private Point ResolveCenterLocation(int width, int height)
        {
            Rectangle work;
            try
            {
                work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SettingsWindow: screen probe failed: {ex.Message}");
                work = new Rectangle(0, 0, 1920, 1080);
            }

            int x = work.Left + Math.Max(0, (work.Width - width) / 2);
            int y = work.Top + Math.Max(0, (work.Height - height) / 2);
            return new Point(x, y);
        }

        private static Color Adjust(Color color, float amount)
        {
            float clamped = Math.Clamp(amount, -1f, 1f);
            Color target = clamped >= 0f ? Color.White : Color.Black;
            float weight = MathF.Abs(clamped);

            return Color.FromArgb(
                color.A,
                (int)MathF.Round(color.R + (target.R - color.R) * weight),
                (int)MathF.Round(color.G + (target.G - color.G) * weight),
                (int)MathF.Round(color.B + (target.B - color.B) * weight));
        }

        private static float EaseOutCubic(float progress)
        {
            float inverse = 1f - Math.Clamp(progress, 0f, 1f);
            return 1f - inverse * inverse * inverse;
        }

        private static float EaseInOutCubic(float progress)
        {
            float clamped = Math.Clamp(progress, 0f, 1f);

            return clamped < 0.5f
                ? 4f * clamped * clamped * clamped
                : 1f - MathF.Pow(-2f * clamped + 2f, 3f) / 2f;
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
                if (_wheelFilterInstalled)
                {
                    Application.RemoveMessageFilter(this);
                    _wheelFilterInstalled = false;
                }

                _timer.Tick -= OnTick;
                _timer.Dispose();
                _titleFont.Dispose();
                _buttonFont.Dispose();
            }

            base.Dispose(disposing);

            if (disposing)
            {
                _contentBuilder.Dispose();
            }
        }
    }
}

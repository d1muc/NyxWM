using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
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

        private const float BaseWidthDip = 1080f;
        private const float BaseHeightDip = 720f;
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
        private const float LayoutFocusWindowMs = 2000f;
        private const int LayoutRecoveryAttempts = 8;
        private const int LayoutRecoveryStepMs = 150;

        // Output is coalesced instead of painted per read: a full-screen program
        // such as cmatrix writes many small chunks per frame, and repainting each
        // one would burn a core per window. Focused windows stay smooth, background
        // windows drop to a low refresh rate.
        private const int OutputPumpFocusedMs = 16;
        private const int OutputPumpBackgroundMs = 100;

        private NotificationTheme _theme;
        private NotificationTheme _themeFrom;
        private float _themeProgress = 1f;
        private readonly float _scale;
        private readonly Font _titleFont;
        private Font _monoFont;
        private readonly Action<IntPtr>? _requestCenter;
        private float _windowOpacity;
        private readonly bool _animations;
        private readonly float _cornerRadiusDip;
        private readonly WinFormsTimer _timer;
        private readonly WinFormsTimer _blinkTimer;
        private readonly WinFormsTimer _pumpTimer;
        private readonly StringBuilder _pendingOutput = new();
        private readonly object _pendingLock = new();
        private bool _pumpRunning;
        private readonly SolidBrush _textBrush = new(Color.Black);
        private readonly SolidBrush _cellBrush = new(Color.Black);
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        private TerminalColorScheme _scheme;
        private TerminalColorScheme _schemeFrom;
        private string _schemeName;
        private string _fontFamily;
        private float _fontSize;
        private string _cursorStyle;
        private bool _cursorBlink;
        private bool _autoScroll;
        private bool _copyOnSelect;
        private string _shellName;
        private bool _cursorBlinkOn = true;

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
        private Color _consoleForeground;
        private Color _cursorColor;
        private readonly Color[] _ansiPalette = new Color[16];

        private double _lastTickMs;
        private float _elapsedMs;

        private TerminalSession? _session;
        private AnsiScreenBuffer? _screen;
        private int _consoleColumns;
        private int _consoleRows;
        private float _cellWidth = 8f;
        private float _cellHeight = 16f;
        private string? _sessionError;
        private bool _sessionEnded;

        private bool _selecting;
        private bool _hasSelection;
        private Point _selectionStart;
        private Point _selectionEnd;
        private double _lastFocusMs;

        internal TerminalWindow(
            NotificationTheme theme,
            float scale,
            int cascadeIndex,
            Action<IntPtr>? requestCenter)
        {
            _theme = theme ?? throw new ArgumentNullException(nameof(theme));
            _scale = Math.Clamp(scale, 0.75f, 3f);
            _requestCenter = requestCenter;

            TerminalSettings terminal = TerminalPreferences.Current;
            _fontFamily = TerminalPreferences.NormalizeFontFamily(terminal.FontFamily);
            _fontSize = TerminalPreferences.NormalizeFontSize(terminal.FontSize);
            _cursorStyle = TerminalPreferences.NormalizeCursorStyle(terminal.CursorStyle);
            _cursorBlink = terminal.CursorBlink;
            _autoScroll = terminal.AutoScroll;
            _copyOnSelect = terminal.CopyOnSelect;
            _shellName = TerminalPreferences.NormalizeShell(terminal.Shell);
            _schemeName = TerminalPreferences.NormalizeColorScheme(terminal.ColorScheme);

            _windowOpacity = TerminalPreferences.NormalizeOpacity(terminal.Opacity);
            _animations = VisualPreferences.SmoothAnimations;
            _cornerRadiusDip = VisualPreferences.CornerRadius(CornerRadiusDip);
            _titleFont = VisualPreferences.CreateUiFont("Segoe UI", 9.5f, FontStyle.Bold);
            _monoFont = TerminalPreferences.CreateConsoleFont(_fontFamily, _fontSize);

            _scheme = TerminalColorScheme.Create(_schemeName, _theme);
            _schemeFrom = _scheme;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = true;
            TopMost = false;
            BackColor = _theme.BackgroundTop;
            DoubleBuffered = true;
            KeyPreview = true;
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

            _blinkTimer = new WinFormsTimer { Interval = 530 };
            _blinkTimer.Tick += OnBlinkTick;
            UpdateBlinkTimer();

            _pumpTimer = new WinFormsTimer { Interval = OutputPumpFocusedMs };
            _pumpTimer.Tick += OnOutputPumpTick;

            NotificationService.LayoutSwitched += OnLayoutSwitched;
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

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            Activate();
            SetForegroundWindow(Handle);

            StartSession();

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

            _lastFocusMs = _clock.Elapsed.TotalMilliseconds;
            _pumpTimer.Interval = OutputPumpFocusedMs;
            _cursorBlinkOn = true;

            // Centring on a mouse click is wanted as much as on alt-tab, so no
            // button-state check: the camera follows the window the user clicked.
            _requestCenter?.Invoke(Handle);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);

            _pumpTimer.Interval = OutputPumpBackgroundMs;
            _cursorBlinkOn = true;
            InvalidateCursor();
        }

        /// <summary>
        /// A keyboard-layout switch makes Windows show its language flyout, which
        /// briefly steals the foreground and swallows typed keys. Once the flyout
        /// is gone, hand the foreground back to the terminal so typing resumes
        /// without an extra click.
        /// </summary>
        private void OnLayoutSwitched()
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            // Only when this terminal was the window being typed into recently,
            // so a layout switch elsewhere never yanks focus to this window.
            if (_clock.Elapsed.TotalMilliseconds - _lastFocusMs > LayoutFocusWindowMs)
                return;

            BeginInvoke(async () =>
            {
                // The flyout is created lazily and can take a while to disappear.
                // Retry instead of guessing a single delay, and stop as soon as the
                // terminal is foreground again.
                for (int attempt = 0; attempt < LayoutRecoveryAttempts; attempt++)
                {
                    await Task.Delay(LayoutRecoveryStepMs);

                    if (IsDisposed || !IsHandleCreated)
                        return;

                    if (WinApi.GetForegroundWindow() == Handle)
                        return;

                    if (TryTakeForeground())
                        return;
                }
            });
        }

        /// <summary>
        /// Brings this window to the foreground, attaching to the current foreground
        /// thread first so Windows does not reject <c>SetForegroundWindow</c> while
        /// another process (the language flyout) owns the foreground lock.
        /// </summary>
        private bool TryTakeForeground()
        {
            IntPtr foreground = WinApi.GetForegroundWindow();
            uint foregroundThread = foreground == IntPtr.Zero
                ? 0u
                : WinApi.GetWindowThreadProcessId(foreground, out _);
            uint ownThread = GetCurrentThreadId();

            bool attached = foregroundThread != 0
                            && foregroundThread != ownThread
                            && AttachThreadInput(ownThread, foregroundThread, true);

            try
            {
                SetForegroundWindow(Handle);
                Activate();
                Focus();
            }
            finally
            {
                if (attached)
                    AttachThreadInput(ownThread, foregroundThread, false);
            }

            return WinApi.GetForegroundWindow() == Handle;
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

        private void OnBlinkTick(object? sender, EventArgs e)
        {
            if (IsDisposed)
                return;

            if (!_cursorBlink || !ContainsFocus)
            {
                if (!_cursorBlinkOn)
                {
                    _cursorBlinkOn = true;
                    InvalidateCursor();
                }

                return;
            }

            _cursorBlinkOn = !_cursorBlinkOn;
            InvalidateCursor();
        }

        private void UpdateBlinkTimer()
        {
            _cursorBlinkOn = true;
            _blinkTimer.Stop();

            if (_cursorBlink)
                _blinkTimer.Start();
        }

        private void ResetCursorBlink()
        {
            if (!_cursorBlink || _cursorBlinkOn)
                return;

            _cursorBlinkOn = true;
            Invalidate();
        }

        /// <summary>
        /// Re-applies the Terminal settings save to this open window: font size
        /// and family, cursor, palette, opacity and behaviour switch live. A
        /// shell change restarts only the session, not the window.
        /// </summary>
        internal void ApplySettings(bool animate)
        {
            if (IsDisposed)
                return;

            TerminalSettings settings = TerminalPreferences.Current;

            string fontFamily = TerminalPreferences.NormalizeFontFamily(settings.FontFamily);
            float fontSize = TerminalPreferences.NormalizeFontSize(settings.FontSize);
            bool fontChanged = !string.Equals(fontFamily, _fontFamily, StringComparison.Ordinal)
                               || Math.Abs(fontSize - _fontSize) > 0.01f;

            string shell = TerminalPreferences.NormalizeShell(settings.Shell);
            bool shellChanged = !string.Equals(shell, _shellName, StringComparison.Ordinal);

            _cursorStyle = TerminalPreferences.NormalizeCursorStyle(settings.CursorStyle);
            _cursorBlink = settings.CursorBlink;
            _autoScroll = settings.AutoScroll;
            _copyOnSelect = settings.CopyOnSelect;
            _windowOpacity = TerminalPreferences.NormalizeOpacity(settings.Opacity);

            if (!_animations || _elapsedMs >= EntryDurationMs)
                Opacity = _windowOpacity;

            string schemeName = TerminalPreferences.NormalizeColorScheme(settings.ColorScheme);
            if (!string.Equals(schemeName, _schemeName, StringComparison.Ordinal))
                ApplyScheme(schemeName, animate);

            if (fontChanged)
            {
                _fontFamily = fontFamily;
                _fontSize = fontSize;
                RebuildConsoleFont();
            }

            if (shellChanged)
            {
                _shellName = shell;
                RestartSession();
            }

            UpdateBlinkTimer();
            Invalidate();
        }

        private void ApplyScheme(string schemeName, bool animate)
        {
            _schemeFrom = _scheme;
            _schemeName = schemeName;
            _scheme = TerminalColorScheme.Create(schemeName, _theme);

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

        private void RebuildConsoleFont()
        {
            Font previous = _monoFont;
            _monoFont = TerminalPreferences.CreateConsoleFont(_fontFamily, _fontSize);
            previous.Dispose();

            UpdateConsoleMetrics();
            Invalidate();
        }

        private void RestartSession()
        {
            TerminalSession? session = _session;
            _session = null;
            _screen = null;

            // Output queued by the old shell must not be fed into the new screen.
            lock (_pendingLock)
            {
                _pendingOutput.Clear();
            }

            session?.Dispose();

            _sessionError = null;
            _sessionEnded = false;
            _hasSelection = false;
            _selecting = false;

            StartSession();
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
            _schemeFrom = _scheme;
            _scheme = TerminalColorScheme.Create(_schemeName, theme);

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

            _consoleFill = Blend(_schemeFrom.Background, _scheme.Background, t);
            _consoleForeground = Blend(_schemeFrom.Foreground, _scheme.Foreground, t);
            _cursorColor = Blend(_schemeFrom.Cursor, _scheme.Cursor, t);

            for (int index = 0; index < _ansiPalette.Length; index++)
            {
                _ansiPalette[index] = Blend(
                    _schemeFrom.PaletteColor(index),
                    _scheme.PaletteColor(index),
                    t);
            }

            Invalidate();
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

            if (e.Button == MouseButtons.Right)
            {
                if (_hasSelection)
                    CopySelection();
                return;
            }

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
                return;
            }

            if (TryGetConsoleCell(e.Location, out int row, out int column))
            {
                _selecting = true;
                _hasSelection = false;
                _selectionStart = new Point(column, row);
                _selectionEnd = _selectionStart;
                Capture = true;
                Invalidate();
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

            if (_selecting)
            {
                if (TryGetConsoleCell(e.Location, out int row, out int column))
                {
                    _selectionEnd = new Point(column, row);
                    _hasSelection = _selectionStart != _selectionEnd;
                    Invalidate();
                }

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

            if (e.Button != MouseButtons.Left)
                return;

            _dragging = false;

            if (_selecting)
            {
                _selecting = false;
                Capture = false;

                if (TryGetConsoleCell(e.Location, out int row, out int column))
                {
                    _selectionEnd = new Point(column, row);
                    _hasSelection = _selectionStart != _selectionEnd;
                }

                if (_copyOnSelect && _hasSelection)
                    CopySelection();

                Invalidate();
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);

            AnsiScreenBuffer? screen = _screen;
            if (screen is null)
                return;

            int lines = SystemInformation.MouseWheelScrollLines;
            if (lines <= 0)
                lines = 3;

            _hasSelection = false;
            _selecting = false;

            screen.Scroll(e.Delta > 0 ? lines : -lines);
            Invalidate();
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

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);

            ResetCursorBlink();

            if (char.IsControl(e.KeyChar))
                return;

            _hasSelection = false;
            _screen?.ScrollToBottom();

            TerminalSession? session = _session;
            if (session is null)
                return;

            session.WriteInput(e.KeyChar.ToString());
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            ResetCursorBlink();

            // Track real typing, not just activation: a layout switch must only
            // reclaim focus for the window the user was actually typing into.
            _lastFocusMs = _clock.Elapsed.TotalMilliseconds;

            // Clipboard shortcuts work even while the shell is not ready (or has
            // already ended), so a copied selection is never swallowed by the
            // session-null guard below.
            if ((e.Control && e.Shift && e.KeyCode == Keys.C) ||
                (e.Control && e.KeyCode == Keys.Insert))
            {
                CopySelection();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            if (e.Control && e.KeyCode == Keys.C && _hasSelection)
            {
                CopySelection();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            if ((e.Control && e.Shift && e.KeyCode == Keys.V) ||
                (e.Shift && e.KeyCode == Keys.Insert))
            {
                PasteClipboard();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            TerminalSession? session = _session;
            if (session is null)
                return;

            byte[] sequence = TerminalInputEncoder.Encode(e.KeyCode, e.Shift, e.Control, e.Alt);
            if (sequence.Length == 0)
                return;

            // Cleared only after a key that really reaches the shell: modifier-only
            // presses (Ctrl, Shift, Alt) must not destroy a selection before the
            // clipboard shortcut that follows them can copy it.
            _hasSelection = false;
            _screen?.ScrollToBottom();

            session.Write(sequence);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private void PasteClipboard()
        {
            TerminalSession? session = _session;
            if (session is null || !Clipboard.ContainsText())
                return;

            session.WriteInput(Clipboard.GetText());
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);

            ApplyLayout();
            ApplyRoundedRegion();
            UpdateConsoleMetrics();
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
            float originX = _consoleRect.Left + pad;
            float originY = _consoleRect.Top + pad;

            GraphicsState state = graphics.Save();
            graphics.SetClip(consolePath);

            if (_sessionError is not null)
            {
                DrawConsoleMessage(graphics, _sessionError, originX, originY);
                graphics.Restore(state);
                return;
            }

            AnsiScreenBuffer? screen = _screen;
            if (screen is null)
            {
                DrawConsoleMessage(graphics, "starting shell...", originX, originY);
                graphics.Restore(state);
                return;
            }

            DrawScreen(graphics, screen, originX, originY);
            DrawSelection(graphics, originX, originY);

            if (_sessionEnded)
                DrawConsoleMessage(graphics, "[process exited]", originX, originY + _cellHeight);

            graphics.Restore(state);
        }

        private void DrawScreen(Graphics graphics, AnsiScreenBuffer screen, float originX, float originY)
        {
            Color defaultForeground = _consoleForeground;
            float bottom = _consoleRect.Bottom - ConsolePaddingDip * _scale;

            using var format = new StringFormat(StringFormat.GenericTypographic)
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Near,
                FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces
            };

            var builder = new StringBuilder(screen.Columns);

            for (int row = 0; row < screen.Rows; row++)
            {
                float y = originY + row * _cellHeight;
                if (y + _cellHeight > bottom + _cellHeight)
                    break;

                int column = 0;
                while (column < screen.Columns)
                {
                    ScreenCell first = screen.GetViewCell(row, column);
                    if (first.IsContinuation)
                    {
                        column++;
                        continue;
                    }

                    int runStart = column;
                    builder.Clear();

                    while (column < screen.Columns)
                    {
                        ScreenCell current = screen.GetViewCell(row, column);
                        if (current.IsContinuation)
                        {
                            column++;
                            continue;
                        }

                        if (builder.Length > 0 && !SameCellStyle(first, current))
                            break;

                        builder.Append(current.Rune.ToString());
                        column++;
                    }

                    string text = builder.ToString();
                    bool reverse = (first.Attributes & AnsiAttributes.Reverse) != 0;

                    Color foreground;
                    Color background;

                    if (reverse)
                    {
                        foreground = ResolveColor(first.Background, _consoleFill);
                        background = ResolveColor(first.Foreground, defaultForeground);
                    }
                    else
                    {
                        foreground = ResolveColor(first.Foreground, defaultForeground);
                        background = ResolveColor(first.Background, Color.Transparent);
                    }

                    var rect = new RectangleF(
                        originX + runStart * _cellWidth,
                        y,
                        MathF.Max(1f, (column - runStart) * _cellWidth),
                        _cellHeight);

                    if (background.A > 0)
                    {
                        // One brush is reused per paint; a full-screen animation
                        // would otherwise allocate thousands of GDI+ objects.
                        _cellBrush.Color = background;
                        graphics.FillRectangle(_cellBrush, rect);
                    }

                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        _textBrush.Color = foreground;
                        graphics.DrawString(text, _monoFont, _textBrush, rect.Location, format);

                        if ((first.Attributes & AnsiAttributes.Underline) != 0)
                        {
                            using var underline = new Pen(foreground, MathF.Max(1f, _scale));
                            graphics.DrawLine(
                                underline,
                                rect.Left,
                                rect.Bottom - 1f,
                                rect.Right,
                                rect.Bottom - 1f);
                        }
                    }
                }
            }

            DrawCursor(graphics, screen, originX, originY);
        }

        private void DrawCursor(Graphics graphics, AnsiScreenBuffer screen, float originX, float originY)
        {
            if (!screen.CursorVisible || !ContainsFocus || screen.ScrollOffset > 0)
                return;

            if (_cursorBlink && !_cursorBlinkOn)
                return;

            var rect = new RectangleF(
                originX + screen.CursorColumn * _cellWidth,
                originY + screen.CursorRow * _cellHeight,
                MathF.Max(1f, _cellWidth),
                MathF.Max(1f, _cellHeight));

            switch (_cursorStyle)
            {
                case TerminalPreferences.CursorLine:
                {
                    float width = MathF.Max(1f, 2f * _scale);
                    using var pen = new Pen(_cursorColor, width);
                    float x = rect.Left + width * 0.5f;
                    graphics.DrawLine(pen, x, rect.Top, x, rect.Bottom);
                    break;
                }

                case TerminalPreferences.CursorUnderline:
                {
                    float height = MathF.Max(1f, 2f * _scale);
                    using var brush = new SolidBrush(_cursorColor);
                    graphics.FillRectangle(brush, rect.Left, rect.Bottom - height, rect.Width, height);
                    break;
                }

                default:
                {
                    using var brush = new SolidBrush(Color.FromArgb(96, _cursorColor));
                    graphics.FillRectangle(brush, rect);
                    break;
                }
            }
        }

        private void DrawSelection(Graphics graphics, float originX, float originY)
        {
            if (!_hasSelection)
                return;

            (Point start, Point end) = NormalizeSelection();
            using var brush = new SolidBrush(Color.FromArgb(72, _accentColor));

            for (int row = start.Y; row <= end.Y; row++)
            {
                int from = row == start.Y ? start.X : 0;
                int to = row == end.Y ? end.X : Math.Max(0, _consoleColumns - 1);

                var rect = new RectangleF(
                    originX + from * _cellWidth,
                    originY + row * _cellHeight,
                    MathF.Max(1f, (to - from + 1) * _cellWidth),
                    _cellHeight);

                graphics.FillRectangle(brush, rect);
            }
        }

        private (Point Start, Point End) NormalizeSelection()
        {
            Point a = _selectionStart;
            Point b = _selectionEnd;

            bool swap = a.Y > b.Y || (a.Y == b.Y && a.X > b.X);
            return swap ? (b, a) : (a, b);
        }

        private bool TryGetConsoleCell(Point point, out int row, out int column)
        {
            row = 0;
            column = 0;

            if (_screen is null || _consoleColumns <= 0 || _consoleRows <= 0)
                return false;

            float pad = ConsolePaddingDip * _scale;
            float left = _consoleRect.Left + pad;
            float top = _consoleRect.Top + pad;
            float right = _consoleRect.Right - pad;
            float bottom = _consoleRect.Bottom - pad;

            if (point.X < left || point.X > right || point.Y < top || point.Y > bottom)
                return false;

            column = Math.Clamp((int)MathF.Floor((point.X - left) / _cellWidth), 0, _consoleColumns - 1);
            row = Math.Clamp((int)MathF.Floor((point.Y - top) / _cellHeight), 0, _consoleRows - 1);
            return true;
        }

        private string GetSelectedText()
        {
            AnsiScreenBuffer? screen = _screen;
            if (!_hasSelection || screen is null)
                return string.Empty;

            (Point start, Point end) = NormalizeSelection();
            var builder = new StringBuilder();

            for (int row = start.Y; row <= end.Y; row++)
            {
                int from = row == start.Y ? start.X : 0;
                int to = row == end.Y ? end.X : Math.Max(0, _consoleColumns - 1);

                var line = new StringBuilder();

                for (int column = from; column <= to; column++)
                {
                    ScreenCell cell = screen.GetViewCell(row, column);
                    if (cell.IsContinuation)
                        continue;

                    line.Append(cell.Rune.ToString());
                }

                builder.Append(line.ToString().TrimEnd());

                if (row < end.Y)
                    builder.Append("\r\n");
            }

            return builder.ToString();
        }

        private void CopySelection()
        {
            string text = GetSelectedText();
            if (text.Length == 0)
                return;

            try
            {
                Clipboard.SetText(text);
            }
            catch (ExternalException)
            {
                // Clipboard is held by another process; nothing sensible to do.
            }
        }

        private void DrawConsoleMessage(Graphics graphics, string message, float x, float y)
        {
            using var brush = new SolidBrush(_mutedColor);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Near,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            };

            var rect = new RectangleF(
                x,
                y,
                MathF.Max(1f, _consoleRect.Right - x - ConsolePaddingDip * _scale),
                _monoFont.GetHeight(graphics));

            graphics.DrawString(message, _monoFont, brush, rect, format);
        }

        private static bool SameCellStyle(ScreenCell left, ScreenCell right)
        {
            return left.Foreground.Equals(right.Foreground)
                   && left.Background.Equals(right.Background)
                   && left.Attributes == right.Attributes;
        }

        private Color ResolveColor(AnsiColor color, Color fallback)
        {
            return color.Kind switch
            {
                AnsiColorKind.Rgb => Color.FromArgb(color.Red, color.Green, color.Blue),
                AnsiColorKind.Indexed => IndexedColor(color.Index),
                _ => fallback
            };
        }

        private Color IndexedColor(byte index)
        {
            if (index < 16)
                return _ansiPalette[index];

            return TerminalColorScheme.StandardColor(index);
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

        private void StartSession()
        {
            if (_session is not null || _sessionError is not null)
                return;

            UpdateConsoleMetrics();

            try
            {
                TerminalShellLaunch launch = TerminalPreferences.ResolveShell(_shellName);
                var screen = new AnsiScreenBuffer(_consoleColumns, _consoleRows);
                var session = new TerminalSession(new TerminalSessionOptions
                {
                    Columns = _consoleColumns,
                    Rows = _consoleRows,
                    ShellPath = launch.Path,
                    ShellArguments = launch.Arguments,
                    UseMsysEnvironment = launch.UseMsysEnvironment,
                    EnvironmentOverrides = TerminalPreferences.BuildPromptEnvironment(
                        _shellName,
                        TerminalPreferences.Current.Prompt)
                });

                screen.ResponseRequested += response => session.WriteInput(response);
                session.OutputReceived += OnSessionOutput;
                session.Exited += OnSessionExited;
                session.Faulted += OnSessionFaulted;

                _screen = screen;
                _session = session;
                session.Start();
            }
            catch (Exception error)
            {
                _session?.Dispose();
                _session = null;
                _screen = null;
                _sessionError = error.Message;
            }

            Invalidate();
        }

        private void UpdateConsoleMetrics()
        {
            if (!IsHandleCreated || _consoleRect.Width <= 1f || _consoleRect.Height <= 1f)
                return;

            float pad = ConsolePaddingDip * _scale;
            float width = MathF.Max(1f, _consoleRect.Width - pad * 2f);
            float height = MathF.Max(1f, _consoleRect.Height - pad * 2f);

            float cellWidth;
            float cellHeight;

            using (Graphics graphics = CreateGraphics())
            {
                // GDI+ metrics, not GDI TextRenderer: DrawScreen draws with
                // GenericTypographic, and a GDI/GDI+ mismatch skews the cell grid.
                SizeF cell = graphics.MeasureString(
                    "M",
                    _monoFont,
                    int.MaxValue,
                    StringFormat.GenericTypographic);

                cellWidth = MathF.Max(1f, MathF.Ceiling(cell.Width));
                cellHeight = MathF.Max(1f, _monoFont.GetHeight(graphics));
            }

            _cellWidth = cellWidth;
            _cellHeight = cellHeight;

            int columns = Math.Max(1, (int)MathF.Floor(width / cellWidth));
            int rows = Math.Max(1, (int)MathF.Floor(height / cellHeight));

            if (_consoleColumns == columns && _consoleRows == rows)
                return;

            _consoleColumns = columns;
            _consoleRows = rows;

            _hasSelection = false;
            _selecting = false;

            _screen?.Resize(columns, rows);
            _session?.Resize(columns, rows);
        }

        private void OnSessionOutput(object? sender, TerminalOutputEventArgs e)
        {
            if (e.Text.Length == 0 || !ReferenceEquals(sender, _session))
                return;

            bool startPump;

            lock (_pendingLock)
            {
                _pendingOutput.Append(e.Text);
                startPump = !_pumpRunning;
                _pumpRunning = true;
            }

            if (startPump)
                PostToUi(StartOutputPump);
        }

        private void StartOutputPump()
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            if (!_pumpTimer.Enabled)
                _pumpTimer.Start();
        }

        private void OnOutputPumpTick(object? sender, EventArgs e)
        {
            string text;

            lock (_pendingLock)
            {
                if (_pendingOutput.Length == 0)
                {
                    // Cleared under the same lock the reader appends under, so a
                    // chunk that arrives now sees _pumpRunning == false and
                    // restarts the pump instead of being stranded.
                    _pumpRunning = false;
                    _pumpTimer.Stop();
                    return;
                }

                text = _pendingOutput.ToString();
                _pendingOutput.Clear();
            }

            FeedConsole(text);
        }

        private void FeedConsole(string text)
        {
            AnsiScreenBuffer? screen = _screen;
            if (screen is null)
                return;

            // Auto-scroll follows new output only while the view is already at the
            // newest line; a user who scrolled up stays where they are.
            bool follow = _autoScroll && screen.ScrollOffset == 0;

            screen.Feed(text);

            if (follow)
                screen.ScrollToBottom();

            InvalidateConsole();
        }

        private void InvalidateConsole()
        {
            if (IsDisposed || WindowState == FormWindowState.Minimized)
                return;

            if (_consoleRect.Width <= 1f || _consoleRect.Height <= 1f)
            {
                Invalidate();
                return;
            }

            Invalidate(Rectangle.Round(_consoleRect));
        }

        private void InvalidateCursor()
        {
            if (IsDisposed)
                return;

            AnsiScreenBuffer? screen = _screen;

            if (WindowState == FormWindowState.Minimized
                || screen is null
                || _consoleRect.Width <= 1f
                || _cellWidth <= 0f
                || _cellHeight <= 0f)
            {
                Invalidate();
                return;
            }

            float pad = ConsolePaddingDip * _scale;
            float left = _consoleRect.Left + pad + screen.CursorColumn * _cellWidth;
            float top = _consoleRect.Top + pad + screen.CursorRow * _cellHeight;

            var rect = new Rectangle(
                (int)MathF.Floor(left) - 1,
                (int)MathF.Floor(top) - 1,
                (int)MathF.Ceiling(_cellWidth) + 2,
                (int)MathF.Ceiling(_cellHeight) + 2);

            Invalidate(rect);
        }

        private void OnSessionExited(object? sender, EventArgs e)
        {
            if (!ReferenceEquals(sender, _session))
                return;

            PostToUi(() =>
            {
                if (!ReferenceEquals(sender, _session))
                    return;

                _sessionEnded = true;
                Invalidate();
            });
        }

        private void OnSessionFaulted(object? sender, TerminalErrorEventArgs e)
        {
            if (!ReferenceEquals(sender, _session))
                return;

            PostToUi(() =>
            {
                if (!ReferenceEquals(sender, _session))
                    return;

                _sessionError = e.Error.Message;
                Invalidate();
            });
        }

        private void PostToUi(Action action)
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            try
            {
                BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
                // Handle destroyed between the check and the post.
            }
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                NotificationService.LayoutSwitched -= OnLayoutSwitched;

                _timer.Stop();
                _timer.Dispose();

                _blinkTimer.Stop();
                _blinkTimer.Dispose();

                _pumpTimer.Stop();
                _pumpTimer.Dispose();

                lock (_pendingLock)
                {
                    _pendingOutput.Clear();
                }

                _session?.Dispose();
                _session = null;

                _titleFont.Dispose();
                _monoFont.Dispose();
                _textBrush.Dispose();
                _cellBrush.Dispose();
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

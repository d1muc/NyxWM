using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using WinFormsTimer = System.Windows.Forms.Timer;

namespace nyxWM
{
    /// <summary>
    /// Shows and hides the centred quick-action panel (sliders, search, terminal).
    /// Owns the animation ticker and disposes every button window.
    /// All members are expected to be used from the UI thread.
    /// </summary>
    internal static class LauncherService
    {
        private const float BaseButtonSize = 116f;
        private const float ButtonGapDip = 34f;
        private const float EntryStaggerMs = 55f;
        private const float ExitStaggerMs = 45f;
        private const float EdgeMarginDip = 24f;

        private static readonly LauncherIcon[] Icons =
        {
            LauncherIcon.Sliders,
            LauncherIcon.Search,
            LauncherIcon.Terminal
        };

        private static readonly List<LauncherButton> Buttons = new();

        private static WinFormsTimer? _timer;
        private static Stopwatch? _clock;
        private static double _lastTickMs;
        private static Func<bool>? _isGameModeActive;
        private static bool _initialized;
        private static bool _open;
        private static bool _closing;

        /// <summary>
        /// Starts the launcher ticker. <paramref name="isGameModeActive"/> is polled
        /// on every tick; while it returns true, the panel is closed.
        /// </summary>
        internal static void Initialize(Func<bool> isGameModeActive)
        {
            if (_initialized)
                return;

            _isGameModeActive = isGameModeActive ?? throw new ArgumentNullException(nameof(isGameModeActive));
            _clock = Stopwatch.StartNew();
            _lastTickMs = 0.0;

            _timer = new WinFormsTimer { Interval = 15 };
            _timer.Tick += OnTick;

            _initialized = true;
        }

        /// <summary>
        /// Opens the panel, or hides it with the closing animation when it is
        /// already visible.
        /// </summary>
        internal static void Toggle()
        {
            if (!_initialized || SettingsWindowService.IsOpen)
                return;

            if (!_open)
            {
                Open();
            }
            else if (!_closing)
            {
                CloseAll();
            }
        }

        private static void Open()
        {
            if (_open || Buttons.Count > 0 || IsGameModeActive())
                return;

            float scale = GetDpiScale();
            NotificationTheme theme = NotificationThemeResolver.Resolve(ConfigManager.GetLiveTheme());
            Rectangle work = Screen.PrimaryScreen?.WorkingArea
                             ?? new Rectangle(0, 0, 1920, 1080);

            float buttonSize = BaseButtonSize * scale;
            float gap = ButtonGapDip * scale;
            float totalWidth = Icons.Length * buttonSize + (Icons.Length - 1) * gap;
            float margin = EdgeMarginDip * scale;

            if (totalWidth > work.Width - margin * 2f)
            {
                // Keep the row on screen on narrow displays.
                scale *= (work.Width - margin * 2f) / totalWidth;
                buttonSize = BaseButtonSize * scale;
                gap = ButtonGapDip * scale;
                totalWidth = Icons.Length * buttonSize + (Icons.Length - 1) * gap;
            }

            float left = work.Left + (work.Width - totalWidth) / 2f;
            float centerY = work.Top + work.Height / 2f;

            _clock ??= Stopwatch.StartNew();
            _clock.Restart();
            _lastTickMs = 0.0;

            for (int i = 0; i < Icons.Length; i++)
            {
                float centerX = left + buttonSize / 2f + i * (buttonSize + gap);
                LauncherIcon icon = Icons[i];
                Action onActivated = icon switch
                {
                    LauncherIcon.Sliders => OpenSettings,
                    LauncherIcon.Search => OpenRofiMenu,
                    _ => CloseAll
                };

                var button = new LauncherButton(
                    icon,
                    scale,
                    i * EntryStaggerMs,
                    theme,
                    onActivated);

                button.SetCenter(centerX, centerY);
                button.Advance(0f);
                Buttons.Add(button);
                button.Show();
            }

            _open = true;
            _closing = false;
            _timer?.Start();
        }

        private static void CloseAll()
        {
            if (!_open || _closing || Buttons.Count == 0)
                return;

            _closing = true;

            for (int i = 0; i < Buttons.Count; i++)
            {
                float delay = (Buttons.Count - 1 - i) * ExitStaggerMs;
                Buttons[i].BeginClose(delay);
            }
        }

        /// <summary>
        /// Closes the panel immediately. Used when another overlay (the Rofi
        /// menu) takes over and must not capture fading launcher buttons.
        /// </summary>
        internal static void Hide()
        {
            CloseImmediately();
        }

        /// <summary>
        /// Closes the panel and opens the Rofi application menu.
        /// </summary>
        private static void OpenRofiMenu()
        {
            CloseImmediately();
            RofiMenuService.Show();
        }

        /// <summary>
        /// Closes the panel and opens the settings window.
        /// </summary>
        private static void OpenSettings()
        {
            CloseAll();
            SettingsWindowService.Show();
        }

        private static void CloseImmediately()
        {
            foreach (LauncherButton button in Buttons)
            {
                button.Close();
                button.Dispose();
            }

            Buttons.Clear();
            _open = false;
            _closing = false;
            _timer?.Stop();
        }

        private static void OnTick(object? sender, EventArgs e)
        {
            if (_clock == null)
                return;

            double now = _clock.Elapsed.TotalMilliseconds;
            float dtMs = (float)Math.Clamp(now - _lastTickMs, 0.0, 50.0);
            _lastTickMs = now;

            if (dtMs <= 0f)
                return;

            if (IsGameModeActive())
            {
                CloseImmediately();
                return;
            }

            for (int i = Buttons.Count - 1; i >= 0; i--)
            {
                LauncherButton button = Buttons[i];
                button.Advance(dtMs);

                if (button.Finished)
                {
                    Buttons.RemoveAt(i);
                    button.Close();
                    button.Dispose();
                }
            }

            if (Buttons.Count == 0)
            {
                _open = false;
                _closing = false;
                _timer?.Stop();
            }
        }

        /// <summary>
        /// Stops the ticker and disposes every button. Safe to call multiple times.
        /// </summary>
        internal static void Shutdown()
        {
            if (!_initialized)
                return;

            _timer?.Stop();
            if (_timer != null)
            {
                _timer.Tick -= OnTick;
                _timer.Dispose();
            }

            _timer = null;
            CloseImmediately();
            _initialized = false;
            _clock = null;
            _isGameModeActive = null;
        }

        private static bool IsGameModeActive()
        {
            try
            {
                return _isGameModeActive?.Invoke() ?? false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"LauncherService: game mode probe failed: {ex.Message}");
                return false;
            }
        }

        private static float GetDpiScale()
        {
            try
            {
                using Graphics graphics = Graphics.FromHwnd(IntPtr.Zero);
                return Math.Clamp(graphics.DpiX / 96f, 1f, 3f);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"LauncherService: DPI probe failed: {ex.Message}");
                return 1f;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace nyxWM
{
    /// <summary>
    /// Shows and hides the centred quick-action panel (sliders, search, terminal).
    /// Buttons advance on the shared frame clock; this service owns their lifetime.
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

        private static Action? _unsubscribe;
        private static int _tickInFlight;
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
            NotificationTheme theme = NotificationThemeResolver.Resolve(
                ConfigManager.GetLiveTheme(),
                ConfigManager.Current.ThemeMode);
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

            for (int i = 0; i < Icons.Length; i++)
            {
                float centerX = left + buttonSize / 2f + i * (buttonSize + gap);
                LauncherIcon icon = Icons[i];
                Action onActivated = icon switch
                {
                    LauncherIcon.Sliders => OpenSettings,
                    LauncherIcon.Search => OpenRofiMenu,
                    LauncherIcon.Terminal => OpenTerminalWindow,
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

            // One shared clock advances every button and posts a single repaint
            // pass per frame on the UI thread.
            _unsubscribe = UiAnimationHost.Subscribe(
                dtMs => AdvanceButtons(dtMs),
                () => RepaintButtons());
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
        /// Closes the panel and opens a new terminal window on the canvas.
        /// </summary>
        private static void OpenTerminalWindow()
        {
            CloseImmediately();
            TerminalWindowService.OpenNew();
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
            Unsubscribe();
        }

        /// <summary>
        /// Advances every button on the frame clock. Runs on the ticker thread;
        /// onboarding and removal are marshalled back to the UI thread.
        /// </summary>
        private static void AdvanceButtons(double dtMs)
        {
            if (Interlocked.Exchange(ref _tickInFlight, 1) == 1)
                return;

            try
            {
                LauncherButton[] snapshot;

                lock (Buttons)
                {
                    snapshot = Buttons.ToArray();
                }

                foreach (LauncherButton button in snapshot)
                    button.Advance((float)dtMs);
            }
            finally
            {
                Interlocked.Exchange(ref _tickInFlight, 0);
            }
        }

        /// <summary>
        /// Schedules this frame's repaint and window moves on the UI thread.
        /// Buttons advance off-thread, so every window change is posted back:
        /// moving a window from the ticker thread would post one marshalled
        /// call per button per frame.
        /// </summary>
        private static void RepaintButtons()
        {
            if (Buttons.Count == 0)
                return;

            LauncherButton[] snapshot;

            lock (Buttons)
            {
                snapshot = Buttons.ToArray();
            }

            UiAnimationHost.Post(() => ApplyButtons(snapshot));
        }

        private static void ApplyButtons(LauncherButton[] snapshot)
        {
            for (int i = 0; i < snapshot.Length; i++)
            {
                LauncherButton button = snapshot[i];

                if (button.IsDisposed)
                    continue;

                button.ApplyFrame();
                InvalidateUi(button);

                // Finished buttons are destroyed here, on the UI thread.
                if (button.Finished)
                    RemoveButton(button);
            }

            if (Buttons.Count == 0)
            {
                _open = false;
                _closing = false;
                Unsubscribe();
            }
        }

        private static void RemoveButton(LauncherButton button)
        {
            lock (Buttons)
            {
                if (!Buttons.Remove(button))
                    return;
            }

            button.Close();
            button.Dispose();
        }

        private static void InvalidateUi(LauncherButton button)
        {
            try
            {
                button.Invalidate();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"LauncherService: invalidate failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Stops the ticker and disposes every button. Safe to call multiple times.
        /// </summary>
        internal static void Shutdown()
        {
            if (!_initialized)
                return;

            CloseImmediately();
            _initialized = false;
            _isGameModeActive = null;
        }

        /// <summary>Detaches the panel from the shared frame clock.</summary>
        private static void Unsubscribe()
        {
            Action? unsubscribe = Interlocked.Exchange(ref _unsubscribe, null);
            _unsubscribe = null;

            try
            {
                unsubscribe?.Invoke();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"LauncherService: unsubscribe failed: {ex.Message}");
            }
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
                return Math.Clamp(graphics.DpiX / 96f, 1f, 3f) * VisualPreferences.UiScale;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"LauncherService: DPI probe failed: {ex.Message}");
                return VisualPreferences.UiScale;
            }
        }
    }
}

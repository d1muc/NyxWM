using System;
using System.Drawing;

namespace nyxWM
{
    /// <summary>
    /// Creates the single settings window instance and feeds it the live theme.
    /// </summary>
    internal static class SettingsWindowService
    {
        private static SettingsWindow? _window;
        private static Action<bool>? _setCameraSuspended;
        private static Func<bool>? _isGameModeActive;

        internal static bool IsOpen => _window != null;

        /// <summary>
        /// Supplies the camera suspension switch (true while settings are open,
        /// so dragging cannot pan the desktop behind the window).
        /// </summary>
        internal static void Initialize(Action<bool> setCameraSuspended, Func<bool> isGameModeActive)
        {
            _setCameraSuspended = setCameraSuspended ?? throw new ArgumentNullException(nameof(setCameraSuspended));
            _isGameModeActive = isGameModeActive ?? throw new ArgumentNullException(nameof(isGameModeActive));
        }

        /// <summary>
        /// Opens the settings window in the centre of the primary screen, or
        /// brings the existing window to the front when one is already open.
        /// Does nothing when game mode is active.
        /// </summary>
        internal static void Show()
        {
            if (_window != null)
            {
                _window.Activate();
                return;
            }

            if (IsGameModeActive())
                return;

            try
            {
                NotificationTheme theme = NotificationThemeResolver.Resolve(
                    ConfigManager.GetLiveTheme(),
                    ConfigManager.Current.ThemeMode);
                var window = new SettingsWindow(
                    theme,
                    GetDpiScale(),
                    settings =>
                    {
                        ConfigManager.Save(settings);
                        NotificationService.Show("Settings", "Config saved and reloaded");
                    },
                    () =>
                    {
                        ConfigManager.Save(ConfigManager.CreateDefaultSettings());
                        NotificationService.Show("Settings", "All settings reset to defaults");
                    });

                _window = window;
                window.FormClosed += (_, _) =>
                {
                    if (ReferenceEquals(_window, window))
                        _window = null;

                    _setCameraSuspended?.Invoke(false);
                };

                _setCameraSuspended?.Invoke(true);
                window.Show();
            }
            catch (Exception ex)
            {
                _setCameraSuspended?.Invoke(false);
                Console.WriteLine($"SettingsWindowService: failed to open settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Closes the settings window if it is open. Unlike Shutdown this does
        /// not touch the camera suspension, so it is safe to call during game mode.
        /// Safe to call multiple times.
        /// </summary>
        internal static void Close()
        {
            SettingsWindow? window = _window;
            _window = null;

            if (window == null || window.IsDisposed)
                return;

            try
            {
                window.Close();
                window.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SettingsWindowService: close failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Closes the settings window and resumes camera panning. Safe to call multiple times.
        /// </summary>
        internal static void Shutdown()
        {
            SettingsWindow? window = _window;
            _window = null;

            _setCameraSuspended?.Invoke(false);

            if (window == null || window.IsDisposed)
                return;

            window.Close();
            window.Dispose();
        }

        private static bool IsGameModeActive()
        {
            try
            {
                return _isGameModeActive?.Invoke() ?? false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SettingsWindowService: game mode probe failed: {ex.Message}");
                return false;
            }
        }

        private static float GetDpiScale()
        {
            try
            {
                using Graphics graphics = Graphics.FromHwnd(IntPtr.Zero);
                return Math.Clamp(graphics.DpiX / 96f, 0.75f, 3f) * VisualPreferences.UiScale;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SettingsWindowService: DPI probe failed: {ex.Message}");
                return VisualPreferences.UiScale;
            }
        }
    }
}
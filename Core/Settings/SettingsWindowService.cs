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

        internal static bool IsOpen => _window != null;

        /// <summary>
        /// Opens the settings window in the centre of the primary screen, or
        /// brings the existing window to the front when one is already open.
        /// </summary>
        internal static void Show()
        {
            if (_window != null)
            {
                _window.Activate();
                return;
            }

            try
            {
                NotificationTheme theme = NotificationThemeResolver.Resolve(ConfigManager.GetLiveTheme());
                var window = new SettingsWindow(
                    theme,
                    GetDpiScale(),
                    () => NotificationService.Show("Settings", "Config saved and reloaded"));

                _window = window;
                window.FormClosed += (_, _) =>
                {
                    if (ReferenceEquals(_window, window))
                        _window = null;
                };

                window.Show();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SettingsWindowService: failed to open settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Closes the settings window if it is open. Safe to call multiple times.
        /// </summary>
        internal static void Shutdown()
        {
            SettingsWindow? window = _window;
            _window = null;

            if (window == null || window.IsDisposed)
                return;

            window.Close();
            window.Dispose();
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
                Console.WriteLine($"SettingsWindowService: DPI probe failed: {ex.Message}");
                return 1f;
            }
        }
    }
}

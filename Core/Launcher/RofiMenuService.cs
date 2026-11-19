using System;
using System.Drawing;
using System.Windows.Forms;

namespace nyxWM
{
    /// <summary>
    /// Owns the single Rofi menu window. All members are used from the UI thread.
    /// While the menu is open the camera is suspended, so dragging cannot pan
    /// the desktop behind it.
    /// </summary>
    internal static class RofiMenuService
    {
        private static RofiMenuWindow? _window;
        private static Action<bool>? _setCameraSuspended;
        private static Func<bool>? _isGameModeActive;
        private static Func<bool>? _isCameraMoving;

        internal static bool IsOpen => _window != null;

        /// <summary>
        /// Supplies the camera suspension switch. Safe to call once at startup.
        /// </summary>
        internal static void Initialize(Action<bool> setCameraSuspended, Func<bool> isGameModeActive, Func<bool> isCameraMoving)
        {
            _setCameraSuspended = setCameraSuspended ?? throw new ArgumentNullException(nameof(setCameraSuspended));
            _isGameModeActive = isGameModeActive ?? throw new ArgumentNullException(nameof(isGameModeActive));
            _isCameraMoving = isCameraMoving ?? throw new ArgumentNullException(nameof(isCameraMoving));

            // Scan the Start Menu now, so the first hotkey press opens instantly.
            RofiAppCatalog.Prefetch();
        }

        /// <summary>
        /// Opens the menu, or closes it when it is already visible.
        /// </summary>
        internal static void Toggle()
        {
            if (_window != null)
            {
                _window.RequestClose();
                return;
            }

            Show();
        }

        /// <summary>
        /// Opens the menu in the centre of the primary screen. Does nothing while
        /// the settings window is open or game mode is active.
        /// </summary>
        internal static void Show()
        {
            if (_window != null)
            {
                _window.Activate();
                return;
            }

            if (SettingsWindowService.IsOpen)
                return;

            if (IsCameraMoving())
                return;

            if (IsGameModeActive())
                return;

            try
            {
                // Freeze camera BEFORE constructing the window so the backdrop
                // capture captures a stable desktop, not a mid-pan frame.
                _setCameraSuspended?.Invoke(true);

                NotificationTheme theme = NotificationThemeResolver.Resolve(
                    ConfigManager.GetLiveTheme(),
                    ConfigManager.Current.ThemeMode);
                var window = new RofiMenuWindow(theme, GetDpiScale());

                _window = window;
                window.FormClosed += (_, _) =>
                {
                    if (ReferenceEquals(_window, window))
                        _window = null;

                    _setCameraSuspended?.Invoke(false);
                };

                window.Show();
            }
            catch (Exception ex)
            {
                _window = null;
                _setCameraSuspended?.Invoke(false);
                Console.WriteLine($"RofiMenuService: failed to open menu: {ex.Message}");
            }
        }

        /// <summary>
        /// Starts the closing animation when the menu is open.
        /// </summary>
        internal static void Close()
        {
            _window?.RequestClose();
        }

        /// <summary>
        /// Closes the menu immediately. Safe to call multiple times.
        /// </summary>
        internal static void Shutdown()
        {
            RofiMenuWindow? window = _window;
            _window = null;

            _setCameraSuspended?.Invoke(false);

            if (window == null || window.IsDisposed)
                return;

            window.Close();
            window.Dispose();
        }

        private static bool IsCameraMoving()
        {
            try
            {
                return _isCameraMoving?.Invoke() ?? false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"RofiMenuService: camera probe failed: {ex.Message}");
                return false;
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
                Console.WriteLine($"RofiMenuService: game mode probe failed: {ex.Message}");
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
                Console.WriteLine($"RofiMenuService: DPI probe failed: {ex.Message}");
                return VisualPreferences.UiScale;
            }
        }
    }
}
using System;
using System.Collections.Generic;
using System.Drawing;

namespace nyxWM
{
    /// <summary>
    /// Opens borderless terminal windows on the canvas. Unlike the settings and
    /// Rofi overlays every call creates another instance, so the panel tile and
    /// the hotkey can spawn as many windows as the user wants.
    /// </summary>
    internal static class TerminalWindowService
    {
        private const int CascadeCount = 8;

        private static readonly List<TerminalWindow> Windows = new();

        private static Action? _invalidateWindowCache;
        private static Action<IntPtr>? _requestCenter;
        private static int _cascadeIndex;

        /// <summary>
        /// Supplies the hooks that make terminal windows first-class canvas
        /// windows: the cache invalidation makes the camera pan them, and the
        /// centring request reproduces the auto-center-on-focus behaviour for
        /// windows whose focus events are skipped as own-process events.
        /// </summary>
        internal static void Initialize(
            Action invalidateWindowCache,
            Action<IntPtr> requestCenter)
        {
            _invalidateWindowCache = invalidateWindowCache
                ?? throw new ArgumentNullException(nameof(invalidateWindowCache));
            _requestCenter = requestCenter
                ?? throw new ArgumentNullException(nameof(requestCenter));
        }

        /// <summary>
        /// Opens a new terminal window, cascaded from the previous one.
        /// </summary>
        internal static void OpenNew()
        {
            try
            {
                NotificationTheme theme = NotificationThemeResolver.Resolve(
                    ConfigManager.GetLiveTheme(),
                    ConfigManager.Current.ThemeMode);

                var window = new TerminalWindow(theme, GetDpiScale(), _cascadeIndex, _requestCenter);
                _cascadeIndex = (_cascadeIndex + 1) % CascadeCount;

                Windows.Add(window);
                window.FormClosed += (_, _) =>
                {
                    Windows.Remove(window);
                    _invalidateWindowCache?.Invoke();
                };

                window.SizeChanged += (_, _) => _invalidateWindowCache?.Invoke();

                window.Show();
                _invalidateWindowCache?.Invoke();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"TerminalWindowService: failed to open a window: {ex.Message}");
            }
        }

        /// <summary>
        /// Re-resolves the palette and hands it to every open window. The window
        /// cross-fades from its old palette, so a theme change in the settings
        /// (or a saved config) animates instead of snapping.
        /// </summary>
        internal static void ApplyThemeToAll()
        {
            if (Windows.Count == 0)
                return;

            try
            {
                NotificationTheme theme = NotificationThemeResolver.Resolve(
                    ConfigManager.GetLiveTheme(),
                    ConfigManager.Current.ThemeMode);

                bool animate = VisualPreferences.SmoothAnimations;

                foreach (TerminalWindow window in Windows.ToArray())
                {
                    if (window.IsDisposed)
                        continue;

                    window.ApplyTheme(theme, animate);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"TerminalWindowService: failed to apply theme: {ex.Message}");
            }
        }

        /// <summary>
        /// Closes every terminal window. Safe to call multiple times.
        /// </summary>
        internal static void CloseAll()
        {
            TerminalWindow[] snapshot = Windows.ToArray();
            Windows.Clear();

            foreach (TerminalWindow window in snapshot)
            {
                if (window.IsDisposed)
                    continue;

                window.Close();
                window.Dispose();
            }

            _invalidateWindowCache?.Invoke();
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
                Console.WriteLine($"TerminalWindowService: DPI probe failed: {ex.Message}");
                return VisualPreferences.UiScale;
            }
        }
    }
}

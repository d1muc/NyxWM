using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using nyxWM;
using System.Windows.Forms; // Added for Screen
using System.Threading; // Added for SynchronizationContext
using System.Diagnostics; // Added for Stopwatch

namespace nyxWM
{
    public class WindowManager
    {
        // Flags for SetWindowPos (same as in WinApi.cs but we define here for simplicity)
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FLAGS = SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE;

        // Cached initial positions of windows when dragging starts
        private readonly Dictionary<IntPtr, WinApi.RECT> initialWindowPositions = new();

        // Mouse start position (at drag start)
        private int startMouseX;
        private int startMouseY;

        // Lerp fields
        private float currentDx = 0, currentDy = 0;   // current window offset
        private float targetDx = 0, targetDy = 0;     // target offset from mouse movement
        private bool isDragging = false;

        // Frame timing
        private long _lastUpdateTicks = 0;
        private readonly Stopwatch _stopwatch = new Stopwatch();

        // Monitor refresh rate
        private int _monitorRefreshRate = 165; // Default fallback
        private float _targetDeltaTime => 1000f / _monitorRefreshRate; // Target time per frame in ms

        // Lerp tuning - how quickly we catch up to target (per second)
        private readonly float _lerpRatePerSecond;

        // Auto-center on focus flag
        public bool AutoCenterOnFocus { get; set; }

        /// <summary>
        /// Toggles the auto-center-on-focus feature.
        /// </summary>
        public void ToggleAutoCenter()
        {
            AutoCenterOnFocus = !AutoCenterOnFocus;
            Console.WriteLine($"AutoCenterOnFocus is now: {AutoCenterOnFocus}");
        }

        // WinEvent hook fields
        private WinApi.WinEventProc _winEventProcDelegate;
        private IntPtr _winEventHookHandle;
        private SynchronizationContext? _syncContext;

        /// <summary>
        /// Constructor: sets up the WinEvent hook for foreground window changes.
        /// </>
        public WindowManager()
        {
            // Ensure a synchronization context exists for the current thread
            if (SynchronizationContext.Current == null)
            {
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            }
            // Capture the synchronization context from the UI thread
            _syncContext = SynchronizationContext.Current;

            // Get monitor refresh rate with fallback from config
            _monitorRefreshRate = MonitorUtils.GetPrimaryMonitorRefreshRate(ConfigManager.Current.FallbackRefreshRate);
            Console.WriteLine($"Detected monitor refresh rate: {_monitorRefreshRate} Hz");

            // Get lerp rate from config
            _lerpRatePerSecond = ConfigManager.Current.LerpRatePerSecond;

            // Set auto-center-on-focus default from config
            AutoCenterOnFocus = ConfigManager.Current.AutoCenterOnFocusDefault;

            // Initialize stopwatch for frame timing
            _stopwatch.Start();
            _lastUpdateTicks = _stopwatch.ElapsedTicks;

            // Set up the WinEvent delegate
            _winEventProcDelegate = new WinApi.WinEventProc(WinEventProcCallback);

            // Install the hook for EVENT_SYSTEM_FOREGROUND
            _winEventHookHandle = WinApi.SetWinEventHook(
                WinApi.EVENT_SYSTEM_FOREGROUND,
                WinApi.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero,
                _winEventProcDelegate,
                0,
                0,
                WinApi.WINEVENT_OUTOFCONTEXT | WinApi.WINEVENT_SKIPOWNPROCESS);
        }

        /// <summary>
        /// Destructor: unhooks the WinEvent hook.
        /// </summary>
        ~WindowManager()
        {
            if (_winEventHookHandle != IntPtr.Zero)
            {
                WinApi.UnhookWinEvent(_winEventHookHandle);
                _winEventHookHandle = IntPtr.Zero;
            }
        }

        /// <summary>
        /// Callback for WinEvent hook. Marshals to the UI thread.
        /// </summary>
        private void WinEventProcCallback(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            // Debug output
            Console.WriteLine($"WinEventProcCallback: hwnd={hwnd.ToInt64():X} eventType={eventType}");

            // Marshal to the UI thread
            if (_syncContext != null)
            {
                _syncContext.Post(state => HandleFocusChange(hwnd), null);
            }
        }

        /// <summary>
        /// Handles the foreground window change event on the UI thread.
        /// </summary>
        /// <param name="hwnd">The handle of the window that gained focus.</param>
        private void HandleFocusChange(IntPtr hwnd)
        {
            // Debug output
            int debugTitleLen = WinApi.GetWindowTextLength(hwnd);
            if (debugTitleLen > 0)
            {
                var titleSb = new System.Text.StringBuilder(debugTitleLen + 1);
                WinApi.GetWindowText(hwnd, titleSb, titleSb.Capacity);
                Console.WriteLine($"HandleFocusChange: hwnd={hwnd.ToInt64():X} title=\"{titleSb}\"");
            }
            else
            {
                Console.WriteLine($"HandleFocusChange: hwnd={hwnd.ToInt64():X} (no title)");
            }

            // If auto-center is disabled or we are currently dragging, do nothing.
            if (!AutoCenterOnFocus || isDragging)
                return;

            // Check window validity: visible, not minimized, has a title, and not a system window.
            if (!WinApi.IsWindowVisible(hwnd))
                return;

            if (WinApi.IsIconic(hwnd))
                return;

            int titleLen = WinApi.GetWindowTextLength(hwnd);
            if (titleLen == 0)
                return;

            var classNameBuilder = new System.Text.StringBuilder(256);
            WinApi.GetClassName(hwnd, classNameBuilder, classNameBuilder.Capacity);
            string className = classNameBuilder.ToString();

            // Skip ignored windows
            if (IsIgnoredWindow(className))
            {
                return;
            }

            // Update initialWindowPositions with current positions of all visible windows.
            initialWindowPositions.Clear();
            WinApi.EnumWindows((hWnd, lParam) =>
            {
                // Skip invisible windows
                if (!WinApi.IsWindowVisible(hWnd))
                    return true;

                // Skip minimized windows
                if (WinApi.IsIconic(hWnd))
                    return true;

                // Skip windows with empty title
                int winTitleLen = WinApi.GetWindowTextLength(hWnd);
                if (winTitleLen == 0)
                    return true;

                // Get window class name to filter out system UI
                var winClassNameBuilder = new System.Text.StringBuilder(256);
                WinApi.GetClassName(hWnd, winClassNameBuilder, winClassNameBuilder.Capacity);
                string winClassName = winClassNameBuilder.ToString();

                // Skip ignored windows
                if (IsIgnoredWindow(winClassName))
                {
                    return true;
                }

                // Get window rectangle
                if (WinApi.GetWindowRect(hWnd, out WinApi.RECT rect))
                {
                    // Skip windows with invalid coordinates (far off-screen)
                    if (rect.Left < -10000 || rect.Top < -10000)
                        return true;

                    // Store current position
                    initialWindowPositions[hWnd] = rect;
                }

                return true; // continue enumeration
            }, IntPtr.Zero);

            // Get the rectangle of the focused window
            if (!WinApi.GetWindowRect(hwnd, out WinApi.RECT rect))
                return;

            // Calculate the center of the focused window
            int winCenterX = (rect.Left + rect.Right) / 2;
            int winCenterY = (rect.Top + rect.Bottom) / 2;

            // Get the center of the primary screen
            Rectangle primaryScreenBounds = Screen.PrimaryScreen!.Bounds;
            int screenCenterX = primaryScreenBounds.X + primaryScreenBounds.Width / 2;
            int screenCenterY = primaryScreenBounds.Y + primaryScreenBounds.Height / 2;

            // Calculate delta to center the window on the screen
            int deltaX = screenCenterX - winCenterX;
            int deltaY = screenCenterY - winCenterY;

            // Reset current offsets and set target to move to center
            currentDx = 0;
            currentDy = 0;
            targetDx = deltaX;
            targetDy = deltaY;
        }

        /// <summary>
        /// Checks if a window class name is in the ignored list.
        /// </summary>
        private bool IsIgnoredWindow(string className)
        {
            foreach (var ignored in ConfigManager.Current.IgnoredWindowClassNames)
            {
                if (string.Equals(className, ignored, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Called when drag starts: records mouse position, resets Lerp variables, and caches initial window positions.
        /// </summary>
        public void StartDragging()
        {
            // Get current cursor position
            WinApi.GetCursorPos(out WinApi.POINT pt);
            startMouseX = pt.X;
            startMouseY = pt.Y;
            isDragging = true;

            // Reset Lerp variables
            currentDx = 0;
            currentDy = 0;
            targetDx = 0;
            targetDy = 0;

            // Clear previous cache
            initialWindowPositions.Clear();

            // Enumerate all top-level windows
            WinApi.EnumWindows((hWnd, lParam) =>
            {
                // Skip invisible windows
                if (!WinApi.IsWindowVisible(hWnd))
                    return true;

                // Skip windows with empty title
                int titleLen = WinApi.GetWindowTextLength(hWnd);
                if (titleLen == 0)
                    return true;

                // Get window class name to filter out system UI
                var classNameBuilder = new StringBuilder(256);
                WinApi.GetClassName(hWnd, classNameBuilder, classNameBuilder.Capacity);
                string className = classNameBuilder.ToString();

                // Skip ignored windows
                if (IsIgnoredWindow(className))
                {
                    return true;
                }

                // Get window rectangle
                if (WinApi.GetWindowRect(hWnd, out WinApi.RECT rect))
                {
                    initialWindowPositions[hWnd] = rect;
                }

                return true; // continue enumeration
            }, IntPtr.Zero);

            // Set dragging flag to true
            isDragging = true;
        }

        /// <summary>
        /// Called when drag stops: stops dragging but leaves Lerp to continue to settle.
        /// </summary>
        public void StopDragging()
        {
            isDragging = false;
        }

        /// <summary>
        /// Resets the camera to center the group of visible windows on the primary monitor.
        /// </summary>
        public void ResetCamera()
        {
            // Dictionary to hold current window positions
            var currentPositions = new Dictionary<IntPtr, WinApi.RECT>();
            WinApi.EnumWindows((hWnd, lParam) =>
            {
                // Skip invisible windows
                if (!WinApi.IsWindowVisible(hWnd))
                    return true;

                // Skip minimized windows
                if (WinApi.IsIconic(hWnd))
                    return true;

                // Get window class name to filter out system UI
                var classNameBuilder = new System.Text.StringBuilder(256);
                WinApi.GetClassName(hWnd, classNameBuilder, classNameBuilder.Capacity);
                string className = classNameBuilder.ToString();

                // Skip ignored windows
                if (IsIgnoredWindow(className))
                    return true;

                // Get window rectangle
                if (WinApi.GetWindowRect(hWnd, out WinApi.RECT rect))
                {
                    // Store current position
                    currentPositions[hWnd] = rect;
                }

                return true; // continue enumeration
            }, IntPtr.Zero);

            // If no valid windows, do nothing
            if (currentPositions.Count == 0)
                return;

            // Compute bounding box
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (var rect in currentPositions.Values)
            {
                if (rect.Left < minX) minX = rect.Left;
                if (rect.Top < minY) minY = rect.Top;
                if (rect.Right > maxX) maxX = rect.Right;
                if (rect.Bottom > maxY) maxY = rect.Bottom;
            }

            // Compute center of the bounding box
            int groupCenterX = (minX + maxX) / 2;
            int groupCenterY = (minY + maxY) / 2;

            // Get the center of the primary monitor
            Rectangle primaryScreenBounds = Screen.PrimaryScreen!.Bounds;
            int screenCenterX = primaryScreenBounds.X + primaryScreenBounds.Width / 2;
            int screenCenterY = primaryScreenBounds.Y + primaryScreenBounds.Height / 2;

            // Compute delta needed to move the group's center to screen center
            int deltaX = screenCenterX - groupCenterX;
            int deltaY = screenCenterY - groupCenterY;

            // FIXED: Remove the false cache update during reset animation
            // The base cache initialWindowPositions should only be updated during actual dragging
            // Not during ResetCamera() as it causes the animation to collapse

            // Reset current animation progress
            currentDx = 0;
            currentDy = 0;

            // Set target offset to move to center
            targetDx = deltaX;
            targetDy = deltaY;

            // For instant snap (as requested), teleport current position to target
            currentDx = targetDx;
            currentDy = targetDy;
        }

        /// <summary>
        /// Called on each timer tick: updates window positions based on mouse movement with Lerp smoothing.
        /// </summary>
        /// <param name="deltaTime">Time elapsed since last update in milliseconds</param>
        public void UpdatePosition(float deltaTime)
        {
            // If dragging, update target based on mouse movement and warp cursor
            if (isDragging)
            {
                WinApi.GetCursorPos(out WinApi.POINT pt);
                int stepX = pt.X - startMouseX;
                int stepY = pt.Y - startMouseY;

                // Normal movement: windows follow mouse
                targetDx += stepX;
                targetDy += stepY;

                // Warp cursor back to start position to allow infinite dragging
                WinApi.SetCursorPos(startMouseX, startMouseY);
            }

            // Apply frame-rate independent Lerp smoothing: current chases target
            // Convert LerpRatePerSecond to a per-millisecond rate, then calculate factor for this deltaTime
            float lerpFactor = 1.0f - MathF.Exp(-_lerpRatePerSecond * deltaTime / 1000.0f);
            currentDx += (targetDx - currentDx) * lerpFactor;
            currentDy += (targetDy - currentDy) * lerpFactor;

            // If not dragging and we've sufficiently reached the target, we can skip updates
            if (!isDragging &&
                Math.Abs(targetDx - currentDx) < 0.5f &&
                Math.Abs(targetDy - currentDy) < 0.5f)
            {
                return;
            }

            // Convert to integer offset for window movement
            int dx = (int)Math.Round(currentDx);
            int dy = (int)Math.Round(currentDy);

            // Begin deferred window position session
            IntPtr hdwp = WinApi.BeginDeferWindowPos(initialWindowPositions.Count);
            if (hdwp == IntPtr.Zero)
                return;

            try
            {
                // Update each cached window
                foreach (var kvp in initialWindowPositions)
                {
                    IntPtr hWnd = kvp.Key;
                    WinApi.RECT rect = kvp.Value;

                    int newX = rect.Left + dx;   // using PLUS as per instruction (inversion achieved via negative target accumulation)
                    int newY = rect.Top + dy;

                    // Defer the window position change
                    hdwp = WinApi.DeferWindowPos(hdwp, hWnd, IntPtr.Zero, newX, newY, 0, 0, SWP_FLAGS);
                    if (hdwp == IntPtr.Zero)
                        break;
                }

                // Apply all deferred window position changes
                if (!WinApi.EndDeferWindowPos(hdwp))
                {
                    // If failed, we could optionally fall back to individual SetWindowPos calls
                    // but for simplicity we just ignore the error.
                }
            }
            finally
            {
                // Ensure we end the defer session even on error
                if (hdwp != IntPtr.Zero)
                {
                    WinApi.EndDeferWindowPos(hdwp);
                }
            }
        }
    }
}
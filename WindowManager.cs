using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using nyxWM;
using System.Windows.Forms; // Added for Screen

namespace nyxWM
{
    public class WindowManager
    {
        // Flags for SetWindowPos (same as in WinApi.cs but we define here for simplicity)
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FLAGS = SWP_NOZORDER | SWP_NOACTIVATE; // exclude SWP_NOSIZE because we change size

        // Cached initial positions of windows when dragging starts
        private readonly Dictionary<IntPtr, WinApi.RECT> initialWindowPositions = new();

        // Mouse start position (at drag start)
        private int startMouseX;
        private int startMouseY;

        // Lerp fields
        private float currentDx = 0, currentDy = 0;   // current window offset
        private float targetDx = 0, targetDy = 0;     // target offset from mouse movement
        private bool isDragging = false;

        // Zoom fields
        private float currentZoomLevel = 1.0f;
        private float targetZoomLevel = 1.0f;
        private const float ZOOM_LERP = 0.15f;
        private const float ZOOM_MIN = 0.5f;
        private const float ZOOM_MAX = 2.0f;

        // Keyboard hook for Alt+Tab and Win+Tab
        private readonly KeyboardHook _keyboardHook;

        public WindowManager()
        {
            _keyboardHook = new KeyboardHook();
            _keyboardHook.OnWindowSwitchTriggered += OnWindowSwitchTriggered;
            _keyboardHook.Start();
        }

        private void OnWindowSwitchTriggered()
        {
            CenterOnActiveWindow();
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

                // Skip taskbar and desktop windows
                if (className == "Shell_TrayWnd" || // Taskbar
                    className == "Progman" ||       // Desktop background
                    className == "WorkerW")         // Desktop worker
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
            Dictionary<IntPtr, WinApi.RECT> currentPositions = new Dictionary<IntPtr, WinApi.RECT>();

            // Enumerate all top-level windows to get current positions
            WinApi.EnumWindows((hWnd, lParam) =>
            {
                // Skip invisible windows
                if (!WinApi.IsWindowVisible(hWnd))
                    return true;

                // Skip minimized windows
                if (WinApi.IsIconic(hWnd))
                    return true;

                // Skip windows with empty title
                int titleLen = WinApi.GetWindowTextLength(hWnd);
                if (titleLen == 0)
                    return true;

                // Get window class name to filter out system UI
                var classNameBuilder = new StringBuilder(256);
                WinApi.GetClassName(hWnd, classNameBuilder, classNameBuilder.Capacity);
                string className = classNameBuilder.ToString();

                // Skip taskbar and desktop windows
                if (className == "Shell_TrayWnd" || // Taskbar
                    className == "Progman" ||       // Desktop background
                    className == "WorkerW")         // Desktop worker
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
                    currentPositions[hWnd] = rect;
                }

                return true; // continue enumeration
            }, IntPtr.Zero);

            // If no valid windows, do nothing
            if (currentPositions.Count == 0)
                return;

            // Compute bounding box from current positions
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (var rect in currentPositions.Values)
            {
                if (rect.Left < minX) minX = rect.Left;
                if (rect.Top < minY) minY = rect.Top;
                if (rect.Right > maxX) maxX = rect.Right;
                if (rect.Bottom > maxY) maxY = rect.Bottom;
            }

            // Compute center of the bounding box
            int centerX = (minX + maxX) / 2;
            int centerY = (minY + maxY) / 2;

            // Get the center of the primary monitor
            Rectangle primaryScreenBounds = Screen.PrimaryScreen.Bounds;
            int screenCenterX = primaryScreenBounds.X + primaryScreenBounds.Width / 2;
            int screenCenterY = primaryScreenBounds.Y + primaryScreenBounds.Height / 2;

            // Compute delta needed to move the windows' center to the screen center
            int deltaX = screenCenterX - centerX;
            int deltaY = screenCenterY - centerY;

            // IMPORTANT: Update initialWindowPositions with current positions
            initialWindowPositions.Clear();
            foreach (var kvp in currentPositions)
            {
                initialWindowPositions[kvp.Key] = kvp.Value;
            }

            // Reset current offsets and set target to move to center
            currentDx = 0;
            currentDy = 0;
            targetDx = deltaX;
            targetDy = deltaY;

            // Reset zoom
            currentZoomLevel = 1.0f;
            targetZoomLevel = 1.0f;
        }

        /// <summary>
        /// Centers the camera on the currently active window (triggered by Alt+Tab or Win+Tab).
        /// </summary>
        public void CenterOnActiveWindow()
        {
            IntPtr foreground = WinApi.GetForegroundWindow();
            if (foreground == IntPtr.Zero) return;

            // Get the rectangle of the foreground window
            if (!WinApi.GetWindowRect(foreground, out WinApi.RECT rect)) return;

            // Compute center of the window
            int windowCenterX = (rect.Left + rect.Right) / 2;
            int windowCenterY = (rect.Top + rect.Bottom) / 2;

            // Get the center of the primary monitor
            Rectangle screenBounds = Screen.PrimaryScreen.Bounds;
            int screenCenterX = screenBounds.X + screenBounds.Width / 2;
            int screenCenterY = screenBounds.Y + screenBounds.Height / 2;

            // Compute delta to move the window center to screen center
            int deltaX = screenCenterX - windowCenterX;
            int deltaY = screenCenterY - windowCenterY;

            // Set target offset to this delta (so that when we apply the offset, the window moves to center)
            targetDx = deltaX;
            targetDy = deltaY;

            // Note: We do not reset currentDx/currentDy here; the Lerp will smoothly move from current to target.
            // Zoom remains unchanged.
        }

        /// <summary>
        /// Called on each timer tick: updates window positions based on mouse movement with Lerp smoothing.
        /// </summary>
        public void UpdatePosition()
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

            // Apply Lerp smoothing: current chases target
            currentDx += (targetDx - currentDx) * 0.15f;
            currentDy += (targetDy - currentDy) * 0.15f;

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
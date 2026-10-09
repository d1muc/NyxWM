using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using nyxWM;

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

        // Accumulated delta since drag start (used for window movement)
        private int accumulatedDx = 0;
        private int accumulatedDy = 0;

        /// <summary>
        /// Called when drag starts: records mouse position, resets accumulators, and caches initial window positions.
        /// </summary>
        public void StartDragging()
        {
            // Get current cursor position
            WinApi.GetCursorPos(out WinApi.POINT pt);
            startMouseX = pt.X;
            startMouseY = pt.Y;

            // Reset accumulated delta
            accumulatedDx = 0;
            accumulatedDy = 0;

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
        }

        /// <summary>
        /// Called on each timer tick while dragging: updates window positions based on accumulated mouse movement.
        /// Implements warp cursor: after each step, returns cursor to start position to allow infinite dragging.
        /// </summary>
        public void UpdatePosition()
        {
            // Get current cursor position
            WinApi.GetCursorPos(out WinApi.POINT pt);
            int currentX = pt.X;
            int currentY = pt.Y;

            // Calculate step since last tick
            int stepX = currentX - startMouseX;
            int stepY = currentY - startMouseY;

            // If no movement, do nothing
            if (stepX == 0 && stepY == 0)
                return;

            // WARP: immediately return cursor to start position to prevent hitting screen edges
            WinApi.SetCursorPos(startMouseX, startMouseY);

            // Accumulate the step
            accumulatedDx += stepX;
            accumulatedDy += stepY;

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

                    // Apply inverted accumulated delta (windows move opposite to mouse)
                    int newX = rect.Left - accumulatedDx;
                    int newY = rect.Top - accumulatedDy;

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

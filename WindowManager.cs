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

        // Lerp fields
        private float currentDx = 0, currentDy = 0;   // current window offset
        private float targetDx = 0, targetDy = 0;     // target offset from mouse movement
        private bool isDragging = false;

        /// <summary>
        /// Called when drag starts: records mouse position, resets Lerp variables, and caches initial window positions.
        /// </summary>
        public void StartDragging()
        {
            // Get current cursor position
            WinApi.GetCursorPos(out WinApi.POINT pt);
            startMouseX = pt.X;
            startMouseY = pt.Y;

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
        }

        /// <summary>
        /// Called when drag stops: stops dragging but leaves Lerp to continue to settle.
        /// </summary>
        public void StopDragging()
        {
            isDragging = false;
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

                // Invert accumulation so windows move opposite to mouse
                targetDx -= stepX;
                targetDy -= stepY;

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

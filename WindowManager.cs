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

            // Mouse start position
            private int startMouseX;
            private int startMouseY;

            /// <summary>
            /// Called when drag starts: records mouse position and caches initial window positions.
            /// </summary>
            public void StartDragging()
            {
                // Get current cursor position
                WinApi.GetCursorPos(out WinApi.POINT pt);
                startMouseX = pt.X;
                startMouseY = pt.Y;

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
            /// Called on each timer tick while dragging: updates window positions based on mouse movement.
            /// </summary>
            public void UpdatePosition()
            {
                // Get current cursor position
                WinApi.GetCursorPos(out WinApi.POINT pt);
                int currentX = pt.X;
                int currentY = pt.Y;

                // Calculate delta from start position
                int dx = currentX - startMouseX;
                int dy = currentY - startMouseY;

                // If no movement, do nothing
                if (dx == 0 && dy == 0)
                    return;

                // Invert delta to simulate camera movement (moving mouse right moves windows left)
                dx = -dx;
                dy = -dy;

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

                        int newX = rect.Left + dx;
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

                // Optional: reset cursor to start position for infinite dragging
                // WinApi.SetCursorPos(startMouseX, startMouseY);
            }
        }
}

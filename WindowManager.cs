using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace nyxWM
{
    public class WindowManager
    {
        // Flags for SetWindowPos
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FLAGS = SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE;

        public List<IntPtr> GetDriftableWindows()
        {
            var windows = new List<IntPtr>();
            WinApi.EnumWindows((hWnd, lParam) =>
            {
                if (WinApi.IsWindowVisible(hWnd))
                {
                    windows.Add(hWnd);
                }
                return true; // continue enumeration
            }, IntPtr.Zero);
            return windows;
        }

        public void MoveAllWindows(int dx, int dy)
        {
            var windows = GetDriftableWindows();
            foreach (var hWnd in windows)
            {
                try
                {
                    if (WinApi.GetWindowRect(hWnd, out WinApi.RECT rect))
                    {
                        int newX = rect.Left + dx;
                        int newY = rect.Top + dy;
                        WinApi.SetWindowPos(hWnd, IntPtr.Zero, newX, newY, 0, 0, SWP_FLAGS);
                    }
                }
                catch (Exception)
                {
                    // Ignore errors for individual windows (e.g., window closed)
                }
            }
        }
    }
}

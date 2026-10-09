using System;
using System.Runtime.InteropServices;
using nyxWM; // To access WinApi

namespace nyxWM
{
    public class MouseHook
    {
        // Constants for mouse messages
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_MBUTTONUP = 0x0208;
        private const int WM_MOUSEMOVE = 0x0200;

        // State variables
        private bool isDragging = false;
        private int startX = 0;
        private int startY = 0;

        // Event for dragging
        public event Action<int, int> OnMouseDrag;

        // Hook handle and procedure
        private IntPtr hHook = IntPtr.Zero;
        private WinApi.LowLevelMouseProc _hookProc;

        public MouseHook()
        {
            _hookProc = HookCallback;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                // Marshal the struct from lParam
                WinApi.MSLLHOOKSTRUCT mouseStruct = (WinApi.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(WinApi.MSLLHOOKSTRUCT));
                int x = mouseStruct.pt.X;
                int y = mouseStruct.pt.Y;

                uint message = (uint)wParam.ToInt32();

                if (message == WM_MBUTTONDOWN)
                {
                    isDragging = true;
                    startX = x;
                    startY = y;
                    // Block the middle button down event
                    return (IntPtr)1;
                }
                else if (message == WM_MOUSEMOVE && isDragging)
                {
                    int dx = x - startX;
                    int dy = y - startY;
                    OnMouseDrag?.Invoke(dx, dy);
                    // Update the last position
                    startX = x;
                    startY = y;
                    // Block the mouse move event
                    return (IntPtr)1;
                }
                else if (message == WM_MBUTTONUP)
                {
                    isDragging = false;
                    // Let the system handle the middle button up event
                    return WinApi.CallNextHookEx(hHook, nCode, wParam, lParam);
                }
            }

            // Call the next hook in the chain
            return WinApi.CallNextHookEx(hHook, nCode, wParam, lParam);
        }

        public void Start()
        {
            if (hHook == IntPtr.Zero)
            {
                // Get the handle of the current module
                IntPtr hModule = WinApi.GetModuleHandle(System.Diagnostics.Process.GetCurrentProcess().MainModule.ModuleName);
                hHook = WinApi.SetWindowsHookEx(WinApi.WH_MOUSE_LL, _hookProc, hModule, 0);
                if (hHook == IntPtr.Zero)
                {
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                }
            }
        }

        public void Stop()
        {
            if (hHook != IntPtr.Zero)
            {
                bool result = WinApi.UnhookWindowsHookEx(hHook);
                hHook = IntPtr.Zero;
                if (!result)
                {
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                }
            }
        }
    }
}

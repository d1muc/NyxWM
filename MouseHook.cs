using System;
using System.Runtime.InteropServices;
using nyxWM;

namespace nyxWM
{
    public class MouseHook
    {
        // Constants for mouse messages
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_MBUTTONUP = 0x0208;
        private const int WM_MOUSEMOVE = 0x0200;

        // Events for drag start and stop
        public event Action OnDragStart = delegate {};
        public event Action OnDragStop = delegate {};

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
                uint message = (uint)wParam.ToInt32();

                if (message == WM_MBUTTONDOWN)
                {
                    OnDragStart?.Invoke();
                    // Block the middle button down event to prevent system scroll
                    return (IntPtr)1;
                }
                else if (message == WM_MBUTTONUP)
                {
                    OnDragStop?.Invoke();
                    // Let the system handle the middle button up event
                    return hHook == IntPtr.Zero ? IntPtr.Zero : WinApi.CallNextHookEx(hHook, nCode, wParam, lParam);
                }
                // For WM_MOUSEMOVE and other messages, call the next hook without blocking
            }

            // Call the next hook in the chain
            return hHook == IntPtr.Zero ? IntPtr.Zero : WinApi.CallNextHookEx(hHook, nCode, wParam, lParam);
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

using System;
using System.Runtime.InteropServices;
using nyxWM;

namespace nyxWM
{
    public class KeyboardHook
    {
        // Constants for keyboard messages
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104; // Alt+key combinations

        // Virtual keys
        private const int VK_MENU = 0x12; // Alt
        private const int VK_LWIN = 0x5B; // Left Windows
        private const int VK_RWIN = 0x5C; // Right Windows
        private const int VK_TAB = 0x09;  // Tab

        // Events
        public event Action OnWindowSwitchTriggered = delegate { };

        // Hook handle and procedure
        private IntPtr hHook = IntPtr.Zero;
        private WinApi.LowLevelKeyboardProc _hookProc;

        // State tracking
        private bool altDown = false;
        private bool winDown = false;

        public KeyboardHook()
        {
            _hookProc = HookCallback;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vkCode = Marshal.ReadInt32(lParam);

                // Track key state for Alt and Win keys
                if (vkCode == VK_MENU)
                {
                    if ((wParam.ToInt32() == WM_KEYDOWN || wParam.ToInt32() == WM_SYSKEYDOWN))
                    {
                        altDown = true;
                    }
                    else if (wParam.ToInt32() == 0x0101 || wParam.ToInt32() == 0x0105) // WM_KEYUP, WM_SYSKEYUP
                    {
                        altDown = false;
                    }
                }
                else if (vkCode == VK_LWIN || vkCode == VK_RWIN)
                {
                    if (wParam.ToInt32() == WM_KEYDOWN || wParam.ToInt32() == WM_SYSKEYDOWN)
                    {
                        winDown = true;
                    }
                    else if (wParam.ToInt32() == 0x0101 || wParam.ToInt32() == 0x0105) // WM_KEYUP, WM_SYSKEYUP
                    {
                        winDown = false;
                    }
                }

                // Check for Tab key down while Alt or Win is held
                if (vkCode == VK_TAB &&
                    (wParam.ToInt32() == WM_KEYDOWN || wParam.ToInt32() == WM_SYSKEYDOWN))
                {
                    if (altDown || winDown)
                    {
                        // Trigger the event
                        OnWindowSwitchTriggered?.Invoke();
                    }
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
                hHook = WinApi.SetWindowsHookEx(WinApi.WH_KEYBOARD_LL, _hookProc, hModule, 0);
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
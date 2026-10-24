using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using nyxWM;

namespace nyxWM
{
    public class MouseHook
    {
        // Constants for mouse messages
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_MBUTTONUP = 0x0208;
        private const int WM_XBUTTONDOWN = 0x020B;
        private const int WM_XBUTTONUP = 0x020C;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int WM_MOUSEHWHEEL = 0x020E;

        // Constants for keyboard messages
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;

        // Events for drag start and stop
        public event Action OnDragStart = delegate {};
        public event Action OnDragStop = delegate {};

        // Hook handles and procedures
        private IntPtr hMouseHook = IntPtr.Zero;
        private IntPtr hKeyboardHook = IntPtr.Zero;
        private WinApi.LowLevelMouseProc _mouseHookProc;
        private WinApi.LowLevelKeyboardProc _keyboardHookProc;

        // State tracking
        private readonly HashSet<int> _requiredKeys = new HashSet<int>();
        private readonly HashSet<int> _pressedKeys = new HashSet<int>();
        private readonly string _mainmod;
        private bool _isDragging = false;
        private volatile bool _isEnabled = true;

        // Virtual keys whose button-down/key-down was swallowed by the hook.
        // Their matching release must be swallowed too, otherwise the target
        // application receives an orphan up-event (context menu, stray click).
        private int _blockedMouseVk;
        private int _blockedKeyVk;

        public MouseHook(string cameraMoveButton, string mainmod)
        {
            _mainmod = mainmod;
            _mouseHookProc = MouseHookCallback;
            _keyboardHookProc = KeyboardHookCallback;

            // Parse the cameraMoveButton string to determine required keys
            ParseCameraMoveButton(cameraMoveButton);
        }

        private void ParseCameraMoveButton(string cameraMoveButton)
        {
            if (string.IsNullOrWhiteSpace(cameraMoveButton))
                return;

            // Replace mainmod placeholder
            string replaced = cameraMoveButton.Replace("mainmod", _mainmod, StringComparison.InvariantCultureIgnoreCase);
            // Split by '+'
            string[] parts = replaced.Split('+', StringSplitOptions.RemoveEmptyEntries);

            // Clear existing required keys
            _requiredKeys.Clear();

            // Convert each part to virtual key codes
            foreach (string part in parts)
            {
                string trimmed = part.Trim().ToUpperInvariant();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                int vkCode = GetVirtualKeyCode(trimmed);
                if (vkCode != 0)
                {
                    _requiredKeys.Add(vkCode);
                }
            }
        }

        private int GetVirtualKeyCode(string key)
        {
            // Handle modifiers
            return key switch
            {
                "CONTROL" or "CTRL" => WinApi.VK_CONTROL,
                "LCTRL" => WinApi.VK_LCONTROL,
                "RCTRL" => WinApi.VK_RCONTROL,
                "ALT" or "MENU" => WinApi.VK_MENU,
                "LALT" => WinApi.VK_LMENU,
                "RALT" => WinApi.VK_RMENU,
                "SHIFT" => WinApi.VK_SHIFT,
                "LSHIFT" => WinApi.VK_LSHIFT,
                "RSHIFT" => WinApi.VK_RSHIFT,
                "WIN" or "LWIN" => WinApi.VK_LWIN,
                "RWIN" => WinApi.VK_RWIN,
                "APPS" => WinApi.VK_APPS,

                // Mouse buttons
                "LEFT" => WinApi.VK_LBUTTON,
                "RIGHT" => WinApi.VK_RBUTTON,
                "MIDDLE" => WinApi.VK_MBUTTON,
                "MBUTTON" => WinApi.VK_MBUTTON,
                "XBUTTON1" => WinApi.VK_XBUTTON1,
                "XBUTTON2" => WinApi.VK_XBUTTON2,

                // Special keys
                "F1" => WinApi.VK_F1,
                "F2" => WinApi.VK_F2,
                "F3" => WinApi.VK_F3,
                "F4" => WinApi.VK_F4,
                "F5" => WinApi.VK_F5,
                "F6" => WinApi.VK_F6,
                "F7" => WinApi.VK_F7,
                "F8" => WinApi.VK_F8,
                "F9" => WinApi.VK_F9,
                "F10" => WinApi.VK_F10,
                "F11" => WinApi.VK_F11,
                "F12" => WinApi.VK_F12,
                "LEFTARROW" or "LEFT" => WinApi.VK_LEFT,
                "UP" => WinApi.VK_UP,
                "RIGHTARROW" or "RIGHT" => WinApi.VK_RIGHT,
                "DOWNARROW" or "DOWN" => WinApi.VK_DOWN,
                "ENTER" => WinApi.VK_RETURN,
                "ESC" => WinApi.VK_ESCAPE,
                "SPACE" => WinApi.VK_SPACE,
                "TAB" => WinApi.VK_TAB,
                "BACKSPACE" => WinApi.VK_BACK,
                "DELETE" => WinApi.VK_DELETE,
                "INSERT" => WinApi.VK_INSERT,
                "HOME" => WinApi.VK_HOME,
                "END" => WinApi.VK_END,
                "PAGEUP" => WinApi.VK_PRIOR,
                "PAGEDOWN" => WinApi.VK_NEXT,

                // Single character keys (A-Z, 0-9)
                _ => GetCharacterVirtualKeyCode(key)
            };
        }

        private int GetCharacterVirtualKeyCode(string key)
        {
            if (key.Length == 1)
            {
                char c = key[0];
                if (c >= 'A' && c <= 'Z')
                    return c;
                if (c >= '0' && c <= '9')
                    return c;
            }
            return 0; // Invalid key
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                // While disabled (game mode) the hook must not track keys or start
                // drags: swallowing/processing input during a game is exactly what
                // we are trying to avoid.
                if (!_isEnabled)
                    return WinApi.CallNextHookEx(hMouseHook, nCode, wParam, lParam);

                uint message = (uint)wParam.ToInt32();

                // Handle mouse button events
                switch (message)
                {
                    case WM_LBUTTONDOWN:
                    case WM_RBUTTONDOWN:
                    case WM_MBUTTONDOWN:
                        AddKeyFromMouseMessage(message);
                        CheckDragState();
                        break;

                    case WM_LBUTTONUP:
                    case WM_RBUTTONUP:
                    case WM_MBUTTONUP:
                        RemoveKeyFromMouseMessage(message);
                        CheckDragState();
                        break;

                    case WM_XBUTTONDOWN:
                    case WM_XBUTTONUP:
                        // For X buttons, we need to get the actual button from mouseData
                        var mouseHookStruct = Marshal.PtrToStructure<WinApi.MSLLHOOKSTRUCT>(lParam);
                        uint buttonData = (mouseHookStruct.mouseData >> 16) & 0xFFFF;

                        if (message == WM_XBUTTONDOWN)
                        {
                            AddKeyFromXButton(buttonData);
                            CheckDragState();
                        }
                        else // WM_XBUTTONUP
                        {
                            RemoveKeyFromXButton(buttonData);
                            CheckDragState();
                        }
                        break;
                }

                // Determine if we should block this event
                int eventVk = ResolveVkCode(message, lParam);
                bool isRelease = IsReleaseMessage(message);
                bool shouldBlock = _isEnabled && _isDragging && IsEventBlockedByCurrentState(message, lParam);

                // If we should block, return non-zero to prevent the event from reaching the system
                if (shouldBlock)
                {
                    if (isRelease)
                        _blockedMouseVk = 0;
                    else
                        _blockedMouseVk = eventVk;

                    return new IntPtr(1);
                }

                // The matching button-up must not reach the application when the
                // down-event was swallowed, even though dragging already ended.
                if (isRelease && eventVk != 0 && eventVk == _blockedMouseVk)
                {
                    _blockedMouseVk = 0;
                    return new IntPtr(1);
                }
            }

            // Call the next hook in the chain without blocking
            return hMouseHook == IntPtr.Zero ? IntPtr.Zero : WinApi.CallNextHookEx(hMouseHook, nCode, wParam, lParam);
        }

        /// <summary>
        /// Returns true for messages that release a previously pressed key or button.
        /// </summary>
        private static bool IsReleaseMessage(uint message)
        {
            return message == WM_LBUTTONUP ||
                   message == WM_RBUTTONUP ||
                   message == WM_MBUTTONUP ||
                   message == WM_XBUTTONUP ||
                   message == WM_KEYUP ||
                   message == WM_SYSKEYUP;
        }

        /// <summary>
        /// Resolves the virtual-key code carried by a hook event.
        /// Returns 0 when the event is not a key/button event.
        /// </summary>
        private static int ResolveVkCode(uint message, IntPtr lParam)
        {
            switch (message)
            {
                case WM_KEYDOWN:
                case WM_KEYUP:
                case WM_SYSKEYDOWN:
                case WM_SYSKEYUP:
                    var keyboardHookStruct = Marshal.PtrToStructure<WinApi.KBDLLHOOKSTRUCT>(lParam);
                    return (int)keyboardHookStruct.vkCode;

                case WM_LBUTTONDOWN:
                case WM_LBUTTONUP:
                    return WinApi.VK_LBUTTON;

                case WM_RBUTTONDOWN:
                case WM_RBUTTONUP:
                    return WinApi.VK_RBUTTON;

                case WM_MBUTTONDOWN:
                case WM_MBUTTONUP:
                    return WinApi.VK_MBUTTON;

                case WM_XBUTTONDOWN:
                case WM_XBUTTONUP:
                    var mouseHookStruct = Marshal.PtrToStructure<WinApi.MSLLHOOKSTRUCT>(lParam);
                    uint buttonData = (mouseHookStruct.mouseData >> 16) & 0xFFFF;
                    return buttonData switch
                    {
                        1 => WinApi.VK_XBUTTON1,
                        2 => WinApi.VK_XBUTTON2,
                        _ => 0
                    };

                default:
                    return 0;
            }
        }

        /// <summary>
        /// Determines if the given event should be blocked based on current state.
        /// Returns true if the event is for a key/button that's part of the required combination.
        /// </summary>
        private bool IsEventBlockedByCurrentState(uint message, IntPtr lParam)
        {
            int vkCode = ResolveVkCode(message, lParam);

            // If we couldn't determine a VK code, don't block
            if (vkCode == 0)
                return false;

            // Check if this VK code (or its generalized form) is in our required keys
            int generalizedVkCode = MapToGeneralizedKey(vkCode);
            return _requiredKeys.Contains(generalizedVkCode);
        }

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                // Disabled during game mode: pass everything through untouched.
                if (!_isEnabled)
                    return WinApi.CallNextHookEx(hKeyboardHook, nCode, wParam, lParam);

                uint message = (uint)wParam.ToInt32();

                // Handle keyboard events
                switch (message)
                {
                    case WM_KEYDOWN:
                    case WM_SYSKEYDOWN:
                        // Get the virtual key code from lParam using proper structure
                        var keyDownStruct = Marshal.PtrToStructure<WinApi.KBDLLHOOKSTRUCT>(lParam);
                        int vkCode = (int)keyDownStruct.vkCode;
                        _pressedKeys.Add(MapToGeneralizedKey(vkCode));
                        CheckDragState();
                        break;

                    case WM_KEYUP:
                    case WM_SYSKEYUP:
                        // Get the virtual key code from lParam using proper structure
                        var keyUpStruct = Marshal.PtrToStructure<WinApi.KBDLLHOOKSTRUCT>(lParam);
                        int keyCode = (int)keyUpStruct.vkCode;
                        _pressedKeys.Remove(MapToGeneralizedKey(keyCode));
                        CheckDragState();
                        break;
                }

                // Determine if we should block this event
                int eventVk = ResolveVkCode(message, lParam);
                bool isRelease = IsReleaseMessage(message);
                bool shouldBlock = _isEnabled && _isDragging && IsEventBlockedByCurrentState(message, lParam);

                // If we should block, return non-zero to prevent the event from reaching the system
                if (shouldBlock)
                {
                    if (isRelease)
                        _blockedKeyVk = 0;
                    else
                        _blockedKeyVk = eventVk;

                    return new IntPtr(1);
                }

                // Swallow the key-up that belongs to a swallowed key-down.
                if (isRelease && eventVk != 0 && eventVk == _blockedKeyVk)
                {
                    _blockedKeyVk = 0;
                    return new IntPtr(1);
                }
            }

            // Call the next hook in the chain without blocking
            return hKeyboardHook == IntPtr.Zero ? IntPtr.Zero : WinApi.CallNextHookEx(hKeyboardHook, nCode, wParam, lParam);
        }

        private void AddKeyFromMouseMessage(uint message)
        {
            // For mouse messages other than X buttons, we can directly map
            if (message != WM_XBUTTONDOWN && message != WM_XBUTTONUP)
            {
                int vkCode = message switch
                {
                    WM_LBUTTONDOWN => WinApi.VK_LBUTTON,
                    WM_RBUTTONDOWN => WinApi.VK_RBUTTON,
                    WM_MBUTTONDOWN => WinApi.VK_MBUTTON,
                    _ => 0
                };

                if (vkCode != 0)
                    _pressedKeys.Add(vkCode);
            }
            // X button handling is done in MouseHookCallback via mouseData
        }

        private void RemoveKeyFromMouseMessage(uint message)
        {
            // For mouse messages other than X buttons, we can directly map
            if (message != WM_XBUTTONDOWN && message != WM_XBUTTONUP)
            {
                int vkCode = message switch
                {
                    WM_LBUTTONUP => WinApi.VK_LBUTTON,
                    WM_RBUTTONUP => WinApi.VK_RBUTTON,
                    WM_MBUTTONUP => WinApi.VK_MBUTTON,
                    _ => 0
                };

                if (vkCode != 0)
                    _pressedKeys.Remove(vkCode);
            }
            // X button handling is done in MouseHookCallback via mouseData
        }

        private void AddKeyFromXButton(uint buttonData)
        {
            int vkCode = buttonData switch
            {
                1 => WinApi.VK_XBUTTON1,
                2 => WinApi.VK_XBUTTON2,
                _ => 0
            };

            if (vkCode != 0)
                _pressedKeys.Add(vkCode);
        }

        private void RemoveKeyFromXButton(uint buttonData)
        {
            int vkCode = buttonData switch
            {
                1 => WinApi.VK_XBUTTON1,
                2 => WinApi.VK_XBUTTON2,
                _ => 0
            };

            if (vkCode != 0)
                _pressedKeys.Remove(vkCode);
        }

        /// <summary>
        /// Maps specific left/right modifier keys to their generalized counterparts.
        /// This allows CONTROL in config to match both VK_LCONTROL and VK_RCONTROL.
        /// </summary>
        private int MapToGeneralizedKey(int vkCode)
        {
            return vkCode switch
            {
                WinApi.VK_LCONTROL or WinApi.VK_RCONTROL => WinApi.VK_CONTROL,
                WinApi.VK_LMENU or WinApi.VK_RMENU => WinApi.VK_MENU,
                WinApi.VK_LSHIFT or WinApi.VK_RSHIFT => WinApi.VK_SHIFT,
                _ => vkCode
            };
        }

        private void CheckDragState()
        {
            bool allKeysPressed = _requiredKeys.Count > 0 &&
                                 _requiredKeys.All(k =>
                                 {
                                     // Check if any pressed key maps to this required key
                                     return _pressedKeys.Any(pk => MapToGeneralizedKey(pk) == k);
                                 });

            if (allKeysPressed && !_isDragging)
            {
                _isDragging = true;
                OnDragStart?.Invoke();
            }
            else if (!allKeysPressed && _isDragging)
            {
                _isDragging = false;
                OnDragStop?.Invoke();
            }
        }

        public void Start()
        {
            // Reset state to prevent sticking
            ResetState();

            if (hMouseHook == IntPtr.Zero)
            {
                // Get the handle of the current module
                Process currentProcess = System.Diagnostics.Process.GetCurrentProcess();
                if (currentProcess.MainModule == null)
                {
                    throw new InvalidOperationException("Cannot determine main module name.");
                }
                IntPtr hModule = WinApi.GetModuleHandle(currentProcess.MainModule.ModuleName);

                // Set up mouse hook
                hMouseHook = WinApi.SetWindowsHookEx(WinApi.WH_MOUSE_LL, _mouseHookProc, hModule, 0);
                if (hMouseHook == IntPtr.Zero)
                {
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                }

                // Set up keyboard hook
                hKeyboardHook = WinApi.SetWindowsHookEx(WinApi.WH_KEYBOARD_LL, _keyboardHookProc, hModule, 0);
                if (hKeyboardHook == IntPtr.Zero)
                {
                    // Clean up mouse hook if keyboard hook fails
                    WinApi.UnhookWindowsHookEx(hMouseHook);
                    hMouseHook = IntPtr.Zero;
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                }
            }
        }

        /// <summary>
        /// Resets the internal state of the mouse hook.
        /// Clears pressed keys and resets dragging state.
        /// </summary>
        public void ResetState()
        {
            _pressedKeys.Clear();
            _isDragging = false;
            _blockedMouseVk = 0;
            _blockedKeyVk = 0;
            OnDragStop?.Invoke();
        }

        /// <summary>
        /// Sets whether the hook should actively filter and suppress input.
        /// When disabled, all events are passed through to the system.
        /// </summary>
        /// <param name="enabled">True to enable filtering, false to disable</param>
        public void SetEnabled(bool enabled)
        {
            _isEnabled = enabled;
            // If disabling, reset state to prevent stuck keys
            if (!enabled)
            {
                ResetState();
            }
        }

        public void Stop()
        {
            if (hMouseHook != IntPtr.Zero)
            {
                WinApi.UnhookWindowsHookEx(hMouseHook);
                hMouseHook = IntPtr.Zero;
            }

            if (hKeyboardHook != IntPtr.Zero)
            {
                WinApi.UnhookWindowsHookEx(hKeyboardHook);
                hKeyboardHook = IntPtr.Zero;
            }

            // Reset state to prevent sticking
            ResetState();
        }
    }
}
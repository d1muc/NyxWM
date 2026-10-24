using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace nyxWM
{
    /// <summary>
    /// Reads relative mouse movement from the Raw Input API (WM_INPUT).
    /// Raw deltas bypass the system pointer acceleration curve, screen-edge
    /// clamping and the position of the cursor itself, so fast flicks stay
    /// linear and the cursor warp cannot pollute the measurement.
    /// </summary>
    public sealed class RawMouseInput : IDisposable
    {
        private const int WM_INPUT = 0x00FF;

        private const uint RID_INPUT = 0x10000003;
        private const uint RIDEV_REMOVE = 0x00000001;
        private const uint RIDEV_INPUTSINK = 0x00000100;
        private const uint RIM_TYPEMOUSE = 0;

        private const ushort MOUSE_MOVE_ABSOLUTE = 0x0001;
        private const int HWND_MESSAGE = -3;
        private const uint ErrorValue = unchecked((uint)-1);

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTHEADER
        {
            public uint dwType;
            public uint dwSize;
            public IntPtr hDevice;
            public IntPtr wParam;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct RAWMOUSE
        {
            [FieldOffset(0)]
            public ushort usFlags;

            [FieldOffset(4)]
            public uint ulButtons;

            [FieldOffset(8)]
            public uint ulRawButtons;

            [FieldOffset(12)]
            public int lLastX;

            [FieldOffset(16)]
            public int lLastY;

            [FieldOffset(20)]
            public uint ulExtraInformation;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices(
            [In] RAWINPUTDEVICE[] pRawInputDevices,
            uint uiNumDevices,
            uint cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputData(
            IntPtr hRawInput,
            uint uiCommand,
            IntPtr pData,
            ref uint pcbSize,
            uint cbSizeHeader);

        private readonly int _bufferSize;
        private readonly int _headerSize;
        private readonly int _mouseSize;
        private readonly IntPtr _buffer;

        private MessageWindow? _window;
        private bool _registered;
        private bool _disposed;

        private int _deltaX;
        private int _deltaY;

        // Absolute-mode state (RDP, tablets, some virtual machines).
        private bool _absoluteMode;
        private int _lastAbsoluteX;
        private int _lastAbsoluteY;

        /// <param name="countsToPixels">
        /// Multiplier applied to raw device counts so that the default cursor
        /// speed keeps a familiar feel. Values are clamped to a sane range.
        /// </param>
        internal RawMouseInput(float countsToPixels)
        {
            _headerSize = Marshal.SizeOf<RAWINPUTHEADER>();
            _mouseSize = Marshal.SizeOf<RAWMOUSE>();
            _bufferSize = _headerSize + _mouseSize + 16;
            _buffer = Marshal.AllocHGlobal(_bufferSize);
            CountsToPixels = Math.Clamp(countsToPixels, 0.05f, 8f);

            try
            {
                _window = new MessageWindow(OnRawMessage);
                _registered = RegisterDevice();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Raw input unavailable, falling back to cursor polling: {ex.Message}");
                _registered = false;
            }

            if (!_registered && _window != null)
            {
                _window.Dispose();
                _window = null;
            }
        }

        private bool RegisterDevice()
        {
            return RegisterRawInputDevices(
                new[]
                {
                    new RAWINPUTDEVICE
                    {
                        usUsagePage = 0x01, // Generic desktop controls
                        usUsage = 0x02,     // Mouse
                        dwFlags = RIDEV_INPUTSINK,
                        hwndTarget = _window!.Handle
                    }
                },
                1,
                (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
        }

        private void UnregisterDevice()
        {
            if (!_registered)
                return;

            try
            {
                RegisterRawInputDevices(
                    new[]
                    {
                        new RAWINPUTDEVICE
                        {
                            usUsagePage = 0x01,
                            usUsage = 0x02,
                            dwFlags = RIDEV_REMOVE,
                            hwndTarget = IntPtr.Zero
                        }
                    },
                    1,
                    (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to unregister raw input: {ex.Message}");
            }

            _registered = false;
        }

        /// <summary>
        /// Stops receiving WM_INPUT while keeping the message window alive.
        /// Used in game mode so the process does not sit in the raw-input path.
        /// </summary>
        internal void Suspend()
        {
            if (_disposed)
                return;

            UnregisterDevice();
            Flush();
        }

        /// <summary>
        /// Re-registers for WM_INPUT after <see cref="Suspend"/>.
        /// </summary>
        internal void Resume()
        {
            if (_disposed || _registered || _window == null)
                return;

            try
            {
                _registered = RegisterDevice();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to re-register raw input: {ex.Message}");
                _registered = false;
            }

            Flush();
        }

        /// <summary>True when WM_INPUT is delivering movement for this process.</summary>
        internal bool IsReady => _registered && _window != null && !_disposed;

        /// <summary>Normalization factor between raw device counts and screen pixels.</summary>
        internal float CountsToPixels { get; }

        /// <summary>
        /// Takes the accumulated movement since the previous call. Returns false
        /// when no movement arrived.
        /// </summary>
        internal bool TryConsume(out int dx, out int dy)
        {
            dx = Interlocked.Exchange(ref _deltaX, 0);
            dy = Interlocked.Exchange(ref _deltaY, 0);
            return dx != 0 || dy != 0;
        }

        /// <summary>Drops any movement captured so far and resets absolute-mode baselines.</summary>
        internal void Flush()
        {
            Interlocked.Exchange(ref _deltaX, 0);
            Interlocked.Exchange(ref _deltaY, 0);
            _lastAbsoluteX = int.MinValue;
            _lastAbsoluteY = int.MinValue;
        }

        private void OnRawMessage(Message message)
        {
            if (message.Msg != WM_INPUT || message.LParam == IntPtr.Zero)
                return;

            uint size = (uint)_bufferSize;
            uint read = GetRawInputData(
                message.LParam,
                RID_INPUT,
                _buffer,
                ref size,
                (uint)_headerSize);

            if (read == ErrorValue || read < (uint)(_headerSize + _mouseSize))
                return;

            RAWINPUTHEADER header = Marshal.PtrToStructure<RAWINPUTHEADER>(_buffer);
            if (header.dwType != RIM_TYPEMOUSE)
                return;

            RAWMOUSE mouse = Marshal.PtrToStructure<RAWMOUSE>(
                IntPtr.Add(_buffer, _headerSize));

            bool absolute = (mouse.usFlags & MOUSE_MOVE_ABSOLUTE) != 0;
            if (absolute)
            {
                AccumulateAbsolute(mouse.lLastX, mouse.lLastY);
            }
            else
            {
                _absoluteMode = false;
                Accumulate(mouse.lLastX, mouse.lLastY);
            }
        }

        private void Accumulate(int dx, int dy)
        {
            if (dx != 0)
                Interlocked.Add(ref _deltaX, dx);
            if (dy != 0)
                Interlocked.Add(ref _deltaY, dy);
        }

        private void AccumulateAbsolute(int x, int y)
        {
            // Absolute devices report a position in 0..65535 rather than a delta.
            if (!_absoluteMode)
            {
                _absoluteMode = true;
                _lastAbsoluteX = x;
                _lastAbsoluteY = y;
                return;
            }

            int dx = x - _lastAbsoluteX;
            int dy = y - _lastAbsoluteY;
            _lastAbsoluteX = x;
            _lastAbsoluteY = y;

            // Wrap-around happens when the pointer crosses the virtual desktop edge.
            if (Math.Abs(dx) > 32000 || Math.Abs(dy) > 32000)
                return;

            Accumulate(dx, dy);
        }

        /// <summary>
        /// Reads the Windows cursor speed setting so raw counts keep roughly the
        /// same on-screen scale the user is used to. Falls back to 1.0.
        /// </summary>
        internal static float ReadCursorSpeedNormalization()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                    @"Control Panel\Mouse", writable: false);

                if (key?.GetValue("MouseSensitivity") is string raw &&
                    int.TryParse(raw, out int sensitivity) &&
                    sensitivity >= 1 && sensitivity <= 20)
                {
                    return sensitivity / 10f;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not read cursor speed: {ex.Message}");
            }

            return 1f;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            UnregisterDevice();

            _window?.Dispose();
            _window = null;

            Marshal.FreeHGlobal(_buffer);
        }

        /// <summary>Message-only window that receives WM_INPUT.</summary>
        private sealed class MessageWindow : NativeWindow, IDisposable
        {
            private readonly Action<Message> _onMessage;

            internal MessageWindow(Action<Message> onMessage)
            {
                _onMessage = onMessage;
                CreateHandle(new CreateParams
                {
                    Caption = "nyxWM.RawInput",
                    Style = 0,
                    ExStyle = 0,
                    Parent = new IntPtr(HWND_MESSAGE)
                });
            }

            protected override void WndProc(ref Message m)
            {
                _onMessage(m);
                base.WndProc(ref m);
            }

            public void Dispose()
            {
                if (Handle != IntPtr.Zero)
                    DestroyHandle();
            }
        }
    }
}

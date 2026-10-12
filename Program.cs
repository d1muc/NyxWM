using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Diagnostics;
using nyxWM;

namespace nyxWM
{
    internal class Program
    {
        // Hotkey constants
        private const int MOD_CONTROL = 0x0002;
        private const int VK_R = 0x52;
        private const int VK_T = 0x54;
        internal const int WM_HOTKEY = 0x0312;

        // Hotkey functions
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [STAThread]
        static void Main(string[] args)
        {
            // Initialize mouse hook and window manager
            var mouseHook = new MouseHook();
            var windowManager = new WindowManager(mouseHook);

            // Get monitor refresh rate for timer setup
            int fallbackRefreshRate = ConfigManager.Current.FallbackRefreshRate;
            int refreshRate = MonitorUtils.GetPrimaryMonitorRefreshRate(fallbackRefreshRate);
            int intervalMs = Math.Max(1, (int)(1000.0 / refreshRate)); // Calculate interval for target refresh rate

            Console.WriteLine($"Setting up render timer for {refreshRate} Hz ({intervalMs} ms interval)");

            // Set up high-resolution timer for updating window positions
            long lastTimestamp = Stopwatch.GetTimestamp();
            var renderTimer = new HighResolutionTimer(() =>
            {
                // Calculate delta time
                long currentTimestamp = Stopwatch.GetTimestamp();
                float deltaTime = (float)(currentTimestamp - lastTimestamp) * 1000f / Stopwatch.Frequency;
                lastTimestamp = currentTimestamp;
                windowManager.UpdatePosition(deltaTime);
            }, intervalMs);
            renderTimer.Start(); // Start timer immediately and keep it running

            // Subscribe to mouse hook events
            mouseHook.OnDragStart += () => windowManager.StartDragging();
            mouseHook.OnDragStop += () => windowManager.StopDragging();

            // Register hotkeys from configuration
            RegisterConfiguredHotkeys();

            Application.AddMessageFilter(new HotkeyMessageFilter(windowManager));

            // Start the mouse hook
            mouseHook.Start();

            // Handle Ctrl+C to stop the hook and exit
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true; // Prevent immediate termination
                mouseHook.Stop();
                UnregisterHotKey(IntPtr.Zero, 0x0000); // Unregister reset camera hotkey
                UnregisterHotKey(IntPtr.Zero, 0x0001); // Unregister toggle auto-center hotkey
                renderTimer.Dispose(); // Clean up timer
                Application.Exit(); // Exit the message loop
            };

            // Run the application message loop
            Application.Run();
        }

        /// <summary>
        /// Registers hotkeys based on the configuration.
        /// </summary>
        private static void RegisterConfiguredHotkeys()
        {
            var config = ConfigManager.Current;

            // Reset camera hotkey
            uint resetCameraModifiers = ParseModifierString(config.Hotkeys.ResetCamera.Modifiers);
            uint resetCameraKey = ParseKeyString(config.Hotkeys.ResetCamera.Key);
            if (!RegisterHotKey(IntPtr.Zero, 0x0000, resetCameraModifiers, resetCameraKey))
            {
                Console.WriteLine("Failed to register reset camera hotkey.");
            }

            // Toggle auto-center hotkey
            uint toggleModifiers = ParseModifierString(config.Hotkeys.ToggleAutoCenter.Modifiers);
            uint toggleKey = ParseKeyString(config.Hotkeys.ToggleAutoCenter.Key);
            if (!RegisterHotKey(IntPtr.Zero, 0x0001, toggleModifiers, toggleKey))
            {
                Console.WriteLine("Failed to register toggle auto-center hotkey.");
            }
        }

        /// <summary>
        /// Converts a modifier string (e.g., "CONTROL+SHIFT") to the corresponding modifier flags.
        /// </summary>
        private static uint ParseModifierString(string modifierString)
        {
            uint result = 0;
            string[] parts = modifierString.Split('+', StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                string trimmed = part.Trim().ToUpperInvariant();
                switch (trimmed)
                {
                    case "CONTROL":
                        result |= 0x0002; // MOD_CONTROL
                        break;
                    case "ALT":
                        result |= 0x0001; // MOD_ALT
                        break;
                    case "SHIFT":
                        result |= 0x0004; // MOD_SHIFT
                        break;
                    case "WIN":
                    case "LWIN":
                    case "RWIN":
                        result |= 0x0008; // MOD_WIN
                        break;
                    default:
                        Console.WriteLine($"Unknown modifier part: {trimmed}");
                        break;
                }
            }
            return result;
        }

        /// <summary>
        /// Converts a key string (e.g., "A", "F1") to the corresponding virtual key code.
        /// </summary>
        private static uint ParseKeyString(string keyString)
        {
            if (string.IsNullOrEmpty(keyString))
                return 0;

            string key = keyString.Trim().ToUpperInvariant();

            // Handle single character keys (A-Z, 0-9)
            if (key.Length == 1)
            {
                char c = key[0];
                if (c >= 'A' && c <= 'Z')
                    return (uint)c;
                if (c >= '0' && c <= '9')
                    return (uint)c;
            }

            // Handle special keys
            switch (key)
            {
                case "F1": return 0x70;
                case "F2": return 0x71;
                case "F3": return 0x72;
                case "F4": return 0x73;
                case "F5": return 0x74;
                case "F6": return 0x75;
                case "F7": return 0x76;
                case "F8": return 0x77;
                case "F9": return 0x78;
                case "F10": return 0x79;
                case "F11": return 0x7A;
                case "F12": return 0x7B;
                case "LEFT": return 0x25;
                case "UP": return 0x26;
                case "RIGHT": return 0x27;
                case "DOWN": return 0x28;
                case "ENTER": return 0x0D;
                case "ESC": return 0x1B;
                case "SPACE": return 0x20;
                case "TAB": return 0x09;
                case "BACKSPACE": return 0x08;
                case "DELETE": return 0x2E;
                case "INSERT": return 0x2D;
                case "HOME": return 0x24;
                case "END": return 0x23;
                case "PAGEUP": return 0x21;
                case "PAGEDOWN": return 0x22;
                default:
                    Console.WriteLine($"Unknown key: {keyString}. Using 0.");
                    return 0;
            }
        }
    }

    /// <summary>
    /// Message filter to handle hotkey events.
    /// </summary>
    internal class HotkeyMessageFilter : IMessageFilter
    {
        private readonly WindowManager _windowManager;

        public HotkeyMessageFilter(WindowManager windowManager)
        {
            _windowManager = windowManager;
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg == Program.WM_HOTKEY)
            {
                int wParam = m.WParam.ToInt32();
                // Note: We don't have the actual modifier and key values here, but we can compare wParam to our registered IDs.
                // We registered two hotkeys with IDs 0x0000 and 0x0001.
                if (wParam == 0x0000) // Reset camera
                {
                    _windowManager.ResetCamera();
                    return true; // Message handled
                }
                else if (wParam == 0x0001) // Toggle auto-center-on-focus
                {
                    _windowManager.ToggleAutoCenter();
                    return true; // Message handled
                }
            }
            return false; // Message not handled
        }
    }
}
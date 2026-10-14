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
            string mainmod = config.MainMod;

            // Reset camera hotkey
            uint resetCameraModifiers;
            uint resetCameraKey;
            if (!ParseHotkeyString(config.Hotkeys.ResetCamera, mainmod, out resetCameraModifiers, out resetCameraKey))
            {
                Console.WriteLine("Invalid reset camera hotkey in config. Using default.");
                // Fallback to default: mainmod + space, but we know mainmod is valid from config validation
                ParseHotkeyString("mainmod + space", mainmod, out resetCameraModifiers, out resetCameraKey);
            }
            if (!RegisterHotKey(IntPtr.Zero, 0x0000, resetCameraModifiers, resetCameraKey))
            {
                Console.WriteLine("Failed to register reset camera hotkey.");
            }

            // Toggle auto-center hotkey
            uint toggleModifiers;
            uint toggleKey;
            if (!ParseHotkeyString(config.Hotkeys.ToggleAutoCenter, mainmod, out toggleModifiers, out toggleKey))
            {
                Console.WriteLine("Invalid toggle auto-center hotkey in config. Using default.");
                ParseHotkeyString("mainmod + t", mainmod, out toggleModifiers, out toggleKey);
            }
            if (!RegisterHotKey(IntPtr.Zero, 0x0001, toggleModifiers, toggleKey))
            {
                Console.WriteLine("Failed to register toggle auto-center hotkey.");
            }
        }

        /// <summary>
        /// Parses a hotkey string (e.g., "mainmod + space") into modifier flags and virtual key code.
        /// Replaces "mainmod" with the provided mainmod string.
        /// Returns true if successful, false otherwise.
        /// </summary>
        private static bool ParseHotkeyString(string hotkeyString, string mainmod, out uint modifiers, out uint key)
        {
            modifiers = 0;
            key = 0;

            if (string.IsNullOrWhiteSpace(hotkeyString))
                return false;

            // Replace mainmod placeholder (case-insensitive)
            string replaced = hotkeyString.Replace("mainmod", mainmod, StringComparison.InvariantCultureIgnoreCase);
            // Split by '+'
            string[] parts = replaced.Split('+', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return false;

            // The last part is the key, the rest are modifiers.
            string keyPart = parts[parts.Length - 1].Trim();
            if (string.IsNullOrEmpty(keyPart))
                return false;

            // Parse modifier parts
            for (int i = 0; i < parts.Length - 1; i++)
            {
                string modifier = parts[i].Trim().ToUpperInvariant();
                uint modFlag = ParseModifierString(modifier);
                if (modFlag == 0)
                    return false; // Invalid modifier
                modifiers |= modFlag;
            }

            // Parse key part
            key = ParseKeyString(keyPart);
            if (key == 0)
                return false; // Invalid key

            return true;
        }

        /// <summary>
        /// Converts a modifier string (e.g., "CONTROL+SHIFT") to the corresponding modifier flags.
        /// Note: This method expects a single modifier (no '+') and is used by ParseHotkeyString for each modifier part.
        /// </summary>
        private static uint ParseModifierString(string modifierString)
        {
            // This method now expects a single modifier (like "CONTROL") and returns the flag.
            // We keep the same logic as before but for a single part.
            string trimmed = modifierString.Trim().ToUpperInvariant();
            return trimmed switch
            {
                "CONTROL" => 0x0002, // MOD_CONTROL
                "ALT" => 0x0001, // MOD_ALT
                "SHIFT" => 0x0004, // MOD_SHIFT
                "WIN" or "LWIN" or "RWIN" => 0x0008, // MOD_WIN
                _ => 0 // Unknown modifier
            };
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
            return key switch
            {
                "F1" => 0x70,
                "F2" => 0x71,
                "F3" => 0x72,
                "F4" => 0x73,
                "F5" => 0x74,
                "F6" => 0x75,
                "F7" => 0x76,
                "F8" => 0x77,
                "F9" => 0x78,
                "F10" => 0x79,
                "F11" => 0x7A,
                "F12" => 0x7B,
                "LEFT" => 0x25,
                "UP" => 0x26,
                "RIGHT" => 0x27,
                "DOWN" => 0x28,
                "ENTER" => 0x0D,
                "ESC" => 0x1B,
                "SPACE" => 0x20,
                "TAB" => 0x09,
                "BACKSPACE" => 0x08,
                "DELETE" => 0x2E,
                "INSERT" => 0x2D,
                "HOME" => 0x24,
                "END" => 0x23,
                "PAGEUP" => 0x21,
                "PAGEDOWN" => 0x22,
                _ => 0 // Unknown key
            };
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
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
            // Initialize window manager and mouse hook
            var windowManager = new WindowManager();
            var mouseHook = new MouseHook();

            // Get monitor refresh rate for timer setup
            int refreshRate = MonitorUtils.GetPrimaryMonitorRefreshRate(165);
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

            // Register Ctrl+R hotkey for resetting camera
            RegisterHotKey(IntPtr.Zero, 0x0000, MOD_CONTROL, VK_R);
            // Register Ctrl+T hotkey for toggling auto-center-on-focus
            RegisterHotKey(IntPtr.Zero, 0x0001, MOD_CONTROL, VK_T);
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
                if (wParam == 0x0000) // Ctrl+R: Reset camera
                {
                    _windowManager.ResetCamera();
                    return true; // Message handled
                }
                else if (wParam == 0x0001) // Ctrl+T: Toggle auto-center-on-focus
                {
                    _windowManager.ToggleAutoCenter();
                    return true; // Message handled
                }
            }
            return false; // Message not handled
        }
    }
}

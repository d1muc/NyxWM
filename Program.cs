using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace nyxWM
{
    internal class Program
    {
        // Hotkey constants
        private const int MOD_CONTROL = 0x0002;
        private const int VK_R = 0x52;
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

            // Set up timer for updating window positions (runs constantly at 100 Hz)
            System.Windows.Forms.Timer renderTimer = new System.Windows.Forms.Timer { Interval = 10 }; // 10 ms = 100 Hz
            renderTimer.Tick += (s, e) => windowManager.UpdatePosition();
            renderTimer.Start(); // Start timer immediately and keep it running

            // Subscribe to mouse hook events
            mouseHook.OnDragStart += () => windowManager.StartDragging();
            mouseHook.OnDragStop += () => windowManager.StopDragging();

            // Register Ctrl+R hotkey for resetting camera
            RegisterHotKey(IntPtr.Zero, 0x0000, MOD_CONTROL, VK_R);
            Application.AddMessageFilter(new HotkeyMessageFilter(windowManager));

            // Start the mouse hook
            mouseHook.Start();

            // Handle Ctrl+C to stop the hook and exit
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true; // Prevent immediate termination
                mouseHook.Stop();
                UnregisterHotKey(IntPtr.Zero, 0x0000); // Unregister hotkey
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
            if (m.Msg == Program.WM_HOTKEY && m.WParam.ToInt32() == 0x0000) // Our hotkey ID
            {
                _windowManager.ResetCamera();
                return true; // Message handled
            }
            return false; // Message not handled
        }
    }
}

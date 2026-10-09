using System;
using System.Windows.Forms;

namespace nyxWM
{
    internal class Program
    {
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

            // Start the mouse hook
            mouseHook.Start();

            // Handle Ctrl+C to stop the hook and exit
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true; // Prevent immediate termination
                mouseHook.Stop();
                Application.Exit(); // Exit the message loop
            };

            // Run the application message loop
            Application.Run();
        }
    }
}

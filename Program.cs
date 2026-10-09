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

            // Set up timer for updating window positions
            System.Windows.Forms.Timer renderTimer = new System.Windows.Forms.Timer { Interval = 10 }; // 10 ms = 100 Hz
            renderTimer.Tick += (s, e) => windowManager.UpdatePosition();

            // Subscribe to mouse hook events
            mouseHook.OnDragStart += () =>
            {
                windowManager.StartDragging();
                renderTimer.Start();
            };
            mouseHook.OnDragStop += () =>
            {
                renderTimer.Stop();
            };

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

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

            // Subscribe to mouse drag events
            mouseHook.OnMouseDrag += (dx, dy) => windowManager.MoveAllWindows(dx, dy);

            // Start the mouse hook
            mouseHook.Start();

            // Handle Ctrl+C to stop the hook and exit
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true; // Prevent the process from terminating immediately
                mouseHook.Stop();
                Application.Exit(); // Exit the message loop
            };

            // Run the application message loop
            Application.Run();
        }
    }
}

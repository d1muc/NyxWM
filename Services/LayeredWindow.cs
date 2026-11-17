using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace nyxWM
{
    /// <summary>
    /// Pushes a 32-bit ARGB bitmap onto a window with per-pixel alpha via
    /// UpdateLayeredWindow. The result is truly translucent: rounded corners and
    /// antialiased edges keep their soft fringe instead of being cut to a
    /// pixel-aligned region mask. Call from the UI thread.
    /// </summary>
    internal static class LayeredWindow
    {
        internal const int WS_EX_LAYERED = 0x00080000;

        [StructLayout(LayoutKind.Sequential)]
        internal struct Point
        {
            public int X;
            public int Y;

            public Point(int x, int y)
            {
                X = x;
                Y = y;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Size
        {
            public int Width;
            public int Height;

            public Size(int width, int height)
            {
                Width = width;
                Height = height;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BlendFunction
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        private const byte AC_SRC_OVER = 0;
        private const byte AC_SRC_ALPHA = 1;
        private const uint ULW_ALPHA = 0x2;

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr window);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr window, IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr dc, IntPtr handle);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr handle);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(
            IntPtr hwnd,
            IntPtr destination,
            ref Point location,
            ref Size size,
            IntPtr source,
            ref Point sourcePoint,
            int colorKey,
            ref BlendFunction blend,
            uint flags);

        /// <summary>
        /// Copies <paramref name="bitmap"/> onto <paramref name="hwnd"/> at the
        /// given screen position, multiplied by <paramref name="opacity"/>.
        /// </summary>
        internal static void Apply(IntPtr hwnd, Bitmap? bitmap, int left, int top, float opacity)
        {
            if (hwnd == IntPtr.Zero || bitmap == null)
                return;

            byte alpha = (byte)Math.Clamp((int)MathF.Round(opacity * 255f), 0, 255);

            IntPtr memory = CreateCompatibleDC(IntPtr.Zero);

            if (memory == IntPtr.Zero)
                return;

            IntPtr bitmapHandle = IntPtr.Zero;
            IntPtr previous;

            try
            {
                bitmapHandle = bitmap.GetHbitmap(Color.FromArgb(0));
                previous = SelectObject(memory, bitmapHandle);

                IntPtr screen = GetDC(IntPtr.Zero);

                if (screen == IntPtr.Zero)
                    return;

                try
                {
                    var location = new Point(left, top);
                    var size = new Size(bitmap.Width, bitmap.Height);
                    var sourcePoint = new Point(0, 0);
                    var blend = new BlendFunction
                    {
                        BlendOp = AC_SRC_OVER,
                        BlendFlags = 0,
                        SourceConstantAlpha = alpha,
                        AlphaFormat = AC_SRC_ALPHA
                    };

                    UpdateLayeredWindow(
                        hwnd,
                        screen,
                        ref location,
                        ref size,
                        memory,
                        ref sourcePoint,
                        0,
                        ref blend,
                        ULW_ALPHA);
                }
                finally
                {
                    ReleaseDC(IntPtr.Zero, screen);
                }

                SelectObject(memory, previous);
            }
            finally
            {
                if (bitmapHandle != IntPtr.Zero)
                    DeleteObject(bitmapHandle);

                DeleteDC(memory);
            }
        }
    }
}
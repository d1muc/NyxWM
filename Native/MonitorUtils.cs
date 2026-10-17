using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace nyxWM
{
    /// <summary>
    /// Utilities for monitor/display information, including refresh rate detection.
    /// </summary>
    public static class MonitorUtils
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct DEVMODE
        {
            private const int CCHDEVICENAME = 32;
            private const int CCHFORMNAME = 32;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHFORMNAME)]
            public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }

        [DllImport("user32.dll")]
        private static extern int EnumDisplaySettings(IntPtr lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

        private const int ENUM_CURRENT_SETTINGS = -1;
        private const int ENUM_REGISTRY_SETTINGS = -2;

        /// <summary>
        /// Gets the refresh rate (in Hz) of the primary monitor.
        /// Returns a safe fallback value if detection fails.
        /// </summary>
        public static int GetPrimaryMonitorRefreshRate(int fallbackHz = 165)
        {
            try
            {
                DEVMODE dm = new DEVMODE();
                // Fix CS0266: Explicitly cast int to short
                dm.dmSize = (short)Marshal.SizeOf<DEVMODE>();

                if (EnumDisplaySettings(IntPtr.Zero, ENUM_CURRENT_SETTINGS, ref dm) != 0)
                {
                    int refreshRate = dm.dmDisplayFrequency;
                    if (refreshRate >= 30 && refreshRate <= 360) // Reasonable range for monitor refresh rates
                    {
                        return refreshRate;
                    }
                }
            }
            catch (Exception)
            {
                // Ignore exceptions and use fallback
            }

            // Return fallback value if detection failed or returned invalid value
            return fallbackHz;
        }

        /// <summary>
        /// Gets the bounds of the primary monitor.
        /// Falls back to System.Windows.Forms.Screen if needed.
        /// </summary>
        public static Rectangle GetPrimaryMonitorBounds()
        {
            try
            {
                // Fix CS8625/CS8602: Handle possible null return from PrimaryScreen
                var primaryScreen = System.Windows.Forms.Screen.PrimaryScreen;
                if (primaryScreen != null)
                {
                    return primaryScreen.Bounds;
                }
            }
            catch
            {
                // Ignore exceptions and use fallback
            }

            // Ultimate fallback
            return new Rectangle(0, 0, 1920, 1080);
        }

        /// <summary>
        /// Gets the center point of the primary monitor.
        /// </>
        public static Point GetPrimaryMonitorCenter()
        {
            Rectangle bounds = GetPrimaryMonitorBounds();
            return new Point(
                bounds.X + bounds.Width / 2,
                bounds.Y + bounds.Height / 2
            );
        }
    }
}
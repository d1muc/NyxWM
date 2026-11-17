using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace nyxWM.Nx
{
    /// <summary>
    /// Machine and session data: locale, region, hardware load, battery and
    /// display metrics. Expensive probes are sampled on an interval and cached,
    /// so a document can bind "{sys.cpu}" without paying for it every frame.
    /// </summary>
    internal sealed class SystemProvider : NxProviderBase
    {
        private const double FastRefreshMs = 1000.0;
        private const double SlowRefreshMs = 15000.0;

        private static readonly string[] KeyNames =
        {
            "language", "lang", "region", "country", "culture", "keyboardLayout",
            "cpu", "mem", "memused", "memfree", "memtotal",
            "battery", "batteryState", "acPower",
            "screenwidth", "screenheight", "refreshRate", "dpi", "scale",
            "hostname", "username", "processes", "processCount", "appname",
            "monitorcount", "idle"
        };

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public uint BatteryLifeTime;
            public uint BatteryFullLifeTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        {
            public uint Low;
            public uint High;

            public ulong ToUInt64() => ((ulong)High << 32) | Low;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

        [DllImport("user32.dll")]
        private static extern int GetKeyboardLayout(uint threadId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        private readonly object _sync = new();

        private double _fastAccumulator = FastRefreshMs;
        private double _slowAccumulator = SlowRefreshMs;

        private double _cpuPercent;
        private ulong _memTotalMb;
        private ulong _memFreeMb;
        private uint _memLoad;
        private int _batteryPercent = -1;
        private bool _acPower = true;
        private ulong _previousIdle;
        private ulong _previousKernel;
        private ulong _previousUser;
        private bool _cpuSeeded;

        private string _hostName = string.Empty;
        private string _userName = string.Empty;
        private int _processCount;

        /// <summary>Samples load averages and identity strings when their interval elapses.</summary>
        internal void Update(double deltaMs)
        {
            lock (_sync)
            {
                _fastAccumulator += deltaMs;
                _slowAccumulator += deltaMs;

                if (_fastAccumulator >= FastRefreshMs)
                {
                    _fastAccumulator = 0.0;
                    SampleCpu();
                    SampleMemory();
                    SamplePower();
                }

                if (_slowAccumulator >= SlowRefreshMs)
                {
                    _slowAccumulator = 0.0;
                    SampleIdentity();
                }
            }
        }

        public override string Namespace => "sys";

        public override IEnumerable<string> Keys => KeyNames;

        public override NxValue? Read(string key, string? argument)
        {
            switch (key.ToLowerInvariant())
            {
                case "language":
                case "lang":
                    return Text(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
                case "region":
                case "country":
                    return Text(RegionInfo.CurrentRegion.TwoLetterISORegionName);
                case "culture":
                    return Text(CultureInfo.CurrentCulture.Name);
                case "keyboardlayout":
                    return Text(CurrentKeyboardLayout());

                case "cpu":
                    lock (_sync) return Number(Math.Round(_cpuPercent, 1));
                case "mem":
                    lock (_sync) return Number(_memLoad);
                case "memused":
                    lock (_sync) return Number(_memTotalMb - _memFreeMb);
                case "memfree":
                    lock (_sync) return Number(_memFreeMb);
                case "memtotal":
                    lock (_sync) return Number(_memTotalMb);

                case "battery":
                    lock (_sync) return _batteryPercent < 0 ? Empty : Number(_batteryPercent);
                case "batterystate":
                    lock (_sync) return Text(BatteryStateText(_batteryPercent, _acPower));
                case "acpower":
                    lock (_sync) return Flag(_acPower);

                case "screenwidth":
                    return Number(Screen.PrimaryScreen?.Bounds.Width ?? 0);
                case "screenheight":
                    return Number(Screen.PrimaryScreen?.Bounds.Height ?? 0);
                case "refreshRate":
                case "refreshrate":
                    return Number(MonitorUtils.GetPrimaryMonitorRefreshRate(
                        ConfigManager.Current.FallbackRefreshRate));
                case "dpi":
                    return Number(CurrentDpi());
                case "scale":
                    return Number(Math.Round(CurrentDpi() / 96.0, 2));
                case "monitorcount":
                    return Number(Screen.AllScreens.Length);

                case "hostname":
                    lock (_sync) return Text(_hostName);
                case "username":
                    lock (_sync) return Text(_userName);
                case "processes":
                case "processcount":
                    lock (_sync) return Number(_processCount);
                case "appname":
                    return Text("nyxWM");

                case "idle":
                    return Number(IdleSeconds());

                default:
                    return null;
            }
        }

        private void SampleCpu()
        {
            if (!GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user))
                return;

            ulong idleTicks = idle.ToUInt64();
            ulong kernelTicks = kernel.ToUInt64();
            ulong userTicks = user.ToUInt64();

            if (!_cpuSeeded)
            {
                _previousIdle = idleTicks;
                _previousKernel = kernelTicks;
                _previousUser = userTicks;
                _cpuSeeded = true;
                return;
            }

            ulong idleDelta = idleTicks - _previousIdle;
            ulong kernelDelta = kernelTicks - _previousKernel;
            ulong userDelta = userTicks - _previousUser;

            // Kernel time includes idle time on Windows.
            ulong totalDelta = kernelDelta + userDelta;

            if (totalDelta > 0)
                _cpuPercent = 100.0 * (totalDelta - idleDelta) / totalDelta;

            _previousIdle = idleTicks;
            _previousKernel = kernelTicks;
            _previousUser = userTicks;
        }

        private void SampleMemory()
        {
            var status = new MEMORYSTATUSEX
            {
                dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>()
            };

            if (!GlobalMemoryStatusEx(ref status))
                return;

            _memLoad = status.dwMemoryLoad;
            _memTotalMb = status.ullTotalPhys / (1024 * 1024);
            _memFreeMb = status.ullAvailPhys / (1024 * 1024);
        }

        private void SamplePower()
        {
            if (!GetSystemPowerStatus(out SYSTEM_POWER_STATUS status))
                return;

            _acPower = status.ACLineStatus == 1;
            _batteryPercent = status.BatteryLifePercent == 255 ? -1 : status.BatteryLifePercent;
        }

        private void SampleIdentity()
        {
            try
            {
                _hostName = Environment.MachineName;
                _userName = Environment.UserName;
                _processCount = Process.GetProcesses().Length;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SystemProvider: identity probe failed: {ex.Message}");
            }
        }

        private static string BatteryStateText(int percent, bool acPower)
        {
            if (percent < 0)
                return acPower ? "AC" : "Unknown";

            return acPower ? "Charging" : "Discharging";
        }

        private static string CurrentKeyboardLayout()
        {
            try
            {
                int layout = GetKeyboardLayout(GetCurrentThreadId());
                int languageId = layout & 0xFFFF;

                return new CultureInfo(languageId).TwoLetterISOLanguageName;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SystemProvider: keyboard layout probe failed: {ex.Message}");
                return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            }
        }

        private static double CurrentDpi()
        {
            try
            {
                using var graphics = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
                return Math.Round(graphics.DpiX);
            }
            catch (Exception)
            {
                return 96.0;
            }
        }

        private static double IdleSeconds()
        {
            try
            {
                var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };

                if (!GetLastInputInfo(ref info))
                    return 0.0;

                uint now = (uint)Environment.TickCount;
                return Math.Max(0.0, (now - info.dwTime) / 1000.0);
            }
            catch (Exception)
            {
                return 0.0;
            }
        }
    }
}

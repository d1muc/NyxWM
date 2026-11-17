using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace nyxWM.Nx
{
    /// <summary>
    /// Window data: what is open, what has focus, what is minimised and which
    /// monitor a window sits on. Enumeration is cached because walking the whole
    /// desktop is far too expensive to do per frame.
    /// </summary>
    internal sealed class WindowProvider : NxProviderBase
    {
        private const double RefreshIntervalMs = 500.0;
        private const int MaxWindows = 64;

        private static readonly string[] KeyNames =
        {
            "count", "visible", "minimized", "foreground", "foregroundClass",
            "foregroundProcess", "foregroundPid", "list", "minimizedList",
            "title", "class", "process", "pid", "monitor", "hasFullscreen"
        };

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT point, uint flags);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr parameter);

        private readonly object _sync = new();
        private readonly StringBuilder _titleBuffer = new(256);
        private readonly StringBuilder _classBuffer = new(256);
        private readonly List<WindowEntry> _entries = new(MaxWindows);
        private readonly Dictionary<string, string> _processNames = new(StringComparer.Ordinal);

        private double _accumulator = RefreshIntervalMs;
        private bool _hasFullscreen;

        /// <summary>Rescans the desktop when the interval elapses.</summary>
        internal void Update(double deltaMs)
        {
            _accumulator += deltaMs;

            if (_accumulator < RefreshIntervalMs)
                return;

            _accumulator = 0.0;
            Refresh();
        }

        public override string Namespace => "win";

        public override IEnumerable<string> Keys => KeyNames;

        public override NxValue? Read(string key, string? argument)
        {
            lock (_sync)
            {
                switch (key.ToLowerInvariant())
                {
                    case "count":
                        return Number(_entries.Count);
                    case "visible":
                        return Number(CountMatching(entry => true));
                    case "minimized":
                        return Number(CountMatching(entry => entry.Minimized));
                    case "foreground":
                        return Text(_entries.Count > 0 ? _entries[0].Title : string.Empty);
                    case "foregroundclass":
                        return Text(_entries.Count > 0 ? _entries[0].ClassName : string.Empty);
                    case "foregroundprocess":
                        return Text(_entries.Count > 0 ? _entries[0].ProcessName : string.Empty);
                    case "foregroundpid":
                        return Number(_entries.Count > 0 ? _entries[0].ProcessId : 0);
                    case "list":
                        return Text(JoinTitles(entry => !entry.Minimized));
                    case "minimizedlist":
                        return Text(JoinTitles(entry => entry.Minimized));
                    case "hasfullscreen":
                        return Flag(_hasFullscreen);
                    case "title":
                        return FindByArgument(argument) is { } titleEntry ? Text(titleEntry.Title) : Empty;
                    case "class":
                        return FindByArgument(argument) is { } classEntry ? Text(classEntry.ClassName) : Empty;
                    case "process":
                        return FindByArgument(argument) is { } processEntry ? Text(processEntry.ProcessName) : Empty;
                    case "pid":
                        return FindByArgument(argument) is { } pidEntry ? Number(pidEntry.ProcessId) : Number(0);
                    case "monitor":
                        return FindByArgument(argument) is { } monitorEntry ? Number(monitorEntry.MonitorIndex) : Number(0);
                    default:
                        return null;
                }
            }
        }

        private WindowEntry? FindByArgument(string? argument)
        {
            if (string.IsNullOrWhiteSpace(argument))
                return _entries.Count > 0 ? _entries[0] : null;

            string needle = argument.Trim();

            // "3" addresses a slot, anything else matches the process or class name.
            if (int.TryParse(needle, out int index) && index >= 0 && index < _entries.Count)
                return _entries[index];

            foreach (WindowEntry entry in _entries)
            {
                if (entry.ProcessName.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                    entry.ClassName.Contains(needle, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            return null;
        }

        private int CountMatching(Func<WindowEntry, bool> predicate)
        {
            int count = 0;

            foreach (WindowEntry entry in _entries)
            {
                if (predicate(entry))
                    count++;
            }

            return count;
        }

        private string JoinTitles(Func<WindowEntry, bool> predicate)
        {
            var builder = new StringBuilder();
            int written = 0;

            foreach (WindowEntry entry in _entries)
            {
                if (!predicate(entry) || entry.Title.Length == 0)
                    continue;

                if (written > 0)
                    builder.Append(", ");

                builder.Append(entry.Title);
                written++;

                if (written >= 6)
                    break;
            }

            return builder.ToString();
        }

        private void Refresh()
        {
            var found = new List<WindowEntry>(MaxWindows);
            bool fullscreen = false;

            try
            {
                IntPtr foreground = GetForegroundWindow();

                EnumWindows((hWnd, _) =>
                {
                    if (found.Count >= MaxWindows)
                        return false;

                    if (!IsWindowVisible(hWnd))
                        return true;

                    bool minimized = IsIconic(hWnd);

                    if (!GetWindowRect(hWnd, out RECT rect))
                        return true;

                    int width = rect.Right - rect.Left;
                    int height = rect.Bottom - rect.Top;

                    if (width <= 0 || height <= 0)
                        return true;

                    _titleBuffer.Clear();
                    GetWindowText(hWnd, _titleBuffer, _titleBuffer.Capacity);
                    string title = _titleBuffer.ToString();

                    _classBuffer.Clear();
                    GetClassName(hWnd, _classBuffer, _classBuffer.Capacity);
                    string className = _classBuffer.ToString();

                    // Skip the shell surfaces that are always present.
                    if (IsShellWindow(className))
                        return true;

                    GetWindowThreadProcessId(hWnd, out uint processId);
                    string processName = ResolveProcessName(processId);

                    if (!minimized && hWnd == foreground)
                    {
                        var primary = System.Windows.Forms.Screen.PrimaryScreen;

                        if (primary != null &&
                            width >= primary.Bounds.Width && height >= primary.Bounds.Height)
                        {
                            fullscreen = true;
                        }
                    }

                    found.Add(new WindowEntry(
                        hWnd,
                        title,
                        className,
                        processName,
                        processId,
                        minimized,
                        MonitorIndexFor(hWnd)));

                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WindowProvider: enumeration failed: {ex.Message}");
            }

            // The focused window leads the list so "{win.foreground}" is stable.
            found.Sort((left, right) =>
            {
                bool leftFocused = left.Handle == GetForegroundWindow();
                bool rightFocused = right.Handle == GetForegroundWindow();

                if (leftFocused == rightFocused)
                    return 0;

                return leftFocused ? -1 : 1;
            });

            lock (_sync)
            {
                _entries.Clear();
                _entries.AddRange(found);
                _hasFullscreen = fullscreen;
            }
        }

        private static int MonitorIndexFor(IntPtr hWnd)
        {
            try
            {
                IntPtr monitor = MonitorFromWindow(hWnd, 2); // MONITOR_DEFAULTTONEAREST
                var screens = System.Windows.Forms.Screen.AllScreens;

                for (int i = 0; i < screens.Length; i++)
                {
                    // Screen exposes no HMONITOR, so resolve each screen's origin
                    // back to a monitor handle and compare that instead.
                    var origin = new POINT
                    {
                        X = screens[i].Bounds.Left,
                        Y = screens[i].Bounds.Top
                    };

                    if (MonitorFromPoint(origin, 2) == monitor)
                        return i;
                }
            }
            catch (Exception)
            {
                // Fall through to the primary monitor.
            }

            return 0;
        }

        private string ResolveProcessName(uint processId)
        {
            string key = processId.ToString();

            if (_processNames.TryGetValue(key, out string? cached))
                return cached;

            string name = string.Empty;

            try
            {
                using Process process = Process.GetProcessById((int)processId);
                name = process.ProcessName;
            }
            catch (Exception)
            {
                // Protected or exiting processes have no readable name.
            }

            _processNames[key] = name;
            return name;
        }

        private static bool IsShellWindow(string className)
        {
            return className switch
            {
                "Shell_TrayWnd" or "Progman" or "WorkerW" or "Windows.UI.Core.CoreWindow" => true,
                _ => false
            };
        }

        private readonly struct WindowEntry
        {
            internal IntPtr Handle { get; }
            internal string Title { get; }
            internal string ClassName { get; }
            internal string ProcessName { get; }
            internal uint ProcessId { get; }
            internal bool Minimized { get; }
            internal int MonitorIndex { get; }

            internal WindowEntry(
                IntPtr handle,
                string title,
                string className,
                string processName,
                uint processId,
                bool minimized,
                int monitorIndex)
            {
                Handle = handle;
                Title = string.IsNullOrWhiteSpace(title) ? className : title;
                ClassName = className;
                ProcessName = processName;
                ProcessId = processId;
                Minimized = minimized;
                MonitorIndex = monitorIndex;
            }
        }
    }
}

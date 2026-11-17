using System;
using System.Collections.Generic;
using System.Globalization;

namespace nyxWM.Nx
{
    /// <summary>
    /// Every value a .nx document can read, addressed as "namespace.key" with an
    /// optional argument: "{time.hh}", "{win.title(3)}", "{media.progress}".
    ///
    /// The registry owns the providers and drives their sampling from one place,
    /// so twenty widgets reading "{sys.cpu}" still cost one probe per interval.
    /// </summary>
    internal sealed class NxDataRegistry : IDisposable
    {
        private readonly Dictionary<string, INxDataProvider> _providers =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly TimeProvider _time;
        private readonly SystemProvider _system;
        private readonly WindowProvider _windows;
        private readonly MediaProvider _media;

        private bool _disposed;

        internal NxDataRegistry()
        {
            _time = new TimeProvider(CultureInfo.CurrentCulture, MachineUptime);
            _system = new SystemProvider();
            _windows = new WindowProvider();
            _media = new MediaProvider();

            _providers[_time.Namespace] = _time;
            _providers[_system.Namespace] = _system;
            _providers[_windows.Namespace] = _windows;
            _providers[_media.Namespace] = _media;

            // The media poller owns a background task, so it starts once here
            // rather than on first read.
            _media.Start();
        }

        /// <summary>Namespaces a document may bind, for diagnostics and documentation.</summary>
        internal IEnumerable<string> Namespaces => _providers.Keys;

        internal IEnumerable<INxDataProvider> Providers => _providers.Values;

        /// <summary>
        /// Advances every provider's sampling clock. Called once per frame by the
        /// runtime; providers decide internally whether this frame is their turn.
        /// </summary>
        internal void Update(double deltaMs)
        {
            if (_disposed)
                return;

            _time.Update(deltaMs);
            _system.Update(deltaMs);
            _windows.Update(deltaMs);
            _media.Update(deltaMs);
        }

        /// <summary>
        /// Resolves "namespace.key" or "namespace.key(argument)". Returns null for
        /// an unknown path so a typo renders as empty text instead of crashing the
        /// surface.
        /// </summary>
        internal NxValue? Resolve(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            string trimmed = path.Trim();
            string? argument = null;

            int open = trimmed.IndexOf('(');

            if (open >= 0)
            {
                int close = trimmed.LastIndexOf(')');

                if (close < open)
                    return null;

                argument = trimmed[(open + 1)..close].Trim().Trim('"', '\'');
                trimmed = trimmed[..open].Trim();
            }

            int dot = trimmed.IndexOf('.');

            if (dot <= 0 || dot == trimmed.Length - 1)
                return null;

            string ns = trimmed[..dot];
            string key = trimmed[(dot + 1)..];

            if (!_providers.TryGetValue(ns, out INxDataProvider? provider))
                return null;

            try
            {
                return provider.Read(key, argument);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"NxDataRegistry: '{path}' failed: {ex.Message}");
                return null;
            }
        }

        private static TimeSpan MachineUptime()
        {
            try
            {
                return TimeSpan.FromMilliseconds(Environment.TickCount64);
            }
            catch (Exception)
            {
                return TimeSpan.Zero;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _media.Dispose();
        }
    }
}

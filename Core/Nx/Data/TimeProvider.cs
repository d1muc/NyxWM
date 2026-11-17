using System;
using System.Collections.Generic;
using System.Globalization;

namespace nyxWM.Nx
{
    /// <summary>
    /// Date and time bindings. Every key is read from a single cached snapshot so
    /// a document that shows "{time.hh}:{time.mm}" and "{date.day}" in one frame
    /// can never straddle a minute or midnight boundary.
    /// </summary>
    internal sealed class TimeProvider : NxProviderBase
    {
        private const double RefreshIntervalMs = 50.0;

        private static readonly string[] KeyNames =
        {
            "hh", "mm", "ss", "hms", "hm", "hour12", "hour", "minute", "second",
            "ampm", "year", "month", "day", "weekday", "monthname", "weekdayname",
            "dayofyear", "week", "unix", "date", "iso", "timezone", "utc",
            "uptime"
        };

        private readonly CultureInfo _culture;
        private readonly Func<TimeSpan> _uptime;
        private readonly object _sync = new();

        private DateTime _stamp;
        private TimeSpan _uptimeCache;
        private double _sampledAt = double.NaN;

        internal TimeProvider(CultureInfo? culture, Func<TimeSpan> uptime)
        {
            _culture = culture ?? CultureInfo.CurrentCulture;
            _uptime = uptime ?? (() => TimeSpan.Zero);
        }

        public override string Namespace => "time";

        public override IEnumerable<string> Keys => KeyNames;

        /// <summary>
        /// Advances the cached clock when its refresh interval has elapsed.
        /// Called by the runtime with the frame delta in milliseconds.
        /// </summary>
        internal void Update(double deltaMs)
        {
            _sampledAt += deltaMs;

            if (!double.IsNaN(_sampledAt) && _sampledAt < RefreshIntervalMs)
                return;

            _sampledAt = 0.0;

            lock (_sync)
            {
                // Keep a stored wall clock so every key reads one consistent instant.
                _stamp = _stamp == default ? DateTime.Now : _stamp.AddMilliseconds(Math.Max(0.0, deltaMs));

                // If the machine clock or the process was suspended, trust DateTime.Now.
                if (Math.Abs((DateTime.Now - _stamp).TotalMinutes) > 5.0)
                    _stamp = DateTime.Now;

                _uptimeCache = _uptime();
            }
        }

        public override NxValue? Read(string key, string? argument)
        {
            DateTime now;
            TimeSpan uptime;

            lock (_sync)
            {
                now = _stamp == default ? DateTime.Now : _stamp;
                uptime = _uptimeCache;
            }

            string normalized = key.ToLowerInvariant();
            bool twentyFour = string.Equals(argument, "24", StringComparison.OrdinalIgnoreCase);

            switch (normalized)
            {
                case "hh":
                    return Text(FormatHour(now, twentyFour, withLeadingZero: true));
                case "mm":
                    return Text(now.ToString("mm", CultureInfo.InvariantCulture));
                case "ss":
                    return Text(now.ToString("ss", CultureInfo.InvariantCulture));
                case "hm":
                    return Text($"{FormatHour(now, twentyFour, true)}:{now:mm}");
                case "hms":
                    return Text($"{FormatHour(now, twentyFour, true)}:{now:mm}:{now:ss}");
                case "hour":
                    return Number(now.Hour);
                case "hour12":
                    return Number(now.Hour % 12 == 0 ? 12 : now.Hour % 12);
                case "minute":
                    return Number(now.Minute);
                case "second":
                    return Number(now.Second);
                case "ampm":
                    return Text(now.Hour < 12 ? "AM" : "PM");
                case "year":
                    return Text(now.Year.ToString(CultureInfo.InvariantCulture));
                case "month":
                    return Text(now.Month.ToString("00", CultureInfo.InvariantCulture));
                case "day":
                    return Text(now.Day.ToString("00", CultureInfo.InvariantCulture));
                case "weekday":
                    return Text(((int)now.DayOfWeek).ToString(CultureInfo.InvariantCulture));
                case "monthname":
                    return Text(now.ToString("MMMM", _culture));
                case "weekdayname":
                    return Text(now.ToString("dddd", _culture));
                case "dayofyear":
                    return Number(now.DayOfYear);
                case "week":
                    return Number(ISOWeek.GetWeekOfYear(now));
                case "unix":
                    return Number(new DateTimeOffset(now).ToUnixTimeSeconds());
                case "date":
                    return Text(now.ToString("d", _culture));
                case "iso":
                    return Text(now.ToString("s", CultureInfo.InvariantCulture));
                case "timezone":
                    return Text(TimeZoneInfo.Local.StandardName);
                case "uttc":
                case "utc":
                    return Text(DateTime.UtcNow.ToString("HH:mm", CultureInfo.InvariantCulture));
                case "uptime":
                    return Text(FormatUptime(uptime));
                default:
                    return null;
            }
        }

        private static string FormatHour(DateTime value, bool twentyFour, bool withLeadingZero)
        {
            int hour = twentyFour ? value.Hour : value.Hour % 12;

            if (!twentyFour && hour == 0)
                hour = 12;

            return withLeadingZero
                ? hour.ToString("00", CultureInfo.InvariantCulture)
                : hour.ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatUptime(TimeSpan value)
        {
            return value.TotalHours < 1.0
                ? $"{(int)value.TotalMinutes}m"
                : $"{(int)value.TotalHours}h {value.Minutes:00}m";
        }
    }
}

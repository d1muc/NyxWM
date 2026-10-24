using System;
using System.Globalization;

namespace nyxWM
{
    /// <summary>
    /// Detects keyboard layout changes of the foreground window by polling its HKL.
    /// The first poll only records the baseline and never raises a change.
    /// </summary>
    internal sealed class KeyboardLanguageWatcher
    {
        private IntPtr _lastLayout;
        private string? _lastName;

        /// <summary>
        /// Polls the current foreground keyboard layout.
        /// Returns the display name when the layout changed, otherwise null.
        /// </summary>
        internal string? Poll()
        {
            IntPtr layout = GetForegroundLayout();
            if (layout == IntPtr.Zero)
                return null;

            if (_lastLayout == IntPtr.Zero)
            {
                _lastLayout = layout;
                _lastName = Describe(layout);
                return null;
            }

            if (layout == _lastLayout)
                return null;

            _lastLayout = layout;
            string name = Describe(layout);

            if (string.Equals(name, _lastName, StringComparison.Ordinal))
                return null;

            _lastName = name;
            return name;
        }

        private static IntPtr GetForegroundLayout()
        {
            IntPtr foreground = WinApi.GetForegroundWindow();

            uint threadId = foreground == IntPtr.Zero
                ? 0u
                : WinApi.GetWindowThreadProcessId(foreground, out _);

            IntPtr layout = WinApi.GetKeyboardLayout(threadId);

            // Fall back to the calling thread's layout when the foreground thread is unavailable.
            if (layout == IntPtr.Zero)
            {
                layout = WinApi.GetKeyboardLayout(0);
            }

            return layout;
        }

        private static string Describe(IntPtr layout)
        {
            int languageId = (int)((long)layout & 0xFFFF);

            try
            {
                CultureInfo culture = CultureInfo.GetCultureInfo(languageId);
                string iso = culture.TwoLetterISOLanguageName;
                string code = string.Equals(iso, "iv", StringComparison.OrdinalIgnoreCase)
                    ? languageId.ToString("X4")
                    : iso.ToUpperInvariant();

                return $"{code} · {culture.NativeName}";
            }
            catch (CultureNotFoundException)
            {
                return $"0x{languageId:X4}";
            }
            catch (ArgumentException)
            {
                return $"0x{languageId:X4}";
            }
        }
    }
}

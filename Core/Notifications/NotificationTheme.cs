using System;
using System.Drawing;

namespace nyxWM
{
    /// <summary>
    /// Resolved visual palette: accent, surface, text and field colours for the
    /// dark or light UI mode. Every window reads its colours from here, so one
    /// theme change reaches the settings window, the Rofi menu, the launcher and
    /// notifications.
    /// </summary>
    internal sealed class NotificationTheme
    {
        internal string Name { get; }
        internal Color Accent { get; }
        internal Color BackgroundTop { get; }
        internal Color BackgroundBottom { get; }
        internal Color Border { get; }
        internal Color TextPrimary { get; }
        internal Color TextMuted { get; }
        internal Color OnAccent { get; }
        internal Color FieldBackground { get; }
        internal Color FieldBorder { get; }
        internal Color Track { get; }
        internal Color Knob { get; }
        internal Color CardSurface { get; }
        internal Color CardBorder { get; }
        internal Color Overlay { get; }
        internal bool IsLight { get; }

        private NotificationTheme(
            string name,
            Color accent,
            bool isLight,
            Color backgroundTop,
            Color backgroundBottom,
            Color border,
            Color textPrimary,
            Color textMuted,
            Color onAccent,
            Color fieldBackground,
            Color fieldBorder,
            Color track,
            Color knob,
            Color cardSurface,
            Color cardBorder,
            Color overlay)
        {
            Name = name;
            Accent = accent;
            IsLight = isLight;
            BackgroundTop = backgroundTop;
            BackgroundBottom = backgroundBottom;
            Border = border;
            TextPrimary = textPrimary;
            TextMuted = textMuted;
            OnAccent = onAccent;
            FieldBackground = fieldBackground;
            FieldBorder = fieldBorder;
            Track = track;
            Knob = knob;
            CardSurface = cardSurface;
            CardBorder = cardBorder;
            Overlay = overlay;
        }

        internal static NotificationTheme Create(string name, Color accent, bool isLight)
        {
            if (isLight)
            {
                var top = Color.FromArgb(244, 245, 247);
                var bottom = Color.FromArgb(232, 234, 239);

                return new NotificationTheme(
                    name,
                    accent,
                    true,
                    Blend(top, accent, 0.020f),
                    Blend(bottom, accent, 0.015f),
                    Color.FromArgb(48, accent.R, accent.G, accent.B),
                    Color.FromArgb(20, 22, 28),
                    Color.FromArgb(98, 105, 118),
                    OnAccentFor(accent),
                    Color.FromArgb(255, 255, 255),
                    Color.FromArgb(40, 20, 22, 28),
                    Color.FromArgb(203, 208, 218),
                    Color.FromArgb(255, 255, 255),
                    Color.FromArgb(255, 255, 255),
                    Color.FromArgb(26, 20, 22, 28),
                    Color.Black);
            }

            var darkTop = Color.FromArgb(27, 29, 36);
            var darkBottom = Color.FromArgb(16, 17, 22);

            return new NotificationTheme(
                name,
                accent,
                false,
                Blend(darkTop, accent, 0.10f),
                Blend(darkBottom, accent, 0.06f),
                Color.FromArgb(56, accent.R, accent.G, accent.B),
                Color.FromArgb(238, 241, 248),
                Color.FromArgb(160, 166, 178),
                OnAccentFor(accent),
                Color.FromArgb(33, 35, 44),
                Color.FromArgb(58, 235, 238, 245),
                Color.FromArgb(62, 66, 78),
                Color.FromArgb(242, 244, 250),
                Color.FromArgb(22, 24, 31),
                Color.FromArgb(30, 255, 255, 255),
                Color.White);
        }

        private static Color OnAccentFor(Color accent)
        {
            double luminance = (0.2126 * accent.R + 0.7152 * accent.G + 0.0722 * accent.B) / 255.0;

            return luminance > 0.58
                ? Color.FromArgb(18, 20, 26)
                : Color.FromArgb(250, 251, 253);
        }

        private static Color Blend(Color background, Color tint, float amount)
        {
            float clamped = Math.Clamp(amount, 0f, 1f);

            return Color.FromArgb(
                background.A,
                (int)MathF.Round(background.R + (tint.R - background.R) * clamped),
                (int)MathF.Round(background.G + (tint.G - background.G) * clamped),
                (int)MathF.Round(background.B + (tint.B - background.B) * clamped));
        }
    }

    /// <summary>
    /// Resolves the "Theme" accent and "ThemeMode" values into a palette.
    /// Accents: White, Black, Red, Blue (default), Orange, Green, Purple,
    /// Cyan and Pink. Legacy names are migrated onto the nearest accent.
    /// Unknown values fall back to Blue.
    /// </summary>
    internal static class NotificationThemeResolver
    {
        internal const string DefaultAccentName = "Blue";

        internal static NotificationTheme Resolve(string? value, string? mode = null)
        {
            TryMap(Normalize(value), out string name, out Color accent);
            return NotificationTheme.Create(
                name,
                accent,
                VisualPreferences.NormalizeMode(mode) == VisualPreferences.ModeLight);
        }

        internal static bool IsKnown(string? value)
        {
            return TryMap(Normalize(value), out _, out _);
        }

        /// <summary>
        /// Returns the canonical accent name for a stored value, so the settings
        /// dropdown can select legacy config values correctly.
        /// </summary>
        internal static string NormalizeName(string? value)
        {
            return TryMap(Normalize(value), out string name, out _)
                ? name
                : DefaultAccentName;
        }

        private static bool TryMap(string key, out string name, out Color accent)
        {
            switch (key)
            {
                case "white":
                    name = "White";
                    accent = Color.FromArgb(236, 239, 246);
                    return true;
                case "black":
                    name = "Black";
                    accent = Color.FromArgb(17, 19, 24);
                    return true;
                case "red":
                    name = "Red";
                    accent = Color.FromArgb(255, 100, 110);
                    return true;
                case "blue":
                case "cyberblue":
                    name = "Blue";
                    accent = Color.FromArgb(59, 130, 246);
                    return true;
                case "orange":
                case "sunsetorange":
                case "sunset":
                case "yellow":
                    name = "Orange";
                    accent = Color.FromArgb(255, 165, 80);
                    return true;
                case "green":
                case "mintgreen":
                case "mint":
                    name = "Green";
                    accent = Color.FromArgb(92, 220, 140);
                    return true;
                case "purple":
                case "violet":
                    name = "Purple";
                    accent = Color.FromArgb(178, 132, 255);
                    return true;
                case "cyan":
                case "lightblue":
                case "goluboy":
                    name = "Cyan";
                    accent = Color.FromArgb(86, 220, 230);
                    return true;
                case "pink":
                    name = "Pink";
                    accent = Color.FromArgb(244, 114, 182);
                    return true;
                default:
                    name = DefaultAccentName;
                    accent = Color.FromArgb(59, 130, 246);
                    return false;
            }
        }

        private static string Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            Span<char> buffer = stackalloc char[value.Length];
            int length = 0;

            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c))
                {
                    buffer[length++] = char.ToLowerInvariant(c);
                }
            }

            return new string(buffer[..length]);
        }
    }
}

using System;
using System.Drawing;

namespace nyxWM
{
    /// <summary>
    /// Accent palette used by notifications. Every theme keeps the dark card
    /// surface and only tints it with the selected accent colour.
    /// </summary>
    internal sealed class NotificationTheme
    {
        internal string Name { get; }
        internal Color Accent { get; }
        internal Color BackgroundTop { get; }
        internal Color BackgroundBottom { get; }
        internal Color Border { get; }

        private NotificationTheme(
            string name,
            Color accent,
            Color backgroundTop,
            Color backgroundBottom,
            Color border)
        {
            Name = name;
            Accent = accent;
            BackgroundTop = backgroundTop;
            BackgroundBottom = backgroundBottom;
            Border = border;
        }

        internal static NotificationTheme Create(string name, Color accent)
        {
            var baseTop = Color.FromArgb(27, 29, 36);
            var baseBottom = Color.FromArgb(16, 17, 22);

            return new NotificationTheme(
                name,
                accent,
                Blend(baseTop, accent, 0.10f),
                Blend(baseBottom, accent, 0.06f),
                Color.FromArgb(56, accent.R, accent.G, accent.B));
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
    /// Resolves the "Theme" config value into a notification palette.
    /// Accepts Blue, Purple, White, Green, Cyan, Orange, Red and Yellow
    /// (case-insensitive, separators ignored). Unknown values fall back to Blue.
    /// </summary>
    internal static class NotificationThemeResolver
    {
        internal static NotificationTheme Resolve(string? value)
        {
            TryMap(Normalize(value), out string name, out Color accent);
            return NotificationTheme.Create(name, accent);
        }

        internal static bool IsKnown(string? value)
        {
            return TryMap(Normalize(value), out _, out _);
        }

        private static bool TryMap(string key, out string name, out Color accent)
        {
            switch (key)
            {
                case "blue":
                    name = "Blue";
                    accent = Color.FromArgb(108, 180, 255);
                    return true;
                case "purple":
                case "violet":
                    name = "Purple";
                    accent = Color.FromArgb(178, 132, 255);
                    return true;
                case "white":
                    name = "White";
                    accent = Color.FromArgb(236, 239, 246);
                    return true;
                case "green":
                    name = "Green";
                    accent = Color.FromArgb(92, 220, 140);
                    return true;
                case "cyan":
                case "lightblue":
                case "goluboy":
                    name = "Cyan";
                    accent = Color.FromArgb(86, 220, 230);
                    return true;
                case "orange":
                    name = "Orange";
                    accent = Color.FromArgb(255, 165, 80);
                    return true;
                case "red":
                    name = "Red";
                    accent = Color.FromArgb(255, 100, 110);
                    return true;
                case "yellow":
                    name = "Yellow";
                    accent = Color.FromArgb(255, 214, 92);
                    return true;
                default:
                    name = "Blue";
                    accent = Color.FromArgb(108, 180, 255);
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

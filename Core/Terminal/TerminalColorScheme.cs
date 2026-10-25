using System;
using System.Drawing;

namespace nyxWM
{
    /// <summary>
    /// Resolved console palette for one terminal colour scheme: the default
    /// foreground/background, the cursor colour and the 16 base ANSI colours.
    /// The "Theme" and "Custom Accent" schemes tint the standard xterm palette
    /// towards the application accent, so the console follows the accent colour
    /// chosen in the Visuals page. All other schemes are fixed presets.
    /// </summary>
    internal sealed class TerminalColorScheme
    {
        private readonly Color[] _palette;

        private TerminalColorScheme(
            Color foreground,
            Color background,
            Color cursor,
            Color[] palette)
        {
            Foreground = foreground;
            Background = background;
            Cursor = cursor;
            _palette = palette;
        }

        /// <summary>Default text colour used for <c>AnsiColor.Default</c>.</summary>
        internal Color Foreground { get; }

        /// <summary>Console surface colour. Its alpha is applied when drawn.</summary>
        internal Color Background { get; }

        /// <summary>Cursor colour.</summary>
        internal Color Cursor { get; }

        /// <summary>
        /// Colour for an xterm palette index. The first sixteen come from the
        /// scheme; the rest use the standard 6x6x6 cube and grey ramp.
        /// </summary>
        internal Color PaletteColor(int index)
        {
            if ((uint)index < 16u)
                return _palette[index];

            return StandardColor(index);
        }

        internal static TerminalColorScheme Create(string? name, NotificationTheme theme)
        {
            if (theme == null)
                throw new ArgumentNullException(nameof(theme));

            string scheme = TerminalPreferences.NormalizeColorScheme(name);

            if (string.Equals(scheme, TerminalPreferences.SchemeDracula, StringComparison.Ordinal))
                return Preset(DraculaPalette, Color.FromArgb(248, 248, 242), Color.FromArgb(235, 40, 42, 54), Color.FromArgb(255, 121, 198), theme);

            if (string.Equals(scheme, TerminalPreferences.SchemeMonokai, StringComparison.Ordinal))
                return Preset(MonokaiPalette, Color.FromArgb(248, 248, 242), Color.FromArgb(235, 39, 40, 34), Color.FromArgb(166, 226, 46), theme);

            if (string.Equals(scheme, TerminalPreferences.SchemeNord, StringComparison.Ordinal))
                return Preset(NordPalette, Color.FromArgb(216, 222, 233), Color.FromArgb(235, 46, 52, 64), Color.FromArgb(136, 192, 208), theme);

            if (string.Equals(scheme, TerminalPreferences.SchemeMatrix, StringComparison.Ordinal))
                return Preset(MatrixPalette, Color.FromArgb(214, 255, 224), Color.FromArgb(238, 4, 18, 10), Color.FromArgb(51, 255, 119), theme);

            if (string.Equals(scheme, TerminalPreferences.SchemeCyberpunk, StringComparison.Ordinal))
                return Preset(CyberpunkPalette, Color.FromArgb(248, 248, 255), Color.FromArgb(238, 13, 2, 33), Color.FromArgb(255, 126, 219), theme);

            Color consoleFill = theme.IsLight
                ? Color.FromArgb(150, theme.FieldBackground)
                : Color.FromArgb(170, theme.FieldBackground);

            float tint = string.Equals(scheme, TerminalPreferences.SchemeCustomAccent, StringComparison.Ordinal)
                ? 0.70f
                : 0.30f;

            return new TerminalColorScheme(
                theme.TextPrimary,
                consoleFill,
                theme.Accent,
                TintedPalette(theme.Accent, tint));
        }

        private static TerminalColorScheme Preset(Color[] palette, Color foreground, Color background, Color cursor, NotificationTheme theme)
        {
            // Presets always keep their own surface, but a light UI still needs a
            // little more opacity so the desktop does not wash the text out.
            Color surface = theme.IsLight
                ? Color.FromArgb(Math.Max(200, (int)background.A), background.R, background.G, background.B)
                : background;

            return new TerminalColorScheme(foreground, surface, cursor, palette);
        }

        private static Color[] TintedPalette(Color accent, float amount)
        {
            var palette = new Color[16];

            for (int index = 0; index < palette.Length; index++)
                palette[index] = Blend(XtermPalette[index], accent, amount);

            return palette;
        }

        private static Color Blend(Color from, Color to, float amount)
        {
            float clamped = Math.Clamp(amount, 0f, 1f);

            return Color.FromArgb(
                from.A,
                (int)MathF.Round(from.R + (to.R - from.R) * clamped),
                (int)MathF.Round(from.G + (to.G - from.G) * clamped),
                (int)MathF.Round(from.B + (to.B - from.B) * clamped));
        }

        internal static Color StandardColor(int index)
        {
            if (index < 232)
            {
                int cube = index - 16;
                return Color.FromArgb(
                    CubeLevel(cube / 36),
                    CubeLevel(cube % 36 / 6),
                    CubeLevel(cube % 6));
            }

            int gray = 8 + (index - 232) * 10;
            return Color.FromArgb(gray, gray, gray);
        }

        private static int CubeLevel(int value)
        {
            return value == 0 ? 0 : 55 + value * 40;
        }

        private static Color Rgb(int red, int green, int blue) =>
            Color.FromArgb(255, red, green, blue);

        private static readonly Color[] XtermPalette =
        {
            Rgb(0, 0, 0), Rgb(128, 0, 0), Rgb(0, 128, 0), Rgb(128, 128, 0),
            Rgb(0, 0, 128), Rgb(128, 0, 128), Rgb(0, 128, 128), Rgb(192, 192, 192),
            Rgb(128, 128, 128), Rgb(255, 0, 0), Rgb(0, 255, 0), Rgb(255, 255, 0),
            Rgb(0, 0, 255), Rgb(255, 0, 255), Rgb(0, 255, 255), Rgb(255, 255, 255)
        };

        private static readonly Color[] DraculaPalette =
        {
            Rgb(33, 34, 44), Rgb(255, 85, 85), Rgb(80, 250, 123), Rgb(241, 250, 140),
            Rgb(189, 147, 249), Rgb(255, 121, 198), Rgb(139, 233, 253), Rgb(248, 248, 242),
            Rgb(98, 114, 164), Rgb(255, 110, 110), Rgb(105, 255, 148), Rgb(255, 255, 165),
            Rgb(214, 172, 255), Rgb(255, 146, 223), Rgb(164, 255, 255), Rgb(255, 255, 255)
        };

        private static readonly Color[] MonokaiPalette =
        {
            Rgb(39, 40, 34), Rgb(249, 38, 114), Rgb(166, 226, 46), Rgb(244, 191, 117),
            Rgb(102, 217, 239), Rgb(174, 129, 255), Rgb(161, 239, 228), Rgb(248, 248, 242),
            Rgb(117, 113, 94), Rgb(249, 38, 114), Rgb(166, 226, 46), Rgb(244, 191, 117),
            Rgb(102, 217, 239), Rgb(174, 129, 255), Rgb(161, 239, 228), Rgb(249, 248, 245)
        };

        private static readonly Color[] NordPalette =
        {
            Rgb(59, 66, 82), Rgb(191, 97, 106), Rgb(163, 190, 140), Rgb(235, 203, 139),
            Rgb(129, 161, 193), Rgb(180, 142, 173), Rgb(136, 192, 208), Rgb(229, 233, 240),
            Rgb(76, 86, 106), Rgb(191, 97, 106), Rgb(163, 190, 140), Rgb(235, 203, 139),
            Rgb(129, 161, 193), Rgb(180, 142, 173), Rgb(143, 188, 187), Rgb(236, 239, 244)
        };

        private static readonly Color[] MatrixPalette =
        {
            Rgb(11, 31, 19), Rgb(255, 85, 119), Rgb(51, 255, 119), Rgb(182, 255, 92),
            Rgb(62, 201, 167), Rgb(125, 255, 176), Rgb(89, 255, 208), Rgb(214, 255, 224),
            Rgb(26, 61, 40), Rgb(255, 120, 148), Rgb(102, 255, 153), Rgb(206, 255, 130),
            Rgb(94, 226, 192), Rgb(150, 255, 198), Rgb(130, 255, 224), Rgb(236, 255, 244)
        };

        private static readonly Color[] CyberpunkPalette =
        {
            Rgb(26, 11, 46), Rgb(255, 56, 100), Rgb(114, 241, 184), Rgb(254, 222, 93),
            Rgb(54, 249, 246), Rgb(255, 126, 219), Rgb(45, 226, 230), Rgb(248, 248, 255),
            Rgb(56, 30, 92), Rgb(255, 110, 140), Rgb(150, 250, 205), Rgb(255, 235, 140),
            Rgb(110, 250, 248), Rgb(255, 160, 230), Rgb(100, 240, 242), Rgb(255, 255, 255)
        };
    }
}

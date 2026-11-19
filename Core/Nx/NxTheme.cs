using System;
using System.Collections.Generic;
using System.Drawing;

namespace nyxWM.Nx
{
    /// <summary>Window-surface colours for one theme mode.</summary>
    internal readonly struct NxSurfaceColors
    {
        internal Color Top { get; }
        internal Color Bottom { get; }
        internal Color Foreground { get; }
        internal Color Muted { get; }
        internal Color Stroke { get; }

        internal NxSurfaceColors(Color top, Color bottom, Color foreground, Color muted, Color stroke)
        {
            Top = top;
            Bottom = bottom;
            Foreground = foreground;
            Muted = muted;
            Stroke = stroke;
        }
    }

    /// <summary>
    /// The palette and geometry resolved from <c>theme.nx</c>. This is the bridge
    /// that makes one .nx file style every surface in the application: the Nx
    /// surfaces read their tokens directly, and the WinForms overlays read them
    /// through this class.
    ///
    /// Immutable by construction. Installed once at startup via
    /// <see cref="Install"/>.
    /// </summary>
    internal sealed class NxTheme
    {
        internal static NxTheme Current { get; private set; } = CreateDefault();

        /// <summary>
        /// The accent in force. When the theme file says "auto", this is the
        /// accent picked in nyxWM settings.
        /// </summary>
        internal Color Accent { get; }
        internal Color AccentSoft { get; }
        internal Color Foreground { get; }
        internal Color Muted { get; }
        internal Color Ok { get; }
        internal Color Warn { get; }
        internal Color Bad { get; }
        internal Color BackgroundTop { get; }
        internal Color BackgroundBottom { get; }
        internal Color Hover { get; }
        internal Color Stroke { get; }
        internal Color LightBackgroundTop { get; }
        internal Color LightBackgroundBottom { get; }
        internal Color LightForeground { get; }
        internal Color LightMuted { get; }
        internal Color LightStroke { get; }
        internal float Radius { get; }
        internal float RadiusSmall { get; }
        internal float Gap { get; }
        internal float Padding { get; }
        internal string FontFamily { get; }
        internal float FontSize { get; }
        internal float FontSizeSmall { get; }
        internal float SurfaceOpacity { get; }

        /// <summary>Target frame rate from theme file, or 0 if not set (use default).</summary>
        internal int Fps { get; }

        private NxTheme(
            Color accent,
            Color accentSoft,
            Color foreground,
            Color muted,
            Color ok,
            Color warn,
            Color bad,
            Color backgroundTop,
            Color backgroundBottom,
            Color hover,
            Color stroke,
            Color lightBackgroundTop,
            Color lightBackgroundBottom,
            Color lightForeground,
            Color lightMuted,
            Color lightStroke,
            float radius,
            float radiusSmall,
            float gap,
            float padding,
            string fontFamily,
            float fontSize,
            float fontSizeSmall,
            float surfaceOpacity,
            int fps)
        {
            Accent = accent;
            AccentSoft = accentSoft;
            Foreground = foreground;
            Muted = muted;
            Ok = ok;
            Warn = warn;
            Bad = bad;
            BackgroundTop = backgroundTop;
            BackgroundBottom = backgroundBottom;
            Hover = hover;
            Stroke = stroke;
            LightBackgroundTop = lightBackgroundTop;
            LightBackgroundBottom = lightBackgroundBottom;
            LightForeground = lightForeground;
            LightMuted = lightMuted;
            LightStroke = lightStroke;
            Radius = radius;
            RadiusSmall = radiusSmall;
            Gap = gap;
            Padding = padding;
            FontFamily = fontFamily;
            FontSize = fontSize;
            FontSizeSmall = fontSizeSmall;
            SurfaceOpacity = surfaceOpacity;
            Fps = fps;
        }

        internal static void Install(NxTheme? theme)
        {
            Current = theme ?? CreateDefault();
        }

        /// <summary>
        /// Replaces "accent: auto" and "accent-soft: auto" with concrete colours
        /// derived from the settings accent, so the .nx surfaces resolve $accent
        /// to a real colour instead of the word "auto".
        /// </summary>
        internal static void ApplyAutoAccent(IDictionary<string, NxValue> tokens, Color autoAccent)
        {
            ArgumentNullException.ThrowIfNull(tokens);

            ReplaceIfAuto(tokens, "accent", autoAccent);
            ReplaceIfAuto(tokens, "accent-soft", WithAlpha(autoAccent, 77));
        }

        private static void ReplaceIfAuto(
            IDictionary<string, NxValue> tokens,
            string name,
            Color resolved)
        {
            if (!tokens.TryGetValue(name, out NxValue? value))
                return;

            if (value.Kind != NxValueKind.Text ||
                !string.Equals(value.Text.Trim(), "auto", StringComparison.OrdinalIgnoreCase))
                return;

            tokens[name] = NxValue.FromColor(
                NxColor.FromBytes(resolved.A, resolved.R, resolved.G, resolved.B),
                value.Line);
        }

        /// <summary>
        /// The window-surface colours for the dark or light interface mode.
        /// Both sets come from the same theme file, so a theme can style either
        /// mode or just the one it cares about.
        /// </summary>
        internal NxSurfaceColors ForMode(bool light)
        {
            return light
                ? new NxSurfaceColors(LightBackgroundTop, LightBackgroundBottom, LightForeground, LightMuted, LightStroke)
                : new NxSurfaceColors(BackgroundTop, BackgroundBottom, Foreground, Muted, Stroke);
        }

        /// <summary>
        /// Resolves the theme from a document's token table.
        /// <paramref name="autoAccent"/> is what "accent: auto" means: the accent
        /// chosen in nyxWM settings.
        /// </summary>
        internal static NxTheme FromTokens(IReadOnlyDictionary<string, NxValue> tokens, Color autoAccent)
        {
            Color accent = ResolveAccent(tokens, "accent", autoAccent);
            Color accentSoft = ResolveAccent(tokens, "accent-soft", WithAlpha(accent, 77));

            return new NxTheme(
                accent,
                accentSoft,
                ColorToken(tokens, "fg", Color.FromArgb(238, 241, 248)),
                ColorToken(tokens, "muted", Color.FromArgb(160, 166, 178)),
                ColorToken(tokens, "ok", Color.FromArgb(158, 206, 106)),
                ColorToken(tokens, "warn", Color.FromArgb(224, 175, 104)),
                ColorToken(tokens, "bad", Color.FromArgb(247, 118, 142)),
                Opaque(ColorToken(tokens, "bg", Color.FromArgb(27, 29, 36))),
                Opaque(ColorToken(tokens, "bg-soft", Color.FromArgb(16, 17, 22))),
                ColorToken(tokens, "bg-hover", Color.FromArgb(38, 255, 255, 255)),
                ColorToken(tokens, "stroke", Color.FromArgb(56, 235, 238, 245)),
                Opaque(ColorToken(tokens, "bg-light", Color.FromArgb(244, 245, 247))),
                Opaque(ColorToken(tokens, "bg-light-soft", Color.FromArgb(232, 234, 239))),
                ColorToken(tokens, "fg-light", Color.FromArgb(20, 22, 28)),
                ColorToken(tokens, "muted-light", Color.FromArgb(98, 105, 118)),
                ColorToken(tokens, "stroke-light", Color.FromArgb(40, 20, 22, 28)),
                NumberToken(tokens, "radius", 14f),
                NumberToken(tokens, "radius-small", 10f),
                NumberToken(tokens, "gap", 10f),
                NumberToken(tokens, "padding", 8f),
                TextToken(tokens, "font", "Segoe UI"),
                NumberToken(tokens, "size", 13f),
                NumberToken(tokens, "size-small", 11f),
                Math.Clamp(NumberToken(tokens, "surface-opacity", 0.95f), 0.4f, 1f),
                (int)NumberToken(tokens, "fps", 0f));
        }

        internal static NxTheme CreateDefault()
        {
            Color accent = Color.FromArgb(59, 130, 246);

            return new NxTheme(
                accent,
                WithAlpha(accent, 77),
                Color.FromArgb(238, 241, 248),
                Color.FromArgb(160, 166, 178),
                Color.FromArgb(158, 206, 106),
                Color.FromArgb(224, 175, 104),
                Color.FromArgb(247, 118, 142),
                Color.FromArgb(27, 29, 36),
                Color.FromArgb(16, 17, 22),
                Color.FromArgb(38, 255, 255, 255),
                Color.FromArgb(56, 235, 238, 245),
                Color.FromArgb(244, 245, 247),
                Color.FromArgb(232, 234, 239),
                Color.FromArgb(20, 22, 28),
                Color.FromArgb(98, 105, 118),
                Color.FromArgb(40, 20, 22, 28),
                14f,
                10f,
                10f,
                8f,
                "Segoe UI",
                13f,
                11f,
                0.95f,
                0);
        }

        private static Color ResolveAccent(
            IReadOnlyDictionary<string, NxValue> tokens,
            string name,
            Color fallback)
        {
            if (!tokens.TryGetValue(name, out NxValue? value))
                return fallback;

            // "auto" means: use the accent chosen in nyxWM settings, so the
            // settings dropdown keeps working until the theme pins a colour.
            if (value.Kind == NxValueKind.Text &&
                string.Equals(value.Text.Trim(), "auto", StringComparison.OrdinalIgnoreCase))
                return fallback;

            return value.Kind == NxValueKind.Color ? FromNx(value.Color) : fallback;
        }

        private static Color ColorToken(
            IReadOnlyDictionary<string, NxValue> tokens,
            string name,
            Color fallback)
        {
            return tokens.TryGetValue(name, out NxValue? value) && value.Kind == NxValueKind.Color
                ? FromNx(value.Color)
                : fallback;
        }

        private static float NumberToken(
            IReadOnlyDictionary<string, NxValue> tokens,
            string name,
            float fallback)
        {
            if (!tokens.TryGetValue(name, out NxValue? value))
                return fallback;

            if (value.Kind is not (NxValueKind.Number or NxValueKind.Duration))
                return fallback;

            double number = value.Number;
            return double.IsFinite(number) ? (float)number : fallback;
        }

        private static string TextToken(
            IReadOnlyDictionary<string, NxValue> tokens,
            string name,
            string fallback)
        {
            if (!tokens.TryGetValue(name, out NxValue? value) || value.Kind != NxValueKind.Text)
                return fallback;

            return string.IsNullOrWhiteSpace(value.Text) ? fallback : value.Text.Trim();
        }

        private static Color FromNx(NxColor color)
        {
            return Color.FromArgb(color.A, color.R, color.G, color.B);
        }

        private static Color WithAlpha(Color color, byte alpha)
        {
            return Color.FromArgb(alpha, color.R, color.G, color.B);
        }

        /// <summary>
        /// Window backgrounds must be opaque: translucency on a WinForms window
        /// comes from its whole-window opacity, not from the brush alpha.
        /// </summary>
        private static Color Opaque(Color color)
        {
            return color.A == 255 ? color : Color.FromArgb(255, color.R, color.G, color.B);
        }
    }
}

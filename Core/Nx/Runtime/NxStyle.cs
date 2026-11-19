using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;

namespace nyxWM.Nx
{
    /// <summary>
    /// The visual state of one element, resolved to concrete values. Immutable:
    /// an animation produces a new style each frame instead of mutating the
    /// declared one, so the document's own values stay the baseline.
    /// </summary>
    internal sealed record NxStyle
    {
        internal IReadOnlyList<NxColor> Background { get; init; } = Array.Empty<NxColor>();
        internal bool GradientHorizontal { get; init; }

        internal NxColor Foreground { get; init; } = NxColor.White;
        internal NxColor BorderColor { get; init; } = NxColor.Transparent;

        internal double Opacity { get; init; } = 1.0;
        internal double Radius { get; init; }
        internal double BorderWidth { get; init; }
        internal double Scale { get; init; } = 1.0;
        internal double OffsetX { get; init; }
        internal double OffsetY { get; init; }
        internal double Rotation { get; init; }

        internal string FontFamily { get; init; } = "Segoe UI";
        internal double FontSize { get; init; } = 12.0;
        internal bool FontBold { get; init; }

        /// <summary>Text alignment inside the box: left, center or right.</summary>
        internal string TextAlign { get; init; } = "center";

        internal double Padding { get; init; } = 6.0;
        internal double Spacing { get; init; } = 8.0;

        /// <summary>Box silhouette: round, square, circle, triangle, diamond, line, none, plus custom SVG-like path.</summary>
        internal string Shape { get; init; } = "round";

        internal bool Shadow { get; init; }
        internal string Icon { get; init; } = string.Empty;
        internal double IconSize { get; init; }

        /// <summary>Set by an animation to resize the element without touching its layout slot.</summary>
        internal double? WidthOverride { get; init; }
        internal double? HeightOverride { get; init; }

        /// <summary>Declared visibility. A false value removes the element from layout.</summary>
        internal bool Visible { get; init; } = true;

        // ── Glow / bloom ──
        internal NxColor GlowColor { get; init; } = NxColor.Transparent;
        internal double GlowRadius { get; init; }
        internal double GlowSpread { get; init; } = 4.0;

        // ── CRT / scanline ──
        internal double Scanlines { get; init; }

        // ── Chromatic aberration ──
        internal double ChromaticShift { get; init; }

        // ── Random animation offset ──
        internal double RandomX { get; init; }
        internal double RandomY { get; init; }

        // ── Custom SVG-like path data ──
        internal string PathData { get; init; } = string.Empty;

        // ── Particle emitter (element spawns child particles) ──
        internal bool Particles { get; init; }
        internal double ParticleCount { get; init; } = 12.0;
        internal double ParticleSpeed { get; init; } = 60.0;
        internal double ParticleLifetime { get; init; } = 2000.0;
        internal string ParticleShape { get; init; } = "circle";
        internal double ParticleSize { get; init; } = 8.0;
        internal string ParticleImage { get; init; } = string.Empty;

        /// <summary>First background colour, or transparent when none was given.</summary>
        internal NxColor PrimaryBackground =>
            Background.Count > 0 ? Background[0] : NxColor.Transparent;

        internal static NxStyle FromNode(
            NxNode node,
            NxExpressionEvaluator evaluator,
            NxStyle? parent = null)
        {
            NxStyle style = parent ?? Default;
            string name = node.Type.ToLowerInvariant();

            // The element's own name doubles as its shape for the terse forms
            // "circle {}", "triangle {}" and "line {}".
            if (name is "circle" or "triangle" or "diamond" or "line" or "square")
                style = style with { Shape = name };

            foreach (NxProperty property in node.Properties)
            {
                style = Apply(style, property.Name.ToLowerInvariant(), property.Value, evaluator);
            }

            return style;
        }

        internal static NxStyle Apply(
            NxStyle style,
            string name,
            NxValue value,
            NxExpressionEvaluator evaluator)
        {
            switch (name)
            {
                case "background":
                case "bg":
                    return style with { Background = ReadColors(value, evaluator) };

                case "direction":
                case "gradient":
                    return style with
                    {
                        GradientHorizontal = evaluator.EvaluateText(value, "vertical")
                            .Equals("horizontal", StringComparison.OrdinalIgnoreCase)
                    };

                case "foreground":
                case "fg":
                case "color":
                    return style with { Foreground = evaluator.EvaluateColor(value, style.Foreground) };

                case "border":
                    return style with { BorderColor = evaluator.EvaluateColor(value, style.BorderColor) };

                case "borderwidth":
                    return style with { BorderWidth = evaluator.EvaluateNumber(value, style.BorderWidth) };

                case "opacity":
                    return style with { Opacity = Math.Clamp(evaluator.EvaluateNumber(value, style.Opacity), 0.0, 1.0) };

                case "radius":
                    return style with { Radius = evaluator.EvaluateNumber(value, style.Radius) };

                case "scale":
                    return style with { Scale = evaluator.EvaluateNumber(value, style.Scale) };

                case "offsetx":
                    return style with { OffsetX = evaluator.EvaluateNumber(value, style.OffsetX) };

                case "offsety":
                    return style with { OffsetY = evaluator.EvaluateNumber(value, style.OffsetY) };

                case "rotation":
                    return style with { Rotation = evaluator.EvaluateNumber(value, style.Rotation) };

                case "font":
                    return style with { FontFamily = evaluator.EvaluateText(value, style.FontFamily) };

                case "fontsize":
                    return style with { FontSize = evaluator.EvaluateNumber(value, style.FontSize) };

                case "bold":
                    return style with { FontBold = evaluator.EvaluateFlag(value, style.FontBold) };

                case "align":
                    return style with { TextAlign = evaluator.EvaluateText(value, style.TextAlign) };

                case "padding":
                    return style with { Padding = evaluator.EvaluateNumber(value, style.Padding) };

                case "spacing":
                case "gap":
                    return style with { Spacing = evaluator.EvaluateNumber(value, style.Spacing) };

                case "shape":
                    return style with { Shape = evaluator.EvaluateText(value, style.Shape) };

                case "shadow":
                    return style with { Shadow = evaluator.EvaluateFlag(value, style.Shadow) };

                case "icon":
                    return style with { Icon = evaluator.EvaluateText(value, style.Icon) };

                case "iconsize":
                    return style with { IconSize = evaluator.EvaluateNumber(value, style.IconSize) };

                case "visible":
                    return style with { Visible = evaluator.EvaluateFlag(value, style.Visible) };

                case "width":
                    return style with { WidthOverride = ReadSize(value, evaluator) };

                case "height":
                    return style with { HeightOverride = ReadSize(value, evaluator) };

                case "glow":
                case "glowcolor":
                    return style with { GlowColor = evaluator.EvaluateColor(value, style.GlowColor) };

                case "glowradius":
                    return style with { GlowRadius = evaluator.EvaluateNumber(value, style.GlowRadius) };

                case "glowspread":
                    return style with { GlowSpread = evaluator.EvaluateNumber(value, style.GlowSpread) };

                case "scanlines":
                    return style with { Scanlines = Math.Clamp(evaluator.EvaluateNumber(value, style.Scanlines), 0.0, 1.0) };

                case "chromatic":
                case "chromaticshift":
                    return style with { ChromaticShift = evaluator.EvaluateNumber(value, style.ChromaticShift) };

                case "randomx":
                    return style with { RandomX = evaluator.EvaluateNumber(value, style.RandomX) };

                case "randomy":
                    return style with { RandomY = evaluator.EvaluateNumber(value, style.RandomY) };

                case "pathdata":
                case "path":
                    return style with { PathData = evaluator.EvaluateText(value, style.PathData) };

                case "particles":
                    return style with { Particles = evaluator.EvaluateFlag(value, style.Particles) };

                case "particlecount":
                    return style with { ParticleCount = evaluator.EvaluateNumber(value, style.ParticleCount) };

                case "particlespeed":
                    return style with { ParticleSpeed = evaluator.EvaluateNumber(value, style.ParticleSpeed) };

                case "particlelifetime":
                    return style with { ParticleLifetime = evaluator.EvaluateNumber(value, style.ParticleLifetime) };

                case "particleshape":
                    return style with { ParticleShape = evaluator.EvaluateText(value, style.ParticleShape) };

                case "particlesize":
                    return style with { ParticleSize = evaluator.EvaluateNumber(value, style.ParticleSize) };

                case "particleimage":
                    return style with { ParticleImage = evaluator.EvaluateText(value, style.ParticleImage) };

                default:
                    return style;
            }
        }

        /// <summary>Reads a single colour or a list of colours for a gradient.</summary>
        private static IReadOnlyList<NxColor> ReadColors(NxValue value, NxExpressionEvaluator evaluator)
        {
            if (value.Kind != NxValueKind.List)
                return new[] { evaluator.EvaluateColor(value, NxColor.Transparent) };

            var colors = new List<NxColor>(value.List.Count);

            foreach (NxValue item in value.List)
                colors.Add(evaluator.EvaluateColor(item, NxColor.Transparent));

            return colors;
        }

        /// <summary>Reads a size in pixels, or null when the value is not a plain number.</summary>
        private static double? ReadSize(NxValue value, NxExpressionEvaluator evaluator)
        {
            if (value.Kind == NxValueKind.Text && value.Text.TrimEnd().EndsWith('%'))
                return null;

            return evaluator.EvaluateNumber(value, 0.0);
        }

        internal static readonly NxStyle Default = new();

        /// <summary>
        /// Sets a numeric property by its document name. Animations use this to
        /// write an interpolated frame without going back through the parser.
        /// </summary>
        internal NxStyle WithNumber(string name, double value)
        {
            return name switch
            {
                "opacity" => this with { Opacity = Math.Clamp(value, 0.0, 1.0) },
                "radius" => this with { Radius = value },
                "scale" => this with { Scale = value },
                "offsetx" => this with { OffsetX = value },
                "offsety" => this with { OffsetY = value },
                "rotation" => this with { Rotation = value },
                "borderwidth" => this with { BorderWidth = value },
                "fontsize" => this with { FontSize = Math.Max(1.0, value) },
                "iconsize" => this with { IconSize = value },
                "padding" => this with { Padding = value },
                "spacing" or "gap" => this with { Spacing = value },
                "width" => this with { WidthOverride = value },
                "height" => this with { HeightOverride = value },
                "glowradius" => this with { GlowRadius = value },
                "glowspread" => this with { GlowSpread = value },
                "scanlines" => this with { Scanlines = Math.Clamp(value, 0.0, 1.0) },
                "chromatic" or "chromaticshift" => this with { ChromaticShift = value },
                "randomx" => this with { RandomX = value },
                "randomy" => this with { RandomY = value },
                "particlecount" => this with { ParticleCount = value },
                "particlespeed" => this with { ParticleSpeed = value },
                "particlelifetime" => this with { ParticleLifetime = value },
                "particlesize" => this with { ParticleSize = value },
                _ => this
            };
        }

        /// <summary>Sets a colour property by its document name, for animated colours.</summary>
        internal NxStyle WithColor(string name, NxColor value)
        {
            return name switch
            {
                "background" or "bg" => this with { Background = new[] { value } },
                "foreground" or "fg" or "color" => this with { Foreground = value },
                "border" => this with { BorderColor = value },
                "glow" or "glowcolor" => this with { GlowColor = value },
                _ => this
            };
        }

        /// <summary>True when a property can be interpolated between keyframes.</summary>
        internal static bool IsAnimatable(string name)
        {
            return name switch
            {
                "opacity" or "radius" or "scale" or "offsetx" or "offsety" or "rotation" or
                "borderwidth" or "fontsize" or "iconsize" or "padding" or "spacing" or "gap" or
                "width" or "height" or "background" or "bg" or "foreground" or "fg" or
                "color" or "border" or "glowradius" or "glowspread" or "scanlines" or
                "chromatic" or "chromaticshift" or "randomx" or "randomy" or
                "particlecount" or "particlespeed" or "particlelifetime" or "particlesize" or
                "glow" or "glowcolor" => true,
                _ => false
            };
        }

        internal static bool IsColorProperty(string name)
        {
            return name switch
            {
                "background" or "bg" or "foreground" or "fg" or "color" or "border" or "glow" or "glowcolor" => true,
                _ => false
            };
        }

        internal Color ToColor(NxColor color, double alphaScale)
        {
            int alpha = (int)Math.Round(Math.Clamp(color.A * alphaScale, 0, 255));
            return Color.FromArgb(alpha, color.R, color.G, color.B);
        }
    }

    /// <summary>
    /// Fonts are expensive to build and a surface asks for the same handful every
    /// frame, so they are cached by family, size and weight.
    /// </summary>
    internal static class NxFontCache
    {
        private static readonly ConcurrentDictionary<string, Font> Cache = new(StringComparer.Ordinal);

        internal static Font Get(string family, double size, bool bold)
        {
            int rounded = Math.Max(1, (int)Math.Round(size));
            string key = $"{family}|{rounded}|{bold}";

            return Cache.GetOrAdd(key, _ => Build(family, rounded, bold));
        }

        private static Font Build(string family, int size, bool bold)
        {
            try
            {
                return new Font(family, size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
            }
            catch (ArgumentException)
            {
                // An unknown family falls back to the default UI font.
                return new Font(FontFamily.GenericSansSerif, size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
            }
        }
    }

    /// <summary>Easing curves a .nx animation may name.</summary>
    internal static class NxEasing
    {
        internal static double Apply(string name, double t)
        {
            t = Math.Clamp(t, 0.0, 1.0);

            return name.ToLowerInvariant() switch
            {
                "linear" => t,
                "ease-in" or "easein" => t * t,
                "ease-out" or "easeout" => 1.0 - (1.0 - t) * (1.0 - t),
                "ease-in-out" or "easeinout" => t < 0.5 ? 2 * t * t : 1 - 2 * (1 - t) * (1 - t),
                "sine" or "sine-in-out" => 0.5 - 0.5 * Math.Cos(Math.PI * t),
                "sine-in" => 1.0 - Math.Cos(t * Math.PI / 2),
                "sine-out" => Math.Sin(t * Math.PI / 2),
                "bounce-out" => BounceOut(t),
                "back-out" => BackOut(t),
                "step" => t < 1.0 ? 0.0 : 1.0,
                _ => t
            };
        }

        private static double BounceOut(double t)
        {
            const double n = 7.5625;
            const double d = 2.75;

            if (t < 1 / d)
                return n * t * t;

            if (t < 2 / d)
            {
                t -= 1.5 / d;
                return n * t * t + 0.75;
            }

            if (t < 2.5 / d)
            {
                t -= 2.25 / d;
                return n * t * t + 0.9375;
            }

            t -= 2.625 / d;
            return n * t * t + 0.984375;
        }

        private static double BackOut(double t)
        {
            const double c = 1.70158;
            double shifted = t - 1;
            return 1 + (c + 1) * shifted * shifted * shifted + c * shifted * shifted;
        }

        internal static double Lerp(double from, double to, double t) => from + (to - from) * t;

        internal static NxColor Lerp(NxColor from, NxColor to, double t)
        {
            static byte Mix(byte a, byte b, double amount) =>
                (byte)Math.Round(Math.Clamp(a + (b - a) * amount, 0, 255));

            return NxColor.FromBytes(
                Mix(from.A, to.A, t),
                Mix(from.R, to.R, t),
                Mix(from.G, to.G, t),
                Mix(from.B, to.B, t));
        }
    }
}

using System;
using System.Drawing;

namespace nyxWM
{
    /// <summary>
    /// Reads the Visuals settings into the values the windows need: theme mode,
    /// glass, opacity, animation and corner-radius style. Every consumer reads
    /// the live config, so a save applies to the next window that opens.
    /// </summary>
    internal static class VisualPreferences
    {
        internal const string ModeDark = "Dark";
        internal const string ModeLight = "Light";

        internal const string CornerSharp = "Sharp";
        internal const string CornerRounded = "Rounded";
        internal const string CornerSmooth = "Smooth";

        internal const float MinUiScale = 0.8f;
        internal const float MaxUiScale = 1.4f;

        internal static bool GlassEnabled => ConfigManager.Current.GlassEnabled;

        internal static bool SmoothAnimations => ConfigManager.Current.SmoothAnimations;

        /// <summary>
        /// Manual interface scale multiplier from the Visuals settings.
        /// Falls back to 1.0 when the stored value is missing or out of range.
        /// </summary>
        internal static float UiScale => NormalizeUiScale(ConfigManager.Current.UiScale);

        internal static float NormalizeUiScale(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
                return 1f;

            return Math.Clamp(value, MinUiScale, MaxUiScale);
        }

        /// <summary>
        /// Creates a UI font whose point size follows the manual interface scale.
        /// DPI scaling is applied by GDI+ itself, so only the manual multiplier
        /// belongs here.
        /// </summary>
        internal static Font CreateUiFont(string family, float sizePoints, FontStyle style)
        {
            return new Font(family, sizePoints * UiScale, style, GraphicsUnit.Point);
        }

        internal static float WindowOpacity
        {
            get
            {
                float value = ConfigManager.Current.WindowOpacity;
                if (value <= 0f)
                    value = 0.95f;

                return Math.Clamp(value, 0.4f, 1f);
            }
        }

        internal static string NormalizeMode(string? value)
        {
            return string.Equals(value?.Trim(), ModeLight, StringComparison.OrdinalIgnoreCase)
                ? ModeLight
                : ModeDark;
        }

        internal static string NormalizeCorner(string? value)
        {
            if (string.Equals(value?.Trim(), CornerSharp, StringComparison.OrdinalIgnoreCase))
                return CornerSharp;

            if (string.Equals(value?.Trim(), CornerSmooth, StringComparison.OrdinalIgnoreCase))
                return CornerSmooth;

            return CornerRounded;
        }

        /// <summary>
        /// Maps the selected corner style onto a rounded-design radius.
        /// Sharp removes the rounding, Smooth exaggerates it.
        /// </summary>
        internal static float CornerRadius(float roundedDip)
        {
            return NormalizeCorner(ConfigManager.Current.CornerStyle) switch
            {
                CornerSharp => 0f,
                CornerSmooth => roundedDip * 1.5f,
                _ => roundedDip
            };
        }

        /// <summary>
        /// Same mapping for controls that express their radius as a size ratio.
        /// </summary>
        internal static float CornerRadiusRatio(float roundedRatio)
        {
            return NormalizeCorner(ConfigManager.Current.CornerStyle) switch
            {
                CornerSharp => 0.05f,
                CornerSmooth => MathF.Min(0.5f, roundedRatio * 1.3f),
                _ => roundedRatio
            };
        }
    }
}

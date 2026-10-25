using System.Drawing;

namespace nyxWM
{
    /// <summary>
    /// Shared colours for the settings content controls. The accent comes from
    /// the active notification theme; everything else stays on the dark surface.
    /// </summary>
    internal static class SettingsPalette
    {
        internal static readonly Color FieldBackground = Color.FromArgb(33, 35, 44);
        internal static readonly Color FieldBorder = Color.FromArgb(58, 235, 238, 245);
        internal static readonly Color Track = Color.FromArgb(62, 66, 78);
        internal static readonly Color TextPrimary = Color.FromArgb(238, 241, 248);
        internal static readonly Color TextMuted = Color.FromArgb(160, 166, 178);
        internal static readonly Color Knob = Color.FromArgb(242, 244, 250);
    }
}

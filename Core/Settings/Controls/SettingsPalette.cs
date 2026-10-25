using System.Drawing;

namespace nyxWM
{
    /// <summary>
    /// Shared colours for the settings content controls. Values are refreshed
    /// from the active palette whenever a settings window is built, so the same
    /// controls render in both dark and light mode.
    /// </summary>
    internal static class SettingsPalette
    {
        internal static Color FieldBackground { get; private set; } = Color.FromArgb(33, 35, 44);
        internal static Color FieldBorder { get; private set; } = Color.FromArgb(58, 235, 238, 245);
        internal static Color Track { get; private set; } = Color.FromArgb(62, 66, 78);
        internal static Color TextPrimary { get; private set; } = Color.FromArgb(238, 241, 248);
        internal static Color TextMuted { get; private set; } = Color.FromArgb(160, 166, 178);
        internal static Color Knob { get; private set; } = Color.FromArgb(242, 244, 250);
        internal static Color OnAccent { get; private set; } = Color.FromArgb(18, 20, 26);
        internal static Color Overlay { get; private set; } = Color.White;

        internal static void Apply(NotificationTheme theme)
        {
            FieldBackground = theme.FieldBackground;
            FieldBorder = theme.FieldBorder;
            Track = theme.Track;
            TextPrimary = theme.TextPrimary;
            TextMuted = theme.TextMuted;
            Knob = theme.Knob;
            OnAccent = theme.OnAccent;
            Overlay = theme.Overlay;
        }
    }
}

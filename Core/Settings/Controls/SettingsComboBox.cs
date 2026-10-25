using System;
using System.Drawing;
using System.Windows.Forms;

namespace nyxWM
{
    /// <summary>
    /// Dark owner-drawn combo box for enum-like settings.
    /// </summary>
    internal sealed class SettingsComboBox : ComboBox
    {
        private readonly Color _accent;

        internal SettingsComboBox(Color accent)
        {
            _accent = accent;

            DropDownStyle = ComboBoxStyle.DropDownList;
            FlatStyle = FlatStyle.Flat;
            DrawMode = DrawMode.OwnerDrawFixed;
            BackColor = SettingsPalette.FieldBackground;
            ForeColor = SettingsPalette.TextPrimary;

            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count)
                return;

            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

            Color background = selected
                ? Blend(_accent, SettingsPalette.FieldBackground, 0.55f)
                : SettingsPalette.FieldBackground;

            using (var brush = new SolidBrush(background))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }

            string text = Items[e.Index]?.ToString() ?? string.Empty;
            Color textColor = SettingsPalette.TextPrimary;

            TextRenderer.DrawText(
                e.Graphics,
                text,
                Font,
                e.Bounds,
                textColor,
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix |
                TextFormatFlags.PreserveGraphicsClipping);
        }

        private static Color Blend(Color accent, Color background, float backgroundWeight)
        {
            float weight = Math.Clamp(backgroundWeight, 0f, 1f);

            return Color.FromArgb(
                (int)MathF.Round(accent.R * (1f - weight) + background.R * weight),
                (int)MathF.Round(accent.G * (1f - weight) + background.G * weight),
                (int)MathF.Round(accent.B * (1f - weight) + background.B * weight));
        }
    }
}

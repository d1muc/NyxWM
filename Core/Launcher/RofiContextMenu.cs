using System;
using System.Drawing;
using System.Windows.Forms;

namespace nyxWM
{
    /// <summary>
    /// Small themed context menu for the Rofi app list. A ToolStripDropDown
    /// does not activate its owner, so opening it keeps the search field
    /// focused while the menu is visible.
    /// </summary>
    internal sealed class RofiContextMenu : ContextMenuStrip
    {
        private readonly NotificationTheme _theme;
        private readonly float _scale;

        internal RofiContextMenu(NotificationTheme theme, float scale, Font font)
        {
            _theme = theme ?? throw new ArgumentNullException(nameof(theme));
            _scale = Math.Clamp(scale, 0.75f, 3f);

            BackColor = theme.BackgroundTop;
            ForeColor = theme.TextPrimary;
            Font = font ?? throw new ArgumentNullException(nameof(font));
            ShowImageMargin = false;
            ShowCheckMargin = false;
            Renderer = new RofiMenuRenderer(theme);
            Padding = new Padding(
                0,
                (int)MathF.Round(4f * _scale),
                0,
                (int)MathF.Round(4f * _scale));
        }

        internal ToolStripMenuItem AddAction(string text)
        {
            var item = new ToolStripMenuItem(text)
            {
                ForeColor = _theme.TextPrimary,
                Padding = new Padding(
                    (int)MathF.Round(10f * _scale),
                    (int)MathF.Round(5f * _scale),
                    (int)MathF.Round(10f * _scale),
                    (int)MathF.Round(5f * _scale))
            };

            Items.Add(item);
            return item;
        }

        private sealed class RofiMenuRenderer : ToolStripProfessionalRenderer
        {
            private readonly NotificationTheme _theme;

            internal RofiMenuRenderer(NotificationTheme theme)
                : base(new RofiMenuColors(theme))
            {
                _theme = theme;
                RoundedEdges = false;
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                if (e.Item.Selected || e.Item.Pressed)
                    e.TextColor = _theme.IsLight ? Color.Black : Color.White;

                base.OnRenderItemText(e);
            }
        }

        private sealed class RofiMenuColors : ProfessionalColorTable
        {
            private readonly Color _background;
            private readonly Color _selected;
            private readonly Color _border;

            internal RofiMenuColors(NotificationTheme theme)
            {
                _background = theme.BackgroundTop;
                _selected = Blend(theme.BackgroundTop, theme.Accent, theme.IsLight ? 0.20f : 0.30f);
                _border = Blend(theme.BackgroundTop, theme.Accent, 0.45f);
            }

            public override Color ToolStripDropDownBackground => _background;
            public override Color ImageMarginGradientBegin => _background;
            public override Color ImageMarginGradientMiddle => _background;
            public override Color ImageMarginGradientEnd => _background;
            public override Color MenuBorder => _border;
            public override Color MenuItemBorder => _border;
            public override Color MenuItemSelected => _selected;
            public override Color MenuItemSelectedGradientBegin => _selected;
            public override Color MenuItemSelectedGradientEnd => _selected;
            public override Color MenuItemPressedGradientBegin => _selected;
            public override Color MenuItemPressedGradientEnd => _selected;

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
    }
}

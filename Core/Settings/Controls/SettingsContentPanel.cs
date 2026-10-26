using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace nyxWM
{
    /// <summary>
    /// Panel that paints the same vertical surface gradient as the settings
    /// window, so content pages blend into the card while they slide.
    /// </summary>
    internal sealed class SettingsContentPanel : Panel
    {
        private readonly NotificationTheme _theme;

        /// <summary>
        /// Smallest height the page keeps when it fits without scrolling.
        /// </summary>
        internal int MinimumHeight;

        /// <summary>Empty space kept below the lowest control.</summary>
        internal int ContentPadding = 24;

        internal SettingsContentPanel(NotificationTheme theme)
        {
            _theme = theme;

            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);

            BackColor = _theme.BackgroundTop;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            int offset = 0;
            Control? parent = this;

            while (parent != null && parent is not Form)
            {
                offset += parent.Top;
                parent = parent.Parent;
            }

            Form? form = FindForm();
            int totalHeight = form?.ClientSize.Height ?? Height + offset;

            var gradientRect = new Rectangle(
                0,
                -offset,
                Math.Max(1, Width),
                Math.Max(1, totalHeight));

            using var brush = new LinearGradientBrush(
                gradientRect,
                _theme.BackgroundTop,
                _theme.BackgroundBottom,
                LinearGradientMode.Vertical);

            e.Graphics.FillRectangle(brush, ClientRectangle);
        }

        /// <summary>
        /// Grows the page so every control stays reachable by scrolling.
        /// Called after the page is built and whenever rows are added or
        /// removed at runtime.
        /// </summary>
        internal void FitToContent()
        {
            int required = MinimumHeight;

            foreach (Control control in Controls)
            {
                required = Math.Max(required, control.Bottom + ContentPadding);
            }

            if (Height != required)
                Height = required;
        }
    }
}

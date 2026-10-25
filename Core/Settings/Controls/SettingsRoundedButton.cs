using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace nyxWM
{
    /// <summary>
    /// Small rounded action button used inside the settings content.
    /// </summary>
    internal sealed class SettingsRoundedButton : Control
    {
        private readonly Color _accent;
        private readonly bool _accented;
        private bool _hovered;
        private bool _pressed;

        internal SettingsRoundedButton(Color accent, bool accented)
        {
            _accent = accent;
            _accented = accented;

            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.ResizeRedraw,
                true);

            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            float radius = Height * 0.28f;
            var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);

            using GraphicsPath path = CreateRoundedPath(rect, radius);

            if (_accented)
            {
                Color fill = _accent;

                if (_pressed)
                    fill = Adjust(fill, -0.08f);
                else if (_hovered)
                    fill = Adjust(fill, 0.10f);

                using (var brush = new SolidBrush(fill))
                    graphics.FillPath(brush, path);
            }
            else
            {
                int alpha = (int)Math.Clamp(16f + (_hovered ? 18f : 0f) + (_pressed ? 8f : 0f), 0f, 255f);
                using (var brush = new SolidBrush(Color.FromArgb(alpha, 236, 239, 246)))
                    graphics.FillPath(brush, path);

                using var border = new Pen(SettingsPalette.FieldBorder);
                graphics.DrawPath(border, path);
            }

            Color textColor = _accented
                ? Color.FromArgb(18, 20, 26)
                : SettingsPalette.TextPrimary;

            using var textBrush = new SolidBrush(textColor);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap
            };

            graphics.DrawString(Text, Font, textBrush, rect, format);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hovered = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button == MouseButtons.Left)
            {
                _pressed = true;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (e.Button == MouseButtons.Left && _pressed)
            {
                _pressed = false;
                Invalidate();
            }
        }

        private static Color Adjust(Color color, float amount)
        {
            float clamped = Math.Clamp(amount, -1f, 1f);
            Color target = clamped >= 0f ? Color.White : Color.Black;
            float weight = MathF.Abs(clamped);

            return Color.FromArgb(
                color.A,
                (int)MathF.Round(color.R + (target.R - color.R) * weight),
                (int)MathF.Round(color.G + (target.G - color.G) * weight),
                (int)MathF.Round(color.B + (target.B - color.B) * weight));
        }

        private static GraphicsPath CreateRoundedPath(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            float diameter = Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height));

            if (diameter <= 0f)
            {
                path.AddRectangle(rect);
                return path;
            }

            path.AddArc(rect.Left, rect.Top, diameter, diameter, 180f, 90f);
            path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270f, 90f);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0f, 90f);
            path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90f, 90f);
            path.CloseFigure();
            return path;
        }
    }
}

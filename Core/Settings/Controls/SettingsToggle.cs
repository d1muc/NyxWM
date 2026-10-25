using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace nyxWM
{
    /// <summary>
    /// Capsule toggle switch drawn with the active accent colour.
    /// </summary>
    internal sealed class SettingsToggle : Control
    {
        private readonly Color _accent;
        private bool _checked;
        private bool _hovered;

        internal SettingsToggle(Color accent)
        {
            _accent = accent;

            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.ResizeRedraw,
                true);

            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            TabStop = false;
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal bool Checked
        {
            get => _checked;
            set
            {
                if (_checked == value)
                    return;

                _checked = value;
                Invalidate();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        internal event EventHandler? CheckedChanged;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            float height = Height;
            float width = Width;

            if (height <= 2f || width <= 2f)
                return;

            using (GraphicsPath track = CreateRoundedPath(
                       new RectangleF(0f, 0f, width, height),
                       height / 2f))
            {
                Color trackColor = _checked ? _accent : SettingsPalette.Track;
                if (_hovered)
                {
                    trackColor = Blend(trackColor, Color.White, 0.10f);
                }

                using var brush = new SolidBrush(trackColor);
                graphics.FillPath(brush, track);
            }

            float diameter = height - 6f;
            float knobX = _checked ? width - diameter - 3f : 3f;
            using var knob = new SolidBrush(SettingsPalette.Knob);
            graphics.FillEllipse(knob, knobX, 3f, diameter, diameter);
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
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);

            if (e.Button == MouseButtons.Left)
            {
                Checked = !Checked;
            }
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

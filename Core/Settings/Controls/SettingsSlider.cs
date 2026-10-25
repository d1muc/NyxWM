using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace nyxWM
{
    /// <summary>
    /// Accent-filled slider that can be dragged with the mouse.
    /// </summary>
    internal sealed class SettingsSlider : Control
    {
        private readonly Color _accent;
        private double _minimum;
        private double _maximum = 1.0;
        private double _value;
        private bool _dragging;

        internal SettingsSlider(Color accent)
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
        internal double Minimum
        {
            get => _minimum;
            set
            {
                _minimum = value;
                if (_maximum <= _minimum)
                    _maximum = _minimum + 1.0;

                Value = _value;
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal double Maximum
        {
            get => _maximum;
            set
            {
                _maximum = Math.Max(value, _minimum + 0.0001);
                Value = _value;
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal double Value
        {
            get => _value;
            set
            {
                double clamped = Math.Clamp(value, _minimum, _maximum);
                if (Math.Abs(clamped - _value) < 0.000001)
                    return;

                _value = clamped;
                Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        internal event EventHandler? ValueChanged;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            float centerY = Height / 2f;
            float trackHeight = MathF.Max(3f, Height * 0.14f);
            float thumbRadius = MathF.Max(6f, MathF.Min(Height * 0.38f, 11f));
            float usable = MathF.Max(1f, Width - thumbRadius * 2f);

            var trackRect = new RectangleF(0f, centerY - trackHeight / 2f, Width, trackHeight);
            using (GraphicsPath track = CreateRoundedPath(trackRect, trackHeight / 2f))
            using (var trackBrush = new SolidBrush(SettingsPalette.Track))
            {
                graphics.FillPath(trackBrush, track);
            }

            double range = Math.Max(0.0001, _maximum - _minimum);
            float progress = (float)((_value - _minimum) / range);
            float fillWidth = MathF.Max(trackHeight, Width * progress);

            if (fillWidth > 0f)
            {
                var fillRect = new RectangleF(0f, centerY - trackHeight / 2f, fillWidth, trackHeight);
                using GraphicsPath fill = CreateRoundedPath(fillRect, trackHeight / 2f);
                using var fillBrush = new SolidBrush(_accent);
                graphics.FillPath(fillBrush, fill);
            }

            float thumbX = thumbRadius + usable * progress;
            using (var glow = new SolidBrush(Color.FromArgb(48, _accent)))
            {
                graphics.FillEllipse(
                    glow,
                    thumbX - thumbRadius - 2f,
                    centerY - thumbRadius - 2f,
                    (thumbRadius + 2f) * 2f,
                    (thumbRadius + 2f) * 2f);
            }

            using (var knob = new SolidBrush(SettingsPalette.Knob))
            {
                graphics.FillEllipse(
                    knob,
                    thumbX - thumbRadius,
                    centerY - thumbRadius,
                    thumbRadius * 2f,
                    thumbRadius * 2f);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button != MouseButtons.Left)
                return;

            _dragging = true;
            Capture = true;
            SetFromX(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (_dragging)
            {
                SetFromX(e.X);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (e.Button == MouseButtons.Left)
            {
                _dragging = false;
                Capture = false;
            }
        }

        private void SetFromX(int x)
        {
            float thumbRadius = MathF.Max(6f, MathF.Min(Height * 0.38f, 11f));
            float usable = MathF.Max(1f, Width - thumbRadius * 2f);
            float progress = Math.Clamp((x - thumbRadius) / usable, 0f, 1f);

            Value = _minimum + (_maximum - _minimum) * progress;
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

using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace nyxWM
{
    /// <summary>
    /// Segmented pill selector used for two- and three-way choices such as the
    /// theme mode and the corner style. The selected segment glides with the
    /// accent colour.
    /// </summary>
    internal sealed class SettingsSegmented : Control
    {
        private readonly NotificationTheme _theme;
        private readonly string[] _options;
        private int _selectedIndex;
        private int _hoverIndex = -1;

        internal SettingsSegmented(NotificationTheme theme, string[] options)
        {
            _theme = theme ?? throw new ArgumentNullException(nameof(theme));
            _options = options ?? throw new ArgumentNullException(nameof(options));

            if (_options.Length == 0)
                throw new ArgumentException("At least one option is required.", nameof(options));

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
        internal int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                int clamped = Math.Clamp(value, 0, _options.Length - 1);
                if (clamped == _selectedIndex)
                    return;

                _selectedIndex = clamped;
                Invalidate();
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        internal string SelectedOption =>
            _selectedIndex >= 0 && _selectedIndex < _options.Length
                ? _options[_selectedIndex]
                : string.Empty;

        internal event EventHandler? SelectedIndexChanged;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var track = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            float radius = track.Height / 2f;

            using (GraphicsPath trackPath = CreateRoundedPath(track, radius))
            using (var trackBrush = new SolidBrush(_theme.Track))
            {
                graphics.FillPath(trackBrush, trackPath);
            }

            float segmentWidth = (Width - 4f) / _options.Length;
            var selected = new RectangleF(
                2f + segmentWidth * _selectedIndex,
                2f,
                MathF.Max(1f, segmentWidth - 2f),
                Height - 4f);

            using (GraphicsPath selectedPath = CreateRoundedPath(selected, radius - 2f))
            using (var selectedBrush = new SolidBrush(_theme.Accent))
            {
                graphics.FillPath(selectedBrush, selectedPath);
            }

            using var selectedText = new SolidBrush(_theme.OnAccent);
            using var normalText = new SolidBrush(_theme.TextMuted);
            using var hoverText = new SolidBrush(_theme.TextPrimary);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            };

            for (int i = 0; i < _options.Length; i++)
            {
                var cell = new RectangleF(2f + segmentWidth * i, 0f, segmentWidth, Height);
                bool isSelected = i == _selectedIndex;
                Brush brush = isSelected
                    ? selectedText
                    : i == _hoverIndex
                        ? hoverText
                        : normalText;

                graphics.DrawString(_options[i], Font, brush, cell, format);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            int index = HitTest(e.X);
            if (index == _hoverIndex)
                return;

            _hoverIndex = index;
            Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);

            if (_hoverIndex >= 0)
            {
                _hoverIndex = -1;
                Invalidate();
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);

            if (e.Button != MouseButtons.Left)
                return;

            int index = HitTest(e.X);
            if (index >= 0)
                SelectedIndex = index;
        }

        private int HitTest(int x)
        {
            if (Width <= 0 || x < 0 || x >= Width)
                return -1;

            float segmentWidth = (Width - 4f) / _options.Length;
            return Math.Clamp((int)((x - 2f) / segmentWidth), 0, _options.Length - 1);
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

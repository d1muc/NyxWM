using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace nyxWM
{
    /// <summary>
    /// Owner-drawn, scrollable application list used by the Rofi menu. The
    /// search field keeps keyboard focus, so this control only paints rows and
    /// reports mouse selection. The selection highlight glides between rows.
    /// </summary>
    internal sealed class RofiAppList : Control
    {
        private const float RowHeightDip = 52f;
        private const float IconSizeDip = 32f;
        private const float IconRadiusDip = 9f;
        private const float ScrollbarWidthDip = 5f;
        private const float ScrollbarMarginDip = 4f;
        private const float SelectionSmoothingPerSecond = 26f;

        private readonly NotificationTheme _theme;
        private readonly float _scale;
        private readonly Font _nameFont;
        private readonly Font _metaFont;
        private readonly Font _badgeFont;
        private readonly Font _iconFont;
        private readonly Dictionary<string, Bitmap?> _iconCache =
            new(StringComparer.OrdinalIgnoreCase);

        private IReadOnlyList<RofiApp> _items = Array.Empty<RofiApp>();
        private int _selectedIndex = -1;
        private int _topIndex;
        private int _hoverIndex = -1;

        private float _highlightTop;
        private float _highlightHeight;
        private bool _highlightReady;

        internal RofiAppList(NotificationTheme theme, float scale)
        {
            _theme = theme ?? throw new ArgumentNullException(nameof(theme));
            _scale = Math.Clamp(scale, 0.75f, 3f);
            _nameFont = new Font("Segoe UI", 10.5f, FontStyle.Regular, GraphicsUnit.Point);
            _metaFont = new Font("Segoe UI", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
            _badgeFont = new Font("Segoe UI", 7.75f, FontStyle.Regular, GraphicsUnit.Point);
            _iconFont = new Font("Segoe UI", 9.5f, FontStyle.Bold, GraphicsUnit.Point);

            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.ResizeRedraw,
                true);

            BackColor = Color.Transparent;
            TabStop = false;
            Cursor = Cursors.Default;
        }

        internal event Action<RofiApp, bool>? ItemActivated;

        internal int SelectedIndex => _selectedIndex;
        internal int VisibleRowCount => Math.Max(1, Height / ItemHeight);

        internal bool HasSelection =>
            _selectedIndex >= 0 && _selectedIndex < _items.Count;

        internal RofiApp? SelectedItem => HasSelection ? _items[_selectedIndex] : null;

        private int ItemHeight => Math.Max(1, (int)MathF.Round(RowHeightDip * _scale));

        internal void SetItems(IReadOnlyList<RofiApp> items)
        {
            _items = items ?? Array.Empty<RofiApp>();
            _selectedIndex = _items.Count > 0 ? 0 : -1;
            _topIndex = 0;
            _hoverIndex = -1;
            SnapHighlight();
            Invalidate();
        }

        internal void MoveSelection(int delta)
        {
            if (_items.Count == 0)
                return;

            int current = _selectedIndex < 0 ? 0 : _selectedIndex;
            int next = Math.Clamp(current + delta, 0, _items.Count - 1);

            if (next != _selectedIndex)
            {
                _selectedIndex = next;
                Invalidate();
            }

            EnsureSelectionVisible();
        }

        internal void ActivateSelected(bool asAdministrator)
        {
            RofiApp? item = SelectedItem;
            if (item != null)
                ItemActivated?.Invoke(item, asAdministrator);
        }

        /// <summary>
        /// Eases the selection pill toward the selected row.
        /// </summary>
        internal void Advance(float dtMs)
        {
            if (_items.Count == 0 || _selectedIndex < 0)
            {
                _highlightReady = false;
                return;
            }

            float targetTop = (_selectedIndex - _topIndex) * ItemHeight + 2f * _scale;
            float targetHeight = ItemHeight - 4f * _scale;

            if (!_highlightReady)
            {
                _highlightTop = targetTop;
                _highlightHeight = targetHeight;
                _highlightReady = true;
                Invalidate();
                return;
            }

            float factor = 1f - MathF.Exp(-SelectionSmoothingPerSecond * dtMs / 1000f);
            float nextTop = _highlightTop + (targetTop - _highlightTop) * factor;
            float nextHeight = _highlightHeight + (targetHeight - _highlightHeight) * factor;

            bool settled = MathF.Abs(nextTop - targetTop) < 0.35f
                           && MathF.Abs(nextHeight - targetHeight) < 0.35f;

            float appliedTop = settled ? targetTop : nextTop;
            float appliedHeight = settled ? targetHeight : nextHeight;

            bool moved = MathF.Abs(appliedTop - _highlightTop) > 0.01f
                         || MathF.Abs(appliedHeight - _highlightHeight) > 0.01f;

            _highlightTop = appliedTop;
            _highlightHeight = appliedHeight;

            if (moved)
                Invalidate();
        }

        private void SnapHighlight()
        {
            if (_selectedIndex < 0)
            {
                _highlightReady = false;
                return;
            }

            _highlightTop = (_selectedIndex - _topIndex) * ItemHeight + 2f * _scale;
            _highlightHeight = ItemHeight - 4f * _scale;
            _highlightReady = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            int itemHeight = ItemHeight;
            int visible = VisibleRowCount;
            float scrollReserve = _items.Count > visible
                ? (ScrollbarWidthDip + ScrollbarMarginDip * 2f) * _scale
                : 0f;
            float rowWidth = MathF.Max(1f, Width - scrollReserve);

            DrawHighlight(graphics, rowWidth);

            for (int i = _topIndex; i < _items.Count; i++)
            {
                float y = (i - _topIndex) * itemHeight;
                if (y >= Height)
                    break;

                bool selected = i == _selectedIndex;
                bool hovered = i == _hoverIndex && !selected;

                if (hovered)
                    DrawHover(graphics, rowWidth, y, itemHeight);

                DrawRow(graphics, i, itemHeight, rowWidth, y, selected);
            }

            if (_items.Count == 0)
                DrawEmptyState(graphics);

            DrawScrollbar(graphics, visible);
        }

        private void DrawHighlight(Graphics graphics, float rowWidth)
        {
            if (!_highlightReady || _selectedIndex < 0)
                return;

            var pill = new RectangleF(
                2f * _scale,
                _highlightTop,
                MathF.Max(1f, rowWidth - 4f * _scale),
                MathF.Max(1f, _highlightHeight));

            Color left = Color.FromArgb(74, _theme.Accent);
            Color right = Color.FromArgb(10, _theme.Accent);

            using (GraphicsPath path = CreateRoundedPath(pill, 10f * _scale))
            using (var fill = new LinearGradientBrush(pill, left, right, LinearGradientMode.Horizontal))
            {
                graphics.FillPath(fill, path);

                using var border = new Pen(Color.FromArgb(58, _theme.Accent), MathF.Max(1f, _scale));
                graphics.DrawPath(border, path);
            }

            float barHeight = pill.Height * 0.5f;
            var bar = new RectangleF(
                2f * _scale,
                pill.Top + (pill.Height - barHeight) / 2f,
                MathF.Max(2f, 3f * _scale),
                barHeight);

            using (GraphicsPath barPath = CreateRoundedPath(bar, bar.Width / 2f))
            using (var barBrush = new SolidBrush(_theme.Accent))
            {
                graphics.FillPath(barBrush, barPath);
            }
        }

        private void DrawHover(Graphics graphics, float rowWidth, float y, int itemHeight)
        {
            var pill = new RectangleF(
                2f * _scale,
                y + 2f * _scale,
                MathF.Max(1f, rowWidth - 4f * _scale),
                itemHeight - 4f * _scale);

            using GraphicsPath path = CreateRoundedPath(pill, 10f * _scale);
            using var fill = new SolidBrush(Color.FromArgb(12, 255, 255, 255));
            graphics.FillPath(fill, path);
        }

        private void DrawRow(
            Graphics graphics,
            int index,
            int itemHeight,
            float rowWidth,
            float y,
            bool selected)
        {
            RofiApp app = _items[index];

            float iconSize = IconSizeDip * _scale;
            float iconLeft = 12f * _scale;
            float iconTop = y + (itemHeight - iconSize) / 2f;
            var iconRect = new RectangleF(iconLeft, iconTop, iconSize, iconSize);

            DrawIcon(graphics, app, iconRect, selected);

            float textLeft = iconLeft + iconSize + 12f * _scale;
            float badgeWidth = selected ? MeasureEnterBadge(graphics) : 0f;
            float textWidth = MathF.Max(
                1f,
                rowWidth - textLeft - 12f * _scale - (selected ? badgeWidth + 10f * _scale : 0f));

            var nameRect = new RectangleF(textLeft, y + 9f * _scale, textWidth, 20f * _scale);
            var categoryRect = new RectangleF(textLeft, y + 29f * _scale, textWidth, 16f * _scale);

            using (var nameBrush = new SolidBrush(
                       selected ? Color.FromArgb(248, 250, 253) : Color.FromArgb(216, 223, 234)))
            using (var nameFormat = new StringFormat
                   {
                       Alignment = StringAlignment.Near,
                       LineAlignment = StringAlignment.Center,
                       Trimming = StringTrimming.EllipsisCharacter,
                       FormatFlags = StringFormatFlags.NoWrap
                   })
            {
                graphics.DrawString(app.Name, _nameFont, nameBrush, nameRect, nameFormat);
            }

            if (!string.IsNullOrWhiteSpace(app.Category))
            {
                using var metaBrush = new SolidBrush(
                    selected ? Color.FromArgb(172, 188, 172) : Color.FromArgb(120, 132, 150));
                using var metaFormat = new StringFormat
                {
                    Alignment = StringAlignment.Near,
                    LineAlignment = StringAlignment.Center,
                    Trimming = StringTrimming.EllipsisCharacter,
                    FormatFlags = StringFormatFlags.NoWrap
                };

                graphics.DrawString(app.Category, _metaFont, metaBrush, categoryRect, metaFormat);
            }

            if (selected)
                DrawEnterBadge(graphics, rowWidth, y, itemHeight);
        }

        private void DrawIcon(Graphics graphics, RofiApp app, RectangleF iconRect, bool selected)
        {
            Bitmap? image = TryGetIcon(app);
            float radius = IconRadiusDip * _scale;

            using GraphicsPath iconPath = CreateRoundedPath(iconRect, radius);

            if (image != null)
            {
                GraphicsState state = graphics.Save();
                graphics.SetClip(iconPath);
                graphics.DrawImage(image, iconRect);
                graphics.Restore(state);

                using var iconBorder = new Pen(Color.FromArgb(46, 255, 255, 255), MathF.Max(1f, _scale));
                graphics.DrawPath(iconBorder, iconPath);
                return;
            }

            int hash = StableHash(app.Name);
            float lift = 0.10f + (hash & 7) * 0.018f;
            Color top = Blend(_theme.Accent, Color.White, lift);
            Color bottom = Blend(_theme.Accent, Color.Black, selected ? 0.42f : 0.54f);

            using (var fill = new LinearGradientBrush(iconRect, top, bottom, LinearGradientMode.Vertical))
            {
                graphics.FillPath(fill, iconPath);
            }

            using (var border = new Pen(Color.FromArgb(selected ? 90 : 48, 255, 255, 255), MathF.Max(1f, _scale)))
            {
                graphics.DrawPath(border, iconPath);
            }

            string initial = app.Name.Length > 0
                ? char.ToUpperInvariant(app.Name[0]).ToString()
                : "?";

            using var initialBrush = new SolidBrush(Color.FromArgb(245, 248, 252));
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            graphics.DrawString(initial, _iconFont, initialBrush, iconRect, format);
        }

        private Bitmap? TryGetIcon(RofiApp app)
        {
            string? path = app.Path;
            if (string.IsNullOrWhiteSpace(path))
                return null;

            if (_iconCache.TryGetValue(path, out Bitmap? cached))
                return cached;

            Bitmap? image = null;

            try
            {
                if (System.IO.File.Exists(path))
                {
                    using Icon? icon = Icon.ExtractAssociatedIcon(path);
                    if (icon != null)
                        image = icon.ToBitmap();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Rofi menu: failed to read icon for '{app.Name}': {ex.Message}");
            }

            _iconCache[path] = image;
            return image;
        }

        private float MeasureEnterBadge(Graphics graphics)
        {
            SizeF size = graphics.MeasureString("Enter ↵", _badgeFont);
            return size.Width + 14f * _scale;
        }

        private void DrawEnterBadge(Graphics graphics, float rowWidth, float y, int itemHeight)
        {
            string text = "Enter ↵";
            float width = MeasureEnterBadge(graphics);
            float height = 22f * _scale;
            float left = rowWidth - width - 12f * _scale;
            float top = y + (itemHeight - height) / 2f;

            var badge = new RectangleF(left, top, width, height);

            using (GraphicsPath path = CreateRoundedPath(badge, height / 2f))
            using (var fill = new SolidBrush(Color.FromArgb(16, 255, 255, 255)))
            {
                graphics.FillPath(fill, path);

                using var border = new Pen(Color.FromArgb(26, 255, 255, 255), MathF.Max(1f, _scale));
                graphics.DrawPath(border, path);
            }

            using var brush = new SolidBrush(Color.FromArgb(158, 168, 186));
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            graphics.DrawString(text, _badgeFont, brush, badge, format);
        }

        private void DrawEmptyState(Graphics graphics)
        {
            using var brush = new SolidBrush(Color.FromArgb(120, 130, 146));
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            graphics.DrawString(
                "No applications found",
                _nameFont,
                brush,
                new RectangleF(0f, 0f, Width, Height),
                format);
        }

        private void DrawScrollbar(Graphics graphics, int visible)
        {
            int count = _items.Count;
            if (count <= visible || count == 0)
                return;

            float width = ScrollbarWidthDip * _scale;
            float margin = ScrollbarMarginDip * _scale;
            float x = Width - width - margin;
            var track = new RectangleF(x, margin, width, Height - margin * 2f);

            if (track.Height <= 0f)
                return;

            using (GraphicsPath trackPath = CreateRoundedPath(track, width / 2f))
            using (var trackBrush = new SolidBrush(Color.FromArgb(22, 255, 255, 255)))
            {
                graphics.FillPath(trackBrush, trackPath);
            }

            float ratio = visible / (float)count;
            float thumbHeight = MathF.Min(
                track.Height,
                MathF.Max(26f * _scale, track.Height * ratio));
            int maxTop = Math.Max(1, count - visible);
            float progress = Math.Clamp(_topIndex / (float)maxTop, 0f, 1f);
            float thumbTop = track.Top + (track.Height - thumbHeight) * progress;

            var thumb = new RectangleF(x, thumbTop, width, thumbHeight);

            using GraphicsPath thumbPath = CreateRoundedPath(thumb, width / 2f);
            using var thumbBrush = new SolidBrush(Color.FromArgb(140, _theme.Accent));
            graphics.FillPath(thumbBrush, thumbPath);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            int index = HitTest(e.Y);
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
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button != MouseButtons.Left)
                return;

            int index = HitTest(e.Y);
            if (index < 0)
                return;

            _selectedIndex = index;
            EnsureSelectionVisible();
            Invalidate();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);

            if (e.Button == MouseButtons.Left)
                ActivateSelected(asAdministrator: false);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            MoveSelection(e.Delta > 0 ? -3 : 3);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            EnsureSelectionVisible();
        }

        private int HitTest(int y)
        {
            if (_items.Count == 0)
                return -1;

            int row = Math.Max(0, y) / ItemHeight;
            int index = _topIndex + row;

            return index < _items.Count ? index : -1;
        }

        private void EnsureSelectionVisible()
        {
            int visible = VisibleRowCount;
            int count = _items.Count;
            int maxTop = Math.Max(0, count - visible);
            int previousTop = _topIndex;

            if (_selectedIndex >= 0)
            {
                if (_selectedIndex < _topIndex)
                    _topIndex = _selectedIndex;
                else if (_selectedIndex >= _topIndex + visible)
                    _topIndex = _selectedIndex - visible + 1;
            }

            _topIndex = Math.Clamp(_topIndex, 0, maxTop);

            // Scrolling changes every row's position, so the pill has to jump
            // with it. Within one viewport it stays animated.
            if (_topIndex != previousTop && _highlightReady)
                SnapHighlight();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _nameFont.Dispose();
                _metaFont.Dispose();
                _badgeFont.Dispose();
                _iconFont.Dispose();

                foreach (Bitmap? image in _iconCache.Values)
                    image?.Dispose();

                _iconCache.Clear();
            }

            base.Dispose(disposing);
        }

        private static int StableHash(string value)
        {
            int hash = 17;
            foreach (char c in value)
                hash = unchecked(hash * 31 + c);

            return Math.Abs(hash);
        }

        private static Color Blend(Color background, Color tint, float amount)
        {
            float clamped = Math.Clamp(amount, 0f, 1f);

            return Color.FromArgb(
                background.A,
                (int)MathF.Round(background.R + (tint.R - background.R) * clamped),
                (int)MathF.Round(background.G + (tint.G - background.G) * clamped),
                (int)MathF.Round(background.B + (tint.B - background.B) * clamped));
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

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace nyxWM
{
    internal sealed class RofiAppList : IDisposable
    {
        private const float RowHeightDip = 52f;
        private const float IconSizeDip = 32f;
        private const float IconRadiusDip = 9f;
        private const float ScrollbarWidthDip = 4f;
        private const float ScrollbarMarginDip = 4f;
        private const float SelectionSmoothingPerSecond = 26f;

        private readonly NotificationTheme _theme;
        private readonly float _scale;
        private readonly Nx.NxLayout _layout;
        private readonly int _columns;
        private readonly bool _smooth = VisualPreferences.SmoothAnimations;
        private readonly Font _nameFont;
        private readonly Font _metaFont;
        private readonly Font _badgeFont;
        private readonly Font _iconFont;
        private readonly Font _menuFont;
        private readonly RofiContextMenu _pinMenu;
        private readonly ToolStripMenuItem _pinAction;
        private readonly Dictionary<string, Bitmap?> _iconCache =
            new(StringComparer.OrdinalIgnoreCase);

        private IReadOnlyList<RofiApp> _items = Array.Empty<RofiApp>();
        private RofiApp? _pinTarget;
        private int _selectedIndex = -1;
        private int _topIndex;
        private int _hoverIndex = -1;

        private float _highlightTop;
        private float _highlightHeight;
        private bool _highlightReady;

        private float _drawWidth;
        private float _drawHeight;

        internal RofiAppList(NotificationTheme theme, float scale)
        {
            _theme = theme ?? throw new ArgumentNullException(nameof(theme));
            _scale = Math.Clamp(scale, 0.75f, 3f);
            _layout = Nx.NxTheme.Current.Layout;
            _columns = _layout.EffectiveColumns;
            _nameFont = VisualPreferences.CreateUiFont("Segoe UI", 10.5f, FontStyle.Regular);
            _metaFont = VisualPreferences.CreateUiFont("Segoe UI", 8.25f, FontStyle.Regular);
            _badgeFont = VisualPreferences.CreateUiFont("Segoe UI", 7.75f, FontStyle.Regular);
            _iconFont = VisualPreferences.CreateUiFont("Segoe UI", 9.5f, FontStyle.Bold);
            _menuFont = VisualPreferences.CreateUiFont("Segoe UI", 9.5f, FontStyle.Regular);

            _pinMenu = new RofiContextMenu(_theme, _scale, _menuFont);
            _pinAction = _pinMenu.AddAction("Pin to top");
            _pinAction.Click += (_, _) => TogglePinnedTarget();
        }

        internal event Action<RofiApp, bool>? ItemActivated;
        internal event Action? PinsChanged;
        internal event Action? Invalidated;

        /// <summary>Increments whenever the visible state changes (highlight, hover, scroll, items).</summary>
        internal int RenderVersion { get; private set; }

        private void BumpVersion() => RenderVersion++;

        internal int SelectedIndex => _selectedIndex;
        internal bool HasActiveAnimation { get; private set; }
        internal bool IsPinMenuOpen => _pinMenu.Visible;

        internal bool HasSelection =>
            _selectedIndex >= 0 && _selectedIndex < _items.Count;

        internal RofiApp? SelectedItem => HasSelection ? _items[_selectedIndex] : null;

        private int ItemHeight => Math.Max(1, (int)MathF.Round(RowHeightDip * _scale));

        /// <summary>Selection step for Up/Down: one row in the active layout.</summary>
        internal int ColumnStep => _columns;

        /// <summary>Items that fit on one page (visible rows times columns).</summary>
        internal int VisibleItems()
        {
            if (_drawHeight <= 0) return _columns;
            return Math.Max(_columns, (int)(_drawHeight / ItemHeight) * _columns);
        }

        private float StaggerPx => _layout.IsZigzag ? _layout.MenuStagger * _scale : 0f;

        private float RowShift(int row) => _layout.IsZigzag && (row & 1) == 1 ? StaggerPx : 0f;

        private float ScrollReserve()
        {
            return _items.Count > VisibleItems()
                ? (ScrollbarWidthDip + ScrollbarMarginDip * 2f) * _scale
                : 0f;
        }

        private float CellWidth(float rowWidth)
        {
            return MathF.Max(1f, (rowWidth - StaggerPx) / _columns);
        }

        internal void SetBounds(float width, float height)
        {
            _drawWidth = MathF.Max(1f, width);
            _drawHeight = MathF.Max(1f, height);
            EnsureSelectionVisible();
        }

        internal void SetItems(IReadOnlyList<RofiApp> items, bool preserveSelection = false)
        {
            string? keepPath = preserveSelection ? SelectedItem?.Path : null;

            _items = RofiPins.Order(items ?? Array.Empty<RofiApp>());

            int selected = 0;
            if (!string.IsNullOrWhiteSpace(keepPath))
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    if (string.Equals(_items[i].Path, keepPath, StringComparison.OrdinalIgnoreCase))
                    {
                        selected = i;
                        break;
                    }
                }
            }

            _selectedIndex = _items.Count > 0 ? selected : -1;
            _topIndex = 0;
            _hoverIndex = -1;
            SnapHighlight();
            EnsureSelectionVisible();
            BumpVersion();
            Invalidated?.Invoke();
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
                Invalidated?.Invoke();
            }

            EnsureSelectionVisible();

            if (!_smooth)
            {
                SnapHighlight();
                Invalidated?.Invoke();
            }
        }

        internal void ActivateSelected(bool asAdministrator)
        {
            RofiApp? item = SelectedItem;
            if (item != null)
                ItemActivated?.Invoke(item, asAdministrator);
        }

        internal RenderTicker.TickResult AdvanceFrame(double dtMs)
        {
            bool moved = Advance((float)dtMs);
            HasActiveAnimation = moved;

            if (_items.Count == 0 || _selectedIndex < 0)
                return RenderTicker.TickResult.Idle;

            return moved ? RenderTicker.TickResult.Active : RenderTicker.TickResult.Idle;
        }

        internal void FlushFrame()
        {
            BumpVersion();
            Invalidated?.Invoke();
        }

        private bool Advance(float dtMs)
        {
            if (_items.Count == 0 || _selectedIndex < 0)
            {
                _highlightReady = false;
                return false;
            }

            int rel = _selectedIndex - _topIndex;
            int row = rel / _columns;
            float targetTop = row * ItemHeight + 2f * _scale;
            float targetHeight = ItemHeight - 4f * _scale;

            if (!_smooth)
            {
                if (!_highlightReady
                    || MathF.Abs(_highlightTop - targetTop) > 0.01f
                    || MathF.Abs(_highlightHeight - targetHeight) > 0.01f)
                {
                    _highlightTop = targetTop;
                    _highlightHeight = targetHeight;
                    _highlightReady = true;
                    return true;
                }

                return false;
            }

            if (!_highlightReady)
            {
                _highlightTop = targetTop;
                _highlightHeight = targetHeight;
                _highlightReady = true;
                return true;
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
                BumpVersion();

            return moved;
        }

        private void SnapHighlight()
        {
            if (_selectedIndex < 0)
            {
                _highlightReady = false;
                return;
            }

            int rel = _selectedIndex - _topIndex;
            int row = rel / _columns;
            _highlightTop = row * ItemHeight + 2f * _scale;
            _highlightHeight = ItemHeight - 4f * _scale;
            _highlightReady = true;
        }

        // ---- Public drawing entry point ----

        internal void Draw(Graphics graphics)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            int itemHeight = ItemHeight;
            int visible = VisibleItems();
            float scrollReserve = _items.Count > visible
                ? (ScrollbarWidthDip + ScrollbarMarginDip * 2f) * _scale
                : 0f;
            float rowWidth = MathF.Max(1f, _drawWidth - scrollReserve);
            float cellW = CellWidth(rowWidth);

            DrawHighlight(graphics, cellW);

            for (int i = _topIndex; i < _items.Count; i++)
            {
                int rel = i - _topIndex;
                int row = rel / _columns;
                float y = row * itemHeight;
                if (y >= _drawHeight)
                    break;

                float x = (rel % _columns) * cellW + RowShift(row);
                bool selected = i == _selectedIndex;
                bool hovered = i == _hoverIndex && !selected;

                if (hovered)
                    DrawHover(graphics, x, cellW, y, itemHeight);

                DrawRow(graphics, i, itemHeight, x, cellW, y, selected);
            }

            if (_items.Count == 0)
                DrawEmptyState(graphics);

            DrawScrollbar(graphics, visible);
        }

        // ---- Mouse input helpers ----

        internal int HitTest(int x, int y)
        {
            if (_items.Count == 0)
                return -1;

            int row = Math.Max(0, y) / ItemHeight;
            float rowWidth = MathF.Max(1f, _drawWidth - ScrollReserve());
            float cellW = CellWidth(rowWidth);
            float shift = RowShift(row);
            int col = (int)((x - shift) / cellW);
            if (col < 0 || col >= _columns)
                return -1;

            int index = _topIndex + row * _columns + col;
            return index < _items.Count ? index : -1;
        }

        internal bool SetHover(int x, int y)
        {
            int index = HitTest(x, y);
            if (index == _hoverIndex)
                return false;

            _hoverIndex = index;
            BumpVersion();
            return true;
        }

        internal bool ClearHover()
        {
            if (_hoverIndex < 0)
                return false;

            _hoverIndex = -1;
            BumpVersion();
            return true;
        }

        internal void HandleMouseDown(int x, int y, MouseButtons button, Point screenLocation)
        {
            int index = HitTest(x, y);
            if (index < 0)
                return;

            if (button == MouseButtons.Right)
            {
                _selectedIndex = index;
                EnsureSelectionVisible();

                if (!_smooth)
                    SnapHighlight();

                BumpVersion();
                Invalidated?.Invoke();
                ShowPinMenu(_items[index], screenLocation);
                return;
            }

            if (button != MouseButtons.Left)
                return;

            _selectedIndex = index;
            EnsureSelectionVisible();

            if (!_smooth)
                SnapHighlight();

            BumpVersion();
            Invalidated?.Invoke();
        }

        internal void HandleMouseUp(int x, int y, MouseButtons button)
        {
            if (button != MouseButtons.Left)
                return;

            int index = HitTest(x, y);
            if (index < 0)
                return;

            _selectedIndex = index;

            if (!_smooth)
                SnapHighlight();

            ActivateSelected(asAdministrator: false);
        }

        internal void HandleMouseWheel(int delta, int x, int y)
        {
            MoveSelection(delta > 0 ? -_columns : _columns);
            SetHover(x, y);
        }

        // ---- Context menu ----

        private void ShowPinMenu(RofiApp app, Point screenLocation)
        {
            _pinTarget = app;
            _pinAction.Text = RofiPins.IsPinned(app.Path)
                ? "Unpin from top"
                : "Pin to top";

            _pinMenu.Show(screenLocation);
        }

        private void TogglePinnedTarget()
        {
            RofiApp? app = _pinTarget;
            _pinTarget = null;

            if (app == null || string.IsNullOrWhiteSpace(app.Path))
                return;

            RofiPins.Toggle(app.Path);
            PinsChanged?.Invoke();
        }

        // ---- Drawing helpers ----

        private void DrawHighlight(Graphics graphics, float cellW)
        {
            if (!_highlightReady || _selectedIndex < 0)
                return;

            int rel = _selectedIndex - _topIndex;
            if (rel < 0) return;
            int row = rel / _columns;
            float x = 2f * _scale + (rel % _columns) * cellW + RowShift(row);

            var pill = new RectangleF(
                x,
                _highlightTop,
                MathF.Max(1f, cellW - 4f * _scale),
                MathF.Max(1f, _highlightHeight));

            Color left = Color.FromArgb(_theme.IsLight ? 54 : 74, _theme.Accent);
            Color right = Color.FromArgb(_theme.IsLight ? 6 : 10, _theme.Accent);

            using (GraphicsPath path = CreateRoundedPath(pill, 10f * _scale))
            using (var fill = new LinearGradientBrush(pill, left, right, LinearGradientMode.Horizontal))
            {
                graphics.FillPath(fill, path);

                using var border = new Pen(
                    Color.FromArgb(_theme.IsLight ? 78 : 58, _theme.Accent),
                    MathF.Max(1f, _scale));
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

        private void DrawHover(Graphics graphics, float x, float cellW, float y, int itemHeight)
        {
            var pill = new RectangleF(
                x + 2f * _scale,
                y + 2f * _scale,
                MathF.Max(1f, cellW - 4f * _scale),
                itemHeight - 4f * _scale);

            using GraphicsPath path = CreateRoundedPath(pill, 10f * _scale);
            using var fill = new SolidBrush(_theme.IsLight
                ? Color.FromArgb(10, 0, 0, 0)
                : Color.FromArgb(12, 255, 255, 255));
            graphics.FillPath(fill, path);
        }

        private void DrawRow(
            Graphics graphics,
            int index,
            int itemHeight,
            float x,
            float cellW,
            float y,
            bool selected)
        {
            RofiApp app = _items[index];

            float iconSize = IconSizeDip * _scale;
            float iconLeft = x + 12f * _scale;
            float iconTop = y + (itemHeight - iconSize) / 2f;
            var iconRect = new RectangleF(iconLeft, iconTop, iconSize, iconSize);

            DrawIcon(graphics, app, iconRect, selected);

            float textLeft = iconLeft + iconSize + 12f * _scale;
            float badgeWidth = selected ? MeasureEnterBadge(graphics) : 0f;
            float textWidth = MathF.Max(
                1f,
                x + cellW - textLeft - 12f * _scale - (selected ? badgeWidth + 10f * _scale : 0f));

            var nameRect = new RectangleF(textLeft, y + 9f * _scale, textWidth, 20f * _scale);
            var categoryRect = new RectangleF(textLeft, y + 29f * _scale, textWidth, 16f * _scale);

            Color nameColor = selected
                ? (_theme.IsLight ? _theme.TextPrimary : Color.FromArgb(248, 250, 253))
                : (_theme.IsLight ? Color.FromArgb(52, 57, 66) : Color.FromArgb(216, 223, 234));

            using (var nameBrush = new SolidBrush(nameColor))
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
                    selected
                        ? (_theme.IsLight
                            ? Color.FromArgb(72, 78, 90)
                            : Color.FromArgb(172, 188, 172))
                        : _theme.TextMuted);
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
                DrawEnterBadge(graphics, x, cellW, y, itemHeight);
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

                using var iconBorder = new Pen(
                    _theme.IsLight
                        ? Color.FromArgb(30, 0, 0, 0)
                        : Color.FromArgb(46, 255, 255, 255),
                    MathF.Max(1f, _scale));
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

            using (var border = new Pen(
                       Color.FromArgb(selected ? 90 : 48, 255, 255, 255),
                       MathF.Max(1f, _scale)))
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

        private void DrawEnterBadge(Graphics graphics, float x, float cellW, float y, int itemHeight)
        {
            string text = "Enter ↵";
            float width = MeasureEnterBadge(graphics);
            float height = 22f * _scale;
            float left = x + cellW - width - 12f * _scale;
            float top = y + (itemHeight - height) / 2f;

            var badge = new RectangleF(left, top, width, height);

            using (GraphicsPath path = CreateRoundedPath(badge, height / 2f))
            using (var fill = new SolidBrush(_theme.IsLight
                       ? Color.FromArgb(10, 0, 0, 0)
                       : Color.FromArgb(16, 255, 255, 255)))
            {
                graphics.FillPath(fill, path);

                using var border = new Pen(
                    _theme.IsLight
                        ? Color.FromArgb(24, 0, 0, 0)
                        : Color.FromArgb(26, 255, 255, 255),
                    MathF.Max(1f, _scale));
                graphics.DrawPath(border, path);
            }

            using var brush = new SolidBrush(_theme.TextMuted);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            graphics.DrawString(text, _badgeFont, brush, badge, format);
        }

        private void DrawEmptyState(Graphics graphics)
        {
            using var brush = new SolidBrush(_theme.TextMuted);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            graphics.DrawString(
                "No applications found",
                _nameFont,
                brush,
                new RectangleF(0f, 0f, _drawWidth, _drawHeight),
                format);
        }

        private void DrawScrollbar(Graphics graphics, int visible)
        {
            int count = _items.Count;
            if (count <= visible || count == 0)
                return;

            float width = ScrollbarWidthDip * _scale;
            float margin = ScrollbarMarginDip * _scale;
            float x = _drawWidth - width - margin;
            var track = new RectangleF(x, margin, width, _drawHeight - margin * 2f);

            if (track.Height <= 0f)
                return;

            using (GraphicsPath trackPath = CreateRoundedPath(track, width / 2f))
            using (var trackBrush = new SolidBrush(_theme.IsLight
                       ? Color.FromArgb(16, 0, 0, 0)
                       : Color.FromArgb(22, 255, 255, 255)))
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

        private void EnsureSelectionVisible()
        {
            int visible = VisibleItems();
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
            // Keep the scroll offset on a row boundary so no half-row shows.
            _topIndex -= _topIndex % _columns;

            if (_topIndex != previousTop && _highlightReady)
                SnapHighlight();
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
            float clamped = Math.Clamp(amount, 0f,1f);

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

        public void Dispose()
        {
            _nameFont.Dispose();
            _metaFont.Dispose();
            _badgeFont.Dispose();
            _iconFont.Dispose();
            _pinMenu.Dispose();
            _menuFont.Dispose();

            foreach (Bitmap? image in _iconCache.Values)
                image?.Dispose();

            _iconCache.Clear();
        }
    }
}
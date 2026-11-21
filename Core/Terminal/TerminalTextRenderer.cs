using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Text;

namespace nyxWM
{
    /// <summary>
    /// Colours one console repaint resolves ANSI values against: the theme
    /// foreground/background of the console itself, the cursor and selection
    /// tints and the sixteen palette entries of the active scheme.
    /// </summary>
    internal readonly struct TerminalPalette
    {
        internal TerminalPalette(
            Color foreground,
            Color background,
            Color cursor,
            Color selection,
            Color[] ansi)
        {
            Foreground = foreground;
            Background = background;
            Cursor = cursor;
            Selection = selection;
            Ansi = ansi ?? throw new ArgumentNullException(nameof(ansi));
        }

        /// <summary>Colour of a cell that carries no explicit foreground.</summary>
        internal Color Foreground { get; }

        /// <summary>Colour behind a cell that carries no explicit background.</summary>
        internal Color Background { get; }

        /// <summary>Cursor tint.</summary>
        internal Color Cursor { get; }

        /// <summary>Selection tint, drawn over the cell backgrounds.</summary>
        internal Color Selection { get; }

        /// <summary>The scheme's native sixteen colours; indices above use the xterm cube.</summary>
        internal Color[] Ansi { get; }
    }

    /// <summary>
    /// Paints a <see cref="AnsiScreenBuffer"/> as text on an integer cell grid.
    ///
    /// Every glyph is drawn on its own cell origin instead of as part of a long
    /// run. That is the whole point: GDI+ lays a run out with its own per-glyph
    /// rounding, so a run drifts away from the grid the pseudoconsole wrapped
    /// against, characters crowd into their neighbours ("glued" letters) and
    /// the final glyph is dropped by trimming. One glyph per cell cannot drift —
    /// a glyph is drawn exactly where its cell is, whatever came before it.
    ///
    /// The font is owned by the caller: <see cref="SetFont"/> accepts a
    /// replacement but this class never disposes one.
    /// </summary>
    internal sealed class TerminalTextRenderer : IDisposable
    {
        /// <summary>Cap on the glyph string cache, so a screen of random runes cannot grow it without bound.</summary>
        private const int MaxCachedGlyphs = 4096;

        private readonly Dictionary<int, string> _glyphs = new();
        private readonly StringBuilder _scratch = new(8);
        private readonly SolidBrush _textBrush = new(Color.Black);
        private readonly SolidBrush _fillBrush = new(Color.Black);
        private readonly Pen _linePen = new(Color.Black);
        private readonly StringFormat _cellFormat;

        private Font _font;
        private TerminalCellMetrics _metrics = new(8, 16);
        private bool _disposed;

        internal TerminalTextRenderer(Font font)
        {
            _font = font ?? throw new ArgumentNullException(nameof(font));
            _cellFormat = CreateCellFormat();
        }

        /// <summary>Cell geometry currently in use. Valid after <see cref="RefreshMetrics"/>.</summary>
        internal TerminalCellMetrics Metrics => _metrics;

        /// <summary>
        /// Takes over a new console font. The previous one is not disposed here;
        /// the caller created it and the caller replaces it.
        /// </summary>
        internal void SetFont(Font font)
        {
            _font = font ?? throw new ArgumentNullException(nameof(font));
            _glyphs.Clear();
        }

        /// <summary>Re-measures the cell for the current font at the graphics' DPI.</summary>
        internal void RefreshMetrics(Graphics graphics)
        {
            if (graphics is null)
                throw new ArgumentNullException(nameof(graphics));

            _metrics = TerminalCellMetrics.Measure(_font, graphics);
        }

        /// <summary>
        /// Draws the visible grid: scrollback lines first, the live screen below
        /// them. <paramref name="rows"/> should come from
        /// <see cref="TerminalCellMetrics.RowsFor"/> so nothing spills into the
        /// console padding.
        /// </summary>
        internal void DrawScreen(
            Graphics graphics,
            AnsiScreenBuffer screen,
            in TerminalPalette palette,
            int originX,
            int originY,
            int columns,
            int rows,
            int bottomLimit,
            float scale)
        {
            if (graphics is null)
                throw new ArgumentNullException(nameof(graphics));

            if (screen is null)
                throw new ArgumentNullException(nameof(screen));

            TerminalCellMetrics metrics = _metrics;
            PixelOffsetMode offsetMode = graphics.PixelOffsetMode;

            // Half-pixel offsets would move every glyph off the grid it was
            // measured against; the grid is the contract, so text opts out.
            graphics.PixelOffsetMode = PixelOffsetMode.None;

            try
            {
                for (int row = 0; row < rows; row++)
                {
                    int y = metrics.RowY(originY, row);
                    if (y > bottomLimit)
                        break;

                    DrawRow(graphics, screen, palette, originX, y, row, columns, scale);
                }
            }
            finally
            {
                graphics.PixelOffsetMode = offsetMode;
            }
        }

        /// <summary>Draws one row as a sequence of same-style runs.</summary>
        private void DrawRow(
            Graphics graphics,
            AnsiScreenBuffer screen,
            in TerminalPalette palette,
            int originX,
            int y,
            int row,
            int columns,
            float scale)
        {
            int column = 0;

            while (column < columns)
            {
                ScreenCell first = screen.GetViewCell(row, column);
                if (first.IsContinuation)
                {
                    column++;
                    continue;
                }

                int runStart = column;

                while (column < columns)
                {
                    ScreenCell current = screen.GetViewCell(row, column);
                    if (current.IsContinuation)
                    {
                        // The trailing half of a wide glyph belongs to its cell.
                        column++;
                        continue;
                    }

                    if (!SameStyle(first, current))
                        break;

                    column++;
                }

                DrawRun(graphics, screen, palette, originX, y, row, runStart, column, scale);
            }
        }

        /// <summary>
        /// Fills a run's background once, then draws its glyphs one cell at a
        /// time, each on its own column origin.
        /// </summary>
        private void DrawRun(
            Graphics graphics,
            AnsiScreenBuffer screen,
            in TerminalPalette palette,
            int originX,
            int y,
            int row,
            int runStart,
            int runEnd,
            float scale)
        {
            ScreenCell first = screen.GetViewCell(row, runStart);
            bool reverse = (first.Attributes & AnsiAttributes.Reverse) != 0;

            Color foreground = Resolve(
                reverse ? first.Background : first.Foreground,
                reverse ? palette.Background : palette.Foreground,
                palette);

            Color background = Resolve(
                reverse ? first.Foreground : first.Background,
                reverse ? palette.Foreground : Color.Transparent,
                palette);

            TerminalCellMetrics metrics = _metrics;

            if (background.A > 0)
            {
                _fillBrush.Color = background;
                graphics.FillRectangle(
                    _fillBrush,
                    metrics.ColumnX(originX, runStart),
                    y,
                    (runEnd - runStart) * metrics.Width,
                    metrics.Height);
            }

            _textBrush.Color = foreground;

            for (int column = runStart; column < runEnd; column++)
            {
                ScreenCell cell = screen.GetViewCell(row, column);
                if (cell.IsContinuation)
                    continue;

                string? glyph = GetGlyph(cell);
                if (glyph is null)
                    continue;

                // No layout rectangle and no trimming: a cell's glyph is never
                // shortened, it is placed and left alone.
                graphics.DrawString(
                    glyph,
                    _font,
                    _textBrush,
                    metrics.ColumnX(originX, column),
                    y,
                    _cellFormat);
            }

            bool underline = (first.Attributes & AnsiAttributes.Underline) != 0;
            bool strikethrough = (first.Attributes & AnsiAttributes.Strikethrough) != 0;

            if (!underline && !strikethrough)
                return;

            float left = metrics.ColumnX(originX, runStart);
            float right = metrics.ColumnX(originX, runEnd);
            float thickness = MathF.Max(1f, MathF.Round(scale));
            float baseline = y + metrics.Height - thickness;

            _linePen.Color = foreground;
            _linePen.Width = thickness;

            if (underline)
                graphics.DrawLine(_linePen, left, baseline, right, baseline);

            if (strikethrough)
            {
                float middle = y + MathF.Floor(metrics.Height / 2f);
                graphics.DrawLine(_linePen, left, middle, right, middle);
            }
        }

        /// <summary>
        /// Draws the selection band. Coordinates are grid cells; the band is
        /// filled on the same integer grid the glyphs sit on.
        /// </summary>
        internal void DrawSelection(
            Graphics graphics,
            in TerminalPalette palette,
            int originX,
            int originY,
            Point start,
            Point end,
            int columns)
        {
            if (graphics is null)
                throw new ArgumentNullException(nameof(graphics));

            TerminalCellMetrics metrics = _metrics;
            _fillBrush.Color = palette.Selection;

            for (int row = start.Y; row <= end.Y; row++)
            {
                int from = row == start.Y ? start.X : 0;
                int to = row == end.Y ? end.X : Math.Max(0, columns - 1);

                graphics.FillRectangle(
                    _fillBrush,
                    metrics.ColumnX(originX, from),
                    metrics.RowY(originY, row),
                    (to - from + 1) * metrics.Width,
                    metrics.Height);
            }
        }

        /// <summary>Draws the caret for a grid cell in the configured style.</summary>
        internal void DrawCursor(
            Graphics graphics,
            in TerminalPalette palette,
            int originX,
            int originY,
            int column,
            int row,
            string cursorStyle,
            float scale)
        {
            if (graphics is null)
                throw new ArgumentNullException(nameof(graphics));

            TerminalCellMetrics metrics = _metrics;
            int x = metrics.ColumnX(originX, column);
            int y = metrics.RowY(originY, row);
            int thickness = Math.Max(1, (int)MathF.Round(2f * scale));

            switch (cursorStyle)
            {
                case TerminalPreferences.CursorLine:
                    _fillBrush.Color = palette.Cursor;
                    graphics.FillRectangle(_fillBrush, x, y, thickness, metrics.Height);
                    break;

                case TerminalPreferences.CursorUnderline:
                    _fillBrush.Color = palette.Cursor;
                    graphics.FillRectangle(
                        _fillBrush,
                        x,
                        y + metrics.Height - thickness,
                        metrics.Width,
                        thickness);
                    break;

                default:
                    _fillBrush.Color = Color.FromArgb(96, palette.Cursor);
                    graphics.FillRectangle(_fillBrush, x, y, metrics.Width, metrics.Height);
                    break;
            }
        }

        /// <summary>
        /// Draws a status message ("starting shell...", a startup error) on the
        /// console's first row, clipped to the grid rather than trimmed with an
        /// ellipsis so the text stays where the eye expects it.
        /// </summary>
        internal void DrawMessage(
            Graphics graphics,
            string message,
            Color color,
            int originX,
            int originY,
            int columns)
        {
            if (graphics is null)
                throw new ArgumentNullException(nameof(graphics));

            if (string.IsNullOrEmpty(message))
                return;

            int limit = Math.Min(message.Length, Math.Max(1, columns));
            string text = limit < message.Length ? message[..limit] : message;

            _textBrush.Color = color;
            graphics.DrawString(text, _font, _textBrush, originX, originY, _cellFormat);
        }

        /// <summary>
        /// The glyph string for a cell, or <c>null</c> for a blank cell that
        /// needs no drawing (space, an empty cell, a control byte). Cached per
        /// rune: a repaint asks for the same few hundred strings thousands of
        /// times and must not allocate them each time.
        /// </summary>
        private string? GetGlyph(ScreenCell cell)
        {
            int value = cell.Rune.Value;
            if (value == 0 || cell.Rune == new Rune(' '))
                return null;

            if (cell.Combining is null)
            {
                if (_glyphs.TryGetValue(value, out string? cached))
                    return cached;

                if (_glyphs.Count >= MaxCachedGlyphs)
                    _glyphs.Clear();

                string created = cell.Rune.ToString();
                _glyphs[value] = created;
                return created;
            }

            // Base plus its combining marks: a rare case, so it is built fresh.
            return _scratch.Clear().Append(cell.Rune.ToString()).Append(cell.Combining).ToString();
        }

        private static Color Resolve(AnsiColor color, Color fallback, in TerminalPalette palette)
        {
            switch (color.Kind)
            {
                case AnsiColorKind.Rgb:
                    return Color.FromArgb(color.Red, color.Green, color.Blue);

                case AnsiColorKind.Indexed:
                    byte index = color.Index;
                    return index < palette.Ansi.Length
                        ? palette.Ansi[index]
                        : TerminalColorScheme.StandardColor(index);

                default:
                    return fallback;
            }
        }

        private static bool SameStyle(ScreenCell left, ScreenCell right) =>
            left.Foreground.Equals(right.Foreground)
            && left.Background.Equals(right.Background)
            && left.Attributes == right.Attributes;

        private static StringFormat CreateCellFormat()
        {
            return new StringFormat(StringFormat.GenericTypographic)
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Near,
                HotkeyPrefix = HotkeyPrefix.None,

                // NoWrap: a single glyph never wraps. LineLimit: never spill onto
                // an implicit second line. No trimming flag: a glyph that is one
                // pixel wider than its cell must still be drawn in full.
                FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.LineLimit
            };
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _cellFormat.Dispose();
            _textBrush.Dispose();
            _fillBrush.Dispose();
            _linePen.Dispose();
            _glyphs.Clear();
        }
    }
}

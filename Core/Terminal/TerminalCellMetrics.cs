using System;
using System.Drawing;
using System.Drawing.Text;

namespace nyxWM
{
    /// <summary>
    /// Geometry of a single character cell, in whole device pixels.
    ///
    /// Every coordinate the terminal uses — glyph origins, background fills,
    /// cursor, selection, mouse hit testing — is derived from these two
    /// integers. Fractional cell sizes are what make a terminal look broken:
    /// rows land on half pixels, ClearType rounds each glyph to a different
    /// pixel than its neighbours, run starts stop matching the grid and the
    /// column count no longer agrees with the pseudoconsole. Integer metrics
    /// keep the grid, the drawing and the pseudoconsole on the same page.
    /// </summary>
    internal readonly struct TerminalCellMetrics
    {
        /// <summary>
        /// Characters measured to find the advance. A single glyph can be
        /// rounded by up to half a pixel; averaging a long run of the widest
        /// monospace glyph recovers the real advance within ~0.03 px.
        /// </summary>
        private const int SampleLength = 32;

        /// <summary>Glyph used for the sample: the canonical cell marker.</summary>
        private const char SampleGlyph = 'M';

        internal TerminalCellMetrics(int width, int height)
        {
            Width = Math.Max(1, width);
            Height = Math.Max(1, height);
        }

        /// <summary>Cell advance in pixels; the horizontal grid pitch.</summary>
        internal int Width { get; }

        /// <summary>Cell line height in pixels; the vertical grid pitch.</summary>
        internal int Height { get; }

        /// <summary>
        /// Measures the cell of <paramref name="font"/> with the same GDI+
        /// typographic layout the renderer draws glyphs with, so a run of
        /// characters is exactly as wide as the cells it occupies.
        /// </summary>
        internal static TerminalCellMetrics Measure(Font font, Graphics graphics)
        {
            if (font is null)
                throw new ArgumentNullException(nameof(font));

            if (graphics is null)
                throw new ArgumentNullException(nameof(graphics));

            int width = MeasureAdvance(font, graphics);
            // Ceiling, not round: GetHeight is the font's whole line box, so
            // rounding it down shaves the fraction of a pixel a descender
            // (g, y, p, ц, щ) needs below the baseline, and the next row's
            // background then paints over it. A row one pixel too tall only
            // costs vertical space; a row too short clips.
            int height = (int)MathF.Ceiling(font.GetHeight(graphics));

            return new TerminalCellMetrics(width, height);
        }

        private static int MeasureAdvance(Font font, Graphics graphics)
        {
            try
            {
                string sample = new string(SampleGlyph, SampleLength);
                SizeF measured = graphics.MeasureString(
                    sample,
                    font,
                    int.MaxValue,
                    StringFormat.GenericTypographic);

                return (int)MathF.Round(measured.Width / SampleLength);
            }
            catch (ArgumentException)
            {
                // A font the text renderer cannot measure is unusable; fall back
                // to a sane monospace cell instead of throwing mid-paint.
                return 0;
            }
        }

        /// <summary>Whole columns that fit into the given pixel width.</summary>
        internal int ColumnsFor(float pixelWidth) =>
            Math.Max(1, (int)MathF.Floor(pixelWidth / Width));

        /// <summary>Whole rows that fit into the given pixel height.</summary>
        internal int RowsFor(float pixelHeight) =>
            Math.Max(1, (int)MathF.Floor(pixelHeight / Height));

        /// <summary>Left edge of a column, snapped to the pixel grid.</summary>
        internal int ColumnX(int originX, int column) => originX + column * Width;

        /// <summary>Top edge of a row, snapped to the pixel grid.</summary>
        internal int RowY(int originY, int row) => originY + row * Height;
    }
}

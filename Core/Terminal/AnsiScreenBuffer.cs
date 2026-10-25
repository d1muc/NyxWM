using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;

namespace nyxWM
{
    /// <summary>Colour model carried inside <see cref="ScreenCell"/>.</summary>
    internal enum AnsiColorKind : byte
    {
        Default,
        Indexed,
        Rgb
    }

    /// <summary>
    /// A terminal colour: the theme default, an xterm palette index (0-255) or a
    /// 24-bit RGB value. <see cref="Default"/> is simply <c>default</c>.
    /// </summary>
    internal readonly struct AnsiColor : IEquatable<AnsiColor>
    {
        private AnsiColor(AnsiColorKind kind, byte index, byte red, byte green, byte blue)
        {
            Kind = kind;
            Index = index;
            Red = red;
            Green = green;
            Blue = blue;
        }

        public AnsiColorKind Kind { get; }
        public byte Index { get; }
        public byte Red { get; }
        public byte Green { get; }
        public byte Blue { get; }

        public static AnsiColor Default => default;
        public static AnsiColor Indexed(byte index) => new(AnsiColorKind.Indexed, index, 0, 0, 0);
        public static AnsiColor Rgb(byte red, byte green, byte blue) => new(AnsiColorKind.Rgb, 0, red, green, blue);

        public bool Equals(AnsiColor other) =>
            Kind == other.Kind && Index == other.Index && Red == other.Red && Green == other.Green && Blue == other.Blue;

        public override bool Equals(object? obj) => obj is AnsiColor other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Kind, Index, Red, Green, Blue);
    }

    /// <summary>SGR attributes tracked per cell.</summary>
    [Flags]
    internal enum AnsiAttributes : byte
    {
        None = 0,
        Bold = 1,
        Faint = 2,
        Italic = 4,
        Underline = 8,
        Reverse = 16,
        Strikethrough = 32
    }

    /// <summary>
    /// One character cell of the emulated screen. <see cref="IsContinuation"/>
    /// marks the trailing cell of a wide (CJK/emoji) character; a renderer
    /// follows it and skips the cell.
    /// </summary>
    internal readonly struct ScreenCell
    {
        internal ScreenCell(
            Rune rune,
            AnsiColor foreground,
            AnsiColor background,
            AnsiAttributes attributes,
            bool isContinuation)
        {
            Rune = rune;
            Foreground = foreground;
            Background = background;
            Attributes = attributes;
            IsContinuation = isContinuation;
        }

        public Rune Rune { get; }
        public AnsiColor Foreground { get; }
        public AnsiColor Background { get; }
        public AnsiAttributes Attributes { get; }
        public bool IsContinuation { get; }

        public override string ToString() => Rune.ToString();
    }

    /// <summary>
    /// A VT100/ANSI parser that keeps a two-dimensional screen grid. It consumes
    /// the same UTF-16 text <see cref="TerminalSession"/> emits and produces a
    /// cell-accurate picture — cursor movement, scrolling, SGR colours, erase in
    /// line/display, the DEC alternate screen and wide characters all land in
    /// the right column, so commands render exactly as they do on a real
    /// terminal.
    ///
    /// State is kept per instance, so an escape sequence split across two
    /// <see cref="Feed"/> calls is still parsed correctly.
    /// </summary>
    internal sealed class AnsiScreenBuffer
    {
        private enum ParserState
        {
            Ground,
            Escape,
            EscapeIntermediate,
            CsiEntry,
            CsiParam,
            CsiIntermediate,
            Ignore,
            OscString
        }

        private readonly List<int> _parameters = new(16);
        private readonly List<ScreenCell[]> _history = new();

        private const int MaxHistoryLines = 2000;

        private ScreenCell[] _cells;
        private ScreenCell[] _alternateCells;

        private int _columns;
        private int _rows;

        private int _cursorRow;
        private int _cursorColumn;
        private int _savedCursorRow;
        private int _savedCursorColumn;

        private int _scrollTop;
        private int _scrollBottom;

        private AnsiColor _foreground = AnsiColor.Default;
        private AnsiColor _background = AnsiColor.Default;
        private AnsiAttributes _attributes;

        private bool _cursorVisible = true;
        private bool _wrapEnabled = true;
        private bool _wrapPending;
        private bool _alternate;
        private bool _dirty;
        private int _scrollOffset;

        private ParserState _state = ParserState.Ground;
        private int _version;

        internal AnsiScreenBuffer(int columns, int rows)
        {
            _columns = Math.Clamp(columns, 1, 1000);
            _rows = Math.Clamp(rows, 1, 500);
            _cells = NewGrid(_columns, _rows);
            _alternateCells = NewGrid(_columns, _rows);
            _scrollBottom = _rows - 1;
        }

        /// <summary>Raised after <see cref="Feed"/> changed the screen.</summary>
        internal event EventHandler? Changed;

        /// <summary>
        /// Raised when the shell queries the terminal (cursor position, status).
        /// The response text must be written back to the session's stdin, e.g.
        /// <c>session.WriteInput</c>.
        /// </summary>
        internal event Action<string>? ResponseRequested;

        public int Columns => _columns;
        public int Rows => _rows;
        public int CursorRow => _cursorRow;
        public int CursorColumn => _cursorColumn;
        public bool CursorVisible => _cursorVisible;
        public bool UsingAlternateScreen => _alternate;

        /// <summary>Monotonically increases each time the screen changes.</summary>
        public int Version => _version;

        /// <summary>Lines scrolled up from the live screen; 0 means bottom.</summary>
        internal int ScrollOffset => _scrollOffset;

        /// <summary>Total scrollback lines currently retained.</summary>
        internal int HistoryCount => _history.Count;

        /// <summary>Largest valid <see cref="ScrollOffset"/>.</summary>
        internal int MaxScrollOffset => _history.Count;

        /// <summary>Feeds decoded terminal output into the screen grid.</summary>
        internal void Feed(ReadOnlySpan<char> text)
        {
            int index = 0;
            while (index < text.Length)
            {
                OperationStatus status = Rune.DecodeFromUtf16(text[index..], out Rune rune, out int consumed);
                if (consumed <= 0)
                {
                    index++;
                    continue;
                }

                index += consumed;
                if (status != OperationStatus.Done)
                    continue;

                ProcessRune(rune);
            }

            if (_dirty)
            {
                _dirty = false;
                RaiseChanged();
            }
        }

        /// <summary>Scrolls the view through the scrollback; positive scrolls up.</summary>
        internal void Scroll(int lines)
        {
            if (_history.Count == 0)
                return;

            int next = Math.Clamp(_scrollOffset + lines, 0, _history.Count);
            if (next == _scrollOffset)
                return;

            _scrollOffset = next;
            RaiseChanged();
        }

        /// <summary>Returns the view to the live screen.</summary>
        internal void ScrollToBottom()
        {
            if (_scrollOffset == 0)
                return;

            _scrollOffset = 0;
            RaiseChanged();
        }

        /// <summary>
        /// Cell as currently displayed: scrollback lines first, live screen at
        /// the bottom. Rendering must use this instead of <see cref="GetCell"/>.
        /// </summary>
        internal ScreenCell GetViewCell(int row, int column)
        {
            if ((uint)row >= (uint)_rows || (uint)column >= (uint)_columns)
                return default;

            if (_scrollOffset == 0 || _alternate)
                return GetCell(row, column);

            int index = _history.Count - _scrollOffset + row;
            if (index < 0)
                return default;

            if (index < _history.Count)
                return _history[index][column];

            int screenRow = index - _history.Count;
            if ((uint)screenRow >= (uint)_rows)
                return default;

            return Active[screenRow * _columns + column];
        }

        private void RaiseChanged()
        {
            _version++;

            try
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception)
            {
                // A render callback must not break the pipeline.
            }
        }

        /// <summary>Rebuilds the grid at a new size, preserving visible content.</summary>
        internal void Resize(int columns, int rows)
        {
            int width = Math.Clamp(columns, 1, 1000);
            int height = Math.Clamp(rows, 1, 500);

            if (width == _columns && height == _rows)
                return;

            ScreenCell[] newMain = NewGrid(width, height);
            ScreenCell[] newAlternate = NewGrid(width, height);

            CopyGrid(_cells, _columns, _rows, newMain, width, height);
            CopyGrid(_alternateCells, _columns, _rows, newAlternate, width, height);

            _cells = newMain;
            _alternateCells = newAlternate;
            _columns = width;
            _rows = height;

            _cursorRow = Math.Min(_cursorRow, height - 1);
            _cursorColumn = Math.Min(_cursorColumn, width - 1);
            _savedCursorRow = Math.Min(_savedCursorRow, height - 1);
            _savedCursorColumn = Math.Min(_savedCursorColumn, width - 1);
            _scrollTop = 0;
            _scrollBottom = height - 1;
            _wrapPending = false;

            ResizeHistory(width);

            RaiseChanged();
        }

        private void ResizeHistory(int columns)
        {
            for (int index = 0; index < _history.Count; index++)
            {
                ScreenCell[] previous = _history[index];
                ScreenCell[] line = NewGrid(columns, 1);
                Array.Copy(previous, line, Math.Min(previous.Length, columns));
                _history[index] = line;
            }
        }

        /// <summary>Cell at <c>(row, column)</c>, or <c>default</c> out of range.</summary>
        internal ScreenCell GetCell(int row, int column)
        {
            if ((uint)row >= (uint)_rows || (uint)column >= (uint)_columns)
                return default;

            return Active[row * _columns + column];
        }

        private ScreenCell[] Active => _alternate ? _alternateCells : _cells;

        private void ProcessRune(Rune rune)
        {
            int value = rune.Value;

            switch (_state)
            {
                case ParserState.Ground:
                    Ground(rune, value);
                    break;
                case ParserState.Escape:
                    Escape(rune, value);
                    break;
                case ParserState.EscapeIntermediate:
                    _state = value is '[' ? ParserState.CsiEntry : ParserState.Ground;
                    break;
                case ParserState.CsiEntry:
                    CsiEntry(rune, value);
                    break;
                case ParserState.CsiParam:
                    CsiParam(rune, value);
                    break;
                case ParserState.CsiIntermediate:
                    CsiIntermediate(rune, value);
                    break;
                case ParserState.Ignore:
                    if (value is >= 0x40 and <= 0x7E)
                        _state = ParserState.Ground;
                    break;
                case ParserState.OscString:
                    Osc(value);
                    break;
            }
        }

        private void Ground(Rune rune, int value)
        {
            if (value == 0x1B)
            {
                _state = ParserState.Escape;
                return;
            }

            if (value is 0x18 or 0x1A or 0x9C)
                return;

            if (value == 0x9B)
            {
                StartCsi();
                return;
            }

            if (value < 0x20)
            {
                Control(value);
                return;
            }

            if (value == 0x7F)
                return;

            PutRune(rune);
        }

        private void Control(int value)
        {
            switch (value)
            {
                case 0x07: return;                       // BEL
                case 0x08: Backspace(); return;          // BS
                case 0x09: Tab(); return;                // HT
                case 0x0A:
                case 0x0B:
                case 0x0C: LineFeed(); return;           // LF / VT / FF
                case 0x0D: CarriageReturn(); return;     // CR
                default: return;
            }
        }

        private void Escape(Rune rune, int value)
        {
            switch (value)
            {
                case '[': StartCsi(); return;
                case ']': _state = ParserState.OscString; return;
                case 'P':
                case 'X':
                case '^':
                case '_': _state = ParserState.Ignore; return;
                case '(':
                case ')':
                case '#': _state = ParserState.EscapeIntermediate; return;
                case '7': SaveCursor(); _state = ParserState.Ground; return;
                case '8': RestoreCursor(); _state = ParserState.Ground; return;
                case 'D': LineFeed(); _state = ParserState.Ground; return;
                case 'E': CarriageReturn(); LineFeed(); _state = ParserState.Ground; return;
                case 'M': ReverseLineFeed(); _state = ParserState.Ground; return;
                case 'c': ResetAll(); _state = ParserState.Ground; return;
                case '=':
                case '>': _state = ParserState.Ground; return;
                default: _state = ParserState.Ground; return;
            }
        }

        private void StartCsi()
        {
            _parameters.Clear();
            _state = ParserState.CsiEntry;
        }

        private void CsiEntry(Rune rune, int value)
        {
            if (value is >= 0x30 and <= 0x39)
            {
                _parameters.Add(value - '0');
                _state = ParserState.CsiParam;
                return;
            }

            if (value == ';')
            {
                _parameters.Add(0);
                _state = ParserState.CsiParam;
                return;
            }

            if (value is '?' or '>' or '<' or '=')
                return;

            if (value is >= 0x40 and <= 0x7E)
            {
                CsiDispatch((char)value);
                _state = ParserState.Ground;
                return;
            }

            _state = value is >= 0x20 and <= 0x2F ? ParserState.CsiIntermediate : ParserState.Ground;
        }

        private void CsiParam(Rune rune, int value)
        {
            if (value is >= 0x30 and <= 0x39)
            {
                int last = _parameters.Count - 1;
                _parameters[last] = Math.Min(_parameters[last] * 10 + (value - '0'), 10000);
                return;
            }

            if (value == ';')
            {
                _parameters.Add(0);
                return;
            }

            if (value is >= 0x40 and <= 0x7E)
            {
                CsiDispatch((char)value);
                _state = ParserState.Ground;
                return;
            }

            _state = value is >= 0x20 and <= 0x2F ? ParserState.CsiIntermediate : ParserState.Ground;
        }

        private void CsiIntermediate(Rune rune, int value)
        {
            if (value is >= 0x20 and <= 0x2F)
                return;

            if (value is >= 0x40 and <= 0x7E)
                CsiDispatch((char)value);

            _state = ParserState.Ground;
        }

        private void Osc(int value)
        {
            if (value is 0x07 or 0x1B)
                _state = value == 0x1B ? ParserState.Escape : ParserState.Ground;
        }

        private void CsiDispatch(char final)
        {
            switch (final)
            {
                case 'A': CursorUp(Param(0, 1)); return;
                case 'B': CursorDown(Param(0, 1)); return;
                case 'C': CursorForward(Param(0, 1)); return;
                case 'D': CursorBack(Param(0, 1)); return;
                case 'E': CursorNextLine(Param(0, 1)); return;
                case 'F': CursorPreviousLine(Param(0, 1)); return;
                case 'G': MoveToColumn(Param(0, 1)); return;
                case 'H':
                case 'f': MoveTo(Param(0, 1), Param(1, 1)); return;
                case 'J': EraseInDisplay(Param(0, 0)); return;
                case 'K': EraseInLine(Param(0, 0)); return;
                case 'L': InsertLines(Param(0, 1)); return;
                case 'M': DeleteLines(Param(0, 1)); return;
                case 'P': DeleteCharacters(Param(0, 1)); return;
                case '@': InsertCharacters(Param(0, 1)); return;
                case 'S': ScrollUp(Param(0, 1)); return;
                case 'T': ScrollDown(Param(0, 1)); return;
                case 'X': EraseCharacters(Param(0, 1)); return;
                case 'd': MoveToLine(Param(0, 1)); return;
                case 'r': SetScrollRegion(Param(0, 0), Param(1, 0)); return;
                case 'm': SelectGraphicRendition(); return;
                case 'h': SetMode(true); return;
                case 'l': SetMode(false); return;
                case 'n': DeviceStatusReport(); return;
                case 'c': ResponseRequested?.Invoke("\u001b[?6c"); return;
                case 's': SaveCursor(); return;
                case 'u': RestoreCursor(); return;
                default: return;
            }
        }

        private int Param(int index, int fallback)
        {
            if (index >= _parameters.Count)
                return fallback;

            int value = _parameters[index];
            return value == 0 ? fallback : value;
        }

        // ---- cursor movement -----------------------------------------------------

        private void CursorUp(int count) => MoveRow(-count);
        private void CursorDown(int count) => MoveRow(count);

        private void MoveRow(int delta)
        {
            _cursorRow = Math.Clamp(_cursorRow + delta, 0, _rows - 1);
            _dirty = true;
        }

        private void CursorForward(int count)
        {
            _cursorColumn = Math.Clamp(_cursorColumn + count, 0, _columns - 1);
            _dirty = true;
        }

        private void CursorBack(int count)
        {
            _cursorColumn = Math.Clamp(_cursorColumn - count, 0, _columns - 1);
            _dirty = true;
        }

        private void CursorNextLine(int count)
        {
            _cursorRow = Math.Clamp(_cursorRow + count, 0, _rows - 1);
            _cursorColumn = 0;
            _dirty = true;
        }

        private void CursorPreviousLine(int count)
        {
            _cursorRow = Math.Clamp(_cursorRow - count, 0, _rows - 1);
            _cursorColumn = 0;
            _dirty = true;
        }

        private void MoveToColumn(int column)
        {
            _cursorColumn = Math.Clamp(column - 1, 0, _columns - 1);
            _dirty = true;
        }

        private void MoveToLine(int line)
        {
            _cursorRow = Math.Clamp(line - 1, 0, _rows - 1);
            _dirty = true;
        }

        private void MoveTo(int row, int column)
        {
            _cursorRow = Math.Clamp(row - 1, 0, _rows - 1);
            _cursorColumn = Math.Clamp(column - 1, 0, _columns - 1);
            _dirty = true;
        }

        private void SaveCursor()
        {
            _savedCursorRow = _cursorRow;
            _savedCursorColumn = _cursorColumn;
        }

        private void RestoreCursor()
        {
            _cursorRow = Math.Clamp(_savedCursorRow, 0, _rows - 1);
            _cursorColumn = Math.Clamp(_savedCursorColumn, 0, _columns - 1);
            _dirty = true;
        }

        // ---- text plumbing -------------------------------------------------------

        private void PutRune(Rune rune)
        {
            int width = RuneWidth(rune);
            if (width <= 0)
                return;

            if (_wrapPending)
            {
                _cursorColumn = 0;
                LineFeed();
                _wrapPending = false;
            }

            int row = _cursorRow;
            int column = _cursorColumn;

            if (width == 2 && column >= _columns - 1)
            {
                SetCell(row, column, MakeCell(new Rune(' ')));
                column = 0;
                LineFeed();
                row = _cursorRow;
            }

            SetCell(row, column, MakeCell(rune));

            if (width == 2 && column + 1 < _columns)
                SetCell(row, column + 1, ContinuationCell());

            int next = column + width;
            if (next >= _columns)
            {
                _cursorColumn = _columns - 1;
                _wrapPending = _wrapEnabled;
            }
            else
            {
                _cursorColumn = next;
            }
        }

        private void Backspace()
        {
            if (_cursorColumn > 0)
            {
                _cursorColumn--;
                _dirty = true;
            }
        }

        private void Tab()
        {
            int next = (_cursorColumn / 8 + 1) * 8;
            _cursorColumn = Math.Min(next, _columns - 1);
            _dirty = true;
        }

        private void CarriageReturn()
        {
            _cursorColumn = 0;
            _dirty = true;
        }

        private void LineFeed()
        {
            if (_cursorRow == _scrollBottom)
            {
                ScrollRegion(1);
            }
            else if (_cursorRow < _rows - 1)
            {
                _cursorRow++;
            }

            _dirty = true;
        }

        private void ReverseLineFeed()
        {
            if (_cursorRow == _scrollTop)
            {
                ScrollRegion(-1);
            }
            else if (_cursorRow > 0)
            {
                _cursorRow--;
            }

            _dirty = true;
        }

        // ---- screen editing ------------------------------------------------------

        private void EraseInDisplay(int mode)
        {
            ScreenCell[] grid = Active;

            switch (mode)
            {
                case 0:
                    FillRowRange(grid, _cursorRow, _cursorColumn, _columns);
                    for (int row = _cursorRow + 1; row < _rows; row++)
                        FillRow(grid, row);
                    break;
                case 1:
                    FillRowRange(grid, _cursorRow, 0, _cursorColumn + 1);
                    for (int row = 0; row < _cursorRow; row++)
                        FillRow(grid, row);
                    break;
                default:
                    for (int row = 0; row < _rows; row++)
                        FillRow(grid, row);
                    break;
            }

            _dirty = true;
        }

        private void EraseInLine(int mode)
        {
            ScreenCell[] grid = Active;

            switch (mode)
            {
                case 0: FillRowRange(grid, _cursorRow, _cursorColumn, _columns); break;
                case 1: FillRowRange(grid, _cursorRow, 0, _cursorColumn + 1); break;
                default: FillRow(grid, _cursorRow); break;
            }

            _dirty = true;
        }

        private void EraseCharacters(int count)
        {
            ScreenCell[] grid = Active;
            int offset = _cursorRow * _columns;
            int stop = Math.Min(_cursorColumn + count, _columns);
            ScreenCell blank = BlankCell(_background);

            for (int column = _cursorColumn; column < stop; column++)
                grid[offset + column] = blank;

            _dirty = true;
        }

        private void InsertCharacters(int count)
        {
            ScreenCell[] grid = Active;
            int offset = _cursorRow * _columns;
            int move = Math.Min(count, _columns - _cursorColumn);

            for (int column = _columns - 1; column >= _cursorColumn + move; column--)
                grid[offset + column] = grid[offset + column - move];

            ScreenCell blank = BlankCell(_background);
            for (int column = _cursorColumn; column < _cursorColumn + move; column++)
                grid[offset + column] = blank;

            _dirty = true;
        }

        private void DeleteCharacters(int count)
        {
            ScreenCell[] grid = Active;
            int offset = _cursorRow * _columns;
            int move = Math.Min(count, _columns - _cursorColumn);

            for (int column = _cursorColumn; column < _columns - move; column++)
                grid[offset + column] = grid[offset + column + move];

            ScreenCell blank = BlankCell(_background);
            for (int column = _columns - move; column < _columns; column++)
                grid[offset + column] = blank;

            _dirty = true;
        }

        private void InsertLines(int count)
        {
            if (_cursorRow < _scrollTop || _cursorRow > _scrollBottom)
                return;

            ScreenCell[] grid = Active;
            int available = _scrollBottom - _cursorRow + 1;
            count = Math.Min(count, available);

            for (int row = _scrollBottom; row >= _cursorRow + count; row--)
                CopyRow(grid, row - count, row);

            for (int row = _cursorRow; row < _cursorRow + count; row++)
                FillRow(grid, row);

            _dirty = true;
        }

        private void DeleteLines(int count)
        {
            if (_cursorRow < _scrollTop || _cursorRow > _scrollBottom)
                return;

            ScreenCell[] grid = Active;
            int available = _scrollBottom - _cursorRow + 1;
            count = Math.Min(count, available);

            for (int row = _cursorRow; row <= _scrollBottom - count; row++)
                CopyRow(grid, row + count, row);

            for (int row = _scrollBottom - count + 1; row <= _scrollBottom; row++)
                FillRow(grid, row);

            _dirty = true;
        }

        private void ScrollUp(int count) => ScrollRegion(count);

        private void ScrollDown(int count) => ScrollRegion(-count);

        private void ScrollRegion(int lines)
        {
            int top = _scrollTop;
            int bottom = _scrollBottom;
            int height = bottom - top + 1;

            if (height <= 0)
                return;

            lines = Math.Clamp(lines, -height, height);
            if (lines == 0)
                return;

            ScreenCell[] grid = Active;

            if (lines > 0)
            {
                if (!_alternate && top == 0 && bottom == _rows - 1)
                    PushHistory(grid, Math.Min(lines, _rows));

                for (int row = top; row <= bottom - lines; row++)
                    CopyRow(grid, row + lines, row);

                for (int row = bottom - lines + 1; row <= bottom; row++)
                    FillRow(grid, row);
            }
            else
            {
                int count = -lines;
                for (int row = bottom; row >= top + count; row--)
                    CopyRow(grid, row - count, row);

                for (int row = top; row < top + count; row++)
                    FillRow(grid, row);
            }

            _dirty = true;
        }

        private void PushHistory(ScreenCell[] grid, int lines)
        {
            for (int row = 0; row < lines; row++)
            {
                var line = new ScreenCell[_columns];
                Array.Copy(grid, row * _columns, line, 0, _columns);
                _history.Add(line);
            }

            int overflow = _history.Count - MaxHistoryLines;
            if (overflow > 0)
                _history.RemoveRange(0, overflow);

            if (_scrollOffset > 0)
                _scrollOffset = Math.Clamp(_scrollOffset + lines, 0, _history.Count);
        }

        private void SetScrollRegion(int top, int bottom)
        {
            int topRow = top <= 0 ? 0 : top - 1;
            int bottomRow = bottom <= 0 ? _rows - 1 : bottom - 1;

            if (topRow < 0 || bottomRow >= _rows || topRow >= bottomRow)
            {
                _scrollTop = 0;
                _scrollBottom = _rows - 1;
            }
            else
            {
                _scrollTop = topRow;
                _scrollBottom = bottomRow;
            }

            _cursorRow = 0;
            _cursorColumn = 0;
            _dirty = true;
        }

        // ---- SGR / modes ---------------------------------------------------------

        private void SelectGraphicRendition()
        {
            if (_parameters.Count == 0)
            {
                ResetAttributes();
                return;
            }

            int index = 0;
            while (index < _parameters.Count)
            {
                int code = _parameters[index];

                switch (code)
                {
                    case 0: ResetAttributes(); break;
                    case 1: _attributes |= AnsiAttributes.Bold; break;
                    case 2: _attributes |= AnsiAttributes.Faint; break;
                    case 3: _attributes |= AnsiAttributes.Italic; break;
                    case 4: _attributes |= AnsiAttributes.Underline; break;
                    case 5:
                    case 6: _attributes |= AnsiAttributes.Bold; break;
                    case 7: _attributes |= AnsiAttributes.Reverse; break;
                    case 9: _attributes |= AnsiAttributes.Strikethrough; break;
                    case 21:
                    case 22: _attributes &= ~(AnsiAttributes.Bold | AnsiAttributes.Faint); break;
                    case 23: _attributes &= ~AnsiAttributes.Italic; break;
                    case 24: _attributes &= ~AnsiAttributes.Underline; break;
                    case 27: _attributes &= ~AnsiAttributes.Reverse; break;
                    case 29: _attributes &= ~AnsiAttributes.Strikethrough; break;
                    case >= 30 and <= 37: _foreground = AnsiColor.Indexed((byte)(code - 30)); break;
                    case 38: index += SelectExtendedColor(ref _foreground, index); break;
                    case 39: _foreground = AnsiColor.Default; break;
                    case >= 40 and <= 47: _background = AnsiColor.Indexed((byte)(code - 40)); break;
                    case 48: index += SelectExtendedColor(ref _background, index); break;
                    case 49: _background = AnsiColor.Default; break;
                    case >= 90 and <= 97: _foreground = AnsiColor.Indexed((byte)(code - 90 + 8)); break;
                    case >= 100 and <= 107: _background = AnsiColor.Indexed((byte)(code - 100 + 8)); break;
                    default: break;
                }

                index++;
            }

            _dirty = true;
        }

        private int SelectExtendedColor(ref AnsiColor target, int codeIndex)
        {
            if (codeIndex + 1 >= _parameters.Count)
                return 0;

            int type = _parameters[codeIndex + 1];

            if (type == 5 && codeIndex + 2 < _parameters.Count)
            {
                target = AnsiColor.Indexed((byte)Math.Clamp(_parameters[codeIndex + 2], 0, 255));
                return 2;
            }

            if (type == 2 && codeIndex + 4 < _parameters.Count)
            {
                target = AnsiColor.Rgb(
                    (byte)Math.Clamp(_parameters[codeIndex + 2], 0, 255),
                    (byte)Math.Clamp(_parameters[codeIndex + 3], 0, 255),
                    (byte)Math.Clamp(_parameters[codeIndex + 4], 0, 255));
                return 4;
            }

            return 1;
        }

        private void SetMode(bool set)
        {
            foreach (int mode in _parameters)
            {
                switch (mode)
                {
                    case 7: _wrapEnabled = set; _wrapPending = false; break;
                    case 25: _cursorVisible = set; break;
                    case 47:
                    case 1047:
                    case 1049: SetAlternateScreen(set); break;
                    case 1048:
                        if (set) SaveCursor(); else RestoreCursor();
                        break;
                    default: break;
                }
            }

            _dirty = true;
        }

        private void SetAlternateScreen(bool enabled)
        {
            if (_alternate == enabled)
                return;

            _alternate = enabled;
            _scrollOffset = 0;

            if (enabled)
            {
                SaveCursor();
                for (int index = 0; index < _alternateCells.Length; index++)
                    _alternateCells[index] = BlankCell(_background);

                _cursorRow = 0;
                _cursorColumn = 0;
                _scrollTop = 0;
                _scrollBottom = _rows - 1;
            }
            else
            {
                _cursorRow = 0;
                _cursorColumn = 0;
                _scrollTop = 0;
                _scrollBottom = _rows - 1;
                RestoreCursor();
            }

            _wrapPending = false;
            _dirty = true;
        }

        private void DeviceStatusReport()
        {
            int code = _parameters.Count > 0 && _parameters[0] != 0 ? _parameters[0] : 5;

            if (code == 6)
                ResponseRequested?.Invoke($"\u001b[{_cursorRow + 1};{_cursorColumn + 1}R");
            else if (code == 5)
                ResponseRequested?.Invoke("\u001b[0n");
        }

        private void ResetAll()
        {
            _cursorRow = 0;
            _cursorColumn = 0;
            _savedCursorRow = 0;
            _savedCursorColumn = 0;
            _scrollTop = 0;
            _scrollBottom = _rows - 1;
            _foreground = AnsiColor.Default;
            _background = AnsiColor.Default;
            _attributes = AnsiAttributes.None;
            _cursorVisible = true;
            _wrapEnabled = true;
            _wrapPending = false;

            _history.Clear();
            _scrollOffset = 0;

            Clear(Active);
            _dirty = true;
        }

        private void ResetAttributes()
        {
            _foreground = AnsiColor.Default;
            _background = AnsiColor.Default;
            _attributes = AnsiAttributes.None;
        }

        // ---- grid helpers ---------------------------------------------------------

        private void SetCell(int row, int column, ScreenCell cell)
        {
            Active[row * _columns + column] = cell;
            _dirty = true;
        }

        private ScreenCell MakeCell(Rune rune) => new(rune, _foreground, _background, _attributes, false);

        private static ScreenCell ContinuationCell() =>
            new(new Rune(' '), AnsiColor.Default, AnsiColor.Default, AnsiAttributes.None, true);

        private static ScreenCell BlankCell(AnsiColor background) =>
            new(new Rune(' '), AnsiColor.Default, background, AnsiAttributes.None, false);

        private void FillRow(ScreenCell[] grid, int row)
        {
            ScreenCell blank = BlankCell(_background);
            int offset = row * _columns;

            for (int column = 0; column < _columns; column++)
                grid[offset + column] = blank;
        }

        private void FillRowRange(ScreenCell[] grid, int row, int from, int to)
        {
            ScreenCell blank = BlankCell(_background);
            int offset = row * _columns;
            int stop = Math.Min(to, _columns);

            for (int column = from; column < stop; column++)
                grid[offset + column] = blank;
        }

        private void CopyRow(ScreenCell[] grid, int sourceRow, int destinationRow)
        {
            Array.Copy(grid, sourceRow * _columns, grid, destinationRow * _columns, _columns);
        }

        private void Clear(ScreenCell[] grid)
        {
            ScreenCell blank = BlankCell(_background);
            for (int index = 0; index < grid.Length; index++)
                grid[index] = blank;
        }

        private static ScreenCell[] NewGrid(int columns, int rows)
        {
            var grid = new ScreenCell[columns * rows];
            ScreenCell blank = BlankCell(AnsiColor.Default);
            Array.Fill(grid, blank);
            return grid;
        }

        private static void CopyGrid(
            ScreenCell[] source,
            int sourceColumns,
            int sourceRows,
            ScreenCell[] destination,
            int destinationColumns,
            int destinationRows)
        {
            int rows = Math.Min(sourceRows, destinationRows);
            int columns = Math.Min(sourceColumns, destinationColumns);

            for (int row = 0; row < rows; row++)
                Array.Copy(source, row * sourceColumns, destination, row * destinationColumns, columns);
        }

        private static int RuneWidth(Rune rune)
        {
            int value = rune.Value;

            if (value == 0 || value < 0x20 || (value >= 0x7F && value < 0xA0))
                return 0;

            if (IsCombining(value))
                return 0;

            return IsWide(value) ? 2 : 1;
        }

        private static bool IsCombining(int value) =>
            (value >= 0x0300 && value <= 0x036F) ||
            (value >= 0x1AB0 && value <= 0x1AFF) ||
            (value >= 0x1DC0 && value <= 0x1DFF) ||
            (value >= 0x20D0 && value <= 0x20FF) ||
            (value >= 0xFE20 && value <= 0xFE2F);

        private static bool IsWide(int value) =>
            (value >= 0x1100 && value <= 0x115F) ||
            (value >= 0x2329 && value <= 0x232A) ||
            (value >= 0x2E80 && value <= 0xA4CF) ||
            (value >= 0xAC00 && value <= 0xD7A3) ||
            (value >= 0xF900 && value <= 0xFAFF) ||
            (value >= 0xFE10 && value <= 0xFE19) ||
            (value >= 0xFE30 && value <= 0xFE6F) ||
            (value >= 0xFF00 && value <= 0xFF60) ||
            (value >= 0xFFE0 && value <= 0xFFE6) ||
            (value >= 0x1F300 && value <= 0x1F64F) ||
            (value >= 0x1F900 && value <= 0x1F9FF) ||
            (value >= 0x20000 && value <= 0x3FFFD);
    }
}

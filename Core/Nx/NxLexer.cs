using System;
using System.Globalization;
using System.Text;

namespace nyxWM.Nx
{
    /// <summary>Kind of a lexed token. Keep the enum small: the language is declarative.</summary>
    internal enum NxTokenKind
    {
        EndOfFile,
        Newline,
        Identifier,
        String,
        Number,
        Color,
        Duration,
        Arrow,
        Dollar,
        Colon,
        Comma,
        Dot,
        Equals,
        LParen,
        RParen,
        LBrace,
        RBrace,
        LBracket,
        RBracket
    }

    /// <summary>
    /// One lexed token with its source position, so diagnostics can point at the
    /// exact line and column instead of failing silently.
    /// </summary>
    internal readonly struct NxToken
    {
        internal NxTokenKind Kind { get; }
        internal string Text { get; }
        internal int Line { get; }
        internal int Column { get; }

        internal NxToken(NxTokenKind kind, string text, int line, int column)
        {
            Kind = kind;
            Text = text;
            Line = line;
            Column = column;
        }

        public override string ToString()
        {
            return Kind == NxTokenKind.Newline
                ? $"newline at {Line}:{Column}"
                : $"'{Text}' ({Kind}) at {Line}:{Column}";
        }
    }

    /// <summary>
    /// Turns .nx source text into tokens. The grammar is line oriented for
    /// readability and braces for nesting, so the lexer tracks newlines but the
    /// parser is free to ignore runs of them.
    /// </summary>
    internal sealed class NxLexer
    {
        private readonly string _source;
        private readonly string _fileName;
        private int _index;
        private int _line = 1;
        private int _column = 1;

        internal NxLexer(string source, string fileName)
        {
            _source = source ?? string.Empty;
            _fileName = fileName ?? "<nx>";
        }

        /// <summary>Reads the whole source into a token list. Throws on a bad character.</summary>
        internal NxToken[] Tokenize()
        {
            var tokens = new System.Collections.Generic.List<NxToken>(_source.Length / 4 + 16);

            while (true)
            {
                NxToken token = Next();
                tokens.Add(token);

                if (token.Kind == NxTokenKind.EndOfFile)
                    return tokens.ToArray();
            }
        }

        private NxToken Next()
        {
            SkipSpacesAndComments();

            if (_index >= _source.Length)
                return new NxToken(NxTokenKind.EndOfFile, string.Empty, _line, _column);

            char c = _source[_index];
            int line = _line;
            int column = _column;

            if (c == '\r' || c == '\n')
                return ReadNewline();

            // '#' starts either a colour literal or an element name such as
            // "#play". ReadHash decides by looking at the word that follows.
            if (c == '#')
                return ReadHash(line, column);

            if (char.IsLetter(c) || c == '_' || c == '@')
                return ReadIdentifier();

            if (char.IsDigit(c))
                return ReadNumber();

            // A sign directly before a digit belongs to the number, so "x: -4"
            // needs no special case in the parser.
            if (c == '-' && char.IsDigit(Peek(_index + 1)))
                return ReadSignedNumber(line, column);

            if (c == '"' || c == '\'')
                return ReadString();

            return ReadPunctuation(c, line, column);
        }

        private void SkipSpacesAndComments()
        {
            while (_index < _source.Length)
            {
                char c = _source[_index];

                if (c == ' ' || c == '\t')
                {
                    Advance();
                    continue;
                }

                // Comments run to the end of the line so block comments cannot
                // unbalance the newline-oriented grammar.
                if (c == '/' && _index + 1 < _source.Length && _source[_index + 1] == '/')
                {
                    while (_index < _source.Length && _source[_index] != '\n' && _source[_index] != '\r')
                        Advance();

                    continue;
                }

                return;
            }
        }

        private NxToken ReadNewline()
        {
            int line = _line;
            int column = _column;

            if (_source[_index] == '\r' && _index + 1 < _source.Length && _source[_index + 1] == '\n')
                _index++;

            Advance();
            return new NxToken(NxTokenKind.Newline, "\n", line, column);
        }

        private NxToken ReadIdentifier()
        {
            int line = _line;
            int column = _column;
            int start = _index;

            while (_index < _source.Length)
            {
                char c = _source[_index];

                if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '@' ||
                    c == '#' || c == '.' || c == '$')
                {
                    // A dot only continues a name when it is followed by a name
                    // character: "music.title" stays one token, "1.5" does not
                    // apply because numbers never reach this branch.
                    if (c == '.' && !IsNameChar(Peek(_index + 1)))
                        break;

                    Advance();
                    continue;
                }

                break;
            }

            string text = _source[start.._index];
            return new NxToken(NxTokenKind.Identifier, text, line, column);
        }

        private NxToken ReadNumber()
        {
            int line = _line;
            int column = _column;
            int start = _index;

            while (_index < _source.Length && (char.IsDigit(_source[_index]) || _source[_index] == '.'))
                Advance();

            // A unit suffix such as "ms" keeps the number and its unit together.
            while (_index < _source.Length && char.IsLetter(_source[_index]))
                Advance();

            string text = _source[start.._index];

            return text.EndsWith("ms", StringComparison.OrdinalIgnoreCase)
                ? new NxToken(NxTokenKind.Duration, text, line, column)
                : new NxToken(NxTokenKind.Number, text, line, column);
        }

        private NxToken ReadSignedNumber(int line, int column)
        {
            int start = _index;

            Advance(); // '-'

            while (_index < _source.Length && (char.IsDigit(_source[_index]) || _source[_index] == '.'))
                Advance();

            return new NxToken(NxTokenKind.Number, _source[start.._index], line, column);
        }

        private NxToken ReadString()
        {
            int line = _line;
            int column = _column;
            char quote = _source[_index];

            Advance();
            var builder = new StringBuilder();

            while (_index < _source.Length)
            {
                char c = _source[_index];

                if (c == '\\' && _index + 1 < _source.Length)
                {
                    Advance();
                    char escaped = _source[_index];
                    builder.Append(escaped switch
                    {
                        'n' => '\n',
                        't' => '\t',
                        'r' => '\r',
                        '"' => '"',
                        '\'' => '\'',
                        '\\' => '\\',
                        _ => escaped
                    });
                    Advance();
                    continue;
                }

                if (c == quote)
                {
                    Advance();
                    return new NxToken(NxTokenKind.String, builder.ToString(), line, column);
                }

                if (c == '\n' || c == '\r')
                    throw Error($"Unterminated string starting at {line}:{column}");

                builder.Append(c);
                Advance();
            }

            throw Error($"Unterminated string starting at {line}:{column}");
        }

        private NxToken ReadPunctuation(char c, int line, int column)
        {
            switch (c)
            {
                case '#':
                    return ReadHash(line, column);

                case '-':
                    if (Peek(_index + 1) == '>')
                    {
                        Advance();
                        Advance();
                        return new NxToken(NxTokenKind.Arrow, "->", line, column);
                    }

                    return ReadOperator(c, line, column);

                case '+':
                case '*':
                case '/':
                case '%':
                case '?':
                    return ReadOperator(c, line, column);

                case '<':
                case '>':
                case '!':
                case '&':
                case '|':
                    return ReadPairOrSingle(c, line, column);

                case '$':
                    Advance();
                    return new NxToken(NxTokenKind.Dollar, "$", line, column);

                case ':':
                    Advance();
                    return new NxToken(NxTokenKind.Colon, ":", line, column);

                case ',':
                    Advance();
                    return new NxToken(NxTokenKind.Comma, ",", line, column);

                case '.':
                    Advance();
                    return new NxToken(NxTokenKind.Dot, ".", line, column);

                case '=':
                    if (Peek(_index + 1) == '=')
                        return ReadPairOrSingle(c, line, column);

                    Advance();
                    return new NxToken(NxTokenKind.Equals, "=", line, column);

                case '(':
                    Advance();
                    return new NxToken(NxTokenKind.LParen, "(", line, column);

                case ')':
                    Advance();
                    return new NxToken(NxTokenKind.RParen, ")", line, column);

                case '{':
                    Advance();
                    return new NxToken(NxTokenKind.LBrace, "{", line, column);

                case '}':
                    Advance();
                    return new NxToken(NxTokenKind.RBrace, "}", line, column);

                case '[':
                    Advance();
                    return new NxToken(NxTokenKind.LBracket, "[", line, column);

                case ']':
                    Advance();
                    return new NxToken(NxTokenKind.RBracket, "]", line, column);
            }

            throw Error($"Unexpected character '{c}' at {line}:{column}");
        }

        /// <summary>
        /// A single operator character. Operators are Identifier tokens so the
        /// expression reader can reassemble their source text verbatim.
        /// </summary>
        private NxToken ReadOperator(char c, int line, int column)
        {
            Advance();
            return new NxToken(NxTokenKind.Identifier, c.ToString(), line, column);
        }

        /// <summary>Reads "&amp;&amp;", "||", "&lt;=", "&gt;=", "!=" and "==" as one token.</summary>
        private NxToken ReadPairOrSingle(char c, int line, int column)
        {
            char next = Peek(_index + 1);
            bool paired = (c == '&' && next == '&') ||
                          (c == '|' && next == '|') ||
                          ((c == '<' || c == '>' || c == '!' || c == '=') && next == '=');

            if (!paired)
                return ReadOperator(c, line, column);

            Advance();
            Advance();
            return new NxToken(NxTokenKind.Identifier, $"{c}{next}", line, column);
        }

        /// <summary>
        /// Reads a '#' word. A hex run of exactly 3, 6 or 8 digits is a colour;
        /// anything else is an element name such as "#play".
        /// </summary>
        private NxToken ReadHash(int line, int column)
        {
            int nameStart = _index;
            Advance(); // '#'

            int wordStart = _index;

            while (_index < _source.Length && IsNameChar(_source[_index]))
                Advance();

            string word = _source[wordStart.._index];

            if (word.Length == 0)
                throw Error($"'#' at {line}:{column} must be followed by a colour or a name");

            bool allHex = true;

            foreach (char c in word)
            {
                if (!Uri.IsHexDigit(c))
                {
                    allHex = false;
                    break;
                }
            }

            bool isColor = allHex && word.Length is 3 or 6 or 8;

            return new NxToken(
                isColor ? NxTokenKind.Color : NxTokenKind.Identifier,
                _source[nameStart.._index],
                line,
                column);
        }

        private static bool IsNameChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_' || c == '-';
        }

        /// <summary>Character at an absolute source position, or NUL past either end.</summary>
        private char Peek(int position)
        {
            return position >= 0 && position < _source.Length ? _source[position] : '\0';
        }

        private void Advance()
        {
            if (_index >= _source.Length)
                return;

            if (_source[_index] == '\n')
            {
                _line++;
                _column = 1;
            }
            else
            {
                _column++;
            }

            _index++;
        }

        private Exception Error(string message)
        {
            return new InvalidOperationException($"{_fileName}: {message}");
        }
    }

    /// <summary>Parsing helpers shared by the document reader.</summary>
    internal static class NxLexing
    {
        /// <summary>Parses the numeric part of a number or duration token.</summary>
        internal static double ParseNumber(NxToken token)
        {
            string text = token.Text;

            if (text.EndsWith("ms", StringComparison.OrdinalIgnoreCase))
                text = text[..^2];

            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                throw new InvalidOperationException($"'{token.Text}' at {token.Line}:{token.Column} is not a number");

            return value;
        }

        /// <summary>Parses a duration token into milliseconds.</summary>
        internal static double ParseDurationMs(NxToken token)
        {
            string text = token.Text;

            if (text.EndsWith("ms", StringComparison.OrdinalIgnoreCase))
                return ParseNumber(token);

            if (text.EndsWith("s", StringComparison.OrdinalIgnoreCase))
                return ParseNumber(new NxToken(NxTokenKind.Number, text[..^1], token.Line, token.Column)) * 1000.0;

            return ParseNumber(token);
        }
    }
}

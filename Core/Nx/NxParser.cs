using System;
using System.Collections.Generic;
using System.Globalization;

namespace nyxWM.Nx
{
    /// <summary>
    /// Recursive-descent reader for .nx documents.
    ///
    /// Grammar, informally:
    ///
    ///   document   := (token | surface | animation | NEWLINE)*
    ///   token      := IDENT ':' value NEWLINE
    ///   surface    := IDENT ('#' IDENT)? ':' NEWLINE? '{' body '}'
    ///   body       := (property | child | animationRef)*
    ///   property   := IDENT ':' value NEWLINE
    ///   child      := surface
    ///   animation  := 'animation' IDENT ':' NEWLINE? '{' keyframe* '}'
    ///   keyframe   := (NUMBER '%' IDENT)? ':' NEWLINE? '{' property* '}'
    ///
    /// Newlines are significant only as statement terminators; runs of them and
    /// trailing separators are skipped so hand-written files stay readable.
    /// </summary>
    internal sealed class NxParser
    {
        private readonly NxToken[] _tokens;
        private readonly string _fileName;
        private int _position;

        private NxParser(NxToken[] tokens, string fileName)
        {
            _tokens = tokens;
            _fileName = fileName;
        }

        /// <summary>Parses source text into a document. Throws with file, line and column.</summary>
        internal static NxDocument Parse(string source, string fileName)
        {
            NxToken[] tokens = new NxLexer(source, fileName).Tokenize();
            var parser = new NxParser(tokens, fileName);
            return parser.ParseDocument();
        }

        private NxDocument ParseDocument()
        {
            var document = new NxDocument(_fileName);

            SkipSeparators();

            while (Current.Kind != NxTokenKind.EndOfFile)
            {
                if (Current.Kind != NxTokenKind.Identifier)
                    throw Error($"Unexpected {Current}");

                if (string.Equals(Current.Text, "animation", StringComparison.OrdinalIgnoreCase))
                {
                    document.AddAnimation(ParseAnimation());
                    SkipSeparators();
                    continue;
                }

                NodeOrToken result = ParseStatement();

                if (result.Token is { } token)
                {
                    document.TokenTable[token.Name] = token.Value;
                }
                else
                {
                    document.AddRoot(result.Node!);
                }

                SkipSeparators();
            }

            return document;
        }

        /// <summary>
        /// One "name: value" or "name: block" line. A lines whose value is missing
        /// opens a block, written either with braces or by indentation.
        /// </summary>
        private NodeOrToken ParseStatement()
        {
            int line = Current.Line;
            int column = Current.Column;
            string name = Expect(NxTokenKind.Identifier).Text;
            string? suffix = null;

            if (Current.Kind == NxTokenKind.Identifier &&
                (Current.Text.StartsWith('@') || Current.Text.StartsWith('#')))
            {
                suffix = Current.Text;
                Advance();
            }

            Expect(NxTokenKind.Colon);
            SkipInline();

            // A header ends the line, or opens a brace block on the same line. Any
            // other token is the value of a plain property.
            bool opensBlock = Current.Kind is NxTokenKind.Newline or NxTokenKind.LBrace or NxTokenKind.EndOfFile;

            if (opensBlock)
            {
                var node = new NxNode(name, CombineName(name, suffix), _fileName, line);
                ParseElementBody(node, column);
                return new NodeOrToken(node, null);
            }

            if (suffix != null)
                throw Error($"'{name}{suffix}' at {line} declares an element but has no block");

            NxValue value = ParseValue();
            AdvanceStatement();

            return new NodeOrToken(null, new NamedValue(name, value, line));
        }

        /// <summary>
        /// Reads the body of an element. Either a brace block, or the lines indented
        /// deeper than the header. Both forms nest, and both accept properties and
        /// children, which the statement itself tells apart.
        /// </summary>
        private void ParseElementBody(NxNode node, int headerColumn)
        {
            SkipSeparators();

            if (Current.Kind == NxTokenKind.LBrace)
            {
                ParseBraceBody(node);
                SkipSeparators();
                return;
            }

            while (CanContinue(headerColumn))
            {
                AddStatement(node, ParseStatement());
            }
        }

        private void ParseBraceBody(NxNode node)
        {
            Expect(NxTokenKind.LBrace);
            SkipSeparators();

            while (Current.Kind != NxTokenKind.RBrace)
            {
                if (Current.Kind == NxTokenKind.EndOfFile)
                    throw Error($"Unclosed block for '{node.Type}' opened at {node.Line}");

                if (Current.Kind != NxTokenKind.Identifier)
                    throw Error($"Unexpected {Current} inside '{node}'");

                AddStatement(node, ParseStatement());
            }

            Expect(NxTokenKind.RBrace);
        }

        /// <summary>A deeper indented line continues the body; anything else ends it.</summary>
        private bool CanContinue(int headerColumn)
        {
            return Current.Kind switch
            {
                NxTokenKind.EndOfFile => false,
                NxTokenKind.Newline => false,
                NxTokenKind.Comma => false,
                NxTokenKind.RBrace => false,
                _ => Current.Column > headerColumn
            };
        }

        private static void AddStatement(NxNode node, NodeOrToken result)
        {
            if (result.Token is { } token)
                node.AddProperty(new NxProperty(token.Name, token.Value, token.Line));
            else
                node.AddChild(result.Node!);
        }

        private NxAnimation ParseAnimation()
        {
            int column = Current.Column;
            Advance(); // 'animation'
            SkipInline();

            string name = Expect(NxTokenKind.Identifier).Text;
            Expect(NxTokenKind.Colon);
            SkipInline();

            // Options and keyframes are either braced, like an element body, or the
            // deeper indented lines below the header.
            bool braced = Current.Kind == NxTokenKind.LBrace;

            if (braced)
                Advance();

            SkipSeparators();

            double durationMs = 400.0;
            bool loop = false;
            bool alternate = false;
            string easing = "ease-out";
            bool randomOffset = false;
            var frames = new List<NxKeyframe>();
            int index = 0;

            while (true)
            {
                if (Current.Kind == NxTokenKind.EndOfFile)
                {
                    if (braced)
                        throw Error($"Unclosed animation '{name}'");

                    break;
                }

                if (braced)
                {
                    if (Current.Kind == NxTokenKind.RBrace)
                        break;
                }
                else if (!CanContinue(column))
                {
                    break;
                }

                if (Current.Kind == NxTokenKind.Identifier &&
                    IsAnimationOption(Current.Text))
                {
                    string option = Current.Text.ToLowerInvariant();
                    Advance();
                    Expect(NxTokenKind.Colon);
                    SkipInline();

                    switch (option)
                    {
                        case "duration":
                            durationMs = ParseDurationValue();
                            break;
                        case "loop":
                            loop = ParseBooleanValue();
                            break;
                        case "alternate":
                            alternate = ParseBooleanValue();
                            break;
                        case "easing":
                            easing = ParseTextValue();
                            break;
                        case "random":
                            randomOffset = ParseBooleanValue();
                            break;
                    }

                    AdvanceStatement();
                    continue;
                }

                frames.Add(ParseKeyframe(index++, easing));
            }

            if (braced)
            {
                Expect(NxTokenKind.RBrace);
            }
            else if (Current.Kind == NxTokenKind.Newline || Current.Kind == NxTokenKind.Comma)
            {
                SkipSeparators();
            }

            return new NxAnimation(name, durationMs, loop, alternate, easing, frames, randomOffset);
        }

        private NxKeyframe ParseKeyframe(int index, string defaultEasing)
        {
            double offset = index switch
            {
                0 => 0.0,
                _ => 1.0
            };
            string easing = defaultEasing;

            // "40% ease-in {" or "40% {" or "40 ease-in {" or "{".
            if (Current.Kind == NxTokenKind.Number)
            {
                double percent = NxLexing.ParseNumber(Current);
                Advance();
                offset = Math.Clamp(percent / 100.0, 0.0, 1.0);

                if (Current.Kind == NxTokenKind.Identifier && Current.Text == "%")
                    Advance();
            }

            if (Current.Kind == NxTokenKind.Identifier)
            {
                easing = Current.Text;
                Advance();
            }

            SkipInline();

            // The colon after an offset is optional: "40% {" and "40%: {" both read.
            if (Current.Kind == NxTokenKind.Colon)
                Advance();

            SkipSeparators();
            Expect(NxTokenKind.LBrace);
            SkipSeparators();

            var properties = new List<NxProperty>();

            while (Current.Kind != NxTokenKind.RBrace)
            {
                if (Current.Kind == NxTokenKind.EndOfFile)
                    throw Error("Unclosed keyframe");

                int line = Current.Line;
                string propertyName = Expect(NxTokenKind.Identifier).Text;
                Expect(NxTokenKind.Colon);
                SkipInline();
                properties.Add(new NxProperty(propertyName, ParseValue(), line));
                AdvanceStatement();
            }

            Expect(NxTokenKind.RBrace);
            AdvanceStatement();

            return new NxKeyframe(offset, easing, properties);
        }

        private static bool IsAnimationOption(string text)
        {
            return text switch
            {
                "duration" => true,
                "loop" => true,
                "alternate" => true,
                "easing" => true,
                _ => false
            };
        }

        /// <summary>Reads a value: literal, list or a braced binding expression.</summary>
        private NxValue ParseValue()
        {
            NxToken token = Current;

            switch (token.Kind)
            {
                case NxTokenKind.Number:
                    Advance();

                    // "50%" is a percentage of the parent box, kept as text so the
                    // layout pass can tell it apart from an absolute 50 pixels.
                    if (Current.Kind == NxTokenKind.Identifier && Current.Text == "%")
                    {
                        Advance();
                        return NxValue.FromText($"{token.Text}%", token.Line);
                    }

                    return NxValue.FromNumber(NxLexing.ParseNumber(token), token.Line);

                case NxTokenKind.Duration:
                    Advance();
                    return NxValue.FromDuration(NxLexing.ParseDurationMs(token), token.Line);

                case NxTokenKind.Color:
                    Advance();
                    return NxValue.FromColor(NxColor.Parse(token.Text), token.Line);

                case NxTokenKind.String:
                    Advance();
                    return ParseInterpolatedText(token);

                case NxTokenKind.LBrace:
                    Advance();
                    SkipInline();
                    return ParseExpression();

                case NxTokenKind.LBracket:
                    return ParseList();

                case NxTokenKind.Identifier:
                    Advance();
                    return ParseIdentifierValue(token);

                case NxTokenKind.Dollar:
                    Advance();
                    return ParseTokenReference();
            }

            throw Error($"Unexpected {token} where a value was expected");
        }

        private NxValue ParseIdentifierValue(NxToken token)
        {
            string text = token.Text;

            return text.ToLowerInvariant() switch
            {
                "true" => NxValue.FromBoolean(true, token.Line),
                "false" => NxValue.FromBoolean(false, token.Line),
                _ => ParseInterpolatedText(
                    new NxToken(NxTokenKind.String, text, token.Line, token.Column))
            };
        }

        /// <summary>A '$name' value copies a previously declared token.</summary>
        private NxValue ParseTokenReference()
        {
            NxToken reference = Expect(NxTokenKind.Identifier);
            return NxValue.FromText("$" + reference.Text, reference.Line);
        }

        /// <summary>
        /// Text values may interpolate bindings, so "12:{time.ss}" becomes an
        /// expression while plain text stays a string literal.
        /// </summary>
        private NxValue ParseInterpolatedText(NxToken token)
        {
            string text = token.Text;

            if (!NxInterpolation.ContainsBinding(text))
                return NxValue.FromText(text, token.Line);

            var segments = new List<string>(NxInterpolation.Split(text));
            return NxValue.FromExpression(
                new NxExpression(text, segments, token.Line),
                token.Line);
        }

        private NxValue ParseExpression()
        {
            // Everything up to the closing brace belongs to the binding, including
            // nested braces of string literals, which this simple reader refuses
            // loudly instead of guessing.
            var builder = new System.Text.StringBuilder();
            int depth = 1;
            NxToken start = Current;

            while (Current.Kind != NxTokenKind.EndOfFile)
            {
                if (Current.Kind == NxTokenKind.RBrace)
                {
                    depth--;

                    if (depth == 0)
                    {
                        Advance();
                        break;
                    }
                }
                else if (Current.Kind == NxTokenKind.LBrace)
                {
                    depth++;
                }

                if (builder.Length > 0 && Current.Kind == NxTokenKind.Newline)
                    builder.Append(' ');

                if (Current.Kind != NxTokenKind.Newline && Current.Kind != NxTokenKind.RBrace)
                    builder.Append(Current.Text);

                Advance();
            }

            string expression = builder.ToString().Trim();

            if (expression.Length == 0)
                throw Error($"Empty expression at {start.Line}:{start.Column}");

            return NxValue.FromExpression(
                new NxExpression(expression, new[] { expression }, start.Line),
                start.Line);
        }

        private NxValue ParseList()
        {
            int line = Current.Line;
            Advance();
            SkipInline();

            var items = new List<NxValue>();

            while (Current.Kind != NxTokenKind.RBracket)
            {
                if (Current.Kind == NxTokenKind.EndOfFile)
                    throw Error($"Unclosed list starting at {line}");

                items.Add(ParseValue());

                if (Current.Kind == NxTokenKind.Comma)
                {
                    Advance();
                    SkipInline();
                }
                else if (Current.Kind != NxTokenKind.RBracket)
                {
                    throw Error($"Expected ',' or ']' in list at {Current.Line}:{Current.Column}");
                }
            }

            Advance();
            return NxValue.FromList(items, line);
        }

        private double ParseDurationValue()
        {
            NxToken token = Current;

            return token.Kind switch
            {
                NxTokenKind.Duration => AdvanceAndReturn(() => NxLexing.ParseDurationMs(token)),
                NxTokenKind.Number => AdvanceAndReturn(() => NxLexing.ParseNumber(token)),
                _ => throw Error($"Expected a duration at {token.Line}:{token.Column}")
            };
        }

        private bool ParseBooleanValue()
        {
            NxToken token = Expect(NxTokenKind.Identifier);
            return token.Text.ToLowerInvariant() switch
            {
                "true" or "on" or "yes" => true,
                "false" or "off" or "no" => false,
                _ => throw Error($"Expected true or false at {token.Line}:{token.Column}")
            };
        }

        private string ParseTextValue()
        {
            if (Current.Kind == NxTokenKind.String || Current.Kind == NxTokenKind.Identifier)
            {
                string text = Current.Text;
                Advance();
                return text;
            }

            throw Error($"Expected text at {Current.Line}:{Current.Column}");
        }

        private T AdvanceAndReturn<T>(Func<T> read)
        {
            T value = read();
            Advance();
            return value;
        }

        private void AdvanceStatement()
        {
            SkipInline();

            if (Current.Kind == NxTokenKind.Newline)
                Advance();

            SkipSeparators();
        }

        private void SkipInline()
        {
            while (Current.Kind == NxTokenKind.Comma)
                Advance();
        }

        private void SkipSeparators()
        {
            while (Current.Kind == NxTokenKind.Newline || Current.Kind == NxTokenKind.Comma)
                Advance();
        }

        private NxToken Current => _tokens[_position];

        private NxToken Advance()
        {
            NxToken token = _tokens[_position];

            if (_position < _tokens.Length - 1)
                _position++;

            return token;
        }

        private NxToken Expect(NxTokenKind kind)
        {
            if (Current.Kind != kind)
                throw Error($"Expected {kind} but found {Current}");

            return Advance();
        }

        private static string CombineName(string type, string? suffix)
        {
            return string.IsNullOrEmpty(suffix) ? type : suffix;
        }

        private Exception Error(string message)
        {
            return new InvalidOperationException($"{_fileName}: {message}");
        }

        private readonly struct NodeOrToken
        {
            internal NxNode? Node { get; }
            internal NamedValue? Token { get; }

            internal NodeOrToken(NxNode? node, NamedValue? token)
            {
                Node = node;
                Token = token;
            }
        }

        private readonly struct NamedValue
        {
            internal string Name { get; }
            internal NxValue Value { get; }
            internal int Line { get; }

            internal NamedValue(string name, NxValue value, int line)
            {
                Name = name;
                Value = value;
                Line = line;
            }
        }
    }

    /// <summary>Helpers for "{a}.{b}" style text interpolation.</summary>
    internal static class NxInterpolation
    {
        internal static bool ContainsBinding(string text)
        {
            int start = text.IndexOf('{');

            return start >= 0 &&
                   text.IndexOf('}', start + 1) > start + 1;
        }

        /// <summary>
        /// Splits text into literal runs and binding runs. A run is returned with
        /// its braces intact so the evaluator can tell them apart.
        /// </summary>
        internal static IEnumerable<string> Split(string text)
        {
            int index = 0;

            while (index < text.Length)
            {
                int open = text.IndexOf('{', index);

                if (open < 0)
                {
                    yield return text[index..];
                    yield break;
                }

                int close = text.IndexOf('}', open + 1);

                if (close < 0)
                {
                    yield return text[index..];
                    yield break;
                }

                if (open > index)
                    yield return text[index..open];

                yield return text[open..(close + 1)];
                index = close + 1;
            }
        }

        internal static string FormatNumber(double value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}

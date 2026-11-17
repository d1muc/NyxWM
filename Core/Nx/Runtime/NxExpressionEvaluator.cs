using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace nyxWM.Nx
{
    /// <summary>
    /// Evaluates the bindings a document declares: plain values, "{time.hh}"
    /// templates, and computed expressions such as "{sys.cpu * 2}" or
    /// "{media.playing ? media.title : 'paused'}".
    ///
    /// Every expression is parsed once and cached, because a surface re-evaluates
    /// its bindings on every frame and re-parsing text at 165 Hz would dwarf the
    /// cost of painting.
    /// </summary>
    internal sealed class NxExpressionEvaluator
    {
        private readonly NxDataRegistry _registry;
        private readonly IReadOnlyDictionary<string, NxValue> _tokens;
        private readonly ConcurrentDictionary<string, Node?> _cache =
            new(StringComparer.Ordinal);

        internal NxExpressionEvaluator(
            NxDataRegistry registry,
            IReadOnlyDictionary<string, NxValue>? tokens = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _tokens = tokens ?? new Dictionary<string, NxValue>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Resolves a declared value: literals pass through, bindings are computed.</summary>
        internal NxValue Evaluate(NxValue value)
        {
            switch (value.Kind)
            {
                case NxValueKind.Text:
                    // "$accent" copies a document token; a plain string is itself.
                    if (value.Text.Length > 1 && value.Text[0] == '$')
                        return LookupToken(value.Text[1..]) ?? value;

                    return value;

                case NxValueKind.Expression:
                    return EvaluateExpression(value.Expression!);

                default:
                    return value;
            }
        }

        /// <summary>Evaluates a binding into its display string.</summary>
        internal string EvaluateText(NxValue? value, string fallback = "")
        {
            if (value == null)
                return fallback;

            NxValue resolved = Evaluate(value);
            string text = ToText(resolved);

            return string.IsNullOrEmpty(text) ? fallback : text;
        }

        /// <summary>Evaluates a binding into a number, using the fallback when it is not numeric.</summary>
        internal double EvaluateNumber(NxValue? value, double fallback)
        {
            if (value == null)
                return fallback;

            NxValue resolved = Evaluate(value);

            if (resolved.Kind == NxValueKind.Text)
            {
                // "50%" and "12px" carry a unit; only the leading number matters.
                string trimmed = resolved.Text.Trim().TrimEnd('%', 'x', 'p');

                return double.TryParse(
                    trimmed,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double parsed)
                    ? parsed
                    : fallback;
            }

            return resolved.Kind switch
            {
                NxValueKind.Expression => fallback,
                _ => resolved.AsNumber
            };
        }

        /// <summary>Evaluates a binding into a colour, keeping the fallback for anything else.</summary>
        internal NxColor EvaluateColor(NxValue? value, NxColor fallback)
        {
            if (value == null)
                return fallback;

            NxValue resolved = Evaluate(value);

            switch (resolved.Kind)
            {
                case NxValueKind.Color:
                    return resolved.Color;
                case NxValueKind.Text:
                    try
                    {
                        return resolved.Text.StartsWith('#')
                            ? NxColor.Parse(resolved.Text)
                            : fallback;
                    }
                    catch (FormatException)
                    {
                        return fallback;
                    }
                default:
                    return fallback;
            }
        }

        /// <summary>Evaluates a binding into a flag. Missing values are false.</summary>
        internal bool EvaluateFlag(NxValue? value, bool fallback = false)
        {
            if (value == null)
                return fallback;

            NxValue resolved = Evaluate(value);

            switch (resolved.Kind)
            {
                case NxValueKind.Boolean:
                    return resolved.Boolean;
                case NxValueKind.Number:
                case NxValueKind.Duration:
                    return resolved.Number != 0;
                case NxValueKind.Text:
                    return resolved.Text.ToLowerInvariant() switch
                    {
                        "true" or "on" or "yes" or "1" => true,
                        "false" or "off" or "no" or "0" or "" => false,
                        _ => fallback
                    };
                default:
                    return fallback;
            }
        }

        /// <summary>Evaluates a whole value into its string form, used for text and tooltips.</summary>
        internal string ToText(NxValue value)
        {
            return value.Kind switch
            {
                NxValueKind.Number => FormatNumber(value.Number),
                NxValueKind.Duration => FormatNumber(value.Number) + "ms",
                NxValueKind.Text => value.Text,
                NxValueKind.Color => value.Color.ToHex(),
                NxValueKind.Boolean => value.Boolean ? "true" : "false",
                NxValueKind.Expression => EvaluateText(value),
                NxValueKind.List => FormatList(value),
                _ => string.Empty
            };
        }

        private string FormatList(NxValue value)
        {
            var builder = new StringBuilder();
            int written = 0;

            foreach (NxValue item in value.List)
            {
                if (written++ > 0)
                    builder.Append(", ");

                builder.Append(ToText(Evaluate(item)));
            }

            return builder.ToString();
        }

        private NxValue EvaluateExpression(NxExpression expression)
        {
            string source = expression.Text;

            // A template mixes literal text with bindings; a bare "{expr}" is a
            // single expression whose source holds no brace at all.
            if (source.IndexOf('{') < 0)
                return EvaluateSource(source);

            var builder = new StringBuilder();

            foreach (string segment in NxInterpolation.Split(source))
            {
                if (segment.Length > 2 && segment[0] == '{' && segment[^1] == '}')
                    builder.Append(ToText(EvaluateSource(segment[1..^1])));
                else
                    builder.Append(segment);
            }

            return NxValue.FromText(builder.ToString(), expression.Line);
        }

        /// <summary>Evaluates expression source without braces, e.g. "time.hh + ':' + time.mm".</summary>
        internal NxValue EvaluateSource(string source)
        {
            Node? node = _cache.GetOrAdd(source, Compile);

            if (node == null)
                return NxValue.FromText(source, 0);

            try
            {
                return node.Eval(this);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"NxExpressionEvaluator: '{source}' failed: {ex.Message}");
                return NxValue.FromText(string.Empty, 0);
            }
        }

        private static string FormatNumber(double value)
        {
            return Math.Abs(value - Math.Round(value)) < 0.0005
                ? Math.Round(value).ToString(CultureInfo.InvariantCulture)
                : value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private Node? LookupTokenNode(string name)
        {
            return _tokens.TryGetValue(name, out NxValue? token) ? new TokenNode(token) : null;
        }

        private NxValue? LookupToken(string name)
        {
            if (!_tokens.TryGetValue(name, out NxValue? token))
                return null;

            return Evaluate(token);
        }

        // ---------------------------------------------------------------- parsing

        private Node? Compile(string source)
        {
            try
            {
                var reader = new Reader(source);
                Node node = reader.ParseExpression();

                if (!reader.AtEnd)
                    return null;

                return node;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"NxExpressionEvaluator: cannot parse '{source}': {ex.Message}");
                return null;
            }
        }

        private sealed class Reader
        {
            private readonly string _source;
            private int _index;

            internal Reader(string source)
            {
                _source = source;
            }

            internal bool AtEnd => _index >= _source.Length;

            internal Node ParseExpression() => ParseTernary();

            private Node ParseTernary()
            {
                Node condition = ParseOr();

                SkipSpace();

                if (Peek() != '?')
                    return condition;

                _index++;
                Node whenTrue = ParseExpression();

                SkipSpace();

                if (Peek() != ':')
                    throw new FormatException("expected ':' in conditional");

                _index++;
                Node whenFalse = ParseExpression();

                return new ConditionalNode(condition, whenTrue, whenFalse);
            }

            private Node ParseOr()
            {
                Node left = ParseAnd();

                while (true)
                {
                    SkipSpace();

                    if (Peek() != '|' || PeekAt(1) != '|')
                        return left;

                    _index += 2;
                    left = new BinaryNode("||", left, ParseAnd());
                }
            }

            private Node ParseAnd()
            {
                Node left = ParseEquality();

                while (true)
                {
                    SkipSpace();

                    if (Peek() != '&' || PeekAt(1) != '&')
                        return left;

                    _index += 2;
                    left = new BinaryNode("&&", left, ParseEquality());
                }
            }

            private Node ParseEquality()
            {
                Node left = ParseRelational();

                while (true)
                {
                    SkipSpace();

                    if (Peek() == '=' && PeekAt(1) == '=')
                    {
                        _index += 2;
                        left = new BinaryNode("==", left, ParseRelational());
                        continue;
                    }

                    if (Peek() == '!' && PeekAt(1) == '=')
                    {
                        _index += 2;
                        left = new BinaryNode("!=", left, ParseRelational());
                        continue;
                    }

                    return left;
                }
            }

            private Node ParseRelational()
            {
                Node left = ParseAdditive();

                while (true)
                {
                    SkipSpace();

                    char c = Peek();
                    bool paired = PeekAt(1) == '=';
                    string? op = c switch
                    {
                        '<' => paired ? "<=" : "<",
                        '>' => paired ? ">=" : ">",
                        _ => null
                    };

                    if (op == null)
                        return left;

                    _index += op.Length;
                    left = new BinaryNode(op, left, ParseAdditive());
                }
            }

            private Node ParseAdditive()
            {
                Node left = ParseMultiplicative();

                while (true)
                {
                    SkipSpace();

                    char c = Peek();

                    if (c != '+' && c != '-')
                        return left;

                    _index++;
                    left = new BinaryNode(c.ToString(), left, ParseMultiplicative());
                }
            }

            private Node ParseMultiplicative()
            {
                Node left = ParseUnary();

                while (true)
                {
                    SkipSpace();

                    char c = Peek();

                    if (c != '*' && c != '/' && c != '%')
                        return left;

                    _index++;
                    left = new BinaryNode(c.ToString(), left, ParseUnary());
                }
            }

            private Node ParseUnary()
            {
                SkipSpace();

                char c = Peek();

                if (c == '-')
                {
                    _index++;
                    return new UnaryNode("-", ParseUnary());
                }

                if (c == '!')
                {
                    _index++;
                    return new UnaryNode("!", ParseUnary());
                }

                return ParsePrimary();
            }

            private Node ParsePrimary()
            {
                SkipSpace();

                char c = Peek();

                if (c == '\0')
                    throw new FormatException("unexpected end of expression");

                if (c == '(')
                {
                    _index++;
                    Node inner = ParseExpression();
                    SkipSpace();

                    if (Peek() != ')')
                        throw new FormatException("expected ')'");

                    _index++;
                    return inner;
                }

                if (c == '"' || c == '\'')
                    return new LiteralNode(NxValue.FromText(ReadString(c), 0));

                if (c == '#')
                    return new LiteralNode(NxValue.FromColor(NxColor.Parse(ReadColor()), 0));

                if (char.IsDigit(c))
                    return new LiteralNode(NxValue.FromNumber(ReadNumber(), 0));

                if (char.IsLetter(c) || c == '_' || c == '@' || c == '$')
                    return ParseNameOrCall();

                throw new FormatException($"unexpected '{c}'");
            }

            private Node ParseNameOrCall()
            {
                string name = ReadName();

                SkipSpace();

                if (Peek() != '(')
                    return new PathNode(name);

                _index++;

                var arguments = new List<Node>();

                SkipSpace();

                if (Peek() != ')')
                {
                    while (true)
                    {
                        arguments.Add(ParseExpression());
                        SkipSpace();

                        if (Peek() == ',')
                        {
                            _index++;
                            continue;
                        }

                        break;
                    }
                }

                SkipSpace();

                if (Peek() != ')')
                    throw new FormatException($"expected ')' closing '{name}('");

                _index++;

                return new CallNode(name, arguments);
            }

            private string ReadName()
            {
                int start = _index;

                while (_index < _source.Length)
                {
                    char c = _source[_index];

                    if (char.IsLetterOrDigit(c) || c == '_' || c == '@' || c == '$')
                    {
                        _index++;
                        continue;
                    }

                    // A dot joins "time.hh" into one path, but only when a name
                    // character follows it.
                    if (c == '.' && _index + 1 < _source.Length &&
                        (char.IsLetter(_source[_index + 1]) || _source[_index + 1] == '_'))
                    {
                        _index++;
                        continue;
                    }

                    break;
                }

                if (_index == start)
                    throw new FormatException("expected an identifier");

                return _source[start.._index];
            }

            private string ReadString(char quote)
            {
                _index++;
                var builder = new StringBuilder();

                while (_index < _source.Length)
                {
                    char c = _source[_index];

                    if (c == '\\' && _index + 1 < _source.Length)
                    {
                        _index++;
                        builder.Append(_source[_index] switch
                        {
                            'n' => '\n',
                            't' => '\t',
                            _ => _source[_index]
                        });
                        _index++;
                        continue;
                    }

                    if (c == quote)
                    {
                        _index++;
                        return builder.ToString();
                    }

                    builder.Append(c);
                    _index++;
                }

                throw new FormatException("unterminated string");
            }

            private string ReadColor()
            {
                int start = _index;
                _index++;

                while (_index < _source.Length && Uri.IsHexDigit(_source[_index]))
                    _index++;

                return _source[start.._index];
            }

            private double ReadNumber()
            {
                int start = _index;

                while (_index < _source.Length &&
                       (char.IsDigit(_source[_index]) || _source[_index] == '.'))
                {
                    _index++;
                }

                string text = _source[start.._index];

                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                    ? value
                    : 0.0;
            }

            private void SkipSpace()
            {
                while (_index < _source.Length && char.IsWhiteSpace(_source[_index]))
                    _index++;
            }

            private char Peek() => _index < _source.Length ? _source[_index] : '\0';

            private char PeekAt(int offset)
            {
                int position = _index + offset;
                return position < _source.Length ? _source[position] : '\0';
            }
        }

        // -------------------------------------------------------------- node tree

        private abstract class Node
        {
            internal abstract NxValue Eval(NxExpressionEvaluator host);
        }

        private sealed class LiteralNode : Node
        {
            private readonly NxValue _value;

            internal LiteralNode(NxValue value)
            {
                _value = value;
            }

            internal override NxValue Eval(NxExpressionEvaluator host) => _value;
        }

        private sealed class TokenNode : Node
        {
            private readonly NxValue _value;

            internal TokenNode(NxValue value)
            {
                _value = value;
            }

            internal override NxValue Eval(NxExpressionEvaluator host) => host.Evaluate(_value);
        }

        private sealed class PathNode : Node
        {
            private readonly string _path;

            internal PathNode(string path)
            {
                _path = path;
            }

            internal override NxValue Eval(NxExpressionEvaluator host)
            {
                if (_path.Length > 1 && _path[0] == '$')
                    return host.LookupToken(_path[1..]) ?? NxValue.FromText(string.Empty, 0);

                return host._registry.Resolve(_path) ?? NxValue.FromText(string.Empty, 0);
            }
        }

        private sealed class UnaryNode : Node
        {
            private readonly string _operator;
            private readonly Node _operand;

            internal UnaryNode(string @operator, Node operand)
            {
                _operator = @operator;
                _operand = operand;
            }

            internal override NxValue Eval(NxExpressionEvaluator host)
            {
                NxValue value = _operand.Eval(host);

                return _operator switch
                {
                    "-" => NxValue.FromNumber(-value.AsNumber, 0),
                    "!" => NxValue.FromBoolean(!host.EvaluateFlagValue(value), 0),
                    _ => value
                };
            }
        }

        private sealed class ConditionalNode : Node
        {
            private readonly Node _condition;
            private readonly Node _whenTrue;
            private readonly Node _whenFalse;

            internal ConditionalNode(Node condition, Node whenTrue, Node whenFalse)
            {
                _condition = condition;
                _whenTrue = whenTrue;
                _whenFalse = whenFalse;
            }

            internal override NxValue Eval(NxExpressionEvaluator host)
            {
                NxValue condition = _condition.Eval(host);

                return host.EvaluateFlagValue(condition)
                    ? _whenTrue.Eval(host)
                    : _whenFalse.Eval(host);
            }
        }

        private sealed class BinaryNode : Node
        {
            private readonly string _operator;
            private readonly Node _left;
            private readonly Node _right;

            internal BinaryNode(string @operator, Node left, Node right)
            {
                _operator = @operator;
                _left = left;
                _right = right;
            }

            internal override NxValue Eval(NxExpressionEvaluator host)
            {
                // Short-circuit so "{media.hasSession && media.title}" is safe.
                if (_operator == "&&")
                    return NxValue.FromBoolean(
                        host.EvaluateFlagValue(_left.Eval(host)) &&
                        host.EvaluateFlagValue(_right.Eval(host)), 0);

                if (_operator == "||")
                    return NxValue.FromBoolean(
                        host.EvaluateFlagValue(_left.Eval(host)) ||
                        host.EvaluateFlagValue(_right.Eval(host)), 0);

                NxValue left = _left.Eval(host);
                NxValue right = _right.Eval(host);

                switch (_operator)
                {
                    case "+":
                        // Text on either side makes this concatenation, which is how
                        // "{time.hh}:{time.mm}" style text is assembled by hand.
                        if (left.Kind == NxValueKind.Text || right.Kind == NxValueKind.Text)
                            return NxValue.FromText(host.ToText(left) + host.ToText(right), 0);

                        return NxValue.FromNumber(left.AsNumber + right.AsNumber, 0);

                    case "-":
                        return NxValue.FromNumber(left.AsNumber - right.AsNumber, 0);

                    case "*":
                        return NxValue.FromNumber(left.AsNumber * right.AsNumber, 0);

                    case "/":
                        return NxValue.FromNumber(
                            Math.Abs(right.AsNumber) < double.Epsilon ? 0.0 : left.AsNumber / right.AsNumber,
                            0);

                    case "%":
                        return NxValue.FromNumber(
                            Math.Abs(right.AsNumber) < double.Epsilon ? 0.0 : left.AsNumber % right.AsNumber,
                            0);

                    case "==":
                        return NxValue.FromBoolean(Compare(host, left, right) == 0, 0);

                    case "!=":
                        return NxValue.FromBoolean(Compare(host, left, right) != 0, 0);

                    case "<":
                        return NxValue.FromBoolean(Compare(host, left, right) < 0, 0);

                    case "<=":
                        return NxValue.FromBoolean(Compare(host, left, right) <= 0, 0);

                    case ">":
                        return NxValue.FromBoolean(Compare(host, left, right) > 0, 0);

                    case ">=":
                        return NxValue.FromBoolean(Compare(host, left, right) >= 0, 0);

                    default:
                        return NxValue.FromText(string.Empty, 0);
                }
            }

            private static int Compare(NxExpressionEvaluator host, NxValue left, NxValue right)
            {
                bool numeric = left.Kind is NxValueKind.Number or NxValueKind.Duration or NxValueKind.Boolean &&
                               right.Kind is NxValueKind.Number or NxValueKind.Duration or NxValueKind.Boolean;

                return numeric
                    ? left.AsNumber.CompareTo(right.AsNumber)
                    : string.CompareOrdinal(host.ToText(left), host.ToText(right));
            }
        }

        private sealed class CallNode : Node
        {
            private readonly string _name;
            private readonly List<Node> _arguments;

            internal CallNode(string name, List<Node> arguments)
            {
                _name = name;
                _arguments = arguments;
            }

            internal override NxValue Eval(NxExpressionEvaluator host)
            {
                var values = new NxValue[_arguments.Count];

                for (int i = 0; i < _arguments.Count; i++)
                    values[i] = _arguments[i].Eval(host);

                return NxFunctions.Invoke(_name, values, host);
            }
        }

        internal bool EvaluateFlagValue(NxValue value)
        {
            return value.Kind switch
            {
                NxValueKind.Boolean => value.Boolean,
                NxValueKind.Number or NxValueKind.Duration => value.Number != 0,
                NxValueKind.Text => value.Text.Length > 0 &&
                                    !string.Equals(value.Text, "false", StringComparison.OrdinalIgnoreCase) &&
                                    !string.Equals(value.Text, "0", StringComparison.Ordinal),
                _ => false
            };
        }
    }

    /// <summary>The functions a .nx expression can call, e.g. "round(sys.cpu)".</summary>
    internal static class NxFunctions
    {
        internal static NxValue Invoke(string name, NxValue[] arguments, NxExpressionEvaluator host)
        {
            switch (name.ToLowerInvariant())
            {
                case "upper":
                    return Text(Text0(host, arguments).ToUpperInvariant());
                case "lower":
                    return Text(Text0(host, arguments).ToLowerInvariant());
                case "trim":
                    return Text(Text0(host, arguments).Trim());
                case "len":
                    return Number(Text0(host, arguments).Length);
                case "number":
                    return Number(NumberAt(host, arguments, 0, 0));
                case "round":
                    return Number(Math.Round(
                        NumberAt(host, arguments, 0, 0),
                        arguments.Length > 1 ? (int)NumberAt(host, arguments, 1, 0) : 0));
                case "floor":
                    return Number(Math.Floor(NumberAt(host, arguments, 0, 0)));
                case "ceil":
                    return Number(Math.Ceiling(NumberAt(host, arguments, 0, 0)));
                case "abs":
                    return Number(Math.Abs(NumberAt(host, arguments, 0, 0)));
                case "min":
                    return Number(Math.Min(NumberAt(host, arguments, 0, 0), NumberAt(host, arguments, 1, 0)));
                case "max":
                    return Number(Math.Max(NumberAt(host, arguments, 0, 0), NumberAt(host, arguments, 1, 0)));
                case "clamp":
                    return Number(Math.Clamp(
                        NumberAt(host, arguments, 0, 0),
                        NumberAt(host, arguments, 1, 0),
                        NumberAt(host, arguments, 2, 1)));
                case "pad":
                    return Text(Pad(
                        NumberAt(host, arguments, 0, 0),
                        arguments.Length > 1 ? (int)NumberAt(host, arguments, 1, 2) : 2));
                case "concat":
                    return Text(Concat(host, arguments));
                case "truncate":
                    return Text(Truncate(
                        Text0(host, arguments),
                        arguments.Length > 1 ? (int)NumberAt(host, arguments, 1, 8) : 8));
                case "percent":
                    return Text($"{NumberAt(host, arguments, 0, 0):0}%");
                case "clock":
                    return Text(Clock(NumberAt(host, arguments, 0, 0)));
                case "if":
                    return host.EvaluateFlagValue(arguments.Length > 0 ? arguments[0] : Empty)
                        ? (arguments.Length > 1 ? arguments[1] : Empty)
                        : (arguments.Length > 2 ? arguments[2] : Text(string.Empty));
                case "default":
                    return Text0(host, arguments).Length > 0
                        ? arguments[0]
                        : (arguments.Length > 1 ? arguments[1] : Text(string.Empty));
                default:
                    return Text(string.Empty);
            }
        }

        private static readonly NxValue Empty = NxValue.FromText(string.Empty, 0);

        private static NxValue Text(string value) => NxValue.FromText(value, 0);

        private static NxValue Number(double value) => NxValue.FromNumber(value, 0);

        private static string Text0(NxExpressionEvaluator host, NxValue[] arguments)
        {
            return arguments.Length > 0 ? host.ToText(arguments[0]) : string.Empty;
        }

        private static double NumberAt(NxExpressionEvaluator host, NxValue[] arguments, int index, double fallback)
        {
            if (index >= arguments.Length)
                return fallback;

            NxValue value = arguments[index];

            return value.Kind switch
            {
                NxValueKind.Number or NxValueKind.Duration or NxValueKind.Boolean => value.AsNumber,
                NxValueKind.Text => double.TryParse(
                    value.Text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double parsed)
                    ? parsed
                    : fallback,
                _ => fallback
            };
        }

        private static string Concat(NxExpressionEvaluator host, NxValue[] arguments)
        {
            var builder = new StringBuilder();

            foreach (NxValue argument in arguments)
                builder.Append(host.ToText(argument));

            return builder.ToString();
        }

        private static string Pad(double value, int width)
        {
            string text = ((long)value).ToString(CultureInfo.InvariantCulture);
            return text.PadLeft(Math.Max(0, width), '0');
        }

        private static string Truncate(string text, int length)
        {
            if (length <= 0 || text.Length <= length)
                return text;

            return text[..Math.Max(0, length - 1)] + "…";
        }

        private static string Clock(double seconds)
        {
            if (seconds < 0)
                seconds = 0;

            var span = TimeSpan.FromSeconds(seconds);
            return $"{(int)span.TotalMinutes}:{span.Seconds:00}";
        }
    }
}

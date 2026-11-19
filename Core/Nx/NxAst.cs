using System;
using System.Collections.Generic;

namespace nyxWM.Nx
{
    /// <summary>A colour value in the .nx document.</summary>
    internal readonly struct NxColor
    {
        internal byte A { get; }
        internal byte R { get; }
        internal byte G { get; }
        internal byte B { get; }

        private NxColor(byte a, byte r, byte g, byte b)
        {
            A = a;
            R = r;
            G = g;
            B = b;
        }

        /// <summary>Builds a colour from its parts, for computed and animated values.</summary>
        internal static NxColor FromBytes(byte a, byte r, byte g, byte b) => new(a, r, g, b);

        internal static readonly NxColor Transparent = new(0, 0, 0, 0);

        internal static readonly NxColor White = new(255, 255, 255, 255);

        /// <summary>Parses #rgb, #rrggbb or #aarrggbb.</summary>
        internal static NxColor Parse(string text)
        {
            if (string.IsNullOrEmpty(text) || text[0] != '#')
                throw new FormatException($"'{text}' is not a colour");

            string hex = text[1..];

            static byte Pair(string value)
            {
                return byte.Parse(value, System.Globalization.NumberStyles.HexNumber);
            }

            return hex.Length switch
            {
                3 => new NxColor(
                    255,
                    (byte)(Pair(hex[0].ToString()) * 17),
                    (byte)(Pair(hex[1].ToString()) * 17),
                    (byte)(Pair(hex[2].ToString()) * 17)),
                6 => new NxColor(255, Pair(hex[..2]), Pair(hex[2..4]), Pair(hex[4..6])),
                8 => new NxColor(Pair(hex[..2]), Pair(hex[2..4]), Pair(hex[4..6]), Pair(hex[6..8])),
                _ => throw new FormatException($"'{text}' is not a colour")
            };
        }

        /// <summary>Base name used when a keyframe animates this colour.</summary>
        internal string ToHex()
        {
            return A == 255
                ? $"#{R:x2}{G:x2}{B:x2}"
                : $"#{A:x2}{R:x2}{G:x2}{B:x2}";
        }

        public override bool Equals(object? obj) => obj is NxColor other && Equals(other);

        internal bool Equals(NxColor other)
        {
            return A == other.A && R == other.R && G == other.G && B == other.B;
        }

        public override int GetHashCode() => HashCode.Combine(A, R, G, B);
    }

    /// <summary>What kind of value a .nx property holds.</summary>
    internal enum NxValueKind
    {
        Number,
        Duration,
        Text,
        Color,
        Boolean,
        List,
        Expression
    }

    /// <summary>
    /// One value: a literal, a list of literals or a binding expression such as
    /// "{time.hh}:{time.mm}". Immutable by construction.
    /// </summary>
    internal sealed class NxValue
    {
        internal NxValueKind Kind { get; }
        internal double Number { get; }
        internal string Text { get; }
        internal NxColor Color { get; }
        internal IReadOnlyList<NxValue> List { get; }
        internal NxExpression? Expression { get; }
        internal int Line { get; }

        private NxValue(NxValueKind kind, double number, string text, NxColor color, IReadOnlyList<NxValue> list, NxExpression? expression, int line)
        {
            Kind = kind;
            Number = number;
            Text = text;
            Color = color;
            List = list;
            Expression = expression;
            Line = line;
        }

        internal static NxValue FromNumber(double value, int line) =>
            new(NxValueKind.Number, value, string.Empty, default, Array.Empty<NxValue>(), null, line);

        internal static NxValue FromDuration(double value, int line) =>
            new(NxValueKind.Duration, value, string.Empty, default, Array.Empty<NxValue>(), null, line);

        internal static NxValue FromText(string value, int line) =>
            new(NxValueKind.Text, 0, value ?? string.Empty, default, Array.Empty<NxValue>(), null, line);

        internal static NxValue FromColor(NxColor value, int line) =>
            new(NxValueKind.Color, 0, string.Empty, value, Array.Empty<NxValue>(), null, line);

        internal static NxValue FromBoolean(bool value, int line) =>
            new(NxValueKind.Boolean, value ? 1 : 0, string.Empty, default, Array.Empty<NxValue>(), null, line);

        internal static NxValue FromExpression(NxExpression value, int line) =>
            new(NxValueKind.Expression, 0, string.Empty, default, Array.Empty<NxValue>(), value, line);

        internal static NxValue FromList(IReadOnlyList<NxValue> value, int line) =>
            new(NxValueKind.List, 0, string.Empty, default, value, null, line);

        internal bool Boolean => Number != 0;

        internal double AsNumber => Kind switch
        {
            NxValueKind.Number or NxValueKind.Duration or NxValueKind.Boolean => Number,
            NxValueKind.Text => double.TryParse(Text, out double parsed) ? parsed : 0,
            _ => 0
        };

        internal string Describe()
        {
            return Kind switch
            {
                NxValueKind.Number => Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
                NxValueKind.Duration => $"{Number.ToString(System.Globalization.CultureInfo.InvariantCulture)}ms",
                NxValueKind.Text => $"\"{Text}\"",
                NxValueKind.Color => Color.ToHex(),
                NxValueKind.Boolean => Boolean ? "true" : "false",
                NxValueKind.List => $"[{string.Join(", ", DescribeItems())}]",
                NxValueKind.Expression => $"{{{Expression!.Text}}}",
                _ => "?"
            };
        }

        private IEnumerable<string> DescribeItems()
        {
            foreach (NxValue item in List)
                yield return item.Describe();
        }
    }

    /// <summary>
    /// A binding such as "{music.title}" or a computed expression such as
    /// "{cpu.percent * 2}". The runtime evaluates it each frame.
    /// </summary>
    internal sealed class NxExpression
    {
        /// <summary>Source without the surrounding braces.</summary>
        internal string Text { get; }

        /// <summary>Offsets in <see cref="Text"/> where each interpolation starts.</summary>
        internal IReadOnlyList<string> Segments { get; }

        internal int Line { get; }

        internal NxExpression(string text, IReadOnlyList<string> segments, int line)
        {
            Text = text ?? string.Empty;
            Segments = segments ?? Array.Empty<string>();
            Line = line;
        }
    }

    /// <summary>One "property: value" entry inside a block.</summary>
    internal sealed class NxProperty
    {
        internal string Name { get; }
        internal NxValue Value { get; }
        internal int Line { get; }

        internal NxProperty(string name, NxValue value, int line)
        {
            Name = name;
            Value = value;
            Line = line;
        }
    }

    /// <summary>An element declaration: "button #play" or "@time".</summary>
    internal sealed class NxNode
    {
        private readonly List<NxProperty> _properties = new();
        private readonly List<NxNode> _children = new();

        internal string Type { get; }

        /// <summary>Name and id suffixes, e.g. "play" in "button #play".</summary>
        internal string Name { get; }

        internal string SourceFile { get; }
        internal int Line { get; }

        /// <summary>Child nodes declared in this element's scope.</summary>
        internal IReadOnlyList<NxNode> Children => _children;

        internal IReadOnlyList<NxProperty> Properties => _properties;

        internal NxNode(string type, string name, string sourceFile, int line)
        {
            Type = type;
            Name = name;
            SourceFile = sourceFile;
            Line = line;
        }

        internal void AddChild(NxNode node) => _children.Add(node);

        internal void AddProperty(NxProperty property) => _properties.Add(property);

        internal NxProperty? FindProperty(string name)
        {
            foreach (NxProperty property in _properties)
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    return property;
            }

            return null;
        }

        internal NxNode? FindChild(string name)
        {
            foreach (NxNode child in _children)
            {
                if (string.Equals(child.Name, name, StringComparison.OrdinalIgnoreCase))
                    return child;
            }

            return null;
        }

        public override string ToString() => $"{Type}{(string.IsNullOrEmpty(Name) ? string.Empty : " " + Name)}";
    }

    /// <summary>One animation keyframe: offset, interpolated properties and easing.</summary>
    internal sealed class NxKeyframe
    {
        internal double Offset { get; }
        internal string Easing { get; }
        internal IReadOnlyList<NxProperty> Properties { get; }

        internal NxKeyframe(double offset, string easing, IReadOnlyList<NxProperty> properties)
        {
            Offset = offset;
            Easing = easing ?? "linear";
            Properties = properties;
        }
    }

    /// <summary>One declared animation: "animation gain: ... { ... }".</summary>
    internal sealed class NxAnimation
    {
        internal string Name { get; }
        internal double DurationMs { get; }
        internal bool Loop { get; }
        internal bool Alternate { get; }
        internal string Easing { get; }
        internal IReadOnlyList<NxKeyframe> Keyframes { get; }
        internal bool RandomOffset { get; }

        internal NxAnimation(
            string name,
            double durationMs,
            bool loop,
            bool alternate,
            string easing,
            IReadOnlyList<NxKeyframe> keyframes,
            bool randomOffset = false)
        {
            Name = name;
            DurationMs = Math.Max(1.0, durationMs);
            Loop = loop;
            Alternate = alternate;
            Easing = easing;
            Keyframes = keyframes;
            RandomOffset = randomOffset;
        }
    }

    /// <summary>
    /// A parsed .nx document. It is plain data: no window knows about it until
    /// the runtime instantiates a surface from it.
    /// </summary>
    internal sealed class NxDocument
    {
        private readonly List<NxNode> _roots = new();
        private readonly List<NxAnimation> _animations = new();

        internal string SourcePath { get; }
        internal IReadOnlyList<NxNode> Roots => _roots;
        internal IReadOnlyList<NxAnimation> Animations => _animations;
        internal IReadOnlyDictionary<string, NxValue> Tokens => TokenTable;

        internal Dictionary<string, NxValue> TokenTable { get; } = new(StringComparer.OrdinalIgnoreCase);

        internal NxDocument(string sourcePath)
        {
            SourcePath = sourcePath ?? "<nx>";
        }

        internal void AddRoot(NxNode node) => _roots.Add(node);

        internal void AddAnimation(NxAnimation animation) => _animations.Add(animation);
    }
}

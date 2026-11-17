using System;
using System.Globalization;

namespace nyxWM.Nx
{
    /// <summary>
    /// Everything a widget needs while it measures, arranges and paints: the data
    /// providers, a compiled-binding evaluator and the animation clock.
    /// </summary>
    internal sealed class NxContext
    {
        internal NxContext(
            NxDataRegistry registry,
            NxExpressionEvaluator evaluator,
            NxRuntime? runtime = null)
        {
            Registry = registry;
            Evaluator = evaluator;
            Runtime = runtime;
        }

        internal NxDataRegistry Registry { get; }

        internal NxExpressionEvaluator Evaluator { get; }

        /// <summary>The runtime that owns this surface, used to run button actions.</summary>
        internal NxRuntime? Runtime { get; }

        /// <summary>Animation clock in milliseconds. Advanced once per frame.</summary>
        internal double NowMs { get; set; }
    }

    /// <summary>How a declared width or height is interpreted.</summary>
    internal enum NxSizeMode
    {
        /// <summary>As large as the content needs.</summary>
        Auto,
        Absolute,
        Percent,
        /// <summary>Takes the space left over by its siblings.</summary>
        Fill
    }

    internal readonly struct NxSize
    {
        internal NxSizeMode Mode { get; }
        internal double Value { get; }

        private NxSize(NxSizeMode mode, double value)
        {
            Mode = mode;
            Value = value;
        }

        internal static NxSize Auto => new(NxSizeMode.Auto, 0.0);

        internal static NxSize Fill => new(NxSizeMode.Fill, 0.0);

        internal static NxSize Absolute(double value) => new(NxSizeMode.Absolute, value);

        internal static NxSize Percent(double value) => new(NxSizeMode.Percent, value);

        /// <summary>
        /// Reads "width: 120", "width: 50%", "width: fill" or "width: auto".
        /// </summary>
        internal static NxSize Parse(NxValue? value, NxExpressionEvaluator evaluator)
        {
            if (value == null)
                return Auto;

            if (value.Kind == NxValueKind.Text || value.Kind == NxValueKind.Expression)
            {
                string text = evaluator.EvaluateText(value, string.Empty).Trim();

                if (text.Length == 0)
                    return Auto;

                if (text.Equals("fill", StringComparison.OrdinalIgnoreCase) ||
                    text.Equals("flex", StringComparison.OrdinalIgnoreCase))
                {
                    return Fill;
                }

                if (text.Equals("auto", StringComparison.OrdinalIgnoreCase))
                    return Auto;

                if (text.EndsWith('%') &&
                    double.TryParse(
                        text[..^1].Trim(),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out double percent))
                {
                    return Percent(percent);
                }

                return double.TryParse(
                    text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double parsed)
                    ? Absolute(parsed)
                    : Auto;
            }

            return Absolute(evaluator.EvaluateNumber(value, 0.0));
        }

        /// <summary>The pixel value this size resolves to inside <paramref name="available"/>.</summary>
        internal float Resolve(float available, float auto)
        {
            return Mode switch
            {
                NxSizeMode.Absolute => (float)Value,
                NxSizeMode.Percent => available * (float)Value / 100f,
                NxSizeMode.Fill => available,
                _ => auto
            };
        }
    }

    /// <summary>4-sided edge values, used for padding and margin.</summary>
    internal readonly struct NxThickness
    {
        internal float Left { get; }
        internal float Top { get; }
        internal float Right { get; }
        internal float Bottom { get; }

        internal NxThickness(float left, float top, float right, float bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        internal static NxThickness All(float value) => new(value, value, value, value);

        internal static NxThickness Uniform(double value) => All((float)value);

        internal float Horizontal => Left + Right;

        internal float Vertical => Top + Bottom;
    }
}

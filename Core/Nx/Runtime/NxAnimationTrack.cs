using System;
using System.Collections.Generic;

namespace nyxWM.Nx
{
    /// <summary>
    /// A running instance of a declared animation. It owns no window and touches
    /// no GDI object: it only turns the current time into a style, which the
    /// widget then paints. That keeps animation off the UI thread's critical path.
    /// </summary>
    internal sealed class NxAnimationTrack
    {
        private readonly NxAnimation _animation;
        private readonly NxExpressionEvaluator _evaluator;
        private readonly string[] _animatedProperties;
        private readonly double _startMs;
        private readonly double _randomDelayMs;

        private bool _finished;

        internal NxAnimationTrack(
            NxAnimation animation,
            NxExpressionEvaluator evaluator,
            double startMs)
        {
            _animation = animation;
            _evaluator = evaluator;
            _startMs = startMs;
            _animatedProperties = CollectProperties(animation);

            // Random delay: stagger animations across elements
            if (animation.RandomOffset)
                _randomDelayMs = new Random(Environment.TickCount + _startMs.GetHashCode()).NextDouble() * animation.DurationMs * 0.5;
            else
                _randomDelayMs = 0;
        }

        internal string Name => _animation.Name;

        internal bool Loop => _animation.Loop;

        internal bool Alternate => _animation.Alternate;

        internal double DurationMs => _animation.DurationMs;

        /// <summary>True once a non-looping animation has played out.</summary>
        internal bool Finished => _finished;

        /// <summary>Computes the style for <paramref name="nowMs"/> on top of the declared style.</summary>
        internal NxStyle Apply(double nowMs, NxStyle baseStyle)
        {
            double elapsed = Math.Max(0.0, nowMs - _startMs - _randomDelayMs);
            double duration = _animation.DurationMs;
            double progress = elapsed / duration;

            if (!_animation.Loop)
            {
                if (progress >= 1.0)
                {
                    progress = 1.0;
                    _finished = true;
                }
            }
            else if (_animation.Alternate)
            {
                // Ping-pong: even cycles run forward, odd cycles run backward.
                double cycles = progress;
                double whole = Math.Floor(cycles);
                double fraction = cycles - whole;

                progress = (long)whole % 2 == 0 ? fraction : 1.0 - fraction;
            }
            else
            {
                progress -= Math.Floor(progress);
            }

            if (_animation.Keyframes.Count == 0)
                return baseStyle;

            NxKeyframe from = _animation.Keyframes[0];
            NxKeyframe to = from;
            double localT = 0.0;

            for (int i = 0; i < _animation.Keyframes.Count; i++)
            {
                NxKeyframe current = _animation.Keyframes[i];

                if (current.Offset <= progress)
                {
                    from = current;
                    to = i + 1 < _animation.Keyframes.Count ? _animation.Keyframes[i + 1] : current;
                    continue;
                }

                to = current;
                break;
            }

            double span = to.Offset - from.Offset;
            localT = span > 0.0001 ? Math.Clamp((progress - from.Offset) / span, 0.0, 1.0) : 0.0;
            double eased = NxEasing.Apply(from.Easing, localT);

            NxStyle style = baseStyle;

            foreach (string property in _animatedProperties)
            {
                NxValue? startValue = Find(from, property);
                NxValue? endValue = Find(to, property) ?? startValue;

                if (startValue == null && endValue == null)
                    continue;

                if (NxStyle.IsColorProperty(property))
                {
                    NxColor start = _evaluator.EvaluateColor(startValue ?? endValue, NxColor.Transparent);
                    NxColor end = _evaluator.EvaluateColor(endValue ?? startValue, NxColor.Transparent);

                    style = style.WithColor(property, NxEasing.Lerp(start, end, eased));
                }
                else
                {
                    double start = _evaluator.EvaluateNumber(startValue ?? endValue, 0.0);
                    double end = _evaluator.EvaluateNumber(endValue ?? startValue, 0.0);

                    style = style.WithNumber(property, NxEasing.Lerp(start, end, eased));
                }
            }

            return style;
        }

        private static NxValue? Find(NxKeyframe frame, string property)
        {
            foreach (NxProperty candidate in frame.Properties)
            {
                if (string.Equals(candidate.Name, property, StringComparison.OrdinalIgnoreCase))
                    return candidate.Value;
            }

            return null;
        }

        private static string[] CollectProperties(NxAnimation animation)
        {
            var names = new List<string>();

            foreach (NxKeyframe frame in animation.Keyframes)
            {
                foreach (NxProperty property in frame.Properties)
                {
                    if (!NxStyle.IsAnimatable(property.Name))
                        continue;

                    string folded = property.Name.ToLowerInvariant();

                    if (!names.Contains(folded, StringComparer.Ordinal))
                        names.Add(folded);
                }
            }

            return names.ToArray();
        }
    }
}

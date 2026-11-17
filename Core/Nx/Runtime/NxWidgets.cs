using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace nyxWM.Nx
{
    /// <summary>How a panel lays its children out.</summary>
    internal enum NxPanelFlow
    {
        Row,
        Column,
        Stack
    }

    /// <summary>
    /// A container. Row and column place children one after another; stack gives
    /// every child the same box so elements can be layered freely.
    /// </summary>
    internal sealed class NxPanel : NxWidget
    {
        private readonly List<NxWidget> _children = new();

        internal NxPanel(NxNode node, NxStyle style, NxPanelFlow flow)
            : base(node, style)
        {
            Flow = flow;
        }

        internal NxPanelFlow Flow { get; }

        internal IReadOnlyList<NxWidget> Children => _children;

        /// <summary>Main-axis placement: start, center or end.</summary>
        internal string Justify { get; set; } = "start";

        /// <summary>Cross-axis placement: start, center or end.</summary>
        internal string Cross { get; set; } = "start";

        internal void Add(NxWidget child) => _children.Add(child);

        protected override SizeF MeasureContent(NxContext context, SizeF available)
        {
            if (_children.Count == 0)
                return SizeF.Empty;

            float padding = (float)AnimatedStyle.Padding;
            float spacing = (float)AnimatedStyle.Spacing;
            var inner = new SizeF(
                Math.Max(1f, available.Width - padding * 2),
                Math.Max(1f, available.Height - padding * 2));

            if (Flow == NxPanelFlow.Stack)
            {
                float width = 0f;
                float height = 0f;

                foreach (NxWidget child in _children)
                {
                    if (!child.AnimatedStyle.Visible)
                        continue;

                    SizeF size = child.Measure(context, inner);
                    width = Math.Max(width, size.Width + child.X);
                    height = Math.Max(height, size.Height + child.Y);
                }

                return new SizeF(width, height);
            }

            float total = 0f;
            float longest = 0f;
            int visible = 0;

            foreach (NxWidget child in _children)
            {
                if (!child.AnimatedStyle.Visible)
                    continue;

                SizeF size = child.Measure(context, inner);
                visible++;

                if (Flow == NxPanelFlow.Row)
                {
                    total += size.Width;
                    longest = Math.Max(longest, size.Height);
                }
                else
                {
                    total += size.Height;
                    longest = Math.Max(longest, size.Width);
                }
            }

            if (visible > 1)
                total += spacing * (visible - 1);

            return Flow == NxPanelFlow.Row
                ? new SizeF(total, longest)
                : new SizeF(longest, total);
        }

        protected override void OnArrange(NxContext context, RectangleF box)
        {
            float padding = (float)AnimatedStyle.Padding;

            var content = new RectangleF(
                box.X + padding,
                box.Y + padding,
                Math.Max(0f, box.Width - padding * 2),
                Math.Max(0f, box.Height - padding * 2));

            if (Flow == NxPanelFlow.Stack)
                ArrangeStack(context, content);
            else
                ArrangeLinear(context, content);
        }

        private void ArrangeStack(NxContext context, RectangleF content)
        {
            foreach (NxWidget child in _children)
            {
                if (!child.AnimatedStyle.Visible)
                    continue;

                SizeF size = child.Measure(context, new SizeF(content.Width, content.Height));

                var slot = new RectangleF(
                    content.X + child.X + (float)child.AnimatedStyle.OffsetX,
                    content.Y + child.Y + (float)child.AnimatedStyle.OffsetY,
                    size.Width,
                    size.Height);

                child.Arrange(context, slot);
            }
        }

        private void ArrangeLinear(NxContext context, RectangleF content)
        {
            bool horizontal = Flow == NxPanelFlow.Row;
            float spacing = (float)AnimatedStyle.Spacing;
            float available = horizontal ? content.Width : content.Height;

            var sizes = new List<SizeF>(_children.Count);
            float fixedTotal = 0f;
            int fillCount = 0;
            int visibleCount = 0;

            foreach (NxWidget child in _children)
            {
                SizeF size = child.Measure(context, new SizeF(content.Width, content.Height));
                sizes.Add(size);

                if (!child.AnimatedStyle.Visible)
                    continue;

                visibleCount++;

                bool fills = horizontal
                    ? child.Width.Mode == NxSizeMode.Fill
                    : child.Height.Mode == NxSizeMode.Fill;

                if (fills)
                    fillCount++;
                else
                    fixedTotal += horizontal ? size.Width : size.Height;
            }

            if (visibleCount > 1)
                fixedTotal += spacing * (visibleCount - 1);

            float fillShare = fillCount > 0 ? Math.Max(0f, (available - fixedTotal) / fillCount) : 0f;

            // The main axis is centred or right-aligned as a block, which is what
            // "justify: center" means for a bar with a few items in it.
            float totalUsed = 0f;

            for (int i = 0; i < _children.Count; i++)
            {
                if (!_children[i].AnimatedStyle.Visible)
                    continue;

                bool fills = horizontal
                    ? _children[i].Width.Mode == NxSizeMode.Fill
                    : _children[i].Height.Mode == NxSizeMode.Fill;

                totalUsed += fills ? fillShare : (horizontal ? sizes[i].Width : sizes[i].Height);

                if (i < _children.Count - 1)
                    totalUsed += spacing;
            }

            float cursor = (horizontal ? content.X : content.Y) + AlignmentOffset(Justify, available, totalUsed);

            for (int i = 0; i < _children.Count; i++)
            {
                NxWidget child = _children[i];

                if (!child.AnimatedStyle.Visible)
                    continue;

                SizeF size = sizes[i];

                bool fills = horizontal
                    ? child.Width.Mode == NxSizeMode.Fill
                    : child.Height.Mode == NxSizeMode.Fill;

                float extent = fills ? fillShare : (horizontal ? size.Width : size.Height);
                float crossAxis = horizontal
                    ? content.Y + AlignmentOffset(Cross, content.Height, size.Height)
                    : content.X + AlignmentOffset(Cross, content.Width, size.Width);

                var slot = horizontal
                    ? new RectangleF(cursor, crossAxis, extent, size.Height)
                    : new RectangleF(crossAxis, cursor, size.Width, extent);

                child.Arrange(context, new RectangleF(
                    slot.X + (float)child.AnimatedStyle.OffsetX,
                    slot.Y + (float)child.AnimatedStyle.OffsetY,
                    slot.Width,
                    slot.Height));

                cursor += extent + spacing;
            }
        }

        private static float AlignmentOffset(string alignment, float available, float used)
        {
            return alignment.ToLowerInvariant() switch
            {
                "center" or "middle" => (available - used) / 2f,
                "end" or "right" or "bottom" => available - used,
                _ => 0f
            };
        }

        internal override void Paint(Graphics graphics, NxContext context)
        {
            if (!AnimatedStyle.Visible)
                return;

            PaintBackground(graphics, context, Bounds, (float)AnimatedStyle.Radius);
        }

        internal override NxWidget? HitTest(PointF point)
        {
            if (!AnimatedStyle.Visible)
                return null;

            // Topmost child wins, so later declarations sit above earlier ones.
            for (int i = _children.Count - 1; i >= 0; i--)
            {
                NxWidget? hit = _children[i].HitTest(point);

                if (hit != null)
                    return hit;
            }

            return Bounds.Contains(point) ? this : null;
        }
    }

    /// <summary>A plain box: background, border, optional icon and text.</summary>
    internal class NxBox : NxWidget
    {
        private NxValue? _textBinding;

        protected string Text = string.Empty;

        internal NxBox(NxNode node, NxStyle style)
            : base(node, style)
        {
            _textBinding = node.FindProperty("text")?.Value;
        }

        /// <summary>Overrides the text binding, used by the "@key" shorthand.</summary>
        internal void BindText(NxValue value)
        {
            _textBinding = value;
        }

        internal override void Update(NxContext context)
        {
            base.Update(context);
            Text = context.Evaluator.EvaluateText(_textBinding, string.Empty);
        }

        /// <summary>Natural text width; a proportional estimate avoids a GDI measure per frame.</summary>
        protected SizeF MeasureText()
        {
            if (string.IsNullOrEmpty(Text))
                return SizeF.Empty;

            Font font = NxFontCache.Get(AnimatedStyle.FontFamily, AnimatedStyle.FontSize, AnimatedStyle.FontBold);

            return new SizeF(Text.Length * (float)AnimatedStyle.FontSize * 0.62f, font.Height + 2f);
        }

        protected SizeF MeasureIcon()
        {
            double size = AnimatedStyle.IconSize > 0 ? AnimatedStyle.IconSize : 0.0;

            return size > 0 ? new SizeF((float)size, (float)size) : SizeF.Empty;
        }

        protected override SizeF MeasureContent(NxContext context, SizeF available)
        {
            SizeF icon = MeasureIcon();
            SizeF text = MeasureText();

            return new SizeF(
                Math.Max(icon.Width, text.Width),
                Math.Max(icon.Height, text.Height));
        }

        internal override void Paint(Graphics graphics, NxContext context)
        {
            if (!AnimatedStyle.Visible)
                return;

            PaintBackground(graphics, context, Bounds, (float)AnimatedStyle.Radius);

            float padding = (float)AnimatedStyle.Padding;

            var inner = new RectangleF(
                Bounds.X + padding,
                Bounds.Y + padding,
                Math.Max(0f, Bounds.Width - padding * 2),
                Math.Max(0f, Bounds.Height - padding * 2));

            if (AnimatedStyle.Icon.Length > 0)
            {
                PaintIcon(graphics, inner);

                if (string.IsNullOrEmpty(Text))
                    return;
            }

            PaintText(graphics, Text, inner);
        }
    }

    /// <summary>
    /// A clickable element. It owns hover and press feedback and reports the
    /// action named in the document; the runtime decides what that action means.
    /// </summary>
    internal sealed class NxButton : NxBox
    {
        private readonly float _hoverScale;
        private float _hoverAmount;
        private double _lastMs = double.NaN;

        internal NxButton(NxNode node, NxStyle style)
            : base(node, style)
        {
            Action = node.FindProperty("action")?.Value.Text ?? string.Empty;
            Tooltip = node.FindProperty("tooltip")?.Value.Text ?? string.Empty;
            _hoverScale = (float)(style.Scale <= 0 ? 1.08 : style.Scale);
        }

        /// <summary>Document action, e.g. "launcher", "rofi" or "command:wt.exe".</summary>
        internal string Action { get; }

        internal string Tooltip { get; }

        /// <summary>Current hover amount, 0 to 1, used to grow the button.</summary>
        internal float HoverAmount => _hoverAmount;

        /// <summary>Scale applied on top of the element's own scale.</summary>
        internal float HoverFactor => 1f + (_hoverScale - 1f) * _hoverAmount;

        internal override void Update(NxContext context)
        {
            base.Update(context);

            double deltaMs = double.IsNaN(_lastMs) ? 16.0 : Math.Clamp(context.NowMs - _lastMs, 0.0, 100.0);
            _lastMs = context.NowMs;

            // Ease toward the target at a fixed rate so the motion is the same at
            // 60 Hz and at 240 Hz.
            float step = (float)Math.Clamp(deltaMs / 120.0, 0.0, 1.0);
            float target = Hovered || Pressed ? 1f : 0f;

            _hoverAmount += (target - _hoverAmount) * step;

            if (Math.Abs(target - _hoverAmount) < 0.005f)
                _hoverAmount = target;
        }

        internal override void Paint(Graphics graphics, NxContext context)
        {
            if (!AnimatedStyle.Visible)
                return;

            if (_hoverAmount <= 0.005f)
            {
                base.Paint(graphics, context);
                return;
            }

            // Growing about the centre keeps the button inside its slot.
            float factor = HoverFactor;
            float width = Bounds.Width * factor;
            float height = Bounds.Height * factor;
            var scaled = new RectangleF(
                Bounds.X - (width - Bounds.Width) / 2f,
                Bounds.Y - (height - Bounds.Height) / 2f,
                width,
                height);

            RectangleF saved = Bounds;
            MoveBounds(scaled);
            base.Paint(graphics, context);
            MoveBounds(saved);
        }

        private void MoveBounds(RectangleF box)
        {
            SetBounds(box);
        }
    }

    /// <summary>A progress or level bar, driven by a binding.</summary>
    internal sealed class NxBar : NxWidget
    {
        private readonly NxValue? _valueBinding;
        private readonly NxValue? _maxBinding;
        private readonly NxValue? _fillBinding;

        private NxColor _fill;
        private double _fraction;

        internal NxBar(NxNode node, NxStyle style)
            : base(node, style)
        {
            _valueBinding = node.FindProperty("value")?.Value;
            _maxBinding = node.FindProperty("max")?.Value;
            _fillBinding = node.FindProperty("fill")?.Value ?? node.FindProperty("accent")?.Value;
            _fill = style.Foreground;

            Vertical = string.Equals(
                node.FindProperty("orientation")?.Value.Text,
                "vertical",
                StringComparison.OrdinalIgnoreCase);
        }

        internal bool Vertical { get; }

        internal override void Update(NxContext context)
        {
            base.Update(context);

            double value = context.Evaluator.EvaluateNumber(_valueBinding, 0.0);
            double max = context.Evaluator.EvaluateNumber(_maxBinding, 1.0);

            if (max <= 0.0)
                max = 1.0;

            // A bare percentage such as "{sys.cpu}" needs no division in the document.
            if (max == 1.0 && value > 1.0)
                value /= 100.0;

            _fraction = Math.Clamp(value / max, 0.0, 1.0);
            _fill = context.Evaluator.EvaluateColor(_fillBinding, AnimatedStyle.Foreground);
        }

        protected override SizeF MeasureContent(NxContext context, SizeF available)
        {
            return Vertical ? new SizeF(8f, 60f) : new SizeF(120f, 8f);
        }

        internal override void Paint(Graphics graphics, NxContext context)
        {
            if (!AnimatedStyle.Visible)
                return;

            NxStyle style = AnimatedStyle;
            float radius = (float)style.Radius;

            using (GraphicsPath track = BuildShape(style.Shape, Bounds, radius))
            using (var trackBrush = new SolidBrush(style.ToColor(style.PrimaryBackground, 1.0)))
            {
                graphics.FillPath(trackBrush, track);
            }

            float filled = (float)_fraction;

            var fill = Vertical
                ? new RectangleF(Bounds.X, Bounds.Bottom - Bounds.Height * filled, Bounds.Width, Bounds.Height * filled)
                : new RectangleF(Bounds.X, Bounds.Y, Bounds.Width * filled, Bounds.Height);

            if (fill.Width <= 0.5f || fill.Height <= 0.5f)
                return;

            using GraphicsPath fillPath = BuildShape(style.Shape, fill, radius);
            using var fillBrush = new SolidBrush(style.ToColor(_fill, 1.0));

            graphics.FillPath(fillBrush, fillPath);
        }
    }

    /// <summary>Empty space that pushes its siblings apart.</summary>
    internal sealed class NxSpacer : NxWidget
    {
        internal NxSpacer(NxNode node, NxStyle style)
            : base(node, style)
        {
        }

        protected override SizeF MeasureContent(NxContext context, SizeF available)
        {
            return new SizeF((float)AnimatedStyle.Spacing, (float)AnimatedStyle.Spacing);
        }

        internal override void Paint(Graphics graphics, NxContext context)
        {
        }
    }

    /// <summary>Builds a widget tree from a parsed document.</summary>
    internal static class NxWidgetFactory
    {
        internal static NxWidget Build(
            NxNode node,
            NxExpressionEvaluator evaluator,
            IReadOnlyDictionary<string, NxAnimation> animations,
            NxStyle? inherited = null,
            double nowMs = 0.0)
        {
            NxStyle style = NxStyle.FromNode(node, evaluator, inherited);
            NxWidget widget = Create(node, style);

            widget.ReadLayout(evaluator);

            // A leaf may still carry one child, which fills it: that is how
            // "box { label { text: \"hi\" } }" works.
            if (widget is NxBox box && node.Children.Count > 0)
                ApplyChildAsContent(box, node.Children[0], evaluator, animations, style, nowMs);

            if (widget is NxPanel panel)
            {
                foreach (NxNode child in node.Children)
                    panel.Add(Build(child, evaluator, animations, style, nowMs));

                panel.Justify = ReadText(node, "justify", evaluator, panel.Justify);
                panel.Cross = ReadText(node, "cross", evaluator, panel.Cross);
            }

            ApplyAnimation(node, widget, animations, evaluator, nowMs);

            return widget;
        }

        private static void ApplyChildAsContent(
            NxBox box,
            NxNode child,
            NxExpressionEvaluator evaluator,
            IReadOnlyDictionary<string, NxAnimation> animations,
            NxStyle inherited,
            double nowMs)
        {
            // The child's text becomes the box's own text when the box has none,
            // and its other properties (icon, colours) are inherited downward.
            if (box.Node.FindProperty("text") == null)
            {
                NxProperty? text = child.FindProperty("text");

                if (text != null)
                    box.BindText(text.Value);
            }

            _ = (evaluator, animations, inherited, nowMs);
        }

        private static string ReadText(
            NxNode node,
            string property,
            NxExpressionEvaluator evaluator,
            string fallback)
        {
            NxProperty? found = node.FindProperty(property);

            return found == null ? fallback : evaluator.EvaluateText(found.Value, fallback);
        }

        private static NxWidget Create(NxNode node, NxStyle style)
        {
            string type = node.Type.ToLowerInvariant();

            // "@time" and "@media.title" are shorthand for a label bound to that key.
            if (type.StartsWith('@'))
            {
                var label = new NxBox(node, style);
                string key = Shorthand(type[1..]);

                label.BindText(NxValue.FromExpression(
                    new NxExpression(key, new[] { key }, node.Line),
                    node.Line));

                return label;
            }

            return type switch
            {
                "row" or "hstack" => new NxPanel(node, style, NxPanelFlow.Row),
                "column" or "vstack" => new NxPanel(node, style, NxPanelFlow.Column),
                "stack" or "canvas" or "surface" => new NxPanel(node, style, NxPanelFlow.Stack),
                "widget" or "card" or "group" => new NxPanel(node, style, NxPanelFlow.Column),
                "button" => new NxButton(node, style),
                "bar" or "progress" => new NxBar(node, style),
                "spacer" or "fill" => new NxSpacer(node, style),
                "divider" or "separator" => new NxBox(node, style with { Shape = "line" }),
                _ => new NxBox(node, style)
            };
        }

        /// <summary>Expands terse bindings: "@time" means "{time.hm}", "@date" means "{time.date}".</summary>
        private static string Shorthand(string key)
        {
            return key.ToLowerInvariant() switch
            {
                "time" => "time.hm",
                "date" => "time.date",
                "year" => "time.year",
                "month" => "time.monthname",
                "day" => "time.day",
                "weekday" => "time.weekdayname",
                "track" => "media.title",
                "artist" => "media.artist",
                "cpu" => "sys.cpu",
                "ram" or "mem" => "sys.mem",
                "battery" => "sys.battery",
                "windows" or "win" => "win.count",
                _ => key
            };
        }

        private static void ApplyAnimation(
            NxNode node,
            NxWidget widget,
            IReadOnlyDictionary<string, NxAnimation> animations,
            NxExpressionEvaluator evaluator,
            double nowMs)
        {
            foreach (NxProperty property in node.Properties)
            {
                string name = property.Name.ToLowerInvariant();

                if (name is not ("animate" or "animation"))
                    continue;

                string animationName = evaluator.EvaluateText(property.Value, string.Empty);

                if (animationName.Length == 0 || !animations.TryGetValue(animationName, out NxAnimation? animation))
                    continue;

                widget.AddTrack(new NxAnimationTrack(animation, evaluator, nowMs));
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace nyxWM.Nx
{
    /// <summary>
    /// One element of a .nx surface. A widget owns its declared style, its layout
    /// size and its running animations, and knows how to measure, arrange and
    /// paint itself. It never talks to a window directly: an animation computes a
    /// new style, and only the paint pass touches GDI.
    /// </summary>
    internal abstract class NxWidget
    {
        private readonly List<NxAnimationTrack> _tracks = new();

        protected NxWidget(NxNode node, NxStyle style)
        {
            Node = node;
            DeclaredStyle = style;
            AnimatedStyle = style;
            Name = node.Name;
            ElementType = node.Type;
        }

        internal string Name { get; }

        internal string ElementType { get; }

        internal NxNode Node { get; }

        /// <summary>The style as written in the document, animations aside.</summary>
        internal NxStyle DeclaredStyle { get; }

        /// <summary>The style for the current frame, after animations.</summary>
        internal NxStyle AnimatedStyle { get; private set; }

        internal NxSize Width { get; private set; } = NxSize.Auto;

        internal NxSize Height { get; private set; } = NxSize.Auto;

        internal float X { get; private set; }

        internal float Y { get; private set; }

        internal float WidthOffset { get; private set; }

        internal float HeightOffset { get; private set; }

        internal float Opacity { get; private set; } = 1f;

        /// <summary>Box assigned by the parent during layout.</summary>
        internal RectangleF Bounds { get; private set; }

        internal bool Hovered { get; set; }

        internal bool Pressed { get; set; }

        internal IReadOnlyList<NxAnimationTrack> Tracks => _tracks;

        /// <summary>Runs before measuring: resolves bindings and advances animations.</summary>
        internal virtual void Update(NxContext context)
        {
            float opacity = (float)Math.Clamp(DeclaredStyle.Opacity, 0.0, 1.0);

            foreach (NxAnimationTrack track in _tracks)
            {
                NxStyle animated = track.Apply(context.NowMs, DeclaredStyle);
                opacity *= (float)Math.Clamp(animated.Opacity, 0.0, 1.0);
                AnimatedStyle = animated;
            }

            Opacity = opacity;
        }

        /// <summary>Registers a running animation on this element.</summary>
        internal void AddTrack(NxAnimationTrack track) => _tracks.Add(track);

        internal bool HasActiveAnimation
        {
            get
            {
                foreach (NxAnimationTrack track in _tracks)
                {
                    if (track.Loop || !track.Finished)
                        return true;
                }

                return false;
            }
        }

        /// <summary>Natural size of the element's own content, padding excluded.</summary>
        protected virtual SizeF MeasureContent(NxContext context, SizeF available) => SizeF.Empty;

        /// <summary>Draws the element inside its bounds, opacity already applied to colours.</summary>
        internal abstract void Paint(Graphics graphics, NxContext context);

        internal SizeF Measure(NxContext context, SizeF available)
        {
            SizeF content = MeasureContent(context, available);
            NxStyle style = AnimatedStyle;
            float padding = (float)style.Padding;

            float width = Resolve(Width, style.WidthOverride, available.Width, content.Width + padding * 2);
            float height = Resolve(Height, style.HeightOverride, available.Height, content.Height + padding * 2);

            return new SizeF(Math.Max(0f, width), Math.Max(0f, height));
        }

        private static float Resolve(NxSize size, double? animationOverride, float available, float natural)
        {
            float resolved = size.Resolve(available, natural);

            // A keyframe may resize the element without changing its layout slot.
            if (animationOverride is { } value)
                resolved = (float)value;

            return resolved;
        }

        internal void Arrange(NxContext context, RectangleF box)
        {
            Bounds = box;
            OnArrange(context, box);
        }

        /// <summary>Moves the box without re-running the parent's layout, for hover scale.</summary>
        protected void SetBounds(RectangleF box) => Bounds = box;

        protected virtual void OnArrange(NxContext context, RectangleF box)
        {
        }

        /// <summary>Reads layout properties off the node. Called once, at build time.</summary>
        internal void ReadLayout(NxExpressionEvaluator evaluator)
        {
            Width = NxSize.Parse(Node.FindProperty("width")?.Value, evaluator);
            Height = NxSize.Parse(Node.FindProperty("height")?.Value, evaluator);
            X = (float)evaluator.EvaluateNumber(Node.FindProperty("x")?.Value, 0.0);
            Y = (float)evaluator.EvaluateNumber(Node.FindProperty("y")?.Value, 0.0);
            WidthOffset = (float)evaluator.EvaluateNumber(Node.FindProperty("widthoffset")?.Value, 0.0);
            HeightOffset = (float)evaluator.EvaluateNumber(Node.FindProperty("heightoffset")?.Value, 0.0);
        }

        /// <summary>The element under a point, or null. Hit testing ignores invisible elements.</summary>
        internal virtual NxWidget? HitTest(PointF point)
        {
            return !AnimatedStyle.Visible ? null : (Bounds.Contains(point) ? this : null);
        }

        /// <summary>Paints the background, border and shadow of this element's box.</summary>
        protected void PaintBackground(Graphics graphics, NxContext context, RectangleF box, float cornerRadius)
        {
            NxStyle style = AnimatedStyle;

            if (style.Shadow && style.Background.Count > 0)
                PaintShadow(graphics, box, cornerRadius, style);

            if (style.Background.Count == 0 || box.Width <= 0 || box.Height <= 0)
                return;

            using GraphicsPath path = BuildShape(style.Shape, box, cornerRadius);

            using (Brush brush = BuildBrush(style, box))
                graphics.FillPath(brush, path);

            if (style.BorderWidth > 0)
            {
                using var pen = new Pen(
                    style.ToColor(style.BorderColor, 1.0),
                    (float)style.BorderWidth);

                graphics.DrawPath(pen, path);
            }
        }

        private void PaintShadow(Graphics graphics, RectangleF box, float cornerRadius, NxStyle style)
        {
            NxColor shadowColor = style.Background[0];

            for (int i = 3; i >= 1; i--)
            {
                var layer = new RectangleF(
                    box.X - i,
                    box.Y - i + i * 0.6f,
                    box.Width + i * 2,
                    box.Height + i * 2);

                using GraphicsPath path = BuildShape(style.Shape, layer, cornerRadius + i);
                using var brush = new SolidBrush(
                    Color.FromArgb(Math.Max(0, 18 - i * 4), 0, 0, 0));

                graphics.FillPath(brush, path);
            }
        }

        private static Brush BuildBrush(NxStyle style, RectangleF box)
        {
            if (style.Background.Count == 1)
                return new SolidBrush(style.ToColor(style.Background[0], 1.0));

            var colors = new Color[style.Background.Count];

            for (int i = 0; i < style.Background.Count; i++)
                colors[i] = style.ToColor(style.Background[i], 1.0);

            var rectangle = new RectangleF(box.X, box.Y, Math.Max(1f, box.Width), Math.Max(1f, box.Height));

            // A three-stop gradient cannot be expressed by LinearGradientBrush, so
            // the middle colour is blended into the two-stop ramp.
            if (colors.Length > 2)
                colors = new[] { colors[0], colors[1], colors[^1] };

            return new LinearGradientBrush(
                rectangle,
                colors[0],
                colors[^1],
                style.GradientHorizontal ? 0f : 90f);
        }

        /// <summary>Builds the silhouette a shape name asks for.</summary>
        internal static GraphicsPath BuildShape(string shape, RectangleF box, float radius)
        {
            var path = new GraphicsPath();
            float x = box.X;
            float y = box.Y;
            float w = Math.Max(0.001f, box.Width);
            float h = Math.Max(0.001f, box.Height);

            switch (shape.ToLowerInvariant())
            {
                case "circle":
                    path.AddEllipse(x, y, w, h);
                    break;

                case "triangle":
                    path.AddPolygon(new[]
                    {
                        new PointF(x + w / 2f, y),
                        new PointF(x + w, y + h),
                        new PointF(x, y + h)
                    });
                    break;

                case "diamond":
                    path.AddPolygon(new[]
                    {
                        new PointF(x + w / 2f, y),
                        new PointF(x + w, y + h / 2f),
                        new PointF(x + w / 2f, y + h),
                        new PointF(x, y + h / 2f)
                    });
                    break;

                case "line":
                    path.AddRectangle(new RectangleF(x, y + h / 2f - Math.Max(1f, h / 6f), w, Math.Max(2f, h / 3f)));
                    break;

                case "square":
                    path.AddRectangle(new RectangleF(x, y, w, h));
                    break;

                default:
                    float r = Math.Min(radius, Math.Min(w, h) / 2f);

                    if (r <= 0.5f)
                    {
                        path.AddRectangle(new RectangleF(x, y, w, h));
                        break;
                    }

                    path.AddArc(x, y, r * 2, r * 2, 180, 90);
                    path.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
                    path.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
                    path.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
                    path.CloseFigure();
                    break;
            }

            return path;
        }

        /// <summary>Draws an icon inside a box, honouring an explicit icon size.</summary>
        protected void PaintIcon(Graphics graphics, RectangleF box)
        {
            NxStyle style = AnimatedStyle;

            if (style.Icon.Length == 0)
                return;

            float size = style.IconSize > 0
                ? (float)style.IconSize
                : Math.Min(box.Width, box.Height);

            var target = new RectangleF(
                box.X + (box.Width - size) / 2f,
                box.Y + (box.Height - size) / 2f,
                size,
                size);

            // An icon value that names a file draws that file; anything else is the
            // name of a built-in glyph. A file that will not load falls through, so
            // a missing icon leaves a gap instead of an exception.
            if (NxImageCache.IsPath(style.Icon))
            {
                Image? image = NxImageCache.Get(style.Icon, Node.SourceFile);

                if (image != null)
                {
                    DrawImage(graphics, image, target);
                    return;
                }
            }

            NxIcons.Draw(graphics, style.Icon, target, style.ToColor(style.Foreground, 1.0));
        }

        /// <summary>Draws a bitmap inside a box, scaled to fit and centred.</summary>
        private static void DrawImage(Graphics graphics, Image image, RectangleF box)
        {
            if (image.Width <= 0 || image.Height <= 0 || box.Width <= 0 || box.Height <= 0)
                return;

            float scale = Math.Min(box.Width / image.Width, box.Height / image.Height);
            float width = image.Width * scale;
            float height = image.Height * scale;

            var destination = new RectangleF(
                box.X + (box.Width - width) / 2f,
                box.Y + (box.Height - height) / 2f,
                width,
                height);

            graphics.DrawImage(image, destination);
        }

        /// <summary>Draws a text run inside a box, honouring the style's alignment.</summary>
        protected void PaintText(Graphics graphics, string text, RectangleF box)
        {
            if (string.IsNullOrEmpty(text) || box.Width <= 1 || box.Height <= 1)
                return;

            NxStyle style = AnimatedStyle;
            Font font = NxFontCache.Get(style.FontFamily, style.FontSize, style.FontBold);
            Color color = style.ToColor(style.Foreground, 1.0);

            var alignment = style.TextAlign.ToLowerInvariant() switch
            {
                "left" or "start" => StringAlignment.Near,
                "right" or "end" => StringAlignment.Far,
                _ => StringAlignment.Center
            };

            using var format = new StringFormat
            {
                Alignment = alignment,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            };

            using var brush = new SolidBrush(color);
            graphics.DrawString(text, font, brush, box, format);
        }
    }
}

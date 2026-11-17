using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace nyxWM.Nx
{
    /// <summary>
    /// Built-in vector icons a .nx document names with "icon: play". Everything is
    /// drawn from primitives so no image files ship with the language and an icon
    /// stays sharp at any size.
    ///
    /// A document may also write "icon: Res/Icons/foo.png" or a single glyph, in
    /// which case the widget draws that instead.
    /// </summary>
    internal static class NxIcons
    {
        private static readonly HashSet<string> Known = new(StringComparer.OrdinalIgnoreCase)
        {
            "play", "pause", "stop", "next", "prev", "music", "volume", "mute",
            "search", "apps", "grid", "power", "settings", "terminal", "window",
            "close", "plus", "minus", "check", "dot", "circle", "square", "ring",
            "triangle", "diamond", "star", "heart", "sun", "moon", "cloud",
            "battery", "cpu", "ram", "clock", "calendar", "globe", "wifi",
            "chevron-up", "chevron-down", "chevron-left", "chevron-right",
            "arrow-up", "arrow-down", "arrow-left", "arrow-right", "pin", "bell"
        };

        internal static bool IsKnown(string name) => name.Length > 0 && Known.Contains(name);

        /// <summary>
        /// Paints an icon into <paramref name="box"/>. Unknown names draw nothing,
        /// so a typo leaves a gap rather than a crash.
        /// </summary>
        internal static void Draw(Graphics graphics, string name, RectangleF box, Color color)
        {
            if (box.Width <= 0 || box.Height <= 0)
                return;

            using var pen = new Pen(color, Math.Max(1.5f, box.Height * 0.09f))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };

            using var brush = new SolidBrush(color);

            float cx = box.X + box.Width / 2f;
            float cy = box.Y + box.Height / 2f;
            float w = box.Width;
            float h = box.Height;
            float unit = Math.Min(w, h) / 2f;

            switch (name.ToLowerInvariant())
            {
                case "play":
                    graphics.FillPolygon(brush, new[]
                    {
                        new PointF(cx - unit * 0.55f, cy - unit * 0.8f),
                        new PointF(cx + unit * 0.75f, cy),
                        new PointF(cx - unit * 0.55f, cy + unit * 0.8f)
                    });
                    break;

                case "pause":
                    FillBar(graphics, brush, cx - unit * 0.42f, cy, unit * 0.24f, unit * 1.5f);
                    FillBar(graphics, brush, cx + unit * 0.42f, cy, unit * 0.24f, unit * 1.5f);
                    break;

                case "stop":
                    graphics.FillRectangle(
                        brush,
                        cx - unit * 0.6f,
                        cy - unit * 0.6f,
                        unit * 1.2f,
                        unit * 1.2f);
                    break;

                case "next":
                    graphics.FillPolygon(brush, new[]
                    {
                        new PointF(cx - unit * 0.8f, cy - unit * 0.7f),
                        new PointF(cx + unit * 0.15f, cy),
                        new PointF(cx - unit * 0.8f, cy + unit * 0.7f)
                    });
                    FillBar(graphics, brush, cx + unit * 0.55f, cy, unit * 0.22f, unit * 1.4f);
                    break;

                case "prev":
                    graphics.FillPolygon(brush, new[]
                    {
                        new PointF(cx + unit * 0.8f, cy - unit * 0.7f),
                        new PointF(cx - unit * 0.15f, cy),
                        new PointF(cx + unit * 0.8f, cy + unit * 0.7f)
                    });
                    FillBar(graphics, brush, cx - unit * 0.55f, cy, unit * 0.22f, unit * 1.4f);
                    break;

                case "music":
                    graphics.DrawEllipse(pen, cx - unit * 0.9f, cy + unit * 0.1f, unit * 0.7f, unit * 0.55f);
                    graphics.DrawEllipse(pen, cx + unit * 0.15f, cy - unit * 0.25f, unit * 0.7f, unit * 0.55f);
                    graphics.DrawLine(pen, cx - unit * 0.55f, cy + unit * 0.3f, cx - unit * 0.55f, cy - unit * 0.85f);
                    graphics.DrawLine(pen, cx - unit * 0.55f, cy - unit * 0.85f, cx + unit * 0.5f, cy - unit * 1.0f);
                    graphics.DrawLine(pen, cx + unit * 0.5f, cy - unit * 1.0f, cx + unit * 0.5f, cy);
                    break;

                case "volume":
                    graphics.FillPolygon(brush, new[]
                    {
                        new PointF(cx - unit * 0.9f, cy - unit * 0.3f),
                        new PointF(cx - unit * 0.35f, cy - unit * 0.3f),
                        new PointF(cx + unit * 0.15f, cy - unit * 0.85f),
                        new PointF(cx + unit * 0.15f, cy + unit * 0.85f),
                        new PointF(cx - unit * 0.35f, cy + unit * 0.3f),
                        new PointF(cx - unit * 0.9f, cy + unit * 0.3f)
                    });
                    graphics.DrawArc(pen, cx - unit * 0.05f, cy - unit * 0.55f, unit * 0.9f, unit * 1.1f, -60, 120);
                    graphics.DrawArc(pen, cx - unit * 0.05f, cy - unit * 0.9f, unit * 1.4f, unit * 1.8f, -60, 120);
                    break;

                case "mute":
                    graphics.FillPolygon(brush, new[]
                    {
                        new PointF(cx - unit * 0.9f, cy - unit * 0.3f),
                        new PointF(cx - unit * 0.35f, cy - unit * 0.3f),
                        new PointF(cx + unit * 0.15f, cy - unit * 0.85f),
                        new PointF(cx + unit * 0.15f, cy + unit * 0.85f),
                        new PointF(cx - unit * 0.35f, cy + unit * 0.3f),
                        new PointF(cx - unit * 0.9f, cy + unit * 0.3f)
                    });
                    graphics.DrawLine(pen, cx + unit * 0.3f, cy - unit * 0.4f, cx + unit * 0.95f, cy + unit * 0.4f);
                    graphics.DrawLine(pen, cx + unit * 0.95f, cy - unit * 0.4f, cx + unit * 0.3f, cy + unit * 0.4f);
                    break;

                case "search":
                    graphics.DrawEllipse(pen, cx - unit * 0.85f, cy - unit * 0.85f, unit * 1.2f, unit * 1.2f);
                    graphics.DrawLine(pen, cx + unit * 0.2f, cy + unit * 0.2f, cx + unit * 0.9f, cy + unit * 0.9f);
                    break;

                case "apps":
                case "grid":
                    float cell = unit * 0.62f;
                    float gap = unit * 0.2f;

                    for (int row = 0; row < 2; row++)
                    {
                        for (int column = 0; column < 2; column++)
                        {
                            graphics.FillRectangle(
                                brush,
                                cx - cell - gap / 2 + column * (cell + gap),
                                cy - cell - gap / 2 + row * (cell + gap),
                                cell,
                                cell);
                        }
                    }
                    break;

                case "window":
                    graphics.DrawRectangle(pen, cx - unit * 0.9f, cy - unit * 0.75f, unit * 1.8f, unit * 1.5f);
                    graphics.DrawLine(pen, cx - unit * 0.9f, cy - unit * 0.4f, cx + unit * 0.9f, cy - unit * 0.4f);
                    graphics.FillRectangle(brush, cx - unit * 0.75f, cy - unit * 0.68f, unit * 0.18f, unit * 0.18f);
                    break;

                case "terminal":
                    graphics.DrawRectangle(pen, cx - unit * 0.95f, cy - unit * 0.8f, unit * 1.9f, unit * 1.6f);
                    graphics.DrawLine(pen, cx - unit * 0.6f, cy - unit * 0.3f, cx - unit * 0.2f, cy + unit * 0.05f);
                    graphics.DrawLine(pen, cx - unit * 0.6f, cy + unit * 0.4f, cx - unit * 0.2f, cy + unit * 0.05f);
                    graphics.DrawLine(pen, cx + unit * 0.1f, cy + unit * 0.4f, cx + unit * 0.6f, cy + unit * 0.4f);
                    break;

                case "settings":
                    graphics.DrawEllipse(pen, cx - unit * 0.4f, cy - unit * 0.4f, unit * 0.8f, unit * 0.8f);
                    graphics.DrawEllipse(pen, cx - unit * 0.95f, cy - unit * 0.95f, unit * 1.9f, unit * 1.9f);
                    graphics.DrawLine(pen, cx, cy - unit, cx, cy - unit * 1.35f);
                    graphics.DrawLine(pen, cx, cy + unit, cx, cy + unit * 1.35f);
                    graphics.DrawLine(pen, cx - unit, cy, cx - unit * 1.35f, cy);
                    graphics.DrawLine(pen, cx + unit, cy, cx + unit * 1.35f, cy);
                    break;

                case "power":
                    graphics.DrawArc(pen, cx - unit, cy - unit, unit * 2f, unit * 2f, -60, 300);
                    graphics.DrawLine(pen, cx, cy - unit * 1.2f, cx, cy - unit * 0.25f);
                    break;

                case "close":
                    graphics.DrawLine(pen, cx - unit * 0.7f, cy - unit * 0.7f, cx + unit * 0.7f, cy + unit * 0.7f);
                    graphics.DrawLine(pen, cx + unit * 0.7f, cy - unit * 0.7f, cx - unit * 0.7f, cy + unit * 0.7f);
                    break;

                case "plus":
                    graphics.DrawLine(pen, cx - unit * 0.8f, cy, cx + unit * 0.8f, cy);
                    graphics.DrawLine(pen, cx, cy - unit * 0.8f, cx, cy + unit * 0.8f);
                    break;

                case "minus":
                    graphics.DrawLine(pen, cx - unit * 0.8f, cy, cx + unit * 0.8f, cy);
                    break;

                case "check":
                    graphics.DrawLine(pen, cx - unit * 0.85f, cy, cx - unit * 0.15f, cy + unit * 0.7f);
                    graphics.DrawLine(pen, cx - unit * 0.15f, cy + unit * 0.7f, cx + unit * 0.9f, cy - unit * 0.75f);
                    break;

                case "dot":
                    graphics.FillEllipse(brush, cx - unit * 0.35f, cy - unit * 0.35f, unit * 0.7f, unit * 0.7f);
                    break;

                case "circle":
                case "ring":
                    graphics.DrawEllipse(pen, cx - unit * 0.9f, cy - unit * 0.9f, unit * 1.8f, unit * 1.8f);
                    break;

                case "square":
                    graphics.DrawRectangle(pen, cx - unit * 0.8f, cy - unit * 0.8f, unit * 1.6f, unit * 1.6f);
                    break;

                case "triangle":
                    graphics.DrawPolygon(pen, new[]
                    {
                        new PointF(cx, cy - unit * 0.9f),
                        new PointF(cx + unit * 0.9f, cy + unit * 0.8f),
                        new PointF(cx - unit * 0.9f, cy + unit * 0.8f)
                    });
                    break;

                case "diamond":
                    graphics.DrawPolygon(pen, new[]
                    {
                        new PointF(cx, cy - unit),
                        new PointF(cx + unit, cy),
                        new PointF(cx, cy + unit),
                        new PointF(cx - unit, cy)
                    });
                    break;

                case "star":
                    graphics.DrawPolygon(pen, StarPoints(cx, cy, unit, unit * 0.45f, 5));
                    break;

                case "heart":
                    using (var path = new GraphicsPath())
                    {
                        path.AddBezier(
                            cx, cy + unit * 0.75f,
                            cx - unit * 1.8f, cy - unit * 0.35f,
                            cx - unit * 0.5f, cy - unit * 1.15f,
                            cx, cy - unit * 0.35f);
                        path.AddBezier(
                            cx, cy + unit * 0.75f,
                            cx + unit * 1.8f, cy - unit * 0.35f,
                            cx + unit * 0.5f, cy - unit * 1.15f,
                            cx, cy - unit * 0.35f);
                        graphics.DrawPath(pen, path);
                    }
                    break;

                case "sun":
                    graphics.DrawEllipse(pen, cx - unit * 0.45f, cy - unit * 0.45f, unit * 0.9f, unit * 0.9f);

                    for (int i = 0; i < 8; i++)
                    {
                        double angle = i * Math.PI / 4;
                        float dx = (float)Math.Cos(angle);
                        float dy = (float)Math.Sin(angle);
                        graphics.DrawLine(
                            pen,
                            cx + dx * unit * 0.7f, cy + dy * unit * 0.7f,
                            cx + dx * unit * 1.05f, cy + dy * unit * 1.05f);
                    }
                    break;

                case "moon":
                    using (var path = new GraphicsPath())
                    {
                        path.AddArc(cx - unit, cy - unit, unit * 2f, unit * 2f, 60, 240);
                        path.AddArc(cx - unit * 0.35f, cy - unit * 1.1f, unit * 1.9f, unit * 2f, 300, -220);
                        path.CloseFigure();
                        graphics.DrawPath(pen, path);
                    }
                    break;

                case "cloud":
                    graphics.DrawArc(pen, cx - unit, cy - unit * 0.25f, unit, unit, 140, 220);
                    graphics.DrawArc(pen, cx - unit * 0.55f, cy - unit * 0.9f, unit * 1.1f, unit * 1.1f, 180, 200);
                    graphics.DrawArc(pen, cx, cy - unit * 0.8f, unit, unit, 220, 160);
                    graphics.DrawLine(pen, cx - unit * 0.95f, cy + unit * 0.45f, cx + unit * 0.85f, cy + unit * 0.45f);
                    break;

                case "battery":
                    graphics.DrawRectangle(pen, cx - unit * 0.95f, cy - unit * 0.5f, unit * 1.7f, unit);
                    graphics.FillRectangle(brush, cx + unit * 0.78f, cy - unit * 0.22f, unit * 0.2f, unit * 0.44f);
                    break;

                case "cpu":
                    graphics.DrawRectangle(pen, cx - unit * 0.65f, cy - unit * 0.65f, unit * 1.3f, unit * 1.3f);
                    graphics.DrawRectangle(pen, cx - unit * 0.25f, cy - unit * 0.25f, unit * 0.5f, unit * 0.5f);

                    for (int i = -1; i <= 1; i++)
                    {
                        graphics.DrawLine(pen, cx + i * unit * 0.45f, cy - unit * 0.65f, cx + i * unit * 0.45f, cy - unit);
                        graphics.DrawLine(pen, cx + i * unit * 0.45f, cy + unit * 0.65f, cx + i * unit * 0.45f, cy + unit);
                    }
                    break;

                case "ram":
                    graphics.DrawRectangle(pen, cx - unit, cy - unit * 0.55f, unit * 2f, unit * 1.1f);

                    for (int i = 0; i < 4; i++)
                    {
                        float x = cx - unit * 0.75f + i * unit * 0.5f;
                        graphics.DrawLine(pen, x, cy - unit * 0.3f, x, cy + unit * 0.3f);
                    }
                    break;

                case "clock":
                    graphics.DrawEllipse(pen, cx - unit * 0.9f, cy - unit * 0.9f, unit * 1.8f, unit * 1.8f);
                    graphics.DrawLine(pen, cx, cy, cx, cy - unit * 0.55f);
                    graphics.DrawLine(pen, cx, cy, cx + unit * 0.45f, cy + unit * 0.2f);
                    break;

                case "calendar":
                    graphics.DrawRectangle(pen, cx - unit * 0.9f, cy - unit * 0.75f, unit * 1.8f, unit * 1.6f);
                    graphics.DrawLine(pen, cx - unit * 0.9f, cy - unit * 0.35f, cx + unit * 0.9f, cy - unit * 0.35f);
                    graphics.DrawLine(pen, cx - unit * 0.5f, cy - unit * 1.0f, cx - unit * 0.5f, cy - unit * 0.6f);
                    graphics.DrawLine(pen, cx + unit * 0.5f, cy - unit * 1.0f, cx + unit * 0.5f, cy - unit * 0.6f);
                    break;

                case "globe":
                    graphics.DrawEllipse(pen, cx - unit * 0.9f, cy - unit * 0.9f, unit * 1.8f, unit * 1.8f);
                    graphics.DrawEllipse(pen, cx - unit * 0.4f, cy - unit * 0.9f, unit * 0.8f, unit * 1.8f);
                    graphics.DrawLine(pen, cx - unit * 0.9f, cy, cx + unit * 0.9f, cy);
                    break;

                case "wifi":
                    graphics.DrawArc(pen, cx - unit, cy - unit * 0.6f, unit * 2f, unit * 1.6f, 200, 140);
                    graphics.DrawArc(pen, cx - unit * 0.65f, cy - unit * 0.25f, unit * 1.3f, unit * 1.1f, 200, 140);
                    graphics.FillEllipse(brush, cx - unit * 0.15f, cy + unit * 0.45f, unit * 0.3f, unit * 0.3f);
                    break;

                case "bell":
                    graphics.DrawArc(pen, cx - unit * 0.7f, cy - unit * 0.85f, unit * 1.4f, unit * 1.5f, 180, 180);
                    graphics.DrawLine(pen, cx - unit * 0.7f, cy + unit * 0.2f, cx - unit * 0.85f, cy + unit * 0.55f);
                    graphics.DrawLine(pen, cx + unit * 0.7f, cy + unit * 0.2f, cx + unit * 0.85f, cy + unit * 0.55f);
                    graphics.DrawLine(pen, cx - unit * 0.85f, cy + unit * 0.55f, cx + unit * 0.85f, cy + unit * 0.55f);
                    break;

                case "pin":
                    graphics.DrawEllipse(pen, cx - unit * 0.5f, cy - unit * 0.95f, unit, unit);
                    graphics.DrawLine(pen, cx, cy - unit * 0.1f, cx, cy + unit);
                    break;

                case "chevron-up":
                case "arrow-up":
                    DrawChevron(graphics, pen, cx, cy, unit, 0);
                    break;

                case "chevron-down":
                case "arrow-down":
                    DrawChevron(graphics, pen, cx, cy, unit, 180);
                    break;

                case "chevron-left":
                case "arrow-left":
                    DrawChevron(graphics, pen, cx, cy, unit, 90);
                    break;

                case "chevron-right":
                case "arrow-right":
                    DrawChevron(graphics, pen, cx, cy, unit, 270);
                    break;
            }
        }

        private static void FillBar(Graphics graphics, Brush brush, float centerX, float centerY, float width, float height)
        {
            graphics.FillRectangle(brush, centerX - width / 2f, centerY - height / 2f, width, height);
        }

        private static void DrawChevron(Graphics graphics, Pen pen, float cx, float cy, float unit, float rotation)
        {
            float x = cx;
            float y = cy - unit * 0.1f;
            float spread = unit * 0.55f;
            float drop = unit * 0.45f;

            var state = graphics.Save();
            graphics.TranslateTransform(x, y);
            graphics.RotateTransform(rotation);

            graphics.DrawLine(pen, -spread, -drop, 0, drop * 0.6f);
            graphics.DrawLine(pen, 0, drop * 0.6f, spread, -drop);

            graphics.Restore(state);
        }

        private static PointF[] StarPoints(float cx, float cy, float outer, float inner, int points)
        {
            var result = new PointF[points * 2];

            for (int i = 0; i < points * 2; i++)
            {
                double angle = -Math.PI / 2 + i * Math.PI / points;
                float radius = i % 2 == 0 ? outer : inner;

                result[i] = new PointF(
                    cx + (float)Math.Cos(angle) * radius,
                    cy + (float)Math.Sin(angle) * radius);
            }

            return result;
        }
    }
}

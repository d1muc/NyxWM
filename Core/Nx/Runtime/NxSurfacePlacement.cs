using System;
using System.Drawing;
using System.Windows.Forms;

namespace nyxWM.Nx
{
    /// <summary>
    /// Where a surface sits on screen and how big it is. Read from the surface
    /// node's own properties, so a document controls its own placement.
    /// </summary>
    internal sealed record NxSurfacePlacement
    {
        /// <summary>top, bottom, left, right, center or float.</summary>
        internal string Position { get; init; } = "float";

        /// <summary>Alignment along the edge: start, center or end.</summary>
        internal string Anchor { get; init; } = "center";

        internal double OffsetX { get; init; }
        internal double OffsetY { get; init; }
        internal double Margin { get; init; } = 8;

        internal NxSize Width { get; init; } = NxSize.Auto;
        internal NxSize Height { get; init; } = NxSize.Auto;

        /// <summary>Monitor index, or -1 for the monitor under the cursor.</summary>
        internal int Monitor { get; init; } = -1;

        internal bool ClickThrough { get; init; }
        internal double Opacity { get; init; } = 1.0;

        /// <summary>Font smoothing and text rendering hint surfaced for the window.</summary>
        internal bool StayOnTop { get; init; } = true;

        internal static NxSurfacePlacement FromNode(NxNode node, NxExpressionEvaluator evaluator)
        {
            var placement = new NxSurfacePlacement();

            foreach (NxProperty property in node.Properties)
            {
                string name = property.Name.ToLowerInvariant();

                placement = name switch
                {
                    "position" => placement with { Position = evaluator.EvaluateText(property.Value, "float") },
                    "anchor" or "align" => placement with { Anchor = evaluator.EvaluateText(property.Value, "center") },
                    "offsetx" => placement with { OffsetX = evaluator.EvaluateNumber(property.Value, 0.0) },
                    "offsety" => placement with { OffsetY = evaluator.EvaluateNumber(property.Value, 0.0) },
                    "margin" => placement with { Margin = evaluator.EvaluateNumber(property.Value, 8.0) },
                    "width" => placement with { Width = NxSize.Parse(property.Value, evaluator) },
                    "height" => placement with { Height = NxSize.Parse(property.Value, evaluator) },
                    "monitor" => placement with { Monitor = (int)evaluator.EvaluateNumber(property.Value, -1.0) },
                    "clickthrough" => placement with { ClickThrough = evaluator.EvaluateFlag(property.Value) },
                    "opacity" => placement with { Opacity = Math.Clamp(evaluator.EvaluateNumber(property.Value, 1.0), 0.05, 1.0) },
                    "ontop" or "topmost" => placement with { StayOnTop = evaluator.EvaluateFlag(property.Value, true) },
                    _ => placement
                };
            }

            return placement;
        }

        /// <summary>Resolves the window rectangle for a given monitor and content size.</summary>
        internal Rectangle Resolve(Rectangle monitor, Size content)
        {
            int margin = (int)Math.Round(Margin);

            int width = (int)Math.Round(Width.Resolve(monitor.Width, content.Width));
            int height = (int)Math.Round(Height.Resolve(monitor.Height, content.Height));

            width = Math.Max(1, Math.Min(width, monitor.Width));
            height = Math.Max(1, Math.Min(height, monitor.Height));

            int x;
            int y;

            switch (Position.ToLowerInvariant())
            {
                case "top":
                    x = monitor.X + Align(monitor.Width, width, Anchor);
                    y = monitor.Y + margin;
                    break;

                case "bottom":
                    x = monitor.X + Align(monitor.Width, width, Anchor);
                    y = monitor.Bottom - height - margin;
                    break;

                case "left":
                    x = monitor.X + margin;
                    y = monitor.Y + Align(monitor.Height, height, Anchor);
                    break;

                case "right":
                    x = monitor.Right - width - margin;
                    y = monitor.Y + Align(monitor.Height, height, Anchor);
                    break;

                case "center":
                    x = monitor.X + Align(monitor.Width, width, Anchor);
                    y = monitor.Y + Align(monitor.Height, height, "center");
                    break;

                default:
                    // float: the document positions itself from the monitor's corner.
                    x = monitor.X + margin;
                    y = monitor.Y + margin;
                    break;
            }

            return new Rectangle(
                x + (int)Math.Round(OffsetX),
                y + (int)Math.Round(OffsetY),
                width,
                height);
        }

        private static int Align(int available, int size, string anchor)
        {
            return anchor.ToLowerInvariant() switch
            {
                "left" or "start" or "top" => 0,
                "right" or "end" or "bottom" => available - size,
                _ => (available - size) / 2
            };
        }
    }

    /// <summary>Monitor lookup that does not depend on a live window.</summary>
    internal static class NxMonitor
    {
        internal static Rectangle ForIndex(int index, Point fallbackPoint)
        {
            Screen[] screens = Screen.AllScreens;

            if (screens.Length == 0)
                return new Rectangle(0, 0, 1920, 1080);

            if (index >= 0 && index < screens.Length)
                return screens[index].Bounds;

            try
            {
                return Screen.FromPoint(fallbackPoint).Bounds;
            }
            catch (Exception)
            {
                return screens[0].Bounds;
            }
        }
    }
}

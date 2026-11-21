using System;
using System.Collections.Generic;

namespace nyxWM.Nx
{
    /// <summary>
    /// Layout and placement options resolved from the theme file's layout tokens.
    /// Controls how the WinForms overlays (Rofi menu, notifications) arrange
    /// their elements, independent of config.jsonc.
    /// </summary>
    internal sealed class NxLayout
    {
        internal static readonly NxLayout Default = new("list", 1, 24f, "", 20f, 10f);

        /// <summary>Menu item arrangement: "list", "grid" or "zigzag".</summary>
        internal string MenuLayout { get; }
        /// <summary>Columns for grid/zigzag layouts.</summary>
        internal int MenuColumns { get; }
        /// <summary>Horizontal shift in DIP for odd rows in zigzag layout.</summary>
        internal float MenuStagger { get; }
        /// <summary>Notification anchor override; empty means use config.jsonc.</summary>
        internal string NotificationPosition { get; }
        internal float NotificationMargin { get; }
        internal float NotificationGap { get; }

        private NxLayout(
            string menuLayout,
            int menuColumns,
            float menuStagger,
            string notificationPosition,
            float notificationMargin,
            float notificationGap)
        {
            MenuLayout = menuLayout;
            MenuColumns = menuColumns;
            MenuStagger = menuStagger;
            NotificationPosition = notificationPosition;
            NotificationMargin = notificationMargin;
            NotificationGap = notificationGap;
        }

        internal int EffectiveColumns => MenuLayout == "list" ? 1 : Math.Clamp(MenuColumns, 1, 6);
        internal bool IsZigzag => MenuLayout == "zigzag";

        internal static NxLayout FromTokens(IReadOnlyDictionary<string, NxValue> tokens)
        {
            string layout = TextToken(tokens, "menu-layout", "list").ToLowerInvariant();
            if (layout is not ("list" or "grid" or "zigzag")) layout = "list";
            int columns = (int)Math.Clamp(NumberToken(tokens, "menu-columns", layout == "list" ? 1 : 2), 1, 6);
            float stagger = Math.Clamp(NumberToken(tokens, "menu-stagger", 24f), 0f, 200f);
            string position = TextToken(tokens, "notification-position", "");
            if (position.Equals("auto", StringComparison.OrdinalIgnoreCase)) position = "";
            float margin = Math.Clamp(NumberToken(tokens, "notification-margin", 20f), 0f, 200f);
            float gap = Math.Clamp(NumberToken(tokens, "notification-gap", 10f), 0f, 100f);
            return new NxLayout(layout, columns, stagger, position, margin, gap);
        }

        private static float NumberToken(
            IReadOnlyDictionary<string, NxValue> tokens,
            string name,
            float fallback)
        {
            if (!tokens.TryGetValue(name, out NxValue? value))
                return fallback;
            if (value.Kind is not (NxValueKind.Number or NxValueKind.Duration))
                return fallback;
            double number = value.Number;
            return double.IsFinite(number) ? (float)number : fallback;
        }

        private static string TextToken(
            IReadOnlyDictionary<string, NxValue> tokens,
            string name,
            string fallback)
        {
            if (!tokens.TryGetValue(name, out NxValue? value) || value.Kind != NxValueKind.Text)
                return fallback;
            return string.IsNullOrWhiteSpace(value.Text) ? fallback : value.Text.Trim();
        }
    }
}

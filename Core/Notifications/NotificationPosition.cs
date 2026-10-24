using System;

namespace nyxWM
{
    /// <summary>
    /// Anchor position of the notification stack on the primary monitor.
    /// </summary>
    public enum NotificationPosition
    {
        TopCenter,
        TopLeft,
        TopRight,
        BottomCenter,
        BottomLeft,
        BottomRight
    }

    /// <summary>
    /// Parses notification position values from config.jsonc.
    /// Accepts "TopCenter", "top-center", "top_center", "topcenter", and so on.
    /// </summary>
    public static class NotificationPositionParser
    {
        public static bool TryParse(string? value, out NotificationPosition position)
        {
            position = NotificationPosition.TopCenter;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            string normalized = Normalize(value);

            switch (normalized)
            {
                case "topcenter":
                case "topmiddle":
                case "center":
                    position = NotificationPosition.TopCenter;
                    return true;
                case "topleft":
                    position = NotificationPosition.TopLeft;
                    return true;
                case "topright":
                    position = NotificationPosition.TopRight;
                    return true;
                case "bottomcenter":
                case "bottommiddle":
                    position = NotificationPosition.BottomCenter;
                    return true;
                case "bottomleft":
                    position = NotificationPosition.BottomLeft;
                    return true;
                case "bottomright":
                    position = NotificationPosition.BottomRight;
                    return true;
                default:
                    return false;
            }
        }

        public static NotificationPosition Parse(string? value)
        {
            return TryParse(value, out NotificationPosition position)
                ? position
                : NotificationPosition.TopCenter;
        }

        private static string Normalize(string value)
        {
            Span<char> buffer = stackalloc char[value.Length];
            int length = 0;

            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c))
                {
                    buffer[length++] = char.ToLowerInvariant(c);
                }
            }

            return new string(buffer[..length]);
        }
    }
}

namespace nyxWM
{
    /// <summary>
    /// Notification configuration from the "Notifications" section of config.jsonc.
    /// </summary>
    public class NotificationSettings
    {
        /// <summary>
        /// Enables or disables all notifications.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Stack anchor: TopCenter (default), TopLeft, TopRight,
        /// BottomCenter, BottomLeft or BottomRight.
        /// </summary>
        public string Position { get; set; } = "TopCenter";

        /// <summary>
        /// How long a notification stays fully visible, in milliseconds.
        /// </summary>
        public int DurationMs { get; set; } = 3200;
    }
}

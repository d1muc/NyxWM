namespace nyxWM
{
    /// <summary>
    /// Terminal page of the settings window: font, cursor, palette, opacity and
    /// shell behaviour. Every value has a working default, so a config without a
    /// Terminal section behaves like the first version of the terminal.
    /// </summary>
    public class TerminalSettings
    {
        /// <summary>
        /// Monospace family used by the console. Falls back through the other
        /// bundled choices when the selected family is not installed.
        /// </summary>
        public string FontFamily { get; set; } = "Cascadia Code";

        /// <summary>Console font size in points, 11-18.</summary>
        public float FontSize { get; set; } = 11f;

        /// <summary>Cursor shape: "Block" (default), "Line" or "Underline".</summary>
        public string CursorStyle { get; set; } = "Block";

        /// <summary>Blinks the cursor while the terminal has focus.</summary>
        public bool CursorBlink { get; set; } = true;

        /// <summary>
        /// Palette preset: "Theme" (default), "Custom Accent", "Dracula",
        /// "Matrix Green", "Cyberpunk Neon", "Monokai" or "Nord".
        /// </summary>
        public string ColorScheme { get; set; } = "Theme";

        /// <summary>
        /// Terminal window opacity from 0.4 (very transparent) to 1.0 (fully
        /// opaque). Independent from the global window opacity.
        /// </summary>
        public float Opacity { get; set; } = 0.98f;

        /// <summary>
        /// Default shell: "zsh" (default), "bash", "powershell" or "cmd".
        /// zsh and bash come from the detected MSYS2 installation.
        /// </summary>
        public string Shell { get; set; } = "zsh";

        /// <summary>Scrolls to the newest output when the shell writes.</summary>
        public bool AutoScroll { get; set; } = true;

        /// <summary>Copies a mouse selection to the clipboard on release.</summary>
        public bool CopyOnSelect { get; set; }
    }
}

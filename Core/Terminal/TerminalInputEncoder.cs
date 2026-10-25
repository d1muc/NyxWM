using System;
using System.Text;
using System.Windows.Forms;

namespace nyxWM
{
    /// <summary>
    /// Turns WinForms key presses into terminal byte sequences.
    ///
    /// Printable characters do not belong here: type them with the control's
    /// <c>KeyPress</c> event and pass <c>e.KeyChar</c> to
    /// <see cref="TerminalSession.WriteInput(string)"/>, which keeps keyboard
    /// layouts, dead keys and Cyrillic input intact. This class covers only the
    /// keys that have no character: navigation, editing, function keys and
    /// modifier combinations.
    /// </summary>
    internal static class TerminalInputEncoder
    {
        /// <summary>
        /// Encodes a non-printable key. Returns an empty array for keys that
        /// produce text, so the caller knows to wait for <c>KeyPress</c>.
        /// </summary>
        internal static byte[] Encode(Keys key, bool shift, bool control, bool alt)
        {
            Keys normalized = key & Keys.KeyCode;

            switch (normalized)
            {
                case Keys.Enter: return Ascii(0x0D);
                case Keys.Escape: return Ascii(0x1B);
                case Keys.Back: return control ? Ascii(0x08) : Ascii(0x7F);
                case Keys.Tab: return control ? EscapeSequence("[Z") : Ascii(0x09);

                case Keys.Up: return Arrow('A', control, alt);
                case Keys.Down: return Arrow('B', control, alt);
                case Keys.Left: return Arrow('D', control, alt);
                case Keys.Right: return Arrow('C', control, alt);

                case Keys.Home: return CursorKey('H', control, alt);
                case Keys.End: return CursorKey('F', control, alt);
                case Keys.Insert: return EscapeSequence(control ? "[2;5~" : "[2~");
                case Keys.Delete: return EscapeSequence(control ? "[3;5~" : "[3~");
                case Keys.PageUp: return EscapeSequence("[5~");
                case Keys.PageDown: return EscapeSequence("[6~");

                case Keys.F1: return EscapeSequence("OP");
                case Keys.F2: return EscapeSequence("OQ");
                case Keys.F3: return EscapeSequence("OR");
                case Keys.F4: return EscapeSequence("OS");
                case Keys.F5: return EscapeSequence("[15~");
                case Keys.F6: return EscapeSequence("[17~");
                case Keys.F7: return EscapeSequence("[18~");
                case Keys.F8: return EscapeSequence("[19~");
                case Keys.F9: return EscapeSequence("[20~");
                case Keys.F10: return EscapeSequence("[21~");
                case Keys.F11: return EscapeSequence("[23~");
                case Keys.F12: return EscapeSequence("[24~");

                default: break;
            }

            int code = (int)normalized;

            // Ctrl+A..Ctrl+Z map onto 0x01..0x1A, which is what readline and zsh
            // bindings expect. Alt+letter sends ESC followed by the letter.
            if (code >= (int)Keys.A && code <= (int)Keys.Z)
            {
                char letter = (char)('a' + (code - (int)Keys.A));

                if (control)
                    return Ascii(letter & 0x1F);

                if (alt)
                    return EscapeSequence(letter.ToString());

                return Array.Empty<byte>();
            }

            return Array.Empty<byte>();
        }

        private static byte[] Arrow(char suffix, bool control, bool alt)
        {
            if (control)
                return EscapeSequence($"[1;5{suffix}");

            if (alt)
                return EscapeSequence($"[1;3{suffix}");

            return EscapeSequence($"[{suffix}");
        }

        private static byte[] CursorKey(char suffix, bool control, bool alt)
        {
            if (control)
                return EscapeSequence($"[1;5{suffix}");

            if (alt)
                return EscapeSequence($"[1;3{suffix}");

            return EscapeSequence($"[{suffix}");
        }

        private static byte[] Ascii(int value) => new[] { (byte)(value & 0xFF) };

        private static byte[] EscapeSequence(string sequence) =>
            Encoding.UTF8.GetBytes("\u001b" + sequence);
    }
}

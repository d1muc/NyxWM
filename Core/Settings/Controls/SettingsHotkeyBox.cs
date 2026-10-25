using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace nyxWM
{
    /// <summary>
    /// Field that records a hotkey when it has focus. The recorded combination
    /// replaces a modifier set equal to MainMod with the "mainmod" token.
    /// </summary>
    internal sealed class SettingsHotkeyBox : Control
    {
        private static readonly string[] ModifierOrder = { "CONTROL", "ALT", "SHIFT", "WIN" };

        private readonly Color _accent;
        private readonly Func<string?>? _mainModProvider;
        private string _value = string.Empty;

        internal SettingsHotkeyBox(Color accent, Func<string?>? mainModProvider)
        {
            _accent = accent;
            _mainModProvider = mainModProvider;

            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.Selectable |
                ControlStyles.ResizeRedraw,
                true);

            BackColor = Color.Transparent;
            Cursor = Cursors.IBeam;
            TabStop = true;
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal string Value
        {
            get => _value;
            set
            {
                _value = value ?? string.Empty;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            float radius = Height * 0.28f;
            var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);

            using (GraphicsPath path = CreateRoundedPath(rect, radius))
            using (var fill = new SolidBrush(SettingsPalette.FieldBackground))
            {
                graphics.FillPath(fill, path);

                using var border = new Pen(
                    Focused ? _accent : SettingsPalette.FieldBorder,
                    Focused ? MathF.Max(1.5f, Height * 0.06f) : 1f);
                graphics.DrawPath(border, path);
            }

            string text = _value;
            Color color = SettingsPalette.TextPrimary;

            if (string.IsNullOrEmpty(text) && Focused)
            {
                text = "Press keys...";
                color = SettingsPalette.TextMuted;
            }

            float padding = MathF.Max(8f, Height * 0.32f);
            using var brush = new SolidBrush(color);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            };

            graphics.DrawString(
                text,
                Font,
                brush,
                new RectangleF(padding, 0f, MathF.Max(1f, Width - padding * 2f), Height),
                format);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button == MouseButtons.Left)
            {
                Focus();
            }
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            Keys key = e.KeyCode;

            if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
                return;

            if (key is Keys.Escape or Keys.Back or Keys.Delete)
            {
                Value = string.Empty;
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            string? keyName = MapKey(key);
            if (keyName == null)
                return;

            Value = FormatHotkey(e.Modifiers, keyName, _mainModProvider?.Invoke());
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        internal static string FormatHotkey(Keys modifiers, string keyName, string? mainMod)
        {
            string combo = ComposeModifiers(modifiers);

            if (!string.IsNullOrWhiteSpace(combo) &&
                !string.IsNullOrWhiteSpace(mainMod) &&
                string.Equals(combo, ComposeModifiers(mainMod), StringComparison.OrdinalIgnoreCase))
            {
                return "mainmod + " + keyName;
            }

            if (string.IsNullOrWhiteSpace(combo))
                return keyName;

            return combo + " + " + keyName;
        }

        private static string ComposeModifiers(Keys modifiers)
        {
            bool control = (modifiers & Keys.Control) == Keys.Control;
            bool alt = (modifiers & Keys.Alt) == Keys.Alt;
            bool shift = (modifiers & Keys.Shift) == Keys.Shift;
            bool win = (modifiers & Keys.LWin) == Keys.LWin;

            var parts = new System.Collections.Generic.List<string>(4);

            if (control)
                parts.Add(ModifierOrder[0]);
            if (alt)
                parts.Add(ModifierOrder[1]);
            if (shift)
                parts.Add(ModifierOrder[2]);
            if (win)
                parts.Add(ModifierOrder[3]);

            return string.Join("+", parts);
        }

        private static string ComposeModifiers(string value)
        {
            var parts = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string raw in value.Split('+', StringSplitOptions.RemoveEmptyEntries))
            {
                string part = raw.Trim();

                if (part.Equals("LWIN", StringComparison.OrdinalIgnoreCase) ||
                    part.Equals("RWIN", StringComparison.OrdinalIgnoreCase))
                {
                    part = "WIN";
                }

                parts.Add(part.ToUpperInvariant());
            }

            var ordered = new System.Collections.Generic.List<string>(4);
            foreach (string modifier in ModifierOrder)
            {
                if (parts.Contains(modifier))
                    ordered.Add(modifier);
            }

            return string.Join("+", ordered);
        }

        private static string? MapKey(Keys key)
        {
            return key switch
            {
                Keys.None => null,
                Keys.Space => "space",
                Keys.Enter => "enter",
                Keys.Escape => "esc",
                Keys.Tab => "tab",
                Keys.Back => "backspace",
                Keys.Delete => "delete",
                Keys.Insert => "insert",
                Keys.Home => "home",
                Keys.End => "end",
                Keys.PageUp => "pageup",
                Keys.PageDown => "pagedown",
                Keys.Left => "left",
                Keys.Up => "up",
                Keys.Right => "right",
                Keys.Down => "down",
                Keys.F1 => "f1",
                Keys.F2 => "f2",
                Keys.F3 => "f3",
                Keys.F4 => "f4",
                Keys.F5 => "f5",
                Keys.F6 => "f6",
                Keys.F7 => "f7",
                Keys.F8 => "f8",
                Keys.F9 => "f9",
                Keys.F10 => "f10",
                Keys.F11 => "f11",
                Keys.F12 => "f12",
                >= Keys.A and <= Keys.Z => key.ToString().ToLowerInvariant(),
                >= Keys.D0 and <= Keys.D9 => ((char)('0' + (key - Keys.D0))).ToString(),
                >= Keys.NumPad0 and <= Keys.NumPad9 => "numpad" + (key - Keys.NumPad0),
                _ => key.ToString().ToLowerInvariant()
            };
        }

        private static GraphicsPath CreateRoundedPath(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            float diameter = Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height));

            if (diameter <= 0f)
            {
                path.AddRectangle(rect);
                return path;
            }

            path.AddArc(rect.Left, rect.Top, diameter, diameter, 180f, 90f);
            path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270f, 90f);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0f, 90f);
            path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90f, 90f);
            path.CloseFigure();
            return path;
        }
    }
}

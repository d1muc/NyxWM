using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using WinFormsTimer = System.Windows.Forms.Timer;

namespace nyxWM
{
    /// <summary>
    /// Field that records a hotkey or mouse button once it is clicked. Clicking
    /// arms the recorder; the first captured combination is committed and the
    /// recorder goes silent again so stray input cannot trigger extra edits.
    /// </summary>
    internal sealed class SettingsHotkeyBox : Control
    {
        private const int WmLeftButtonDown = 0x0201;
        private const int WmRightButtonDown = 0x0204;
        private const int WmMiddleButtonDown = 0x0207;
        private const int WmXButtonDown = 0x020B;

        private static readonly string[] ModifierOrder = { "CONTROL", "ALT", "SHIFT", "WIN" };

        private readonly Color _accent;
        private readonly Func<string?>? _mainModProvider;
        private readonly bool _allowMouse;
        private readonly MouseFilter _filter;
        private readonly WinFormsTimer _modifierTimer;

        private string _value = string.Empty;
        private bool _armed;
        private Keys _accumulatedModifiers;
        private Keys _downModifiers;

        internal SettingsHotkeyBox(Color accent, Func<string?>? mainModProvider, bool allowMouse)
        {
            _accent = accent;
            _mainModProvider = mainModProvider;
            _allowMouse = allowMouse;
            _filter = new MouseFilter(this);

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

            _modifierTimer = new WinFormsTimer { Interval = 450 };
            _modifierTimer.Tick += (_, _) => CommitModifierOnly();
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
                    Focused || _armed ? _accent : SettingsPalette.FieldBorder,
                    Focused || _armed ? MathF.Max(1.5f, Height * 0.06f) : 1f);
                graphics.DrawPath(border, path);
            }

            string text;
            Color color;

            if (_armed)
            {
                string pending = ComposeModifiers(_accumulatedModifiers);
                text = string.IsNullOrEmpty(pending) ? "Press keys..." : pending + "+";
                color = SettingsPalette.TextMuted;
            }
            else
            {
                text = _value;
                color = SettingsPalette.TextPrimary;
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
                Arm();
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

            if (_armed)
                Cancel();

            Invalidate();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return _armed;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (!_armed)
                return;

            Keys key = e.KeyCode;

            if (IsModifierKey(key))
            {
                Keys flag = ToModifierFlag(key);
                _accumulatedModifiers |= flag;
                _downModifiers |= flag;
                _modifierTimer.Stop();
                e.Handled = true;
                e.SuppressKeyPress = true;
                Invalidate();
                return;
            }

            if (key == Keys.Escape)
            {
                Cancel();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            string? keyName = MapKey(key);
            if (keyName == null)
                return;

            Commit(_accumulatedModifiers, keyName);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);

            if (!_armed)
                return;

            Keys key = e.KeyCode;

            if (!IsModifierKey(key))
                return;

            _downModifiers &= ~ToModifierFlag(key);

            if (_downModifiers == Keys.None && _accumulatedModifiers != Keys.None)
            {
                _modifierTimer.Stop();
                _modifierTimer.Start();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Disarm(excludeFilter: false);
                _modifierTimer.Dispose();
            }

            base.Dispose(disposing);
        }

        private void Arm()
        {
            _armed = true;
            _accumulatedModifiers = Keys.None;
            _downModifiers = Keys.None;
            _modifierTimer.Stop();

            if (_allowMouse)
            {
                Application.AddMessageFilter(_filter);
            }

            Focus();
            Invalidate();
        }

        private void Commit(Keys modifiers, string keyName)
        {
            if (!_armed)
                return;

            string combo = FormatHotkey(modifiers, keyName, _mainModProvider?.Invoke());
            _value = combo;
            Finish();
        }

        private void CommitModifierOnly()
        {
            if (!_armed)
                return;

            string combo = ComposeModifiers(_accumulatedModifiers);
            _value = string.IsNullOrEmpty(combo) ? string.Empty : combo;
            Finish();
        }

        private void CommitMouse(MouseButtons button)
        {
            if (!_armed)
                return;

            string? name = button switch
            {
                MouseButtons.Left => "LEFT",
                MouseButtons.Right => "RIGHT",
                MouseButtons.Middle => "MBUTTON",
                MouseButtons.XButton1 => "XBUTTON1",
                MouseButtons.XButton2 => "XBUTTON2",
                _ => null
            };

            if (name == null)
                return;

            string mods = ComposeModifiers(Control.ModifierKeys);

            if (!string.IsNullOrEmpty(mods) &&
                !string.IsNullOrWhiteSpace(_mainModProvider?.Invoke()) &&
                string.Equals(mods, ComposeModifiers(_mainModProvider!.Invoke()!), StringComparison.OrdinalIgnoreCase))
            {
                _value = "mainmod+" + name;
            }
            else if (string.IsNullOrEmpty(mods))
            {
                _value = name;
            }
            else
            {
                _value = mods + "+" + name;
            }

            Finish();
        }

        private void Cancel()
        {
            if (!_armed)
                return;

            Disarm(excludeFilter: false);
            Invalidate();
        }

        private void Finish()
        {
            Disarm(excludeFilter: false);
            Invalidate();
        }

        private void Disarm(bool excludeFilter)
        {
            _armed = false;
            _accumulatedModifiers = Keys.None;
            _downModifiers = Keys.None;
            _modifierTimer.Stop();

            if (!excludeFilter && _allowMouse)
            {
                Application.RemoveMessageFilter(_filter);
            }
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
            bool win = (modifiers & (Keys.LWin | Keys.RWin)) != Keys.None;

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

        private static bool IsModifierKey(Keys key)
        {
            return key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin;
        }

        private static Keys ToModifierFlag(Keys key)
        {
            return key switch
            {
                Keys.ControlKey => Keys.Control,
                Keys.ShiftKey => Keys.Shift,
                Keys.Menu => Keys.Alt,
                Keys.LWin or Keys.RWin => Keys.LWin,
                _ => Keys.None
            };
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

        private sealed class MouseFilter : IMessageFilter
        {
            private readonly SettingsHotkeyBox _owner;

            internal MouseFilter(SettingsHotkeyBox owner)
            {
                _owner = owner;
            }

            public bool PreFilterMessage(ref Message m)
            {
                if (!_owner._armed)
                    return false;

                switch (m.Msg)
                {
                    case WmLeftButtonDown:
                        _owner.CommitMouse(MouseButtons.Left);
                        return true;
                    case WmRightButtonDown:
                        _owner.CommitMouse(MouseButtons.Right);
                        return true;
                    case WmMiddleButtonDown:
                        _owner.CommitMouse(MouseButtons.Middle);
                        return true;
                    case WmXButtonDown:
                        int button = (int)(m.WParam.ToInt64() >> 16) & 0xFFFF;
                        _owner.CommitMouse(button == 2 ? MouseButtons.XButton2 : MouseButtons.XButton1);
                        return true;
                    default:
                        return false;
                }
            }
        }
    }
}

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace nyxWM
{
    internal sealed class RofiMenuWindow : Form
    {
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        private const int WM_CHAR = 0x0102;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int WM_MOUSELEAVE = 0x02A3;
        private const int WM_MOUSEACTIVATE = 0x0021;
        private const int WM_SETCURSOR = 0x0020;
        private const int WM_NCCALCSIZE = 0x0083;
        private const int WM_NCPAINT = 0x0085;

        private const int DwmwaWindowCornerPreference = 33;
        private const int DwmcpDoNotRound = 1;

        private const int MA_NOACTIVATE = 3;
        private const int VK_LBUTTON = 0x01;
        private const int VK_RBUTTON = 0x02;
        private const int VK_MBUTTON = 0x04;

        private const float BaseWidthDip = 540f;
        private const float BaseHeightDip = 560f;
        private const float CornerRadiusDip = 18f;
        private const float PaddingDip = 18f;
        private const float SearchHeightDip = 50f;
        private const float GapDip = 10f;
        private const float FooterHeightDip = 52f;

        private const float EntryDurationMs = 360f;
        private const float ExitDurationMs = 250f;
        private const float EntryStartScale = 0.90f;
        private const float ExitEndScale = 0.91f;
        private const float EntrySlideDip = 12f;
        private const float ExitSlideDip = 6f;

        private readonly NotificationTheme _theme;
        private readonly float _scale;

        private readonly int _baseWidth;
        private readonly int _baseHeight;
        private readonly int _baseX;
        private readonly int _baseY;

        private readonly Font _searchFont;
        private readonly Font _badgeFont;
        private readonly RofiAppList _list;
        private Action? _unsubscribe;
        private int _frameInFlight;
        private readonly Color _fieldColor;
        private readonly bool _glass;
        private readonly bool _animations;
        private readonly float _cornerRadiusDip;

        private RectangleF _searchRect;
        private RectangleF _footerRect;

        // Search state
        private string _searchText = "";
        private int _caretIndex;
        private int _selectionLength;

        private Bitmap? _backdrop;
        private float _elapsedMs;
        private float _closeElapsedMs;
        private bool _closing;
        private float _visualScale = 1f;
        private float _slideOffset;

        // Layered-window rendering
        private Bitmap? _buffer;
        private int _renderStamp = -1;
        private float _opacity = 1f;

        internal RofiMenuWindow(NotificationTheme theme, float scale)
        {
            _theme = theme ?? throw new ArgumentNullException(nameof(theme));
            _scale = Math.Clamp(scale, 0.75f, 3f);

            Rectangle work = Screen.PrimaryScreen?.WorkingArea
                             ?? new Rectangle(0, 0, 1920,1080);

            _baseWidth = Math.Max(1, (int)MathF.Round(MathF.Min(
                BaseWidthDip * _scale,
                work.Width - 40f * _scale)));
            _baseHeight = Math.Max(1, (int)MathF.Round(MathF.Min(
                BaseHeightDip * _scale,
                work.Height - 60f * _scale)));

            _baseX = work.Left + (work.Width - _baseWidth) / 2;
            _baseY = work.Top + (work.Height - _baseHeight) / 2;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = false;
            Text = "Rofi menu";

            _glass = VisualPreferences.GlassEnabled;
            _animations = VisualPreferences.SmoothAnimations;
            _cornerRadiusDip = VisualPreferences.CornerRadius(CornerRadiusDip);

            _backdrop = _glass ? CaptureBackdrop() : null;

            _opacity = _animations ? 0f : 1f;

            if (_animations)
                _visualScale = EntryStartScale;

            _fieldColor = _theme.FieldBackground;

            _searchFont = VisualPreferences.CreateUiFont("Segoe UI", 11f, FontStyle.Regular);
            _badgeFont = VisualPreferences.CreateUiFont("Segoe UI", 7.75f, FontStyle.Regular);

            Size = new Size(_baseWidth, _baseHeight);
            Location = new Point(_baseX, _baseY);

            _list = new RofiAppList(_theme, _scale);
            _list.ItemActivated += OnItemActivated;
            _list.PinsChanged += () => ApplyFilter(preserveSelection: true);
            _list.Invalidated += () => ApplyLayered();

            ApplyFilter();
            ApplyScaledLayout(_visualScale, _animations ? EntrySlideDip * _scale : 0f);
        }

        internal bool IsClosing => _closing;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | LayeredWindow.WS_EX_LAYERED;
                return parameters;
            }
        }

        protected override bool ShowWithoutActivation => true;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                try
                {
                    int preference = DwmcpDoNotRound;
                    DwmSetWindowAttribute(Handle, DwmwaWindowCornerPreference, ref preference, sizeof(int));
                }
                catch { }
            }

            ApplyLayered();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (IsHandleCreated)
                ApplyLayered();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            ApplyLayered();
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_NCPAINT:
                    return;

                case WM_NCCALCSIZE when m.WParam != IntPtr.Zero:
                    m.Result = IntPtr.Zero;
                    return;

                case WM_CHAR:
                    HandleChar((char)m.WParam);
                    return;

                case WM_KEYDOWN:
                case WM_SYSKEYDOWN:
                    HandleKeyDown((Keys)m.WParam, m.LParam);
                    return;

                case WM_MOUSEMOVE:
                    HandleMouseMove(GetMouseX(m.LParam), GetMouseY(m.LParam));
                    return;

                case WM_LBUTTONDOWN:
                    HandleMouseDown(GetMouseX(m.LParam), GetMouseY(m.LParam), MouseButtons.Left);
                    return;

                case WM_LBUTTONUP:
                    HandleMouseUp(GetMouseX(m.LParam), GetMouseY(m.LParam), MouseButtons.Left);
                    return;

                case WM_RBUTTONDOWN:
                    HandleMouseDown(GetMouseX(m.LParam), GetMouseY(m.LParam), MouseButtons.Right);
                    return;

                case WM_RBUTTONUP:
                    HandleMouseUp(GetMouseX(m.LParam), GetMouseY(m.LParam), MouseButtons.Right);
                    return;

                case WM_MOUSEWHEEL:
                    HandleMouseWheel(m);
                    return;

                case WM_MOUSELEAVE:
                    if (_list.ClearHover())
                        ApplyLayered();
                    return;

                case WM_MOUSEACTIVATE:
                    m.Result = (IntPtr)MA_NOACTIVATE;
                    return;

                case WM_SETCURSOR:
                    {
                        Point client = PointToClient(Cursor.Position);
                        if (_list.HitTest(client.Y) >= 0)
                        {
                            Cursor.Current = Cursors.Hand;
                            return;
                        }
                    }
                    break;
            }

            base.WndProc(ref m);
        }

        private static int GetMouseX(IntPtr lParam) => (short)(lParam.ToInt32() & 0xFFFF);
        private static int GetMouseY(IntPtr lParam) => (short)((lParam.ToInt32() >> 16) & 0xFFFF);

        private void HandleChar(char c)
        {
            if (c <' ' && c != '\t')
                return;

            if (_selectionLength >0)
                DeleteSelection();

            _searchText = _searchText.Insert(_caretIndex, c.ToString());
            _caretIndex++;
            ApplyFilter();
        }

        private void HandleKeyDown(Keys key, IntPtr lParam)
        {
            bool ctrl = (ModifierKeys & Keys.Control) != 0;
            bool shift = (ModifierKeys & Keys.Shift) != 0;

            switch (key)
            {
                case Keys.Back:
                    if (_selectionLength >0)
                        DeleteSelection();
                    else if (_caretIndex >0)
                    {
                        _caretIndex--;
                        _searchText = _searchText.Remove(_caretIndex, 1);
                    }
                    ApplyFilter();
                    return;

                case Keys.Delete:
                    if (_selectionLength >0)
                        DeleteSelection();
                    else if (_caretIndex < _searchText.Length)
                        _searchText = _searchText.Remove(_caretIndex, 1);
                    ApplyFilter();
                    return;

                case Keys.Left:
                    if (ctrl)
                        _caretIndex = PrevWordBoundary(_caretIndex);
                    else if (_caretIndex >0)
                        _caretIndex--;
                    if (!shift) _selectionLength =0;
                    ApplyLayered();
                    return;

                case Keys.Right:
                    if (ctrl)
                        _caretIndex = NextWordBoundary(_caretIndex);
                    else if (_caretIndex < _searchText.Length)
                        _caretIndex++;
                    if (!shift) _selectionLength =0;
                    ApplyLayered();
                    return;

                case Keys.Home:
                    _caretIndex =0;
                    if (!shift) _selectionLength =0;
                    ApplyLayered();
                    return;

                case Keys.End:
                    _caretIndex = _searchText.Length;
                    if (!shift) _selectionLength =0;
                    ApplyLayered();
                    return;

                case Keys.A when ctrl:
                    _caretIndex =0;
                    _selectionLength = _searchText.Length;
                    ApplyLayered();
                    return;

                case Keys.C when ctrl:
                    if (_selectionLength >0)
                        Clipboard.SetText(GetSelectedText());
                    return;

                case Keys.V when ctrl:
                    string? pasteText = Clipboard.GetText();
                    if (!string.IsNullOrEmpty(pasteText))
                    {
                        if (_selectionLength >0) DeleteSelection();
                        _searchText = _searchText.Insert(_caretIndex, pasteText);
                        _caretIndex += pasteText.Length;
                        ApplyFilter();
                    }
                    return;

                case Keys.X when ctrl:
                    if (_selectionLength >0)
                    {
                        Clipboard.SetText(GetSelectedText());
                        DeleteSelection();
                        ApplyFilter();
                    }
                    return;

                case Keys.Down:
                    _list.MoveSelection(1);
                    return;

                case Keys.Up:
                    _list.MoveSelection(-1);
                    return;

                case Keys.PageDown:
                    _list.MoveSelection(_list.VisibleRows());
                    return;

                case Keys.PageUp:
                    _list.MoveSelection(-_list.VisibleRows());
                    return;

                case Keys.Tab:
                    _list.MoveSelection(shift ? -1 :1);
                    return;

                case Keys.Enter:
                    if (_list.HasSelection)
                        _list.ActivateSelected(ctrl);
                    return;

                case Keys.Escape:
                    BeginClose();
                    return;
            }
        }

        private void DeleteSelection()
        {
            if (_selectionLength ==0) return;
            int start = Math.Min(_caretIndex, _caretIndex + _selectionLength);
            int len = Math.Abs(_selectionLength);
            _searchText = _searchText.Remove(start, len);
            _caretIndex = start;
            _selectionLength =0;
        }

        private string GetSelectedText()
        {
            int start = Math.Min(_caretIndex, _caretIndex + _selectionLength);
            int len = Math.Abs(_selectionLength);
            return _searchText.Substring(start, len);
        }

        private int PrevWordBoundary(int from)
        {
            int i = Math.Min(from -1, _searchText.Length -1);
            while (i >=0 && _searchText[i] ==' ') i--;
            while (i >=0 && _searchText[i] !=' ') i--;
            return Math.Max(0, i +1);
        }

        private int NextWordBoundary(int from)
        {
            int i = from;
            while (i < _searchText.Length && _searchText[i] !=' ') i++;
            while (i < _searchText.Length && _searchText[i] ==' ') i++;
            return Math.Min(_searchText.Length, i);
        }

        private void HandleMouseMove(int x, int y)
        {
            if (IsOutsideListArea(x, y))
            {
                if (_list.ClearHover()) ApplyLayered();
                return;
            }

            int listY = MapToListY(y);
            if (_list.SetHover(listY)) ApplyLayered();
        }

        private void HandleMouseDown(int x, int y, MouseButtons button)
        {
            if (_closing) return;

            if (!IsOutsideListArea(x, y) && _list.HitTest(MapToListY(y)) >=0)
            {
                _list.HandleMouseDown(MapToListY(y), button, PointToScreen(new Point(x, y)));
                return;
            }

            if (IsOutsideCard(x, y))
                BeginClose();
        }

        private void HandleMouseUp(int x, int y, MouseButtons button)
        {
            if (_closing) return;
            if (!IsOutsideListArea(x, y))
                _list.HandleMouseUp(MapToListY(y), button);
        }

        private void HandleMouseWheel(Message m)
        {
            if (_closing) return;
            int delta = (short)((ulong)m.WParam >>16);
            Point client = PointToClient(Cursor.Position);
            _list.HandleMouseWheel(delta, client.Y);
        }

        private bool IsOutsideCard(int x, int y)
        {
            float cw = Width * _visualScale;
            float ch = Height * _visualScale;
            float cl = (Width - cw) / 2f;
            float ct = (Height - ch) / 2f + _slideOffset;
            return x < cl || x > cl + cw || y < ct || y > ct + ch;
        }

        private bool IsOutsideListArea(int x, int y)
        {
            float listTop = _searchRect.Bottom + GapDip * _scale * _visualScale;
            float listBottom = _footerRect.Top;
            float listLeft = _searchRect.Left;
            float listRight = _searchRect.Right;
            return x < listLeft || x > listRight || y < listTop || y >= listBottom;
        }

        private int MapToListY(int y)
        {
            float listTop = _searchRect.Bottom + GapDip * _scale * _visualScale;
            return (int)(y - listTop);
        }

        private void ApplyFilter(bool preserveSelection = false)
        {
            _list.SetItems(
                RofiSearch.Filter(RofiAppCatalog.GetApps(), _searchText),
                preserveSelection);
            ApplyLayered();
        }

        private void OnItemActivated(RofiApp app, bool asAdministrator)
        {
            if (_closing) return;
            TryLaunch(app, asAdministrator);
            BeginClose();
        }

        // ---- Layered rendering ----

        private void EnsureBufferSize()
        {
            if (Width <=0 || Height <=0) return;

            if (_buffer != null && _buffer.Width == Width && _buffer.Height == Height)
                return;

            Bitmap? prev = _buffer;
            _buffer = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
            prev?.Dispose();
            _renderStamp = -1;
        }

        private int RenderStamp()
        {
            int h = _scale.GetHashCode();
            h = h *31 + _visualScale.GetHashCode();
            h = h *31 + _slideOffset.GetHashCode();
            h = h *31 + _searchText.GetHashCode();
            h = h *31 + _caretIndex.GetHashCode();
            h = h *31 + _theme.IsLight.GetHashCode();
            h = h *31 + _list.SelectedIndex.GetHashCode();
            h = h *31 + _list.HasActiveAnimation.GetHashCode();
            h = h *31 + _list.RenderVersion;
            h = h *31 + (_backdrop != null).GetHashCode();
            return h;
        }

        private void RenderIfNeeded()
        {
            if (_buffer == null) return;
            int stamp = RenderStamp();
            if (stamp == _renderStamp) return;
            _renderStamp = stamp;
            using Graphics g = Graphics.FromImage(_buffer);
            Render(g);
        }

        private void Render(Graphics g)
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            DrawGlass(g);
            DrawCardContent(g);
        }

        private void ApplyLayered()
        {
            if (IsDisposed || !IsHandleCreated) return;
            EnsureBufferSize();
            RenderIfNeeded();
            if (_buffer == null) return;
            LayeredWindow.Apply(Handle, _buffer, Left, Top, _opacity);
        }

        // ---- Glass / backdrop ----

        private void DrawGlass(Graphics g)
        {
            var fullRect = new RectangleF(0f, 0f, Width, Height);
            float cw = Width * _visualScale;
            float ch = Height * _visualScale;
            float cl = (Width - cw) / 2f;
            float ct = (Height - ch) / 2f + _slideOffset;
            var cardRect = new RectangleF(cl, ct, cw, ch);
            float radius = _cornerRadiusDip * _scale;

            using GraphicsPath cardPath = CreateRoundedPath(cardRect, radius);

            if (_backdrop != null)
            {
                GraphicsState state = g.Save();
                g.SetClip(cardPath);
                g.DrawImage(_backdrop, new Rectangle(0, 0, Width, Height));
                g.Restore(state);

                Color tintColor = _theme.IsLight
                    ? Color.FromArgb(226, _theme.BackgroundTop)
                    : Color.FromArgb(214, _theme.BackgroundTop);

                using (var tint = new SolidBrush(tintColor))
                    g.FillPath(tint, cardPath);

                Color shadeBottom = _theme.IsLight
                    ? Color.FromArgb(18, 0, 0, 0)
                    : Color.FromArgb(40, 0, 0, 0);

                using var shade = new LinearGradientBrush(
                    fullRect,
                    Color.FromArgb(0, 0, 0, 0),
                    shadeBottom,
                    LinearGradientMode.Vertical);
                g.FillPath(shade, cardPath);
            }
            else
            {
                using var fill = new LinearGradientBrush(
                    fullRect,
                    _theme.BackgroundTop,
                    _theme.BackgroundBottom,
                    LinearGradientMode.Vertical);

                using GraphicsPath fullPath = CreateRoundedPath(fullRect, 0f);
                g.FillPath(fill, fullPath);
            }
        }

        // ---- Card content ----

        private void DrawCardContent(Graphics g)
        {
            float cw = Width * _visualScale;
            float ch = Height * _visualScale;
            float cl = (Width - cw) / 2f;
            float ct = (Height - ch) / 2f + _slideOffset;

            float radius = _cornerRadiusDip * _scale;
            float bw = MathF.Max(1f, _scale);

            using GraphicsPath cardPath = CreateRoundedPath(
                new RectangleF(cl + bw / 2f, ct + bw / 2f, cw - bw, ch - bw),
                radius);

            float pad = PaddingDip * _scale * _visualScale;
            float sh = SearchHeightDip * _scale * _visualScale;
            float fh = FooterHeightDip * _scale * _visualScale;
            float gap = GapDip * _scale * _visualScale;

            var sr = new RectangleF(cl + pad, ct + pad, MathF.Max(1f, cw - pad * 2f), sh);
            float ft = ct + ch - pad - fh;
            var fr = new RectangleF(cl + pad, ft, MathF.Max(1f, cw - pad * 2f), fh);

            _searchRect = sr;
            _footerRect = fr;

            DrawSearchField(g, sr);
            DrawList(g, sr, ft, gap);
            DrawFooter(g, fr);

            Color bc = _theme.IsLight
                ? Color.FromArgb(28, 20, 22, 28)
                : Color.FromArgb(20, 255, 255, 255);

            using var border = new Pen(bc, bw);
            g.DrawPath(border, cardPath);
        }

        private void DrawSearchField(Graphics g, RectangleF sr)
        {
            bool hasText = _searchText.Length >0;

            float radius = 12f * _scale * _visualScale;
            using (GraphicsPath fp = CreateRoundedPath(sr, radius))
            {
                Color fill = _theme.IsLight
                    ? Color.FromArgb(150, 255, 255, 255)
                    : Color.FromArgb(18, 255, 255, 255);

                using (var b = new SolidBrush(fill))
                    g.FillPath(b, fp);

                using var ab = new Pen(
                    Color.FromArgb(hasText ? 110 :64, _theme.Accent),
                    MathF.Max(1f,1.1f * _scale));
                g.DrawPath(ab, fp);
            }

            float ir = 7.5f * _scale * _visualScale;
            float cx = sr.Left + 22f * _scale * _visualScale;
            float cy = sr.Top + sr.Height /2f;

            Color ic = hasText ? _theme.Accent : _theme.TextMuted;

            using var pen = new Pen(ic, MathF.Max(1.2f,1.6f * _scale))
            { StartCap = LineCap.Round, EndCap = LineCap.Round };

            g.DrawEllipse(pen, cx - ir, cy - ir, ir * 2f, ir *2f);

            const float diag = 0.70710678f;
            g.DrawLine(pen,
                cx + ir * diag, cy + ir * diag,
                cx + ir * diag + ir * 0.85f, cy + ir * diag + ir *0.85f);

            float tl = sr.Left + 44f * _scale * _visualScale;
            float tt = sr.Top + 3f * _scale * _visualScale;
            var tr = new RectangleF(tl, tt, MathF.Max(1f, sr.Right - tl -10f * _scale), sr.Height -6f * _scale * _visualScale);

            if (hasText)
                DrawSearchText(g, tr);
            else
                DrawPlaceholder(g, tr);
        }

        private void DrawPlaceholder(Graphics g, RectangleF tr)
        {
            using var pb = new SolidBrush(_theme.TextMuted);
            using var pf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
            g.DrawString("Search apps or type command...", _searchFont, pb, tr, pf);
        }

        private void DrawSearchText(Graphics g, RectangleF tr)
        {
            using var tb = new SolidBrush(_theme.TextPrimary);
            using var fmt = new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.None,
                FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces
            };

            if (_caretIndex >0)
            {
                string before = _searchText.Substring(0, _caretIndex);
                g.DrawString(before, _searchFont, tb, tr, fmt);
            }

            string beforeCaret = _searchText.Substring(0, _caretIndex);
            SizeF bs = g.MeasureString(beforeCaret, _searchFont, (int)tr.Width, fmt);

            float cx = tr.Left + bs.Width;
            float ch = MathF.Min(tr.Height * 0.6f, 22f * _scale * _visualScale);
            float cy = tr.Top + (tr.Height - ch) /2f;

            using var cp = new Pen(_theme.Accent, MathF.Max(1f,1.2f * _scale));
            g.DrawLine(cp, cx, cy, cx, cy + ch);

            if (_caretIndex < _searchText.Length)
            {
                string after = _searchText.Substring(_caretIndex);
                var ar = new RectangleF(cx +2f, tr.Top, MathF.Max(1f, tr.Right - cx -4f), tr.Height);
                g.DrawString(after, _searchFont, tb, ar, fmt);
            }
        }

        private void DrawList(Graphics g, RectangleF sr, float footerTop, float gap)
        {
            float listTop = sr.Bottom + gap;
            float listH = MathF.Max(1f, footerTop - listTop - gap);

            _list.SetBounds(MathF.Max(1f, Width * _visualScale - PaddingDip * _scale * _visualScale * 2f), listH);

            GraphicsState state = g.Save();
            g.TranslateTransform(sr.Left, listTop);
            _list.Draw(g);
            g.Restore(state);
        }

        private void DrawFooter(Graphics g, RectangleF fr)
        {
            float by = fr.Top + (FooterHeightDip -20f) * 0.5f * _scale * _visualScale;

            DrawBadgeRow(g,
                new[] { ("↑↓", "Navigate"), ("↵", "Open"), ("ESC", "Close") },
                by);
        }

        private void DrawBadgeRow(Graphics g, (string Key, string Label)[] items, float y)
        {
            float px = 7f * _scale * _visualScale;
            float bh = 20f * _scale * _visualScale;
            float kg = 7f * _scale * _visualScale;
            float ig = 18f * _scale * _visualScale;

            var kw = new float[items.Length];
            var lw = new float[items.Length];
            float total = ig * (items.Length -1);

            for (int i =0; i < items.Length; i++)
            {
                kw[i] = g.MeasureString(items[i].Key, _badgeFont).Width + px *2f;
                lw[i] = g.MeasureString(items[i].Label, _badgeFont).Width;
                total += kw[i] + kg + lw[i];
            }

            float x = _footerRect.Left + (_footerRect.Width - total) /2f;

            using var kb = new SolidBrush(_theme.TextPrimary);
            using var lb = new SolidBrush(_theme.TextMuted);
            using var kf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using var lf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };

            for (int i =0; i < items.Length; i++)
            {
                var badge = new RectangleF(x, y, kw[i], bh);

                Color bf = _theme.IsLight
                    ? Color.FromArgb(10, 0, 0, 0)
                    : Color.FromArgb(16, 255, 255, 255);
                Color bb = _theme.IsLight
                    ? Color.FromArgb(26, 0, 0, 0)
                    : Color.FromArgb(26, 255, 255, 255);

                using (GraphicsPath path = CreateRoundedPath(badge, 6f * _scale * _visualScale))
                using (var fill = new SolidBrush(bf))
                {
                    g.FillPath(fill, path);
                    using var border = new Pen(bb, MathF.Max(1f, _scale));
                    g.DrawPath(border, path);
                }

                g.DrawString(items[i].Key, _badgeFont, kb, badge, kf);
                x += kw[i] + kg;
                var lr = new RectangleF(x, y, lw[i], bh);
                g.DrawString(items[i].Label, _badgeFont, lb, lr, lf);
                x += lw[i] + ig;
            }
        }

        // ---- Layout ----

        private void ApplyScaledLayout(float contentScale, float slide)
        {
            _visualScale = contentScale;
            _slideOffset = slide;

            float cw = Width * contentScale;
            float ch = Height * contentScale;
            float cl = (Width - cw) /2f;
            float ct = (Height - ch) /2f + slide;

            float s = _scale * contentScale;
            float pad = PaddingDip * s;
            float sh = SearchHeightDip * s;
            float fh = FooterHeightDip * s;

            _searchRect = new RectangleF(cl + pad, ct + pad, MathF.Max(1f, cw - pad *2f), sh);

            float ft = ct + ch - pad - fh;
            _footerRect = new RectangleF(cl + pad, ft, MathF.Max(1f, cw - pad *2f), fh);

            float listTop = _searchRect.Bottom + GapDip * s;
            float listH = MathF.Max(1f, ft - listTop - GapDip * s);
            _list.SetBounds(MathF.Max(1f, cw - pad *2f), listH);
        }

        // ---- Animation ----

        private RenderTicker.TickResult AdvanceFrame(double dtMs)
        {
            if (Interlocked.Exchange(ref _frameInFlight,1) ==1)
                return RenderTicker.TickResult.Active;

            try
            {
                Advance((float)dtMs);

                if (_closing || _elapsedMs < EntryDurationMs)
                    return RenderTicker.TickResult.Active;

                return _list.AdvanceFrame(dtMs);
            }
            finally
            {
                Interlocked.Exchange(ref _frameInFlight, 0);
            }
        }

        private void Advance(float dtMs)
        {
            _elapsedMs += dtMs;

            float entryProgress = Math.Clamp(_elapsedMs / EntryDurationMs, 0f, 1f);
            float easedEntry = EaseOutCubic(entryProgress);

            float closeProgress =0f;
            if (_closing)
            {
                _closeElapsedMs += dtMs;
                closeProgress = Math.Clamp(_closeElapsedMs / ExitDurationMs, 0f,1f);
            }

            float easedClose = EaseInCubic(closeProgress);

            float cs = _animations
                ? EntryStartScale + (1f - EntryStartScale) * easedEntry
                : 1f;
            cs *= 1f - (1f - ExitEndScale) * easedClose;

            float slide = _animations
                ? (1f - easedEntry) * EntrySlideDip * _scale
                  + easedClose * ExitSlideDip * _scale
                : 0f;

            float opacity = _animations
                ? Math.Clamp(easedEntry * (1f - easedClose), 0f,1f)
                : 1f;

            if (_closing || entryProgress < 1f)
                UiAnimationHost.Post(() => ApplyFrame(cs, slide, opacity));

            if (_closing && closeProgress >=1f)
            {
                Unsubscribe();
                Close();
            }
        }

        private void ApplyFrame(float contentScale, float slide, float opacity)
        {
            if (IsDisposed) return;
            _opacity = opacity;
            ApplyScaledLayout(contentScale, slide);
            _list.FlushFrame();
            ApplyLayered();
        }

        private void RepaintIfAnimated()
        {
            if (IsDisposed) return;
            if (!_closing && _elapsedMs >= EntryDurationMs && !_list.HasActiveAnimation)
                return;
            ApplyLayered();
        }

        // ---- Show / close ----

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            UiAnimationHost.RequestActivity();

            if (!_animations)
            {
                _elapsedMs = EntryDurationMs;
                _opacity =1f;
            }

            _unsubscribe = UiAnimationHost.Subscribe(
                dtMs => AdvanceFrame(dtMs),
                () => RepaintIfAnimated());

            SetForegroundWindow(Handle);
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (!_closing) ApplyLayered();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);

            if (!_closing && _elapsedMs >140f && !_list.IsPinMenuOpen && IsMouseButtonDown())
                BeginClose();
        }

        private static bool IsMouseButtonDown()
        {
            return (GetAsyncKeyState(VK_LBUTTON) & 0x8000) !=0
                   || (GetAsyncKeyState(VK_RBUTTON) & 0x8000) !=0
                   || (GetAsyncKeyState(VK_MBUTTON) & 0x8000) !=0;
        }

        internal void RequestClose()
        {
            BeginClose();
        }

        private void BeginClose()
        {
            if (_closing) return;

            if (!_animations)
            {
                Unsubscribe();
                Close();
                return;
            }

            _closing = true;
            _closeElapsedMs =0f;
        }

        // ---- App launch ----

        private static void TryLaunch(RofiApp app, bool asAdministrator)
        {
            if (string.IsNullOrWhiteSpace(app.Path))
                return;

            try
            {
                ProcessStartInfo si = app.Kind == RofiAppKind.Uwp
                    ? CreateUwpStartInfo(app)
                    : CreateDesktopStartInfo(app, asAdministrator);

                Process.Start(si);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                Console.WriteLine($"Rofi menu: elevation cancelled for '{app.Name}'.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Rofi menu: failed to launch '{app.Name}': {ex.Message}");
            }
        }

        private static ProcessStartInfo CreateDesktopStartInfo(RofiApp app, bool asAdministrator)
        {
            var si = new ProcessStartInfo { FileName = app.Path, UseShellExecute = true };

            if (!string.IsNullOrWhiteSpace(app.Arguments))
                si.Arguments = app.Arguments;

            if (!string.IsNullOrWhiteSpace(app.WorkingDirectory))
                si.WorkingDirectory = app.WorkingDirectory;

            if (asAdministrator)
                si.Verb = "runas";

            return si;
        }

        private static ProcessStartInfo CreateUwpStartInfo(RofiApp app)
        {
            return new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"shell:AppsFolder\\{app.Path}",
                UseShellExecute = true
            };
        }

        // ---- Backdrop capture ----

        private Bitmap? CaptureBackdrop()
        {
            try
            {
                var source = new Bitmap(_baseWidth, _baseHeight);

                using (Graphics g = Graphics.FromImage(source))
                {
                    g.CopyFromScreen(_baseX, _baseY, 0, 0,
                        new Size(_baseWidth, _baseHeight),
                        CopyPixelOperation.SourceCopy);
                }

                return Blur(source);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Rofi menu: backdrop capture failed: {ex.Message}");
                return null;
            }
        }

        private static Bitmap Blur(Bitmap source)
        {
            try
            {
                int sw = Math.Max(1, source.Width /9);
                int sh = Math.Max(1, source.Height /9);

                using var small = new Bitmap(sw, sh);

                using (Graphics g = Graphics.FromImage(small))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(source,0,0, sw, sh);
                }

                var result = new Bitmap(source.Width, source.Height);

                using (Graphics g = Graphics.FromImage(result))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(small,0,0, source.Width, source.Height);
                }

                return result;
            }
            finally
            {
                source.Dispose();
            }
        }

        // ---- Cleanup ----

        private void Unsubscribe()
        {
            Action? u = Interlocked.Exchange(ref _unsubscribe, null);
            _unsubscribe = null;

            try { u?.Invoke(); }
            catch (Exception ex)
            {
                Console.WriteLine($"RofiMenuWindow: unsubscribe failed: {ex.Message}");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Unsubscribe();
                _searchFont.Dispose();
                _badgeFont.Dispose();
                _backdrop?.Dispose();
                _backdrop = null;
                _buffer?.Dispose();
                _buffer = null;
                _list.Dispose();
            }

            base.Dispose(disposing);
        }

        // ---- Math helpers ----

        private static float EaseOutCubic(float progress)
        {
            float inv = 1f - Math.Clamp(progress, 0f,1f);
            return 1f - inv * inv * inv;
        }

        private static float EaseInCubic(float progress)
        {
            float c = Math.Clamp(progress, 0f,1f);
            return c * c * c;
        }

        private static GraphicsPath CreateRoundedPath(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radius *2f, Math.Min(rect.Width, rect.Height));

            if (d <=0f)
            {
                path.AddRectangle(rect);
                return path;
            }

            path.AddArc(rect.Left, rect.Top, d, d, 180f, 90f);
            path.AddArc(rect.Right - d, rect.Top, d, d, 270f, 90f);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0f, 90f);
            path.AddArc(rect.Left, rect.Bottom - d, d, d, 90f, 90f);
            path.CloseFigure();
            return path;
        }
    }
}
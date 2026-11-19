using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace nyxWM.Nx
{
    /// <summary>
    /// A window whose entire content is one .nx widget tree.
    ///
    /// The window never activates and never appears in the taskbar: it is a
    /// surface for widgets, not an application window. All layout and painting
    /// happens on the UI thread; animation state arrives from the frame clock.
    /// </summary>
    internal sealed class NxSurfaceWindow : Form
    {
        private const int WsExToolWindow = 0x00000080;
        private const int WsExNoActivate = 0x08000000;
        private const int WmMouseActivate = 0x0021;
        private const int MaNoActivate = 3;

        private readonly NxContext _context;
        private readonly NxSurfacePlacement _placement;

        private NxWidget _root;
        private NxWidget? _hovered;
        private NxWidget? _pressed;

        internal NxSurfaceWindow(
            string surfaceName,
            NxWidget root,
            NxContext context,
            NxSurfacePlacement placement)
        {
            SurfaceName = surfaceName;
            _root = root;
            _context = context;
            _placement = placement;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = placement.StayOnTop;
            Text = string.Empty;
            BackColor = Color.Black;
            DoubleBuffered = true;
            Opacity = placement.Opacity;

            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
        }

        internal string SurfaceName { get; }

        internal NxSurfacePlacement Placement => _placement;

        /// <summary>Swaps in a rebuilt tree, which is how hot reload applies.</summary>
        internal void SetRoot(NxWidget root) => _root = root;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= WsExToolWindow | WsExNoActivate;

                // CreateParams is queried once from the base constructor, before the
                // constructor below assigns the field, so it is read defensively.
                if (_placement is { ClickThrough: true })
                {
                    // A click-through surface is a pure display: input falls to the
                    // window underneath it.
                    parameters.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT
                }

                return parameters;
            }
        }

        protected override bool ShowWithoutActivation => true;

        /// <summary>Keeps focus where it was when a widget is clicked.</summary>
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmMouseActivate)
            {
                message.Result = (IntPtr)MaNoActivate;
                return;
            }

            base.WndProc(ref message);
        }

        /// <summary>Recomputes layout and repaints. Called once per frame on the UI thread.</summary>
        internal void Frame()
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            _root.Update(_context);
            ApplyLayout();
            Invalidate();
        }

        /// <summary>Positions the window from its placement and its measured content.</summary>
        internal void ApplyPlacement(Rectangle monitor)
        {
            if (IsDisposed)
                return;

            SizeF content = _root.Measure(_context, new SizeF(monitor.Width, monitor.Height));

            Rectangle target = _placement.Resolve(
                monitor,
                new Size(Math.Max(1, (int)Math.Ceiling(content.Width)), Math.Max(1, (int)Math.Ceiling(content.Height))));

            if (Bounds != target)
                Bounds = target;
        }

        private void ApplyLayout()
        {
            var box = new RectangleF(0, 0, ClientSize.Width, ClientSize.Height);
            _root.Arrange(_context, box);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            graphics.Clear(BackColor);
            PaintTree(graphics, _root);

            base.OnPaint(e);
        }

        private void PaintTree(Graphics graphics, NxWidget widget)
        {
            widget.Paint(graphics, _context);

            if (widget is NxPanel panel)
            {
                // Children of an invisible panel stay invisible, which is what makes
                // "visible: {expr}" a cheap way to hide a whole group.
                if (!panel.AnimatedStyle.Visible)
                    return;

                foreach (NxWidget child in panel.Children)
                    PaintTree(graphics, child);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            NxWidget? hit = _root.HitTest(e.Location);

            if (ReferenceEquals(hit, _hovered))
                return;

            if (_hovered != null)
                _hovered.Hovered = false;

            _hovered = hit;

            if (_hovered != null)
                _hovered.Hovered = true;

            Cursor = hit is NxButton ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);

            if (_hovered == null)
                return;

            _hovered.Hovered = false;
            _hovered = null;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            _pressed = _root.HitTest(e.Location);

            if (_pressed != null)
                _pressed.Pressed = true;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            NxWidget? target = _pressed;

            if (target != null)
                target.Pressed = false;

            _pressed = null;

            if (target is not NxButton button)
                return;

            // Releasing outside the button cancels the click, like any button.
            if (!button.Bounds.Contains(e.Location))
                return;

            _context.Runtime?.RunAction(button.Action);
        }
    }
}

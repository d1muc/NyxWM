using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NyxInstaller.Ui.Controls;

/// <summary>
/// Dark rounded text field. Hosts a borderless <see cref="TextBox"/> inside a custom painted frame.
/// </summary>
public sealed class DarkTextBox : Control
{
    private const int EmSetCueBanner = 0x1501;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, string lParam);

    private readonly TextBox _input;
    private string _placeholder = string.Empty;

    public DarkTextBox()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = Theme.Surface;
        Font = Fonts.Body;
        Size = new Size(240, 38);
        Cursor = Cursors.IBeam;

        _input = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            Font = Fonts.Body,
            TabStop = true
        };

        Controls.Add(_input);
        _input.HandleCreated += (_, _) => ApplyPlaceholder();
        _input.TextChanged += (_, _) => OnTextChanged(EventArgs.Empty);
        _input.GotFocus += (_, _) => Invalidate();
        _input.LostFocus += (_, _) => Invalidate();

        LayoutInput();
    }

    [AllowNull]
    public override string Text
    {
        get => _input.Text;
        set => _input.Text = value ?? string.Empty;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Placeholder
    {
        get => _placeholder;
        set
        {
            _placeholder = value ?? string.Empty;
            ApplyPlaceholder();
        }
    }

    public void SelectAll() => _input.SelectAll();

    public new void Focus() => _input.Focus();

    private int S(int value) => Theme.S(this, value);

    private void ApplyPlaceholder()
    {
        if (_input.IsHandleCreated)
            SendMessage(_input.Handle, EmSetCueBanner, (IntPtr)1, _placeholder);
    }

    private void LayoutInput()
    {
        if (_input is null)
            return;

        var padding = S(12);
        var height = _input.PreferredHeight;
        var top = Math.Max(0, (Height - height) / 2);
        _input.SetBounds(padding, top, Math.Max(0, Width - padding * 2), height);
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        if (_input is null)
            return;

        _input.Font = Font;
        LayoutInput();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (_input is null)
            return;

        LayoutInput();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        _input.Focus();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Theme.Background);

        var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        var radius = S(10);

        Drawing.FillRounded(graphics, rect, radius, Theme.Surface);
        Drawing.StrokeRounded(graphics, rect, radius, _input.Focused ? Theme.Accent : Theme.Border);
    }
}

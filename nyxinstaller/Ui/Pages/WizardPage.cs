using System.Drawing;
using System.Windows.Forms;
using NyxInstaller.Localization;

namespace NyxInstaller.Ui.Pages;

/// <summary>
/// Base class of the wizard content pages. Pages own their controls and lay them out manually
/// so the dark design stays pixel-accurate at any DPI.
/// </summary>
public abstract class WizardPage : UserControl
{
    private readonly Label _error;

    protected WizardPage(Localizer localizer)
    {
        Localizer = localizer;

        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = Theme.Background;

        _error = new Label
        {
            AutoSize = true,
            Font = Fonts.Small,
            ForeColor = Theme.Error,
            BackColor = Theme.Background,
            Visible = false
        };
        Controls.Add(_error);
    }

    protected Localizer Localizer { get; }

    public abstract string TitleKey { get; }

    public abstract string SubtitleKey { get; }

    public abstract void Localize();

    /// <summary>
    /// Re-runs the manual layout. Call after localization or when the page is shown.
    /// </summary>
    public void Relayout() => LayoutPage();

    public virtual bool Validate(out string? error)
    {
        error = null;
        return true;
    }

    public virtual void OnEnter() => ClearError();

    protected int S(int value) => Theme.S(this, value);

    protected void ShowError(string? message)
    {
        _error.Text = message ?? string.Empty;
        _error.Visible = !string.IsNullOrEmpty(message);
        LayoutError();
    }

    protected void ClearError()
    {
        _error.Text = string.Empty;
        _error.Visible = false;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutPage();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Theme.Background);
        base.OnPaint(e);
    }

    protected virtual void LayoutPage() => LayoutError();

    private void LayoutError()
    {
        var y = Math.Max(0, Height - _error.PreferredHeight - S(2));
        _error.MaximumSize = new Size(Math.Max(0, Width), 0);
        _error.Location = new Point(0, y);
    }
}

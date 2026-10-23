using System.Drawing;
using System.Windows.Forms;
using NyxInstaller.App;
using NyxInstaller.Localization;
using NyxInstaller.Ui.Controls;

namespace NyxInstaller.Ui.Pages;

/// <summary>
/// Second page: install root. The actual target is always <c>&lt;root&gt;\nyxWM</c>.
/// </summary>
public sealed class PathPage : WizardPage
{
    private readonly Label _label;
    private readonly DarkTextBox _pathBox;
    private readonly GhostButton _browse;
    private readonly CardPanel _previewCard;
    private readonly Label _previewCaption;
    private readonly Label _previewPath;
    private readonly Label _hint;

    public PathPage(Localizer localizer, string defaultRoot)
        : base(localizer)
    {
        _label = new Label
        {
            AutoSize = true,
            Font = Fonts.Small,
            ForeColor = Theme.Muted,
            BackColor = Theme.Background
        };

        _pathBox = new DarkTextBox
        {
            Text = defaultRoot
        };
        _pathBox.TextChanged += (_, _) => UpdatePreview();

        _browse = new GhostButton();
        _browse.Click += (_, _) => Browse();

        _previewCard = new CardPanel();
        _previewCaption = new Label
        {
            AutoSize = true,
            Font = Fonts.Small,
            ForeColor = Theme.Muted,
            BackColor = Theme.Surface
        };
        _previewPath = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            Font = Fonts.BodyStrong,
            ForeColor = Theme.Accent,
            BackColor = Theme.Surface
        };

        _previewCard.Controls.Add(_previewCaption);
        _previewCard.Controls.Add(_previewPath);

        _hint = new Label
        {
            AutoSize = false,
            Font = Fonts.Small,
            ForeColor = Theme.Dim,
            BackColor = Theme.Background
        };

        Controls.Add(_label);
        Controls.Add(_pathBox);
        Controls.Add(_browse);
        Controls.Add(_previewCard);
        Controls.Add(_hint);
    }

    public override string TitleKey => "step.path.title";

    public override string SubtitleKey => "step.path.desc";

    public string InstallRoot => _pathBox.Text.Trim();

    public string TargetPath => System.IO.Path.Combine(InstallRoot, InstallerDefaults.InstallFolderName);

    public override void Localize()
    {
        _label.Text = Localizer.Get("path.label");
        _browse.Text = Localizer.Get("common.browse");
        _previewCaption.Text = Localizer.Get("path.preview");
        _hint.Text = Localizer.Get("path.hint");
        _pathBox.Placeholder = Localizer.Get("path.ask");
        UpdatePreview();
        Relayout();
    }

    public override bool Validate(out string? error)
    {
        error = null;
        var root = InstallRoot;

        if (string.IsNullOrWhiteSpace(root))
        {
            error = Localizer.Get("path.invalid");
            return false;
        }

        try
        {
            _ = System.IO.Path.GetFullPath(root);
        }
        catch (Exception)
        {
            error = Localizer.Get("path.invalid");
            return false;
        }

        return true;
    }

    private void UpdatePreview()
    {
        var root = InstallRoot;
        _previewPath.Text = string.IsNullOrWhiteSpace(root)
            ? "-"
            : System.IO.Path.Combine(root, InstallerDefaults.InstallFolderName);
    }

    private void Browse()
    {
        using var dialog = new FolderBrowserDialog
        {
            ShowNewFolderButton = true,
            UseDescriptionForTitle = true,
            Description = Localizer.Get("path.ask"),
            SelectedPath = System.IO.Directory.Exists(InstallRoot)
                ? InstallRoot
                : InstallerDefaults.DefaultInstallRoot
        };

        if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
            _pathBox.Text = dialog.SelectedPath;
    }

    protected override void LayoutPage()
    {
        base.LayoutPage();

        var labelHeight = S(20);
        var top = S(2);

        _label.SetBounds(0, top, Width, labelHeight);
        _pathBox.SetBounds(0, top + labelHeight + S(6), Width - S(144), S(38));
        _browse.SetBounds(Width - S(132), top + labelHeight + S(6), S(132), S(38));

        var cardTop = _pathBox.Bottom + S(22);
        _previewCard.SetBounds(0, cardTop, Width, S(78));

        _previewCaption.Location = new Point(S(20), S(16));
        _previewPath.SetBounds(S(20), S(38), Width - S(40), S(24));

        _hint.SetBounds(0, cardTop + S(78) + S(14), Width, S(40));
    }
}

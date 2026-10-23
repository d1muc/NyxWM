using System.Drawing;
using System.Windows.Forms;
using NyxInstaller.Localization;
using NyxInstaller.Ui.Controls;

namespace NyxInstaller.Ui.Pages;

/// <summary>
/// Sixth page: completion summary.
/// </summary>
public sealed class FinishPage : WizardPage
{
    private readonly CheckBadge _badge;
    private readonly Label _heading;
    private readonly Label _subtitle;
    private readonly CardPanel _card;
    private readonly Label _pathCaption;
    private readonly Label _pathValue;
    private readonly Label _autostartCaption;
    private readonly Label _autostartValue;
    private readonly Label _shortcutCaption;
    private readonly Label _shortcutValue;
    private readonly Label _versionCaption;
    private readonly Label _versionValue;
    private readonly Label _note;

    public FinishPage(Localizer localizer)
        : base(localizer)
    {
        _badge = new CheckBadge();
        _heading = new Label
        {
            AutoSize = false,
            Font = Fonts.Heading,
            ForeColor = Theme.Text,
            BackColor = Theme.Background,
            TextAlign = ContentAlignment.MiddleCenter
        };
        _subtitle = new Label
        {
            AutoSize = false,
            Font = Fonts.Body,
            ForeColor = Theme.Muted,
            BackColor = Theme.Background,
            TextAlign = ContentAlignment.MiddleCenter
        };

        _card = new CardPanel();
        _pathCaption = MakeCaption();
        _pathValue = MakeValue();
        _autostartCaption = MakeCaption();
        _autostartValue = MakeValue();
        _shortcutCaption = MakeCaption();
        _shortcutValue = MakeValue();
        _versionCaption = MakeCaption();
        _versionValue = MakeValue();

        _card.Controls.Add(_pathCaption);
        _card.Controls.Add(_pathValue);
        _card.Controls.Add(_autostartCaption);
        _card.Controls.Add(_autostartValue);
        _card.Controls.Add(_shortcutCaption);
        _card.Controls.Add(_shortcutValue);
        _card.Controls.Add(_versionCaption);
        _card.Controls.Add(_versionValue);

        _note = new Label
        {
            AutoSize = false,
            Font = Fonts.Small,
            ForeColor = Theme.Dim,
            BackColor = Theme.Background,
            TextAlign = ContentAlignment.MiddleCenter
        };

        Controls.Add(_badge);
        Controls.Add(_heading);
        Controls.Add(_subtitle);
        Controls.Add(_card);
        Controls.Add(_note);
    }

    public override string TitleKey => "step.finish.title";

    public override string SubtitleKey => "step.finish.desc";

    public void SetSummary(string installPath, bool autostart, bool shortcut, string version)
    {
        _pathValue.Text = installPath;
        _autostartValue.Text = Localizer.Get(autostart ? "common.yes" : "common.no");
        _shortcutValue.Text = Localizer.Get(shortcut ? "common.yes" : "common.no");
        _versionValue.Text = version;
    }

    public override void Localize()
    {
        _heading.Text = Localizer.Get("finish.heading");
        _subtitle.Text = Localizer.Get("finish.subtitle");
        _pathCaption.Text = Localizer.Get("finish.path");
        _autostartCaption.Text = Localizer.Get("finish.autostart");
        _shortcutCaption.Text = Localizer.Get("finish.shortcut");
        _versionCaption.Text = Localizer.Get("finish.version");
        _note.Text = Localizer.Get("finish.simulation");
        Relayout();
    }

    public override void OnEnter()
    {
        base.OnEnter();
        _badge.Play();
    }

    private static Label MakeCaption() => new()
    {
        AutoSize = false,
        Font = Fonts.Small,
        ForeColor = Theme.Muted,
        BackColor = Theme.Surface,
        TextAlign = ContentAlignment.MiddleLeft
    };

    private static Label MakeValue() => new()
    {
        AutoSize = false,
        AutoEllipsis = true,
        Font = Fonts.Body,
        ForeColor = Theme.Text,
        BackColor = Theme.Surface,
        TextAlign = ContentAlignment.MiddleLeft
    };

    protected override void LayoutPage()
    {
        base.LayoutPage();

        var width = Math.Min(S(560), Width);
        var left = Math.Max(0, (Width - width) / 2);

        _badge.Size = new Size(S(72), S(72));
        _badge.Location = new Point((Width - _badge.Width) / 2, S(8));

        _heading.SetBounds(left, S(92), width, S(32));
        _subtitle.SetBounds(left, S(126), width, S(24));

        var cardTop = S(168);
        var cardHeight = S(154);
        _card.SetBounds(left, cardTop, width, cardHeight);

        var rowTop = S(14);
        var rowStep = S(32);
        var captionWidth = S(170);

        var captions = new[] { _pathCaption, _autostartCaption, _shortcutCaption, _versionCaption };
        var values = new[] { _pathValue, _autostartValue, _shortcutValue, _versionValue };

        for (var i = 0; i < captions.Length; i++)
        {
            var top = rowTop + i * rowStep;
            captions[i].SetBounds(S(22), top, captionWidth, S(24));
            values[i].SetBounds(S(22) + captionWidth, top, width - captionWidth - S(44), S(24));
        }

        _note.SetBounds(left, cardTop + cardHeight + S(14), width, S(36));
    }
}

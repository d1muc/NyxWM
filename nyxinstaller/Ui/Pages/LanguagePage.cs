using System.Drawing;
using System.Windows.Forms;
using NyxInstaller.Localization;
using NyxInstaller.Models;
using NyxInstaller.Ui.Controls;

namespace NyxInstaller.Ui.Pages;

/// <summary>
/// First page: installer language.
/// </summary>
public sealed class LanguagePage : WizardPage
{
    private readonly SelectableCard _english;
    private readonly SelectableCard _russian;

    public LanguagePage(Localizer localizer)
        : base(localizer)
    {
        _english = new SelectableCard
        {
            Title = "English",
            Badge = "EN"
        };
        _russian = new SelectableCard
        {
            Title = "Русский",
            Badge = "RU"
        };

        _english.Click += (_, _) => Select(InstallerLanguage.English, true);
        _russian.Click += (_, _) => Select(InstallerLanguage.Russian, true);

        Controls.Add(_english);
        Controls.Add(_russian);
    }

    public event EventHandler<InstallerLanguage>? LanguageChanged;

    public override string TitleKey => "step.language.title";

    public override string SubtitleKey => "step.language.desc";

    public InstallerLanguage SelectedLanguage { get; private set; } = InstallerLanguage.English;

    public void Select(InstallerLanguage language, bool raiseEvent)
    {
        SelectedLanguage = language;
        _english.Selected = language == InstallerLanguage.English;
        _russian.Selected = language == InstallerLanguage.Russian;

        if (raiseEvent)
            LanguageChanged?.Invoke(this, language);
    }

    public override void Localize()
    {
        _english.Subtitle = Localizer.Get("language.en.subtitle");
        _russian.Subtitle = Localizer.Get("language.ru.subtitle");
        Relayout();
    }

    protected override void LayoutPage()
    {
        base.LayoutPage();

        var cardWidth = Math.Min(S(300), (Width - S(24)) / 2);
        var cardHeight = S(136);
        var gap = S(24);

        var totalWidth = cardWidth * 2 + gap;
        var left = Math.Max(0, (Width - totalWidth) / 2);
        var top = Math.Max(S(24), (Height - cardHeight) / 2 - S(12));

        _english.SetBounds(left, top, cardWidth, cardHeight);
        _russian.SetBounds(left + cardWidth + gap, top, cardWidth, cardHeight);
    }
}

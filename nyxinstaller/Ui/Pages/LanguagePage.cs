using System.Drawing;
using System.Windows.Forms;
using NyxInstaller.Localization;
using NyxInstaller.Models;
using NyxInstaller.Ui.Controls;

namespace NyxInstaller.Ui.Pages;

/// <summary>
/// First page: installer language. Shows the ten catalog languages in a 5×2 grid.
/// </summary>
public sealed class LanguagePage : WizardPage
{
    private const int Columns = 5;

    private readonly List<(LanguageInfo Info, SelectableCard Card)> _cards = new();

    public LanguagePage(Localizer localizer)
        : base(localizer)
    {
        foreach (var info in LanguageCatalog.All)
        {
            var card = new SelectableCard
            {
                Title = info.NativeName,
                Badge = info.Badge
            };

            var language = info.Language;
            card.Click += (_, _) => Select(language, true);

            _cards.Add((info, card));
            Controls.Add(card);
        }
    }

    public event EventHandler<InstallerLanguage>? LanguageChanged;

    public override string TitleKey => "step.language.title";

    public override string SubtitleKey => "step.language.desc";

    public InstallerLanguage SelectedLanguage { get; private set; } = InstallerLanguage.English;

    public void Select(InstallerLanguage language, bool raiseEvent)
    {
        SelectedLanguage = language;

        foreach (var (info, card) in _cards)
            card.Selected = info.Language == language;

        if (raiseEvent)
            LanguageChanged?.Invoke(this, language);
    }

    public override void Localize()
    {
        foreach (var (info, card) in _cards)
            card.Subtitle = Localizer.Get($"language.{info.Code}.subtitle");

        Relayout();
    }

    protected override void LayoutPage()
    {
        base.LayoutPage();

        if (_cards.Count == 0 || Width <= 0 || Height <= 0)
            return;

        var rows = (_cards.Count + Columns - 1) / Columns;
        var gap = S(14);
        var cardWidth = Math.Max(S(120), (Width - gap * (Columns - 1)) / Columns);
        var cardHeight = S(120);

        var gridWidth = cardWidth * Columns + gap * (Columns - 1);
        var gridHeight = cardHeight * rows + gap * (rows - 1);

        var left = Math.Max(0, (Width - gridWidth) / 2);
        var top = Math.Max(S(12), (Height - gridHeight) / 2 - S(10));

        for (var i = 0; i < _cards.Count; i++)
        {
            var column = i % Columns;
            var row = i / Columns;

            _cards[i].Card.SetBounds(
                left + column * (cardWidth + gap),
                top + row * (cardHeight + gap),
                cardWidth,
                cardHeight);
        }
    }
}

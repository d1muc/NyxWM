using NyxInstaller.Localization;
using NyxInstaller.Ui.Controls;

namespace NyxInstaller.Ui.Pages;

/// <summary>
/// Third page: autostart and Start Menu shortcut.
/// </summary>
public sealed class OptionsPage : WizardPage
{
    private readonly OptionCard _autostart;
    private readonly OptionCard _shortcut;

    public OptionsPage(Localizer localizer)
        : base(localizer)
    {
        _autostart = new OptionCard { Checked = true };
        _shortcut = new OptionCard { Checked = true };

        Controls.Add(_autostart);
        Controls.Add(_shortcut);
    }

    public override string TitleKey => "step.options.title";

    public override string SubtitleKey => "step.options.desc";

    public bool RegisterAutostart => _autostart.Checked;

    public bool CreateShortcut => _shortcut.Checked;

    public override void Localize()
    {
        _autostart.Title = Localizer.Get("options.autostart");
        _autostart.Description = Localizer.Get("options.autostart.desc");
        _shortcut.Title = Localizer.Get("options.shortcut");
        _shortcut.Description = Localizer.Get("options.shortcut.desc");
        Relayout();
    }

    protected override void LayoutPage()
    {
        base.LayoutPage();

        var top = S(2);
        var height = S(88);
        var gap = S(14);

        _autostart.SetBounds(0, top, Width, height);
        _shortcut.SetBounds(0, top + height + gap, Width, height);
    }
}

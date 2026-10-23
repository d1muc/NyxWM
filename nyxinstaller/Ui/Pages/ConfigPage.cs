using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using NyxInstaller.Localization;
using NyxInstaller.Models;
using NyxInstaller.Ui.Controls;

namespace NyxInstaller.Ui.Pages;

/// <summary>
/// Fourth page: camera settings and hotkeys.
/// </summary>
public sealed class ConfigPage : WizardPage
{
    private const string CustomCameraValue = "__custom__";

    private readonly ConfigOptions _defaults;

    private readonly Label _cameraSection;
    private readonly Label _sensitivityCaption;
    private readonly DarkTextBox _sensitivityBox;
    private readonly Label _cameraCaption;
    private readonly ChipGroup _cameraChips;
    private readonly DarkTextBox _customCameraBox;
    private readonly Label _autoCenterCaption;
    private readonly ToggleSwitch _autoCenterToggle;
    private readonly Label _mainModCaption;
    private readonly ChipGroup _mainModChips;

    private readonly Label _hotkeySection;
    private readonly Label[] _hotkeyCaptions;
    private readonly DarkTextBox[] _hotkeyBoxes;
    private readonly Label _hotkeyHint;

    private string _customCamera = string.Empty;

    public ConfigPage(Localizer localizer, ConfigOptions defaults)
        : base(localizer)
    {
        _defaults = defaults;
        _customCamera = defaults.CameraMoveButton;

        _cameraSection = MakeSection();
        _sensitivityCaption = MakeCaption();
        _sensitivityBox = new DarkTextBox { Text = defaults.CameraSensitivity.ToString(CultureInfo.InvariantCulture) };

        _cameraCaption = MakeCaption();
        _cameraChips = new ChipGroup();
        _cameraChips.SelectionChanged += (_, value) => OnCameraChanged(value);

        _customCameraBox = new DarkTextBox { Visible = false };

        _autoCenterCaption = MakeCaption();
        _autoCenterToggle = new ToggleSwitch { Checked = defaults.AutoCenterOnFocusDefault };

        _mainModCaption = MakeCaption();
        _mainModChips = new ChipGroup();
        _mainModChips.SelectionChanged += (_, _) => ClearError();

        _hotkeySection = MakeSection();
        _hotkeyCaptions = new Label[5];
        _hotkeyBoxes = new DarkTextBox[5];

        var hotkeyDefaults = new[]
        {
            defaults.Hotkeys.ResetCamera,
            defaults.Hotkeys.ToggleAutoCenter,
            defaults.Hotkeys.CloseWindow,
            defaults.Hotkeys.CenterActiveWindow,
            defaults.Hotkeys.ToggleGameMode
        };

        for (var i = 0; i < _hotkeyBoxes.Length; i++)
        {
            _hotkeyCaptions[i] = MakeCaption();
            _hotkeyBoxes[i] = new DarkTextBox { Text = hotkeyDefaults[i] };
            Controls.Add(_hotkeyCaptions[i]);
            Controls.Add(_hotkeyBoxes[i]);
        }

        _hotkeyHint = new Label
        {
            AutoSize = false,
            Font = Fonts.Small,
            ForeColor = Theme.Dim,
            BackColor = Theme.Background
        };

        Controls.Add(_cameraSection);
        Controls.Add(_sensitivityCaption);
        Controls.Add(_sensitivityBox);
        Controls.Add(_cameraCaption);
        Controls.Add(_cameraChips);
        Controls.Add(_customCameraBox);
        Controls.Add(_autoCenterCaption);
        Controls.Add(_autoCenterToggle);
        Controls.Add(_mainModCaption);
        Controls.Add(_mainModChips);
        Controls.Add(_hotkeySection);
        Controls.Add(_hotkeyHint);
    }

    public override string TitleKey => "step.config.title";

    public override string SubtitleKey => "step.config.desc";

    public override void Localize()
    {
        _cameraSection.Text = Localizer.Get("config.section.camera");
        _sensitivityCaption.Text = Localizer.Get("config.sensitivity");
        _cameraCaption.Text = Localizer.Get("config.cameraButton");
        _autoCenterCaption.Text = Localizer.Get("config.autoCenter");
        _mainModCaption.Text = Localizer.Get("config.mainMod");
        _hotkeySection.Text = Localizer.Get("config.section.hotkeys");
        _hotkeyHint.Text = Localizer.Get("config.hotkeysHint");
        _customCameraBox.Placeholder = Localizer.Get("config.cameraCustomPrompt");
        _sensitivityBox.Placeholder = string.Empty;

        var hotkeyNames = new[]
        {
            "hotkey.reset",
            "hotkey.autocenter",
            "hotkey.close",
            "hotkey.center",
            "hotkey.gamemode"
        };

        for (var i = 0; i < _hotkeyCaptions.Length; i++)
            _hotkeyCaptions[i].Text = Localizer.Get(hotkeyNames[i]);

        var selected = _cameraChips.Selected;
        if (string.IsNullOrEmpty(selected))
            selected = _defaults.CameraMoveButton;

        RebuildCameraChips(selected);
        RebuildMainModChips(_mainModChips.Selected.Length == 0
            ? _defaults.MainMod
            : _mainModChips.Selected);

        UpdateCustomVisibility();
        Relayout();
    }

    public override bool Validate(out string? error)
    {
        error = null;

        if (!TryParsePositive(_sensitivityBox.Text, out _))
        {
            error = Localizer.Get("config.positive");
            ShowError(error);
            return false;
        }

        if (IsCustomCamera() && string.IsNullOrWhiteSpace(_customCameraBox.Text))
        {
            error = Localizer.Get("config.empty");
            ShowError(error);
            return false;
        }

        foreach (var box in _hotkeyBoxes)
        {
            if (string.IsNullOrWhiteSpace(box.Text))
            {
                error = Localizer.Get("config.empty");
                ShowError(error);
                return false;
            }
        }

        ClearError();
        return true;
    }

    public ConfigOptions Collect()
    {
        var sensitivity = TryParsePositive(_sensitivityBox.Text, out var parsed)
            ? parsed
            : _defaults.CameraSensitivity;

        var camera = IsCustomCamera()
            ? _customCameraBox.Text.Trim()
            : _cameraChips.Selected;

        return new ConfigOptions(
            sensitivity,
            camera,
            _autoCenterToggle.Checked,
            _mainModChips.Selected,
            new HotkeyOptions(
                _hotkeyBoxes[0].Text.Trim(),
                _hotkeyBoxes[1].Text.Trim(),
                _hotkeyBoxes[2].Text.Trim(),
                _hotkeyBoxes[3].Text.Trim(),
                _hotkeyBoxes[4].Text.Trim()));
    }

    private bool IsCustomCamera() =>
        string.Equals(_cameraChips.Selected, CustomCameraValue, StringComparison.Ordinal);

    private void OnCameraChanged(string value)
    {
        if (!IsCustomCamera() && !string.IsNullOrWhiteSpace(value))
            _customCamera = value;

        UpdateCustomVisibility();
        ClearError();
        Relayout();
    }

    private void UpdateCustomVisibility()
    {
        var custom = IsCustomCamera();
        if (custom && string.IsNullOrWhiteSpace(_customCameraBox.Text))
            _customCameraBox.Text = _customCamera;

        _customCameraBox.Visible = custom;
    }

    private void RebuildCameraChips(string selected)
    {
        var options = new List<(string Value, string Label)>
        {
            (_defaults.CameraMoveButton, _defaults.CameraMoveButton),
            ("RIGHT", "RIGHT"),
            ("LEFT", "LEFT"),
            ("ALT+LEFT", "ALT+LEFT"),
            ("ALT+RIGHT", "ALT+RIGHT"),
            (CustomCameraValue, Localizer.Get("config.cameraCustom"))
        };

        _cameraChips.SetOptions(Deduplicate(options), selected);
    }

    private void RebuildMainModChips(string selected)
    {
        var options = new List<(string Value, string Label)>
        {
            ("CONTROL+ALT", "CONTROL+ALT"),
            ("CONTROL", "CONTROL"),
            ("ALT", "ALT"),
            ("SHIFT", "SHIFT"),
            ("WIN", "WIN")
        };

        _mainModChips.SetOptions(Deduplicate(options), selected);
    }

    private static IEnumerable<(string Value, string Label)> Deduplicate(
        IEnumerable<(string Value, string Label)> options)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var option in options)
        {
            if (seen.Add(option.Value))
                yield return option;
        }
    }

    private static bool TryParsePositive(string text, out float value)
    {
        value = 0f;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var styles = NumberStyles.Float;
        return (float.TryParse(text, styles, CultureInfo.InvariantCulture, out value) ||
                float.TryParse(text, styles, CultureInfo.CurrentCulture, out value))
               && value > 0f && float.IsFinite(value);
    }

    private static Label MakeSection() => new()
    {
        AutoSize = false,
        Font = Fonts.Section,
        ForeColor = Theme.Text,
        BackColor = Theme.Background,
        TextAlign = ContentAlignment.MiddleLeft
    };

    private static Label MakeCaption() => new()
    {
        AutoSize = false,
        Font = Fonts.Small,
        ForeColor = Theme.Muted,
        BackColor = Theme.Background,
        TextAlign = ContentAlignment.MiddleLeft
    };

    protected override void LayoutPage()
    {
        base.LayoutPage();

        var leftWidth = Math.Min(S(440), Width - S(48));
        var rightX = leftWidth + S(36);
        var rightWidth = Math.Max(0, Width - rightX);

        var y = S(0);
        _cameraSection.SetBounds(0, y, leftWidth, S(22));

        y += S(28);
        _sensitivityCaption.SetBounds(0, y, leftWidth, S(20));
        y += S(24);
        _sensitivityBox.SetBounds(0, y, S(128), S(38));

        y += S(52);
        _cameraCaption.SetBounds(0, y, leftWidth, S(20));
        y += S(24);
        _cameraChips.SetBounds(0, y, leftWidth, _cameraChips.Height);

        y += _cameraChips.Height + S(10);
        _customCameraBox.SetBounds(0, y, Math.Min(S(320), leftWidth), S(38));

        var autoCenterTop = S(264);
        _autoCenterCaption.SetBounds(0, autoCenterTop, leftWidth - S(64), S(26));
        _autoCenterToggle.Size = new Size(S(46), S(26));
        _autoCenterToggle.Location = new Point(leftWidth - S(46), autoCenterTop);

        var mainModTop = S(300);
        _mainModCaption.SetBounds(0, mainModTop, leftWidth, S(20));
        _mainModChips.SetBounds(0, mainModTop + S(22), leftWidth, _mainModChips.Height);

        _hotkeySection.SetBounds(rightX, 0, rightWidth, S(22));

        var rowTop = S(28);
        var rowStep = S(54);

        for (var i = 0; i < _hotkeyBoxes.Length; i++)
        {
            var top = rowTop + i * rowStep;
            _hotkeyCaptions[i].SetBounds(rightX, top, rightWidth, S(18));
            _hotkeyBoxes[i].SetBounds(rightX, top + S(20), rightWidth, S(34));
        }

        _hotkeyHint.SetBounds(rightX, rowTop + _hotkeyBoxes.Length * rowStep + S(2), rightWidth, S(32));
    }
}

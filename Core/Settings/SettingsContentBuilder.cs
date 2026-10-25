using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace nyxWM
{
    /// <summary>
    /// Builds the six settings pages from the values loaded in ConfigManager.
    /// The pages are display-only: edits are not written back to config yet.
    /// </summary>
    internal sealed class SettingsContentBuilder : IDisposable
    {
        private const float PadDip = 18f;
        private const float RowHeightDip = 30f;
        private const float RowStepDip = 48f;

        private static readonly string[] ThemeOptions =
        {
            "Blue", "Purple", "White", "Green", "Cyan", "Orange", "Red", "Yellow"
        };

        private static readonly string[] PositionOptions =
        {
            "TopCenter", "TopLeft", "TopRight", "BottomCenter", "BottomLeft", "BottomRight"
        };

        private readonly NotificationTheme _theme;
        private readonly float _scale;
        private readonly ConfigSettings _config;
        private readonly Font _labelFont;
        private readonly Font _fieldFont;
        private readonly Font _boldFont;

        private TextBox? _fallbackRefreshBox;
        private SettingsSlider? _lerpSlider;
        private SettingsToggle? _smoothingToggle;
        private SettingsToggle? _autoCenterToggle;
        private SettingsToggle? _pauseToggle;
        private SettingsHotkeyBox? _mainModBox;

        private SettingsHotkeyBox? _cameraMoveBox;
        private SettingsSlider? _sensitivitySlider;

        private ListBox? _ignoredList;

        private readonly SettingsHotkeyBox?[] _hotkeyBoxes = new SettingsHotkeyBox?[7];

        private readonly List<LaunchAppRow> _launchRows = new();

        private SettingsComboBox? _themeCombo;

        private SettingsToggle? _notifEnabledToggle;
        private SettingsComboBox? _positionCombo;
        private SettingsSlider? _durationSlider;

        internal SettingsContentBuilder(NotificationTheme theme, float scale, ConfigSettings config)
        {
            _theme = theme ?? throw new ArgumentNullException(nameof(theme));
            _scale = Math.Clamp(scale, 0.75f, 3f);
            _config = config ?? throw new ArgumentNullException(nameof(config));

            _labelFont = new Font("Segoe UI", 9.75f, FontStyle.Regular, GraphicsUnit.Point);
            _fieldFont = new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
            _boldFont = new Font("Segoe UI", 10f, FontStyle.Bold, GraphicsUnit.Point);
        }

        internal SettingsContentPanel[] Build(int width, int height)
        {
            var panels = new SettingsContentPanel[7];

            for (int i = 0; i < panels.Length; i++)
            {
                panels[i] = new SettingsContentPanel(_theme)
                {
                    Bounds = new Rectangle(0, 0, width, height)
                };
            }

            BuildCore(panels[0], width);
            BuildCamera(panels[1], width);
            BuildWindows(panels[2], width);
            BuildHotkeys(panels[3], width);
            BuildLaunchApps(panels[4], width);
            BuildVisuals(panels[5], width);
            BuildNotifications(panels[6], width);

            return panels;
        }

        private float Pad => PadDip * _scale;
        private float RowHeight => RowHeightDip * _scale;
        private float RowStep => RowStepDip * _scale;

        private void BuildCore(SettingsContentPanel panel, int width)
        {
            float y = 8f * _scale;
            float labelWidth = width * 0.42f;
            float fieldWidth = width - Pad * 2f - labelWidth - 12f * _scale;

            AddLabel(panel, "Fallback refresh rate", Pad, y, labelWidth);

            TextBox refresh = CreateNumericBox(
                _config.FallbackRefreshRate.ToString(CultureInfo.InvariantCulture),
                allowDecimal: false);

            float refreshWidth = Math.Min(fieldWidth, 180f * _scale);
            refresh.Bounds = Rect(width - Pad - refreshWidth, y, refreshWidth, RowHeight);
            panel.Controls.Add(refresh);
            _fallbackRefreshBox = refresh;
            y += RowStep;

            AddLabel(panel, "Smoothing speed", Pad, y, labelWidth);
            TextBox lerpBox = CreateNumericBox(
                FormatNumber(_config.LerpRatePerSecond),
                allowDecimal: true);

            float textWidth = 76f * _scale;
            float sliderWidth = Math.Min(fieldWidth, 220f * _scale);
            float sliderX = width - Pad - textWidth - 10f * _scale - sliderWidth;

            var lerpSlider = new SettingsSlider(_theme.Accent)
            {
                Minimum = 1.0,
                Maximum = 30.0,
                Value = Math.Clamp((double)_config.LerpRatePerSecond, 1.0, 30.0)
            };

            lerpSlider.Bounds = Rect(sliderX, y, sliderWidth, RowHeight);
            lerpBox.Bounds = Rect(width - Pad - textWidth, y, textWidth, RowHeight);
            panel.Controls.Add(lerpSlider);
            panel.Controls.Add(lerpBox);
            LinkSlider(lerpSlider, lerpBox);
            _lerpSlider = lerpSlider;
            y += RowStep;

            _smoothingToggle = AddToggleRow(panel, "Smoothing enabled", _config.SmoothingEnabled, width, y);
            y += RowStep;
            _autoCenterToggle = AddToggleRow(panel, "Auto-center on focus", _config.AutoCenterOnFocusDefault, width, y);
            y += RowStep;
            _pauseToggle = AddToggleRow(panel, "Pause on fullscreen", _config.PauseOnFullscreen, width, y);
            y += RowStep;

            AddLabel(panel, "Main modifier", Pad, y, labelWidth);
            SettingsHotkeyBox mainMod = CreateHotkeyBox(_config.MainMod, mainModProvider: null);
            float mainModWidth = Math.Min(fieldWidth, 240f * _scale);
            mainMod.Bounds = Rect(width - Pad - mainModWidth, y, mainModWidth, RowHeight);
            panel.Controls.Add(mainMod);
            _mainModBox = mainMod;
        }

        private void BuildCamera(SettingsContentPanel panel, int width)
        {
            float y = 8f * _scale;
            float labelWidth = width * 0.42f;
            float fieldWidth = width - Pad * 2f - labelWidth - 12f * _scale;

            AddLabel(panel, "Camera move button", Pad, y, labelWidth);
            SettingsHotkeyBox moveButton = CreateHotkeyBox(
                _config.CameraMoveButton,
                () => _config.MainMod,
                allowMouse: true);

            float moveWidth = Math.Min(fieldWidth, 240f * _scale);
            moveButton.Bounds = Rect(width - Pad - moveWidth, y, moveWidth, RowHeight);
            panel.Controls.Add(moveButton);
            _cameraMoveBox = moveButton;
            y += RowStep;

            AddLabel(panel, "Camera sensitivity", Pad, y, labelWidth);
            TextBox sensitivityBox = CreateNumericBox(
                FormatNumber(_config.CameraSensitivity),
                allowDecimal: true);

            float textWidth = 76f * _scale;
            float sliderWidth = Math.Min(fieldWidth, 220f * _scale);
            float sliderX = width - Pad - textWidth - 10f * _scale - sliderWidth;

            var sensitivitySlider = new SettingsSlider(_theme.Accent)
            {
                Minimum = 0.1,
                Maximum = 5.0,
                Value = Math.Clamp((double)_config.CameraSensitivity, 0.1, 5.0)
            };

            sensitivitySlider.Bounds = Rect(sliderX, y, sliderWidth, RowHeight);
            sensitivityBox.Bounds = Rect(width - Pad - textWidth, y, textWidth, RowHeight);
            panel.Controls.Add(sensitivitySlider);
            panel.Controls.Add(sensitivityBox);
            LinkSlider(sensitivitySlider, sensitivityBox);
            _sensitivitySlider = sensitivitySlider;
        }

        private void BuildWindows(SettingsContentPanel panel, int width)
        {
            float y = 8f * _scale;
            float listHeight = 210f * _scale;

            AddLabel(panel, "Ignored window class names", Pad, y, width * 0.7f, bold: true);
            y += 34f * _scale;

            var list = new ListBox
            {
                Font = _fieldFont,
                BackColor = SettingsPalette.FieldBackground,
                ForeColor = SettingsPalette.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle,
                IntegralHeight = false,
                Bounds = Rect(Pad, y, width - Pad * 2f, listHeight)
            };

            foreach (string className in _config.IgnoredWindowClassNames ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(className))
                    list.Items.Add(className);
            }

            panel.Controls.Add(list);
            _ignoredList = list;
            y += listHeight + 12f * _scale;

            float buttonHeight = RowHeight;
            float addWidth = 74f * _scale;
            float removeWidth = 90f * _scale;
            float gap = 8f * _scale;
            float inputWidth = width - Pad * 2f - addWidth - removeWidth - gap * 2f;

            var input = new TextBox
            {
                Font = _fieldFont,
                BackColor = SettingsPalette.FieldBackground,
                ForeColor = SettingsPalette.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle,
                Bounds = Rect(Pad, y, inputWidth, buttonHeight)
            };

            var addButton = new SettingsRoundedButton(_theme.Accent, accented: true)
            {
                Text = "Add",
                Font = _boldFont,
                Bounds = Rect(Pad + inputWidth + gap, y, addWidth, buttonHeight)
            };

            var removeButton = new SettingsRoundedButton(_theme.Accent, accented: false)
            {
                Text = "Remove",
                Font = _boldFont,
                Bounds = Rect(Pad + inputWidth + gap + addWidth + gap, y, removeWidth, buttonHeight)
            };

            addButton.Click += (_, _) =>
            {
                string value = input.Text.Trim();
                if (value.Length == 0)
                    return;

                list.Items.Add(value);
                input.Clear();
            };

            removeButton.Click += (_, _) =>
            {
                int index = list.SelectedIndex;
                if (index >= 0)
                    list.Items.RemoveAt(index);
            };

            panel.Controls.Add(input);
            panel.Controls.Add(addButton);
            panel.Controls.Add(removeButton);
        }

        private void BuildHotkeys(SettingsContentPanel panel, int width)
        {
            float y = 8f * _scale;
            float labelWidth = width * 0.42f;
            float fieldWidth = width - Pad * 2f - labelWidth - 12f * _scale;
            float fieldHeight = RowHeight;
            float boxWidth = Math.Min(fieldWidth, 260f * _scale);

            _hotkeyBoxes[0] = AddHotkeyRow(panel, "Reset camera", _config.Hotkeys.ResetCamera, width, boxWidth, fieldHeight, y);
            y += RowStep;
            _hotkeyBoxes[1] = AddHotkeyRow(panel, "Toggle auto-center", _config.Hotkeys.ToggleAutoCenter, width, boxWidth, fieldHeight, y);
            y += RowStep;
            _hotkeyBoxes[2] = AddHotkeyRow(panel, "Close window", _config.Hotkeys.CloseWindow, width, boxWidth, fieldHeight, y);
            y += RowStep;
            _hotkeyBoxes[3] = AddHotkeyRow(panel, "Center active window", _config.Hotkeys.CenterActiveWindow, width, boxWidth, fieldHeight, y);
            y += RowStep;
            _hotkeyBoxes[4] = AddHotkeyRow(panel, "Toggle game mode", _config.Hotkeys.ToggleGameMode, width, boxWidth, fieldHeight, y);
            y += RowStep;
            _hotkeyBoxes[5] = AddHotkeyRow(panel, "Toggle launcher", _config.Hotkeys.ToggleLauncher, width, boxWidth, fieldHeight, y);
            y += RowStep;
            _hotkeyBoxes[6] = AddHotkeyRow(panel, "Toggle Rofi menu", _config.Hotkeys.ToggleRofiMenu, width, boxWidth, fieldHeight, y);
        }

        private void BuildLaunchApps(SettingsContentPanel panel, int width)
        {
            float y = 8f * _scale;

            AddLabel(panel, "Launch apps", Pad, y, width * 0.7f, bold: true);
            y += 34f * _scale;

            float rowsTop = y;
            float rowStep = 42f * _scale;
            float rowHeight = 32f * _scale;
            float gap = 8f * _scale;
            float hotkeyWidth = 170f * _scale;
            float browseWidth = 68f * _scale;
            float removeWidth = 68f * _scale;
            float pathWidth = width - Pad * 2f - hotkeyWidth - browseWidth - removeWidth - gap * 3f;

            SettingsRoundedButton? addButton = null;

            void Relayout()
            {
                int count = _launchRows.Count;

                for (int i = 0; i < count; i++)
                {
                    LaunchAppRow row = _launchRows[i];
                    float rowY = rowsTop + i * rowStep;

                    row.Hotkey.Bounds = Rect(Pad, rowY, hotkeyWidth, rowHeight);
                    row.Path.Bounds = Rect(Pad + hotkeyWidth + gap, rowY, pathWidth, rowHeight);
                    row.Browse.Bounds = Rect(
                        Pad + hotkeyWidth + gap + pathWidth + gap,
                        rowY,
                        browseWidth,
                        rowHeight);
                    row.Remove.Bounds = Rect(
                        Pad + hotkeyWidth + gap + pathWidth + gap + browseWidth + gap,
                        rowY,
                        removeWidth,
                        rowHeight);
                }

                if (addButton != null)
                {
                    float addY = rowsTop + _launchRows.Count * rowStep + 6f * _scale;
                    addButton.Bounds = Rect(Pad, addY, 116f * _scale, rowHeight);
                }
            }

            void AddRow(string hotkey, string path)
            {
                var row = new LaunchAppRow
                {
                    Hotkey = CreateHotkeyBox(hotkey, () => _config.MainMod),
                    Path = new TextBox
                    {
                        Font = _fieldFont,
                        Text = path ?? string.Empty,
                        BackColor = SettingsPalette.FieldBackground,
                        ForeColor = SettingsPalette.TextPrimary,
                        BorderStyle = BorderStyle.FixedSingle
                    },
                    Browse = new SettingsRoundedButton(_theme.Accent, accented: false)
                    {
                        Text = "Browse",
                        Font = _labelFont
                    },
                    Remove = new SettingsRoundedButton(_theme.Accent, accented: false)
                    {
                        Text = "Remove",
                        Font = _labelFont
                    }
                };

                row.Browse.Click += (_, _) =>
                {
                    using var dialog = new OpenFileDialog
                    {
                        Filter = "Programs (*.exe)|*.exe|All files (*.*)|*.*",
                        CheckFileExists = true
                    };

                    if (dialog.ShowDialog() == DialogResult.OK)
                        row.Path.Text = dialog.FileName;
                };

                row.Remove.Click += (_, _) =>
                {
                    panel.Controls.Remove(row.Hotkey);
                    panel.Controls.Remove(row.Path);
                    panel.Controls.Remove(row.Browse);
                    panel.Controls.Remove(row.Remove);

                    row.Hotkey.Dispose();
                    row.Path.Dispose();
                    row.Browse.Dispose();
                    row.Remove.Dispose();

                    _launchRows.Remove(row);
                    Relayout();
                };

                _launchRows.Add(row);
                panel.Controls.Add(row.Hotkey);
                panel.Controls.Add(row.Path);
                panel.Controls.Add(row.Browse);
                panel.Controls.Add(row.Remove);
                Relayout();
            }

            foreach (KeyValuePair<string, string> entry in _config.LaunchApps
                         ?? new Dictionary<string, string>())
            {
                AddRow(entry.Key, entry.Value);
            }

            addButton = new SettingsRoundedButton(_theme.Accent, accented: true)
            {
                Text = "+ Add app",
                Font = _labelFont
            };

            addButton.Click += (_, _) => AddRow(string.Empty, string.Empty);

            panel.Controls.Add(addButton);
            Relayout();
        }

        private void BuildVisuals(SettingsContentPanel panel, int width)
        {
            float y = 8f * _scale;
            float labelWidth = width * 0.42f;

            AddLabel(panel, "Accent theme", Pad, y, labelWidth);

            var combo = new SettingsComboBox(_theme.Accent)
            {
                Font = _fieldFont,
                Bounds = Rect(width - Pad - 220f * _scale, y, 220f * _scale, RowHeight)
            };

            combo.ItemHeight = Math.Max(18, (int)MathF.Round(24f * _scale));
            combo.Items.AddRange(ThemeOptions);
            SelectComboValue(combo, _theme.Name);
            panel.Controls.Add(combo);
            _themeCombo = combo;
        }

        private void BuildNotifications(SettingsContentPanel panel, int width)
        {
            float y = 8f * _scale;
            float labelWidth = width * 0.42f;
            float fieldWidth = width - Pad * 2f - labelWidth - 12f * _scale;

            _notifEnabledToggle = AddToggleRow(panel, "Notifications enabled", _config.Notifications?.Enabled ?? true, width, y);
            y += RowStep;

            AddLabel(panel, "Notification position", Pad, y, labelWidth);

            var position = new SettingsComboBox(_theme.Accent)
            {
                Font = _fieldFont,
                Bounds = Rect(width - Pad - 220f * _scale, y, 220f * _scale, RowHeight)
            };

            position.ItemHeight = Math.Max(18, (int)MathF.Round(24f * _scale));
            position.Items.AddRange(PositionOptions);
            SelectComboValue(position, _config.Notifications?.Position ?? "TopCenter");
            panel.Controls.Add(position);
            _positionCombo = position;
            y += RowStep;

            AddLabel(panel, "Notification duration (ms)", Pad, y, labelWidth);
            TextBox durationBox = CreateNumericBox(
                (_config.Notifications?.DurationMs ?? 3200).ToString(CultureInfo.InvariantCulture),
                allowDecimal: false);

            float textWidth = 76f * _scale;
            float sliderWidth = Math.Min(fieldWidth, 220f * _scale);
            float sliderX = width - Pad - textWidth - 10f * _scale - sliderWidth;

            var durationSlider = new SettingsSlider(_theme.Accent)
            {
                Minimum = 800.0,
                Maximum = 20000.0,
                Value = Math.Clamp((double)(_config.Notifications?.DurationMs ?? 3200), 800.0, 20000.0)
            };

            durationSlider.Bounds = Rect(sliderX, y, sliderWidth, RowHeight);
            durationBox.Bounds = Rect(width - Pad - textWidth, y, textWidth, RowHeight);
            panel.Controls.Add(durationSlider);
            panel.Controls.Add(durationBox);
            LinkSlider(durationSlider, durationBox);
            _durationSlider = durationSlider;
        }

        private SettingsHotkeyBox AddHotkeyRow(
            Control parent,
            string label,
            string value,
            int width,
            float boxWidth,
            float boxHeight,
            float y)
        {
            float labelWidth = width * 0.42f;

            AddLabel(parent, label, Pad, y, labelWidth);
            SettingsHotkeyBox box = CreateHotkeyBox(value, () => _config.MainMod);
            box.Bounds = Rect(width - Pad - boxWidth, y, boxWidth, boxHeight);
            parent.Controls.Add(box);
            return box;
        }

        private SettingsToggle AddToggleRow(Control parent, string label, bool value, int width, float y)
        {
            float labelWidth = width * 0.42f;
            AddLabel(parent, label, Pad, y, labelWidth);

            var toggle = new SettingsToggle(_theme.Accent)
            {
                Checked = value,
                Size = new Size(
                    (int)MathF.Round(46f * _scale),
                    (int)MathF.Round(26f * _scale))
            };

            toggle.Location = new Point(
                (int)MathF.Round(width - Pad - toggle.Width),
                (int)MathF.Round(y + (RowHeight - toggle.Height) / 2f));

            parent.Controls.Add(toggle);
            return toggle;
        }

        private Label AddLabel(
            Control parent,
            string text,
            float x,
            float y,
            float width,
            bool bold = false)
        {
            var label = new Label
            {
                Text = text,
                AutoSize = false,
                BackColor = Color.Transparent,
                ForeColor = SettingsPalette.TextPrimary,
                Font = bold ? _boldFont : _labelFont,
                TextAlign = ContentAlignment.MiddleLeft,
                Bounds = Rect(x, y, width, RowHeight)
            };

            parent.Controls.Add(label);
            return label;
        }

        private SettingsHotkeyBox CreateHotkeyBox(string value, Func<string?>? mainModProvider, bool allowMouse = false)
        {
            return new SettingsHotkeyBox(_theme.Accent, mainModProvider, allowMouse)
            {
                Value = value ?? string.Empty,
                Font = _fieldFont
            };
        }

        private TextBox CreateNumericBox(string text, bool allowDecimal)
        {
            var box = new TextBox
            {
                Font = _fieldFont,
                Text = text,
                BackColor = SettingsPalette.FieldBackground,
                ForeColor = SettingsPalette.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle
            };

            box.KeyPress += (_, e) =>
            {
                if (char.IsControl(e.KeyChar) || char.IsDigit(e.KeyChar))
                    return;

                if (allowDecimal && (e.KeyChar == '.' || e.KeyChar == ','))
                {
                    if (box.Text.IndexOf('.') >= 0 || box.Text.IndexOf(',') >= 0)
                        e.Handled = true;

                    return;
                }

                e.Handled = true;
            };

            return box;
        }

        private static void LinkSlider(SettingsSlider slider, TextBox box)
        {
            bool syncing = false;

            slider.ValueChanged += (_, _) =>
            {
                if (syncing)
                    return;

                syncing = true;
                box.Text = FormatNumber((float)slider.Value);
                syncing = false;
            };

            box.TextChanged += (_, _) =>
            {
                if (syncing)
                    return;

                string raw = box.Text.Replace(',', '.');

                if (!float.TryParse(
                        raw,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out float value))
                {
                    return;
                }

                syncing = true;
                slider.Value = value;
                syncing = false;
            };
        }

        private static void SelectComboValue(SettingsComboBox combo, string? value)
        {
            for (int i = 0; i < combo.Items.Count; i++)
            {
                string item = combo.Items[i]?.ToString() ?? string.Empty;

                if (string.Equals(NormalizeChoice(item), NormalizeChoice(value), StringComparison.Ordinal))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }

            if (combo.Items.Count > 0)
                combo.SelectedIndex = 0;
        }

        private static string NormalizeChoice(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            Span<char> buffer = stackalloc char[value.Length];
            int length = 0;

            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c))
                    buffer[length++] = char.ToLowerInvariant(c);
            }

            return new string(buffer[..length]);
        }

        private static string FormatNumber(float value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static Rectangle Rect(float x, float y, float width, float height)
        {
            return new Rectangle(
                (int)MathF.Round(x),
                (int)MathF.Round(y),
                Math.Max(1, (int)MathF.Round(width)),
                Math.Max(1, (int)MathF.Round(height)));
        }

        /// <summary>
        /// Builds a new configuration snapshot from the current values of every
        /// control on the settings pages. Nothing is mutated here: the resulting
        /// <see cref="ConfigSettings"/> is a fresh object ready to be saved.
        /// </summary>
        internal ConfigSettings CollectConfig()
        {
            string? rawMainMod = _mainModBox?.Value?.Trim();
            string mainMod = string.IsNullOrWhiteSpace(rawMainMod)
                ? "CONTROL+ALT"
                : rawMainMod;

            var hotkeys = new HotkeySettings
            {
                ResetCamera = ValueOf(_hotkeyBoxes[0], "mainmod + space"),
                ToggleAutoCenter = ValueOf(_hotkeyBoxes[1], "mainmod + t"),
                CloseWindow = ValueOf(_hotkeyBoxes[2], "mainmod + c"),
                CenterActiveWindow = ValueOf(_hotkeyBoxes[3], "mainmod + f"),
                ToggleGameMode = ValueOf(_hotkeyBoxes[4], "mainmod + g"),
                ToggleLauncher = ValueOf(_hotkeyBoxes[5], "mainmod + d"),
                ToggleRofiMenu = ValueOf(_hotkeyBoxes[6], "mainmod + q")
            };

            return new ConfigSettings
            {
                FallbackRefreshRate = ParseInt(_fallbackRefreshBox?.Text, 60),
                LerpRatePerSecond = ParseFloat(_lerpSlider?.Value, 8.0f),
                SmoothingEnabled = _smoothingToggle?.Checked ?? true,
                AutoCenterOnFocusDefault = _autoCenterToggle?.Checked ?? true,
                PauseOnFullscreen = _pauseToggle?.Checked ?? true,
                MainMod = mainMod,
                CameraMoveButton = ValueOf(_cameraMoveBox, "MBUTTON"),
                CameraSensitivity = ParseFloat(_sensitivitySlider?.Value, 1.0f),
                IgnoredWindowClassNames = CollectList(_ignoredList),
                Hotkeys = hotkeys,
                LaunchApps = CollectLaunchApps(),
                Theme = _themeCombo?.SelectedItem?.ToString() ?? "Blue",
                Notifications = new NotificationSettings
                {
                    Enabled = _notifEnabledToggle?.Checked ?? true,
                    Position = _positionCombo?.SelectedItem?.ToString() ?? "TopCenter",
                    DurationMs = ParseDuration(_durationSlider?.Value)
                }
            };
        }

        private static string ValueOf(SettingsHotkeyBox? box, string fallback)
        {
            string value = box?.Value?.Trim() ?? string.Empty;
            return value.Length == 0 ? fallback : value;
        }

        private static int ParseInt(string? text, int fallback)
        {
            return int.TryParse(
                text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int value)
                ? value
                : fallback;
        }

        private static float ParseFloat(double? value, float fallback)
        {
            if (value is not double number || double.IsNaN(number) || double.IsInfinity(number))
                return fallback;

            return (float)Math.Clamp(number, -1_000_000.0, 1_000_000.0);
        }

        private static int ParseDuration(double? value)
        {
            int duration = value is double number ? (int)Math.Round(number) : 3200;
            return Math.Clamp(duration, 800, 20000);
        }

        private static string[] CollectList(ListBox? list)
        {
            if (list == null)
                return Array.Empty<string>();

            var result = new string[list.Items.Count];
            for (int i = 0; i < result.Length; i++)
                result[i] = list.Items[i]?.ToString() ?? string.Empty;

            return result;
        }

        private Dictionary<string, string> CollectLaunchApps()
        {
            var apps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (LaunchAppRow row in _launchRows)
            {
                string hotkey = row.Hotkey.Value?.Trim() ?? string.Empty;
                string path = row.Path.Text?.Trim() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(hotkey) || string.IsNullOrWhiteSpace(path))
                    continue;

                apps[hotkey] = path;
            }

            return apps;
        }

        public void Dispose()
        {
            _labelFont.Dispose();
            _fieldFont.Dispose();
            _boldFont.Dispose();
        }

        private sealed class LaunchAppRow
        {
            internal SettingsHotkeyBox Hotkey { get; init; } = null!;
            internal TextBox Path { get; init; } = null!;
            internal SettingsRoundedButton Browse { get; init; } = null!;
            internal SettingsRoundedButton Remove { get; init; } = null!;
        }
    }
}

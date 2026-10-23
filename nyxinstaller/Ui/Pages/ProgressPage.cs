using System.Drawing;
using System.Windows.Forms;
using NyxInstaller.Localization;
using NyxInstaller.Ui.Controls;

namespace NyxInstaller.Ui.Pages;

/// <summary>
/// Fifth page: simulated installation progress.
/// </summary>
public sealed class ProgressPage : WizardPage
{
    private const int TotalTicks = 130;

    private readonly Label _phaseLabel;
    private readonly Label _percentLabel;
    private readonly AccentProgressBar _bar;
    private readonly PhaseRow[] _rows;
    private readonly Label _note;
    private readonly System.Windows.Forms.Timer _timer;

    private int _tick;
    private double _progress;

    public ProgressPage(Localizer localizer)
        : base(localizer)
    {
        _phaseLabel = new Label
        {
            AutoSize = false,
            Font = Fonts.Section,
            ForeColor = Theme.Text,
            BackColor = Theme.Background,
            TextAlign = ContentAlignment.MiddleLeft
        };

        _percentLabel = new Label
        {
            AutoSize = false,
            Font = Fonts.Body,
            ForeColor = Theme.Accent,
            BackColor = Theme.Background,
            TextAlign = ContentAlignment.MiddleRight
        };

        _bar = new AccentProgressBar();
        _rows = new[] { new PhaseRow(), new PhaseRow(), new PhaseRow(), new PhaseRow() };
        _note = new Label
        {
            AutoSize = false,
            Font = Fonts.Small,
            ForeColor = Theme.Dim,
            BackColor = Theme.Background,
            TextAlign = ContentAlignment.MiddleLeft
        };

        Controls.Add(_phaseLabel);
        Controls.Add(_percentLabel);
        Controls.Add(_bar);
        foreach (var row in _rows)
            Controls.Add(row);
        Controls.Add(_note);

        _timer = new System.Windows.Forms.Timer { Interval = 40 };
        _timer.Tick += (_, _) => OnTick();
    }

    public event EventHandler? Completed;

    public override string TitleKey => "step.progress.title";

    public override string SubtitleKey => "step.progress.desc";

    public bool IsComplete { get; private set; }

    public void Begin()
    {
        _tick = 0;
        _progress = 0;
        IsComplete = false;
        _bar.Value = 0;
        _percentLabel.Text = "0%";
        _phaseLabel.Text = Localizer.Get("progress.check");
        _note.Text = Localizer.Get("progress.note");

        for (var i = 0; i < _rows.Length; i++)
        {
            _rows[i].State = i == 0 ? PhaseState.Active : PhaseState.Pending;
            _rows[i].Pulse = 0f;
        }

        _timer.Start();
    }

    public override void Localize()
    {
        var keys = new[] { "progress.check", "progress.download", "progress.extract", "progress.install" };
        for (var i = 0; i < _rows.Length; i++)
            _rows[i].Label = Localizer.Get(keys[i]);

        if (!IsComplete)
            _phaseLabel.Text = Localizer.Get(keys[Math.Max(0, PhaseIndex(_progress))]);
        else
            _phaseLabel.Text = Localizer.Get("progress.done");

        _note.Text = Localizer.Get("progress.note");
        Relayout();
    }

    public override void OnEnter()
    {
        base.OnEnter();
        Relayout();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _timer.Dispose();

        base.Dispose(disposing);
    }

    private static int PhaseIndex(double progress) => progress switch
    {
        < 0.12 => 0,
        < 0.58 => 1,
        < 0.80 => 2,
        _ => 3
    };

    private void OnTick()
    {
        _tick++;

        var ratio = Math.Min(1d, (double)_tick / TotalTicks);
        _progress = 1d - Math.Pow(1d - ratio, 1.6);

        _bar.Value = _progress;
        _percentLabel.Text = $"{(int)Math.Round(_progress * 100d)}%";

        var phase = PhaseIndex(_progress);
        var pulse = (float)((Math.Sin(_tick * 0.35) + 1d) / 2d);

        for (var i = 0; i < _rows.Length; i++)
        {
            _rows[i].State = i < phase
                ? PhaseState.Done
                : i == phase
                    ? PhaseState.Active
                    : PhaseState.Pending;
            _rows[i].Pulse = pulse;
        }

        _phaseLabel.Text = Localizer.Get(phase switch
        {
            0 => "progress.check",
            1 => "progress.download",
            2 => "progress.extract",
            _ => "progress.install"
        });

        if (ratio < 1d)
            return;

        _timer.Stop();
        IsComplete = true;

        foreach (var row in _rows)
            row.State = PhaseState.Done;

        _phaseLabel.Text = Localizer.Get("progress.done");
        _percentLabel.Text = "100%";
        _bar.Value = 1d;

        Completed?.Invoke(this, EventArgs.Empty);
    }

    protected override void LayoutPage()
    {
        base.LayoutPage();

        var width = Math.Min(S(560), Width);
        var left = Math.Max(0, (Width - width) / 2);

        _phaseLabel.SetBounds(left, S(20), width - S(80), S(26));
        _percentLabel.SetBounds(left + width - S(72), S(20), S(72), S(26));
        _bar.SetBounds(left, S(54), width, S(10));

        var top = S(96);
        foreach (var row in _rows)
        {
            row.SetBounds(left, top, width, S(34));
            top += S(42);
        }

        _note.SetBounds(left, top + S(14), width, S(36));
    }
}

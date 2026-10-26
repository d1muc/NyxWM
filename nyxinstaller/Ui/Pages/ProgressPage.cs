using System.Drawing;
using System.Windows.Forms;
using NyxInstaller.Localization;
using NyxInstaller.Models;
using NyxInstaller.Ui.Controls;

namespace NyxInstaller.Ui.Pages;

/// <summary>
/// Fifth page: real installation progress driven by <see cref="InstallProgress"/> reports.
/// </summary>
public sealed class ProgressPage : WizardPage
{
    private readonly Label _phaseLabel;
    private readonly Label _percentLabel;
    private readonly AccentProgressBar _bar;
    private readonly PhaseRow[] _rows;
    private readonly Label _note;
    private readonly System.Windows.Forms.Timer _timer;

    private int _pulseTick;
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
        _timer.Tick += (_, _) => OnPulse();
    }

    public override string TitleKey => "step.progress.title";

    public override string SubtitleKey => "step.progress.desc";

    public bool IsComplete { get; private set; }

    public bool HasFailed { get; private set; }

    public void Begin()
    {
        IsComplete = false;
        HasFailed = false;
        _pulseTick = 0;
        _progress = 0d;
        _bar.Value = 0d;
        _percentLabel.Text = "0%";
        _phaseLabel.Text = Localizer.Get("progress.check");
        _note.ForeColor = Theme.Dim;
        _note.Text = Localizer.Get("progress.note");

        for (var i = 0; i < _rows.Length; i++)
        {
            _rows[i].State = i == 0 ? PhaseState.Active : PhaseState.Pending;
            _rows[i].Pulse = 0f;
        }

        _timer.Start();
    }

    public void Report(InstallProgress progress)
    {
        if (IsDisposed || Disposing)
            return;

        if (InvokeRequired)
        {
            BeginInvoke(new Action<InstallProgress>(Report), progress);
            return;
        }

        if (IsComplete || HasFailed)
            return;

        _progress = Math.Clamp(progress.Fraction, 0d, 1d);
        _bar.Value = _progress;
        _percentLabel.Text = $"{(int)Math.Round(_progress * 100d)}%";
        _phaseLabel.Text = Localizer.Get(PhaseKey(progress.Phase));

        if (!string.IsNullOrWhiteSpace(progress.Detail))
            _note.Text = progress.Detail;

        var phase = PhaseIndex(progress.Phase);
        for (var i = 0; i < _rows.Length; i++)
        {
            _rows[i].State = i < phase
                ? PhaseState.Done
                : i == phase
                    ? PhaseState.Active
                    : PhaseState.Pending;
        }
    }

    public void Complete()
    {
        if (IsDisposed || Disposing)
            return;

        if (InvokeRequired)
        {
            BeginInvoke(new Action(Complete));
            return;
        }

        _timer.Stop();
        IsComplete = true;
        _progress = 1d;
        _bar.Value = 1d;
        _percentLabel.Text = "100%";
        _phaseLabel.Text = Localizer.Get("progress.done");
        _note.ForeColor = Theme.Dim;
        _note.Text = Localizer.Get("progress.note");

        foreach (var row in _rows)
            row.State = PhaseState.Done;
    }

    public void Fail(string message)
    {
        if (IsDisposed || Disposing)
            return;

        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(Fail), message);
            return;
        }

        _timer.Stop();
        HasFailed = true;
        _phaseLabel.Text = Localizer.Get("progress.failed");
        _note.ForeColor = Theme.Error;
        _note.Text = message;

        foreach (var row in _rows)
        {
            if (row.State == PhaseState.Active)
                row.State = PhaseState.Pending;
        }
    }

    public override void Localize()
    {
        var keys = new[] { "progress.check", "progress.download", "progress.extract", "progress.install" };
        for (var i = 0; i < _rows.Length; i++)
            _rows[i].Label = Localizer.Get(keys[i]);

        if (IsComplete)
            _phaseLabel.Text = Localizer.Get("progress.done");
        else if (HasFailed)
            _phaseLabel.Text = Localizer.Get("progress.failed");
        else
            _phaseLabel.Text = Localizer.Get(keys[PhaseIndexFromFraction(_progress)]);

        if (!HasFailed)
        {
            _note.ForeColor = Theme.Dim;
            _note.Text = Localizer.Get("progress.note");
        }

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

    private static string PhaseKey(InstallPhase phase) => phase switch
    {
        InstallPhase.Checking => "progress.check",
        InstallPhase.Downloading => "progress.download",
        InstallPhase.Extracting => "progress.extract",
        _ => "progress.install"
    };

    private static int PhaseIndex(InstallPhase phase) => phase switch
    {
        InstallPhase.Checking => 0,
        InstallPhase.Downloading => 1,
        InstallPhase.Extracting => 2,
        _ => 3
    };

    private static int PhaseIndexFromFraction(double progress) => progress switch
    {
        < 0.05 => 0,
        < 0.62 => 1,
        < 0.80 => 2,
        _ => 3
    };

    private void OnPulse()
    {
        _pulseTick++;
        var pulse = (float)((Math.Sin(_pulseTick * 0.35) + 1d) / 2d);

        foreach (var row in _rows)
        {
            if (row.State == PhaseState.Active)
                row.Pulse = pulse;
        }
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

        _note.SetBounds(left, top + S(14), width, S(48));
    }
}

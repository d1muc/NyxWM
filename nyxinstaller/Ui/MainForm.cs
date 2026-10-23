using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Windows.Forms;
using NyxInstaller.App;
using NyxInstaller.Localization;
using NyxInstaller.Models;
using NyxInstaller.Ui.Controls;
using NyxInstaller.Ui.Pages;

namespace NyxInstaller.Ui;

/// <summary>
/// Borderless dark window that hosts the wizard pages, the custom title bar and the footer.
/// Interface only: no page touches the file system or the registry.
/// </summary>
public sealed class MainForm : Form
{
    private const int LogicalWidth = 900;
    private const int LogicalHeight = 640;
    private const int WmNcHitTest = 0x0084;
    private const int HtCaption = 0x0002;

    private readonly Localizer _localizer;
    private readonly List<WizardPage> _pages = new();
    private readonly LanguagePage _languagePage;
    private readonly PathPage _pathPage;
    private readonly OptionsPage _optionsPage;
    private readonly ConfigPage _configPage;
    private readonly ProgressPage _progressPage;
    private readonly FinishPage _finishPage;

    private readonly BufferedPanel _contentHost;
    private readonly StepDots _dots;
    private readonly GhostButton _back;
    private readonly AccentButton _next;
    private readonly Label _footerNote;
    private readonly IconButton _minimize;
    private readonly IconButton _close;
    private readonly System.Windows.Forms.Timer _advanceTimer;

    private int _index;
    private bool _ready;
    private int _pageOffset;
    private readonly string _pageAnimKey = Animator.NextKey();

    public MainForm(Localizer localizer)
    {
        _localizer = localizer;

        AutoScaleMode = AutoScaleMode.None;
        BackColor = Theme.Background;
        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.None;
        KeyPreview = true;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Text = "nyxWM Setup";

        _contentHost = new BufferedPanel { BackColor = Theme.Background };

        _dots = new StepDots();
        _footerNote = new Label
        {
            AutoSize = true,
            Font = Fonts.Small,
            ForeColor = Theme.Dim,
            BackColor = Theme.Background
        };

        _back = new GhostButton { Enabled = false };
        _next = new AccentButton();

        _back.Click += (_, _) => GoBack();
        _next.Click += (_, _) => GoNext();

        _minimize = new IconButton(IconKind.Minimize);
        _close = new IconButton(IconKind.Close);
        _minimize.Click += (_, _) => WindowState = FormWindowState.Minimized;
        _close.Click += (_, _) => Close();

        _languagePage = new LanguagePage(localizer);
        _pathPage = new PathPage(localizer, InstallerDefaults.DefaultInstallRoot);
        _optionsPage = new OptionsPage(localizer);
        _configPage = new ConfigPage(localizer, InstallerDefaults.DefaultConfig);
        _progressPage = new ProgressPage(localizer);
        _finishPage = new FinishPage(localizer);

        _pages.AddRange(new WizardPage[]
        {
            _languagePage,
            _pathPage,
            _optionsPage,
            _configPage,
            _progressPage,
            _finishPage
        });

        _languagePage.LanguageChanged += (_, language) => ApplyLanguage(language);
        _progressPage.Completed += (_, _) => OnSimulationCompleted();

        _advanceTimer = new System.Windows.Forms.Timer { Interval = 700 };
        _advanceTimer.Tick += (_, _) =>
        {
            _advanceTimer.Stop();
            ShowPage(_index + 1, true);
        };

        foreach (var page in _pages)
        {
            page.Visible = false;
            _contentHost.Controls.Add(page);
        }

        Controls.Add(_contentHost);
        Controls.Add(_dots);
        Controls.Add(_footerNote);
        Controls.Add(_back);
        Controls.Add(_next);
        Controls.Add(_minimize);
        Controls.Add(_close);

        _languagePage.Select(localizer.Language, false);
        ApplyLanguage(localizer.Language);
        ShowPage(0);

        _ready = true;
    }

    private int S(int value) => Theme.S(this, value);

    private int TitleBarHeight => S(52);

    private int HeaderHeight => S(92);

    private int FooterHeight => S(78);

    private int SidePadding => S(36);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        Drawing.EnableRoundedCorners(this);

        var size = new Size(S(LogicalWidth), S(LogicalHeight));
        if (ClientSize != size)
            ClientSize = size;

        MinimumSize = size;
        MaximumSize = size;

        LayoutChrome();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutChrome();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _advanceTimer.Dispose();
        base.OnFormClosed(e);
    }

    private void LayoutChrome()
    {
        if (!_ready)
            return;

        var width = ClientSize.Width;
        var height = ClientSize.Height;
        if (width <= 0 || height <= 0)
            return;

        var titleHeight = TitleBarHeight;
        var headerHeight = HeaderHeight;
        var footerHeight = FooterHeight;

        _contentHost.SetBounds(
            0,
            titleHeight + headerHeight,
            width,
            Math.Max(0, height - titleHeight - headerHeight - footerHeight));
        LayoutPages();

        _dots.UpdateMetrics();
        _dots.Location = new Point(width - SidePadding - _dots.Width, titleHeight + S(38));

        var buttonHeight = S(40);
        var buttonTop = height - footerHeight + (footerHeight - buttonHeight) / 2;

        _next.Size = new Size(S(132), buttonHeight);
        _back.Size = new Size(S(108), buttonHeight);
        _next.Location = new Point(width - SidePadding - _next.Width, buttonTop);
        _back.Location = new Point(_next.Left - S(12) - _back.Width, buttonTop);

        _footerNote.Location = new Point(
            SidePadding,
            height - footerHeight + (footerHeight - _footerNote.Height) / 2);

        _close.Size = new Size(S(38), S(28));
        _minimize.Size = new Size(S(38), S(28));
        _close.Location = new Point(width - S(10) - _close.Width, (titleHeight - _close.Height) / 2);
        _minimize.Location = new Point(_close.Left - S(4) - _minimize.Width, (titleHeight - _minimize.Height) / 2);

        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (!_ready)
            return;

        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Theme.Background);

        var width = ClientSize.Width;
        var titleHeight = TitleBarHeight;
        var footerTop = ClientSize.Height - FooterHeight;

        using (var separator = new Pen(Theme.BorderSoft))
        {
            graphics.DrawLine(separator, 0, titleHeight - 1, width, titleHeight - 1);
            graphics.DrawLine(separator, 0, footerTop, width, footerTop);
        }

        PaintTitleBar(graphics, titleHeight);
        PaintHeader(graphics, titleHeight);

        base.OnPaint(e);
    }

    private void PaintTitleBar(Graphics graphics, int titleHeight)
    {
        var logoSize = S(26);
        var logoRect = new Rectangle(S(18), (titleHeight - logoSize) / 2, logoSize, logoSize);

        Drawing.FillRounded(graphics, logoRect, S(8), Theme.Accent);
        TextRenderer.DrawText(
            graphics,
            "n",
            Fonts.Logo,
            logoRect,
            Theme.AccentText,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        var textLeft = logoRect.Right + S(10);
        var titleWidth = TextRenderer.MeasureText(
            graphics, "nyxWM", Fonts.TitleBar, Size.Empty, TextFormatFlags.NoPrefix).Width;

        TextRenderer.DrawText(
            graphics,
            "nyxWM",
            Fonts.TitleBar,
            new Rectangle(textLeft, 0, titleWidth + S(4), titleHeight),
            Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        TextRenderer.DrawText(
            graphics,
            _localizer.Get("window.subtitle"),
            Fonts.Body,
            new Rectangle(textLeft + titleWidth + S(10), 0, ClientSize.Width, titleHeight),
            Theme.Dim,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    private void PaintHeader(Graphics graphics, int top)
    {
        var page = _pages[_index];
        var right = _dots.Left - S(28);
        var left = SidePadding;
        var width = Math.Max(0, right - left);

        TextRenderer.DrawText(
            graphics,
            _localizer.Get(page.TitleKey),
            Fonts.Header,
            new Rectangle(left, top + S(14), width, S(34)),
            Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

        TextRenderer.DrawText(
            graphics,
            _localizer.Get(page.SubtitleKey),
            Fonts.Body,
            new Rectangle(left, top + S(50), width, S(24)),
            Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmNcHitTest)
        {
            var value = (long)message.LParam;
            var screenPoint = new Point(
                unchecked((short)(value & 0xFFFF)),
                unchecked((short)((value >> 16) & 0xFFFF)));

            var clientPoint = PointToClient(screenPoint);
            if (clientPoint.Y >= 0 && clientPoint.Y < TitleBarHeight)
            {
                message.Result = (IntPtr)HtCaption;
                return;
            }
        }

        base.WndProc(ref message);
    }

    private void ApplyLanguage(InstallerLanguage language)
    {
        _localizer.SetLanguage(language);

        foreach (var page in _pages)
            page.Localize();

        _back.Text = _localizer.Get("common.back");
        _footerNote.Text = _localizer.Get("sim.notice");

        UpdateNavigation();
        LayoutChrome();
        Invalidate();
    }

    private void LayoutPages()
    {
        var width = _contentHost.Width - SidePadding * 2;
        var height = _contentHost.Height - S(8);
        if (width <= 0 || height <= 0)
            return;

        for (var i = 0; i < _pages.Count; i++)
        {
            _pages[i].SetBounds(
                SidePadding + (i == _index ? _pageOffset : 0),
                S(4),
                width,
                height);
        }
    }

    private void ShowPage(int index) => ShowPage(index, false);

    private void ShowPage(int index, bool animate)
    {
        if (index < 0 || index >= _pages.Count)
            return;

        _index = index;

        for (var i = 0; i < _pages.Count; i++)
            _pages[i].Visible = i == index;

        _pages[index].OnEnter();
        _pages[index].Relayout();

        _dots.Current = index;
        UpdateNavigation();

        if (animate)
            AnimatePageIn();
        else
        {
            _pageOffset = 0;
            LayoutPages();
        }

        Invalidate();
    }

    private void AnimatePageIn()
    {
        var from = S(36);
        _pageOffset = from;
        LayoutPages();

        Animator.Instance.Tween(_pageAnimKey, from, 0, Motion.Page, Easing.EaseOutCubic, value =>
        {
            if (IsDisposed || Disposing)
                return;

            _pageOffset = (int)Math.Round(value);
            LayoutPages();
        });
    }

    private void UpdateNavigation()
    {
        _back.Enabled = _index is > 0 and < 4;
        _back.Visible = _index < 4;

        switch (_index)
        {
            case 3:
                _next.Text = _localizer.Get("common.install");
                _next.Enabled = true;
                break;
            case 4:
                _next.Text = _localizer.Get("common.next");
                _next.Enabled = _progressPage.IsComplete;
                break;
            case 5:
                _next.Text = _localizer.Get("common.finish");
                _next.Enabled = true;
                break;
            default:
                _next.Text = _localizer.Get("common.next");
                _next.Enabled = true;
                break;
        }

        LayoutChrome();
    }

    private void GoBack()
    {
        if (_index is > 0 and < 4)
            ShowPage(_index - 1, true);
    }

    private void GoNext()
    {
        switch (_index)
        {
            case 5:
                Close();
                return;
            case 4:
                if (_progressPage.IsComplete)
                    ShowPage(5, true);
                return;
            case 3:
                if (!_configPage.Validate(out _))
                    return;

                StartSimulation();
                return;
            default:
                if (!_pages[_index].Validate(out _))
                    return;

                ShowPage(_index + 1, true);
                return;
        }
    }

    private void StartSimulation()
    {
        ShowPage(4, true);
        _progressPage.Begin();
        UpdateNavigation();
    }

    private void OnSimulationCompleted()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.2.0";

        _finishPage.SetSummary(
            _pathPage.TargetPath,
            _optionsPage.RegisterAutostart,
            _optionsPage.CreateShortcut,
            version);

        _advanceTimer.Start();
    }
}

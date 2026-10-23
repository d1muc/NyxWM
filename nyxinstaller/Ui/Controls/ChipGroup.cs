using System.Drawing;
using System.Windows.Forms;

namespace NyxInstaller.Ui.Controls;

/// <summary>
/// A single-select group of wrapping <see cref="Chip"/> controls.
/// </summary>
public sealed class ChipGroup : Control
{
    private readonly List<Chip> _chips = new();
    private string _selected = string.Empty;
    private bool _layingOut;

    public ChipGroup()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = Theme.Background;
    }

    public event EventHandler<string>? SelectionChanged;

    public string Selected => _selected;

    private int S(int value) => Theme.S(this, value);

    public void SetOptions(IEnumerable<(string Value, string Label)> options, string selected)
    {
        SuspendLayout();

        foreach (var chip in _chips)
        {
            Controls.Remove(chip);
            chip.Dispose();
        }

        _chips.Clear();

        foreach (var (value, label) in options)
        {
            var chip = new Chip(value, label)
            {
                Selected = string.Equals(value, selected, StringComparison.OrdinalIgnoreCase)
            };
            chip.Click += (_, _) => Select(chip.Value, true);
            _chips.Add(chip);
            Controls.Add(chip);
        }

        _selected = selected;
        ResumeLayout();
        LayoutChips();
        Invalidate();
    }

    public void Select(string value, bool raiseEvent)
    {
        _selected = value;

        foreach (var chip in _chips)
            chip.Selected = string.Equals(chip.Value, value, StringComparison.OrdinalIgnoreCase);

        Invalidate();

        if (raiseEvent)
            SelectionChanged?.Invoke(this, value);
    }

    private void LayoutChips()
    {
        if (_layingOut)
            return;

        _layingOut = true;
        try
        {
            var chipHeight = S(32);
            var gap = S(8);
            var padding = S(14);

            var x = 0;
            var y = 0;

            foreach (var chip in _chips)
            {
                chip.RecalculateSize(chipHeight, padding);
                if (x > 0 && x + chip.Width > Width)
                {
                    x = 0;
                    y += chipHeight + gap;
                }

                chip.Location = new Point(x, y);
                x += chip.Width + gap;
            }

            var needed = _chips.Count == 0 ? 0 : y + chipHeight;
            if (Height != needed)
                Height = needed;
        }
        finally
        {
            _layingOut = false;
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutChips();
    }

    protected override void OnPaint(PaintEventArgs pe)
    {
        base.OnPaint(pe);
        pe.Graphics.Clear(Theme.Background);
    }
}

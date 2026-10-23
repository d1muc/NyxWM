using System.Drawing;
using System.Windows.Forms;

namespace NyxInstaller.Ui;

/// <summary>
/// Smoothly animates a single <see cref="Color"/> owned by one control. A new call replaces any
/// in-flight tween, so rapid hover / press / selection changes never fight each other.
/// </summary>
public sealed class TweenColor
{
    private readonly Control _owner;
    private readonly string _key;
    private Color _value;
    private Color _target;

    public TweenColor(Control owner, Color initial)
    {
        _owner = owner;
        _key = Animator.NextKey();
        _value = initial;
        _target = initial;
    }

    public Color Value => _value;

    public void Set(Color target, int durationMs = Motion.Hover)
    {
        if (target == _target)
            return;

        _target = target;

        if (!_owner.IsHandleCreated)
        {
            _value = target;
            return;
        }

        var from = _value;
        Animator.Instance.Tween(_key, 0d, 1d, durationMs, Easing.EaseOutCubic, t =>
        {
            if (_owner.IsDisposed || _owner.Disposing)
                return;

            _value = Animator.Blend(from, target, t);
            _owner.Invalidate();
        });
    }
}

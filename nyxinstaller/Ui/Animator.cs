using System.Drawing;

namespace NyxInstaller.Ui;

/// <summary>Easing curves for the animation engine.</summary>
public enum Easing
{
    Linear,
    EaseOutCubic,
    EaseOutQuint,
    EaseInOutCubic,
    EaseOutBack
}

/// <summary>Maps a 0..1 linear progress through an <see cref="Easing"/> curve.</summary>
public static class EasingFunctions
{
    public static double Apply(Easing easing, double t)
    {
        t = Math.Clamp(t, 0d, 1d);

        return easing switch
        {
            Easing.Linear => t,
            Easing.EaseOutCubic => 1d - Math.Pow(1d - t, 3d),
            Easing.EaseOutQuint => 1d - Math.Pow(1d - t, 5d),
            Easing.EaseInOutCubic => t < 0.5d
                ? 4d * t * t * t
                : 1d - Math.Pow(-2d * t + 2d, 3d) / 2d,
            Easing.EaseOutBack => EaseOutBack(t),
            _ => t
        };
    }

    private static double EaseOutBack(double t)
    {
        const double c1 = 1.70158;
        const double c3 = c1 + 1d;
        return 1d + c3 * Math.Pow(t - 1d, 3d) + c1 * Math.Pow(t - 1d, 2d);
    }
}

/// <summary>Named animation durations (milliseconds) so timings stay consistent and tweakable.</summary>
public static class Motion
{
    public const int Hover = 140;
    public const int Focus = 140;
    public const int Selection = 200;
    public const int Toggle = 220;
    public const int Page = 260;
    public const int Pop = 420;
}

/// <summary>
/// Minimal tween engine driven by one shared WinForms timer. All calls happen on the UI thread;
/// the timer only runs while at least one tween is active.
/// </summary>
public sealed class Animator : IDisposable
{
    private static Animator? _instance;
    private static int _sequence;

    public static Animator Instance => _instance ??= new Animator();

    private readonly System.Windows.Forms.Timer _timer;
    private readonly Dictionary<string, TweenState> _tweens = new();

    private Animator()
    {
        _timer = new System.Windows.Forms.Timer { Interval = 15 };
        _timer.Tick += (_, _) => Tick();
    }

    /// <summary>Returns a key unique to one animation target, used to replace overlapping tweens.</summary>
    public static string NextKey() => $"tween-{Interlocked.Increment(ref _sequence)}";

    public void Tween(
        string key,
        double from,
        double to,
        int durationMs,
        Easing easing,
        Action<double> onFrame,
        Action? onComplete = null)
    {
        _tweens[key] = new TweenState(Environment.TickCount, from, to, Math.Max(0, durationMs), easing, onFrame, onComplete);
        if (!_timer.Enabled)
            _timer.Start();
    }

    public void Cancel(string key)
    {
        _tweens.Remove(key);
        StopIfIdle();
    }

    public static Color Blend(Color from, Color to, double t)
    {
        t = Math.Clamp(t, 0d, 1d);
        return Color.FromArgb(
            BlendChannel(from.A, to.A, t),
            BlendChannel(from.R, to.R, t),
            BlendChannel(from.G, to.G, t),
            BlendChannel(from.B, to.B, t));
    }

    private static int BlendChannel(int from, int to, double t) =>
        (int)Math.Round(from + (to - from) * t);

    private void Tick()
    {
        var now = Environment.TickCount;
        var completed = new List<string>();

        foreach (var (key, tween) in _tweens.ToArray())
        {
            var t = tween.Duration <= 0
                ? 1d
                : Math.Clamp((double)(now - tween.Start) / tween.Duration, 0d, 1d);

            var value = tween.From + (tween.To - tween.From) * EasingFunctions.Apply(tween.Easing, t);
            tween.OnFrame(value);

            if (t >= 1d)
                completed.Add(key);
        }

        foreach (var key in completed)
        {
            if (_tweens.TryGetValue(key, out var done))
            {
                _tweens.Remove(key);
                done.OnComplete?.Invoke();
            }
        }

        StopIfIdle();
    }

    private void StopIfIdle()
    {
        if (_tweens.Count == 0)
            _timer.Stop();
    }

    public void Dispose()
    {
        _tweens.Clear();
        _timer.Dispose();
    }

    private sealed class TweenState
    {
        public TweenState(
            int start,
            double from,
            double to,
            int duration,
            Easing easing,
            Action<double> onFrame,
            Action? onComplete)
        {
            Start = start;
            From = from;
            To = to;
            Duration = duration;
            Easing = easing;
            OnFrame = onFrame;
            OnComplete = onComplete;
        }

        public int Start { get; }
        public double From { get; }
        public double To { get; }
        public int Duration { get; }
        public Easing Easing { get; }
        public Action<double> OnFrame { get; }
        public Action? OnComplete { get; }
    }
}

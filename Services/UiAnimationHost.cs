using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace nyxWM
{
    /// <summary>
    /// Process-wide animation host. Every overlay registers a paint callback
    /// here instead of running its own <see cref="System.Windows.Forms.Timer"/>,
    /// so the whole UI advances on one clock with one delta time.
    ///
    /// All members are expected to be used from the UI thread.
    /// </summary>
    internal static class UiAnimationHost
    {
        /// <summary>Repaints requested by overlays that cannot invalidate from the ticker thread.</summary>
        private static readonly List<Control> PendingInvalidations = new();

        private static readonly object PendingLock = new();

        private static RenderTicker? _ticker;
        private static Control? _marshal;
        private static int _flushScheduled;
        private static int _requestedHz = 60;

        /// <summary>
        /// Starts the shared frame clock. <paramref name="initialHz"/> seeds the
        /// cadence; <see cref="RequestHz"/> updates it when a settings save lands.
        /// </summary>
        internal static void Start(int initialHz)
        {
            if (_ticker != null)
                return;

            // A hidden control created here, on the UI thread, gives every other
            // thread a BeginInvoke target. SynchronizationContext.Current is still
            // null at this point (the message loop has not started yet), so Post
            // must not rely on it.
            var marshal = new Control();
            marshal.CreateControl();
            _marshal = marshal;

            _requestedHz = NormalizeHz(initialHz);

            _ticker = new RenderTicker(_ => FlushInvalidations(), () => _requestedHz);
            _ticker.Start();
        }

        /// <summary>Stops the frame clock and drops every pending repaint.</summary>
        internal static void Stop()
        {
            RenderTicker? ticker = _ticker;
            _ticker = null;

            ticker?.Dispose();

            Control? marshal = _marshal;
            _marshal = null;

            lock (PendingLock)
            {
                PendingInvalidations.Clear();
            }

            try
            {
                marshal?.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UiAnimationHost: marshal dispose failed: {ex.Message}");
            }
        }

        /// <summary>True while the frame clock is running.</summary>
        internal static bool IsRunning => _ticker != null;

        /// <summary>Raises or lowers the frame cadence after a settings save.</summary>
        internal static void RequestHz(int hz)
        {
            _requestedHz = NormalizeHz(hz);
        }

        /// <summary>
        /// Leaves the idle cadence for the next frames. Safe from any thread;
        /// gesture starts call this from the mouse-hook thread.
        /// </summary>
        internal static void RequestActivity()
        {
            _ticker?.RequestActive();
        }

        /// <summary>
        /// Advances <paramref name="advance"/> once per frame. The callback runs
        /// on the ticker thread; <paramref name="onFrame"/> runs on the UI thread
        /// and must be safe to call even when nothing changed.
        /// </summary>
        /// <returns>A token to pass to <see cref="Unsubscribe"/>.</returns>
        internal static Action Subscribe(
            Action<float> advance,
            Action? onFrame = null)
        {
            ArgumentNullException.ThrowIfNull(advance);

            RenderTicker? ticker = _ticker;
            if (ticker == null)
                throw new InvalidOperationException("UiAnimationHost.Start must run before Subscribe.");

            // The ticker thread only advances animation state. Painting stays on
            // the UI thread, so the frame callback is posted instead of executed.
            Func<double, RenderTicker.TickResult> subscriber = null!;
            Action? frameAction = onFrame == null ? null : () =>
            {
                onFrame();
            };

            subscriber = deltaMs =>
            {
                RenderTicker.TickResult result;

                try
                {
                    advance((float)deltaMs);

                    if (frameAction != null)
                        result = RenderTicker.TickResult.Active;
                    else
                        result = RenderTicker.TickResult.Idle;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"UiAnimationHost: advance failed: {ex.Message}");
                    return RenderTicker.TickResult.Idle;
                }

                if (frameAction != null)
                    PostToUi(frameAction);

                return result;
            };

            ticker.Subscribe(subscriber);
            return () => ticker.Unsubscribe(subscriber);
        }

        /// <summary>
        /// Queues a repaint that is safe to request from any thread. Controls
        /// registered on the UI thread are invalidated directly.
        /// </summary>
        internal static void RequestRepaint(Control control)
        {
            ArgumentNullException.ThrowIfNull(control);

            lock (PendingLock)
            {
                PendingInvalidations.Add(control);
            }
        }

        /// <summary>
        /// Runs <paramref name="action"/> on the UI thread. Used by frame-clock
        /// subscribers that must create or destroy windows, since that can only
        /// happen on the thread that owns the message loop.
        /// </summary>
        internal static void Post(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);

            PostToUi(action);
        }

        private static void PostToUi(Action action)
        {
            Control? marshal = _marshal;

            if (marshal == null || marshal.IsDisposed || !marshal.IsHandleCreated)
            {
                // The host has not started, or is shutting down: there is no UI
                // thread to hand the work to, so drop it rather than run UI work
                // on the caller's thread.
                return;
            }

            try
            {
                marshal.BeginInvoke(action);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UiAnimationHost: marshal failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Drains queued repaint requests. Runs on the ticker thread, so the
        /// work is marshalled when the host has a UI context.
        /// </summary>
        private static void FlushInvalidations()
        {
            lock (PendingLock)
            {
                if (PendingInvalidations.Count == 0)
                    return;

                if (Interlocked.CompareExchange(ref _flushScheduled, 1, 0) != 0)
                    return;
            }

            PostToUi(FlushOnUiThread);
        }

        private static void FlushOnUiThread()
        {
            try
            {
                while (true)
                {
                    Control[] batch;

                    lock (PendingLock)
                    {
                        if (PendingInvalidations.Count == 0)
                        {
                            _flushScheduled = 0;
                            return;
                        }

                        batch = PendingInvalidations.ToArray();
                        PendingInvalidations.Clear();
                    }

                    foreach (Control control in batch)
                    {
                        try
                        {
                            if (!control.IsDisposed)
                                control.Invalidate();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"UiAnimationHost: invalidate failed: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _flushScheduled = 0;
                Console.WriteLine($"UiAnimationHost: repaint flush failed: {ex.Message}");
            }
        }

        private static int NormalizeHz(int hz)
        {
            if (hz < 30 || hz > 480)
                return 60;

            return hz;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace nyxWM
{
    /// <summary>
    /// Central frame clock for every animated surface in the process.
    /// One dedicated thread drives all subscribers with a shared delta time,
    /// so overlays never drift apart and the process never runs a timer per
    /// window.
    ///
    /// Timing strategy: the thread sleeps until slightly before the deadline
    /// and busy-waits only for the final fraction of a millisecond. When no
    /// subscriber reports work, the ticker drops to an idle cadence so the
    /// process never burns a core while the desktop is static.
    /// </summary>
    internal sealed class RenderTicker : IDisposable
    {
        /// <summary>What a subscriber reports after being advanced.</summary>
        internal enum TickResult
        {
            /// <summary>The subscriber is static; it will not repaint this frame.</summary>
            Idle,

            /// <summary>The subscriber changed and needs to be repainted.</summary>
            Active
        }

        /// <summary>Upper bound on the spin wait, so a slow frame cannot burn cpu.</summary>
        private const double MaxSpinWindowMs = 0.6;

        /// <summary>Target cadence while every subscriber is static.</summary>
        private const int IdleIntervalMs = 120;

        /// <summary>A pause longer than this means the system slept; the clock restarts.</summary>
        private const double MaxFrameDeltaMs = 250.0;

        private readonly Action<double> _onFrame;
        private readonly List<Func<double, TickResult>> _subscribers = new();
        private readonly Func<int> _targetHzProvider;
        private readonly object _subscriberLock = new();
        private readonly object _lifecycleLock = new();

        private volatile bool _running;
        private bool _disposed;
        private volatile bool _hasActiveSubscribers;
        private Thread? _thread;

        /// <param name="onFrame">Receives the frame delta in milliseconds.</param>
        /// <param name="targetHzProvider">
        /// Supplies the desired tick rate. Read once per frame, so a settings
        /// save can raise or lower the cadence without restarting the ticker.
        /// </param>
        internal RenderTicker(Action<double> onFrame, Func<int> targetHzProvider)
        {
            _onFrame = onFrame ?? throw new ArgumentNullException(nameof(onFrame));
            _targetHzProvider = targetHzProvider ?? throw new ArgumentNullException(nameof(targetHzProvider));
        }

        /// <summary>Runs until <see cref="Stop"/> is called.</summary>
        internal void Start()
        {
            lock (_lifecycleLock)
            {
                if (_running || _disposed)
                    return;

                _running = true;
                _thread = new Thread(RunLoop)
                {
                    IsBackground = true,
                    Name = "nyxWM.RenderTicker",
                    Priority = ThreadPriority.AboveNormal
                };
                _thread.Start();
            }
        }

        /// <summary>Stops the ticker and waits for the thread to leave the loop.</summary>
        internal void Stop()
        {
            Thread? thread;

            lock (_lifecycleLock)
            {
                if (!_running)
                    return;

                _running = false;
                thread = _thread;
                _thread = null;
                Monitor.Pulse(_lifecycleLock);
            }

            thread?.Join();
        }

        /// <summary>
        /// Registers an animation source. The ticker advances every subscriber
        /// in registration order on the ticker thread.
        /// </summary>
        internal void Subscribe(Func<double, TickResult> subscriber)
        {
            ArgumentNullException.ThrowIfNull(subscriber);

            lock (_subscriberLock)
            {
                _subscribers.Add(subscriber);
            }
        }

        internal void Unsubscribe(Func<double, TickResult> subscriber)
        {
            lock (_subscriberLock)
            {
                _subscribers.Remove(subscriber);
                _hasActiveSubscribers = _subscribers.Count > 0;
            }
        }

        /// <summary>
        /// Wakes the ticker at full cadence for the next frames. Idle surfaces
        /// call this when user input arrives outside the tick, so the first
        /// animated frame is not delayed by the idle cadence.
        /// </summary>
        internal void RequestActive()
        {
            _hasActiveSubscribers = true;
            if (Volatile.Read(ref _wakeSignal) == 0)
                Interlocked.Exchange(ref _wakeSignal, 1);
        }

        private int _wakeSignal;

        private void RunLoop()
        {
            var clock = Stopwatch.StartNew();
            double nextTickMs = 0.0;
            double previousFrameMs = 0.0;

            while (_running)
            {
                bool woken = Interlocked.Exchange(ref _wakeSignal, 0) == 1;

                if (woken)
                    nextTickMs = clock.Elapsed.TotalMilliseconds;

                double nowMs = clock.Elapsed.TotalMilliseconds;
                double delayMs = nextTickMs - nowMs;

                if (delayMs > 0.0)
                {
                    if (!SleepUntil(clock, nextTickMs, delayMs))
                        continue;
                }

                double timestampMs = clock.Elapsed.TotalMilliseconds;
                double deltaMs = timestampMs - previousFrameMs;

                if (previousFrameMs > 0.0 && deltaMs > MaxFrameDeltaMs)
                {
                    // The machine slept or the process was frozen: restart the clock
                    // instead of feeding one enormous delta into every animation.
                    deltaMs = 0.0;
                }

                previousFrameMs = timestampMs;
                nextTickMs += CurrentIntervalMs();

                // Idle surfaces are not advanced at all: skipping them is what
                // lets the host park them and drop the busy-wait.
                if (!woken && !_hasActiveSubscribers)
                    continue;

                DispatchFrame(deltaMs);
            }
        }

        /// <summary>
        /// Sleeps most of <paramref name="delayMs"/> and spins the last fraction.
        /// Returns false when the ticker was asked to stop while waiting.
        /// </summary>
        private bool SleepUntil(Stopwatch clock, double deadlineMs, double delayMs)
        {
            double sleepMs = delayMs - MaxSpinWindowMs;

            if (sleepMs > 1.0)
            {
                int sleepWholeMs = (int)sleepMs;

                // A sleep longer than the idle cadence is safe: RequestActive and
                // Stop both pulse the monitor to cut the wait short.
                Monitor.Enter(_lifecycleLock);
                try
                {
                    if (_running)
                        Monitor.Wait(_lifecycleLock, sleepWholeMs);
                }
                finally
                {
                    Monitor.Exit(_lifecycleLock);
                }

                if (!_running)
                    return false;
            }

            while (clock.Elapsed.TotalMilliseconds < deadlineMs)
            {
                if (!_running)
                    return false;

                Thread.SpinWait(40);
            }

            return true;
        }

        private void DispatchFrame(double deltaMs)
        {
            try
            {
                _onFrame(deltaMs);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"RenderTicker: frame callback failed: {ex.Message}");
            }

            if (!_hasActiveSubscribers)
                return;

            Func<double, TickResult>[] snapshot;
            lock (_subscriberLock)
            {
                snapshot = _subscribers.ToArray();
            }

            bool anyActive = false;

            foreach (Func<double, TickResult> subscriber in snapshot)
            {
                try
                {
                    if (subscriber(deltaMs) == TickResult.Active)
                        anyActive = true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"RenderTicker: subscriber failed: {ex.Message}");
                }
            }

            _hasActiveSubscribers = anyActive;
        }

        private int CurrentIntervalMs()
        {
            int hz;

            try
            {
                hz = _targetHzProvider();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"RenderTicker: refresh rate probe failed: {ex.Message}");
                hz = 60;
            }

            if (hz < 30 || hz > 480)
                hz = 60;

            return _hasActiveSubscribers ? Math.Max(1, 1000 / hz) : IdleIntervalMs;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Stop();
        }
    }
}

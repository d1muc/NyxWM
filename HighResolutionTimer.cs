using System;
using System.Threading;
using System.Diagnostics;

namespace nyxWM
{
    /// <summary>
    /// High-resolution timer for achieving precise update rates (e.g., 165 Hz).
    /// Uses a dedicated thread with Stopwatch for accurate timing.
    /// </summary>
    public class HighResolutionTimer : IDisposable
    {
        private readonly Thread _timerThread;
        private readonly Action _callback;
        private readonly int _targetIntervalMs;
        private volatile bool _running;
        private readonly object _lock = new();
        private bool _disposed;

        public HighResolutionTimer(Action callback, int targetIntervalMs)
        {
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
            _targetIntervalMs = Math.Max(1, targetIntervalMs); // Minimum 1ms interval
            _timerThread = new Thread(TimerLoop) { IsBackground = true };
        }

        public void Start()
        {
            lock (_lock)
            {
                if (_running) return;
                _running = true;
                _timerThread.Start();
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (!_running) return;
                _running = false;
                Monitor.Pulse(_lock); // Wake up the thread if it's waiting
            }

            _timerThread.Join(); // Wait for thread to finish
        }

        private void TimerLoop()
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            long nextTick = stopwatch.ElapsedMilliseconds;

            while (_running)
            {
                long now = stopwatch.ElapsedMilliseconds;
                long delay = nextTick - now;

                if (delay > 0)
                {
                    // Sleep for most of the delay, then spin wait for precision
                    int sleepTime = Math.Max(1, (int)(delay - 1)); // Leave 1ms for spin wait
                    Thread.Sleep(sleepTime);

                    // Spin wait for remaining time for precision
                    while (stopwatch.ElapsedMilliseconds < nextTick && _running)
                    {
                        // Busy wait for high precision
                    }
                }

                if (!_running) break;

                try
                {
                    _callback();
                }
                catch (Exception ex)
                {
                    // Log error but continue running
                    Console.WriteLine($"Error in timer callback: {ex}");
                }

                nextTick += _targetIntervalMs;

                // Prevent drift accumulation - if we're too far behind, reset
                if (stopwatch.ElapsedMilliseconds > nextTick + _targetIntervalMs * 2)
                {
                    nextTick = stopwatch.ElapsedMilliseconds;
                }
            }

            stopwatch.Stop();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    Stop();
                }
                _disposed = true;
            }
        }

        ~HighResolutionTimer()
        {
            Dispose(false);
        }
    }
}
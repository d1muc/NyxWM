using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using WinFormsTimer = System.Windows.Forms.Timer;

namespace nyxWM
{
    /// <summary>
    /// Manages notification cards: lifetime, stacking, animation ticks and
    /// language-change detection. All members are expected to be used from
    /// the UI thread.
    /// </summary>
    internal static class NotificationService
    {
        private const int MaxVisibleCards = 4;
        private const float LanguagePollIntervalMs = 250f;
        private const float StartupDelayMs = 700f;

        private const float MarginDip = 20f;
        private const float GapDip = 10f;
        private const float SlideDistanceDip = 22f;

        private static readonly List<NotificationCard> Cards = new();

        private static WinFormsTimer? _timer;
        private static Stopwatch? _clock;
        private static double _lastTickMs;
        private static double _languagePollAccumMs;
        private static float _startupDelayMs;
        private static Func<bool>? _isGameModeActive;
        private static KeyboardLanguageWatcher? _languageWatcher;
        private static NotificationPosition _position = NotificationPosition.TopCenter;
        private static float _dpiScale = 1f;
        private static bool _initialized;

        /// <summary>
        /// Starts the notification system. <paramref name="isGameModeActive"/> is polled on
        /// every tick; while it returns true, notifications are hidden and suppressed.
        /// </summary>
        internal static void Initialize(Func<bool> isGameModeActive)
        {
            if (_initialized)
                return;

            _isGameModeActive = isGameModeActive ?? throw new ArgumentNullException(nameof(isGameModeActive));
            _languageWatcher = new KeyboardLanguageWatcher();
            _clock = Stopwatch.StartNew();
            _lastTickMs = 0.0;
            _languagePollAccumMs = 0.0;
            _startupDelayMs = StartupDelayMs;
            _position = ResolvePosition();
            _dpiScale = GetDpiScale();

            _timer = new WinFormsTimer { Interval = 15 };
            _timer.Tick += OnTick;
            _timer.Start();

            _initialized = true;
        }

        /// <summary>
        /// Stops or resumes the notification ticker. Game mode suspends it so the
        /// UI thread is not woken every 15 ms while a game is running.
        /// </summary>
        internal static void SetSuspended(bool suspended)
        {
            if (!_initialized || _timer == null)
                return;

            if (suspended)
            {
                CloseAllCards();
                _timer.Stop();
                return;
            }

            _startupDelayMs = 0f;
            _lastTickMs = _clock?.Elapsed.TotalMilliseconds ?? 0.0;
            _timer.Start();
        }

        /// <summary>
        /// Queues a notification. Silently ignored when notifications are disabled
        /// in config or when game mode is active.
        /// </summary>
        internal static void Show(string title, string body)
        {
            if (!_initialized || _clock == null)
                return;

            NotificationSettings? settings = ConfigManager.Current.Notifications;
            if (settings == null || !settings.Enabled)
                return;

            if (IsGameModeActive())
                return;

            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(body))
                return;

            _position = ResolvePosition();

            float scale = _dpiScale;
            NotificationTheme theme = NotificationThemeResolver.Resolve(ConfigManager.GetLiveTheme());
            Rectangle work = Screen.PrimaryScreen?.WorkingArea
                             ?? new Rectangle(0, 0, 1920, 1080);
            Size size = NotificationCard.Measure(scale);
            float margin = MarginDip * scale;
            float gap = GapDip * scale;
            float slide = SlideDistanceDip * scale;

            (float targetX, float targetY) = ComputeSlot(work, size, margin, gap, _position, 0);
            float startY = IsTopPosition(_position) ? targetY - slide : targetY + slide;

            var card = new NotificationCard(
                title,
                body,
                settings.DurationMs,
                scale,
                targetX,
                startY,
                targetX,
                targetY,
                theme);

            Cards.Insert(0, card);
            card.Show();
            Reflow();

            TrimExcessCards();
        }

        /// <summary>
        /// Stops the ticker and disposes all cards. Safe to call multiple times.
        /// </summary>
        internal static void Shutdown()
        {
            if (!_initialized)
                return;

            _timer?.Stop();
            if (_timer != null)
            {
                _timer.Tick -= OnTick;
                _timer.Dispose();
            }

            _timer = null;
            CloseAllCards();
            _initialized = false;
            _clock = null;
            _languageWatcher = null;
            _isGameModeActive = null;
        }

        private static void OnTick(object? sender, EventArgs e)
        {
            if (_clock == null)
                return;

            double now = _clock.Elapsed.TotalMilliseconds;
            float dtMs = (float)Math.Clamp(now - _lastTickMs, 0.0, 50.0);
            _lastTickMs = now;

            if (dtMs <= 0f)
                return;

            if (IsGameModeActive())
            {
                _startupDelayMs = 0f;
                CloseAllCards();
                return;
            }

            if (_startupDelayMs > 0f)
            {
                _startupDelayMs -= dtMs;
                if (_startupDelayMs <= 0f)
                {
                    Show("nyxWM", "Running and active");
                }
            }

            _languagePollAccumMs += dtMs;
            if (_languagePollAccumMs >= LanguagePollIntervalMs)
            {
                _languagePollAccumMs = 0.0;
                string? language = _languageWatcher?.Poll();
                if (language != null)
                {
                    Show("Language changed", language);
                }
            }

            if (Cards.Count == 0)
                return;

            for (int i = Cards.Count - 1; i >= 0; i--)
            {
                NotificationCard card = Cards[i];

                if (!card.IsClosing && card.AgeMs >= card.LifetimeMs)
                {
                    card.BeginClose();
                }

                card.Advance(dtMs);

                if (card.Finished)
                {
                    Cards.RemoveAt(i);
                    card.Close();
                    card.Dispose();
                }
            }

            Reflow();
        }

        private static void Reflow()
        {
            if (Cards.Count == 0)
                return;

            float scale = _dpiScale;
            Rectangle work = Screen.PrimaryScreen?.WorkingArea
                             ?? new Rectangle(0, 0, 1920, 1080);
            Size size = NotificationCard.Measure(scale);
            float margin = MarginDip * scale;
            float gap = GapDip * scale;

            int slot = 0;
            foreach (NotificationCard card in Cards)
            {
                // Closing cards keep their slot until they disappear so a fresh card
                // never slides on top of a fading one.
                (float x, float y) = ComputeSlot(work, size, margin, gap, _position, slot);
                card.TargetX = x;
                card.TargetY = y;
                slot++;
            }
        }

        private static (float X, float Y) ComputeSlot(
            Rectangle work,
            Size size,
            float margin,
            float gap,
            NotificationPosition position,
            int slot)
        {
            float x = position switch
            {
                NotificationPosition.TopLeft or NotificationPosition.BottomLeft
                    => work.Left + margin,
                NotificationPosition.TopRight or NotificationPosition.BottomRight
                    => work.Right - size.Width - margin,
                _ => work.Left + (work.Width - size.Width) / 2f
            };

            float stride = size.Height + gap;
            float y = IsTopPosition(position)
                ? work.Top + margin + slot * stride
                : work.Bottom - margin - size.Height - slot * stride;

            return (x, y);
        }

        private static bool IsTopPosition(NotificationPosition position)
        {
            return position is NotificationPosition.TopCenter
                or NotificationPosition.TopLeft
                or NotificationPosition.TopRight;
        }

        private static NotificationPosition ResolvePosition()
        {
            NotificationSettings? settings = ConfigManager.Current.Notifications;
            return NotificationPositionParser.Parse(settings?.Position);
        }

        private static void TrimExcessCards()
        {
            int activeCount = 0;
            foreach (NotificationCard card in Cards)
            {
                if (!card.IsClosing)
                    activeCount++;
            }

            for (int i = Cards.Count - 1; i >= 0 && activeCount > MaxVisibleCards; i--)
            {
                if (Cards[i].IsClosing)
                    continue;

                Cards[i].BeginClose();
                activeCount--;
            }
        }

        private static void CloseAllCards()
        {
            foreach (NotificationCard card in Cards)
            {
                card.Close();
                card.Dispose();
            }

            Cards.Clear();
        }

        private static bool IsGameModeActive()
        {
            try
            {
                return _isGameModeActive?.Invoke() ?? false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"NotificationService: game mode probe failed: {ex.Message}");
                return false;
            }
        }

        private static float GetDpiScale()
        {
            try
            {
                using Graphics graphics = Graphics.FromHwnd(IntPtr.Zero);
                return Math.Clamp(graphics.DpiX / 96f, 1f, 3f);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"NotificationService: DPI probe failed: {ex.Message}");
                return 1f;
            }
        }
    }
}

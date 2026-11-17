using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace nyxWM
{
    /// <summary>
    /// Manages notification cards: lifetime, stacking, frame ticks and
    /// language-change detection. Cards advance on the shared frame clock.
    /// All members are expected to be used from the UI thread.
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

        private static Action? _unsubscribe;
        private static int _tickInFlight;
        private static double _languagePollAccumMs;
        private static float _startupDelayMs;
        private static Func<bool>? _isGameModeActive;
        private static KeyboardLanguageWatcher? _languageWatcher;
        private static NotificationPosition _position = NotificationPosition.TopCenter;
        private static float _dpiScale = 1f;
        private static bool _initialized;

        /// <summary>Raised on the UI thread when a keyboard-layout change is detected.</summary>
        internal static event Action? LayoutSwitched;

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
            _languagePollAccumMs = 0.0;
            _startupDelayMs = StartupDelayMs;
            _position = ResolvePosition();
            _dpiScale = GetDpiScale();

            _unsubscribe = UiAnimationHost.Subscribe(
                dtMs => AdvanceNotifications(dtMs),
                () => { });

            _initialized = true;
        }

        /// <summary>
        /// Suspends lifetime counting while game mode is active: cards are dropped
        /// and the frame clock stops seeing this service as a subscriber.
        /// </summary>
        internal static void SetSuspended(bool suspended)
        {
            if (!_initialized)
                return;

            if (suspended)
            {
                CloseAllCards();
                Unsubscribe();
                return;
            }

            _startupDelayMs = 0f;

            if (_unsubscribe == null)
            {
                _unsubscribe = UiAnimationHost.Subscribe(
                    dtMs => AdvanceNotifications(dtMs),
                    () => { });
            }
        }

        /// <summary>
        /// Queues a notification. Silently ignored when notifications are disabled
        /// in config or when game mode is active.
        /// </summary>
        internal static void Show(string title, string body)
        {
            if (!_initialized)
                return;

            NotificationSettings? settings = ConfigManager.Current.Notifications;
            if (settings == null || !settings.Enabled)
                return;

            if (IsGameModeActive())
                return;

            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(body))
                return;

            _position = ResolvePosition();
            _dpiScale = GetDpiScale();

            float scale = _dpiScale;
            NotificationTheme theme = NotificationThemeResolver.Resolve(
                ConfigManager.GetLiveTheme(),
                ConfigManager.Current.ThemeMode);
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

            // Cards are created either on the UI thread or from a posted frame
            // callback, so the window handle is always available here.
            card.Show();
            Reflow();

            TrimExcessCards();

            // A card that appears while the workspace idles must animate at once.
            UiAnimationHost.RequestActivity();
        }

        /// <summary>
        /// Stops the ticker and disposes all cards. Safe to call multiple times.
        /// </summary>
        internal static void Shutdown()
        {
            if (!_initialized)
                return;

            Unsubscribe();
            CloseAllCards();
            _initialized = false;
            _languageWatcher = null;
            _isGameModeActive = null;
        }

        /// <summary>
        /// Advances card lifetimes and language polling on the frame clock.
        /// Returns Active while a card is still animating so the host keeps the
        /// full cadence; repaints are posted to the UI thread separately.
        /// </summary>
        private static RenderTicker.TickResult AdvanceNotifications(double dtMs)
        {
            if (Interlocked.Exchange(ref _tickInFlight, 1) == 1)
                return RenderTicker.TickResult.Idle;

            try
            {
                if (dtMs <= 0.0)
                    return RenderTicker.TickResult.Idle;

                if (IsGameModeActive())
                {
                    _startupDelayMs = 0f;
                    UiAnimationHost.Post(CloseAllCards);
                    return RenderTicker.TickResult.Idle;
                }

                if (_startupDelayMs > 0f)
                {
                    _startupDelayMs -= (float)dtMs;
                    if (_startupDelayMs <= 0f)
                    {
                        UiAnimationHost.Post(() => Show("nyxWM", "Running and active"));
                    }
                }

                _languagePollAccumMs += dtMs;
                if (_languagePollAccumMs >= LanguagePollIntervalMs)
                {
                    _languagePollAccumMs = 0.0;
                    string? language = _languageWatcher?.Poll();
                    if (language != null)
                    {
                        UiAnimationHost.Post(() => Show("Language changed", language));
                        LayoutSwitched?.Invoke();
                    }
                }

                NotificationCard[] snapshot = Cards.ToArray();
                if (snapshot.Length == 0)
                    return RenderTicker.TickResult.Idle;

                bool anyActive = false;

                foreach (NotificationCard card in snapshot)
                {
                    if (card.IsDisposed)
                        continue;

                    if (!card.IsClosing && card.AgeMs >= card.LifetimeMs)
                    {
                        card.BeginClose();
                    }

                    // The card owns its window, so a closing card is disposed by
                    // the UI thread once it has faded out.
                    card.Advance((float)dtMs);

                    if (card.Finished)
                        UiAnimationHost.Post(() => RemoveCard(card));

                    if (!card.Finished)
                    {
                        anyActive = true;
                    }
                }

                return anyActive
                    ? RenderTicker.TickResult.Active
                    : RenderTicker.TickResult.Idle;
            }
            finally
            {
                Interlocked.Exchange(ref _tickInFlight, 0);
            }
        }

        /// <summary>
        /// Disposes a card that finished its exit fade, then re-stacks the rest.
        /// Runs on the UI thread.
        /// </summary>
        private static void RemoveCard(NotificationCard card)
        {
            try
            {
                if (!Cards.Remove(card))
                    return;

                card.Close();
                card.Dispose();
                Reflow();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"NotificationService: card removal failed: {ex.Message}");
            }
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

        /// <summary>Detaches this service from the shared frame clock.</summary>
        private static void Unsubscribe()
        {
            Action? unsubscribe = Interlocked.Exchange(ref _unsubscribe, null);
            _unsubscribe = null;

            try
            {
                unsubscribe?.Invoke();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"NotificationService: unsubscribe failed: {ex.Message}");
            }
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
                return Math.Clamp(graphics.DpiX / 96f, 1f, 3f) * VisualPreferences.UiScale;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"NotificationService: DPI probe failed: {ex.Message}");
                return VisualPreferences.UiScale;
            }
        }
    }
}

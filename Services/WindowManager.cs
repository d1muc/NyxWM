using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using nyxWM;
using System.Windows.Forms; // Added for Screen
using System.Threading; // Added for SynchronizationContext
using System.Diagnostics; // Added for Stopwatch

namespace nyxWM
{
    public class WindowManager
    {
        // Flags for SetWindowPos (same as in WinApi.cs but we define here for simplicity)
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FLAGS = SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE;

        // Immutable snapshot of the window handles and their base positions used by the render
        // thread. The arrays are republished (never mutated) so the timer thread can iterate
        // them without blocking the UI thread on WinAPI calls.
        private IntPtr[] _renderHandles = Array.Empty<IntPtr>();
        private WinApi.RECT[] _renderRects = Array.Empty<WinApi.RECT>();
        private int _renderCount;
        private readonly object _positionsLock = new();

        // Cached top-level window handles. Rebuilt lazily instead of enumerating the whole
        // desktop on every focus change or drag start.
        private const int InitialCacheCapacity = 64;
        private readonly List<IntPtr> cachedWindows = new(InitialCacheCapacity);
        private readonly object _cachedWindowsLock = new();
        private volatile bool _cacheDirty = true;

        // Last integer offset applied to the windows (avoids redundant WinAPI batches)
        private int _lastAppliedDx = int.MinValue;
        private int _lastAppliedDy = int.MinValue;

        // Reused string buffers for window enumeration (avoids per-window allocations)
        private readonly StringBuilder _classNameBuffer = new(256);
        private readonly StringBuilder _debugTextBuffer = new(256);

        // Offset (in pixels) below which the animation snaps to the target and goes idle
        private const float SettledThreshold = 0.5f;

        // Mouse start position (at drag start)
        private int startMouseX;
        private int startMouseY;

        // Lerp fields
        private float currentDx = 0, currentDy = 0;   // current window offset
        private float targetDx = 0, targetDy = 0;     // target offset from mouse movement
        private bool isDragging = false;

        // Dirty flag: true only while windows need to move. Lets UpdatePosition return
        // immediately when the workspace is at rest.
        private volatile bool _isAnimating;

        // Frame timing
        private long _lastUpdateTicks = 0;
        private readonly Stopwatch _stopwatch = new Stopwatch();

        // Monitor refresh rate
        private int _monitorRefreshRate = 165; // Default fallback
        private float _targetDeltaTime => 1000f / _monitorRefreshRate; // Target time per frame in ms

        // Lerp tuning - how quickly we catch up to target (per second)
        private readonly float _lerpRatePerSecond;

        // Auto-center on focus flag
        public bool AutoCenterOnFocus { get; set; }

        /// <summary>
        /// Gets whether the window manager is paused (game mode).
        /// </summary>
        public bool IsPaused => _isPaused;

        // WinEvent hook fields
        private WinApi.WinEventProc _winEventProcDelegate;
        private IntPtr _winEventHookHandle;
        private WinApi.WinEventProc _winEventObjectProcDelegate;
        private IntPtr _winEventCreateHookHandle;
        private IntPtr _winEventDestroyHookHandle;
        private SynchronizationContext? _syncContext;

        // Anti-cheat pause fields
        private readonly MouseHook _mouseHook;
        private volatile bool _isPaused;

        /// <summary>
        /// Constructor: sets up the WinEvent hook for foreground window changes.
        /// </summary>
        public WindowManager(MouseHook mouseHook)
        {
            _mouseHook = mouseHook ?? throw new ArgumentNullException(nameof(mouseHook));
            _isPaused = false;

            // Ensure a synchronization context exists for the current thread
            if (SynchronizationContext.Current == null)
            {
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            }
            // Capture the synchronization context from the UI thread
            _syncContext = SynchronizationContext.Current;

            // Get monitor refresh rate with fallback from config
            _monitorRefreshRate = MonitorUtils.GetPrimaryMonitorRefreshRate(ConfigManager.Current.FallbackRefreshRate);
            Console.WriteLine($"Detected monitor refresh rate: {_monitorRefreshRate} Hz");

            // Get lerp rate from config
            _lerpRatePerSecond = ConfigManager.Current.LerpRatePerSecond;

            // Set auto-center-on-focus default from config
            AutoCenterOnFocus = ConfigManager.Current.AutoCenterOnFocusDefault;

            // Initialize stopwatch for frame timing
            _stopwatch.Start();
            _lastUpdateTicks = _stopwatch.ElapsedTicks;

            // Set up the WinEvent delegate
            _winEventProcDelegate = new WinApi.WinEventProc(WinEventProcCallback);

            // Install the hook for EVENT_SYSTEM_FOREGROUND
            _winEventHookHandle = WinApi.SetWinEventHook(
                WinApi.EVENT_SYSTEM_FOREGROUND,
                WinApi.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero,
                _winEventProcDelegate,
                0,
                0,
                WinApi.WINEVENT_OUTOFCONTEXT | WinApi.WINEVENT_SKIPOWNPROCESS);

            // Track window creation/destruction so the cached handle list stays current
            // without re-enumerating the whole desktop.
            _winEventObjectProcDelegate = new WinApi.WinEventProc(WinEventObjectProcCallback);

            _winEventCreateHookHandle = WinApi.SetWinEventHook(
                WinApi.EVENT_OBJECT_CREATE,
                WinApi.EVENT_OBJECT_CREATE,
                IntPtr.Zero,
                _winEventObjectProcDelegate,
                0,
                0,
                WinApi.WINEVENT_OUTOFCONTEXT | WinApi.WINEVENT_SKIPOWNPROCESS);

            _winEventDestroyHookHandle = WinApi.SetWinEventHook(
                WinApi.EVENT_OBJECT_DESTROY,
                WinApi.EVENT_OBJECT_DESTROY,
                IntPtr.Zero,
                _winEventObjectProcDelegate,
                0,
                0,
                WinApi.WINEVENT_OUTOFCONTEXT | WinApi.WINEVENT_SKIPOWNPROCESS);
        }

        /// <summary>
        /// Destructor: unhooks the WinEvent hook.
        /// </summary>
        ~WindowManager()
        {
            if (_winEventHookHandle != IntPtr.Zero)
            {
                WinApi.UnhookWinEvent(_winEventHookHandle);
                _winEventHookHandle = IntPtr.Zero;
            }

            if (_winEventCreateHookHandle != IntPtr.Zero)
            {
                WinApi.UnhookWinEvent(_winEventCreateHookHandle);
                _winEventCreateHookHandle = IntPtr.Zero;
            }

            if (_winEventDestroyHookHandle != IntPtr.Zero)
            {
                WinApi.UnhookWinEvent(_winEventDestroyHookHandle);
                _winEventDestroyHookHandle = IntPtr.Zero;
            }
        }


        /// <summary>
        /// Gets the executable file path for the process that owns the given window.
        /// Returns an empty string if the path cannot be determined.
        /// </summary>
        private string GetExePathFromWindow(IntPtr hWnd)
        {
            WinApi.GetWindowThreadProcessId(hWnd, out uint processId);
            try
            {
                var process = Process.GetProcessById((int)processId);
                return process.MainModule?.FileName ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Callback for WinEvent hook. Marshals to the UI thread.
        /// </summary>
        private void WinEventProcCallback(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            // Debug output
            Console.WriteLine($"WinEventProcCallback: hwnd={hwnd.ToInt64():X} eventType={eventType}");

            // Marshal to the UI thread
            if (_syncContext != null)
            {
                _syncContext.Post(state => HandleFocusChange(hwnd), null);
            }
        }

        /// <summary>
        /// Callback for WinEvent create/destroy notifications. Keeps the cached window handle
        /// list current without a full EnumWindows sweep.
        /// </summary>
        private void WinEventObjectProcCallback(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            // Only top-level window objects are managed.
            if (hwnd == IntPtr.Zero || idObject != WinApi.OBJID_WINDOW || idChild != WinApi.CHILDID_SELF)
                return;

            if (WinApi.GetAncestor(hwnd, WinApi.GA_ROOT) != hwnd)
                return;

            if (eventType == WinApi.EVENT_OBJECT_DESTROY)
            {
                lock (_cachedWindowsLock)
                {
                    cachedWindows.Remove(hwnd);
                }
                return;
            }

            // A window is usually created hidden and becomes visible later, so add it right
            // away only when it already qualifies; otherwise rebuild lazily on next use.
            if (IsCacheEligibleWindow(hwnd, _classNameBuffer))
            {
                lock (_cachedWindowsLock)
                {
                    if (!cachedWindows.Contains(hwnd))
                    {
                        cachedWindows.Add(hwnd);
                    }
                }
            }
            else
            {
                _cacheDirty = true;
            }
        }

        /// <summary>
        /// Checks whether a window should be part of the managed workspace.
        /// The caller supplies the class-name buffer so no per-window allocation happens.
        /// </summary>
        private bool IsCacheEligibleWindow(IntPtr hWnd, StringBuilder classNameBuffer)
        {
            if (!WinApi.IsWindowVisible(hWnd))
                return false;

            if (WinApi.IsIconic(hWnd))
                return false;

            if (WinApi.GetWindowTextLength(hWnd) == 0)
                return false;

            WinApi.GetClassName(hWnd, classNameBuffer, classNameBuffer.Capacity);
            return !IsIgnoredWindow(classNameBuffer.ToString());
        }

        /// <summary>
        /// Rebuilds the cached window handle list if it was invalidated by window creation.
        /// </summary>
        private void EnsureWindowCache()
        {
            if (!_cacheDirty)
                return;

            RebuildWindowCache();
        }

        /// <summary>
        /// Performs a single full desktop enumeration and replaces the cached handle list.
        /// </summary>
        private void RebuildWindowCache()
        {
            var freshHandles = new List<IntPtr>(InitialCacheCapacity);
            // One buffer per rebuild (not per window) keeps the sweep allocation-free.
            var classNameBuffer = new StringBuilder(256);

            WinApi.EnumWindows((hWnd, lParam) =>
            {
                if (IsCacheEligibleWindow(hWnd, classNameBuffer))
                {
                    freshHandles.Add(hWnd);
                }
                return true;
            }, IntPtr.Zero);

            lock (_cachedWindowsLock)
            {
                cachedWindows.Clear();
                cachedWindows.AddRange(freshHandles);
            }

            _cacheDirty = false;
        }

        /// <summary>
        /// Publishes a new render snapshot from the cached window handles. Called from the UI
        /// thread; the timer thread reads the previous snapshot until the swap completes.
        /// </summary>
        private void RefreshRenderCache()
        {
            EnsureWindowCache();

            IntPtr[] handles = Array.Empty<IntPtr>();
            WinApi.RECT[] rects = Array.Empty<WinApi.RECT>();
            int count = 0;

            lock (_cachedWindowsLock)
            {
                // Sized inside the lock: the handle list can change between ticks.
                int capacity = Math.Max(InitialCacheCapacity, cachedWindows.Count);
                handles = new IntPtr[capacity];
                rects = new WinApi.RECT[capacity];

                foreach (IntPtr hWnd in cachedWindows)
                {
                    // Visibility/minimized state can change without a cache invalidation.
                    if (!WinApi.IsWindowVisible(hWnd) || WinApi.IsIconic(hWnd))
                        continue;

                    if (!WinApi.GetWindowRect(hWnd, out WinApi.RECT rect))
                        continue;

                    // Skip windows with invalid coordinates (far off-screen)
                    if (rect.Left < -10000 || rect.Top < -10000)
                        continue;

                    handles[count] = hWnd;
                    rects[count] = rect;
                    count++;
                }
            }

            lock (_positionsLock)
            {
                _renderHandles = handles;
                _renderRects = rects;
                _renderCount = count;
            }
        }

        /// <summary>
        /// Handles the foreground window change event on the UI thread.
        /// </summary>
        /// <param name="hwnd">The handle of the window that gained focus.</param>
        private void HandleFocusChange(IntPtr hwnd)
        {
            // Debug output
            int debugTitleLen = WinApi.GetWindowTextLength(hwnd);
            if (debugTitleLen > 0)
            {
                if (_debugTextBuffer.Capacity < debugTitleLen + 1)
                {
                    _debugTextBuffer.Capacity = debugTitleLen + 1;
                }
                WinApi.GetWindowText(hwnd, _debugTextBuffer, _debugTextBuffer.Capacity);
                Console.WriteLine($"HandleFocusChange: hwnd={hwnd.ToInt64():X} title=\"{_debugTextBuffer}\"");
            }
            else
            {
                Console.WriteLine($"HandleFocusChange: hwnd={hwnd.ToInt64():X} (no title)");
            }

            // If auto-center is disabled or we are currently dragging, do nothing.
            if (!AutoCenterOnFocus || isDragging)
                return;

            // Check window validity: visible, not minimized, has a title, and not a system window.
            if (!WinApi.IsWindowVisible(hwnd))
                return;

            if (WinApi.IsIconic(hwnd))
                return;

            int titleLen = WinApi.GetWindowTextLength(hwnd);
            if (titleLen == 0)
                return;

            WinApi.GetClassName(hwnd, _classNameBuffer, _classNameBuffer.Capacity);
            string className = _classNameBuffer.ToString();

            // Skip ignored windows
            if (IsIgnoredWindow(className))
            {
                return;
            }

            // NEW: Pause on fullscreen logic for anti-cheat
            if (ConfigManager.Current.PauseOnFullscreen)
            {
                // Get the executable path
                string exePath = GetExePathFromWindow(hwnd);
                // Exclude our own process
                uint currentProcessId = (uint)Environment.ProcessId;
                WinApi.GetWindowThreadProcessId(hwnd, out uint windowProcessId);
                if (windowProcessId == currentProcessId)
                {
                    // Skip pause for our own process
                }
                else
                {
                    // Check if window is large and borderless
                    Rectangle screenBounds = Screen.PrimaryScreen!.Bounds;
                    if (WinApi.GetWindowRect(hwnd, out WinApi.RECT windowRect))
                    {
                        // Check if window covers >= 90% of screen area
                        int screenArea = screenBounds.Width * screenBounds.Height;
                        int windowArea = (windowRect.Right - windowRect.Left) * (windowRect.Bottom - windowRect.Top);
                        bool largeWindow = screenArea > 0 && ((float)windowArea / screenArea) >= 0.90f;

                        // Check for borderless: no title bar (WS_CAPTION == 0)
                        int style = WinApi.GetWindowLong(hwnd, WinApi.GWL_STYLE);
                        bool hasCaption = (style & WinApi.WS_CAPTION) != 0;

                        int gameScore = GameCacheManager.ComputeGameScore(hwnd, exePath);

                        bool shouldPause = (largeWindow && !hasCaption) || gameScore > 40;

                        if (shouldPause)
                        {
                            // Activate pause
                            if (!_isPaused)
                            {
                                _isPaused = true;
                                _mouseHook?.Stop();
                                // Automatically cache the game for future detection
                                if (!string.IsNullOrEmpty(exePath))
                                {
                                    GameCacheManager.AddKnownGame(exePath);
                                }
                            }
                        }
                        else
                        {
                            // Deactivate pause if we were paused
                            if (_isPaused)
                            {
                                _isPaused = false;
                                _mouseHook?.Start();
                            }
                        }
                    }
                    else
                    {
                        // Failed to get window rect: ensure unpause
                        if (_isPaused)
                        {
                            _isPaused = false;
                            _mouseHook?.Start();
                        }
                    }
                }
            }

            // Refresh the positions of the managed windows from the cached handle list
            // (a full EnumWindows sweep only happens when the cache was invalidated).
            RefreshRenderCache();

            // Get the rectangle of the focused window
            if (!WinApi.GetWindowRect(hwnd, out WinApi.RECT rect))
                return;

            // Calculate the center of the focused window
            int winCenterX = (rect.Left + rect.Right) / 2;
            int winCenterY = (rect.Top + rect.Bottom) / 2;

            // Get the center of the primary screen
            Rectangle primaryScreenBounds = Screen.PrimaryScreen!.Bounds;
            int screenCenterX = primaryScreenBounds.X + primaryScreenBounds.Width / 2;
            int screenCenterY = primaryScreenBounds.Y + primaryScreenBounds.Height / 2;

            // Calculate delta to center the window on the screen
            int deltaX = screenCenterX - winCenterX;
            int deltaY = screenCenterY - winCenterY;

            // Reset current offsets and set target to move to center
            currentDx = 0;
            currentDy = 0;
            targetDx = deltaX;
            targetDy = deltaY;
            _isAnimating = true;
        }


        /// <summary>
        /// Checks if a window class name is in the ignored list.
        /// </summary>
        private bool IsIgnoredWindow(string className)
        {
            foreach (var ignored in ConfigManager.Current.IgnoredWindowClassNames)
            {
                if (string.Equals(className, ignored, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Called when drag starts: records mouse position, resets Lerp variables, and caches initial window positions.
        /// </summary>
        public void StartDragging()
        {
            // Get current cursor position
            WinApi.GetCursorPos(out WinApi.POINT pt);
            startMouseX = pt.X;
            startMouseY = pt.Y;
            isDragging = true;

            // Reset Lerp variables
            currentDx = 0;
            currentDy = 0;
            targetDx = 0;
            targetDy = 0;

            // Cache the current positions of the managed windows. The handle list itself is
            // reused, so no EnumWindows sweep unless windows were created since the last scan.
            RefreshRenderCache();

            _isAnimating = true;
        }

        /// <summary>
        /// Called when drag stops: stops dragging but leaves Lerp to continue to settle.
        /// </summary>
        public void StopDragging()
        {
            isDragging = false;
        }

        /// <summary>
        /// Toggles the auto-center-on-focus feature.
        /// </>
        public void ToggleAutoCenter()
        {
            AutoCenterOnFocus = !AutoCenterOnFocus;
            Console.WriteLine($"AutoCenterOnFocus is now: {AutoCenterOnFocus}");
        }

        /// <summary>
        /// Activates the game mode (pause) state for anti-cheat purposes.
        /// This works independently of the automatic fullscreen detection.
        /// Note: despite the name, this method only activates the pause and does not toggle.
        /// To deactivate, rely on automatic focus change handling.
        /// </summary>
        public void ToggleGameMode()
        {
            if (!_isPaused)
            {
                _isPaused = true;
                _mouseHook?.SetEnabled(false);
                Console.WriteLine("Game mode activated (paused).");
            }
            // If already paused, do nothing.
        }

        /// <summary>
        /// Resets the camera to the canvas origin (0,0) offset.
        /// Sets the target offset to zero, allowing the current offset to smoothly
        /// chase zero via existing Lerp smoothing in UpdatePosition.
        /// Does not modify initialWindowPositions.
        /// </summary>
        public void ResetCamera()
        {
            targetDx = 0;
            targetDy = 0;
            _isAnimating = true;
        }

        /// <summary>
        /// Sets the pause state and manages the mouse hook accordingly.
        /// When paused, disables filtering but keeps the hook active.
        /// When unpaused, enables filtering.
        /// </summary>
        /// <param name="pause">True to pause, false to unpause</param>
        public void SetPause(bool pause)
        {
            _isPaused = pause;
            _mouseHook?.SetEnabled(!pause);
        }

        /// <summary>
        /// Closes the active (foreground) window by sending a WM_CLOSE message.
        /// </summary>
        public void CloseActiveWindow()
        {
            IntPtr foregroundWnd = WinApi.GetForegroundWindow();
            if (foregroundWnd != IntPtr.Zero)
            {
                // Send WM_CLOSE message (0x0010)
                WinApi.SendMessage(foregroundWnd, 0x0010, IntPtr.Zero, IntPtr.Zero);
            }
        }

        /// <summary>
        /// Centers the active window on the screen smoothly via Lerp ticker, mimicking automatic focus centering.
        /// </summary>
        public void CenterActiveWindow()
        {
            IntPtr hWnd = WinApi.GetForegroundWindow();
            if (hWnd == IntPtr.Zero)
                return;

            // Get the rectangle of the focused window
            if (!WinApi.GetWindowRect(hWnd, out WinApi.RECT rect))
                return;

            // Calculate the center of the focused window
            int winCenterX = (rect.Left + rect.Right) / 2;
            int winCenterY = (rect.Top + rect.Bottom) / 2;

            // Get the center of the primary screen
            Rectangle primaryScreenBounds = Screen.PrimaryScreen!.Bounds;
            int screenCenterX = primaryScreenBounds.X + primaryScreenBounds.Width / 2;
            int screenCenterY = primaryScreenBounds.Y + primaryScreenBounds.Height / 2;

            // Calculate delta to center the window on the screen
            int deltaX = screenCenterX - winCenterX;
            int deltaY = screenCenterY - winCenterY;

            // Set target to current offset plus the delta needed to center
            targetDx = currentDx + deltaX;
            targetDy = currentDy + deltaY;
            _isAnimating = true;
        }

        /// <summary>
        /// Called on each timer tick: updates window positions based on mouse movement with Lerp smoothing.
        /// </summary>
        /// <param name="deltaTime">Time elapsed since last update in milliseconds</param>
        public void UpdatePosition(float deltaTime)
        {
            // If paused, skip updating window positions to avoid interfering with games
            if (_isPaused)
                return;

            // Idle fast-path: nothing is moving, so the timer tick costs almost nothing
            if (!_isAnimating && !isDragging)
                return;

            // If dragging, update target based on mouse movement and warp cursor
            if (isDragging)
            {
                WinApi.GetCursorPos(out WinApi.POINT pt);
                int stepX = pt.X - startMouseX;
                int stepY = pt.Y - startMouseY;

                // Apply camera sensitivity
                stepX = (int)(stepX * ConfigManager.Current.CameraSensitivity);
                stepY = (int)(stepY * ConfigManager.Current.CameraSensitivity);

                // Normal movement: windows follow mouse
                targetDx += stepX;
                targetDy += stepY;

                // Warp cursor back to start position to allow infinite dragging
                WinApi.SetCursorPos(startMouseX, startMouseY);

                _isAnimating = true;
            }

            // Apply frame-rate independent Lerp smoothing: current chases target
            // Convert LerpRatePerSecond to a per-millisecond rate, then calculate factor for this deltaTime
            float lerpFactor = 1.0f - MathF.Exp(-_lerpRatePerSecond * deltaTime / 1000.0f);
            currentDx += (targetDx - currentDx) * lerpFactor;
            currentDy += (targetDy - currentDy) * lerpFactor;

            // Snap to the target when close enough: stops sub-pixel micro-movements and lets
            // the animation go idle.
            bool settled = !isDragging &&
                           MathF.Abs(targetDx - currentDx) < SettledThreshold &&
                           MathF.Abs(targetDy - currentDy) < SettledThreshold;

            if (settled)
            {
                currentDx = targetDx;
                currentDy = targetDy;
            }

            // Convert to integer offset for window movement
            int dx = (int)MathF.Round(currentDx);
            int dy = (int)MathF.Round(currentDy);

            // Nothing moved on screen since the previous tick - skip the WinAPI batch
            if (dx == _lastAppliedDx && dy == _lastAppliedDy)
            {
                if (settled)
                    _isAnimating = false;
                return;
            }

            // Snapshot the published render cache (never mutated after publication)
            IntPtr[] handles;
            WinApi.RECT[] rects;
            int count;
            lock (_positionsLock)
            {
                handles = _renderHandles;
                rects = _renderRects;
                count = _renderCount;
            }

            if (count == 0)
            {
                _lastAppliedDx = dx;
                _lastAppliedDy = dy;
                if (settled)
                    _isAnimating = false;
                return;
            }

            // Begin deferred window position session (with a small margin so Windows does not
            // have to reallocate the internal structure).
            IntPtr hdwp = WinApi.BeginDeferWindowPos(count + 5);
            if (hdwp == IntPtr.Zero)
                return;

            bool positionedAll = true;
            for (int i = 0; i < count; i++)
            {
                IntPtr hWnd = handles[i];
                WinApi.RECT rect = rects[i];

                int newX = rect.Left + dx;   // using PLUS as per instruction (inversion achieved via negative target accumulation)
                int newY = rect.Top + dy;

                // Defer the window position change
                hdwp = WinApi.DeferWindowPos(hdwp, hWnd, IntPtr.Zero, newX, newY, 0, 0, SWP_FLAGS);
                if (hdwp == IntPtr.Zero)
                {
                    positionedAll = false;
                    break;
                }
            }

            // EndDeferWindowPos consumes (and frees) the handle, even on a partial batch.
            WinApi.EndDeferWindowPos(hdwp);

            if (!positionedAll)
                return;

            _lastAppliedDx = dx;
            _lastAppliedDy = dy;

            if (settled)
                _isAnimating = false;
        }
    }
}
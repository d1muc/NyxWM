using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Diagnostics;
using nyxWM;

namespace nyxWM
{
    internal class Program
    {
        // Hotkey constants
        private const int MOD_CONTROL = 0x0002;
        private const int VK_R = 0x52;
        private const int VK_T = 0x54;
        internal const int HOTKEY_ID_CLOSE_WINDOW = 0x0003;
        internal const int HOTKEY_ID_CENTER_WINDOW = 0x0004;
        internal const int HOTKEY_ID_GAME_MODE = 0x0005;
        internal const int HOTKEY_ID_LAUNCHER = 0x0006;
        internal const int HOTKEY_ID_ROFI_MENU = 0x0007;
        internal const int HOTKEY_ID_LAUNCH_APP_BASE = 0x1000;
        internal const int WM_HOTKEY = 0x0312;

        // Track registered hotkey IDs for proper cleanup
        private static readonly List<int> RegisteredHotkeyIds = new();

        // Maps a launch-app hotkey ID to the program/document path it should open.
        internal static readonly Dictionary<int, string> LaunchAppPaths = new();

        // Hotkey functions
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [STAThread]
        static void Main(string[] args)
        {
                        // Raw input is the primary movement source: it bypasses the system pointer
            // acceleration curve and the cursor warp. WindowManager falls back to cursor
            // polling on its own when registration failed.
            using var rawMouse = new RawMouseInput(RawMouseInput.ReadCursorSpeedNormalization());

            // Initialize mouse hook and window manager
            var mouseHook = new MouseHook(ConfigManager.Current.CameraMoveButton, ConfigManager.Current.MainMod);
            var windowManager = new WindowManager(mouseHook, rawMouse);

            // Notifications are suppressed while game mode (pause) is active.
            NotificationService.Initialize(() => windowManager.IsPaused);
            LauncherService.Initialize(() => windowManager.IsPaused);

            // The settings window freezes camera panning while it is open, like a
            // lightweight game mode: the hooks stay installed so the UI still works.
            SettingsWindowService.Initialize(windowManager.SetCameraSuspended);

            // The Rofi menu freezes camera panning for the same reason.
            RofiMenuService.Initialize(windowManager.SetCameraSuspended);

            // Get monitor refresh rate for timer setup
            int fallbackRefreshRate = ConfigManager.Current.FallbackRefreshRate;
            int refreshRate = MonitorUtils.GetPrimaryMonitorRefreshRate(fallbackRefreshRate);
            int intervalMs = Math.Max(1, (int)(1000.0 / refreshRate)); // Calculate interval for target refresh rate

            // Declare renderTimer as nullable to be assigned later
            HighResolutionTimer? renderTimer = null;

            string IconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Res", "Pictures", "logo.ico");

            // Set up system tray icon
            var notifyIcon = new NotifyIcon
            {
                Icon = File.Exists(IconPath) ? new System.Drawing.Icon(IconPath) : SystemIcons.Application,
                Visible = true
            };
            var contextMenu = new ContextMenuStrip();
            var statusItem = new ToolStripMenuItem
            {
                Text = "Status: Running"
            };
            var toggleGameModeItem = new ToolStripMenuItem
            {
                Text = "Toggle Game Mode"
            };
            var exitItem = new ToolStripMenuItem
            {
                Text = "Exit"
            };
            contextMenu.Items.AddRange(new ToolStripItem[] { statusItem, toggleGameModeItem, exitItem });
            notifyIcon.ContextMenuStrip = contextMenu;

            // Event handlers for menu items
            statusItem.Click += (sender, e) =>
            {
                // Toggle pause state and update status text
                bool newState = !windowManager.IsPaused;
                windowManager.SetPause(newState);
                statusItem.Text = newState ? "Status: Paused" : "Status: Running";
            };
            toggleGameModeItem.Click += (sender, e) =>
            {
                windowManager.ToggleGameMode();
                // Update status text to reflect game mode state
                statusItem.Text = windowManager.IsPaused ? "Status: Paused" : "Status: Running";
            };
            exitItem.Click += (sender, e) =>
            {
                // Clean up and exit
                notifyIcon.Visible = false;
                mouseHook.Stop();

                // Unregister all registered hotkeys
                foreach (int hotkeyId in RegisteredHotkeyIds)
                {
                    UnregisterHotKey(IntPtr.Zero, hotkeyId);
                }
                RegisteredHotkeyIds.Clear();

                NotificationService.Shutdown();
                LauncherService.Shutdown();
                RofiMenuService.Shutdown();
                renderTimer?.Dispose(); // Clean up timer
                Application.Exit(); // Exit the message loop
            };

            Console.WriteLine($"Setting up render timer for {refreshRate} Hz ({intervalMs} ms interval)");

            // Set up high-resolution timer for updating window positions
            long lastTimestamp = Stopwatch.GetTimestamp();
            renderTimer = new HighResolutionTimer(() =>
            {
                // Calculate delta time
                long currentTimestamp = Stopwatch.GetTimestamp();
                float deltaTime = (float)(currentTimestamp - lastTimestamp) * 1000f / Stopwatch.Frequency;
                lastTimestamp = currentTimestamp;
                windowManager.UpdatePosition(deltaTime);
            }, intervalMs);

            // Game mode must leave as little running as possible: the render thread
            // (busy-spin at monitor refresh rate) and the notification ticker are
            // both suspended until the pause is lifted.
            windowManager.PauseChanged += paused =>
            {
                if (paused)
                    renderTimer?.Stop();
                else
                    renderTimer?.Start();

                if (paused)
                    RofiMenuService.Close();

                NotificationService.SetSuspended(paused);
            };

            renderTimer.Start(); // Start timer immediately and keep it running

            // Subscribe to mouse hook events
            mouseHook.OnDragStart += () =>
            {
                if (mouseHook.IsMouseButtonBind)
                    CursorHider.Hide();

                windowManager.StartDragging();
            };
            mouseHook.OnDragStop += () =>
            {
                windowManager.StopDragging();
                CursorHider.Show();
            };

            // A save from the settings window reloads config.jsonc; re-apply the
            // parts that were cached at startup so the change takes effect now.
            ConfigManager.Applied += () =>
            {
                RegisterConfiguredHotkeys();
                mouseHook.ApplyBind(
                    ConfigManager.Current.CameraMoveButton,
                    ConfigManager.Current.MainMod);
                windowManager.ApplyLiveConfig();
            };

            // Register hotkeys from configuration
            RegisterConfiguredHotkeys();

            Application.AddMessageFilter(new HotkeyMessageFilter(windowManager));

            // Start the mouse hook
            mouseHook.Start();

            // Handle Ctrl+C to stop the hook and exit
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true; // Prevent immediate termination
                mouseHook.Stop();

                // Unregister all registered hotkeys
                foreach (int hotkeyId in RegisteredHotkeyIds)
                {
                    UnregisterHotKey(IntPtr.Zero, hotkeyId);
                }
                RegisteredHotkeyIds.Clear();

                NotificationService.Shutdown();
                LauncherService.Shutdown();
                RofiMenuService.Shutdown();
                renderTimer?.Dispose(); // Clean up timer
                Application.Exit(); // Exit the message loop
            };

            // Never leave the desktop without a pointer if the process ends mid-gesture.
            AppDomain.CurrentDomain.ProcessExit += (_, _) => CursorHider.Show();

            // Run the application message loop
            Application.Run();
        }

        /// <summary>
        /// Registers hotkeys based on the configuration.
        /// </summary>
        private static void RegisterConfiguredHotkeys()
        {
            // Release everything registered before so a reload cannot leave stale
            // combos behind, then start from a clean list.
            UnregisterRegisteredHotkeys();

            var config = ConfigManager.Current;
            string mainmod = config.MainMod;

            // Reset camera hotkey
            uint resetCameraModifiers;
            uint resetCameraKey;
            if (!ParseHotkeyString(config.Hotkeys.ResetCamera, mainmod, out resetCameraModifiers, out resetCameraKey))
            {
                Console.WriteLine("Invalid reset camera hotkey in config. Using default.");
                // Fallback to default: mainmod + space, but we know mainmod is valid from config validation
                ParseHotkeyString("mainmod + space", mainmod, out resetCameraModifiers, out resetCameraKey);
            }
            if (RegisterHotKey(IntPtr.Zero, 0x0000, resetCameraModifiers, resetCameraKey))
            {
                RegisteredHotkeyIds.Add(0x0000);
            }
            else
            {
                Console.WriteLine("Failed to register reset camera hotkey.");
            }

            // Toggle auto-center hotkey
            uint toggleModifiers;
            uint toggleKey;
            if (!ParseHotkeyString(config.Hotkeys.ToggleAutoCenter, mainmod, out toggleModifiers, out toggleKey))
            {
                Console.WriteLine("Invalid toggle auto-center hotkey in config. Using default.");
                ParseHotkeyString("mainmod + t", mainmod, out toggleModifiers, out toggleKey);
            }
            if (RegisterHotKey(IntPtr.Zero, 0x0001, toggleModifiers, toggleKey))
            {
                RegisteredHotkeyIds.Add(0x0001);
            }
            else
            {
                Console.WriteLine("Failed to register toggle auto-center hotkey.");
            }

            // Close window hotkey
            uint closeWindowModifiers;
            uint closeWindowKey;
            if (!ParseHotkeyString(config.Hotkeys.CloseWindow, mainmod, out closeWindowModifiers, out closeWindowKey))
            {
                Console.WriteLine("Invalid close window hotkey in config. Using default.");
                ParseHotkeyString("mainmod + c", mainmod, out closeWindowModifiers, out closeWindowKey);
            }
            if (RegisterHotKey(IntPtr.Zero, HOTKEY_ID_CLOSE_WINDOW, closeWindowModifiers, closeWindowKey))
            {
                RegisteredHotkeyIds.Add(HOTKEY_ID_CLOSE_WINDOW);
            }
            else
            {
                Console.WriteLine("Failed to register close window hotkey.");
            }

            // Center active window hotkey
            uint centerWindowModifiers;
            uint centerWindowKey;
            if (!ParseHotkeyString(config.Hotkeys.CenterActiveWindow, mainmod, out centerWindowModifiers, out centerWindowKey))
            {
                Console.WriteLine("Invalid center active window hotkey in config. Using default.");
                ParseHotkeyString("mainmod + f", mainmod, out centerWindowModifiers, out centerWindowKey);
            }
            if (RegisterHotKey(IntPtr.Zero, HOTKEY_ID_CENTER_WINDOW, centerWindowModifiers, centerWindowKey))
            {
                RegisteredHotkeyIds.Add(HOTKEY_ID_CENTER_WINDOW);
            }
            else
            {
                Console.WriteLine("Failed to register center active window hotkey.");
            }

            // Toggle game mode hotkey
            uint gameModeModifiers;
            uint gameModeKey;
            if (!ParseHotkeyString(config.Hotkeys.ToggleGameMode, mainmod, out gameModeModifiers, out gameModeKey))
            {
                Console.WriteLine("Invalid toggle game mode hotkey in config. Using default.");
                ParseHotkeyString("mainmod + g", mainmod, out gameModeModifiers, out gameModeKey);
            }
            if (RegisterHotKey(IntPtr.Zero, HOTKEY_ID_GAME_MODE, gameModeModifiers, gameModeKey))
            {
                RegisteredHotkeyIds.Add(HOTKEY_ID_GAME_MODE);
            }
            else
            {
                Console.WriteLine("Failed to register toggle game mode hotkey.");
            }

            // Toggle quick-action launcher hotkey
            uint launcherModifiers;
            uint launcherKey;
            if (!ParseHotkeyString(config.Hotkeys.ToggleLauncher, mainmod, out launcherModifiers, out launcherKey))
            {
                Console.WriteLine("Invalid toggle launcher hotkey in config. Using default.");
                ParseHotkeyString("mainmod + d", mainmod, out launcherModifiers, out launcherKey);
            }
            if (RegisterHotKey(IntPtr.Zero, HOTKEY_ID_LAUNCHER, launcherModifiers, launcherKey))
            {
                RegisteredHotkeyIds.Add(HOTKEY_ID_LAUNCHER);
            }
            else
            {
                Console.WriteLine("Failed to register toggle launcher hotkey.");
            }

            // Toggle Rofi application menu hotkey
            uint rofiModifiers;
            uint rofiKey;
            if (!ParseHotkeyString(config.Hotkeys.ToggleRofiMenu, mainmod, out rofiModifiers, out rofiKey))
            {
                Console.WriteLine("Invalid toggle Rofi menu hotkey in config. Using default.");
                ParseHotkeyString("mainmod + q", mainmod, out rofiModifiers, out rofiKey);
            }
            if (RegisterHotKey(IntPtr.Zero, HOTKEY_ID_ROFI_MENU, rofiModifiers, rofiKey))
            {
                RegisteredHotkeyIds.Add(HOTKEY_ID_ROFI_MENU);
            }
            else
            {
                Console.WriteLine("Failed to register toggle Rofi menu hotkey.");
            }

            // Register launch app hotkeys
            RegisterLaunchAppHotkeys(mainmod);
        }

        /// <summary>
        /// Unregisters every OS hotkey this process registered and clears the
        /// bookkeeping used to route them. Called before a fresh registration so
        /// a config reload never leaves stale combinations behind.
        /// </summary>
        private static void UnregisterRegisteredHotkeys()
        {
            foreach (int id in RegisteredHotkeyIds)
            {
                UnregisterHotKey(IntPtr.Zero, id);
            }
            RegisteredHotkeyIds.Clear();
            LaunchAppPaths.Clear();
        }

        /// <summary>
        /// Registers hotkeys for launching programs based on the LaunchApps configuration.
        /// Each entry maps a hotkey string to a program/document path and is assigned a unique
        /// hotkey ID starting at <see cref="HOTKEY_ID_LAUNCH_APP_BASE"/>.
        /// </summary>
        private static void RegisterLaunchAppHotkeys(string mainmod)
        {
            LaunchAppPaths.Clear();

            var config = ConfigManager.Current;
            if (config.LaunchApps == null || config.LaunchApps.Count == 0)
            {
                return;
            }

            int id = HOTKEY_ID_LAUNCH_APP_BASE;
            foreach (var kvp in config.LaunchApps)
            {
                string hotkey = kvp.Key;
                string path = kvp.Value;

                if (!ParseHotkeyString(hotkey, mainmod, out uint modifiers, out uint key))
                {
                    Console.WriteLine($"Invalid launch app hotkey skipped: '{hotkey}'.");
                    continue;
                }

                if (RegisterHotKey(IntPtr.Zero, id, modifiers, key))
                {
                    RegisteredHotkeyIds.Add(id);
                    LaunchAppPaths[id] = path;
                    Console.WriteLine($"Registered launch app hotkey '{hotkey}' -> '{path}'.");
                }
                else
                {
                    Console.WriteLine($"Failed to register launch app hotkey '{hotkey}' (maybe already in use).");
                }

                id++;
            }
        }

        /// <summary>
        /// Launches the program or document at the given path using the default shell handler.
        /// Handles executable paths, document files, and URLs.
        /// </summary>
        /// <param name="path">Path (or URL) to open.</param>
        public static void LaunchApplication(string path)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                };
                Process.Start(startInfo);
                Console.WriteLine($"Launched application: {path}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to launch application '{path}': {ex.Message}");
            }
        }

        /// <summary>
        /// Parses a hotkey string (e.g., "mainmod + space") into modifier flags and virtual key code.
        /// Replaces "mainmod" with the provided mainmod string.
        /// Returns true if successful, false otherwise.
        /// </summary>
        private static bool ParseHotkeyString(string hotkeyString, string mainmod, out uint modifiers, out uint key)
        {
            modifiers = 0;
            key = 0;

            if (string.IsNullOrWhiteSpace(hotkeyString))
                return false;

            // Replace mainmod placeholder (case-insensitive)
            string replaced = hotkeyString.Replace("mainmod", mainmod, StringComparison.InvariantCultureIgnoreCase);
            // Split by '+'
            string[] parts = replaced.Split('+', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return false;

            // The last part is the key, the rest are modifiers.
            string keyPart = parts[parts.Length - 1].Trim();
            if (string.IsNullOrEmpty(keyPart))
                return false;

            // Parse modifier parts
            for (int i = 0; i < parts.Length - 1; i++)
            {
                string modifier = parts[i].Trim().ToUpperInvariant();
                uint modFlag = ParseModifierString(modifier);
                if (modFlag == 0)
                    return false; // Invalid modifier
                modifiers |= modFlag;
            }

            // Parse key part
            key = ParseKeyString(keyPart);
            if (key == 0)
                return false; // Invalid key

            return true;
        }

        /// <summary>
        /// Converts a modifier string to the corresponding modifier flags.
        /// Handles both single modifiers (e.g., "CONTROL") and compound modifiers (e.g., "CONTROL+ALT+SHIFT").
        /// </summary>
        private static uint ParseModifierString(string modifierString)
        {
            if (string.IsNullOrWhiteSpace(modifierString))
                return 0;

            // Split by '+' to handle compound modifiers
            string[] parts = modifierString.Split('+', StringSplitOptions.RemoveEmptyEntries);
            uint modifiers = 0;

            // Process each part
            foreach (string part in parts)
            {
                string trimmed = part.Trim().ToUpperInvariant();
                uint partFlag = trimmed switch
                {
                    "CONTROL" => 0x0002, // MOD_CONTROL
                    "ALT" => 0x0001, // MOD_ALT
                    "SHIFT" => 0x0004, // MOD_SHIFT
                    "WIN" or "LWIN" or "RWIN" => 0x0008, // MOD_WIN
                    _ => 0 // Unknown modifier
                };

                if (partFlag == 0)
                {
                    // If any part is unrecognized, the whole string is invalid
                    return 0;
                }

                modifiers |= partFlag;
            }

            return modifiers;
        }

        /// <summary>
        /// Converts a key string (e.g., "A", "F1") to the corresponding virtual key code.
        /// </summary>
        private static uint ParseKeyString(string keyString)
        {
            if (string.IsNullOrEmpty(keyString))
                return 0;

            string key = keyString.Trim().ToUpperInvariant();

            // Handle single character keys (A-Z, 0-9)
            if (key.Length == 1)
            {
                char c = key[0];
                if (c >= 'A' && c <= 'Z')
                    return (uint)c;
                if (c >= '0' && c <= '9')
                    return (uint)c;
            }

            // Handle special keys
            return key switch
            {
                "F1" => 0x70,
                "F2" => 0x71,
                "F3" => 0x72,
                "F4" => 0x73,
                "F5" => 0x74,
                "F6" => 0x75,
                "F7" => 0x76,
                "F8" => 0x77,
                "F9" => 0x78,
                "F10" => 0x79,
                "F11" => 0x7A,
                "F12" => 0x7B,
                "LEFT" => 0x25,
                "UP" => 0x26,
                "RIGHT" => 0x27,
                "DOWN" => 0x28,
                "ENTER" => 0x0D,
                "ESC" => 0x1B,
                "SPACE" => 0x20,
                "TAB" => 0x09,
                "BACKSPACE" => 0x08,
                "DELETE" => 0x2E,
                "INSERT" => 0x2D,
                "HOME" => 0x24,
                "END" => 0x23,
                "PAGEUP" => 0x21,
                "PAGEDOWN" => 0x22,
                _ => 0 // Unknown key
            };
        }
    }

    /// <summary>
    /// Message filter to handle hotkey events.
    /// </summary>
    internal class HotkeyMessageFilter : IMessageFilter
    {
        private readonly WindowManager _windowManager;

        public HotkeyMessageFilter(WindowManager windowManager)
        {
            _windowManager = windowManager;
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg == Program.WM_HOTKEY)
            {
                // Ignore all hotkeys when paused (game mode)
                if (_windowManager.IsPaused)
                {
                    return true; // Message handled (do nothing)
                }

                int wParam = m.WParam.ToInt32();
                // Note: We don't have the actual modifier and key values here, but we can compare wParam to our registered IDs.
                // We registered two hotkeys with IDs 0x0000 and 0x0001.
                if (wParam == 0x0000) // Reset camera
                {
                    _windowManager.ResetCamera();
                    NotificationService.Show("Camera", "Reset to origin");
                    return true; // Message handled
                }
                else if (wParam == 0x0001) // Toggle auto-center-on-focus
                {
                    _windowManager.ToggleAutoCenter();
                    NotificationService.Show(
                        "Auto-center",
                        _windowManager.AutoCenterOnFocus ? "Enabled" : "Disabled");
                    return true; // Message handled
                }
                else if (wParam == Program.HOTKEY_ID_CLOSE_WINDOW) // Close window
                {
                    _windowManager.CloseActiveWindow();
                    NotificationService.Show("Window", "Closed");
                    return true; // Message handled
                }
                else if (wParam == Program.HOTKEY_ID_CENTER_WINDOW) // Center active window
                {
                    _windowManager.CenterActiveWindow();
                    NotificationService.Show("Window", "Centered");
                    return true; // Message handled
                }
                else if (wParam == Program.HOTKEY_ID_GAME_MODE) // Toggle game mode
                {
                    // Game mode never shows a notification; the manager also hides existing ones.
                    _windowManager.ToggleGameMode();
                    return true; // Message handled
                }
                else if (wParam == Program.HOTKEY_ID_LAUNCHER) // Toggle quick-action panel
                {
                    // The launcher hotkey also dismisses an open Rofi menu.
                    if (RofiMenuService.IsOpen)
                        RofiMenuService.Close();
                    else
                        LauncherService.Toggle();

                    return true; // Message handled
                }
                else if (wParam == Program.HOTKEY_ID_ROFI_MENU) // Toggle Rofi menu
                {
                    LauncherService.Hide();
                    RofiMenuService.Toggle();
                    return true; // Message handled
                }
                else if (Program.LaunchAppPaths.TryGetValue(wParam, out string? launchPath) && launchPath != null) // Launch a configured program
                {
                    Program.LaunchApplication(launchPath);
                    NotificationService.Show("Launching", GetLaunchDisplayName(launchPath));
                    return true; // Message handled
                }
            }
            return false; // Message not handled
        }

        private static string GetLaunchDisplayName(string path)
        {
            try
            {
                string name = Path.GetFileName(path);
                return string.IsNullOrWhiteSpace(name) ? path : name;
            }
            catch (ArgumentException)
            {
                return path;
            }
        }
    }
}
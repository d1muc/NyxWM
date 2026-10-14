using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Threading;
using nyxWM;

namespace nyxWM
{
    /// <summary>
    /// Thread-safe manager for caching game executable paths to enable O(1) detection.
    /// Uses a local JSON file for persistence.
    /// </summary>
    public static class GameCacheManager
    {
        private const string CacheFileName = "gamecache.json";
        private const string BlacklistFileName = "blacklist.json";
        private static readonly string CacheDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache");
        private static readonly string CacheFilePath = Path.Combine(CacheDirectory, CacheFileName);
        private static readonly string BlacklistFilePath = Path.Combine(CacheDirectory, BlacklistFileName);

        // Thread-safe store of known game executable paths (lowercase for case-insensitive comparison)
        private static readonly ConcurrentDictionary<string, bool> _knownGames = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        // Thread-safe store of blacklisted executable names (without path, lowercased for case-insensitive comparison)
        private static readonly ConcurrentDictionary<string, bool> _blacklistedExes = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        // Lock for file operations to prevent concurrent reads/writes
        private static readonly SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1);

        static GameCacheManager()
        {
            // Ensure cache directory exists
            if (!Directory.Exists(CacheDirectory))
            {
                Directory.CreateDirectory(CacheDirectory);
            }

            // Load cache from file
            LoadCache();

            // Load blacklist from file
            LoadBlacklist();
        }

        /// <summary>
        /// Loads the cache from the JSON file.
        /// </summary>
        private static void LoadCache()
        {
            try
            {
                if (File.Exists(CacheFilePath))
                {
                    var json = File.ReadAllText(CacheFilePath);
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var cacheData = JsonSerializer.Deserialize<CacheData>(json, options);
                    if (cacheData != null && cacheData.KnownGames != null)
                    {
                        foreach (var path in cacheData.KnownGames)
                        {
                            _knownGames[path] = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Log error but continue with empty cache
                Console.WriteLine($"Failed to load game cache: {ex.Message}");
            }
        }

        /// <summary>
        /// Saves the cache to the JSON file.
        /// </summary>
        public static void SaveCache()
        {
            try
            {
                _fileLock.Wait();
                try
                {
                    var cacheData = new CacheData
                    {
                        KnownGames = new System.Collections.Generic.List<string>(_knownGames.Keys)
                    };
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    var json = JsonSerializer.Serialize(cacheData, options);
                    File.WriteAllText(CacheFilePath, json);
                }
                finally
                {
                    _fileLock.Release();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save game cache: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads the blacklist from the JSON file.
        /// </summary>
        private static void LoadBlacklist()
        {
            try
            {
                if (File.Exists(BlacklistFilePath))
                {
                    var json = File.ReadAllText(BlacklistFilePath);
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var blacklistData = JsonSerializer.Deserialize<BlacklistData>(json, options);
                    if (blacklistData != null && blacklistData.Executables != null)
                    {
                        foreach (var exe in blacklistData.Executables)
                        {
                            if (!string.IsNullOrWhiteSpace(exe))
                            {
                                _blacklistedExes[exe.Trim().ToLowerInvariant()] = true;
                            }
                        }
                    }
                }
                else
                {
                    // If blacklist file doesn't exist, create it with default values
                    _blacklistedExes["explorer.exe"] = true;
                    _blacklistedExes["cmd.exe"] = true;
                    SaveBlacklist();
                }
            }
            catch (Exception ex)
            {
                // Log error but continue with empty blacklist
                Console.WriteLine($"Failed to load blacklist: {ex.Message}");
                // Ensure we have at least the default blacklist
                _blacklistedExes["explorer.exe"] = true;
                _blacklistedExes["cmd.exe"] = true;
            }
        }

        /// <summary>
        /// Saves the blacklist to the JSON file.
        /// </summary>
        public static void SaveBlacklist()
        {
            try
            {
                _fileLock.Wait();
                try
                {
                    var blacklistData = new BlacklistData
                    {
                        Executables = new System.Collections.Generic.List<string>(_blacklistedExes.Keys)
                    };
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    var json = JsonSerializer.Serialize(blacklistData, options);
                    File.WriteAllText(BlacklistFilePath, json);
                }
                finally
                {
                    _fileLock.Release();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save blacklist: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if the executable (by file name) is in the blacklist.
        /// </summary>
        /// <param name="exePath">Full path to the executable.</param>
        /// <returns>True if the executable is blacklisted.</returns>
        public static bool IsBlacklisted(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath))
                return false;

            try
            {
                string fileName = Path.GetFileName(exePath);
                if (string.IsNullOrWhiteSpace(fileName))
                    return false;

                string lowerFileName = fileName.ToLowerInvariant();
                return _blacklistedExes.ContainsKey(lowerFileName);
            }
            catch
            {
                // In case of any error (e.g., invalid path), assume not blacklisted
                return false;
            }
        }

        /// <summary>
        /// Adds an executable path to the cache.
        /// </summary>
        /// <param name="exePath">Full path to the executable (case-insensitive).</param>
        public static void AddKnownGame(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath))
                return;

            // Do not add blacklisted executables to the cache
            if (IsBlacklisted(exePath))
                return;

            var normalizedPath = exePath.Trim();
            _knownGames.AddOrUpdate(normalizedPath, true, (key, oldValue) => true);
            // Optionally save immediately or batch saves; we'll save immediately for simplicity
            SaveCache();
        }

        /// <summary>
        /// Checks if the given executable path is known to be a game.
        /// </summary>
        /// <param name="exePath">Full path to the executable.</param>
        /// <returns>True if the path is in the cache.</returns>
        public static bool IsKnownGame(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath))
                return false;

            return _knownGames.ContainsKey(exePath);
        }

        /// <summary>
        /// Computes a heuristic score for a window to determine if it's likely a game.
        /// </summary>
        /// <param name="hWnd">Handle to the window.</param>
        /// <param name="exePath">Full path to the executable (if known).</param>
        /// <returns>Score from 0 to 100, higher indicates higher likelihood of being a game.</returns>
        public static int ComputeGameScore(IntPtr hWnd, string exePath)
        {
            // Return 0 immediately for blacklisted executables
            if (IsBlacklisted(exePath))
                return 0;

            int score = 0;

            // 1. Check executable path cache (if known game, high score)
            if (!string.IsNullOrEmpty(exePath) && IsKnownGame(exePath))
            {
                score += 50; // Known game gets a strong base score
            }

            // 2. Check window class name for known game engines
            var classNameBuilder = new System.Text.StringBuilder(256);
            WinApi.GetClassName(hWnd, classNameBuilder, classNameBuilder.Capacity);
            string className = classNameBuilder.ToString().ToLowerInvariant();

            // Common game engine window class names (examples)
            var engineIndicators = new[] { "unity", "unreal", "sfml", "glfw", "sdl", "rage", "frostbite", "cea" };
            foreach (var indicator in engineIndicators)
            {
                if (className.Contains(indicator))
                {
                    score += 20;
                    break; // Only add once
                }
            }

            // 3. Check window styles: WS_POPUP without WS_CAPTION (typical for borderless fullscreen games)
            int style = WinApi.GetWindowLong(hWnd, WinApi.GWL_STYLE);
            bool isPopup = (style & WinApi.WS_POPUP) != 0;
            bool hasCaption = (style & WinApi.WS_CAPTION) != 0;
            if (isPopup && !hasCaption)
            {
                score += 15;
            }

            // 4. Check window geometry: area >= 95% of screen
            if (WinApi.GetWindowRect(hWnd, out WinApi.RECT rect))
            {
                // Get primary screen bounds
                var screen = System.Windows.Forms.Screen.PrimaryScreen;
                if (screen != null)
                {
                    var screenBounds = screen.Bounds;
                    int screenArea = screenBounds.Width * screenBounds.Height;
                    int windowArea = (rect.Right - rect.Left) * (rect.Bottom - rect.Top);
                    if (screenArea > 0)
                    {
                        float areaRatio = (float)windowArea / screenArea;
                        if (areaRatio >= 0.95f)
                        {
                            score += 20;
                        }
                    }
                }
            }

            // 5. Additional heuristic: if the executable path contains certain keywords (game, exe, etc.) - use with caution
            if (!string.IsNullOrEmpty(exePath))
            {
                string lowerExe = exePath.ToLowerInvariant();
                if (lowerExe.Contains("game") || lowerExe.Contains("play") || lowerExe.Contains("steam"))
                {
                    score += 10;
                }
            }

            // Clamp score to 0-100
            return Math.Clamp(score, 0, 100);
        }

        private class CacheData
        {
            public System.Collections.Generic.List<string> KnownGames { get; set; } = new System.Collections.Generic.List<string>();
        }

        private class BlacklistData
        {
            public System.Collections.Generic.List<string> Executables { get; set; } = new System.Collections.Generic.List<string>();
        }
    }
}
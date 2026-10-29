using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace nyxWM
{
    /// <summary>
    /// Pinned Rofi menu entries. Pins are stored by app path in
    /// config/pinned.json so they survive restarts, and are cached in memory
    /// after the first read.
    /// </summary>
    internal static class RofiPins
    {
        private static readonly object SyncRoot = new();
        private static readonly string PinnedFilePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "config", "pinned.json");

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true
        };

        private static HashSet<string>? _pinned;

        internal static bool IsPinned(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            lock (SyncRoot)
            {
                return Load().Contains(path);
            }
        }

        /// <summary>
        /// Adds or removes the path and writes the new set to disk.
        /// Returns true when the entry is now pinned.
        /// </summary>
        internal static bool Toggle(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            lock (SyncRoot)
            {
                HashSet<string> pins = Load();
                bool pinned = !pins.Remove(path);

                if (pinned)
                    pins.Add(path);

                _pinned = pins;
                Save(pins);
                return pinned;
            }
        }

        /// <summary>
        /// Returns a new list with pinned apps first, preserving the relative
        /// order of both groups.
        /// </summary>
        internal static IReadOnlyList<RofiApp> Order(IReadOnlyList<RofiApp> apps)
        {
            if (apps == null || apps.Count == 0)
                return Array.Empty<RofiApp>();

            HashSet<string> pins;
            lock (SyncRoot)
            {
                pins = Load();
            }

            if (pins.Count == 0)
                return apps;

            var pinned = new List<RofiApp>(apps.Count);
            var rest = new List<RofiApp>(apps.Count);

            foreach (RofiApp app in apps)
            {
                if (!string.IsNullOrWhiteSpace(app.Path) && pins.Contains(app.Path))
                    pinned.Add(app);
                else
                    rest.Add(app);
            }

            if (pinned.Count == 0)
                return apps;

            pinned.AddRange(rest);
            return pinned;
        }

        private static HashSet<string> Load()
        {
            if (_pinned != null)
                return _pinned;

            try
            {
                if (File.Exists(PinnedFilePath))
                {
                    string[]? paths = JsonSerializer.Deserialize<string[]>(
                        File.ReadAllText(PinnedFilePath));

                    if (paths != null)
                    {
                        _pinned = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
                        return _pinned;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"RofiPins: failed to read pinned apps: {ex.Message}");
            }

            _pinned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return _pinned;
        }

        private static void Save(HashSet<string> pins)
        {
            try
            {
                string directory = Path.GetDirectoryName(PinnedFilePath)!;
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                string json = JsonSerializer.Serialize(pins, SerializerOptions);
                string temporaryPath = PinnedFilePath + ".tmp";
                File.WriteAllText(temporaryPath, json);
                File.Move(temporaryPath, PinnedFilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"RofiPins: failed to save pinned apps: {ex.Message}");
            }
        }
    }
}

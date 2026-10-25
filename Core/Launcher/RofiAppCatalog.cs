using System;
using System.Collections.Generic;
using System.Threading;

namespace nyxWM
{
    /// <summary>
    /// How the Rofi menu launches an entry.
    /// </summary>
    internal enum RofiAppKind
    {
        /// <summary>A desktop executable or script resolved from a Start Menu shortcut.</summary>
        Desktop,

        /// <summary>A UWP / Microsoft Store app addressed through shell:AppsFolder.</summary>
        Uwp
    }

    /// <summary>
    /// One launchable entry in the Rofi menu. <see cref="Path"/> is the target
    /// executable for desktop apps and the AppUserModelID for UWP apps.
    /// </summary>
    internal sealed class RofiApp
    {
        internal RofiApp(
            string name,
            string category,
            string path,
            string arguments = "",
            string workingDirectory = "",
            RofiAppKind kind = RofiAppKind.Desktop)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Category = category ?? string.Empty;
            Path = path ?? throw new ArgumentNullException(nameof(path));
            Arguments = arguments ?? string.Empty;
            WorkingDirectory = workingDirectory ?? string.Empty;
            Kind = kind;
        }

        internal string Name { get; }
        internal string Category { get; }
        internal string Path { get; }
        internal string Arguments { get; }
        internal string WorkingDirectory { get; }
        internal RofiAppKind Kind { get; }
    }

    /// <summary>
    /// Discovered applications from the Windows Start Menu. The scan uses COM
    /// (WScript.Shell / Shell.Application), so it runs on a dedicated STA
    /// thread and the result is cached for the lifetime of the process.
    /// </summary>
    internal static class RofiAppCatalog
    {
        private static readonly object _lock = new();
        private static IReadOnlyList<RofiApp>? _apps;

        /// <summary>
        /// Returns the cached catalog, scanning the Start Menu on first use.
        /// Subsequent calls are free, but the first caller waits for the scan.
        /// </summary>
        internal static IReadOnlyList<RofiApp> GetApps()
        {
            lock (_lock)
            {
                return _apps ??= Load();
            }
        }

        /// <summary>
        /// Builds the catalog in the background so the first menu open is fast.
        /// Safe to call once at startup; repeated calls simply reuse the cache.
        /// </summary>
        internal static void Prefetch()
        {
            var thread = new Thread(() =>
            {
                try
                {
                    GetApps();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"RofiAppCatalog: prefetch failed: {ex.Message}");
                }
            })
            {
                IsBackground = true,
                Name = "nyxWM Start Menu scan"
            };

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        private static IReadOnlyList<RofiApp> Load()
        {
            var apps = new List<RofiApp>();
            var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            StartMenuAppScanner.Scan(apps, seenTargets, seenNames);
            UwpAppScanner.Scan(apps, seenNames);

            apps.Sort(static (left, right) =>
                StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));

            Console.WriteLine($"RofiAppCatalog: discovered {apps.Count} applications.");
            return apps.ToArray();
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace nyxWM
{
    /// <summary>
    /// Recursively reads the Start Menu shortcut folders and turns every
    /// launchable .lnk into a <see cref="RofiApp"/>. Targets are resolved
    /// through WScript.Shell so arguments and the working directory survive.
    /// </summary>
    internal static class StartMenuAppScanner
    {
        private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".com", ".bat", ".cmd"
        };

        private static readonly HashSet<string> JunkTokens = new(StringComparer.OrdinalIgnoreCase)
        {
            "uninstall", "unins", "unins000", "readme", "help", "documentation",
            "license", "changelog", "release", "website", "homepage", "manual",
            "support", "update", "updater", "repair", "installer", "setup", "remove"
        };

        internal static void Scan(
            List<RofiApp> apps,
            HashSet<string> seenTargets,
            HashSet<string> seenNames)
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var roots = new (string Path, string FallbackCategory)[]
            {
                (Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                    "Programs"), "Applications"),
                (Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                    "Programs"), "Applications"),
                (Path.Combine(
                    appData,
                    "Microsoft",
                    "Internet Explorer",
                    "Quick Launch",
                    "User Pinned",
                    "TaskBar"), "Pinned")
            };

            object? shell = null;

            try
            {
                shell = CreateShell();
                if (shell == null)
                    return;

                foreach ((string rootPath, string fallbackCategory) in roots)
                {
                    if (!Directory.Exists(rootPath))
                        continue;

                    ScanRoot(shell, rootPath, fallbackCategory, apps, seenTargets, seenNames);
                }
            }
            finally
            {
                ReleaseCom(shell);
            }
        }

        internal static bool IsJunkName(string name)
        {
            string compact = name.Replace(" ", string.Empty, StringComparison.Ordinal);

            if (compact.StartsWith("uninstall", StringComparison.OrdinalIgnoreCase) ||
                compact.StartsWith("unins", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string[] tokens = name.Split(
                new[] { ' ', '-', '_', '(', ')', '[', ']', '.', ',', '\t' },
                StringSplitOptions.RemoveEmptyEntries);

            foreach (string token in tokens)
            {
                if (JunkTokens.Contains(token))
                    return true;
            }

            return false;
        }

        private static void ScanRoot(
            object shell,
            string rootPath,
            string fallbackCategory,
            List<RofiApp> apps,
            HashSet<string> seenTargets,
            HashSet<string> seenNames)
        {
            try
            {
                foreach (string shortcutPath in Directory.EnumerateFiles(
                             rootPath, "*.lnk", SearchOption.AllDirectories))
                {
                    AddShortcut(
                        shell,
                        shortcutPath,
                        rootPath,
                        fallbackCategory,
                        apps,
                        seenTargets,
                        seenNames);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"StartMenuScanner: cannot read '{rootPath}': {ex.Message}");
            }
        }

        private static void AddShortcut(
            object shell,
            string shortcutPath,
            string rootPath,
            string fallbackCategory,
            List<RofiApp> apps,
            HashSet<string> seenTargets,
            HashSet<string> seenNames)
        {
            string name = Path.GetFileNameWithoutExtension(shortcutPath).Trim();
            if (name.Length == 0 || IsJunkName(name))
                return;

            if (!TryReadShortcut(
                    shell,
                    shortcutPath,
                    out string target,
                    out string arguments,
                    out string workingDirectory))
            {
                return;
            }

            if (!IsLaunchableTarget(target))
                return;

            string dedupeKey = target + "|" + arguments;
            if (!seenTargets.Add(dedupeKey))
                return;

            if (!seenNames.Add(name))
                return;

            string category = ResolveCategory(shortcutPath, rootPath, fallbackCategory);
            apps.Add(new RofiApp(name, category, target, arguments, workingDirectory));
        }

        private static bool IsLaunchableTarget(string target)
        {
            if (string.IsNullOrWhiteSpace(target))
                return false;

            if (target.IndexOf(@"\Windows\Installer\", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            string extension = Path.GetExtension(target);
            if (!AllowedExtensions.Contains(extension))
                return false;

            string fileName = Path.GetFileNameWithoutExtension(target);
            if (fileName.Contains("unins", StringComparison.OrdinalIgnoreCase) ||
                fileName.Contains("uninstall", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        private static string ResolveCategory(string shortcutPath, string rootPath, string fallback)
        {
            string? directory = Path.GetDirectoryName(shortcutPath);
            if (string.IsNullOrEmpty(directory))
                return fallback;

            string current = Path.GetFileName(directory.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            string root = Path.GetFileName(rootPath.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            return string.Equals(current, root, StringComparison.OrdinalIgnoreCase)
                ? fallback
                : current;
        }

        private static object? CreateShell()
        {
            try
            {
                Type? type = Type.GetTypeFromProgID("WScript.Shell");
                return type == null ? null : Activator.CreateInstance(type);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"StartMenuScanner: WScript.Shell unavailable: {ex.Message}");
                return null;
            }
        }

        private static bool TryReadShortcut(
            object shell,
            string shortcutPath,
            out string target,
            out string arguments,
            out string workingDirectory)
        {
            target = string.Empty;
            arguments = string.Empty;
            workingDirectory = string.Empty;

            object? shortcut = null;

            try
            {
                shortcut = shell.GetType().InvokeMember(
                    "CreateShortcut",
                    BindingFlags.InvokeMethod,
                    null,
                    shell,
                    new object[] { shortcutPath });

                if (shortcut == null)
                    return false;

                Type shortcutType = shortcut.GetType();
                target = ReadStringProperty(shortcutType, shortcut, "TargetPath");
                arguments = ReadStringProperty(shortcutType, shortcut, "Arguments");
                workingDirectory = ReadStringProperty(shortcutType, shortcut, "WorkingDirectory");

                target = Environment.ExpandEnvironmentVariables(target).Trim();
                workingDirectory = Environment.ExpandEnvironmentVariables(workingDirectory).Trim();

                return target.Length > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"StartMenuScanner: cannot resolve '{shortcutPath}': {ex.Message}");
                return false;
            }
            finally
            {
                ReleaseCom(shortcut);
            }
        }

        private static string ReadStringProperty(Type type, object instance, string property)
        {
            object? value = type.InvokeMember(
                property,
                BindingFlags.GetProperty,
                null,
                instance,
                null);

            return (value as string ?? string.Empty).Trim();
        }

        private static void ReleaseCom(object? instance)
        {
            if (instance != null && Marshal.IsComObject(instance))
            {
                try
                {
                    Marshal.ReleaseComObject(instance);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"StartMenuScanner: COM release failed: {ex.Message}");
                }
            }
        }
    }
}

using System;
using System.Drawing;
using System.IO;

namespace nyxWM
{
    /// <summary>
    /// Normalizes the Terminal settings and turns them into the values the
    /// terminal window needs: a console font, a resolved colour scheme and a
    /// shell launch description. Every consumer reads the live config, so a save
    /// applies to open terminal windows without a restart.
    /// </summary>
    internal static class TerminalPreferences
    {
        internal const float MinFontSize = 11f;
        internal const float MaxFontSize = 18f;
        internal const float MinOpacity = 0.4f;
        internal const float MaxOpacity = 1f;

        internal const string CursorBlock = "Block";
        internal const string CursorLine = "Line";
        internal const string CursorUnderline = "Underline";
        internal const string CursorDefault = CursorBlock;

        internal const string ShellZsh = "zsh";
        internal const string ShellBash = "bash";
        internal const string ShellPowerShell = "powershell";
        internal const string ShellCmd = "cmd";
        internal const string ShellDefault = ShellZsh;

        internal const string SchemeTheme = "Theme";
        internal const string SchemeCustomAccent = "Custom Accent";
        internal const string SchemeDracula = "Dracula";
        internal const string SchemeMatrix = "Matrix Green";
        internal const string SchemeCyberpunk = "Cyberpunk Neon";
        internal const string SchemeMonokai = "Monokai";
        internal const string SchemeNord = "Nord";
        internal const string SchemeDefault = SchemeTheme;

        internal static readonly string[] FontFamilies =
        {
            "Cascadia Code", "JetBrains Mono", "Fira Code", "Consolas"
        };

        internal static readonly string[] CursorStyles =
        {
            CursorBlock, CursorLine, CursorUnderline
        };

        internal static readonly string[] ColorSchemes =
        {
            SchemeTheme, SchemeCustomAccent, SchemeDracula, SchemeMatrix,
            SchemeCyberpunk, SchemeMonokai, SchemeNord
        };

        internal static readonly string[] Shells =
        {
            ShellZsh, ShellBash, ShellPowerShell, ShellCmd
        };

        private static readonly string[] FallbackFamilies =
        {
            "Cascadia Code", "Cascadia Mono", "JetBrains Mono", "Fira Code", "Consolas"
        };

        /// <summary>Live terminal settings, never null.</summary>
        internal static TerminalSettings Current =>
            ConfigManager.Current.Terminal ?? new TerminalSettings();

        internal static Font CreateConsoleFont(string? family, float sizePoints)
        {
            string requested = NormalizeFontFamily(family);
            float size = NormalizeFontSize(sizePoints);

            try
            {
                if (IsInstalled(requested))
                    return new Font(requested, size * VisualPreferences.UiScale, FontStyle.Regular, GraphicsUnit.Point);
            }
            catch (ArgumentException)
            {
                // Fall through to the fallback chain.
            }

            foreach (string candidate in FallbackFamilies)
            {
                try
                {
                    if (IsInstalled(candidate))
                        return new Font(candidate, size * VisualPreferences.UiScale, FontStyle.Regular, GraphicsUnit.Point);
                }
                catch (ArgumentException)
                {
                    // Family is not installed; try the next candidate.
                }
            }

            return new Font(
                FontFamily.GenericMonospace,
                size * VisualPreferences.UiScale,
                FontStyle.Regular,
                GraphicsUnit.Point);
        }

        private static bool IsInstalled(string family)
        {
            try
            {
                using var probe = new FontFamily(family);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        internal static string NormalizeFontFamily(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return FontFamilies[0];

            foreach (string family in FontFamilies)
            {
                if (string.Equals(family, value.Trim(), StringComparison.OrdinalIgnoreCase))
                    return family;
            }

            return FontFamilies[0];
        }

        internal static float NormalizeFontSize(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return MinFontSize;

            return Math.Clamp((float)Math.Round(value * 2.0f) / 2.0f, MinFontSize, MaxFontSize);
        }

        internal static string NormalizeCursorStyle(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return CursorDefault;

            foreach (string style in CursorStyles)
            {
                if (string.Equals(style, value.Trim(), StringComparison.OrdinalIgnoreCase))
                    return style;
            }

            return CursorDefault;
        }

        internal static string NormalizeColorScheme(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return SchemeDefault;

            foreach (string scheme in ColorSchemes)
            {
                if (string.Equals(scheme, value.Trim(), StringComparison.OrdinalIgnoreCase))
                    return scheme;
            }

            return SchemeDefault;
        }

        internal static string NormalizeShell(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return ShellDefault;

            foreach (string shell in Shells)
            {
                if (string.Equals(shell, value.Trim(), StringComparison.OrdinalIgnoreCase))
                    return shell;
            }

            return ShellDefault;
        }

        internal static float NormalizeOpacity(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
                return 0.98f;

            return Math.Clamp(value, MinOpacity, MaxOpacity);
        }

        /// <summary>
        /// Resolves the configured shell into an executable path, its command
        /// line arguments and whether the MSYS2 environment should be applied.
        /// </summary>
        internal static TerminalShellLaunch ResolveShell(string? name)
        {
            string shell = NormalizeShell(name);

            switch (shell)
            {
                case ShellBash:
                    string bash = Path.Combine(TerminalEnvironment.DefaultRoot, "usr", "bin", "bash.exe");
                    if (!File.Exists(bash))
                        throw new TerminalStartupException($"bash not found: {bash}");

                    return new TerminalShellLaunch(bash, "-l -i", useMsysEnvironment: true);

                case ShellPowerShell:
                    return new TerminalShellLaunch(
                        ResolvePowerShell(),
                        "-NoLogo",
                        useMsysEnvironment: false);

                case ShellCmd:
                    string comspec = Environment.GetEnvironmentVariable("COMSPEC") ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(comspec) || !File.Exists(comspec))
                        comspec = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.System),
                            "cmd.exe");

                    if (!File.Exists(comspec))
                        throw new TerminalStartupException($"cmd.exe not found: {comspec}");

                    return new TerminalShellLaunch(comspec, string.Empty, useMsysEnvironment: false);

                default:
                    return new TerminalShellLaunch(
                        TerminalEnvironment.ResolveShell(TerminalEnvironment.DefaultRoot),
                        "-l -i",
                        useMsysEnvironment: true);
            }
        }

        private static string ResolvePowerShell()
        {
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string legacy = Path.Combine(
                windows,
                "System32",
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");

            if (File.Exists(legacy))
                return legacy;

            string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string core = Path.Combine(system, "pwsh.exe");

            if (File.Exists(core))
                return core;

            throw new TerminalStartupException($"PowerShell not found: {legacy}");
        }
    }

    /// <summary>
    /// Executable, arguments and environment mode for the configured shell.
    /// </summary>
    internal readonly struct TerminalShellLaunch
    {
        internal TerminalShellLaunch(string path, string arguments, bool useMsysEnvironment)
        {
            Path = path;
            Arguments = arguments;
            UseMsysEnvironment = useMsysEnvironment;
        }

        internal string Path { get; }
        internal string Arguments { get; }
        internal bool UseMsysEnvironment { get; }
    }
}

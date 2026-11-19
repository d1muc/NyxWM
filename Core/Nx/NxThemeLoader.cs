using System;
using System.IO;
using System.Reflection;

namespace nyxWM.Nx
{
    /// <summary>
    /// Loads the main theme from <c>themes/theme.nx</c> next to the executable.
    /// If the file cannot be parsed, the built-in fallback theme (embedded in the
    /// assembly) is used instead and <see cref="ErrorMessage"/> is set. The
    /// WinForms overlays and the Nx runtime read their tokens through this
    /// loader.
    /// </summary>
    internal sealed class NxThemeLoader
    {
        private const string EmbeddedName = "nyxWM.Themes.theme.default.nx";

        internal NxTheme Theme { get; }
        internal string? ErrorMessage { get; }
        internal bool UsedFallback { get; }
        internal string FallbackSource { get; }

        private NxThemeLoader(NxTheme theme, string? error, bool fallback, string fallbackSource)
        {
            Theme = theme;
            ErrorMessage = error;
            UsedFallback = fallback;
            FallbackSource = fallbackSource;
        }

        /// <summary>
        /// Loads <c>themes/{fileName}</c>, falling back to the embedded copy when
        /// the file is missing or broken. <paramref name="autoAccent"/> is the
        /// accent chosen in settings, used where the theme says "auto".
        /// </summary>
        internal static NxThemeLoader Load(string baseDirectory, System.Drawing.Color autoAccent, string themeFileName = "theme.nx")
        {
            string themePath = Path.Combine(baseDirectory, "themes", themeFileName);
            string? loadError = null;

            // Try the editable theme first.
            try
            {
                if (File.Exists(themePath))
                {
                    NxDocument doc = NxParser.Parse(File.ReadAllText(themePath), themePath);
                    var theme = NxTheme.FromTokens(doc.Tokens, autoAccent);
                    return new NxThemeLoader(theme, null, false, GetFallbackSource());
                }
            }
            catch (Exception ex)
            {
                loadError = $"{themeFileName}: {ex.Message}";
            }

            // Parse the embedded fallback.
            string fallbackSource = GetFallbackSource();

            try
            {
                NxDocument doc = NxParser.Parse(fallbackSource, "theme.nx (embedded)");
                var theme = NxTheme.FromTokens(doc.Tokens, autoAccent);
                return new NxThemeLoader(theme, loadError ?? $"{themeFileName} not found, using built-in theme", true, fallbackSource);
            }
            catch (Exception ex)
            {
                // If even the fallback fails (should never happen), use defaults.
                return new NxThemeLoader(
                    NxTheme.CreateDefault(),
                    $"{loadError ?? "theme.nx unreadable"}; built-in fallback also failed: {ex.Message}",
                    true,
                    fallbackSource);
            }
        }

        private static string GetFallbackSource()
        {
            try
            {
                using Stream? stream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream(EmbeddedName);

                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    return reader.ReadToEnd();
                }
            }
            catch
            {
                // Ignore, return empty to trigger defaults.
            }

            return string.Empty;
        }
    }
}
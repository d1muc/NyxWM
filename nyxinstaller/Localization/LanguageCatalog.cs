using NyxInstaller.Models;

namespace NyxInstaller.Localization;

/// <summary>
/// One selectable installer language.
/// </summary>
/// <param name="Language">Enum value stored in <c>InstallOptions</c>.</param>
/// <param name="Code">Suffix of the embedded <c>Strings-&lt;code&gt;.json</c> resource.</param>
/// <param name="Badge">Short code shown on the language card.</param>
/// <param name="NativeName">Language name in its own script.</param>
/// <param name="EnglishName">Language name in English, used as the card subtitle.</param>
public sealed record LanguageInfo(
    InstallerLanguage Language,
    string Code,
    string Badge,
    string NativeName,
    string EnglishName);

/// <summary>
/// The ten installer languages, ordered as they appear on the first page.
/// </summary>
public static class LanguageCatalog
{
    public static IReadOnlyList<LanguageInfo> All { get; } = new[]
    {
        new LanguageInfo(InstallerLanguage.English, "en", "EN", "English", "English"),
        new LanguageInfo(InstallerLanguage.Russian, "ru", "RU", "Русский", "Russian"),
        new LanguageInfo(InstallerLanguage.Spanish, "es", "ES", "Español", "Spanish"),
        new LanguageInfo(InstallerLanguage.ChineseSimplified, "zh", "ZH", "中文（简体）", "Chinese (Simplified)"),
        new LanguageInfo(InstallerLanguage.Hindi, "hi", "HI", "हिन्दी", "Hindi"),
        new LanguageInfo(InstallerLanguage.Arabic, "ar", "AR", "العربية", "Arabic"),
        new LanguageInfo(InstallerLanguage.Portuguese, "pt", "PT", "Português", "Portuguese"),
        new LanguageInfo(InstallerLanguage.French, "fr", "FR", "Français", "French"),
        new LanguageInfo(InstallerLanguage.German, "de", "DE", "Deutsch", "German"),
        new LanguageInfo(InstallerLanguage.Japanese, "ja", "JA", "日本語", "Japanese")
    };

    public static LanguageInfo Get(InstallerLanguage language) =>
        All.First(info => info.Language == language);
}

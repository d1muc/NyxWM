using System.Reflection;
using System.Text.Json;
using NyxInstaller.Models;

namespace NyxInstaller.Localization;

/// <summary>
/// Loads both string tables from embedded resources and serves the active language.
/// </summary>
public sealed class Localizer
{
    private readonly IReadOnlyDictionary<InstallerLanguage, IReadOnlyDictionary<string, string>> _tables;

    private Localizer(
        IReadOnlyDictionary<InstallerLanguage, IReadOnlyDictionary<string, string>> tables)
    {
        _tables = tables;
    }

    public InstallerLanguage Language { get; private set; } = InstallerLanguage.English;

    /// <summary>
    /// Loads <c>Strings.en.json</c> and <c>Strings.ru.json</c> from the assembly resources.
    /// </summary>
    public static Localizer Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceNames = assembly.GetManifestResourceNames();

        var tables = new Dictionary<InstallerLanguage, IReadOnlyDictionary<string, string>>();
        foreach (var language in new[] { InstallerLanguage.English, InstallerLanguage.Russian })
        {
            var suffix = language == InstallerLanguage.English
                ? "Strings-en.json"
                : "Strings-ru.json";

            var resourceName = resourceNames.FirstOrDefault(n => n.EndsWith(suffix, StringComparison.Ordinal));
            if (resourceName == null)
                continue;

            using var stream = assembly.GetManifestResourceStream(resourceName)!;
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            var strings = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                          ?? new Dictionary<string, string>();
            tables[language] = strings;
        }

        return new Localizer(tables);
    }

    public void SetLanguage(InstallerLanguage language) => Language = language;

    /// <summary>
    /// Returns the localized string for <paramref name="key"/>, or the key itself when missing.
    /// </summary>
    public string Get(string key)
    {
        if (_tables.TryGetValue(Language, out var table) && table.TryGetValue(key, out var value))
            return value;
        return key;
    }

    /// <summary>
    /// Formats a localized string with positional arguments.
    /// </summary>
    public string Format(string key, params object[] args) => string.Format(Get(key), args);
}

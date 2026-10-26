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
    /// Loads every <c>Strings-&lt;code&gt;.json</c> table from the assembly resources.
    /// </summary>
    public static Localizer Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceNames = assembly.GetManifestResourceNames();

        var tables = new Dictionary<InstallerLanguage, IReadOnlyDictionary<string, string>>();
        foreach (var info in LanguageCatalog.All)
        {
            var suffix = $"Strings-{info.Code}.json";
            var resourceName = resourceNames.FirstOrDefault(n => n.EndsWith(suffix, StringComparison.Ordinal));
            if (resourceName == null)
                continue;

            using var stream = assembly.GetManifestResourceStream(resourceName)!;
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            var strings = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                          ?? new Dictionary<string, string>();
            tables[info.Language] = strings;
        }

        return new Localizer(tables);
    }

    public void SetLanguage(InstallerLanguage language) => Language = language;

    /// <summary>
    /// Returns the localized string for <paramref name="key"/>, falling back to English and then
    /// to the key itself when missing.
    /// </summary>
    public string Get(string key)
    {
        if (_tables.TryGetValue(Language, out var table) && table.TryGetValue(key, out var value))
            return value;

        if (_tables.TryGetValue(InstallerLanguage.English, out var english) &&
            english.TryGetValue(key, out var fallback))
        {
            return fallback;
        }

        return key;
    }

    /// <summary>
    /// Formats a localized string with positional arguments.
    /// </summary>
    public string Format(string key, params object[] args) => string.Format(Get(key), args);
}

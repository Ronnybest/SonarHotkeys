using System.Globalization;
using System.Text.Json;

namespace SonarHotkeys;

public sealed record LanguageInfo(string Code, string Name);

/// <summary>
/// Interface texts by key, one embedded Strings/&lt;code&gt;.json file per language. Adding a language means adding
/// a file: it appears in the language list automatically, and keys it lacks fall back to English.
/// </summary>
public static class TextCatalog
{
    public const string FallbackLanguage = "en";
    private const string ResourcePrefix = "SonarHotkeys.Strings.", ResourceSuffix = ".json", NameKey = "Language.Name";

    private static readonly Dictionary<string, Dictionary<string, string>> Catalogs = Load();

    /// <summary>Available languages, each named in its own language.</summary>
    public static IReadOnlyList<LanguageInfo> Languages { get; } = Catalogs
        .Select(c => new LanguageInfo(c.Key, c.Value.GetValueOrDefault(NameKey, c.Key)))
        .OrderBy(l => l.Name, StringComparer.InvariantCultureIgnoreCase).ToList();

    /// <summary>The Windows display language when a catalog exists for it, otherwise English.</summary>
    public static string DefaultLanguage => Catalogs.ContainsKey(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)
        ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName : FallbackLanguage;

    public static string NormalizeLanguage(string? language) =>
        language != null && Catalogs.ContainsKey(language) ? language : DefaultLanguage;

    private static Dictionary<string, Dictionary<string, string>> Load()
    {
        var assembly = typeof(TextCatalog).Assembly;
        var catalogs = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(ResourcePrefix, StringComparison.Ordinal) || !name.EndsWith(ResourceSuffix, StringComparison.Ordinal)) continue;
            using var stream = assembly.GetManifestResourceStream(name)!;
            catalogs[name[ResourcePrefix.Length..^ResourceSuffix.Length]] = JsonSerializer.Deserialize(stream, CoreJson.Default.DictionaryStringString)!;
        }
        if (!catalogs.ContainsKey(FallbackLanguage)) throw new InvalidOperationException("The English text catalog is missing.");
        return catalogs;
    }

    public static string Get(string key, string? language = null, params object[] arguments)
    {
        string text = Catalogs[NormalizeLanguage(language)].TryGetValue(key, out var translated)
            || Catalogs[FallbackLanguage].TryGetValue(key, out translated) ? translated : key;
        return arguments.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, arguments);
    }
}

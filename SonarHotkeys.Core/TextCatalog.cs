using System.Globalization;
using System.Text.Json;

namespace SonarHotkeys;

public static class TextCatalog
{
    private static readonly Dictionary<string, string> English = Load();

    public static string DefaultLanguage => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? "ru" : "en";
    public static string NormalizeLanguage(string? language) => language is "ru" or "en" ? language : DefaultLanguage;

    private static Dictionary<string, string> Load()
    {
        using var stream = typeof(TextCatalog).Assembly.GetManifestResourceStream("SonarHotkeys.Translations.json")
            ?? throw new InvalidOperationException("Translation resources are missing.");
        return JsonSerializer.Deserialize(stream, CoreJson.Default.DictionaryStringString)!;
    }

    public static string Get(string key, string? language = null, params object[] arguments)
    {
        string text = NormalizeLanguage(language) == "en" && English.TryGetValue(key, out var translated) ? translated : key;
        return arguments.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, arguments);
    }
}

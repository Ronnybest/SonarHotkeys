using System.Text.Json;
using System.Text.Json.Serialization;

namespace SonarHotkeys;

public sealed record Choice(string Id, string Name);

public sealed class PresetBinding
{
    public string PresetId { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string Hotkey { get; set; } = "";
}

public sealed class AppSettings
{
    public List<PresetBinding> Bindings { get; set; } = [];
    public List<Choice> Favorites { get; set; } = [];
    public List<Choice> Devices { get; set; } = [];
    public string CycleHotkey { get; set; } = "";
    public string Language { get; set; } = TextCatalog.DefaultLanguage;

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SonarHotkeys", "settings.json");

    public static AppSettings Load(string? filePath = null)
    {
        filePath ??= FilePath;
        if (!File.Exists(filePath)) return Defaults();
        var settings = JsonSerializer.Deserialize(File.ReadAllText(filePath), CoreJson.Default.AppSettings)
            ?? throw new InvalidDataException(TextCatalog.Get("Settings.Empty"));
        settings.Language = TextCatalog.NormalizeLanguage(settings.Language);
        if (settings.Bindings is null || settings.Favorites is null || settings.Devices is null ||
            settings.CycleHotkey is null || settings.Bindings.Any(b => b is null || b.PresetId is null || b.DeviceId is null || b.Hotkey is null) ||
            settings.Favorites.Concat(settings.Devices).Any(c => c is null || c.Id is null || c.Name is null))
            throw new InvalidDataException(TextCatalog.Get("Settings.Invalid", settings.Language));
        return settings;
    }

    public void Save(string? filePath = null)
    {
        filePath = Path.GetFullPath(filePath ?? FilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        string temporary = filePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, CoreJson.Default.AppSettings));
        File.Move(temporary, filePath, true);
    }

    // Every user selects presets, outputs and shortcuts from their own installation.
    public static AppSettings Defaults() => new();

    /// <summary>Throws <see cref="ArgumentException"/> unless every binding has a preset and presets and shortcuts are unique.</summary>
    public void Validate()
    {
        var used = new HashSet<Hotkey>();
        var presetIds = new HashSet<string>();
        foreach (var row in Bindings)
        {
            if (string.IsNullOrEmpty(row.PresetId)) throw new ArgumentException(TextCatalog.Get("Validation.PresetMissing", Language));
            if (!presetIds.Add(row.PresetId)) throw new ArgumentException(TextCatalog.Get("Validation.PresetRepeated", Language));
            if (Hotkey.Parse(row.Hotkey, Language) is { } key && !used.Add(key)) throw new ArgumentException(TextCatalog.Get("Validation.ShortcutRepeated", Language));
        }
        if (Hotkey.Parse(CycleHotkey, Language) is { } cycle && !used.Add(cycle))
            throw new ArgumentException(TextCatalog.Get("Validation.CycleTaken", Language));
    }
}

// Source-generated serialization keeps settings and translations working under Native AOT.
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class CoreJson : JsonSerializerContext;

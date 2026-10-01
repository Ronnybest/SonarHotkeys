using System.Text.Json;
using System.Text.Json.Serialization;

namespace SonarHotkeys;

public sealed record Choice(string Id, string Name);

/// <summary>A Sonar channel with its own presets.</summary>
public enum SonarChannel { Game, Chat, Media, Aux, Mic }

/// <summary>A preset of one channel, optionally with a device and its own shortcut.</summary>
public sealed class PresetBinding
{
    public string PresetId { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string Hotkey { get; set; } = "";
}

public sealed class ChannelSettings
{
    /// <summary>The configured presets, in the order the next preset shortcut steps through them.</summary>
    public List<PresetBinding> Presets { get; set; } = [];

    /// <summary>Favorite presets the user removed here, which are therefore not added again from GG.</summary>
    public List<string> Removed { get; set; } = [];
}

public sealed class AppSettings
{
    public Dictionary<SonarChannel, ChannelSettings> Channels { get; set; } = [];
    /// <summary>The channel whose presets the next preset shortcut steps through.</summary>
    public SonarChannel CycleChannel { get; set; } = SonarChannel.Game;
    /// <summary>The presets offered for each channel, as last read from Sonar.</summary>
    public Dictionary<SonarChannel, List<Choice>> KnownPresets { get; set; } = [];
    public List<Choice> Outputs { get; set; } = [];
    public List<Choice> Inputs { get; set; } = [];
    /// <summary>
    /// In Classic mode, an output preset's device routes every output channel, not only its own, as versions 1 and 2 did.
    /// Streamer mode has no per-channel outputs and always routes the Personal mix.
    /// </summary>
    public bool SwitchAllSound { get; set; } = true;
    /// <summary>Ctrl+Alt+1…9 apply the preset at that place in the cycled channel's list.</summary>
    public bool NumberHotkeys { get; set; } = true;
    public const string DefaultChannelHotkey = "Ctrl + Alt + F11", DefaultCycleHotkey = "Ctrl + Alt + F12";
    public string ChannelHotkey { get; set; } = DefaultChannelHotkey;
    public string CycleHotkey { get; set; } = DefaultCycleHotkey;
    public string Language { get; set; } = TextCatalog.DefaultLanguage;

    // Read from settings of versions 1 and 2, which had Game presets only, and never written again.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<PresetBinding>? Bindings { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<Choice>? Favorites { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<Choice>? Devices { get; set; }

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SonarHotkeys", "settings.json");

    public ChannelSettings Channel(SonarChannel channel) => Channels[channel];

    public static AppSettings Load(string? filePath = null)
    {
        filePath ??= FilePath;
        if (!File.Exists(filePath)) return Defaults();
        var settings = JsonSerializer.Deserialize(File.ReadAllText(filePath), CoreJson.Default.AppSettings)
            ?? throw new InvalidDataException(TextCatalog.Get("Settings.Empty"));
        settings.Language = TextCatalog.NormalizeLanguage(settings.Language);
        static bool Invalid(PresetBinding? b) => b is null || b.PresetId is null || b.DeviceId is null || b.Hotkey is null;
        if (settings.Channels is null || settings.KnownPresets is null || settings.Outputs is null || settings.Inputs is null ||
            settings.ChannelHotkey is null || settings.CycleHotkey is null ||
            settings.Channels.Values.Any(s => s is null || s.Presets is null || s.Presets.Any(Invalid) || s.Removed is null || s.Removed.Any(id => id is null)) ||
            settings.KnownPresets.Values.Any(list => list is null) ||
            settings.KnownPresets.Values.SelectMany(list => list).Concat(settings.Outputs).Concat(settings.Inputs)
                .Concat(settings.Favorites ?? []).Concat(settings.Devices ?? []).Any(c => c is null || c.Id is null || c.Name is null) ||
            settings.Bindings?.Any(Invalid) == true || !Enum.IsDefined(settings.CycleChannel))
            throw new InvalidDataException(TextCatalog.Get("Settings.Invalid", settings.Language));
        settings.Upgrade();
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

    // Every user selects presets, devices and shortcuts from their own installation.
    public static AppSettings Defaults()
    {
        var settings = new AppSettings();
        settings.Upgrade();
        return settings;
    }

    /// <summary>
    /// Adds the channels missing from the file and moves the Game bindings of versions 1 and 2 into the Game channel.
    /// Their device switched every output channel, which <see cref="SwitchAllSound"/>, on by default, keeps doing.
    /// </summary>
    private void Upgrade()
    {
        foreach (var channel in Enum.GetValues<SonarChannel>())
            if (!Channels.ContainsKey(channel)) Channels[channel] = new();
        if (Bindings != null && Channel(SonarChannel.Game).Presets.Count == 0) Channel(SonarChannel.Game).Presets = Bindings;
        if (Favorites != null && !KnownPresets.ContainsKey(SonarChannel.Game)) KnownPresets[SonarChannel.Game] = Favorites;
        if (Devices != null && Outputs.Count == 0) Outputs = Devices;
        Bindings = null;
        Favorites = Devices = null;
        // A default cycling shortcut that a preset of an earlier version already uses is left unassigned.
        static Hotkey? Key(string text) { try { return Hotkey.Parse(text, "en"); } catch (ArgumentException) { return null; } }
        // Ctrl+Alt+1…9 select presets by their place now, so a preset of an earlier version that used one gives it up.
        var numbers = NumberKeys();
        foreach (var preset in Channels.Values.SelectMany(c => c.Presets))
            if (Key(preset.Hotkey) is { } number && numbers.Contains(number)) preset.Hotkey = "";
        var presetKeys = Channels.Values.SelectMany(c => c.Presets).Select(p => Key(p.Hotkey)).OfType<Hotkey>().ToHashSet();
        if (ChannelHotkey == DefaultChannelHotkey && (presetKeys.Contains(Key(ChannelHotkey)!.Value) || Key(CycleHotkey) == Key(ChannelHotkey)))
            ChannelHotkey = "";
        if (CycleHotkey == DefaultCycleHotkey && presetKeys.Contains(Key(CycleHotkey)!.Value)) CycleHotkey = "";
    }

    /// <summary>Channels with at least one preset, in their fixed order.</summary>
    public List<SonarChannel> ConfiguredChannels() =>
        [.. Enum.GetValues<SonarChannel>().Where(c => Channel(c).Presets.Count > 0)];

    /// <summary>The configured channel after the cycled one, or null when no channel has presets.</summary>
    public SonarChannel? NextChannel()
    {
        var configured = ConfiguredChannels();
        if (configured.Count == 0) return null;
        return configured.FirstOrDefault(c => c > CycleChannel, configured[0]);
    }

    /// <summary>Throws <see cref="ArgumentException"/> unless every row has a preset, presets are unique in their channel and shortcuts are unique.</summary>
    public void Validate()
    {
        var numbers = NumberKeys();
        var used = new HashSet<Hotkey>();
        void Use(Hotkey key, string takenKey)
        {
            if (numbers.Contains(key)) throw new ArgumentException(TextCatalog.Get("Validation.NumberTaken", Language));
            if (!used.Add(key)) throw new ArgumentException(TextCatalog.Get(takenKey, Language));
        }
        foreach (var channel in Enum.GetValues<SonarChannel>())
        {
            var presetIds = new HashSet<string>();
            foreach (var row in Channel(channel).Presets)
            {
                if (string.IsNullOrEmpty(row.PresetId)) throw new ArgumentException(TextCatalog.Get("Validation.PresetMissing", Language));
                if (!presetIds.Add(row.PresetId)) throw new ArgumentException(TextCatalog.Get("Validation.PresetRepeated", Language));
                if (Hotkey.Parse(row.Hotkey, Language) is { } key) Use(key, "Validation.ShortcutRepeated");
            }
        }
        if (Hotkey.Parse(ChannelHotkey, Language) is { } channelKey) Use(channelKey, "Validation.ChannelTaken");
        if (Hotkey.Parse(CycleHotkey, Language) is { } cycle) Use(cycle, "Validation.CycleTaken");
    }

    /// <summary>The shortcut that applies the preset at a place, from 1 to 9, in the cycled channel.</summary>
    public static Hotkey NumberHotkey(int number) => new(Hotkey.Control | Hotkey.Alt, (uint)('0' + number));

    public static string NumberHotkeyText(int number) => "Ctrl + Alt + " + number;

    /// <summary>The number shortcuts in use: none when they are turned off.</summary>
    public HashSet<Hotkey> NumberKeys() => NumberHotkeys ? [.. Enumerable.Range(1, 9).Select(NumberHotkey)] : [];

    /// <summary>
    /// Appends the favorite presets of each channel that its list does not have yet, unless the user removed them,
    /// so presets favorited in GG appear without adding them by hand. Returns how many were added.
    /// </summary>
    public int AddNewFavorites(IReadOnlyDictionary<SonarChannel, List<string>> favorites)
    {
        int added = 0;
        foreach (var (channel, ids) in favorites)
        {
            var saved = Channel(channel);
            foreach (string id in ids)
            {
                if (saved.Presets.Any(p => p.PresetId == id) || saved.Removed.Contains(id)) continue;
                saved.Presets.Add(new() { PresetId = id });
                added++;
            }
        }
        return added;
    }

    public static bool IsOutput(SonarChannel channel) => channel != SonarChannel.Mic;

    public static string ChannelKey(SonarChannel channel) => channel switch
    {
        SonarChannel.Game => "Channel.Game",
        SonarChannel.Chat => "Channel.Chat",
        SonarChannel.Media => "Channel.Media",
        SonarChannel.Aux => "Channel.Aux",
        _ => "Channel.Mic",
    };
}

// Source-generated serialization keeps settings and translations working under Native AOT.
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class CoreJson : JsonSerializerContext;

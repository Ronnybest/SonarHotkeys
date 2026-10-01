using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using SonarHotkeys;
using SteelSeriesAPI.Core;

int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    checks++;
}

static string SourceDirectory(string project, [CallerFilePath] string path = "") =>
    Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", project));

// Text keys: English is the reference catalog; every other language must cover the same keys.
string sources = string.Concat(new[] { "SonarHotkeys.Core", "SonarHotkeys.WinUI" }
    .SelectMany(project => Directory.GetFiles(SourceDirectory(project), "*.cs")).Select(File.ReadAllText));
var catalogs = Directory.GetFiles(Path.Combine(SourceDirectory("SonarHotkeys.Core"), "Strings"), "*.json")
    .ToDictionary(file => Path.GetFileNameWithoutExtension(file)!, file => JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file))!);
var english = catalogs["en"];
// Key-like literals ("Group.Name") whose group exists in the catalog, including keys chosen by a condition.
var groups = english.Keys.Select(k => k.Split('.')[0]).ToHashSet();
var usedKeys = Regex.Matches(sources, @"""([A-Z][A-Za-z]+)\.([A-Z][A-Za-z]+)""")
    .Where(m => groups.Contains(m.Groups[1].Value)).Select(m => m.Groups[1].Value + "." + m.Groups[2].Value).ToHashSet();
var missing = usedKeys.Where(k => !english.ContainsKey(k)).ToList();
var unused = english.Keys.Where(k => k != "Language.Name" && !usedKeys.Contains(k)).ToList();
Check(missing.Count == 0, "every key used in code exists in en.json: " + string.Join(" | ", missing));
Check(unused.Count == 0, "en.json has no stale keys: " + string.Join(" | ", unused));
Check(usedKeys.Count > 50, "the key scan finds the UI texts");
static string Placeholders(string text) => string.Join(",", Regex.Matches(text, @"\{\d+\}").Select(m => m.Value).Order());
foreach (var (code, catalog) in catalogs)
{
    var absent = english.Keys.Except(catalog.Keys).ToList();
    var extra = catalog.Keys.Except(english.Keys).ToList();
    Check(absent.Count == 0 && extra.Count == 0, $"{code}.json has the same keys as en.json: missing {string.Join(", ", absent)}; extra {string.Join(", ", extra)}");
    var mismatched = catalog.Where(p => Placeholders(p.Value) != Placeholders(english[p.Key])).Select(p => p.Key).ToList();
    Check(mismatched.Count == 0, $"{code}.json keeps the placeholders of en.json: {string.Join(", ", mismatched)}");
}
Check(TextCatalog.Languages.Select(l => l.Code).Order().SequenceEqual(catalogs.Keys.Order(StringComparer.OrdinalIgnoreCase)),
    "every embedded catalog is offered as a language");
Check(TextCatalog.Languages.Single(l => l.Code == "ru").Name == "Русский" && TextCatalog.Get("Page.Add", "ru") == "Добавить",
    "a language is named in its own language and translated");
Check(TextCatalog.NormalizeLanguage("xx") == TextCatalog.DefaultLanguage && TextCatalog.Get("No.SuchKey", "en") == "No.SuchKey",
    "unknown languages and keys fall back safely");

string directory = Path.Combine(Path.GetTempPath(), "SonarHotkeysChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    string path = Path.Combine(directory, "profile", "settings.json");
    var fresh = AppSettings.Load(path);
    Check(Enum.GetValues<SonarChannel>().All(c => fresh.Channel(c).Presets.Count == 0) && fresh.KnownPresets.Count == 0 &&
        fresh.Outputs.Count == 0 && fresh.Inputs.Count == 0 && fresh.ChannelHotkey == "Ctrl + Alt + F11" && fresh.CycleHotkey == "Ctrl + Alt + F12",
        "fresh installation contains no personal data and only the default cycling shortcuts");
    Check(Enum.GetValues<SonarChannel>().All(fresh.Channels.ContainsKey) && fresh.SwitchAllSound &&
        fresh.CycleChannel == SonarChannel.Game, "every channel exists, and a device switches all sound by default");
    Check(!File.Exists(path), "reading defaults does not write settings");
    var other = AppSettings.Defaults();
    fresh.Channel(SonarChannel.Game).Presets.Add(new() { PresetId = "custom-preset", DeviceId = "custom-output", Hotkey = "Ctrl + Shift + F8" });
    fresh.Channel(SonarChannel.Mic).Presets.Add(new() { PresetId = "mic-preset", DeviceId = "custom-mic" });
    Check(other.Channel(SonarChannel.Game).Presets.Count == 0, "defaults do not share mutable state");
    fresh.KnownPresets[SonarChannel.Game] = [new("custom-preset", "User preset")];
    fresh.KnownPresets[SonarChannel.Mic] = [new("mic-preset", "Voice")];
    fresh.Outputs.Add(new("custom-output", "User output"));
    fresh.Inputs.Add(new("custom-mic", "User mic"));
    fresh.CycleChannel = SonarChannel.Mic;
    fresh.ChannelHotkey = "Alt + F8";
    fresh.CycleHotkey = "Alt + F9";
    fresh.SwitchAllSound = false;
    fresh.Save(path);
    var saved = AppSettings.Load(path);
    Check(saved.Channel(SonarChannel.Game).Presets.Single().DeviceId == "custom-output" && saved.Channel(SonarChannel.Mic).Presets.Single().DeviceId == "custom-mic" &&
        saved.KnownPresets[SonarChannel.Mic].Single().Name == "Voice" && saved.Inputs.Single().Name == "User mic" &&
        saved.CycleChannel == SonarChannel.Mic && saved.ChannelHotkey == "Alt + F8" && saved.CycleHotkey == "Alt + F9" && !saved.SwitchAllSound,
        "arbitrary user settings survive restart");
    string written = File.ReadAllText(path);
    Check(!written.Contains("Bindings") && written.Contains("\"CycleChannel\": \"Mic\""), "settings are written with channel names");
    saved.Language = "en";
    saved.Save(path);
    Check(AppSettings.Load(path).Language == "en", "language survives restart");
    File.WriteAllText(path, "{\"Language\":\"unknown\"}");
    Check(AppSettings.Load(path).Language is "ru" or "en", "unsupported language falls back safely");
    saved.Channel(SonarChannel.Game).Presets[0].DeviceId = "";
    saved.Channel(SonarChannel.Game).Presets[0].Hotkey = "";
    saved.Save(path);
    Check(AppSettings.Load(path).Channel(SonarChannel.Game).Presets[0].DeviceId == "" && !File.Exists(path + ".tmp"), "overwrite and preset-only binding");

    saved.CycleChannel = SonarChannel.Game;
    Check(saved.ConfiguredChannels().SequenceEqual([SonarChannel.Game, SonarChannel.Mic]) && saved.NextChannel() == SonarChannel.Mic,
        "the next channel skips channels without presets");
    saved.CycleChannel = SonarChannel.Mic;
    Check(saved.NextChannel() == SonarChannel.Game, "the next channel wraps around");
    saved.CycleChannel = SonarChannel.Chat;
    Check(saved.NextChannel() == SonarChannel.Mic, "the next channel follows a cycled channel without presets");
    Check(AppSettings.Defaults().NextChannel() == null, "no next channel without presets");

    // Presets favorited in GG are added by themselves, after the configured ones, unless the user removed them.
    var synced = AppSettings.Defaults();
    synced.Channel(SonarChannel.Game).Presets.Add(new() { PresetId = "fps", DeviceId = "headphones", Hotkey = "Ctrl + Shift + 1" });
    synced.Channel(SonarChannel.Media).Removed.Add("movie");
    var favorites = new Dictionary<SonarChannel, List<string>> { [SonarChannel.Game] = ["fps", "rpg"], [SonarChannel.Media] = ["music", "movie"] };
    Check(synced.AddNewFavorites(favorites) == 2 &&
        synced.Channel(SonarChannel.Game).Presets.Select(p => p.PresetId).SequenceEqual(["fps", "rpg"]) &&
        synced.Channel(SonarChannel.Game).Presets[0].Hotkey == "Ctrl + Shift + 1" && synced.Channel(SonarChannel.Game).Presets[1].Hotkey == "" &&
        synced.Channel(SonarChannel.Media).Presets.Single().PresetId == "music",
        "new favorites are appended without a shortcut, keeping configured presets and skipping removed ones");
    Check(synced.AddNewFavorites(favorites) == 0, "adding favorites again changes nothing");
    synced.Save(path);
    Check(AppSettings.Load(path).Channel(SonarChannel.Media).Removed.Single() == "movie", "removed favorites survive restart");

    // Settings of versions 1 and 2 had Game presets only, and their device switched every output channel.
    File.WriteAllText(path, """
        {"Bindings":[{"PresetId":"fps","DeviceId":"headphones","Hotkey":"Ctrl + Alt + F1"},{"PresetId":"music","DeviceId":"","Hotkey":""}],
         "Favorites":[{"Id":"fps","Name":"FPS"},{"Id":"music","Name":"Music"}],"Devices":[{"Id":"headphones","Name":"Headphones"}],
         "CycleHotkey":"Ctrl + Alt + F12","Language":"en"}
        """);
    var upgraded = AppSettings.Load(path);
    var game = upgraded.Channel(SonarChannel.Game);
    Check(game.Presets.Count == 2 && game.Presets[0].PresetId == "fps" && game.Presets[0].DeviceId == "headphones" &&
        game.Presets[0].Hotkey == "Ctrl + Alt + F1" && upgraded.SwitchAllSound && upgraded.CycleChannel == SonarChannel.Game &&
        upgraded.KnownPresets[SonarChannel.Game].Count == 2 && upgraded.Outputs.Single().Name == "Headphones" &&
        upgraded.CycleHotkey == "Ctrl + Alt + F12" && upgraded.ChannelHotkey == "Ctrl + Alt + F11",
        "bindings of versions 1 and 2 become Game presets that keep switching every output");
    File.WriteAllText(path, """{"Bindings":[{"PresetId":"fps","DeviceId":"","Hotkey":"Alt + Ctrl + F11"}],"CycleHotkey":""}""");
    var taken = AppSettings.Load(path);
    Check(taken.ChannelHotkey == "" && taken.CycleHotkey == "" && taken.Channel(SonarChannel.Game).Presets[0].Hotkey == "Alt + Ctrl + F11",
        "a default shortcut that an old preset already uses stays unassigned, and a cleared one stays cleared");
    File.WriteAllText(path, """{"Bindings":[{"PresetId":"fps","DeviceId":"","Hotkey":"Ctrl + Alt + D1"},{"PresetId":"rpg","DeviceId":"","Hotkey":"Ctrl + Shift + F2"}]}""");
    var numbered = AppSettings.Load(path).Channel(SonarChannel.Game).Presets;
    Check(numbered[0].Hotkey == "" && numbered[1].Hotkey == "Ctrl + Shift + F2",
        "an old Ctrl+Alt+digit shortcut gives way to the number shortcut of the same place, other shortcuts stay");
    upgraded.Save(path);
    string existing = File.ReadAllText(path);
    Check(!existing.Contains("Bindings") && !existing.Contains("Favorites") && !existing.Contains("\"Devices\"") &&
        AppSettings.Load(path).Channel(SonarChannel.Game).Presets.Count == 2, "upgraded settings are saved without the old fields");
    Check(JsonSerializer.Deserialize<AppSettings>(existing, new JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } })!
        .Channels[SonarChannel.Game].Presets[0].PresetId == "fps", "serialized data remains readable");

    foreach (string invalid in new[] { "{", "null", "{\"Channels\":null}", "{\"Outputs\":[null]}", "{\"Channels\":{\"Game\":null}}", "{\"Channels\":{\"Game\":{\"Removed\":[null]}}}",
        "{\"Channels\":{\"Game\":{\"Presets\":[{\"PresetId\":null}]}}}", "{\"Channels\":{\"Master\":{}}}", "{\"KnownPresets\":{\"Game\":[null]}}",
        "{\"CycleChannel\":\"Master\"}", "{\"CycleChannel\":9}", "{\"Bindings\":[{\"PresetId\":null}]}", "{\"Devices\":[null]}" })
    {
        File.WriteAllText(path, invalid);
        bool rejected = false;
        try { AppSettings.Load(path); } catch (Exception ex) when (ex is JsonException or InvalidDataException) { rejected = true; }
        Check(rejected && File.ReadAllText(path) == invalid, "corrupt settings are rejected without overwriting: " + invalid);
    }

    Hotkey? Parse(string value) => Hotkey.Parse(value, "en");
    Check(Parse("") == null, "unassigned shortcut");
    Check(Parse("Ctrl+Alt+1") == Parse("Alt + Ctrl + D1"), "shortcut alias and order");
    Check(Parse("Ctrl + Alt + F12") == new Hotkey(Hotkey.Control | Hotkey.Alt, (uint)Keys.F12), "modifier flags and virtual key");
    foreach (string invalid in new[] { "Ctrl", "A", "Ctrl + Unknown", "Ctrl + A + B", "Ctrl + ControlKey", "Ctrl + None" })
    {
        bool rejected = false;
        try { Parse(invalid); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "invalid shortcut: " + invalid);
    }
    // Shortcuts saved by the WinForms version use Keys names: every non-modifier key must round-trip.
    var modifierKeys = new[] { Keys.None, Keys.ControlKey, Keys.Menu, Keys.ShiftKey, Keys.LWin, Keys.RWin };
    var legacyKeys = Enum.GetValues<Keys>().Where(k => (int)k is > 0 and <= 255 && !modifierKeys.Contains(k)).Distinct().ToList();
    Check(legacyKeys.All(k => Parse("Ctrl + " + k)?.Key == (uint)k) &&
        Enum.GetNames<Keys>().Where(n => legacyKeys.Contains(Enum.Parse<Keys>(n))).All(n => Parse("Alt + " + n)?.Key == (uint)Enum.Parse<Keys>(n)),
        "every WinForms key name parses to its virtual key");
    Check(legacyKeys.All(k => Hotkey.Format(true, false, true, (uint)k) == "Ctrl + Shift + " + k),
        "captured keys are written with WinForms names");
    Check(Hotkey.Format(true, true, false, (uint)Keys.ControlKey) == null && Hotkey.Format(false, false, false, (uint)Keys.A) == null,
        "modifier-only or unmodified presses are not captured");
    string? ValidationError(string channel, string cycle, PresetBinding[] game, params PresetBinding[] mic)
    {
        var settings = AppSettings.Defaults();
        settings.Language = "en";
        settings.ChannelHotkey = channel;
        settings.CycleHotkey = cycle;
        settings.Channel(SonarChannel.Game).Presets = [.. game];
        settings.Channel(SonarChannel.Mic).Presets = [.. mic];
        try { settings.Validate(); return null; }
        catch (ArgumentException ex) { return ex.Message; }
    }
    PresetBinding Row(string preset, string hotkey) => new() { PresetId = preset, Hotkey = hotkey };
    Check(ValidationError("Ctrl + Alt + F11", "Ctrl + Alt + F12", [Row("a", "Ctrl + Shift + 1"), Row("b", "")], Row("a", "Ctrl + Shift + 2")) == null,
        "valid presets pass validation, including the same id in another channel");
    Check(ValidationError("", "", [Row("", "")]) == "Choose a preset for each row or remove the empty one.", "a row without a preset is rejected");
    Check(ValidationError("", "", [Row("a", ""), Row("a", "Ctrl + F1")]) == "Each preset can be added to a channel only once.", "a repeated preset in a channel is rejected");
    Check(ValidationError("", "", [Row("a", "Ctrl+Shift+1")], Row("b", "Shift + Ctrl + D1")) == "Shortcuts must be unique.",
        "the same shortcut in another spelling and another channel is rejected");
    Check(ValidationError("Ctrl + Shift + 1", "", [Row("a", "Ctrl + Shift + D1")]) == "The change cycled channel shortcut is already assigned to a preset.",
        "the change cycled channel shortcut cannot reuse a preset shortcut");
    Check(ValidationError("Alt + F1", "Alt + F1", []) == "The next preset shortcut is already in use.", "the two cycling shortcuts differ");
    Check(ValidationError("", "", [Row("a", "Ctrl + Alt + 3")]) == "Ctrl+Alt+1…9 select presets by number. Choose another shortcut or turn preset numbers off in settings."
        && ValidationError("Alt + Ctrl + D9", "", []) != null, "a preset or cycling shortcut cannot take a number shortcut");
    var numbersOff = AppSettings.Defaults();
    numbersOff.NumberHotkeys = false;
    numbersOff.Channel(SonarChannel.Game).Presets.Add(Row("a", "Ctrl + Alt + 3"));
    numbersOff.Validate();
    Check(numbersOff.NumberKeys().Count == 0 && AppSettings.Defaults().NumberKeys().SetEquals(Enumerable.Range(1, 9).Select(n => Parse("Ctrl + Alt + " + n)!.Value)) &&
        AppSettings.NumberHotkeyText(4) == "Ctrl + Alt + 4", "number shortcuts are Ctrl+Alt+1…9, and turning them off frees them");
    // Autostart against a scratch folder, never the real Startup folder.
    var autostart = new Autostart(Path.Combine(directory, "Startup"));
    Check(!autostart.IsEnabled, "autostart is off until enabled");
    autostart.Enable(@"C:\Apps\SonarHotkeys\SonarHotkeys.exe");
    Check(autostart.IsEnabled && autostart.Read() == (@"C:\Apps\SonarHotkeys\SonarHotkeys.exe", "--tray"),
        "the Startup shortcut starts the app hidden in the tray");
    Check(autostart.Refresh(@"D:\Moved\SonarHotkeys.exe") && autostart.Read().Target == @"D:\Moved\SonarHotkeys.exe"
        && !autostart.Refresh(@"D:\Moved\SonarHotkeys.exe"), "a moved app updates its shortcut once");
    autostart.Disable();
    autostart.Disable();
    Check(!autostart.IsEnabled && !autostart.Refresh(@"D:\Moved\SonarHotkeys.exe"), "disabling removes the shortcut and refresh does not recreate it");
    Check(new SonarService(() => "en").ErrorText(new SonarNotRunningException()) == "Sonar is unavailable. Enable Sonar in SteelSeries GG.",
        "Sonar errors use the selected language");
    Console.WriteLine($"PASS: {checks} checks. No real user settings or audio routes were modified.");
}
finally
{
    if (!Path.GetFullPath(directory).StartsWith(Path.GetFullPath(Path.GetTempPath()) + "SonarHotkeysChecks-", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Unexpected test directory.");
    Directory.Delete(directory, recursive: true);
}

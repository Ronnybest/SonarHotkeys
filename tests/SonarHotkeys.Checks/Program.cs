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
Check(TextCatalog.Languages.Single(l => l.Code == "ru").Name == "Русский" && TextCatalog.Get("Toolbar.Add", "ru") == "Добавить",
    "a language is named in its own language and translated");
Check(TextCatalog.NormalizeLanguage("xx") == TextCatalog.DefaultLanguage && TextCatalog.Get("No.SuchKey", "en") == "No.SuchKey",
    "unknown languages and keys fall back safely");

string directory = Path.Combine(Path.GetTempPath(), "SonarHotkeysChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    string path = Path.Combine(directory, "profile", "settings.json");
    var fresh = AppSettings.Load(path);
    Check(fresh.Bindings.Count == 0 && fresh.Favorites.Count == 0 && fresh.Devices.Count == 0 && fresh.CycleHotkey == "",
        "fresh installation contains no personal data or assigned shortcuts");
    Check(!File.Exists(path), "reading defaults does not write settings");
    var other = AppSettings.Defaults();
    fresh.Bindings.Add(new() { PresetId = "custom-preset", DeviceId = "custom-output", Hotkey = "Ctrl + Shift + F8" });
    Check(other.Bindings.Count == 0, "defaults do not share mutable state");
    fresh.Favorites.Add(new("custom-preset", "User preset"));
    fresh.Devices.Add(new("custom-output", "User output"));
    fresh.CycleHotkey = "Alt + F9";
    fresh.Save(path);
    var saved = AppSettings.Load(path);
    Check(saved.Bindings.Single().DeviceId == "custom-output" && saved.Favorites.Single().Name == "User preset" && saved.CycleHotkey == "Alt + F9",
        "arbitrary user settings survive restart");
    saved.Language = "en";
    saved.Save(path);
    Check(AppSettings.Load(path).Language == "en", "language survives restart");
    File.WriteAllText(path, "{\"Language\":\"unknown\"}");
    Check(AppSettings.Load(path).Language is "ru" or "en", "unsupported language falls back safely");
    saved.Bindings[0].DeviceId = "";
    saved.Bindings[0].Hotkey = "";
    saved.Save(path);
    Check(AppSettings.Load(path).Bindings[0].DeviceId == "" && !File.Exists(path + ".tmp"), "overwrite and preset-only binding");
    string existing = File.ReadAllText(path);
    Check(JsonSerializer.Deserialize<AppSettings>(existing)!.Bindings[0].PresetId == "custom-preset", "serialized data remains readable");

    foreach (string invalid in new[] { "{", "null", "{\"Bindings\":null}", "{\"Devices\":[null]}", "{\"Bindings\":[{\"PresetId\":null}]}" })
    {
        File.WriteAllText(path, invalid);
        bool rejected = false;
        try { AppSettings.Load(path); } catch (Exception ex) when (ex is JsonException or InvalidDataException) { rejected = true; }
        Check(rejected && File.ReadAllText(path) == invalid, "corrupt settings are rejected without overwriting");
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
    string? ValidationError(string cycle, params PresetBinding[] bindings)
    {
        try { new AppSettings { Language = "en", CycleHotkey = cycle, Bindings = [.. bindings] }.Validate(); return null; }
        catch (ArgumentException ex) { return ex.Message; }
    }
    PresetBinding Row(string preset, string hotkey) => new() { PresetId = preset, Hotkey = hotkey };
    Check(ValidationError("Ctrl + Alt + F12", Row("a", "Ctrl + Alt + 1"), Row("b", "")) == null, "valid bindings pass validation");
    Check(ValidationError("", Row("", "")) == "Choose a preset for each binding or remove the empty one.", "a row without a preset is rejected");
    Check(ValidationError("", Row("a", ""), Row("a", "Ctrl + F1")) == "Each preset can be added only once.", "a repeated preset is rejected");
    Check(ValidationError("", Row("a", "Ctrl+Alt+1"), Row("b", "Alt + Ctrl + D1")) == "Shortcuts must be unique.", "the same shortcut in another spelling is rejected");
    Check(ValidationError("Ctrl + Alt + 1", Row("a", "Ctrl + Alt + D1")) == "The cycling shortcut is already assigned to a preset.", "the cycle shortcut cannot reuse a preset shortcut");
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

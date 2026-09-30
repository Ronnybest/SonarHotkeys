using System.Reflection;
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

void SelectLanguage(ComboBox picker, string language) =>
    picker.SelectedItem = picker.Items.Cast<Choice>().First(c => c.Id == language);

static string SourceDirectory(string project, [CallerFilePath] string path = "") =>
    Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", project));

// Russian source text is the catalog key: a typo in either place would silently drop the English text.
string sources = string.Concat(new[] { "SonarHotkeys", "SonarHotkeys.Core" }
    .SelectMany(project => Directory.GetFiles(SourceDirectory(project), "*.cs")).Select(File.ReadAllText));
var catalog = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(SourceDirectory("SonarHotkeys.Core"), "Translations.json")))!;
var keys = Regex.Matches(sources, @"\b(?:T|TextCatalog\.Get|Localized|AddButton)\((?:(?:[^""()]|\([^""()]*\))*,\s*)?((?:""(?:[^""\\]|\\.)*""\s*\+?\s*)+)")
    .Select(m => string.Concat(Regex.Matches(m.Groups[1].Value, @"""((?:[^""\\]|\\.)*)""").Select(s => Regex.Unescape(s.Groups[1].Value))))
    .ToHashSet();
var missing = keys.Where(k => !catalog.ContainsKey(k)).ToList();
var unused = catalog.Keys.Where(k => !keys.Contains(k)).ToList();
Check(missing.Count == 0, "every UI text has an English translation: " + string.Join(" | ", missing));
Check(unused.Count == 0, "catalog has no stale keys: " + string.Join(" | ", unused));
Check(keys.Count > 50, "catalog scan finds the UI texts");

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
    Check(new SonarService(() => "en").ErrorText(new SonarNotRunningException()) == "Sonar is unavailable. Enable Sonar in SteelSeries GG.",
        "Sonar errors use the selected language");
    Exception? uiFailure = null;
    var uiThread = new Thread(() =>
    {
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("ru-RU");
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            string uiPath = Path.Combine(directory, "ui", "settings.json");
            using var form = new Form1(uiPath);
            var tray = (NotifyIcon)typeof(Form1).GetField("_tray", flags)!.GetValue(form)!;
            tray.Visible = false;
            _ = form.Handle;
            var grid = (DataGridView)typeof(Form1).GetField("_grid", flags)!.GetValue(form)!;
            Check(grid.Rows.Count == 0, "first-run table starts empty");
            var favorites = (List<Choice>)typeof(Form1).GetField("_favorites", flags)!.GetValue(form)!;
            favorites.Add(new("user-selected-id", "Personal favorite"));
            typeof(Form1).GetMethod("UpdateChoices", flags)!.Invoke(form, null);
            typeof(Form1).GetMethod("AddBinding", flags)!.Invoke(form, null);
            Check(grid.Rows.Count == 1, "first-run Add creates a usable row");
            Check(grid.Rows[0].Cells["Preset"].Value as string == "user-selected-id", "row uses discovered user preset");
            var languagePicker = (ComboBox)typeof(Form1).GetField("_languagePicker", flags)!.GetValue(form)!;
            Check(form.Text == "SonarHotkeys — настройки", "Russian UI on first run with Russian Windows culture");
            SelectLanguage(languagePicker, "en");
            Check(form.Text == "SonarHotkeys — settings" && grid.Columns["Device"]!.HeaderText == "Output device", "language changes immediately in the window");
            var menu = (ContextMenuStrip)typeof(Form1).GetField("_menu", flags)!.GetValue(form)!;
            Check(menu.Items[0].Text == "Settings" && menu.Items[^1].Text == "Exit", "tray menu switches to English");
            Check(AppSettings.Load(uiPath).Language == "en" && AppSettings.Load(uiPath).Bindings.Count == 0,
                "changing language saves preference without committing draft bindings");
            Check(grid.Rows[0].Cells["Preset"].Value as string == "user-selected-id", "language switch preserves draft row");
            typeof(Form1).GetMethod("SaveSettings", flags)!.Invoke(form, null);
            Check(AppSettings.Load(uiPath).Bindings.Single().PresetId == "user-selected-id", "window saves first-run setup");
            using var reopened = new Form1(uiPath);
            var reopenedTray = (NotifyIcon)typeof(Form1).GetField("_tray", flags)!.GetValue(reopened)!;
            reopenedTray.Visible = false;
            var reopenedRows = (System.ComponentModel.BindingList<PresetBinding>)typeof(Form1).GetField("_rows", flags)!.GetValue(reopened)!;
            Check(reopenedRows.Single().PresetId == "user-selected-id", "window restores existing user setup");
            Check(reopened.Text == "SonarHotkeys — settings", "saved language overrides Windows culture on restart");
            var reopenedPicker = (ComboBox)typeof(Form1).GetField("_languagePicker", flags)!.GetValue(reopened)!;
            Check(reopenedPicker.SelectedItem is Choice { Id: "en" }, "language picker shows the saved language on restart");
            SelectLanguage(reopenedPicker, "ru");
            Check(reopened.Text == "SonarHotkeys — настройки" && AppSettings.Load(uiPath).Language == "ru", "switch to Russian with one selection after restart");
            SelectLanguage(languagePicker, "ru");
            Check(form.Text == "SonarHotkeys — настройки" && menu.Items[0].Text == "Настройки", "switch back to Russian");
            reopenedTray.Dispose();
            tray.Dispose();
        }
        catch (Exception ex) { uiFailure = ex; }
    });
    uiThread.SetApartmentState(ApartmentState.STA);
    uiThread.Start();
    uiThread.Join();
    if (uiFailure != null) throw new Exception("First-run UI checks failed", uiFailure);
    Console.WriteLine($"PASS: {checks} checks. No real user settings or audio routes were modified.");
}
finally
{
    if (!Path.GetFullPath(directory).StartsWith(Path.GetFullPath(Path.GetTempPath()) + "SonarHotkeysChecks-", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Unexpected test directory.");
    Directory.Delete(directory, recursive: true);
}

using System.Text.Json;

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
    public string CycleHotkey { get; set; } = "Ctrl + Alt + F12";

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SonarHotkeys", "settings.json");

    public static AppSettings Load()
    {
        if (!File.Exists(FilePath)) return Defaults();
        var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath))
            ?? throw new InvalidDataException("Файл настроек пуст.");
        if (settings.Bindings is null || settings.Favorites is null || settings.Devices is null ||
            settings.CycleHotkey is null || settings.Bindings.Any(b => b is null || b.PresetId is null || b.DeviceId is null || b.Hotkey is null) ||
            settings.Favorites.Concat(settings.Devices).Any(c => c is null || c.Id is null || c.Name is null))
            throw new InvalidDataException("Некорректный формат настроек.");
        return settings;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, FilePath, true);
    }

    public static AppSettings Defaults() => new()
    {
        Favorites = [new("a7efa934-1bd7-48c1-af3d-c3f95c0571b1", "The Witcher 3"),
            new("870df8c1-af0b-4eca-acb6-3207c1b4a5da", "War Thunder *")],
        Bindings = [new() { PresetId = "a7efa934-1bd7-48c1-af3d-c3f95c0571b1", Hotkey = "Ctrl + Alt + D1" },
            new() { PresetId = "870df8c1-af0b-4eca-acb6-3207c1b4a5da", Hotkey = "Ctrl + Alt + D2" }]
    };
}

internal readonly record struct Hotkey(uint Modifiers, Keys Key)
{
    public static Hotkey? Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        uint modifiers = 0;
        Keys key = Keys.None;
        foreach (string part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL": modifiers |= 2; break;
                case "ALT": modifiers |= 1; break;
                case "SHIFT": modifiers |= 4; break;
                case "WIN": modifiers |= 8; break;
                default:
                    var token = part.Length == 1 && char.IsDigit(part[0]) ? "D" + part : part;
                    if (key != Keys.None || !Enum.TryParse(token, true, out key) || !Enum.IsDefined(key) ||
                        key is Keys.None or Keys.ControlKey or Keys.Menu or Keys.ShiftKey or Keys.LWin or Keys.RWin || (int)key > 255)
                        throw new ArgumentException($"Некорректное сочетание: {text}");
                    break;
            }
        }
        if (key == Keys.None || modifiers == 0)
            throw new ArgumentException($"Укажите Ctrl, Alt, Shift или Win и клавишу: {text}");
        return new(modifiers, key);
    }

    public static void Capture(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox box) return;
        e.SuppressKeyPress = true;
        if (e.KeyCode is Keys.ControlKey or Keys.Menu or Keys.ShiftKey or Keys.LWin or Keys.RWin) return;
        if (e.KeyCode is Keys.Back or Keys.Delete && e.Modifiers == Keys.None) { box.Clear(); return; }
        if (e.Modifiers == Keys.None) return;
        box.Text = (e.Control ? "Ctrl + " : "") + (e.Alt ? "Alt + " : "") +
            (e.Shift ? "Shift + " : "") + e.KeyCode;
        box.SelectionStart = box.TextLength;
    }
}

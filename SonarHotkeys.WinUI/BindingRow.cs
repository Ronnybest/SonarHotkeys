using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SonarHotkeys;

/// <summary>One editable binding card. Selections are kept as ids, so replacing the choice lists never loses them.</summary>
public sealed partial class BindingRow(string presetId, string deviceId, string hotkey) : INotifyPropertyChanged
{
    private string _presetId = presetId, _deviceId = deviceId, _hotkey = hotkey;
    private List<ChoiceItem> _presets = [], _devices = [];
    private Func<string, string> _text = key => key;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised when the user changes the preset, device or shortcut; not when choice lists are replaced.</summary>
    public event Action? Edited;

    public string PresetId => _presetId;
    public string DeviceId => _deviceId;
    public List<ChoiceItem> Presets => _presets;
    public List<ChoiceItem> Devices => _devices;

    // A ComboBox writes null back while its items are replaced; only a real choice changes the binding.
    public ChoiceItem? Preset
    {
        get => _presets.FirstOrDefault(c => c.Id == _presetId);
        set { if (value != null && value.Id != _presetId) { _presetId = value.Id; Raise(); Edited?.Invoke(); } }
    }

    public ChoiceItem? Device
    {
        get => _devices.FirstOrDefault(c => c.Id == _deviceId);
        set { if (value != null && value.Id != _deviceId) { _deviceId = value.Id; Raise(); Edited?.Invoke(); } }
    }

    public string Hotkey
    {
        get => _hotkey;
        set { if (value != _hotkey) { _hotkey = value; Raise(); Edited?.Invoke(); } }
    }

    public string PresetHeader => T("Card.Preset");
    public string PresetPlaceholder => T("Card.PresetPlaceholder");
    public string DeviceHeader => T("Card.Device");
    public string HotkeyHeader => T("Card.Shortcut");
    public string HotkeyPlaceholder => T("Shortcut.Placeholder");
    public string ApplyLabel => T("Card.Apply");
    public string RemoveLabel => T("Card.Remove");

    public void SetChoices(List<ChoiceItem> presets, List<ChoiceItem> devices)
    {
        _presets = presets;
        _devices = devices;
        Raise(nameof(Presets));
        Raise(nameof(Devices));
        Raise(nameof(Preset));
        Raise(nameof(Device));
    }

    public void SetText(Func<string, string> text)
    {
        _text = text;
        foreach (string name in new[] { nameof(PresetHeader), nameof(PresetPlaceholder), nameof(DeviceHeader),
            nameof(HotkeyHeader), nameof(HotkeyPlaceholder), nameof(ApplyLabel), nameof(RemoveLabel) })
            Raise(name);
    }

    public PresetBinding ToBinding() => new() { PresetId = _presetId, DeviceId = _deviceId, Hotkey = _hotkey };

    private string T(string key) => _text(key);

    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

/// <summary>A preset or device in a card's list. Read-only, so the XAML metadata never needs setters.</summary>
public sealed partial class ChoiceItem(string id, string name)
{
    public string Id { get; } = id;
    public string Name { get; } = name;
}

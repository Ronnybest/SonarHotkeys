using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;

namespace SonarHotkeys;

/// <summary>One editable preset card. Selections are kept as ids, so replacing the choice lists never loses them.</summary>
public sealed partial class BindingRow(string presetId, string deviceId, string hotkey) : INotifyPropertyChanged
{
    private string _presetId = presetId, _deviceId = deviceId, _hotkey = hotkey;
    private List<ChoiceItem> _presets = [], _devices = [];
    private Func<string, string> _text = key => key;
    private bool _selected;
    private string? _number;

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

    public string PresetLabel => T("Card.Preset");
    public string PresetPlaceholder => T("Card.PresetPlaceholder");
    public string DeviceLabel { get; private set; } = "";
    public string HotkeyLabel => T("Card.Shortcut");
    // With a place from 1 to 9 the empty field shows the number shortcut, which works without assigning anything.
    public string HotkeyPlaceholder => _number ?? T("Shortcut.Placeholder");
    public string HotkeyTip => _number == null ? HotkeyLabel : string.Format(T("Card.NumberTip"), _number);

    /// <summary>Sets the number shortcut of this card's place in the list, or null past the ninth or when numbers are off.</summary>
    public void SetNumber(string? number)
    {
        if (number == _number) return;
        _number = number;
        Raise(nameof(HotkeyPlaceholder));
        Raise(nameof(HotkeyTip));
    }

    public string ApplyLabel => T("Card.Apply");
    public string RemoveLabel => T("Card.Remove");
    public string DragLabel => T("Card.Drag");
    public string SelectedLabel => T("Card.Selected");

    /// <summary>Shown when this is the preset that Sonar has selected in the channel now.</summary>
    public Visibility SelectedVisibility => _selected ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ApplyVisibility => _selected ? Visibility.Collapsed : Visibility.Visible;
    public string ApplyTip => _selected ? SelectedLabel : ApplyLabel;

    public void SetSelected(bool selected)
    {
        if (selected == _selected) return;
        _selected = selected;
        Raise(nameof(SelectedVisibility));
        Raise(nameof(ApplyVisibility));
        Raise(nameof(ApplyTip));
    }

    public void SetChoices(List<ChoiceItem> presets, List<ChoiceItem> devices)
    {
        _presets = presets;
        _devices = devices;
        Raise(nameof(Presets));
        Raise(nameof(Devices));
        Raise(nameof(Preset));
        Raise(nameof(Device));
    }

    public void SetText(Func<string, string> text, string deviceLabel)
    {
        _text = text;
        DeviceLabel = deviceLabel;
        foreach (string name in new[] { nameof(PresetLabel), nameof(PresetPlaceholder), nameof(DeviceLabel),
            nameof(HotkeyLabel), nameof(HotkeyPlaceholder), nameof(ApplyLabel), nameof(RemoveLabel), nameof(DragLabel), nameof(SelectedLabel), nameof(ApplyTip), nameof(HotkeyTip) })
            Raise(name);
    }

    public PresetBinding ToBinding() => new() { PresetId = _presetId, DeviceId = _deviceId, Hotkey = _hotkey };

    private string T(string key) => _text(key);

    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

/// <summary>A channel in the sidebar: its name, how many presets it has, and whether the next preset shortcut steps through it.</summary>
public sealed partial class ChannelRow(SonarChannel channel, string glyph) : INotifyPropertyChanged
{
    private string _name = "", _details = "";
    private bool _active;

    public event PropertyChangedEventHandler? PropertyChanged;

    public SonarChannel Channel { get; } = channel;
    public string Glyph { get; } = glyph;
    public string Name => _name;
    public string Details => _details;
    public Visibility CycleVisibility => _active ? Visibility.Visible : Visibility.Collapsed;

    public void Update(string name, string details, bool active)
    {
        if (name != _name) { _name = name; Raise(nameof(Name)); }
        if (details != _details) { _details = details; Raise(nameof(Details)); }
        if (active != _active) { _active = active; Raise(nameof(CycleVisibility)); }
    }

    // The list item's accessible name.
    public override string ToString() => _name;

    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

/// <summary>A preset or device in a card's list. Read-only, so the XAML metadata never needs setters.</summary>
public sealed partial class ChoiceItem(string id, string name)
{
    public string Id { get; } = id;
    public string Name { get; } = name;
}

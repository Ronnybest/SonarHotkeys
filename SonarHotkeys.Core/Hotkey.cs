namespace SonarHotkeys;

/// <summary>A global shortcut: RegisterHotKey modifier flags and a virtual-key code.</summary>
public readonly record struct Hotkey(uint Modifiers, uint Key)
{
    public const uint Alt = 1, Control = 2, Shift = 4, Win = 8;

    public static Hotkey? Parse(string text, string? language = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        uint modifiers = 0, key = 0;
        foreach (string part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL": modifiers |= Control; break;
                case "ALT": modifiers |= Alt; break;
                case "SHIFT": modifiers |= Shift; break;
                case "WIN": modifiers |= Win; break;
                default:
                    var token = part.Length == 1 && char.IsDigit(part[0]) ? "D" + part : part;
                    if (key != 0 || !KeysByName.TryGetValue(token, out key))
                        throw new ArgumentException(TextCatalog.Get("Некорректное сочетание: {0}", language, text));
                    break;
            }
        }
        if (key == 0 || modifiers == 0)
            throw new ArgumentException(TextCatalog.Get("Укажите Ctrl, Alt, Shift или Win и клавишу: {0}", language, text));
        return new(modifiers, key);
    }

    /// <summary>Text for a pressed combination, or null while only modifiers are held.</summary>
    public static string? Format(bool control, bool alt, bool shift, uint key) =>
        (control || alt || shift) && Names.TryGetValue(key, out var names)
            ? (control ? "Ctrl + " : "") + (alt ? "Alt + " : "") + (shift ? "Shift + " : "") + names[0]
            : null;

    // Virtual-key names spelled as System.Windows.Forms.Keys, so shortcuts saved by earlier versions
    // keep parsing. The first name is the one written for a captured key. Modifier keys are excluded.
    private static readonly Dictionary<uint, string[]> Names = new()
    {
        [0x01] = ["LButton"],
        [0x02] = ["RButton"],
        [0x03] = ["Cancel"],
        [0x04] = ["MButton"],
        [0x05] = ["XButton1"],
        [0x06] = ["XButton2"],
        [0x08] = ["Back"],
        [0x09] = ["Tab"],
        [0x0A] = ["LineFeed"],
        [0x0C] = ["Clear"],
        [0x0D] = ["Enter", "Return"],
        [0x13] = ["Pause"],
        [0x14] = ["Capital", "CapsLock"],
        [0x15] = ["KanaMode", "HangulMode", "HanguelMode"],
        [0x17] = ["JunjaMode"],
        [0x18] = ["FinalMode"],
        [0x19] = ["KanjiMode", "HanjaMode"],
        [0x1B] = ["Escape"],
        [0x1C] = ["IMEConvert"],
        [0x1D] = ["IMENonconvert"],
        [0x1E] = ["IMEAceept", "IMEAccept"],
        [0x1F] = ["IMEModeChange"],
        [0x20] = ["Space"],
        [0x21] = ["PageUp", "Prior"],
        [0x22] = ["Next", "PageDown"],
        [0x23] = ["End"],
        [0x24] = ["Home"],
        [0x25] = ["Left"],
        [0x26] = ["Up"],
        [0x27] = ["Right"],
        [0x28] = ["Down"],
        [0x29] = ["Select"],
        [0x2A] = ["Print"],
        [0x2B] = ["Execute"],
        [0x2C] = ["PrintScreen", "Snapshot"],
        [0x2D] = ["Insert"],
        [0x2E] = ["Delete"],
        [0x2F] = ["Help"],
        [0x30] = ["D0"],
        [0x31] = ["D1"],
        [0x32] = ["D2"],
        [0x33] = ["D3"],
        [0x34] = ["D4"],
        [0x35] = ["D5"],
        [0x36] = ["D6"],
        [0x37] = ["D7"],
        [0x38] = ["D8"],
        [0x39] = ["D9"],
        [0x41] = ["A"],
        [0x42] = ["B"],
        [0x43] = ["C"],
        [0x44] = ["D"],
        [0x45] = ["E"],
        [0x46] = ["F"],
        [0x47] = ["G"],
        [0x48] = ["H"],
        [0x49] = ["I"],
        [0x4A] = ["J"],
        [0x4B] = ["K"],
        [0x4C] = ["L"],
        [0x4D] = ["M"],
        [0x4E] = ["N"],
        [0x4F] = ["O"],
        [0x50] = ["P"],
        [0x51] = ["Q"],
        [0x52] = ["R"],
        [0x53] = ["S"],
        [0x54] = ["T"],
        [0x55] = ["U"],
        [0x56] = ["V"],
        [0x57] = ["W"],
        [0x58] = ["X"],
        [0x59] = ["Y"],
        [0x5A] = ["Z"],
        [0x5D] = ["Apps"],
        [0x5F] = ["Sleep"],
        [0x60] = ["NumPad0"],
        [0x61] = ["NumPad1"],
        [0x62] = ["NumPad2"],
        [0x63] = ["NumPad3"],
        [0x64] = ["NumPad4"],
        [0x65] = ["NumPad5"],
        [0x66] = ["NumPad6"],
        [0x67] = ["NumPad7"],
        [0x68] = ["NumPad8"],
        [0x69] = ["NumPad9"],
        [0x6A] = ["Multiply"],
        [0x6B] = ["Add"],
        [0x6C] = ["Separator"],
        [0x6D] = ["Subtract"],
        [0x6E] = ["Decimal"],
        [0x6F] = ["Divide"],
        [0x70] = ["F1"],
        [0x71] = ["F2"],
        [0x72] = ["F3"],
        [0x73] = ["F4"],
        [0x74] = ["F5"],
        [0x75] = ["F6"],
        [0x76] = ["F7"],
        [0x77] = ["F8"],
        [0x78] = ["F9"],
        [0x79] = ["F10"],
        [0x7A] = ["F11"],
        [0x7B] = ["F12"],
        [0x7C] = ["F13"],
        [0x7D] = ["F14"],
        [0x7E] = ["F15"],
        [0x7F] = ["F16"],
        [0x80] = ["F17"],
        [0x81] = ["F18"],
        [0x82] = ["F19"],
        [0x83] = ["F20"],
        [0x84] = ["F21"],
        [0x85] = ["F22"],
        [0x86] = ["F23"],
        [0x87] = ["F24"],
        [0x90] = ["NumLock"],
        [0x91] = ["Scroll"],
        [0xA0] = ["LShiftKey"],
        [0xA1] = ["RShiftKey"],
        [0xA2] = ["LControlKey"],
        [0xA3] = ["RControlKey"],
        [0xA4] = ["LMenu"],
        [0xA5] = ["RMenu"],
        [0xA6] = ["BrowserBack"],
        [0xA7] = ["BrowserForward"],
        [0xA8] = ["BrowserRefresh"],
        [0xA9] = ["BrowserStop"],
        [0xAA] = ["BrowserSearch"],
        [0xAB] = ["BrowserFavorites"],
        [0xAC] = ["BrowserHome"],
        [0xAD] = ["VolumeMute"],
        [0xAE] = ["VolumeDown"],
        [0xAF] = ["VolumeUp"],
        [0xB0] = ["MediaNextTrack"],
        [0xB1] = ["MediaPreviousTrack"],
        [0xB2] = ["MediaStop"],
        [0xB3] = ["MediaPlayPause"],
        [0xB4] = ["LaunchMail"],
        [0xB5] = ["SelectMedia"],
        [0xB6] = ["LaunchApplication1"],
        [0xB7] = ["LaunchApplication2"],
        [0xBA] = ["OemSemicolon", "Oem1"],
        [0xBB] = ["Oemplus"],
        [0xBC] = ["Oemcomma"],
        [0xBD] = ["OemMinus"],
        [0xBE] = ["OemPeriod"],
        [0xBF] = ["Oem2", "OemQuestion"],
        [0xC0] = ["Oemtilde", "Oem3"],
        [0xDB] = ["Oem4", "OemOpenBrackets"],
        [0xDC] = ["OemPipe", "Oem5"],
        [0xDD] = ["Oem6", "OemCloseBrackets"],
        [0xDE] = ["OemQuotes", "Oem7"],
        [0xDF] = ["Oem8"],
        [0xE2] = ["Oem102", "OemBackslash"],
        [0xE5] = ["ProcessKey"],
        [0xE7] = ["Packet"],
        [0xF6] = ["Attn"],
        [0xF7] = ["Crsel"],
        [0xF8] = ["Exsel"],
        [0xF9] = ["EraseEof"],
        [0xFA] = ["Play"],
        [0xFB] = ["Zoom"],
        [0xFC] = ["NoName"],
        [0xFD] = ["Pa1"],
        [0xFE] = ["OemClear"],
    };

    // Declared after Names: static initializers run in source order.
    private static readonly Dictionary<string, uint> KeysByName = Names
        .SelectMany(pair => pair.Value.Select(name => (name, pair.Key)))
        .ToDictionary(entry => entry.name, entry => entry.Key, StringComparer.OrdinalIgnoreCase);
}

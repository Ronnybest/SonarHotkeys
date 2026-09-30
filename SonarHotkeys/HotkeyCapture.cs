namespace SonarHotkeys;

internal static class HotkeyCapture
{
    /// <summary>Writes the pressed combination into a read-only text box; Delete or Backspace clears it.</summary>
    public static void Capture(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox box) return;
        e.SuppressKeyPress = true;
        if (e.KeyCode is Keys.Back or Keys.Delete && e.Modifiers == Keys.None) { box.Clear(); return; }
        if (Hotkey.Format(e.Control, e.Alt, e.Shift, (uint)e.KeyCode) is not { } text) return;
        box.Text = text;
        box.SelectionStart = box.TextLength;
    }
}

using System.Runtime.InteropServices;
using static SonarHotkeys.Interop.Native;

namespace SonarHotkeys.Interop;

/// <summary>A tray menu entry. A null text is a separator; children make a submenu.</summary>
internal sealed record TrayMenuItem(string? Text, int Id = 0, bool Enabled = true, bool Checked = false,
    IReadOnlyList<TrayMenuItem>? Children = null)
{
    public static TrayMenuItem Separator { get; } = new((string?)null);
}

/// <summary>
/// A hidden window on the UI thread that owns the global hotkeys and the tray icon.
/// WinUI windows have no message hook, and a separate window keeps both working while the settings window is closed.
/// </summary>
internal sealed unsafe class TrayHost : IDisposable
{
    private const uint TrayCallback = WM_APP + 1;
    private const uint IconId = 1;
    private static TrayHost? _instance;
    private readonly nint _hwnd;
    private readonly uint _taskbarCreated;
    private readonly nint _icon;
    private readonly HashSet<int> _hotkeys = [];
    private string _tip;
    private bool _disposed;

    /// <summary>Raised with the id passed to <see cref="RegisterHotkey"/>.</summary>
    public event Action<int>? HotkeyPressed;
    public event Action? IconClicked;
    public event Action? MenuRequested;
    public event Action? BalloonClicked;

    /// <summary>Windows is signing out or shutting down, or an installer asked the app to close.</summary>
    public event Action? SessionEnding;

    public TrayHost(string tip)
    {
        if (_instance != null) throw new InvalidOperationException("Only one tray host is supported.");
        _tip = tip;
        nint module = GetModuleHandle(null);
        const string className = "SonarHotkeys.TrayHost";
        fixed (char* name = className)
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WindowProc,
                hInstance = module,
                lpszClassName = name,
            };
            if (RegisterClassEx(&windowClass) == 0) throw new InvalidOperationException("RegisterClassEx failed: " + Marshal.GetLastPInvokeError());
        }
        // A hidden top-level window rather than a message-only one: only top-level windows receive "TaskbarCreated".
        _hwnd = CreateWindowEx(0, className, "SonarHotkeys", WS_POPUP, 0, 0, 0, 0, 0, 0, module, 0);
        if (_hwnd == 0) throw new InvalidOperationException("CreateWindowEx failed: " + Marshal.GetLastPInvokeError());
        _instance = this;
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        _icon = LoadAppIcon(GetSystemMetrics(SM_CXSMICON));
        WindowIcon = LoadAppIcon(GetSystemMetrics(SM_CXICON));
        try
        {
            SetPreferredAppMode(1); // AllowDark: popup menus follow the system theme.
            FlushMenuThemes();
        }
        catch (EntryPointNotFoundException) { }
        AddIcon();
    }

    public nint Handle => _hwnd;

    /// <summary>The application icon at the standard window-icon size.</summary>
    public nint WindowIcon { get; }

    // The .NET SDK embeds ApplicationIcon under the IDI_APPLICATION resource id; fall back to the system icon.
    private static nint LoadAppIcon(int size)
    {
        nint icon = LoadImage(GetModuleHandle(null), IDI_APPLICATION, IMAGE_ICON, size, size, 0);
        return icon != 0 ? icon : LoadIcon(0, IDI_APPLICATION);
    }

    /// <summary>Registers a global hotkey; returns the Win32 error code, or 0 on success.</summary>
    public int RegisterHotkey(int id, Hotkey hotkey)
    {
        if (!RegisterHotKey(_hwnd, id, hotkey.Modifiers | MOD_NOREPEAT, hotkey.Key)) return Marshal.GetLastPInvokeError();
        _hotkeys.Add(id);
        return 0;
    }

    public void UnregisterHotkeys()
    {
        foreach (int id in _hotkeys) UnregisterHotKey(_hwnd, id);
        _hotkeys.Clear();
    }

    public void SetTip(string tip)
    {
        _tip = tip;
        var data = CreateData(NIF_TIP | NIF_SHOWTIP);
        Shell_NotifyIcon(NIM_MODIFY, &data);
    }

    public void ShowBalloon(string title, string text, bool error)
    {
        var data = CreateData(NIF_INFO);
        Copy(title, data.szInfoTitle, 64);
        Copy(text, data.szInfo, 256);
        data.dwInfoFlags = error ? NIIF_ERROR : NIIF_INFO;
        Shell_NotifyIcon(NIM_MODIFY, &data);
    }

    /// <summary>Shows the menu at the cursor and returns the chosen id, or 0 when dismissed.</summary>
    public int ShowMenu(IReadOnlyList<TrayMenuItem> items)
    {
        var menus = new List<nint>();
        nint Build(IReadOnlyList<TrayMenuItem> entries)
        {
            nint menu = CreatePopupMenu();
            menus.Add(menu);
            foreach (var item in entries)
            {
                if (item.Text == null) { AppendMenu(menu, MF_SEPARATOR, 0, null); continue; }
                uint flags = (item.Enabled ? 0 : MF_GRAYED) | (item.Checked ? MF_CHECKED : 0);
                if (item.Children != null) AppendMenu(menu, flags | MF_POPUP, (nuint)Build(item.Children), item.Text);
                else AppendMenu(menu, flags | MF_STRING, (nuint)item.Id, item.Text);
            }
            return menu;
        }
        nint root = Build(items);
        try
        {
            GetCursorPos(out var point);
            // Without foreground activation the menu would not close when the user clicks elsewhere.
            SetForegroundWindow(_hwnd);
            int command = TrackPopupMenuEx(root, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_BOTTOMALIGN, point.X, point.Y, _hwnd, 0);
            PostMessage(_hwnd, WM_NULL, 0, 0);
            return command;
        }
        finally
        {
            // Destroying the root menu also destroys its submenus.
            DestroyMenu(root);
        }
    }

    private void AddIcon()
    {
        var data = CreateData(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        data.uCallbackMessage = TrayCallback;
        data.hIcon = _icon;
        Shell_NotifyIcon(NIM_ADD, &data);
        data.uVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIcon(NIM_SETVERSION, &data);
    }

    private NOTIFYICONDATAW CreateData(uint flags)
    {
        var data = new NOTIFYICONDATAW { cbSize = (uint)sizeof(NOTIFYICONDATAW), hWnd = _hwnd, uID = IconId, uFlags = flags };
        Copy(_tip, data.szTip, 128);
        return data;
    }

    private bool HandleMessage(uint message, nint wParam, nint lParam)
    {
        if (message == WM_HOTKEY)
        {
            HotkeyPressed?.Invoke((int)wParam);
            return true;
        }
        if (message == TrayCallback)
        {
            // NOTIFYICON_VERSION_4: the notification is in the low word of lParam.
            switch ((uint)(lParam & 0xFFFF))
            {
                case NIN_SELECT or NIN_KEYSELECT or WM_LBUTTONDBLCLK: IconClicked?.Invoke(); break;
                case WM_CONTEXTMENU: MenuRequested?.Invoke(); break;
                case NIN_BALLOONUSERCLICK: BalloonClicked?.Invoke(); break;
            }
            return true;
        }
        // The settings window only hides on close, so the session end is handled here: this is how the
        // Restart Manager closes the app during an MSI upgrade. WM_QUERYENDSESSION falls through (allowed).
        if (message == WM_ENDSESSION)
        {
            if (wParam != 0) SessionEnding?.Invoke();
            return true;
        }
        if (message == _taskbarCreated && _taskbarCreated != 0)
        {
            // Explorer restarted: the icon has to be added again.
            AddIcon();
            return true;
        }
        return false;
    }

    [UnmanagedCallersOnly]
    private static nint WindowProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        var host = _instance;
        if (host != null && hwnd == host._hwnd)
        {
            // An exception must not unwind into native code: that would terminate the process.
            try { if (host.HandleMessage(message, wParam, lParam)) return 0; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        }
        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnregisterHotkeys();
        var data = CreateData(0);
        Shell_NotifyIcon(NIM_DELETE, &data);
        DestroyIcon(_icon);
        DestroyIcon(WindowIcon);
        DestroyWindow(_hwnd);
        _instance = null;
    }
}

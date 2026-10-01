using SonarHotkeys.Interop;

namespace SonarHotkeys;

/// <summary>
/// The part of the app that lives in the tray: saved settings, global hotkeys, Sonar calls and notifications.
/// The settings window is only an editor on top of it and may stay closed.
/// </summary>
internal sealed class TrayController
{
    private const int CycleId = 10000, ChannelId = 10001, NumberBaseId = 20000;
    private const int MenuSettings = 1, MenuNext = 2, MenuRefresh = 3, MenuExit = 4, MenuAutostart = 5, MenuNextChannel = 6, MenuPresetBase = 100;
    private readonly string _settingsPath;
    private readonly SonarService _sonar;
    private readonly Autostart _autostart = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<int, (SonarChannel Channel, PresetBinding Binding)> _hotkeys = [];
    // The preset selected in each channel, as last read from Sonar or applied here; marked in the window and the tray menu.
    private Dictionary<SonarChannel, string> _selected = [];
    private bool _readingSelection;
    private TrayHost? _host;
    private MainWindow? _window;
    private AppSettings _settings;
    private bool _busy, _exiting, _hotkeysSuspended;
    private string? _startupError;

    public event Action? Exited;

    public TrayController(string settingsPath)
    {
        _settingsPath = Path.GetFullPath(settingsPath);
        try { _settings = AppSettings.Load(_settingsPath); }
        catch (Exception ex)
        {
            _settings = AppSettings.Defaults();
            _startupError = T("Settings.ReadFailed", ex.Message);
        }
        _sonar = new(() => _settings.Language);
    }

    /// <summary>The saved configuration; the window edits its own copy and saves it through <see cref="Save"/>.</summary>
    public AppSettings Settings => _settings;
    public bool IsBusy => _busy;
    public nint WindowIcon => _host?.WindowIcon ?? 0;
    public bool StartsWithWindows => _autostart.IsEnabled;
    public (string Text, bool Error)? LastMessage { get; private set; }

    public string T(string key, params object[] arguments) => TextCatalog.Get(key, _settings.Language, arguments);

    public string ErrorText(Exception ex) => _sonar.ErrorText(ex);

    private string ChannelName(SonarChannel channel) => T(AppSettings.ChannelKey(channel));

    public void Start(bool startHidden)
    {
        _host = new TrayHost("SonarHotkeys");
        _host.HotkeyPressed += OnHotkey;
        _host.IconClicked += ShowWindow;
        _host.BalloonClicked += ShowWindow;
        _host.MenuRequested += ShowMenu;
        _host.SessionEnding += Exit;
        RegisterHotkeys();
#if !DEBUG
        // A moved portable folder would leave the startup shortcut pointing at the old path.
        // Skipped in Debug builds, so running one never redirects the real shortcut.
        try { _autostart.Refresh(Environment.ProcessPath!); }
        catch (Exception ex) { Report(T("Autostart.Failed", ex.Message), error: true); }
#endif
        bool configured = _settings.ConfiguredChannels().Count > 0;
        // A fresh installation always shows setup, even if launched with --tray.
        if (!startHidden || !configured || _startupError != null) ShowWindow();
        if (_startupError != null) Report(_startupError, error: true);
        else if (!configured) Report(T("Status.Welcome"));
#if DEBUG
        // Keeps the cached presets and devices, e.g. for screenshots with sample settings.
        if (Environment.GetCommandLineArgs().Contains("--skip-refresh")) return;
#endif
        _ = RefreshAsync();
    }

    public void ShowWindow()
    {
        if (_exiting) return;
        _window ??= new MainWindow(this);
        _window.ShowAndActivate();
    }

    public void Exit()
    {
        if (_exiting) return;
        _exiting = true;
        _lifetime.Cancel();
        _window?.CloseForExit();
        _host?.Dispose();
        Exited?.Invoke();
    }

    /// <summary>Releases the hotkeys while a shortcut is being captured, so pressing one does not trigger it.</summary>
    public void SuspendHotkeys()
    {
        _hotkeysSuspended = true;
        _host?.UnregisterHotkeys();
        _hotkeys.Clear();
    }

    public void ResumeHotkeys()
    {
        if (!_hotkeysSuspended || _exiting) return;
        _hotkeysSuspended = false;
        RegisterHotkeys();
    }

    private void RegisterHotkeys(bool throwOnError = false)
    {
        if (_host == null) return;
        _host.UnregisterHotkeys();
        _hotkeys.Clear();
        if (_hotkeysSuspended) return;
        var errors = new List<string>();
        bool Register(int id, string text)
        {
            try
            {
                if (Hotkey.Parse(text, _settings.Language) is not { } key) return false;
                int error = _host.RegisterHotkey(id, key);
                if (error != 0)
                    throw new InvalidOperationException(T("Shortcut.RegisterFailed", text, error));
                return true;
            }
            catch (Exception ex) { errors.Add(ex.Message); return false; }
        }
        int next = 1;
        foreach (var channel in Enum.GetValues<SonarChannel>())
            foreach (var binding in _settings.Channel(channel).Presets)
            {
                int id = next++;
                if (Register(id, binding.Hotkey)) _hotkeys[id] = (channel, binding);
            }
        Register(ChannelId, _settings.ChannelHotkey);
        Register(CycleId, _settings.CycleHotkey);
        // The number shortcuts are reported together, and never block saving: another app may hold some of them.
        var busyNumbers = new List<int>();
        int lastError = 0;
        for (int number = 1; number <= 9 && _settings.NumberHotkeys; number++)
            if (_host.RegisterHotkey(NumberBaseId + number, AppSettings.NumberHotkey(number)) is var error and not 0)
            {
                busyNumbers.Add(number);
                lastError = error;
            }
        if (errors.Count > 0)
        {
            string message = string.Join(Environment.NewLine, errors);
            if (throwOnError) throw new InvalidOperationException(message);
            Report(message, error: true);
        }
        else if (busyNumbers.Count > 0)
            Report(T("Shortcut.RegisterFailed", "Ctrl + Alt + " + string.Join(", ", busyNumbers), lastError), error: true);
    }

    /// <summary>
    /// Validates, registers and saves the edited channels; on failure the previous configuration stays active.
    /// While a shortcut is being captured the hotkeys stay released and are registered when capture ends.
    /// </summary>
    public void Save(Dictionary<SonarChannel, ChannelSettings> channels, string channelHotkey, string cycleHotkey, bool capturing = false)
    {
        var candidate = new AppSettings
        {
            Channels = channels, CycleChannel = _settings.CycleChannel, KnownPresets = _settings.KnownPresets,
            Outputs = _settings.Outputs, Inputs = _settings.Inputs,
            ChannelHotkey = channelHotkey, CycleHotkey = cycleHotkey, SwitchAllSound = _settings.SwitchAllSound, NumberHotkeys = _settings.NumberHotkeys,
            Language = _settings.Language,
        };
        candidate.Validate();
        // Register first; a conflict leaves the previous saved configuration active.
        var previous = _settings;
        _settings = candidate;
        _hotkeysSuspended = capturing;
        try
        {
            RegisterHotkeys(throwOnError: true);
            candidate.Save(_settingsPath);
        }
        catch
        {
            _settings = previous;
            RegisterHotkeys();
            throw;
        }
        _startupError = null;
        Report(T("Status.Saved"));
    }

    /// <summary>Makes a channel the one whose presets the next preset shortcut steps through.</summary>
    public void SetCycleChannel(SonarChannel channel)
    {
        if (_settings.CycleChannel == channel) return;
        _settings.CycleChannel = channel;
        _window?.UpdateChannels();
        // A damaged settings file is not overwritten here; the next edit saves the cycled channel too.
        if (_startupError != null) return;
        try { _settings.Save(_settingsPath); }
        catch (Exception ex) { Report(ErrorText(ex), error: true); }
    }

    public void SetStartWithWindows(bool enabled)
    {
        try
        {
            if (enabled) _autostart.Enable(Environment.ProcessPath!);
            else _autostart.Disable();
        }
        catch (Exception ex) { Report(T("Autostart.Failed", ex.Message), error: true); }
        _window?.UpdateToggles();
    }

    /// <summary>Applies the preset at a place in the cycled channel's list: Ctrl+Alt+1 the first one, and so on.</summary>
    public Task ApplyNumberAsync(int number)
    {
        var channel = _settings.CycleChannel;
        var presets = _settings.Channel(channel).Presets;
        if (number <= presets.Count) return ApplyAsync(channel, presets[number - 1]);
        Report(T("Status.NoPresetNumber", ChannelName(channel), number), notify: true);
        return Task.CompletedTask;
    }

    public void SetNumberHotkeys(bool enabled)
    {
        if (_settings.NumberHotkeys == enabled) return;
        _settings.NumberHotkeys = enabled;
        try { _settings.Validate(); }
        catch (ArgumentException ex)
        {
            // A preset or cycling shortcut already uses one of Ctrl+Alt+1…9.
            _settings.NumberHotkeys = false;
            _window?.UpdateToggles();
            Report(ex.Message, error: true);
            return;
        }
        RegisterHotkeys();
        _window?.UpdateNumbers();
        if (_startupError != null) return;
        try
        {
            _settings.Save(_settingsPath);
            Report(T("Status.Saved"));
        }
        catch (Exception ex) { Report(ErrorText(ex), error: true); }
    }

    public void SetSwitchAllSound(bool enabled)
    {
        if (_settings.SwitchAllSound == enabled) return;
        _settings.SwitchAllSound = enabled;
        // A damaged settings file is not overwritten here; the next edit saves this option too.
        if (_startupError != null) return;
        try
        {
            _settings.Save(_settingsPath);
            Report(T("Status.Saved"));
        }
        catch (Exception ex) { Report(ErrorText(ex), error: true); }
    }

    public void ChangeLanguage(string language)
    {
        if (language == _settings.Language) return;
        _settings.Language = language;
        _window?.ApplyLanguage();
        // The window itself shows the change, so only a failure is reported.
        // A damaged settings file is not overwritten here; the next edit saves the language too.
        if (_startupError != null) return;
        try { _settings.Save(_settingsPath); }
        catch (Exception ex) { Report(T("Language.SaveFailed", ex.Message), error: true); }
    }

    public async Task RefreshAsync()
    {
        if (_busy || _exiting) return;
        SetBusy(true);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            var inventory = await _sonar.DiscoverAsync(timeout.Token);
            if (_exiting) return;
            // Refresh the cache without saving uncommitted edits in the window.
            _settings.KnownPresets = inventory.Presets;
            _settings.Outputs = inventory.Outputs;
            _settings.Inputs = inventory.Inputs;
            // Presets favorited in GG appear in their channel without adding them by hand; they get no shortcut,
            // so the registered hotkeys stay the same.
            int added = _settings.AddNewFavorites(inventory.Favorites);
            if (_startupError == null) _settings.Save(_settingsPath);
            _window?.UpdateChoices();
            if (added > 0) _window?.AddNewRows();
            await RefreshSelectionAsync();
            if (added > 0) Report(T("Status.Added", added));
            // The welcome text stays until there is a preset to switch to.
            else if (_settings.ConfiguredChannels().Count > 0)
                Report(T("Status.Found", inventory.Presets.Values.Sum(p => p.Count), inventory.Outputs.Count, inventory.Inputs.Count));
        }
        catch (Exception ex) { if (!_exiting) Report(_sonar.ErrorText(ex), error: true); }
        finally { SetBusy(false); }
    }

    public string? SelectedPreset(SonarChannel channel) => _selected.GetValueOrDefault(channel);

    /// <summary>
    /// Reads which preset each channel has selected, including changes made directly in GG. Quiet: when Sonar is
    /// unavailable the last known selection stays, since the window polls this while it is open.
    /// </summary>
    public async Task RefreshSelectionAsync()
    {
        if (_readingSelection || _exiting) return;
        _readingSelection = true;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            var selected = await _sonar.SelectedPresetsAsync(timeout.Token);
            if (_exiting) return;
            _selected = selected;
            _window?.UpdateSelection();
        }
        catch (Exception) { }
        finally { _readingSelection = false; }
    }

    /// <summary>Makes the next channel with presets the cycled one and shows which preset it has selected now.</summary>
    public async Task NextChannelAsync()
    {
        if (_exiting) return;
        if (_settings.NextChannel() is not { } channel)
        {
            Report(T("Switch.NoChannels"), error: true);
            return;
        }
        SetCycleChannel(channel);
        string? preset = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            preset = await _sonar.SelectedPresetAsync(channel, timeout.Token);
        }
        // Switching the channel needs no Sonar; the notification only names the preset when Sonar answers.
        catch (Exception) { }
        if (_exiting) return;
        string name = ChannelName(channel);
        Report(preset == null ? T("Status.ChannelSelected", name) : T("Status.ChannelSelectedPreset", name, preset),
            notify: true, title: T("Notification.ChannelTitle"));
    }

    public Task CycleAsync()
    {
        var channel = _settings.CycleChannel;
        var saved = _settings.Channel(channel);
        if (saved.Presets.Count == 0)
        {
            Report(T("Switch.NoConfiguredPresets", ChannelName(channel)), error: true);
            return Task.CompletedTask;
        }
        return RunSonarAsync(ct => _sonar.CycleAsync(channel, saved.Presets, _settings.SwitchAllSound, ct));
    }

    public Task ApplyAsync(SonarChannel channel, PresetBinding binding) =>
        RunSonarAsync(ct => _sonar.ApplyAsync(channel, binding, _settings.SwitchAllSound, ct));

    private async Task RunSonarAsync(Func<CancellationToken, Task<SwitchResult>> action)
    {
        if (_busy || _exiting) return;
        SetBusy(true);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var result = await action(timeout.Token);
            _selected[result.Channel] = result.PresetId;
            _window?.UpdateSelection();
            // The cycled channel stays: a preset applied by its own shortcut, the menu or the window, say a Mic preset,
            // leaves Ctrl+Alt+1…9 and the next preset shortcut on the channel being stepped through.
            string message = ChannelName(result.Channel) + ": " + result.Summary;
            _host?.SetTip("SonarHotkeys — " + message);
            Report(message, notify: true);
        }
        catch (Exception ex) { if (!_exiting) Report(_sonar.ErrorText(ex), error: true); }
        finally { SetBusy(false); }
    }

    private void OnHotkey(int id)
    {
        if (_exiting) return;
        if (id == CycleId) _ = CycleAsync();
        else if (id == ChannelId) _ = NextChannelAsync();
        else if (id is > NumberBaseId and <= NumberBaseId + 9) _ = ApplyNumberAsync(id - NumberBaseId);
        else if (_hotkeys.TryGetValue(id, out var target))
            _ = ApplyAsync(target.Channel, target.Binding);
    }

    private void ShowMenu()
    {
        if (_host == null || _exiting) return;
        // One submenu per channel with presets; the cycled channel is checked.
        var commands = new List<(SonarChannel Channel, PresetBinding Binding)>();
        var submenus = new List<TrayMenuItem>();
        foreach (var channel in _settings.ConfiguredChannels())
        {
            var known = _settings.KnownPresets.GetValueOrDefault(channel) ?? [];
            var presets = new List<TrayMenuItem>();
            foreach (var binding in _settings.Channel(channel).Presets)
            {
                string name = known.FirstOrDefault(c => c.Id == binding.PresetId)?.Name ?? T("Choice.UnavailablePreset", binding.PresetId);
                presets.Add(new(name, MenuPresetBase + commands.Count, !_busy, _selected.GetValueOrDefault(channel) == binding.PresetId));
                commands.Add((channel, binding));
            }
            submenus.Add(new(ChannelName(channel), Checked: channel == _settings.CycleChannel, Children: presets));
        }
        var menu = new List<TrayMenuItem> { new(T("Tray.Settings"), MenuSettings), TrayMenuItem.Separator };
        menu.AddRange(submenus);
        if (submenus.Count > 0) menu.Add(TrayMenuItem.Separator);
        menu.AddRange(
        [
            new(T("Tray.NextChannel"), MenuNextChannel, submenus.Count > 0),
            new(T("Tray.Next"), MenuNext, !_busy && _settings.Channel(_settings.CycleChannel).Presets.Count > 0),
            new(T("Sidebar.Refresh"), MenuRefresh, !_busy),
            TrayMenuItem.Separator,
            new(T("Autostart.Label"), MenuAutostart, Checked: StartsWithWindows),
            new(T("Tray.Exit"), MenuExit),
        ]);
        int command = _host.ShowMenu(menu);
        switch (command)
        {
            case MenuSettings: ShowWindow(); break;
            case MenuAutostart: SetStartWithWindows(!StartsWithWindows); break;
            case MenuNextChannel: _ = NextChannelAsync(); break;
            case MenuNext: _ = CycleAsync(); break;
            case MenuRefresh: _ = RefreshAsync(); break;
            case MenuExit: Exit(); break;
            case >= MenuPresetBase when command - MenuPresetBase < commands.Count:
                var (channel, binding) = commands[command - MenuPresetBase];
                _ = ApplyAsync(channel, binding);
                break;
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        if (!_exiting) _window?.SetBusy(busy);
    }

    public void Report(string message, bool error = false, bool notify = false, string? title = null)
    {
        if (_exiting) return;
        LastMessage = (message, error);
        _window?.ShowStatus(message, error);
        if (error || notify)
            _host?.ShowBalloon(error ? T("Notification.ErrorTitle") : title ?? T("Notification.SwitchedTitle"),
                message.Length > 250 ? message[..247] + "…" : message, error);
    }
}

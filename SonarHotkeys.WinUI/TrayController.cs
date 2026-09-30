using SonarHotkeys.Interop;

namespace SonarHotkeys;

/// <summary>
/// The part of the app that lives in the tray: saved settings, global hotkeys, Sonar calls and notifications.
/// The settings window is only an editor on top of it and may stay closed.
/// </summary>
internal sealed class TrayController
{
    private const int CycleId = 10000;
    private const int MenuSettings = 1, MenuNext = 2, MenuRefresh = 3, MenuExit = 4, MenuAutostart = 5, MenuPresetBase = 100;
    private readonly string _settingsPath;
    private readonly SonarService _sonar;
    private readonly Autostart _autostart = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<int, PresetBinding> _hotkeys = [];
    private TrayHost? _host;
    private MainWindow? _window;
    private AppSettings _settings;
    private bool _busy, _exiting, _hotkeysSuspended;
    private string? _selectedId, _startupError;

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

    /// <summary>The saved configuration; the window edits a copy until Save.</summary>
    public AppSettings Settings => _settings;
    public bool IsBusy => _busy;
    public nint WindowIcon => _host?.WindowIcon ?? 0;
    public bool StartsWithWindows => _autostart.IsEnabled;
    public (string Text, bool Error)? LastMessage { get; private set; }

    public string T(string key, params object[] arguments) => TextCatalog.Get(key, _settings.Language, arguments);

    public string ErrorText(Exception ex) => _sonar.ErrorText(ex);

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
        // A moved portable folder would leave the Run entry pointing at the old path.
        // Skipped in Debug builds, so running one never redirects the real entry.
        try { _autostart.Refresh(Environment.ProcessPath!); }
        catch (Exception ex) { Report(T("Autostart.Failed", ex.Message), error: true); }
#endif
        // A fresh installation always shows setup, even if launched with --tray.
        if (!startHidden || _settings.Bindings.Count == 0 || _startupError != null) ShowWindow();
        if (_startupError != null) Report(_startupError, error: true);
        else if (_settings.Bindings.Count == 0)
            Report(T("Status.Welcome"));
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
        void Register(int id, string text, PresetBinding? binding)
        {
            try
            {
                if (Hotkey.Parse(text, _settings.Language) is not { } key) return;
                int error = _host.RegisterHotkey(id, key);
                if (error != 0)
                    throw new InvalidOperationException(T("Shortcut.RegisterFailed", text, error));
                if (binding != null) _hotkeys[id] = binding;
            }
            catch (Exception ex) { errors.Add(ex.Message); }
        }
        for (int i = 0; i < _settings.Bindings.Count; i++) Register(i + 1, _settings.Bindings[i].Hotkey, _settings.Bindings[i]);
        Register(CycleId, _settings.CycleHotkey, null);
        if (errors.Count == 0) return;
        string message = string.Join(Environment.NewLine, errors);
        if (throwOnError) throw new InvalidOperationException(message);
        Report(message, error: true);
    }

    /// <summary>
    /// Validates, registers and saves the edited bindings; on failure the previous configuration stays active.
    /// While a shortcut is being captured the hotkeys stay released and are registered when capture ends.
    /// </summary>
    public void Save(List<PresetBinding> bindings, string cycleHotkey, bool capturing = false)
    {
        var candidate = new AppSettings
        {
            Bindings = bindings, Favorites = _settings.Favorites.ToList(), Devices = _settings.Devices.ToList(),
            CycleHotkey = cycleHotkey, Language = _settings.Language,
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

    public void SetStartWithWindows(bool enabled)
    {
        try
        {
            if (enabled) _autostart.Enable(Environment.ProcessPath!);
            else _autostart.Disable();
        }
        catch (Exception ex) { Report(T("Autostart.Failed", ex.Message), error: true); }
        _window?.UpdateAutostart();
    }

    public void ChangeLanguage(string language)
    {
        if (language == _settings.Language) return;
        _settings.Language = language;
        _window?.ApplyLanguage();
        // The window itself shows the change, so only a failure is reported.
        // A damaged settings file is not overwritten here; the next binding change saves the language too.
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
            _settings.Favorites = inventory.Favorites;
            _settings.Devices = inventory.Devices;
            if (_startupError == null) _settings.Save(_settingsPath);
            _window?.UpdateChoices();
            int favorites = inventory.Favorites.Count, devices = inventory.Devices.Count;
            Report(favorites == 0
                ? T("Status.NoFavorites")
                : _settings.Bindings.Count == 0
                    ? T("Status.FoundFirstRun", favorites, devices)
                    : T("Status.Found", favorites, devices));
        }
        catch (Exception ex) { if (!_exiting) Report(_sonar.ErrorText(ex), error: true); }
        finally { SetBusy(false); }
    }

    public Task CycleAsync() => RunSonarAsync(ct => _sonar.CycleAsync(_settings.Bindings, ct));

    public Task ApplyAsync(PresetBinding binding) => RunSonarAsync(ct => _sonar.ApplyAsync(binding, ct));

    private async Task RunSonarAsync(Func<CancellationToken, Task<SwitchResult>> action)
    {
        if (_busy || _exiting) return;
        SetBusy(true);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var result = await action(timeout.Token);
            _selectedId = result.PresetId;
            _host?.SetTip("SonarHotkeys — " + result.Summary);
            Report(result.Summary, notify: true);
        }
        catch (Exception ex) { if (!_exiting) Report(_sonar.ErrorText(ex), error: true); }
        finally { SetBusy(false); }
    }

    private void OnHotkey(int id)
    {
        if (_exiting) return;
        if (id == CycleId) _ = CycleAsync();
        else if (_hotkeys.TryGetValue(id, out var binding)) _ = ApplyAsync(binding);
    }

    private IEnumerable<Choice> ConfiguredFavorites() => _settings.Bindings
        .Select(b => _settings.Favorites.FirstOrDefault(f => f.Id == b.PresetId)).OfType<Choice>().DistinctBy(f => f.Id);

    private void ShowMenu()
    {
        if (_host == null || _exiting) return;
        var favorites = ConfiguredFavorites().ToList();
        var presets = favorites.Select((f, i) => new TrayMenuItem(f.Name, MenuPresetBase + i, !_busy, f.Id == _selectedId)).ToList();
        var menu = new List<TrayMenuItem>
        {
            new(T("Tray.Settings"), MenuSettings),
            new(T("Tray.Favorites"), Enabled: !_busy && presets.Count > 0, Children: presets),
            new(T("Tray.Next"), MenuNext, !_busy && presets.Count > 0),
            new(T("Toolbar.Refresh"), MenuRefresh, !_busy),
            TrayMenuItem.Separator,
            new(T("Autostart.Label"), MenuAutostart, Checked: StartsWithWindows),
            new(T("Tray.Exit"), MenuExit),
        };
        int command = _host.ShowMenu(menu);
        switch (command)
        {
            case MenuSettings: ShowWindow(); break;
            case MenuAutostart: SetStartWithWindows(!StartsWithWindows); break;
            case MenuNext: _ = CycleAsync(); break;
            case MenuRefresh: _ = RefreshAsync(); break;
            case MenuExit: Exit(); break;
            case >= MenuPresetBase when command - MenuPresetBase < favorites.Count:
                var favorite = favorites[command - MenuPresetBase];
                _ = ApplyAsync(_settings.Bindings.First(b => b.PresetId == favorite.Id));
                break;
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        if (!_exiting) _window?.SetBusy(busy);
    }

    public void Report(string message, bool error = false, bool notify = false)
    {
        if (_exiting) return;
        LastMessage = (message, error);
        _window?.ShowStatus(message, error);
        if (error || notify)
            _host?.ShowBalloon(error ? T("Notification.ErrorTitle") : T("Notification.SwitchedTitle"),
                message.Length > 250 ? message[..247] + "…" : message, error);
    }
}

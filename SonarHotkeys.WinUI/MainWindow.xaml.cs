using System.Collections.ObjectModel;
using System.Reflection;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using SonarHotkeys.Interop;
using Windows.Graphics;
using Windows.System;
using Windows.UI.Core;
using DispatcherQueuePriority = Microsoft.UI.Dispatching.DispatcherQueuePriority;

namespace SonarHotkeys;

/// <summary>The settings editor. Every change is saved right away through the <see cref="TrayController"/>.</summary>
public sealed partial class MainWindow : Window
{
    private readonly TrayController _app;
    private readonly nint _hwnd;
    private readonly DispatcherTimer _statusTimer = new();
    // Picks up presets selected directly in GG while the window is open; the tray needs no polling.
    private readonly DispatcherTimer _selectionTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Dictionary<string, RadioMenuFlyoutItem> _languageItems = [];
    private readonly Dictionary<SonarChannel, ObservableCollection<BindingRow>> _rows = [];
    // Favorites removed by hand, so a refresh does not add them again; adding one back forgets it.
    private readonly Dictionary<SonarChannel, HashSet<string>> _removed = [];
    private SonarChannel? _shown;
    private bool _exiting, _capturing, _updating, _addingRows, _keyboardUsed;

    public List<ChannelRow> Channels { get; } = [.. Enum.GetValues<SonarChannel>().Select(c => new ChannelRow(c, Glyph(c)))];

    internal MainWindow(TrayController app)
    {
        _app = app;
        InitializeComponent();
        _hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        if (_app.WindowIcon != 0) AppWindow.SetIcon(Win32Interop.GetIconIdFromIcon(_app.WindowIcon));
        PlaceWindow();
        // Closing hides the window; the app keeps running in the tray.
        AppWindow.Closing += (_, e) =>
        {
            if (_exiting) return;
            e.Cancel = true;
            _selectionTimer.Stop();
            AppWindow.Hide();
        };
        // Hotkeys are released only while a shortcut box has focus in the active window,
        // so switching to a game right after capturing a shortcut already uses it.
        Activated += (_, e) =>
        {
            if (!_capturing) return;
            if (e.WindowActivationState == WindowActivationState.Deactivated) _app.ResumeHotkeys();
            else _app.SuspendHotkeys();
        };
        _statusTimer.Tick += (_, _) => HideStatus();
        _selectionTimer.Tick += async (_, _) => await _app.RefreshSelectionAsync();
        SetUpStatusAnimations();
        SetUpFocusVisual();
        BuildLanguageMenu();
        foreach (var channel in Enum.GetValues<SonarChannel>())
        {
            var saved = _app.Settings.Channel(channel);
            var rows = new ObservableCollection<BindingRow>();
            _rows[channel] = rows;
            _removed[channel] = [.. saved.Removed];
            foreach (var binding in saved.Presets) AddRow(rows, new(binding.PresetId, binding.DeviceId, binding.Hotkey));
            // Subscribed after loading, so only user edits save. Reordering removes and inserts a card.
            rows.CollectionChanged += (_, _) =>
            {
                UpdateChannels();
                UpdateNumbers();
                UpdateEmpty();
                SaveEdits();
            };
        }
        ChannelBox.Text = _app.Settings.ChannelHotkey;
        CycleBox.Text = _app.Settings.CycleHotkey;
        ApplyLanguage();
        ChannelsList.SelectedItem = Channels.First(s => s.Channel == _app.Settings.CycleChannel);
        SetBusy(_app.IsBusy);
        UpdateToggles();
        if (_app.LastMessage is { } last) ShowStatus(last.Text, last.Error);
    }

    private static string Glyph(SonarChannel channel) => channel switch
    {
        SonarChannel.Game => "\uE7FC",
        SonarChannel.Chat => "\uE8BD",
        SonarChannel.Media => "\uE8D6",
        SonarChannel.Aux => "\uE767",
        _ => "\uE720",
    };

    private void AddRow(ObservableCollection<BindingRow> rows, BindingRow row)
    {
        rows.Add(row);
        row.Edited += SaveEdits;
    }

    /// <summary>Saves the whole editor; an invalid state is reported and the last saved configuration stays active.</summary>
    private void SaveEdits()
    {
        if (_exiting || _addingRows) return;
        // A changed or added card may now name the selected preset.
        UpdateSelection();
        foreach (var (channel, rows) in _rows) _removed[channel].RemoveWhere(id => rows.Any(r => r.PresetId == id));
        var channels = _rows.ToDictionary(p => p.Key, p => new ChannelSettings
        {
            Presets = [.. p.Value.Select(r => r.ToBinding())], Removed = [.. _removed[p.Key]],
        });
        try { _app.Save(channels, ChannelBox.Text, CycleBox.Text, _capturing); }
        catch (Exception ex) { _app.Report(_app.ErrorText(ex), error: true); }
    }

    /// <summary>Shows the presets that a refresh added to the saved channels from the favorites in GG.</summary>
    public void AddNewRows()
    {
        // Every card added saves the editor, which replaces the saved settings, so the new presets of all channels
        // are taken first and saved once at the end.
        var added = _rows.ToDictionary(p => p.Key, p => _app.Settings.Channel(p.Key).Presets
            .Where(b => !p.Value.Any(r => r.PresetId == b.PresetId)).ToList());
        _addingRows = true;
        try
        {
            foreach (var (channel, bindings) in added)
                foreach (var binding in bindings)
                {
                    var row = new BindingRow(binding.PresetId, binding.DeviceId, binding.Hotkey);
                    row.SetText(key => _app.T(key), DeviceLabel(channel));
                    AddRow(_rows[channel], row);
                }
        }
        finally { _addingRows = false; }
        UpdateChoices();
        SaveEdits();
    }

    private void PlaceWindow()
    {
        double scale = Native.GetDpiForWindow(_hwnd) / 96.0;
        int Scaled(int value) => (int)(value * scale);
        var size = new SizeInt32(Scaled(1040), Scaled(680));
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        size = new(Math.Min(size.Width, area.Width), Math.Min(size.Height, area.Height));
        AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - size.Width) / 2, area.Y + (area.Height - size.Height) / 2, size.Width, size.Height));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = Scaled(860);
            presenter.PreferredMinimumHeight = Scaled(480);
        }
    }

    public void ShowAndActivate()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter) presenter.Restore();
        // The tray menu can change it while the window is hidden.
        UpdateToggles();
        AppWindow.Show();
        Activate();
        Native.SetForegroundWindow(_hwnd);
        UpdateSelection();
        _ = _app.RefreshSelectionAsync();
        _selectionTimer.Start();
    }

    public void CloseForExit()
    {
        _exiting = true;
        _selectionTimer.Stop();
        Close();
    }

    public void ApplyLanguage()
    {
        string T(string key) => _app.T(key);
        AppWindow.Title = T("Window.Title");
        ChannelsHeading.Text = T("Sidebar.Channels");
        SettingsItemText.Text = T("Sidebar.Settings");
        HelpItemText.Text = T("Sidebar.Help");
        AutomationProperties.SetName(HelpItem, HelpItemText.Text);
        if (HelpPage.Visibility == Visibility.Visible) BuildHelp();
        CycleBadgeText.Text = T("Page.Cycled");
        CycleHereText.Text = T("Page.CycleHere");
        AutomationProperties.SetName(CycleHereButton, CycleHereText.Text);
        AllSoundLabel.Text = T("Options.AllSound");
        AllSoundHint.Text = T("Options.AllSoundHint");
        NumbersLabel.Text = T("Options.Numbers");
        NumbersHint.Text = T("Options.NumbersHint");
        PresetsHeading.Text = T("Page.Presets");
        PresetsHint.Text = T("Page.PresetsHint");
        AddText.Text = T("Page.Add");
        EmptyText.Text = T("Page.Empty");
        SettingsHeading.Text = T("Options.Title");
        LanguageLabel.Text = T("Options.Language");
        AutostartLabel.Text = T("Autostart.Label");
        AutostartHint.Text = T("Autostart.Hint");
        ShortcutsHeading.Text = T("Options.Shortcuts");
        ShortcutsHint.Text = T("Options.ShortcutHint");
        ChannelLabel.Text = T("Options.Channel");
        ChannelHint.Text = T("Options.ChannelHint");
        CycleLabel.Text = T("Options.Cycle");
        CycleHint.Text = T("Options.CycleHint");
        ChannelBox.PlaceholderText = CycleBox.PlaceholderText = T("Shortcut.Placeholder");
        // Icon-only buttons and buttons with an icon and text inside a panel get no automatic accessible name.
        ToolTipService.SetToolTip(RefreshButton, T("Sidebar.Refresh"));
        AutomationProperties.SetName(RefreshButton, T("Sidebar.Refresh"));
        AutomationProperties.SetName(AddButton, AddText.Text);
        AutomationProperties.SetName(SettingsItem, SettingsItemText.Text);
        AutomationProperties.SetName(AllSoundSwitch, AllSoundLabel.Text);
        AutomationProperties.SetName(NumbersSwitch, T("Options.Numbers"));
        AutomationProperties.SetName(AutostartSwitch, AutostartLabel.Text);
        AutomationProperties.SetName(LanguageButton, LanguageLabel.Text);
        AutomationProperties.SetName(ChannelBox, ChannelLabel.Text);
        AutomationProperties.SetName(CycleBox, CycleLabel.Text);
        foreach (var (code, item) in _languageItems) item.IsChecked = code == _app.Settings.Language;
        LanguageButton.Content = TextCatalog.Languages.FirstOrDefault(l => l.Code == _app.Settings.Language)?.Name;
        foreach (var (channel, rows) in _rows)
            foreach (var row in rows) row.SetText(T, DeviceLabel(channel));
        UpdateChannels();
        UpdateNumbers();
        UpdateChoices();
    }

    private string DeviceLabel(SonarChannel channel) => _app.T(AppSettings.IsOutput(channel) ? "Card.Output" : "Card.Input");

    /// <summary>Refreshes the sidebar and the header of the shown channel: names, preset counts and the cycled channel mark.</summary>
    public void UpdateChannels()
    {
        foreach (var item in Channels)
        {
            int count = _rows[item.Channel].Count;
            item.Update(_app.T(AppSettings.ChannelKey(item.Channel)),
                count == 0 ? _app.T("Page.None") : _app.T("Page.Count", count), item.Channel == _app.Settings.CycleChannel);
        }
        if (_shown is not { } channel) return;
        ChannelIcon.Glyph = Glyph(channel);
        ChannelHeading.Text = _app.T(AppSettings.ChannelKey(channel));
        bool active = channel == _app.Settings.CycleChannel;
        CycleBadge.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        CycleHereButton.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Rebuilds the choice lists from the discovered presets and devices, keeping unknown saved ids visible.</summary>
    /// <summary>Marks, in every channel, the card of the preset that Sonar has selected now.</summary>
    /// <summary>Shows on each card the number shortcut of its place: Ctrl+Alt+1 for the first one, up to the ninth.</summary>
    public void UpdateNumbers()
    {
        foreach (var rows in _rows.Values)
            for (int i = 0; i < rows.Count; i++)
                rows[i].SetNumber(_app.Settings.NumberHotkeys && i < 9 ? AppSettings.NumberHotkeyText(i + 1) : null);
    }

    public void UpdateSelection()
    {
        foreach (var (channel, rows) in _rows)
        {
            string? selected = _app.SelectedPreset(channel);
            foreach (var row in rows) row.SetSelected(row.PresetId == selected);
        }
    }

    public void UpdateChoices()
    {
        string T(string key, params object[] arguments) => _app.T(key, arguments);
        var settings = _app.Settings;
        foreach (var (channel, rows) in _rows)
        {
            List<ChoiceItem> presets = [.. (settings.KnownPresets.GetValueOrDefault(channel) ?? []).Select(c => new ChoiceItem(c.Id, c.Name))];
            foreach (string id in rows.Select(r => r.PresetId).Where(id => id.Length > 0).Distinct())
                if (!presets.Any(c => c.Id == id)) presets.Add(new(id, T("Choice.UnavailablePreset", id)));
            var known = AppSettings.IsOutput(channel) ? settings.Outputs : settings.Inputs;
            List<ChoiceItem> devices = [new("", T("Card.KeepDevice")), .. known.Select(c => new ChoiceItem(c.Id, c.Name))];
            foreach (string id in rows.Select(r => r.DeviceId).Where(id => id.Length > 0).Distinct())
                if (!devices.Any(c => c.Id == id)) devices.Add(new(id, T("Choice.UnavailableDevice", id)));
            foreach (var row in rows) row.SetChoices(presets, devices);
        }
    }

    public void SetBusy(bool busy)
    {
        foreach (Control control in new Control[] { RefreshButton, AddButton, RowsList, CycleHereButton }) control.IsEnabled = !busy;
    }

    private void OnChannelSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ChannelsList.SelectedItem is not ChannelRow item) return;
        FooterList.SelectedItem = null;
        _shown = item.Channel;
        RowsList.ItemsSource = _rows[item.Channel];
        UpdateChannels();
        UpdateEmpty();
        ChannelPage.Visibility = Visibility.Visible;
        SettingsPage.Visibility = HelpPage.Visibility = Visibility.Collapsed;
    }

    private void OnFooterSelected(object sender, SelectionChangedEventArgs e)
    {
        if (FooterList.SelectedItem == null) return;
        ChannelsList.SelectedItem = null;
        _shown = null;
        bool help = FooterList.SelectedItem is ListViewItem item && item == HelpItem;
        // Filled when shown, so it names the shortcuts as they are set now.
        if (help) BuildHelp();
        ChannelPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = help ? Visibility.Collapsed : Visibility.Visible;
        HelpPage.Visibility = help ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Fills the help page: how the app works, the shortcuts in use and what to do when something fails.</summary>
    private void BuildHelp()
    {
        string T(string key, params object[] arguments) => _app.T(key, arguments);
        var settings = _app.Settings;
        string Shortcut(string text) => text.Length > 0 ? text : T("Help.NotAssigned");
        void Card(TextBlock title, TextBlock text, string titleKey, string textKey)
        {
            title.Text = T(titleKey);
            text.Text = T(textKey);
        }
        HelpHeading.Text = T("Help.Title");
        HelpHowItWorks.Text = T("Help.HowItWorks");
        Card(HelpChannelsTitle, HelpChannelsText, "Help.Channels", "Help.ChannelsText");
        Card(HelpPresetsTitle, HelpPresetsText, "Help.Presets", "Help.PresetsText");
        Card(HelpCycledTitle, HelpCycledText, "Help.Cycled", "Help.CycledText");
        Card(HelpSelectedTitle, HelpSelectedText, "Help.Selected", "Help.SelectedText");
        HelpShortcuts.Text = T("Options.Shortcuts");
        Card(HelpChannelKeyTitle, HelpChannelKeyText, "Options.Channel", "Options.ChannelHint");
        HelpChannelKeyValue.Text = Shortcut(settings.ChannelHotkey);
        Card(HelpCycleKeyTitle, HelpCycleKeyText, "Options.Cycle", "Options.CycleHint");
        HelpCycleKeyValue.Text = Shortcut(settings.CycleHotkey);
        Card(HelpNumbersTitle, HelpNumbersText, "Options.Numbers", "Options.NumbersHint");
        HelpNumbersValue.Text = settings.NumberHotkeys ? "Ctrl + Alt + 1…9" : T("Help.Off");
        Card(HelpOwnTitle, HelpOwnText, "Help.Own", "Help.OwnText");
        HelpProblems.Text = T("Help.Problems");
        Card(HelpNoSonarTitle, HelpNoSonarText, "Help.NoSonar", "Help.NoSonarText");
        Card(HelpNoPresetTitle, HelpNoPresetText, "Help.NoPreset", "Help.NoPresetText");
        Card(HelpBusyTitle, HelpBusyText, "Help.Busy", "Help.BusyText");
        Card(HelpNoSoundTitle, HelpNoSoundText, "Help.NoSound", "Help.NoSoundText");
        HelpProject.Content = T("Help.Project");
        string version = typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "";
        HelpVersion.Text = T("Help.Version", version);
    }

    private void UpdateEmpty() =>
        EmptyText.Visibility = _shown is { } channel && _rows[channel].Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void OnCycleHere(object sender, RoutedEventArgs e)
    {
        if (_shown is { } channel) _app.SetCycleChannel(channel);
    }

    private void OnNumbersToggled(object sender, RoutedEventArgs e)
    {
        if (!_updating) _app.SetNumberHotkeys(NumbersSwitch.IsOn);
    }

    private void OnAllSoundToggled(object sender, RoutedEventArgs e)
    {
        if (!_updating) _app.SetSwitchAllSound(AllSoundSwitch.IsOn);
    }

    public void ShowStatus(string message, bool error)
    {
        Status.Message = message;
        Status.Severity = error ? InfoBarSeverity.Error : InfoBarSeverity.Informational;
        StatusPanel.Visibility = Visibility.Visible;
        // Hides itself; errors stay a little longer.
        _statusTimer.Stop();
        _statusTimer.Interval = TimeSpan.FromSeconds(error ? 10 : 7);
        _statusTimer.Start();
    }

    private void HideStatus()
    {
        _statusTimer.Stop();
        StatusPanel.Visibility = Visibility.Collapsed;
    }

    // The InfoBar stays open inside the panel; its close button hides the panel, so the hide animation plays.
    private void OnStatusClosing(InfoBar sender, InfoBarClosingEventArgs args)
    {
        args.Cancel = true;
        HideStatus();
    }

    /// <summary>Fades and slides the floating status panel in and out whenever its visibility changes.</summary>
    private void SetUpStatusAnimations()
    {
        ElementCompositionPreview.SetIsTranslationEnabled(StatusPanel, true);
        var compositor = ElementCompositionPreview.GetElementVisual(StatusPanel).Compositor;
        CompositionAnimationGroup Animation(float fromOpacity, float toOpacity, float fromY, float toY, int milliseconds)
        {
            var fade = compositor.CreateScalarKeyFrameAnimation();
            fade.Target = "Opacity";
            fade.InsertKeyFrame(0, fromOpacity);
            fade.InsertKeyFrame(1, toOpacity);
            fade.Duration = TimeSpan.FromMilliseconds(milliseconds);
            var slide = compositor.CreateScalarKeyFrameAnimation();
            slide.Target = "Translation.Y";
            slide.InsertKeyFrame(0, fromY);
            slide.InsertKeyFrame(1, toY);
            slide.Duration = TimeSpan.FromMilliseconds(milliseconds);
            var group = compositor.CreateAnimationGroup();
            group.Add(fade);
            group.Add(slide);
            return group;
        }
        ElementCompositionPreview.SetImplicitShowAnimation(StatusPanel, Animation(0, 1, 12, 0, 200));
        ElementCompositionPreview.SetImplicitHideAnimation(StatusPanel, Animation(1, 0, 0, 12, 150));
    }

    // On activation WinUI focuses the first control as if reached with Tab, which draws a white focus rectangle
    // around it, sometimes well after the window is activated. Until a key is pressed in the window, such a focus
    // is set again programmatically: the control keeps the focus without the rectangle. Tab still shows it as usual.
    private void SetUpFocusVisual()
    {
        var root = (UIElement)Content;
        root.PreviewKeyDown += (_, _) => _keyboardUsed = true;
        root.GotFocus += (_, e) =>
        {
            if (!_keyboardUsed && e.OriginalSource is Control { FocusState: FocusState.Keyboard } control) HideFocusVisual(control);
        };
        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated) return;
            _keyboardUsed = false;
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                if (Content.XamlRoot is { } xamlRoot && FocusManager.GetFocusedElement(xamlRoot) is Control { FocusState: FocusState.Keyboard } control)
                    HideFocusVisual(control);
            });
        };
    }

    private void HideFocusVisual(Control control)
    {
        // A shortcut box shows its caret, not a rectangle, and refocusing it would restart capture.
        if (control is TextBox) return;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => control.Focus(FocusState.Programmatic));
    }

    public void UpdateToggles()
    {
        _updating = true;
        AutostartSwitch.IsOn = _app.StartsWithWindows;
        AllSoundSwitch.IsOn = _app.Settings.SwitchAllSound;
        NumbersSwitch.IsOn = _app.Settings.NumberHotkeys;
        _updating = false;
    }

    private void OnAutostartToggled(object sender, RoutedEventArgs e)
    {
        if (!_updating) _app.SetStartWithWindows(AutostartSwitch.IsOn);
    }

    private void BuildLanguageMenu()
    {
        foreach (var language in TextCatalog.Languages)
        {
            var item = new RadioMenuFlyoutItem { Text = language.Name, GroupName = "Language" };
            // The language is captured here rather than read from the sender or a Tag.
            item.Click += (_, _) => _app.ChangeLanguage(language.Code);
            LanguageMenu.Items.Add(item);
            _languageItems[language.Code] = item;
        }
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) => await _app.RefreshAsync();

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        if (_shown is not { } channel) return;
        var rows = _rows[channel];
        var known = _app.Settings.KnownPresets.GetValueOrDefault(channel) ?? [];
        var next = known.FirstOrDefault(f => !rows.Any(r => r.PresetId == f.Id));
        if (next == null)
        {
            string name = _app.T(AppSettings.ChannelKey(channel));
            _app.Report(known.Count == 0 ? _app.T("Status.NoFavoritesToAdd", name) : _app.T("Status.AllFavoritesAdded", name));
            return;
        }
        var row = new BindingRow(next.Id, "", "");
        row.SetText(key => _app.T(key), DeviceLabel(channel));
        AddRow(rows, row);
        UpdateChoices();
        // After layout: scrolling to a card that has no container yet re-created every card below the first,
        // so the whole list blinked on Add.
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => RowsList.ScrollIntoView(row));
    }

    private async void OnApplyRow(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BindingRow row && _shown is { } channel)
            await _app.ApplyAsync(channel, row.ToBinding());
    }

    private void OnRemoveRow(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not BindingRow row || _shown is not { } channel) return;
        if (row.PresetId.Length > 0) _removed[channel].Add(row.PresetId);
        _rows[channel].Remove(row);
    }

    private void OnHotkeyFocus(object sender, RoutedEventArgs e)
    {
        _capturing = true;
        _app.SuspendHotkeys();
    }

    private void OnHotkeyLostFocus(object sender, RoutedEventArgs e)
    {
        _capturing = false;
        _app.ResumeHotkeys();
    }

    /// <summary>Captures a combination into the focused shortcut box; Delete or Backspace clears it.</summary>
    private void OnHotkeyKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not TextBox box) return;
        static bool Down(VirtualKey key) => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
        bool control = Down(VirtualKey.Control), alt = Down(VirtualKey.Menu), shift = Down(VirtualKey.Shift);
        // Plain Tab keeps moving focus between fields.
        if (e.Key == VirtualKey.Tab && !control && !alt) return;
        e.Handled = true;
        string? text = e.Key is VirtualKey.Back or VirtualKey.Delete && !control && !alt && !shift
            ? ""
            : Hotkey.Format(control, alt, shift, (uint)e.Key);
        if (text == null) return;
        // A card saves through its Edited event; the boxes on the settings page save here.
        if (box.DataContext is BindingRow row && box != ChannelBox && box != CycleBox) row.Hotkey = text;
        else if (box.Text != text)
        {
            box.Text = text;
            SaveEdits();
        }
    }
}

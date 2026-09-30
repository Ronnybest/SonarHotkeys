using System.Collections.ObjectModel;
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

namespace SonarHotkeys;

/// <summary>The settings editor. Every change is saved right away through the <see cref="TrayController"/>.</summary>
public sealed partial class MainWindow : Window
{
    private readonly TrayController _app;
    private readonly nint _hwnd;
    private readonly DispatcherTimer _statusTimer = new();
    private List<ChoiceItem> _presets = [], _devices = [];
    private readonly Dictionary<string, RadioMenuFlyoutItem> _languageItems = [];
    private bool _exiting, _capturing, _updatingAutostart;

    public ObservableCollection<BindingRow> Rows { get; } = [];

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
            AppWindow.Hide();
        };
        // Hotkeys are released only while a shortcut box has focus in the active window,
        // so switching to a game right after capturing a shortcut already uses it.
        Activated += (_, e) =>
        {
            if (e.WindowActivationState != WindowActivationState.Deactivated) HideInitialFocusVisual();
            if (!_capturing) return;
            if (e.WindowActivationState == WindowActivationState.Deactivated) _app.ResumeHotkeys();
            else _app.SuspendHotkeys();
        };
        _statusTimer.Tick += (_, _) => HideStatus();
        SetUpStatusAnimations();
        BuildLanguageMenu();
        foreach (var binding in _app.Settings.Bindings) AddRow(new(binding.PresetId, binding.DeviceId, binding.Hotkey));
        CycleBox.Text = _app.Settings.CycleHotkey;
        // Subscribed after loading, so only user edits save.
        Rows.CollectionChanged += (_, _) =>
        {
            EmptyText.Visibility = Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            SaveEdits();
        };
        ApplyLanguage();
        EmptyText.Visibility = Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SetBusy(_app.IsBusy);
        UpdateAutostart();
        if (_app.LastMessage is { } last) ShowStatus(last.Text, last.Error);
    }

    private void AddRow(BindingRow row)
    {
        row.SetText(key => _app.T(key));
        row.SetChoices(_presets, _devices);
        row.Edited += SaveEdits;
        Rows.Add(row);
    }

    /// <summary>Saves the whole editor; an invalid state is reported and the last saved configuration stays active.</summary>
    private void SaveEdits()
    {
        if (_exiting) return;
        try { _app.Save(Rows.Select(r => r.ToBinding()).ToList(), CycleBox.Text, _capturing); }
        catch (Exception ex) { _app.Report(_app.ErrorText(ex), error: true); }
    }

    private void PlaceWindow()
    {
        double scale = Native.GetDpiForWindow(_hwnd) / 96.0;
        int Scaled(int value) => (int)(value * scale);
        var size = new SizeInt32(Scaled(1000), Scaled(700));
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        size = new(Math.Min(size.Width, area.Width), Math.Min(size.Height, area.Height));
        AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - size.Width) / 2, area.Y + (area.Height - size.Height) / 2, size.Width, size.Height));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = Scaled(760);
            presenter.PreferredMinimumHeight = Scaled(480);
        }
    }

    public void ShowAndActivate()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter) presenter.Restore();
        // The tray menu can change it while the window is hidden.
        UpdateAutostart();
        AppWindow.Show();
        Activate();
        Native.SetForegroundWindow(_hwnd);
    }

    public void CloseForExit()
    {
        _exiting = true;
        Close();
    }

    public void ApplyLanguage()
    {
        string T(string key) => _app.T(key);
        AppWindow.Title = T("Window.Title");
        HeadingText.Text = T("Window.Heading");
        HelpText.Text = T("Window.Help");
        RefreshText.Text = T("Toolbar.Refresh");
        AddText.Text = T("Toolbar.Add");
        CycleLabel.Text = T("Cycle.Label");
        CycleBox.PlaceholderText = T("Shortcut.Placeholder");
        NextText.Text = T("Cycle.Next");
        EmptyText.Text = T("Window.Empty");
        // Buttons with an icon and text inside a panel get no automatic accessible name.
        AutomationProperties.SetName(RefreshButton, RefreshText.Text);
        AutomationProperties.SetName(AddButton, AddText.Text);
        AutomationProperties.SetName(NextButton, NextText.Text);
        AutostartSwitch.OnContent = AutostartSwitch.OffContent = T("Autostart.Label");
        foreach (var (code, item) in _languageItems) item.IsChecked = code == _app.Settings.Language;
        LanguageButton.Content = TextCatalog.Languages.FirstOrDefault(l => l.Code == _app.Settings.Language)?.Name;
        foreach (var row in Rows) row.SetText(T);
        UpdateChoices();
    }

    /// <summary>Rebuilds the choice lists from the discovered presets and devices, keeping unknown saved ids visible.</summary>
    public void UpdateChoices()
    {
        string T(string key, params object[] arguments) => _app.T(key, arguments);
        var settings = _app.Settings;
        _presets = [.. settings.Favorites.Select(c => new ChoiceItem(c.Id, c.Name))];
        foreach (string id in Rows.Select(r => r.PresetId).Where(id => id.Length > 0).Distinct())
            if (!_presets.Any(c => c.Id == id)) _presets.Add(new(id, T("Choice.UnavailablePreset", id)));
        _devices = [new("", T("Card.KeepDevice")), .. settings.Devices.Select(c => new ChoiceItem(c.Id, c.Name))];
        foreach (string id in Rows.Select(r => r.DeviceId).Where(id => id.Length > 0).Distinct())
            if (!_devices.Any(c => c.Id == id)) _devices.Add(new(id, T("Choice.UnavailableDevice", id)));
        foreach (var row in Rows) row.SetChoices(_presets, _devices);
    }

    public void SetBusy(bool busy)
    {
        foreach (Control control in new Control[] { RefreshButton, AddButton, NextButton, RowsList, CycleBox, LanguageButton })
            control.IsEnabled = !busy;
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
    // around the language button. Focusing it again programmatically keeps the focus but hides the rectangle;
    // Tab still shows it as usual.
    private void HideInitialFocusVisual() =>
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (Content.XamlRoot is { } root && FocusManager.GetFocusedElement(root) is Control { FocusState: FocusState.Keyboard } control
                && control is not TextBox)
                control.Focus(FocusState.Programmatic);
        });

    public void UpdateAutostart()
    {
        _updatingAutostart = true;
        AutostartSwitch.IsOn = _app.StartsWithWindows;
        _updatingAutostart = false;
    }

    private void OnAutostartToggled(object sender, RoutedEventArgs e)
    {
        if (!_updatingAutostart) _app.SetStartWithWindows(AutostartSwitch.IsOn);
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

    private async void OnNext(object sender, RoutedEventArgs e) => await _app.CycleAsync();

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        var next = _app.Settings.Favorites.FirstOrDefault(f => !Rows.Any(r => r.PresetId == f.Id));
        if (next == null)
        {
            _app.Report(_app.Settings.Favorites.Count == 0
                ? _app.T("Status.NoFavoritesToAdd")
                : _app.T("Status.AllFavoritesAdded"));
            return;
        }
        var row = new BindingRow(next.Id, "", "");
        AddRow(row);
        // After layout: scrolling to a card that has no container yet re-created every card below the first,
        // so the whole list blinked on Add.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => RowsList.ScrollIntoView(row));
    }

    private async void OnApplyRow(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BindingRow row)
            await _app.ApplyAsync(new PresetBinding { PresetId = row.PresetId, DeviceId = row.DeviceId });
    }

    private void OnRemoveRow(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BindingRow row) Rows.Remove(row);
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
        // A card saves through its Edited event; the cycle box saves here.
        if (box.DataContext is BindingRow row && box != CycleBox) row.Hotkey = text;
        else if (box.Text != text)
        {
            box.Text = text;
            SaveEdits();
        }
    }
}

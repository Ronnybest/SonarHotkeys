using System.ComponentModel;
using System.Runtime.InteropServices;
using SteelSeriesAPI.Core;
using SteelSeriesAPI.Sonar;
using SteelSeriesAPI.Sonar.Enums;

namespace SonarHotkeys;

public partial class Form1 : Form
{
    private const int CycleId = 10000;
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false,
        AllowUserToAddRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private readonly TextBox _cycle = new() { Width = 230, ReadOnly = true };
    private readonly TextBox _output = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly FlowLayoutPanel _toolbar = new() { Dock = DockStyle.Fill, AutoSize = true };
    private readonly NotifyIcon _tray = new();
    private readonly ContextMenuStrip _menu = new();
    private readonly Dictionary<int, PresetBinding> _hotkeys = [];
    private readonly List<int> _registered = [];
    private readonly CancellationTokenSource _lifetime = new();
    private AppSettings _settings;
    private BindingList<PresetBinding> _rows = [];
    private List<Choice> _favorites = [];
    private List<Choice> _devices = [];
    private bool _busy, _exit, _closing;
    private string? _selectedId;
    private string? _startupError;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public Form1()
    {
        try { _settings = AppSettings.Load(); }
        catch (Exception ex)
        {
            _settings = AppSettings.Defaults();
            _startupError = "Не удалось прочитать настройки. Загружены начальные значения. " + ex.Message;
        }
        InitializeComponent();
        Text = "SonarHotkeys — настройки";
        ClientSize = new Size(1020, 570);
        MinimumSize = new Size(850, 450);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 1, RowCount = 5 };
        layout.RowStyles.Add(new(SizeType.Absolute, 52));
        layout.RowStyles.Add(new(SizeType.Absolute, 45));
        layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.Absolute, 45));
        layout.RowStyles.Add(new(SizeType.Absolute, 100));
        layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text =
            "Выберите избранный пресет Game и устройство вывода. Для назначения сочетания нажмите его в ячейке.\n" +
            "Устройство: Game, Chat, Media, Aux; в Streamer — Personal. Закрытие окна скрывает его в трей." }, 0, 0);
        AddButton("Обновить из Sonar", async () => await RefreshAsync());
        AddButton("Добавить", () => { _rows.Add(new()); return Task.CompletedTask; });
        AddButton("Удалить", () => { if (_grid.CurrentRow?.DataBoundItem is PresetBinding row) _rows.Remove(row); return Task.CompletedTask; });
        AddButton("Сохранить", () => { SaveSettings(); return Task.CompletedTask; });
        AddButton("Применить строку", async () =>
        {
            _grid.EndEdit();
            if (_grid.CurrentRow?.DataBoundItem is PresetBinding row)
                await ApplyAsync(new() { PresetId = row.PresetId, DeviceId = row.DeviceId });
        });
        layout.Controls.Add(_toolbar, 0, 1);
        _grid.Columns.Add(new DataGridViewComboBoxColumn { Name = "Preset", HeaderText = "Избранный пресет Game", DataPropertyName = "PresetId",
            DisplayMember = "Name", ValueMember = "Id", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 40 });
        _grid.Columns.Add(new DataGridViewComboBoxColumn { Name = "Device", HeaderText = "Устройство вывода", DataPropertyName = "DeviceId",
            DisplayMember = "Name", ValueMember = "Id", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 35 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Hotkey", HeaderText = "Сочетание (Delete — очистить)", DataPropertyName = "Hotkey",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 25 });
        _grid.EditingControlShowing += (_, e) =>
        {
            if (e.Control is TextBox box)
            {
                ClearHotkeys();
                box.ReadOnly = true;
                box.KeyDown -= Hotkey.Capture;
                box.KeyDown += Hotkey.Capture;
            }
        };
        _grid.CellEndEdit += (_, _) => { if (!_closing) RegisterSettings(); };
        _grid.DataError += (_, e) => { e.ThrowException = false; Report("Некорректное значение в таблице. Выберите пресет или устройство из списка.", true); };
        layout.Controls.Add(_grid, 0, 2);
        var cyclePanel = new FlowLayoutPanel { Dock = DockStyle.Fill };
        cyclePanel.Controls.Add(new Label { Text = "Перебор настроенных пресетов:", AutoSize = true, Padding = new Padding(0, 8, 0, 0) });
        _cycle.KeyDown += Hotkey.Capture;
        _cycle.Enter += (_, _) => ClearHotkeys();
        _cycle.Leave += (_, _) => { if (!_closing) RegisterSettings(); };
        cyclePanel.Controls.Add(_cycle);
        var next = new Button { Text = "Следующий", AutoSize = true };
        next.Click += async (_, _) => await CycleAsync();
        cyclePanel.Controls.Add(next);
        layout.Controls.Add(cyclePanel, 0, 3);
        layout.Controls.Add(_output, 0, 4);
        Controls.Add(layout);
        _tray.Icon = SystemIcons.Application;
        _tray.Text = "SonarHotkeys";
        _tray.ContextMenuStrip = _menu;
        _tray.DoubleClick += (_, _) => ShowWindow();
        _tray.BalloonTipClicked += (_, _) => ShowWindow();
        _tray.Visible = true;
        LoadEditor();
        RebuildMenu();
    }

    private void AddButton(string label, Func<Task> action)
    {
        var button = new Button { Text = label, AutoSize = true };
        button.Click += async (_, _) => { try { await action(); } catch (Exception ex) { Report(ErrorText(ex), true); } };
        _toolbar.Controls.Add(button);
    }

    private void LoadEditor()
    {
        _favorites = _settings.Favorites.ToList();
        _devices = _settings.Devices.ToList();
        _rows = new(_settings.Bindings.Select(b => new PresetBinding { PresetId = b.PresetId, DeviceId = b.DeviceId, Hotkey = b.Hotkey }).ToList());
        UpdateChoices();
        _grid.DataSource = _rows;
        _cycle.Text = _settings.CycleHotkey;
    }

    private void UpdateChoices()
    {
        var presets = new List<Choice> { new("", "— выберите пресет —") };
        presets.AddRange(_favorites);
        foreach (string id in _rows.Select(b => b.PresetId).Where(id => id.Length > 0).Distinct())
            if (!presets.Any(c => c.Id == id)) presets.Add(new(id, "Недоступный пресет: " + id));
        var devices = new List<Choice> { new("", "Не менять устройство") };
        devices.AddRange(_devices);
        foreach (string id in _rows.Select(b => b.DeviceId).Where(id => id.Length > 0).Distinct())
            if (!devices.Any(c => c.Id == id)) devices.Add(new(id, "Недоступное устройство: " + id));
        ((DataGridViewComboBoxColumn)_grid.Columns["Preset"]!).DataSource = presets;
        ((DataGridViewComboBoxColumn)_grid.Columns["Device"]!).DataSource = devices;
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        RegisterSettings();
        if (Environment.GetCommandLineArgs().Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase))) Hide();
        if (_startupError != null) Report(_startupError, true);
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_busy || _closing) return;
        SetBusy(true);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var sonar = new SonarClient();
            var configs = await sonar.Configs.GetAllAsync(Channel.Game, timeout.Token);
            var devices = await sonar.Devices.GetAllAsync(AudioDataFlow.Render, false, timeout.Token);
            if (_closing) return;
            _grid.EndEdit();
            _favorites = configs.Where(c => c.IsFavorite).Select(c => new Choice(c.Id, c.Name)).OrderBy(c => c.Name).ToList();
            _devices = devices.Select(d => new Choice(d.Id, d.Name)).OrderBy(d => d.Name).ToList();
            UpdateChoices();
            // Refresh the cache without saving uncommitted edits in the table.
            _settings.Favorites = _favorites.ToList();
            _settings.Devices = _devices.ToList();
            RebuildMenu();
            if (_startupError == null) _settings.Save();
            Report($"Избранных пресетов: {_favorites.Count}. Устройств: {_devices.Count}. Изменения сочетаний и устройств применяются кнопкой «Сохранить».");
        }
        catch (Exception ex) { if (!_closing) Report(ErrorText(ex), true); }
        finally { SetBusy(false); }
    }

    private void SaveSettings()
    {
        _grid.EndEdit();
        BindingContext?[_rows]?.EndCurrentEdit();
        var candidate = new AppSettings { Bindings = _rows.Select(b => new PresetBinding
            { PresetId = b.PresetId, DeviceId = b.DeviceId, Hotkey = b.Hotkey }).ToList(),
            Favorites = _favorites.ToList(), Devices = _devices.ToList(), CycleHotkey = _cycle.Text };
        var used = new HashSet<Hotkey>();
        var presetIds = new HashSet<string>();
        foreach (var row in candidate.Bindings)
        {
            if (string.IsNullOrEmpty(row.PresetId)) throw new ArgumentException("Выберите пресет для каждой строки или удалите пустую строку.");
            if (!presetIds.Add(row.PresetId)) throw new ArgumentException("Каждый пресет должен встречаться в таблице один раз.");
            var hotkey = Hotkey.Parse(row.Hotkey);
            if (hotkey is { } key && !used.Add(key)) throw new ArgumentException("Сочетания клавиш не должны повторяться.");
        }
        if (Hotkey.Parse(candidate.CycleHotkey) is { } cycle && !used.Add(cycle))
            throw new ArgumentException("Сочетание для перебора уже назначено пресету.");
        // Register first; a conflict leaves the previous saved configuration active.
        var previous = _settings;
        _settings = candidate;
        try
        {
            RegisterSettings(throwOnError: true);
            candidate.Save();
        }
        catch
        {
            _settings = previous;
            RegisterSettings();
            throw;
        }
        _startupError = null;
        RebuildMenu();
        Report("Настройки сохранены. Горячие клавиши уже работают.");
    }

    private void ClearHotkeys()
    {
        foreach (int id in _registered) UnregisterHotKey(Handle, id);
        _registered.Clear();
        _hotkeys.Clear();
    }

    private void RegisterSettings(bool throwOnError = false)
    {
        ClearHotkeys();
        var errors = new List<string>();
        void Register(int id, string text, PresetBinding? binding)
        {
            try
            {
                if (Hotkey.Parse(text) is not { } key) return;
                if (!RegisterHotKey(Handle, id, key.Modifiers | 0x4000, (uint)key.Key))
                    throw new InvalidOperationException($"Не удалось назначить {text}: сочетание занято или запрещено Windows (код {Marshal.GetLastWin32Error()}).");
                _registered.Add(id);
                if (binding != null) _hotkeys[id] = binding;
            }
            catch (Exception ex) { errors.Add(ex.Message); }
        }
        for (int i = 0; i < _settings.Bindings.Count; i++) Register(i + 1, _settings.Bindings[i].Hotkey, _settings.Bindings[i]);
        Register(CycleId, _settings.CycleHotkey, null);
        if (errors.Count == 0) return;
        string message = string.Join(Environment.NewLine, errors);
        if (throwOnError) throw new InvalidOperationException(message);
        Report(message, true);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        ClearHotkeys();
        base.OnHandleDestroyed(e);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (_tray.Visible && !_closing) RegisterSettings();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312)
        {
            if (!_closing)
            {
                int id = m.WParam.ToInt32();
                if (id == CycleId) _ = CycleAsync();
                else if (_hotkeys.TryGetValue(id, out var binding)) _ = ApplyAsync(binding);
            }
            return;
        }
        base.WndProc(ref m);
    }

    private async Task CycleAsync()
    {
        if (_busy || _closing) return;
        // Read the actual selection, including changes made directly in GG.
        await RunSonarAsync(async (sonar, ct) =>
        {
            var available = (await sonar.Configs.GetAllAsync(Channel.Game, ct)).Where(c => c.IsFavorite).ToDictionary(c => c.Id);
            var favorites = _settings.Bindings.Select(b => b.PresetId).Distinct()
                .Where(available.ContainsKey).Select(id => available[id]).ToList();
            if (favorites.Count == 0) throw new InvalidOperationException("Нет доступных избранных пресетов из настроек. Добавьте пресеты в избранное GG и в таблицу.");
            var current = await sonar.Configs.GetSelectedAsync(Channel.Game, ct);
            var next = favorites[(favorites.FindIndex(c => c.Id == current?.Id) + 1) % favorites.Count];
            var binding = _settings.Bindings.FirstOrDefault(b => b.PresetId == next.Id)
                ?? new PresetBinding { PresetId = next.Id };
            await ApplyCoreAsync(sonar, binding, ct);
        });
    }

    private async Task ApplyAsync(PresetBinding binding)
    {
        if (_busy || _closing) return;
        await RunSonarAsync((sonar, ct) => ApplyCoreAsync(sonar, binding, ct));
    }

    private async Task RunSonarAsync(Func<SonarClient, CancellationToken, Task> action)
    {
        SetBusy(true);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var sonar = new SonarClient();
            await action(sonar, timeout.Token);
        }
        catch (Exception ex) { if (!_closing) Report(ErrorText(ex), true); }
        finally { SetBusy(false); }
    }

    private async Task ApplyCoreAsync(SonarClient sonar, PresetBinding binding, CancellationToken ct)
    {
        var configs = await sonar.Configs.GetAllAsync(Channel.Game, ct);
        var target = configs.FirstOrDefault(c => c.Id == binding.PresetId)
            ?? throw new InvalidOperationException("Пресет отсутствует в Sonar Game. Обновите список и выберите его заново.");
        var undo = new Stack<Func<CancellationToken, Task>>();
        var previous = await sonar.Configs.GetSelectedAsync(Channel.Game, ct);
        string deviceName = "";
        try
        {
            if (!string.IsNullOrEmpty(binding.DeviceId))
            {
                var devices = await sonar.Devices.GetAllAsync(AudioDataFlow.Render, false, ct);
                var device = devices.FirstOrDefault(d => d.Id == binding.DeviceId)
                    ?? throw new InvalidOperationException("Устройство вывода недоступно. Подключите его или выберите другое в настройках.");
                deviceName = device.Name;
                var mode = await sonar.Mode.GetAsync(ct);
                if (mode == Mode.Classic)
                {
                    var routes = await sonar.Redirections.GetClassicRedirectionsAsync(ct);
                    foreach (Channel channel in new[] { Channel.Game, Channel.Chat, Channel.Media, Channel.Aux })
                    {
                        var old = routes.FirstOrDefault(r => r.Channel == channel)
                            ?? throw new InvalidOperationException($"Sonar не вернул устройство канала {channel}.");
                        undo.Push(token => sonar.Redirections.SetClassicDeviceAsync(channel, old.DeviceId, token));
                        await sonar.Redirections.SetClassicDeviceAsync(channel, device.Id, ct);
                    }
                    var confirmed = await sonar.Redirections.GetClassicRedirectionsAsync(ct);
                    if (new[] { Channel.Game, Channel.Chat, Channel.Media, Channel.Aux }.Any(c => !confirmed.Any(r => r.Channel == c && r.DeviceId == device.Id)))
                        throw new InvalidOperationException("Sonar не подтвердил смену устройства вывода.");
                }
                else
                {
                    var old = (await sonar.Redirections.GetStreamRedirectionsAsync(ct)).Personal
                        ?? throw new InvalidOperationException("Sonar не вернул устройство Personal.");
                    undo.Push(token => sonar.Redirections.SetMixDeviceAsync(Mix.Personal, old.DeviceId, token));
                    await sonar.Redirections.SetMixDeviceAsync(Mix.Personal, device.Id, ct);
                    if ((await sonar.Redirections.GetStreamRedirectionsAsync(ct)).Personal?.DeviceId != device.Id)
                        throw new InvalidOperationException("Sonar не подтвердил смену устройства Personal.");
                }
            }
            if (previous != null) undo.Push(token => sonar.Configs.SelectAsync(previous.Id, token));
            await sonar.Configs.SelectAsync(target.Id, ct);
            if ((await sonar.Configs.GetSelectedAsync(Channel.Game, ct))?.Id != target.Id)
                throw new InvalidOperationException("Sonar не подтвердил выбор пресета.");
        }
        catch (Exception ex)
        {
            if (undo.Count == 0) throw;
            bool restored = true;
            using var rollback = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            while (undo.TryPop(out var restore))
                try { await restore(rollback.Token); } catch { restored = false; }
            throw new InvalidOperationException(ErrorText(ex) + (restored
                ? " Предыдущие настройки восстановлены." : " Восстановить все настройки не удалось; проверьте выход и пресет в GG."), ex);
        }
        _selectedId = target.Id;
        RebuildMenu();
        Report(target.Name + (deviceName.Length == 0 ? "" : " → " + deviceName), notify: true);
    }

    private void RebuildMenu()
    {
        foreach (ToolStripItem item in _menu.Items.Cast<ToolStripItem>().ToArray()) item.Dispose();
        _menu.Items.Clear();
        _menu.Items.Add("Настройки", null, (_, _) => ShowWindow());
        var presets = new ToolStripMenuItem("Избранные пресеты") { Enabled = !_busy && _favorites.Count > 0 };
        foreach (var favorite in _settings.Bindings.Select(b => _favorites.FirstOrDefault(f => f.Id == b.PresetId)).OfType<Choice>().DistinctBy(f => f.Id))
        {
            var binding = _settings.Bindings.FirstOrDefault(b => b.PresetId == favorite.Id) ?? new PresetBinding { PresetId = favorite.Id };
            var item = new ToolStripMenuItem(favorite.Name) { Checked = favorite.Id == _selectedId };
            item.Click += async (_, _) => await ApplyAsync(binding);
            presets.DropDownItems.Add(item);
        }
        _menu.Items.Add(presets);
        _menu.Items.Add(new ToolStripMenuItem("Следующий пресет", null, async (_, _) => await CycleAsync()) { Enabled = !_busy });
        _menu.Items.Add(new ToolStripMenuItem("Обновить из Sonar", null, async (_, _) => await RefreshAsync()) { Enabled = !_busy });
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Выход", null, (_, _) => { _exit = true; Close(); });
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        if (_closing) return;
        _toolbar.Enabled = _grid.Enabled = _cycle.Enabled = !busy;
        RebuildMenu();
    }

    private static string ErrorText(Exception ex) => ex switch
    {
        SteelSeriesNotFoundException => "SteelSeries GG не найден или не запущен. Запустите GG.",
        SonarNotRunningException => "Sonar недоступен. Включите Sonar в SteelSeries GG.",
        DiscoveryException => "Не удалось обнаружить GG/Sonar. Проверьте, что GG запущен и Sonar включён. " + ex.Message,
        SonarWrongModeException => "Операция недоступна в текущем режиме Sonar. " + ex.Message,
        OperationCanceledException => "Sonar не ответил вовремя. Проверьте GG и повторите попытку.",
        HttpRequestException => "Нет соединения с Sonar. Проверьте, что GG запущен. " + ex.Message,
        _ => ex.Message
    };

    private void Report(string message, bool error = false, bool notify = false)
    {
        if (_closing || IsDisposed) return;
        _output.Text = message;
        if (error || notify) _tray.ShowBalloonTip(4000, error ? "SonarHotkeys — ошибка" : "Sonar — выбран пресет",
            message.Length > 250 ? message[..247] + "…" : message, error ? ToolTipIcon.Error : ToolTipIcon.Info);
    }

    private void ShowWindow()
    {
        if (_closing) return;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && !_exit) { e.Cancel = true; Hide(); }
        base.OnFormClosing(e);
        if (!e.Cancel) { _closing = true; _lifetime.Cancel(); }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _tray.Visible = false;
        _tray.Dispose();
        _menu.Dispose();
        base.OnFormClosed(e);
    }
}

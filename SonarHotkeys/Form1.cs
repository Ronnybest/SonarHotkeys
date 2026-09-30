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
    private readonly ComboBox _languagePicker = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 155 };
    private readonly TextBox _output = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly FlowLayoutPanel _toolbar = new() { Dock = DockStyle.Fill, AutoSize = true };
    private readonly NotifyIcon _tray = new();
    private readonly ContextMenuStrip _menu = new();
    private readonly Dictionary<int, PresetBinding> _hotkeys = [];
    private readonly List<int> _registered = [];
    private readonly CancellationTokenSource _lifetime = new();
    private readonly string _settingsPath;
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

    public Form1() : this(AppSettings.FilePath) { }

    public Form1(string settingsPath)
    {
        _settingsPath = Path.GetFullPath(settingsPath);
        try { _settings = AppSettings.Load(_settingsPath); }
        catch (Exception ex)
        {
            _settings = AppSettings.Defaults();
            _startupError = T("Не удалось прочитать настройки. Создайте привязки заново и сохраните их. ") + ex.Message;
        }
        InitializeComponent();
        Localized(this, "SonarHotkeys — настройки");
        ClientSize = new Size(1020, 570);
        MinimumSize = new Size(850, 450);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 1, RowCount = 5 };
        layout.RowStyles.Add(new(SizeType.Absolute, 90));
        layout.RowStyles.Add(new(SizeType.Absolute, 45));
        layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.Absolute, 45));
        layout.RowStyles.Add(new(SizeType.Absolute, 100));
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        header.ColumnStyles.Add(new(SizeType.Percent, 100));
        header.ColumnStyles.Add(new(SizeType.Absolute, 175));
        header.Controls.Add(Localized(new Label { Dock = DockStyle.Fill },
            "1. Включите Sonar в GG и добавьте нужные пресеты Game в избранное. 2. Обновите список и добавьте строки.\n" +
            "3. Выберите свои пресеты и устройства, нажмите сочетание в ячейке и сохраните настройки.\n" +
            "Устройство: Game, Chat, Media, Aux; в Streamer — Personal. Закрытие окна скрывает его в трей."), 0, 0);
        var languagePanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown };
        languagePanel.Controls.Add(new Label { Text = "Язык / Language", AutoSize = true });
        // Items instead of DataSource: SelectedItem applies before the control gets a BindingContext.
        _languagePicker.DisplayMember = "Name";
        _languagePicker.Items.AddRange([new Choice("ru", "Русский"), new Choice("en", "English")]);
        _languagePicker.SelectedItem = _languagePicker.Items.Cast<Choice>().First(c => c.Id == _settings.Language);
        languagePanel.Controls.Add(_languagePicker);
        header.Controls.Add(languagePanel, 1, 0);
        layout.Controls.Add(header, 0, 0);
        AddButton("Обновить из Sonar", async () => await RefreshAsync());
        AddButton("Добавить", () => { AddBinding(); return Task.CompletedTask; });
        AddButton("Удалить", () => { if (_grid.CurrentRow?.DataBoundItem is PresetBinding row) _rows.Remove(row); return Task.CompletedTask; });
        AddButton("Сохранить", () => { SaveSettings(); return Task.CompletedTask; });
        AddButton("Применить строку", async () =>
        {
            _grid.EndEdit();
            if (_grid.CurrentRow?.DataBoundItem is PresetBinding row)
                await ApplyAsync(new() { PresetId = row.PresetId, DeviceId = row.DeviceId });
        });
        layout.Controls.Add(_toolbar, 0, 1);
        _grid.Columns.Add(new DataGridViewComboBoxColumn { Name = "Preset", HeaderText = T("Избранный пресет Game"), Tag = "Избранный пресет Game", DataPropertyName = "PresetId",
            DisplayMember = "Name", ValueMember = "Id", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 40 });
        _grid.Columns.Add(new DataGridViewComboBoxColumn { Name = "Device", HeaderText = T("Устройство вывода"), Tag = "Устройство вывода", DataPropertyName = "DeviceId",
            DisplayMember = "Name", ValueMember = "Id", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 35 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Hotkey", HeaderText = T("Сочетание (Delete — очистить)"), Tag = "Сочетание (Delete — очистить)", DataPropertyName = "Hotkey",
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
        _grid.DataError += (_, e) => { e.ThrowException = false; Report(T("Некорректное значение в таблице. Выберите пресет или устройство из списка."), true); };
        layout.Controls.Add(_grid, 0, 2);
        var cyclePanel = new FlowLayoutPanel { Dock = DockStyle.Fill };
        cyclePanel.Controls.Add(Localized(new Label { AutoSize = true, Padding = new Padding(0, 8, 0, 0) }, "Перебор настроенных пресетов:"));
        _cycle.KeyDown += Hotkey.Capture;
        _cycle.Enter += (_, _) => ClearHotkeys();
        _cycle.Leave += (_, _) => { if (!_closing) RegisterSettings(); };
        cyclePanel.Controls.Add(_cycle);
        var next = Localized(new Button { AutoSize = true }, "Следующий");
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
        _languagePicker.SelectedIndexChanged += (_, _) => ChangeLanguage();
        RebuildMenu();
        if (_settings.Bindings.Count == 0) Report(T("Добро пожаловать! Начните с кнопки «Обновить из Sonar», затем добавьте свои пресеты. Горячие клавиши ещё не назначены."));
    }

    private string T(string key, params object[] arguments) => TextCatalog.Get(key, _settings.Language, arguments);

    // The Tag keeps the catalog key, so the text can be translated again after a language switch.
    private TControl Localized<TControl>(TControl control, string key) where TControl : Control
    {
        control.Tag = key;
        control.Text = T(key);
        return control;
    }

    private void ApplyLanguage()
    {
        void Translate(Control control)
        {
            if (control.Tag is string key) control.Text = T(key);
            foreach (Control child in control.Controls) Translate(child);
        }
        Translate(this);
        foreach (DataGridViewColumn column in _grid.Columns)
            if (column.Tag is string key) column.HeaderText = T(key);
        UpdateChoices();
        RebuildMenu();
    }

    private void ChangeLanguage()
    {
        if (_languagePicker.SelectedItem is not Choice { Id: var language } || language == _settings.Language) return;
        _grid.EndEdit();
        _settings.Language = language;
        ApplyLanguage();
        // Save only the existing configuration; keep unfinished table edits in the editor.
        if (_startupError != null)
        {
            Report(T("Язык изменён. Сохраните настройки, чтобы запомнить выбор языка."));
            return;
        }
        try
        {
            _settings.Save(_settingsPath);
            Report(T("Язык изменён. Выбор языка сохранён."));
        }
        catch (Exception ex) { Report(T("Не удалось сохранить язык. ") + ex.Message, true); }
    }

    private void AddBinding()
    {
        var next = _favorites.FirstOrDefault(f => !_rows.Any(b => b.PresetId == f.Id));
        if (next == null)
        {
            Report(_favorites.Count == 0
                ? T("Добавьте пресеты Game в избранное в GG и нажмите «Обновить из Sonar».")
                : T("Все избранные пресеты уже добавлены. Для других пресетов обновите избранное в GG."));
            return;
        }
        _rows.Add(new() { PresetId = next.Id });
        _grid.CurrentCell = _grid.Rows[_rows.Count - 1].Cells["Device"];
    }

    private void AddButton(string key, Func<Task> action)
    {
        var button = Localized(new Button { AutoSize = true }, key);
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
        var presets = new List<Choice> { new("", T("— выберите пресет —")) };
        presets.AddRange(_favorites);
        foreach (string id in _rows.Select(b => b.PresetId).Where(id => id.Length > 0).Distinct())
            if (!presets.Any(c => c.Id == id)) presets.Add(new(id, T("Недоступный пресет: ") + id));
        var devices = new List<Choice> { new("", T("Не менять устройство")) };
        devices.AddRange(_devices);
        foreach (string id in _rows.Select(b => b.DeviceId).Where(id => id.Length > 0).Distinct())
            if (!devices.Any(c => c.Id == id)) devices.Add(new(id, T("Недоступное устройство: ") + id));
        ((DataGridViewComboBoxColumn)_grid.Columns["Preset"]!).DataSource = presets;
        ((DataGridViewComboBoxColumn)_grid.Columns["Device"]!).DataSource = devices;
    }

    private static bool StartHidden =>
#if DEBUG
        false; // Debug runs always show the window.
#else
        Environment.GetCommandLineArgs().Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
#endif

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        RegisterSettings();
        // A fresh installation always shows setup, even if launched with --tray.
        if (_settings.Bindings.Count > 0 && _startupError == null && StartHidden) Hide();
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
            if (_startupError == null) _settings.Save(_settingsPath);
            Report(_favorites.Count == 0
                ? T("В Sonar Game нет избранных пресетов. Отметьте нужные пресеты избранными в GG и обновите список.")
                : _settings.Bindings.Count == 0
                    ? T("Найдено пресетов: {0}, устройств: {1}. Нажмите «Добавить», выберите устройство и сочетание, затем «Сохранить». Для перебора задайте отдельное сочетание ниже таблицы.", _favorites.Count, _devices.Count)
                    : T("Избранных пресетов: {0}. Устройств: {1}. Изменения сочетаний и устройств применяются кнопкой «Сохранить».", _favorites.Count, _devices.Count));
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
            Favorites = _favorites.ToList(), Devices = _devices.ToList(), CycleHotkey = _cycle.Text, Language = _settings.Language };
        var used = new HashSet<Hotkey>();
        var presetIds = new HashSet<string>();
        foreach (var row in candidate.Bindings)
        {
            if (string.IsNullOrEmpty(row.PresetId)) throw new ArgumentException(T("Выберите пресет для каждой строки или удалите пустую строку."));
            if (!presetIds.Add(row.PresetId)) throw new ArgumentException(T("Каждый пресет должен встречаться в таблице один раз."));
            var hotkey = Hotkey.Parse(row.Hotkey, _settings.Language);
            if (hotkey is { } key && !used.Add(key)) throw new ArgumentException(T("Сочетания клавиш не должны повторяться."));
        }
        if (Hotkey.Parse(candidate.CycleHotkey, _settings.Language) is { } cycle && !used.Add(cycle))
            throw new ArgumentException(T("Сочетание для перебора уже назначено пресету."));
        // Register first; a conflict leaves the previous saved configuration active.
        var previous = _settings;
        _settings = candidate;
        try
        {
            RegisterSettings(throwOnError: true);
            candidate.Save(_settingsPath);
        }
        catch
        {
            _settings = previous;
            RegisterSettings();
            throw;
        }
        _startupError = null;
        RebuildMenu();
        Report(T("Настройки сохранены. Горячие клавиши уже работают."));
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
                if (Hotkey.Parse(text, _settings.Language) is not { } key) return;
                if (!RegisterHotKey(Handle, id, key.Modifiers | 0x4000, (uint)key.Key))
                    throw new InvalidOperationException(T("Не удалось назначить {0}: сочетание занято или запрещено Windows (код {1}).", text, Marshal.GetLastWin32Error()));
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
            if (favorites.Count == 0) throw new InvalidOperationException(T("Нет доступных избранных пресетов из настроек. Добавьте пресеты в избранное GG и в таблицу."));
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
            ?? throw new InvalidOperationException(T("Пресет отсутствует в Sonar Game. Обновите список и выберите его заново."));
        var undo = new Stack<Func<CancellationToken, Task>>();
        var previous = await sonar.Configs.GetSelectedAsync(Channel.Game, ct);
        string deviceName = "";
        try
        {
            if (!string.IsNullOrEmpty(binding.DeviceId))
            {
                var devices = await sonar.Devices.GetAllAsync(AudioDataFlow.Render, false, ct);
                var device = devices.FirstOrDefault(d => d.Id == binding.DeviceId)
                    ?? throw new InvalidOperationException(T("Устройство вывода недоступно. Подключите его или выберите другое в настройках."));
                deviceName = device.Name;
                var mode = await sonar.Mode.GetAsync(ct);
                if (mode == Mode.Classic)
                {
                    var routes = await sonar.Redirections.GetClassicRedirectionsAsync(ct);
                    foreach (Channel channel in new[] { Channel.Game, Channel.Chat, Channel.Media, Channel.Aux })
                    {
                        var old = routes.FirstOrDefault(r => r.Channel == channel)
                            ?? throw new InvalidOperationException(T("Sonar не вернул устройство канала {0}.", channel));
                        undo.Push(token => sonar.Redirections.SetClassicDeviceAsync(channel, old.DeviceId, token));
                        await sonar.Redirections.SetClassicDeviceAsync(channel, device.Id, ct);
                    }
                    var confirmed = await sonar.Redirections.GetClassicRedirectionsAsync(ct);
                    if (new[] { Channel.Game, Channel.Chat, Channel.Media, Channel.Aux }.Any(c => !confirmed.Any(r => r.Channel == c && r.DeviceId == device.Id)))
                        throw new InvalidOperationException(T("Sonar не подтвердил смену устройства вывода."));
                }
                else
                {
                    var old = (await sonar.Redirections.GetStreamRedirectionsAsync(ct)).Personal
                        ?? throw new InvalidOperationException(T("Sonar не вернул устройство Personal."));
                    undo.Push(token => sonar.Redirections.SetMixDeviceAsync(Mix.Personal, old.DeviceId, token));
                    await sonar.Redirections.SetMixDeviceAsync(Mix.Personal, device.Id, ct);
                    if ((await sonar.Redirections.GetStreamRedirectionsAsync(ct)).Personal?.DeviceId != device.Id)
                        throw new InvalidOperationException(T("Sonar не подтвердил смену устройства Personal."));
                }
            }
            if (previous != null) undo.Push(token => sonar.Configs.SelectAsync(previous.Id, token));
            await sonar.Configs.SelectAsync(target.Id, ct);
            if ((await sonar.Configs.GetSelectedAsync(Channel.Game, ct))?.Id != target.Id)
                throw new InvalidOperationException(T("Sonar не подтвердил выбор пресета."));
        }
        catch (Exception ex)
        {
            if (undo.Count == 0) throw;
            bool restored = true;
            using var rollback = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            while (undo.TryPop(out var restore))
                try { await restore(rollback.Token); } catch { restored = false; }
            throw new InvalidOperationException(ErrorText(ex) + (restored
                ? T(" Предыдущие настройки восстановлены.") : T(" Восстановить все настройки не удалось; проверьте выход и пресет в GG.")), ex);
        }
        _selectedId = target.Id;
        RebuildMenu();
        Report(target.Name + (deviceName.Length == 0 ? "" : " → " + deviceName), notify: true);
    }

    private void RebuildMenu()
    {
        foreach (ToolStripItem item in _menu.Items.Cast<ToolStripItem>().ToArray()) item.Dispose();
        _menu.Items.Clear();
        _menu.Items.Add(T("Настройки"), null, (_, _) => ShowWindow());
        var presets = new ToolStripMenuItem(T("Избранные пресеты"));
        foreach (var favorite in _settings.Bindings.Select(b => _favorites.FirstOrDefault(f => f.Id == b.PresetId)).OfType<Choice>().DistinctBy(f => f.Id))
        {
            var binding = _settings.Bindings.FirstOrDefault(b => b.PresetId == favorite.Id) ?? new PresetBinding { PresetId = favorite.Id };
            var item = new ToolStripMenuItem(favorite.Name) { Checked = favorite.Id == _selectedId };
            item.Click += async (_, _) => await ApplyAsync(binding);
            presets.DropDownItems.Add(item);
        }
        presets.Enabled = !_busy && presets.DropDownItems.Count > 0;
        _menu.Items.Add(presets);
        _menu.Items.Add(new ToolStripMenuItem(T("Следующий пресет"), null, async (_, _) => await CycleAsync()) { Enabled = !_busy && presets.DropDownItems.Count > 0 });
        _menu.Items.Add(new ToolStripMenuItem(T("Обновить из Sonar"), null, async (_, _) => await RefreshAsync()) { Enabled = !_busy });
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(T("Выход"), null, (_, _) => { _exit = true; Close(); });
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        if (_closing) return;
        _toolbar.Enabled = _grid.Enabled = _cycle.Enabled = !busy;
        _languagePicker.Enabled = !busy;
        RebuildMenu();
    }

    private string ErrorText(Exception ex) => ex switch
    {
        SteelSeriesNotFoundException => T("SteelSeries GG не найден или не запущен. Запустите GG."),
        SonarNotRunningException => T("Sonar недоступен. Включите Sonar в SteelSeries GG."),
        DiscoveryException => T("Не удалось обнаружить GG/Sonar. Проверьте, что GG запущен и Sonar включён. ") + ex.Message,
        SonarWrongModeException => T("Операция недоступна в текущем режиме Sonar. ") + ex.Message,
        OperationCanceledException => T("Sonar не ответил вовремя. Проверьте GG и повторите попытку."),
        HttpRequestException => T("Нет соединения с Sonar. Проверьте, что GG запущен. ") + ex.Message,
        _ => ex.Message
    };

    private void Report(string message, bool error = false, bool notify = false)
    {
        if (_closing || IsDisposed) return;
        _output.Text = message;
        if (error || notify) _tray.ShowBalloonTip(4000, error ? T("SonarHotkeys — ошибка") : T("Sonar — выбран пресет"),
            message.Length > 250 ? message[..247] + "…" : message, error ? ToolTipIcon.Error : ToolTipIcon.Info);
    }

    internal void ShowWindow()
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

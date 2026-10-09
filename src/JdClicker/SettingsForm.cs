namespace JdClicker;

/// <summary>Поле, которое запоминает нажатое сочетание клавиш. Backspace/Delete очищают.</summary>
internal sealed class KeyBox : TextBox
{
    public bool AllowModifiers { get; init; } = true;
    public event Action? KeyChanged;

    public KeyBox()
    {
        ReadOnly = true;
        BackColor = SystemColors.Window;
        TextAlign = HorizontalAlignment.Center;
        ShortcutsEnabled = false;
        Cursor = Cursors.Hand;
    }

    protected override bool IsInputKey(Keys keyData) => true;

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (Focused) { HandleKey(keyData); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;
        HandleKey(e.KeyData);
    }

    void HandleKey(Keys keyData)
    {
        var code = keyData & Keys.KeyCode;
        if (KeySpec.IsModifierKey(code)) return;
        if (code is Keys.Back or Keys.Delete && (keyData & Keys.Modifiers) == 0)
        {
            Text = "";
        }
        else
        {
            var spec = KeySpec.FromKeyData(keyData);
            if (!AllowModifiers) spec = spec with { Ctrl = false, Alt = false, Shift = false };
            Text = spec.ToString();
        }
        KeyChanged?.Invoke();
    }
}

/// <summary>Окно настроек. Любое изменение сразу применяется и сохраняется в settings.json.</summary>
internal sealed class SettingsForm : Form
{
    readonly TrayApp _app;
    readonly CheckBox[] _slotOn = new CheckBox[Settings.SlotCount];
    readonly KeyBox[] _slotKey = new KeyBox[Settings.SlotCount];
    readonly NumericUpDown[] _slotMs = new NumericUpDown[Settings.SlotCount];
    readonly NumericUpDown _hold, _gap;
    readonly KeyBox _hotkey;
    readonly CheckBox _sound, _overlay, _pauseChat, _skipMods;
    readonly Label _chatInfo, _gameInfo, _status, _hotkeyError;
    readonly System.Windows.Forms.Timer _statusTimer;
    bool _loading;

    public SettingsForm(TrayApp app)
    {
        _app = app;
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9f);
        Text = AppInfo.Name;
        Icon = AppInfo.LoadAppIcon();
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(470, 640);

        int y = 10;

        // ---- Зелья ----
        var gKeys = Group("Зелья: клавиша и как часто её жать", ref y, 50 + Settings.SlotCount * 30);
        for (int i = 0; i < Settings.SlotCount; i++)
        {
            int ry = 24 + i * 30;
            _slotOn[i] = new CheckBox { Location = new Point(14, ry + 3), AutoSize = true };
            var lk = new Label { Text = "Клавиша", Location = new Point(36, ry + 4), AutoSize = true };
            _slotKey[i] = new KeyBox { Location = new Point(100, ry), Width = 80 };
            var li = new Label { Text = "жать каждые", Location = new Point(192, ry + 4), AutoSize = true };
            _slotMs[i] = new NumericUpDown { Location = new Point(280, ry), Width = 80, Minimum = 20, Maximum = 600000, Increment = 50 };
            var lm = new Label { Text = "мс", Location = new Point(366, ry + 4), AutoSize = true };
            gKeys.Controls.AddRange(new Control[] { _slotOn[i], lk, _slotKey[i], li, _slotMs[i], lm });
            _slotOn[i].CheckedChanged += (_, _) => Changed();
            _slotKey[i].KeyChanged += Changed;
            _slotMs[i].ValueChanged += (_, _) => Changed();
        }
        gKeys.Controls.Add(new Label
        {
            Text = "Кликните в поле и нажмите клавишу. Backspace — очистить. 1000 мс = 1 сек.",
            Location = new Point(14, 24 + Settings.SlotCount * 30), AutoSize = true, ForeColor = SystemColors.GrayText,
        });

        // ---- Нажатие ----
        var gPress = Group("Нажатие", ref y, 84);
        gPress.Controls.Add(new Label { Text = "Длительность нажатия, мс", Location = new Point(14, 28), AutoSize = true });
        _hold = new NumericUpDown { Location = new Point(280, 24), Width = 80, Minimum = 5, Maximum = 200 };
        gPress.Controls.Add(new Label { Text = "Пауза между разными клавишами, мс", Location = new Point(14, 56), AutoSize = true });
        _gap = new NumericUpDown { Location = new Point(280, 52), Width = 80, Minimum = 0, Maximum = 1000, Increment = 10 };
        gPress.Controls.AddRange(new Control[] { _hold, _gap });
        _hold.ValueChanged += (_, _) => Changed();
        _gap.ValueChanged += (_, _) => Changed();

        // ---- Управление ----
        var gCtl = Group("Управление", ref y, 108);
        gCtl.Controls.Add(new Label { Text = "Хоткей вкл/выкл", Location = new Point(14, 28), AutoSize = true });
        _hotkey = new KeyBox { Location = new Point(140, 24), Width = 120 };
        _hotkeyError = new Label { Location = new Point(270, 28), AutoSize = true, ForeColor = Color.Firebrick };
        _sound = new CheckBox { Text = "Звук при включении и выключении", Location = new Point(14, 54), AutoSize = true };
        _overlay = new CheckBox { Text = "Индикатор поверх игры (можно перетаскивать)", Location = new Point(14, 78), AutoSize = true };
        gCtl.Controls.AddRange(new Control[] { _hotkey, _hotkeyError, _sound, _overlay });
        _hotkey.KeyChanged += Changed;
        // Пока поле хоткея в фокусе, сам хоткей снят, иначе его не назначить.
        _hotkey.Enter += (_, _) => _app.SuspendHotkey(true);
        _hotkey.Leave += (_, _) => _app.SuspendHotkey(false);
        _sound.CheckedChanged += (_, _) => Changed();
        _overlay.CheckedChanged += (_, _) => Changed();

        // ---- Защита ----
        var gSafe = Group("Защита", ref y, 160);
        _pauseChat = new CheckBox { Text = "Не жать, пока открыт чат (Enter открывает/закрывает)", Location = new Point(14, 24), AutoSize = true };
        var pick = new Button { Text = "Указать поле чата…", Location = new Point(32, 50), Width = 150 };
        var clear = new Button { Text = "Сбросить", Location = new Point(188, 50), Width = 80 };
        _chatInfo = new Label { Location = new Point(276, 55), AutoSize = true, ForeColor = SystemColors.GrayText };
        _skipMods = new CheckBox { Text = "Не жать, пока зажаты Shift / Ctrl / Alt", Location = new Point(14, 84), AutoSize = true };
        _gameInfo = new Label { Location = new Point(14, 114), AutoSize = true };
        var forget = new Button { Text = "Забыть", Location = new Point(350, 109), Width = 80 };
        gSafe.Controls.AddRange(new Control[] { _pauseChat, pick, clear, _chatInfo, _skipMods, _gameInfo, forget });
        _pauseChat.CheckedChanged += (_, _) => Changed();
        _skipMods.CheckedChanged += (_, _) => Changed();
        pick.Click += (_, _) => _app.PickChatArea();
        clear.Click += (_, _) => { var s = State.Settings.Clone(); s.ChatArea = null; _app.ApplySettings(s); LoadFrom(s); };
        forget.Click += (_, _) => _app.ForgetGame();

        // ---- Статус ----
        _status = new Label { Location = new Point(12, y + 4), Size = new Size(446, 56) };
        Controls.Add(_status);
        ClientSize = new Size(470, y + 64);

        ResumeLayout(false);
        PerformLayout();

        _statusTimer = new System.Windows.Forms.Timer { Interval = 250 };
        _statusTimer.Tick += (_, _) => UpdateStatus();
        LoadFrom(State.Settings);
    }

    GroupBox Group(string title, ref int y, int height)
    {
        var g = new GroupBox { Text = title, Location = new Point(10, y), Size = new Size(450, height) };
        Controls.Add(g);
        y += height + 8;
        return g;
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        _statusTimer.Enabled = Visible;
        if (Visible) { LoadFrom(State.Settings); UpdateStatus(); }
        else _app.SuspendHotkey(false);
    }

    // Ушли из окна (например, Alt+Tab в игру) с фокусом в поле хоткея — хоткей должен снова работать.
    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        _app.SuspendHotkey(false);
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (_hotkey.Focused) _app.SuspendHotkey(true);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        base.OnFormClosing(e);
    }

    /// <summary>Показывает текущие настройки (в том числе после правки settings.json вручную).</summary>
    public void LoadFrom(Settings s)
    {
        _loading = true;
        try
        {
            for (int i = 0; i < Settings.SlotCount; i++)
            {
                _slotOn[i].Checked = s.Keys[i].Enabled;
                _slotKey[i].Text = KeySpec.TryParse(s.Keys[i].Key, out var k) ? k.ToString() : s.Keys[i].Key;
                _slotMs[i].Value = Math.Clamp(s.Keys[i].IntervalMs, (int)_slotMs[i].Minimum, (int)_slotMs[i].Maximum);
            }
            _hold.Value = s.HoldMs;
            _gap.Value = s.GapMs;
            _hotkey.Text = KeySpec.TryParse(s.ToggleHotkey, out var hk) ? hk.ToString() : s.ToggleHotkey;
            _sound.Checked = s.Sound;
            _overlay.Checked = s.Overlay;
            _pauseChat.Checked = s.PauseInChat;
            _skipMods.Checked = s.SkipWhenModifiersHeld;
            RefreshInfo(s);
        }
        finally { _loading = false; }
    }

    void RefreshInfo(Settings s)
    {
        _chatInfo.Text = s.ChatArea is { } a ? $"задано: {a.W}×{a.H} в ({a.X}, {a.Y})" : "не задано";
        _gameInfo.Text = "Окно игры: " + (string.IsNullOrEmpty(s.GameProcess)
            ? "elementclient.exe (Jade Dynasty)"
            : s.GameProcess + ".exe");
        _hotkeyError.Text = _app.HotkeyError ?? "";
        for (int i = 0; i < Settings.SlotCount; i++)
        {
            bool bad = s.Keys[i].Enabled && s.Keys[i].Key.Length > 0 && s.Parsed[i] is null;
            _slotKey[i].BackColor = bad ? Color.MistyRose : SystemColors.Window;
        }
    }

    void Changed()
    {
        if (_loading) return;
        var s = State.Settings.Clone();
        for (int i = 0; i < Settings.SlotCount; i++)
        {
            s.Keys[i].Enabled = _slotOn[i].Checked;
            s.Keys[i].Key = _slotKey[i].Text;
            s.Keys[i].IntervalMs = (int)_slotMs[i].Value;
        }
        s.HoldMs = (int)_hold.Value;
        s.GapMs = (int)_gap.Value;
        s.ToggleHotkey = _hotkey.Text;
        s.Sound = _sound.Checked;
        s.Overlay = _overlay.Checked;
        s.PauseInChat = _pauseChat.Checked;
        s.SkipWhenModifiersHeld = _skipMods.Checked;
        _app.ApplySettings(s.Normalize());
        RefreshInfo(State.Settings);
    }

    void UpdateStatus()
    {
        var s = State.Settings;
        string state = State.Enabled ? "ВКЛ" : "ВЫКЛ";
        string game = State.TargetPid == 0 ? "игра не найдена"
            : State.GameForeground() ? "игра активна" : "игра не на переднем плане";
        string chat = State.ChatOpen ? (s.PauseInChat ? "чат открыт — пауза" : "чат открыт") : "чат закрыт";
        string admin = _app.NeedAdmin ? "\nИгра запущена от администратора — перезапустите программу от администратора (меню в трее)." : "";
        _status.ForeColor = _app.NeedAdmin ? Color.Firebrick : SystemColors.ControlText;
        _status.Text = $"Состояние: {state} · {game} · {chat}\n" +
                       $"Нажатий в секунду: {State.PressesPerSecond} · всего дошло до игры: {Interlocked.Read(ref State.Sent)} · отсечено защитой: {Interlocked.Read(ref State.Blocked)}{admin}";
        _hotkeyError.Text = _app.HotkeyError ?? "";
    }
}

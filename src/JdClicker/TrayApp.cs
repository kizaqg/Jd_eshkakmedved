using System.ComponentModel;
using System.Diagnostics;

namespace JdClicker;

/// <summary>Главный объект: трей, хоткей, индикатор, привязка к окну игры, сохранение настроек.</summary>
internal sealed class TrayApp : ApplicationContext
{
    const int HotkeyId = 1;

    readonly NotifyIcon _tray;
    readonly ContextMenuStrip _menu;
    readonly ToolStripMenuItem _miToggle, _miAdmin;
    readonly OverlayForm _overlay;
    readonly HotkeyWindow _hotkeyWindow = new();
    readonly Clicker _clicker = new();
    readonly InputHooks _hooks = new();
    readonly System.Windows.Forms.Timer _tick, _saveTimer, _reloadTimer;
    readonly FileSystemWatcher? _watcher;
    readonly Icon _iconOn, _iconOff, _iconPause, _iconWarn;
    SettingsForm? _settingsForm;

    string _registeredHotkey = "";
    bool _hotkeySuspended;
    string? _lastSavedJson;
    DateTime _ignoreWatcherUntil;
    int _tickCount;
    long _rateSent;
    long _rateAt = Environment.TickCount64;

    public string? HotkeyError { get; private set; }
    public bool NeedAdmin { get; private set; }

    public TrayApp()
    {
        var loaded = SettingsStore.Load(out var loadError);
        if (loaded is null)
        {
            MessageBox.Show(
                $"Не удалось прочитать settings.json:\n{loadError}\n\nБудут использованы настройки по умолчанию. " +
                "Файл не перезапишется, пока вы не поменяете что-нибудь в окне настроек.",
                AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            loaded = Settings.CreateDefault();
        }
        else
        {
            _lastSavedJson = SettingsStore.Serialize(loaded);
        }
        State.Settings = loaded;

        using (var pig = AppInfo.LoadPig())
        {
            _iconOn = MakeIcon(pig, Color.LimeGreen);
            _iconOff = MakeIcon(pig, Color.Gray);
            _iconPause = MakeIcon(pig, Color.Gold);
            _iconWarn = MakeIcon(pig, Color.Red);
        }

        _menu = new ContextMenuStrip();
        _miToggle = new ToolStripMenuItem("Включить", null, (_, _) => Toggle(fromHotkey: false));
        _miAdmin = new ToolStripMenuItem("Перезапустить от администратора", null, (_, _) => RestartAsAdmin())
        {
            Visible = !Environment.IsPrivilegedProcess,
        };
        _menu.Items.AddRange(new ToolStripItem[]
        {
            _miToggle,
            new ToolStripMenuItem("Настройки…", null, (_, _) => ShowSettings()),
            new ToolStripMenuItem("Указать поле чата…", null, (_, _) => PickChatArea()),
            new ToolStripSeparator(),
            _miAdmin,
            new ToolStripMenuItem("Открыть папку с настройками", null, (_, _) => OpenSettingsFolder()),
            new ToolStripSeparator(),
            new ToolStripMenuItem("Выход", null, (_, _) => ExitThread()),
        });

        _tray = new NotifyIcon { Icon = _iconOff, Text = "JD — ВЫКЛ", ContextMenuStrip = _menu, Visible = true };
        _tray.DoubleClick += (_, _) => ShowSettings();

        _overlay = new OverlayForm(_menu);
        _overlay.DragFinished += SaveOverlayOffset;
        _ = _overlay.Handle; // нужен для SynchronizingObject ниже

        _hotkeyWindow.Pressed += () => Toggle(fromHotkey: true);
        RegisterHotkey();

        _hooks.Start();
        _clicker.Start();

        _saveTimer = new System.Windows.Forms.Timer { Interval = 400 };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveNow(); };
        _reloadTimer = new System.Windows.Forms.Timer { Interval = 300 };
        _reloadTimer.Tick += (_, _) => { _reloadTimer.Stop(); ReloadFromDisk(); };

        try
        {
            _watcher = new FileSystemWatcher(Path.GetDirectoryName(SettingsStore.FilePath)!, Path.GetFileName(SettingsStore.FilePath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                SynchronizingObject = _overlay,
            };
            _watcher.Changed += (_, _) => RestartReload();
            _watcher.Created += (_, _) => RestartReload();
            _watcher.Renamed += (_, _) => RestartReload();
            _watcher.EnableRaisingEvents = true;
        }
        catch { _watcher = null; }

        _tick = new System.Windows.Forms.Timer { Interval = 100 };
        _tick.Tick += (_, _) => OnTick();
        _tick.Start();

        Log.Write($"Запуск {AppInfo.Name} {AppInfo.Version}, от админа: {Environment.IsPrivilegedProcess}, хуки: {_hooks.Installed}");
        if (!TryFindGame()) Log.Write("При запуске окно игры не найдено");
        if (!_hooks.Installed)
            Balloon("Не удалось установить перехват клавиатуры — защита чата не работает.", ToolTipIcon.Warning);
        else
            Balloon($"Запущен. Зайдите в игру и нажмите {HotkeyText()} — включить/выключить.");
        UpdateUi();
    }

    // ---------------- Включение ----------------

    void Toggle(bool fromHotkey)
    {
        if (State.Enabled) { SetEnabled(false); return; }

        var s = State.Settings;
        var fg = Native.GetForegroundWindow();
        int fgPid = State.ForegroundPid();

        if (fromHotkey && fg != IntPtr.Zero && fgPid != 0 && fgPid != State.OwnPid)
        {
            // Хоткей нажат в каком-то окне: привязываемся к нему, если это игра
            // (или если игра ещё ни разу не была выбрана — тогда запоминаем это окно).
            var root = Native.GetAncestor(fg, Native.GA_ROOT);
            string name = Native.ProcessName(fgPid);
            string title = Native.WindowTitle(root);
            Log.Write($"Хоткей в окне «{title}» ({name}.exe, pid {fgPid})");
            if (!IsGameWindow(name, title, s) && (s.GameProcess.Length > 0 || FindGameWindow(s) is not null))
            {
                Sounds.PlayError();
                Balloon($"Активное окно — «{title}» ({name}.exe), а игра — {s.GameProcess}.exe. " +
                        "Перейдите в игру или нажмите «Забыть» в настройках.", ToolTipIcon.Warning);
                return;
            }
            BindTarget(root, fgPid, name);
        }
        else if (!TargetAlive() && !TryFindGame())
        {
            Sounds.PlayError();
            Log.Write("Включение: окно игры не найдено");
            Balloon($"Окно игры не найдено. Перейдите в игру и нажмите {HotkeyText()}.", ToolTipIcon.Warning);
            return;
        }

        SetEnabled(true);
    }

    void SetEnabled(bool on)
    {
        if (on)
        {
            State.SetChat(false); // включение заодно сбрасывает состояние чата
            _clicker.Reset();
        }
        State.Enabled = on;
        Log.Write(on ? "ВКЛ" : "ВЫКЛ");
        if (on) Sounds.PlayOn(); else Sounds.PlayOff();
        UpdateUi();
    }

    void BindTarget(IntPtr hwnd, int pid, string name)
    {
        bool changed = State.TargetPid != pid || State.TargetHwnd != hwnd;
        State.TargetHwnd = hwnd;
        State.TargetPid = pid;

        var s = State.Settings;
        if (s.GameProcess.Length == 0 && name.Length > 0)
        {
            var c = s.Clone();
            c.GameProcess = name;
            ApplySettings(c);
            _settingsForm?.LoadFrom(State.Settings);
        }
        if (!changed) return;

        NeedAdmin = !Environment.IsPrivilegedProcess && Native.IsProcessElevated(pid);
        Log.Write($"Окно игры: «{Native.WindowTitle(hwnd)}» ({name}.exe, pid {pid}, hwnd 0x{hwnd:X}), " +
                  $"игра от админа: {Native.IsProcessElevated(pid)}, мы от админа: {Environment.IsPrivilegedProcess}");
        if (NeedAdmin)
            Balloon("Игра запущена от администратора: без прав администратора нажатия до неё не дойдут. " +
                    "Меню в трее → «Перезапустить от администратора».", ToolTipIcon.Warning);
    }

    static bool TargetAlive() => State.TargetHwnd != IntPtr.Zero && Native.IsWindow(State.TargetHwnd);

    static readonly string[] NotGames =
    {
        "chrome", "msedge", "firefox", "opera", "browser", "yandex", "brave", "vivaldi",
        "explorer", "discord", "telegram", "notepad", "notepad++",
    };

    /// <summary>Процесс клиента Jade Dynasty (elementclient.exe) или тот, что запомнен в настройках.</summary>
    static bool IsKnownGameProcess(string name, Settings s) =>
        string.Equals(name, "elementclient", StringComparison.OrdinalIgnoreCase) ||
        (s.GameProcess.Length > 0 && string.Equals(name, s.GameProcess, StringComparison.OrdinalIgnoreCase));

    /// <summary>Окно игры: процесс совпадает с запомненным или заголовок начинается с «Jade Dynasty» (не браузер).</summary>
    static bool IsGameWindow(string name, string title, Settings s)
    {
        if (IsKnownGameProcess(name, s)) return true;
        if (s.GameTitle.Length == 0 || !title.Trim().StartsWith(s.GameTitle, StringComparison.OrdinalIgnoreCase))
            return false;
        return !NotGames.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Ищет видимое окно игры среди всех окон верхнего уровня. Совпадение по процессу важнее, затем — самое большое окно.</summary>
    static (IntPtr Hwnd, int Pid, string Name)? FindGameWindow(Settings s)
    {
        (IntPtr, int, string)? best = null;
        int bestScore = -1;
        var names = new Dictionary<int, string>();
        Native.EnumWindows((h, _) =>
        {
            if (!Native.IsWindowVisible(h) || Native.GetWindow(h, Native.GW_OWNER) != IntPtr.Zero) return true;
            Native.GetWindowThreadProcessId(h, out uint upid);
            int pid = (int)upid;
            if (pid == 0 || pid == State.OwnPid) return true;
            if (!names.TryGetValue(pid, out var name)) names[pid] = name = Native.ProcessName(pid);
            var title = Native.WindowTitle(h);
            if (!IsGameWindow(name, title, s)) return true;

            Native.GetWindowRect(h, out var r);
            int area = Math.Max(0, r.Right - r.Left) / 4 * (Math.Max(0, r.Bottom - r.Top) / 4);
            if (Native.IsIconic(h)) area = 1;
            bool byProcess = IsKnownGameProcess(name, s);
            int score = (byProcess ? 1 << 28 : 0) + Math.Min(area, (1 << 28) - 1);
            if (score > bestScore) { bestScore = score; best = (h, pid, name); }
            return true;
        }, IntPtr.Zero);
        return best;
    }

    bool TryFindGame()
    {
        if (FindGameWindow(State.Settings) is not { } w) return false;
        BindTarget(w.Hwnd, w.Pid, w.Name);
        return true;
    }

    public void ForgetGame()
    {
        if (State.Enabled) SetEnabled(false);
        State.TargetHwnd = IntPtr.Zero;
        State.TargetPid = 0;
        NeedAdmin = false;
        var c = State.Settings.Clone();
        c.GameProcess = "";
        ApplySettings(c);
        _settingsForm?.LoadFrom(State.Settings);
        Log.Write("Окно игры забыто");
    }

    // ---------------- Таймер интерфейса ----------------

    void OnTick()
    {
        var target = State.TargetHwnd;
        if (target != IntPtr.Zero && (!Native.IsWindow(target) || !Native.IsWindowVisible(target)))
        {
            // Игра могла пересоздать окно (загрузка, смена режима) — ищем новое, не выключаясь.
            bool dead = !Native.IsWindow(target);
            if (FindGameWindow(State.Settings) is { } w && w.Hwnd != target)
            {
                Log.Write($"Окно игры сменилось (старое {(dead ? "закрыто" : "скрыто")})");
                BindTarget(w.Hwnd, w.Pid, w.Name);
            }
            else if (dead)
            {
                Log.Write("Окно игры закрыто");
                State.TargetHwnd = IntPtr.Zero;
                State.TargetPid = 0;
                NeedAdmin = false;
                if (State.Enabled)
                {
                    SetEnabled(false);
                    Balloon("Окно игры закрыто — автонажатие выключено.");
                }
            }
        }
        if (State.TargetPid == 0 && ++_tickCount % 10 == 0) TryFindGame();

        long now = Environment.TickCount64;
        if (now - _rateAt >= 1000)
        {
            long sent = Interlocked.Read(ref State.Sent);
            State.PressesPerSecond = (int)Math.Round((sent - _rateSent) * 1000.0 / (now - _rateAt));
            _rateSent = sent;
            _rateAt = now;
        }
        UpdateUi();
    }

    (Color Color, string Text, Icon Icon) CurrentStatus()
    {
        var s = State.Settings;
        if (NeedAdmin) return (Color.Red, "НУЖЕН АДМИН", _iconWarn);
        if (!State.Enabled) return (Color.Gray, $"ВЫКЛ  {HotkeyText()}", _iconOff);
        if (State.ChatBlocks(s)) return (Color.Gold, "ЧАТ — пауза", _iconPause);
        if (s.SkipWhenModifiersHeld && State.ModifiersHeld()) return (Color.Gold, "ПАУЗА", _iconPause);
        return (Color.LimeGreen, $"ВКЛ  {State.PressesPerSecond}/сек", _iconOn);
    }

    void UpdateUi()
    {
        var (color, text, icon) = CurrentStatus();
        if (_tray.Icon != icon) _tray.Icon = icon;
        var tip = "JD YA HAVAU — " + text;
        if (_tray.Text != tip) _tray.Text = tip.Length > 63 ? tip[..63] : tip;
        _miToggle.Text = State.Enabled ? "Выключить" : "Включить";

        var s = State.Settings;
        var target = State.TargetHwnd;
        int fgPid = State.ForegroundPid();
        bool show = s.Overlay && target != IntPtr.Zero && !Native.IsIconic(target)
                    && (fgPid == State.TargetPid || fgPid == State.OwnPid);
        if (!show)
        {
            if (_overlay.Visible && !_overlay.Dragging) _overlay.Hide();
            return;
        }

        _overlay.SetStatus(color, text);
        if (!_overlay.Dragging)
        {
            var origin = new Native.POINT();
            Native.ClientToScreen(target, ref origin);
            var loc = new Point(origin.X + s.OverlayX, origin.Y + s.OverlayY);
            if (_overlay.Location != loc) _overlay.Location = loc;
        }
        if (!_overlay.Visible) _overlay.Show();
    }

    void SaveOverlayOffset()
    {
        var target = State.TargetHwnd;
        if (target == IntPtr.Zero) return;
        var origin = new Native.POINT();
        Native.ClientToScreen(target, ref origin);
        var c = State.Settings.Clone();
        c.OverlayX = _overlay.Left - origin.X;
        c.OverlayY = _overlay.Top - origin.Y;
        ApplySettings(c);
    }

    // ---------------- Настройки ----------------

    public void ApplySettings(Settings s)
    {
        s.Normalize();
        bool hotkeyChanged = s.ToggleHotkey != State.Settings.ToggleHotkey;
        State.Settings = s;
        if (hotkeyChanged) RegisterHotkey();
        _saveTimer.Stop();
        _saveTimer.Start();
        UpdateUi();
    }

    void SaveNow()
    {
        var json = SettingsStore.Serialize(State.Settings);
        if (json == _lastSavedJson) return;
        _ignoreWatcherUntil = DateTime.UtcNow.AddSeconds(1.5);
        if (SettingsStore.Save(State.Settings, out var err)) _lastSavedJson = json;
        else Balloon("Не удалось сохранить settings.json: " + err, ToolTipIcon.Error);
    }

    void RestartReload()
    {
        if (DateTime.UtcNow < _ignoreWatcherUntil) return;
        _reloadTimer.Stop();
        _reloadTimer.Start();
    }

    /// <summary>settings.json поправили вручную: применяем без перезапуска.</summary>
    void ReloadFromDisk()
    {
        if (DateTime.UtcNow < _ignoreWatcherUntil || !File.Exists(SettingsStore.FilePath)) return;
        var s = SettingsStore.Load(out var err);
        if (s is null)
        {
            Balloon("Ошибка в settings.json, оставлены прежние настройки: " + err, ToolTipIcon.Warning);
            return;
        }
        var json = SettingsStore.Serialize(s);
        if (json == _lastSavedJson) return;
        _lastSavedJson = json;
        bool hotkeyChanged = s.ToggleHotkey != State.Settings.ToggleHotkey;
        State.Settings = s;
        if (hotkeyChanged) RegisterHotkey();
        _settingsForm?.LoadFrom(s);
        Balloon("Настройки из settings.json применены.");
    }

    void ShowSettings()
    {
        _settingsForm ??= new SettingsForm(this);
        if (!_settingsForm.Visible) _settingsForm.Show();
        if (_settingsForm.WindowState == FormWindowState.Minimized) _settingsForm.WindowState = FormWindowState.Normal;
        _settingsForm.Activate();
    }

    public void PickChatArea()
    {
        if (!TargetAlive() && !TryFindGame())
        {
            MessageBox.Show("Сначала зайдите в игру и нажмите хоткей включения, чтобы программа узнала окно игры.",
                AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var target = State.TargetHwnd;
        Native.GetClientRect(target, out var rc);
        var origin = new Native.POINT();
        Native.ClientToScreen(target, ref origin);
        using var picker = new ChatAreaPicker(new Rectangle(origin.X, origin.Y, rc.Right - rc.Left, rc.Bottom - rc.Top));
        if (picker.ShowDialog() == DialogResult.OK && picker.Result is { } area)
        {
            var c = State.Settings.Clone();
            c.ChatArea = area;
            ApplySettings(c);
            _settingsForm?.LoadFrom(State.Settings);
            Balloon("Поле чата сохранено. Клик в нём = чат открыт, клик в другом месте игры = чат закрыт.");
        }
    }

    // ---------------- Хоткей ----------------

    public void SuspendHotkey(bool suspend)
    {
        _hotkeySuspended = suspend;
        RegisterHotkey();
    }

    void RegisterHotkey()
    {
        if (_registeredHotkey.Length > 0)
        {
            Native.UnregisterHotKey(_hotkeyWindow.Handle, HotkeyId);
            _registeredHotkey = "";
        }
        HotkeyError = null;
        if (_hotkeySuspended) return;

        var text = State.Settings.ToggleHotkey;
        if (!KeySpec.TryParse(text, out var k))
        {
            HotkeyError = "хоткей не задан";
            return;
        }
        uint mods = Native.MOD_NOREPEAT;
        if (k.Ctrl) mods |= Native.MOD_CONTROL;
        if (k.Alt) mods |= Native.MOD_ALT;
        if (k.Shift) mods |= Native.MOD_SHIFT;
        if (Native.RegisterHotKey(_hotkeyWindow.Handle, HotkeyId, mods, (uint)k.Key))
            _registeredHotkey = text;
        else
            HotkeyError = "занят другой программой";
    }

    static string HotkeyText() =>
        KeySpec.TryParse(State.Settings.ToggleHotkey, out var k) ? k.ToString() : "хоткей";

    sealed class HotkeyWindow : NativeWindow
    {
        public event Action? Pressed;
        public HotkeyWindow() => CreateHandle(new CreateParams());

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY && (int)m.WParam == HotkeyId) Pressed?.Invoke();
            base.WndProc(ref m);
        }
    }

    // ---------------- Прочее ----------------

    void RestartAsAdmin()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--wait")
            {
                UseShellExecute = true,
                Verb = "runas",
            });
        }
        catch (Win32Exception)
        {
            return; // пользователь отказался в окне UAC
        }
        ExitThread();
    }

    static void OpenSettingsFolder()
    {
        try { Process.Start("explorer.exe", $"/select,\"{SettingsStore.FilePath}\""); } catch { }
    }

    void Balloon(string text, ToolTipIcon icon = ToolTipIcon.Info) =>
        _tray.ShowBalloonTip(4000, AppInfo.Name, text, icon);

    static Icon MakeIcon(Bitmap pig, Color c)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(pig, 0, 0, 32, 32);
            using var b = new SolidBrush(c);
            using var p = new Pen(Color.FromArgb(30, 30, 30), 2);
            g.FillEllipse(b, 17, 17, 14, 14);
            g.DrawEllipse(p, 17, 17, 14, 14);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    protected override void ExitThreadCore()
    {
        State.Enabled = false;
        _tick.Stop();
        _clicker.Stop();
        _hooks.Stop();
        if (_registeredHotkey.Length > 0) Native.UnregisterHotKey(_hotkeyWindow.Handle, HotkeyId);
        _saveTimer.Stop();
        SaveNow();
        _watcher?.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _overlay.Close();
        _settingsForm?.Dispose();
        base.ExitThreadCore();
    }
}

namespace JdClicker;

/// <summary>
/// Низкоуровневые хуки клавиатуры и мыши в отдельном потоке.
/// Ввод пользователя только читается, чтобы понять, открыт ли чат:
///   Enter переключает «чат открыт/закрыт», Esc закрывает,
///   клик в заданной области поля чата открывает, клик в остальной части окна игры закрывает.
/// Хук видит события в том же порядке, что и игра, поэтому он последним проверяет
/// каждое наше нажатие и выбрасывает его, если к этому моменту открылся чат или игра потеряла фокус.
/// </summary>
internal sealed unsafe class InputHooks
{
    const int VK_RETURN = 0x0D, VK_ESCAPE = 0x1B;

    readonly Native.HookProc _kbProc, _msProc; // держим ссылки, иначе GC соберёт делегаты
    IntPtr _kbHook, _msHook;
    uint _threadId;
    Thread? _thread;
    bool _enterHeld;

    public bool Installed { get; private set; }

    public InputHooks()
    {
        _kbProc = KeyboardProc;
        _msProc = MouseProc;
    }

    public void Start()
    {
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            _threadId = Native.GetCurrentThreadId();
            var mod = Native.GetModuleHandle(null);
            _kbHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _kbProc, mod, 0);
            _msHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _msProc, mod, 0);
            Installed = _kbHook != IntPtr.Zero && _msHook != IntPtr.Zero;
            ready.Set();
            while (Native.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                Native.TranslateMessage(ref msg);
                Native.DispatchMessage(ref msg);
            }
            if (_kbHook != IntPtr.Zero) Native.UnhookWindowsHookEx(_kbHook);
            if (_msHook != IntPtr.Zero) Native.UnhookWindowsHookEx(_msHook);
        })
        { IsBackground = true, Name = "InputHooks", Priority = ThreadPriority.Highest };
        _thread.Start();
        ready.Wait();
    }

    public void Stop()
    {
        if (_threadId != 0) Native.PostThreadMessage(_threadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread?.Join(1000);
    }

    IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var k = (Native.KBDLLHOOKSTRUCT*)lParam;
            int msg = (int)wParam;
            bool down = msg is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN;
            bool up = msg is Native.WM_KEYUP or Native.WM_SYSKEYUP;
            bool injected = (k->flags & Native.LLKHF_INJECTED) != 0;

            if (injected && k->dwExtraInfo == KeySender.Magic)
            {
                if (down)
                {
                    // Последняя проверка перед игрой: открытый чат или чужое окно означает «не пропускать».
                    var s = State.Settings;
                    if (!State.Enabled || !State.GameForeground() || State.ChatBlocks(s))
                    {
                        Interlocked.Increment(ref State.Blocked);
                        return (IntPtr)1;
                    }
                    if (k->vkCode is not (>= 0x10 and <= 0x12) and not (>= 0xA0 and <= 0xA5))
                        Interlocked.Increment(ref State.Sent); // модификаторы комбинаций не считаем
                }
            }
            else if (!injected)
            {
                if (k->vkCode == VK_RETURN)
                {
                    if (down && !_enterHeld)
                    {
                        _enterHeld = true;
                        // Alt+Enter переключает полноэкранный режим, а не чат.
                        if ((k->flags & Native.LLKHF_ALTDOWN) == 0 && State.GameForeground())
                            State.SetChat(!State.ChatOpen);
                    }
                    else if (up) _enterHeld = false;
                }
                else if (k->vkCode == VK_ESCAPE && down && State.GameForeground())
                {
                    State.SetChat(false);
                }
            }
        }
        return Native.CallNextHookEx(_kbHook, nCode, wParam, lParam);
    }

    IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (int)wParam == Native.WM_LBUTTONDOWN)
        {
            var m = (Native.MSLLHOOKSTRUCT*)lParam;
            if ((m->flags & Native.LLMHF_INJECTED) == 0)
                OnUserClick(m->pt);
        }
        return Native.CallNextHookEx(_msHook, nCode, wParam, lParam);
    }

    static void OnUserClick(Native.POINT pt)
    {
        var target = State.TargetHwnd;
        int pid = State.TargetPid;
        if (target == IntPtr.Zero || pid == 0) return;

        var root = Native.GetAncestor(Native.WindowFromPoint(pt), Native.GA_ROOT);
        Native.GetWindowThreadProcessId(root, out uint clickPid);
        if (clickPid != pid) return; // клик не по игре, например по индикатору или другому окну

        var area = State.Settings.ChatArea;
        if (area is null)
        {
            // Без заданной области клик по игре считаем закрытием чата (так ведёт себя игра при клике мимо).
            State.SetChat(false);
            return;
        }
        var p = pt;
        Native.ScreenToClient(target, ref p);
        State.SetChat(area.Contains(p.X, p.Y));
    }
}

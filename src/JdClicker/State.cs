namespace JdClicker;

/// <summary>Общее состояние: его читают поток нажатий, поток хуков и UI.</summary>
internal static class State
{
    public static volatile Settings Settings = Settings.CreateDefault();

    /// <summary>Автонажатие включено хоткеем.</summary>
    public static volatile bool Enabled;

    /// <summary>По нашим данным открыто поле ввода чата. Меняется через SetChat.</summary>
    public static volatile bool ChatOpen;

    /// <summary>Сколько ждать после закрытия чата, прежде чем снова жать (игра должна успеть закрыть поле ввода).</summary>
    public const int ChatCloseGraceMs = 150;
    static long _chatChangedAt = -ChatCloseGraceMs;

    public static void SetChat(bool open)
    {
        if (ChatOpen == open) return;
        Interlocked.Exchange(ref _chatChangedAt, Environment.TickCount64);
        ChatOpen = open;
    }

    public static volatile IntPtr TargetHwnd;
    public static volatile int TargetPid;

    public static long Sent;
    public static long Blocked;

    /// <summary>Сколько нажатий в секунду реально дошло до игры (обновляется раз в секунду).</summary>
    public static volatile int PressesPerSecond;

    public static readonly int OwnPid = Environment.ProcessId;

    public static int ForegroundPid()
    {
        var fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero) return 0;
        Native.GetWindowThreadProcessId(fg, out uint pid);
        return (int)pid;
    }

    public static bool GameForeground()
    {
        int pid = TargetPid;
        return pid != 0 && ForegroundPid() == pid;
    }

    public static bool ModifiersHeld() =>
        (Native.GetAsyncKeyState(0x10) & 0x8000) != 0 ||  // Shift
        (Native.GetAsyncKeyState(0x11) & 0x8000) != 0 ||  // Ctrl
        (Native.GetAsyncKeyState(0x12) & 0x8000) != 0;    // Alt

    public static bool ChatBlocks(Settings s) =>
        s.PauseInChat && (ChatOpen || Environment.TickCount64 - Interlocked.Read(ref _chatChangedAt) < ChatCloseGraceMs);

    /// <summary>Можно ли прямо сейчас отправить нажатие в игру.</summary>
    public static bool CanSend(Settings s) =>
        Enabled && GameForeground() && !ChatBlocks(s) && !(s.SkipWhenModifiersHeld && ModifiersHeld());
}

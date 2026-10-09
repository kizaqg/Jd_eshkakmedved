using System.Diagnostics;
using System.Runtime.InteropServices;

namespace JdClicker;

/// <summary>Отправка нажатий через SendInput со скан-кодами. Только клавиатура.</summary>
internal static unsafe class KeySender
{
    /// <summary>Метка наших нажатий в dwExtraInfo: по ней хук отличает их от нажатий пользователя.</summary>
    public static readonly UIntPtr Magic = (UIntPtr)0x4A44434Bu; // "JDCK"

    const ushort VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;

    public static bool Press(KeySpec k, int holdMs)
    {
        Span<ushort> mods = stackalloc ushort[3];
        int n = 0;
        if (k.Ctrl) mods[n++] = VK_CONTROL;
        if (k.Alt) mods[n++] = VK_MENU;
        if (k.Shift) mods[n++] = VK_SHIFT;

        var down = stackalloc Native.INPUT[n + 1];
        for (int i = 0; i < n; i++) down[i] = Make(mods[i], false);
        down[n] = Make((ushort)k.Key, false);
        bool ok = Native.SendInput((uint)(n + 1), down, sizeof(Native.INPUT)) == n + 1;

        Thread.Sleep(holdMs);

        // Отпускаем всегда, даже если нажатие было отклонено.
        var up = stackalloc Native.INPUT[n + 1];
        up[0] = Make((ushort)k.Key, true);
        for (int i = 0; i < n; i++) up[i + 1] = Make(mods[n - 1 - i], true);
        Native.SendInput((uint)(n + 1), up, sizeof(Native.INPUT));
        return ok;
    }

    static Native.INPUT Make(ushort vk, bool keyUp)
    {
        uint sc = Native.MapVirtualKey(vk, Native.MAPVK_VK_TO_VSC_EX);
        uint flags = Native.KEYEVENTF_SCANCODE;
        if ((sc >> 8) is 0xE0 or 0xE1) flags |= Native.KEYEVENTF_EXTENDEDKEY;
        if (keyUp) flags |= Native.KEYEVENTF_KEYUP;
        return new Native.INPUT
        {
            type = Native.INPUT_KEYBOARD,
            u = new Native.InputUnion
            {
                ki = new Native.KEYBDINPUT { wVk = 0, wScan = (ushort)(sc & 0xFF), dwFlags = flags, dwExtraInfo = Magic },
            },
        };
    }
}

/// <summary>Поток автонажатия: у каждого слота свой интервал, между клавишами пауза GapMs.</summary>
internal sealed class Clicker
{
    readonly long[] _due = new long[Settings.SlotCount];
    readonly Stopwatch _clock = Stopwatch.StartNew();
    volatile bool _resetRequested = true;
    volatile bool _stop;
    Thread? _thread;

    public void Start()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "Clicker", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public void Stop()
    {
        _stop = true;
        _thread?.Join(1000);
    }

    /// <summary>Вызывается при включении: первые нажатия идут сразу, по очереди.</summary>
    public void Reset() => _resetRequested = true;

    void Loop()
    {
        Native.timeBeginPeriod(1);
        try
        {
            while (!_stop)
            {
                var s = State.Settings;
                long now = _clock.ElapsedMilliseconds;

                if (_resetRequested)
                {
                    _resetRequested = false;
                    for (int i = 0; i < _due.Length; i++) _due[i] = now;
                }

                if (!State.CanSend(s))
                {
                    Thread.Sleep(5);
                    continue;
                }

                // Самый «просроченный» слот жмём первым, так клавиши чередуются честно.
                int pick = -1;
                long best = long.MaxValue, next = long.MaxValue;
                for (int i = 0; i < Settings.SlotCount; i++)
                {
                    if (s.Parsed[i] is null) continue;
                    next = Math.Min(next, _due[i]);
                    if (_due[i] <= now && _due[i] < best) { best = _due[i]; pick = i; }
                }

                if (pick < 0)
                {
                    Thread.Sleep(next == long.MaxValue ? 20 : (int)Math.Clamp(next - now, 1, 5));
                    continue;
                }

                var spec = s.Parsed[pick]!.Value;
                // Счётчики State.Sent/Blocked ведёт хук: он видит, дошло ли нажатие до игры.
                KeySender.Press(spec, s.HoldMs);
                _due[pick] = _clock.ElapsedMilliseconds + s.Keys[pick].IntervalMs;

                if (s.GapMs > 0) Thread.Sleep(s.GapMs);
            }
        }
        finally
        {
            Native.timeEndPeriod(1);
        }
    }
}

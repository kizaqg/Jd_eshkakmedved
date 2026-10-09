namespace JdClicker;

/// <summary>Клавиша с модификаторами: «8», «-», «F5», «Alt+1», «Ctrl+Shift+Q».</summary>
internal readonly record struct KeySpec(Keys Key, bool Ctrl, bool Alt, bool Shift)
{
    static readonly (string Text, Keys Key)[] Aliases =
    {
        ("0", Keys.D0), ("1", Keys.D1), ("2", Keys.D2), ("3", Keys.D3), ("4", Keys.D4),
        ("5", Keys.D5), ("6", Keys.D6), ("7", Keys.D7), ("8", Keys.D8), ("9", Keys.D9),
        ("-", Keys.OemMinus), ("=", Keys.Oemplus), ("`", Keys.Oemtilde),
        ("[", Keys.OemOpenBrackets), ("]", Keys.OemCloseBrackets), (";", Keys.OemSemicolon),
        ("'", Keys.OemQuotes), (",", Keys.Oemcomma), (".", Keys.OemPeriod),
        ("/", Keys.OemQuestion), ("\\", Keys.OemPipe),
        ("Num0", Keys.NumPad0), ("Num1", Keys.NumPad1), ("Num2", Keys.NumPad2), ("Num3", Keys.NumPad3),
        ("Num4", Keys.NumPad4), ("Num5", Keys.NumPad5), ("Num6", Keys.NumPad6), ("Num7", Keys.NumPad7),
        ("Num8", Keys.NumPad8), ("Num9", Keys.NumPad9),
        ("Num*", Keys.Multiply), ("Num+", Keys.Add), ("Num-", Keys.Subtract), ("Num/", Keys.Divide),
    };

    public bool HasModifiers => Ctrl || Alt || Shift;

    public static bool IsModifierKey(Keys k) => k is Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey
        or Keys.ControlKey or Keys.LControlKey or Keys.RControlKey
        or Keys.Menu or Keys.LMenu or Keys.RMenu or Keys.LWin or Keys.RWin;

    /// <summary>Из KeyEventArgs.KeyData (клавиша + модификаторы).</summary>
    public static KeySpec FromKeyData(Keys keyData) => new(
        keyData & Keys.KeyCode,
        (keyData & Keys.Control) != 0,
        (keyData & Keys.Alt) != 0,
        (keyData & Keys.Shift) != 0);

    public static bool TryParse(string? text, out KeySpec spec)
    {
        spec = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Trim().Split('+', StringSplitOptions.TrimEntries);
        // «Num+» и просто «+» раскладываются в пустую последнюю часть: склеиваем обратно.
        if (parts.Length >= 2 && parts[^1].Length == 0)
        {
            parts = parts[..^1];
            parts[^1] += "+";
        }
        bool ctrl = false, alt = false, shift = false;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl": case "control": ctrl = true; break;
                case "alt": alt = true; break;
                case "shift": shift = true; break;
                default: return false;
            }
        }
        var last = parts[^1];
        Keys key = Keys.None;
        foreach (var (t, k) in Aliases)
            if (string.Equals(t, last, StringComparison.OrdinalIgnoreCase)) { key = k; break; }
        if (key == Keys.None && !(Enum.TryParse(last, true, out key) && Enum.IsDefined(key)))
            return false;
        key &= Keys.KeyCode;
        if (key == Keys.None || IsModifierKey(key)) return false;
        spec = new KeySpec(key, ctrl, alt, shift);
        return true;
    }

    public static string KeyName(Keys key)
    {
        foreach (var (t, k) in Aliases)
            if (k == key) return t;
        return key switch
        {
            Keys.Return => "Enter",
            Keys.Next => "PageDown",
            Keys.Prior => "PageUp",
            Keys.Capital => "CapsLock",
            _ => key.ToString(),
        };
    }

    public override string ToString()
    {
        var s = KeyName(Key);
        if (Shift) s = "Shift+" + s;
        if (Alt) s = "Alt+" + s;
        if (Ctrl) s = "Ctrl+" + s;
        return s;
    }
}

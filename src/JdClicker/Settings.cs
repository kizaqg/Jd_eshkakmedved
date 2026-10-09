using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JdClicker;

internal sealed class KeySlot
{
    public bool Enabled { get; set; } = true;
    public string Key { get; set; } = "";
    public int IntervalMs { get; set; } = 300;
}

internal sealed class ChatArea
{
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }

    public bool Contains(int x, int y) => x >= X && y >= Y && x < X + W && y < Y + H;
}

internal sealed class Settings
{
    public const int SlotCount = 6;

    public List<KeySlot> Keys { get; set; } = new();
    public int HoldMs { get; set; } = 30;
    public int GapMs { get; set; } = 40;
    public string ToggleHotkey { get; set; } = "F11";
    public bool Sound { get; set; } = true;
    public bool Overlay { get; set; } = true;
    public int OverlayX { get; set; } = 300;
    public int OverlayY { get; set; } = 6;
    public bool PauseInChat { get; set; } = true;
    public ChatArea? ChatArea { get; set; }
    public bool SkipWhenModifiersHeld { get; set; } = true;
    public string GameProcess { get; set; } = "";

    /// <summary>Разобранные клавиши слотов (null, если слот пуст, выключен или клавиша не распознана).</summary>
    [JsonIgnore] public KeySpec?[] Parsed { get; private set; } = new KeySpec?[SlotCount];

    public static Settings CreateDefault() => new Settings
    {
        Keys =
        {
            new KeySlot { Key = "8", IntervalMs = 300 },
            new KeySlot { Key = "9", IntervalMs = 300 },
            new KeySlot { Key = "0", IntervalMs = 300 },
        },
    }.Normalize();

    /// <summary>Приводит значения к допустимым диапазонам и разбирает клавиши.</summary>
    public Settings Normalize()
    {
        Keys ??= new();
        while (Keys.Count < SlotCount) Keys.Add(new KeySlot { Enabled = false });
        if (Keys.Count > SlotCount) Keys.RemoveRange(SlotCount, Keys.Count - SlotCount);
        HoldMs = Math.Clamp(HoldMs, 5, 200);
        GapMs = Math.Clamp(GapMs, 0, 1000);
        ToggleHotkey ??= "";
        GameProcess ??= "";
        if (ChatArea is { W: <= 0 } or { H: <= 0 }) ChatArea = null;
        Parsed = new KeySpec?[SlotCount];
        for (int i = 0; i < SlotCount; i++)
        {
            var slot = Keys[i] ??= new KeySlot { Enabled = false };
            slot.Key ??= "";
            slot.IntervalMs = Math.Clamp(slot.IntervalMs, 20, 600_000);
            if (slot.Enabled && KeySpec.TryParse(slot.Key, out var spec) && IsAllowedPotionKey(spec.Key))
                Parsed[i] = spec;
        }
        return this;
    }

    /// <summary>Enter и Esc открывают/закрывают чат, их жать нельзя.</summary>
    public static bool IsAllowedPotionKey(System.Windows.Forms.Keys k) =>
        k != System.Windows.Forms.Keys.Return && k != System.Windows.Forms.Keys.Escape;

    public Settings Clone() =>
        JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(this, SettingsStore.Json), SettingsStore.Json)!.Normalize();
}

internal static class SettingsStore
{
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string FilePath { get; } = Path.Combine(AppContext.BaseDirectory, "settings.json");

    /// <summary>Читает настройки. Если файла нет, создаёт его со значениями по умолчанию.</summary>
    public static Settings Load(out string? error)
    {
        error = null;
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json);
                if (s != null) return s.Normalize();
            }
        }
        catch (Exception e)
        {
            error = e.Message;
            return null!;
        }
        var d = Settings.CreateDefault();
        Save(d, out _);
        return d;
    }

    public static string Serialize(Settings s) => JsonSerializer.Serialize(s, Json);

    public static bool Save(Settings s, out string? error)
    {
        error = null;
        try
        {
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, Serialize(s));
            File.Move(tmp, FilePath, overwrite: true);
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }
}

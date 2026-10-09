using System.Media;

namespace JdClicker;

/// <summary>Короткие сигналы, синтезируются в памяти: включение (вверх), выключение (вниз), ошибка.</summary>
internal static class Sounds
{
    static readonly SoundPlayer On = Make((660, 70), (0, 25), (990, 90));
    static readonly SoundPlayer Off = Make((990, 70), (0, 25), (520, 110));
    static readonly SoundPlayer Error = Make((300, 120), (0, 40), (300, 120));

    public static void PlayOn() => Play(On);
    public static void PlayOff() => Play(Off);
    public static void PlayError() => Play(Error);

    static void Play(SoundPlayer p)
    {
        if (!State.Settings.Sound) return;
        try { p.Play(); } catch { /* нет звукового устройства — молчим */ }
    }

    /// <summary>Тоны (частота Гц, длительность мс); частота 0 означает паузу.</summary>
    static SoundPlayer Make(params (int Hz, int Ms)[] tones)
    {
        const int rate = 22050;
        var samples = new List<short>();
        foreach (var (hz, ms) in tones)
        {
            int n = rate * ms / 1000;
            int fade = Math.Min(n / 4, rate * 8 / 1000);
            for (int i = 0; i < n; i++)
            {
                double env = Math.Min(1.0, Math.Min(i, n - 1 - i) / (double)Math.Max(1, fade));
                double v = hz == 0 ? 0 : Math.Sin(2 * Math.PI * hz * i / rate) * env * 0.35;
                samples.Add((short)(v * short.MaxValue));
            }
        }

        var ms2 = new MemoryStream();
        using (var w = new BinaryWriter(ms2, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            int dataLen = samples.Count * 2;
            w.Write("RIFF"u8); w.Write(36 + dataLen); w.Write("WAVE"u8);
            w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write("data"u8); w.Write(dataLen);
            foreach (var s in samples) w.Write(s);
        }
        ms2.Position = 0;
        var player = new SoundPlayer(ms2);
        player.Load();
        return player;
    }
}

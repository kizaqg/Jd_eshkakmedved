using System.Reflection;

namespace JdClicker;

internal static class AppInfo
{
    public const string Name = "JD YA HAVAU TI PADAESH";

    public static readonly string Version =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";

    /// <summary>Иконка со свиньёй из ресурсов exe.</summary>
    public static Bitmap LoadPig()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("JdClicker.Assets.pig.png")!;
        return new Bitmap(s);
    }

    public static Icon LoadAppIcon()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("JdClicker.Assets.app.ico")!;
        return new Icon(s);
    }
}

/// <summary>Короткий журнал событий в файл рядом с exe (для разбора проблем, не для подстройки).</summary>
internal static class Log
{
    static readonly object Gate = new();
    public static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "log.txt");

    public static void Write(string text)
    {
        try
        {
            lock (Gate)
            {
                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > 512 * 1024) File.Delete(FilePath);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {text}{Environment.NewLine}");
            }
        }
        catch { /* журнал не обязателен */ }
    }
}

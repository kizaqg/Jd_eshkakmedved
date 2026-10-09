namespace JdClicker;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using var mutex = new Mutex(true, @"Local\JdClicker.SingleInstance", out bool owned);
        if (!owned && args.Contains("--wait"))
        {
            // Перезапуск от администратора: ждём, пока закроется прежний экземпляр.
            try { owned = mutex.WaitOne(5000); }
            catch (AbandonedMutexException) { owned = true; }
        }
        if (!owned)
        {
            MessageBox.Show("Программа уже запущена — ищите свинью в трее.", AppInfo.Name,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            Application.Run(new TrayApp());
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }
}

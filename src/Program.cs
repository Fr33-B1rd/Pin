namespace PinApp;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args is ["--widget-guard"])
        {
            DesktopWidget.RunGuard();
            return;
        }
        using var single = new Mutex(true, @"Local\PinApp.SingleInstance", out bool first);
        if (!first) return;

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += (_, e) =>
            System.Diagnostics.Debug.WriteLine($"[Pin] UI exception: {e.Exception}");
        Application.Run(new TrayContext());
    }
}


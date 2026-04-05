namespace RestartHistory;

public partial class App : System.Windows.Application
{
    private TrayApplicationContext? _trayContext;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        // No main window — we live in the tray
        _trayContext = new TrayApplicationContext();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _trayContext?.Dispose();
        base.OnExit(e);
    }
}

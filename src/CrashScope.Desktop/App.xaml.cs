using System.Windows;

namespace CrashScope.Desktop;

public partial class App : Application
{
    private DesktopSingleInstanceCoordinator? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var coordinator = new DesktopSingleInstanceCoordinator();

        if (!coordinator.TryAcquirePrimary())
        {
            coordinator.SignalPrimary();
            coordinator.Dispose();
            Shutdown();
            return;
        }

        _singleInstance = coordinator;

        var window = new MainWindow();
        MainWindow = window;

        coordinator.StartListening(() =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                ActivateWindow(window);
            });
        });

        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        _singleInstance = null;
        base.OnExit(e);
    }

    internal static void ActivateWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();

        // A short topmost pulse improves foreground activation when the
        // request comes from the tray or a second process.
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }
}
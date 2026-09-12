using System.IO;
using System.Diagnostics;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace CrashScope.Desktop;

public partial class MainWindow : Window
{
    private readonly DesktopStartupCoordinator _startup = new();
    private bool _initializationStarted;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += MainWindow_SourceInitialized;
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        _ = WindowsWindowTheme.TryApplyDarkChrome(this);
    }
    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initializationStarted)
        {
            return;
        }

        _initializationStarted = true;

        try
        {
            StatusText.Text = "Starting local Agent...";
            await _startup.EnsureAgentAsync();

            StatusText.Text = "Preparing desktop renderer...";

            string runtimeVersion;
            try
            {
                runtimeVersion = CoreWebView2Environment.GetAvailableBrowserVersionString();
            }
            catch (WebView2RuntimeNotFoundException)
            {
                ShowRecoverableError(
                    "Microsoft Edge WebView2 Runtime is not installed. "
                    + "CrashScope can still open the local dashboard in your default browser.");
                return;
            }

            if (string.IsNullOrWhiteSpace(runtimeVersion))
            {
                ShowRecoverableError(
                    "Microsoft Edge WebView2 Runtime could not be detected. "
                    + "CrashScope can still open the local dashboard in your default browser.");
                return;
            }

            var localAppData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            var userDataFolder = Path.Combine(
                localAppData,
                "CrashScope",
                "WebView2");

            Directory.CreateDirectory(userDataFolder);

            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataFolder);

            await DashboardView.EnsureCoreWebView2Async(environment);

            var core = DashboardView.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
#if !DEBUG
            core.Settings.AreDevToolsEnabled = false;
#endif

            DashboardView.NavigationStarting += DashboardView_NavigationStarting;
            core.NewWindowRequested += Core_NewWindowRequested;

            DashboardView.Source = DesktopStartupCoordinator.DashboardUri;
            DashboardView.Visibility = Visibility.Visible;
            LoadingPanel.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            ShowRecoverableError(
                "CrashScope desktop dashboard could not start.\n\n"
                + ex.Message);
        }
    }

    private void DashboardView_NavigationStarting(
        object? sender,
        CoreWebView2NavigationStartingEventArgs e)
    {
        if (DashboardNavigationPolicy.IsAllowed(e.Uri))
        {
            return;
        }

        e.Cancel = true;
        OpenExternal(e.Uri);
    }

    private void Core_NewWindowRequested(
        object? sender,
        CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;

        if (!DashboardNavigationPolicy.IsAllowed(e.Uri))
        {
            OpenExternal(e.Uri);
        }
    }

    private void BrowserFallbackButton_Click(object sender, RoutedEventArgs e)
    {
        OpenExternal(DesktopStartupCoordinator.DashboardUri.AbsoluteUri);
    }

    private void ShowRecoverableError(string message)
    {
        StatusText.Text = message;
        BrowserFallbackButton.Visibility = Visibility.Visible;
    }

    private static void OpenExternal(string? rawUri)
    {
        if (string.IsNullOrWhiteSpace(rawUri))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = rawUri,
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (
            ex is InvalidOperationException
            or System.ComponentModel.Win32Exception)
        {
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        DashboardView.Dispose();
        _startup.Dispose();
    }
}
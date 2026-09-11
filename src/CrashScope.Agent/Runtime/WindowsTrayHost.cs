using System.Runtime.InteropServices;
using CrashScope.Agent.Incidents;
using CrashScope.Core.Incidents;

namespace CrashScope.Agent.Runtime;

internal sealed class WindowsTrayHost : IHostedService, IDisposable
{
    private const uint WmApp = 0x8000;
    private const uint TrayCallbackMessage = WmApp + 1;
    private const uint IncidentNotificationMessage = WmApp + 2;
    private const uint RefreshMessage = WmApp + 3;
    private const uint WmClose = 0x0010;
    private const uint WmDestroy = 0x0002;
    private const uint WmContextMenu = 0x007B;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmRButtonUp = 0x0205;
    private const uint NinSelect = 0x0400;
    private const uint NinKeySelect = 0x0401;

    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;
    private const uint NimSetVersion = 0x00000004;
    private const uint NotifyIconVersion4 = 4;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NifInfo = 0x00000010;
    private const uint NifShowTip = 0x00000080;
    private const uint NiifInfo = 0x00000001;
    private const uint NiifRespectQuietTime = 0x00000080;

    private const uint MfString = 0x00000000;
    private const uint MfSeparator = 0x00000800;
    private const uint MfChecked = 0x00000008;
    private const uint MfGrayed = 0x00000001;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCmd = 0x0100;
    private const uint WsExToolWindow = 0x00000080;

    private const uint CommandOpen = 1001;
    private const uint CommandAutoAssist = 1002;
    private const uint CommandStartup = 1003;
    private const uint CommandExit = 1004;
    private const int IdiApplication = 32512;

    private readonly TrayControlService _controls;
    private readonly InMemoryIncidentReportSink _incidents;
    private readonly object _notificationSync = new();
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Thread? _thread;
    private WindowProc? _windowProc;
    private IntPtr _window;
    private IntPtr _icon;
    private bool _ownsIcon;
    private bool _trayIconRegistered;
    private uint _taskbarCreatedMessage;
    private IncidentReport? _pendingIncident;
    private bool _disposed;

    public WindowsTrayHost(
        TrayControlService controls,
        InMemoryIncidentReportSink incidents)
    {
        ArgumentNullException.ThrowIfNull(controls);
        ArgumentNullException.ThrowIfNull(incidents);
        _controls = controls;
        _incidents = incidents;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.CompletedTask;
        }

        _incidents.ReportAdded += OnIncidentAdded;
        _thread = new Thread(RunMessageLoop)
        {
            IsBackground = true,
            Name = "CrashScope Windows tray"
        };
        _thread.Start();
        return _started.Task.WaitAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _incidents.ReportAdded -= OnIncidentAdded;
        var window = _window;
        if (window != IntPtr.Zero)
        {
            PostMessageW(window, WmClose, UIntPtr.Zero, IntPtr.Zero);
        }

        if (_thread is not null)
        {
            await _stopped.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _incidents.ReportAdded -= OnIncidentAdded;
    }

    private void RunMessageLoop()
    {
        string? className = null;
        IntPtr instance = IntPtr.Zero;
        try
        {
            _windowProc = WindowProcedure;
            instance = GetModuleHandleW(null);
            className = $"CrashScope.Tray.{Environment.ProcessId}";
            _taskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");
            var windowClass = new WindowClassEx
            {
                Size = (uint)Marshal.SizeOf<WindowClassEx>(),
                Instance = instance,
                WindowProcedure = Marshal.GetFunctionPointerForDelegate(_windowProc),
                ClassName = className
            };

            if (RegisterClassExW(ref windowClass) == 0)
            {
                throw new InvalidOperationException("Could not register the CrashScope tray window class.");
            }

            _window = CreateWindowExW(
                WsExToolWindow,
                className,
                "CrashScope Tray",
                0,
                0,
                0,
                0,
                0,
                IntPtr.Zero,
                IntPtr.Zero,
                instance,
                IntPtr.Zero);
            if (_window == IntPtr.Zero)
            {
                throw new InvalidOperationException("Could not create the CrashScope tray owner window.");
            }

            (_icon, _ownsIcon) = LoadTrayIcon();
            _trayIconRegistered = TryAddTrayIcon();
            if (!_trayIconRegistered)
            {
                Console.WriteLine("CrashScope tray icon could not be registered yet. The Agent will continue and retry when Explorer recreates the taskbar.");
            }

            _started.TrySetResult();
            while (GetMessageW(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessageW(ref message);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"CrashScope tray is unavailable: {ex.Message}. The Agent will continue normally.");
            _started.TrySetResult();
        }
        finally
        {
            _incidents.ReportAdded -= OnIncidentAdded;
            if (_window != IntPtr.Zero)
            {
                RemoveTrayIcon();
                DestroyWindow(_window);
                _window = IntPtr.Zero;
            }

            if (_ownsIcon && _icon != IntPtr.Zero)
            {
                DestroyIcon(_icon);
            }
            _icon = IntPtr.Zero;
            _ownsIcon = false;

            if (instance != IntPtr.Zero && className is not null)
            {
                UnregisterClassW(className, instance);
            }
            _stopped.TrySetResult();
        }
    }

    private IntPtr WindowProcedure(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam)
    {
        if (_taskbarCreatedMessage != 0 && message == _taskbarCreatedMessage)
        {
            _trayIconRegistered = TryAddTrayIcon();
            if (_trayIconRegistered)
            {
                UpdateToolTip();
            }
            return IntPtr.Zero;
        }

        switch (message)
        {
            case TrayCallbackMessage:
                HandleTrayCallback(window, lParam);
                return IntPtr.Zero;
            case IncidentNotificationMessage:
                ShowPendingIncident();
                return IntPtr.Zero;
            case RefreshMessage:
                UpdateToolTip();
                return IntPtr.Zero;
            case WmClose:
                RemoveTrayIcon();
                DestroyWindow(window);
                return IntPtr.Zero;
            case WmDestroy:
                _window = IntPtr.Zero;
                PostQuitMessage(0);
                return IntPtr.Zero;
            default:
                return DefWindowProcW(window, message, wParam, lParam);
        }
    }

    private void HandleTrayCallback(IntPtr window, IntPtr lParam)
    {
        var notification = (uint)((long)lParam & 0xffff);
        if (notification is NinSelect or NinKeySelect or WmLButtonUp)
        {
            _controls.OpenDashboard();
            return;
        }

        if (notification is WmContextMenu or WmRButtonUp)
        {
            ShowContextMenu(window);
        }
    }

    private void ShowContextMenu(IntPtr window)
    {
        var state = _controls.Snapshot();
        UpdateToolTip(state);

        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;

        try
        {
            AppendMenuW(menu, MfString | MfGrayed, UIntPtr.Zero, state.StatusText);
            AppendMenuW(menu, MfSeparator, UIntPtr.Zero, null);
            AppendMenuW(menu, MfString, new UIntPtr(CommandOpen), "Open dashboard");
            AppendMenuW(
                menu,
                MfString | (state.AutoAssistEnabled ? MfChecked : 0),
                new UIntPtr(CommandAutoAssist),
                state.AutoAssistEnabled ? "Auto Assist" : "Auto Assist (paused)");

            var startupFlags = MfString
                | (state.Startup.Enabled ? MfChecked : 0)
                | (!state.Startup.Supported ? MfGrayed : 0);
            AppendMenuW(menu, startupFlags, new UIntPtr(CommandStartup), "Start with Windows");
            AppendMenuW(menu, MfSeparator, UIntPtr.Zero, null);
            AppendMenuW(menu, MfString, new UIntPtr(CommandExit), "Exit CrashScope");

            GetCursorPos(out var point);
            SetForegroundWindow(window);
            var command = TrackPopupMenuEx(
                menu,
                TpmRightButton | TpmReturnCmd,
                point.X,
                point.Y,
                window,
                IntPtr.Zero);

            switch (command)
            {
                case CommandOpen:
                    _controls.OpenDashboard();
                    break;
                case CommandAutoAssist:
                    _ = ToggleAutoAssistAsync(window);
                    break;
                case CommandStartup:
                    _controls.ToggleStartup();
                    PostMessageW(window, RefreshMessage, UIntPtr.Zero, IntPtr.Zero);
                    break;
                case CommandExit:
                    _controls.Exit();
                    break;
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private async Task ToggleAutoAssistAsync(IntPtr window)
    {
        try
        {
            await _controls.ToggleAutoAssistAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        finally
        {
            PostMessageW(window, RefreshMessage, UIntPtr.Zero, IntPtr.Zero);
        }
    }

    private void OnIncidentAdded(IncidentReport report)
    {
        if (!OperatingSystem.IsWindows()
            || report.Classification == IncidentClassification.UserDiagnosticMarker)
        {
            return;
        }

        lock (_notificationSync)
        {
            _pendingIncident = report;
        }

        var window = _window;
        if (window != IntPtr.Zero)
        {
            PostMessageW(window, IncidentNotificationMessage, UIntPtr.Zero, IntPtr.Zero);
        }
    }

    private void ShowPendingIncident()
    {
        IncidentReport? report;
        lock (_notificationSync)
        {
            report = _pendingIncident;
            _pendingIncident = null;
        }

        if (report is null || _window == IntPtr.Zero || !_trayIconRegistered) return;

        var data = CreateNotifyIconData(NifInfo);
        data.InfoTitle = "CrashScope captured an incident";
        data.Info = Truncate(report.Title, 255);
        data.InfoFlags = NiifInfo | NiifRespectQuietTime;
        ShellNotifyIconW(NimModify, ref data);
    }

    private bool TryAddTrayIcon()
    {
        if (_window == IntPtr.Zero) return false;

        var data = CreateNotifyIconData(NifMessage | NifIcon | NifTip | NifShowTip);
        if (!ShellNotifyIconW(NimAdd, ref data))
        {
            return false;
        }

        data.Version = NotifyIconVersion4;
        ShellNotifyIconW(NimSetVersion, ref data);
        return true;
    }

    private void RemoveTrayIcon()
    {
        if (_window == IntPtr.Zero || !_trayIconRegistered) return;
        var data = CreateNotifyIconData(0);
        ShellNotifyIconW(NimDelete, ref data);
        _trayIconRegistered = false;
    }

    private void UpdateToolTip() => UpdateToolTip(_controls.Snapshot());

    private void UpdateToolTip(TrayDisplayState state)
    {
        if (_window == IntPtr.Zero || !_trayIconRegistered) return;
        var data = CreateNotifyIconData(NifTip | NifShowTip);
        data.Tip = Truncate(state.ToolTip, 127);
        ShellNotifyIconW(NimModify, ref data);
    }

    private NotifyIconData CreateNotifyIconData(uint flags) => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(),
        Window = _window,
        Id = 1,
        Flags = flags,
        CallbackMessage = TrayCallbackMessage,
        Icon = _icon,
        Tip = Truncate(_controls.Snapshot().ToolTip, 127),
        Info = string.Empty,
        InfoTitle = string.Empty
    };

    private static (IntPtr Icon, bool Owned) LoadTrayIcon()
    {
        var executablePath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(executablePath)
            && ExtractIconExW(executablePath, 0, IntPtr.Zero, out var extractedIcon, 1) > 0
            && extractedIcon != IntPtr.Zero)
        {
            return (extractedIcon, true);
        }

        return (LoadIconW(IntPtr.Zero, new IntPtr(IdiApplication)), false);
    }

    private static string Truncate(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum];

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WindowProc(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClassEx
    {
        public uint Size;
        public uint Style;
        public IntPtr WindowProcedure;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr Window;
        public uint Value;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Point;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public IntPtr Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid GuidItem;
        public IntPtr BalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIconW(uint message, ref NotifyIconData data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "ExtractIconExW")]
    private static extern uint ExtractIconExW(
        string file,
        int iconIndex,
        IntPtr largeIcon,
        out IntPtr smallIcon,
        uint iconCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadIconW")]
    private static extern IntPtr LoadIconW(IntPtr instance, IntPtr iconName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
    private static extern IntPtr GetModuleHandleW(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterClassExW")]
    private static extern ushort RegisterClassExW(ref WindowClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "UnregisterClassW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterClassW(string className, IntPtr instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterWindowMessageW")]
    private static extern uint RegisterWindowMessageW(string message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW")]
    private static extern IntPtr CreateWindowExW(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "DefWindowProcW")]
    private static extern IntPtr DefWindowProcW(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMessageW")]
    private static extern int GetMessageW(out Message message, IntPtr window, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "DispatchMessageW")]
    private static extern IntPtr DispatchMessageW(ref Message message);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "AppendMenuW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(IntPtr menu, uint flags, UIntPtr item, string? text);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "TrackPopupMenuEx")]
    private static extern uint TrackPopupMenuEx(
        IntPtr menu,
        uint flags,
        int x,
        int y,
        IntPtr window,
        IntPtr parameters);
}

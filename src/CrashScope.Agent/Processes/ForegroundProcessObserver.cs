using System.Runtime.InteropServices;

namespace CrashScope.Agent.Processes;

internal interface IForegroundProcessObserver
{
    int? GetForegroundProcessId();
}

internal sealed class ForegroundProcessObserver : IForegroundProcessObserver
{
    public int? GetForegroundProcessId()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var window = GetForegroundWindow();
        if (window == IntPtr.Zero)
        {
            return null;
        }

        _ = GetWindowThreadProcessId(window, out var processId);
        return processId is > 0 and <= int.MaxValue
            ? (int)processId
            : null;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}

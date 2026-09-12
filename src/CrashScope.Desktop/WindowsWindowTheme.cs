using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CrashScope.Desktop;

internal static class WindowsWindowTheme
{
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    public static bool TryApplyDarkChrome(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var handle = new WindowInteropHelper(window).Handle;
        return TryApplyDarkChrome(handle);
    }

    internal static bool TryApplyDarkChrome(IntPtr handle)
    {
        if (!OperatingSystem.IsWindows() || handle == IntPtr.Zero)
        {
            return false;
        }

        var enabled = 1;
        var darkResult = DwmSetWindowAttribute(
            handle,
            DwmwaUseImmersiveDarkMode,
            ref enabled,
            sizeof(int));

        if (darkResult != 0)
        {
            darkResult = DwmSetWindowAttribute(
                handle,
                DwmwaUseImmersiveDarkModeBefore20H1,
                ref enabled,
                sizeof(int));
        }

        var caption = ToColorRef(9, 13, 18);
        _ = DwmSetWindowAttribute(
            handle,
            DwmwaCaptionColor,
            ref caption,
            sizeof(int));

        var text = ToColorRef(238, 245, 247);
        _ = DwmSetWindowAttribute(
            handle,
            DwmwaTextColor,
            ref text,
            sizeof(int));

        var border = ToColorRef(35, 48, 61);
        _ = DwmSetWindowAttribute(
            handle,
            DwmwaBorderColor,
            ref border,
            sizeof(int));

        return darkResult == 0;
    }

    internal static int ToColorRef(byte red, byte green, byte blue) =>
        red | (green << 8) | (blue << 16);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int value,
        int valueSize);
}
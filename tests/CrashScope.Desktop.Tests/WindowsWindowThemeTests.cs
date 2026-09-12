using CrashScope.Desktop;

namespace CrashScope.Desktop.Tests;

public sealed class WindowsWindowThemeTests
{
    [Fact]
    public void ToColorRef_UsesWindowsBgrPacking()
    {
        var value = WindowsWindowTheme.ToColorRef(
            red: 0x11,
            green: 0x22,
            blue: 0x33);

        Assert.Equal(0x00332211, value);
    }

    [Fact]
    public void TryApplyDarkChrome_RejectsZeroWindowHandle()
    {
        Assert.False(WindowsWindowTheme.TryApplyDarkChrome(IntPtr.Zero));
    }
}
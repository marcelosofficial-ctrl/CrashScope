using CrashScope.Agent.Runtime;

namespace CrashScope.Agent.Tests;

public sealed class WindowsStartupRegistrationTests
{
    [Fact]
    public void BuildCommand_QuotesExecutableAndUsesNoBrowser()
    {
        var path = Path.Combine(Path.GetTempPath(), "Crash Scope", "CrashScope.exe");

        var command = WindowsStartupRegistration.BuildCommand(path);

        Assert.Equal($"\"{Path.GetFullPath(path)}\" --no-browser", command);
    }

    [Fact]
    public void SetEnabled_WritesExpectedCurrentUserCommand()
    {
        var store = new FakeStartupStore();
        var path = Path.Combine(Path.GetTempPath(), "CrashScope.exe");
        var registration = new WindowsStartupRegistration(path, store);

        var status = registration.SetEnabled(true);

        Assert.True(status.Supported);
        Assert.True(status.Enabled);
        Assert.Equal(
            WindowsStartupRegistration.BuildCommand(path),
            store.Values[WindowsStartupRegistration.ValueName]);
    }

    [Fact]
    public void SetEnabledFalse_RemovesOnlyCrashScopeValue()
    {
        var store = new FakeStartupStore();
        store.Values[WindowsStartupRegistration.ValueName] = "old";
        store.Values["OtherApp"] = "do-not-touch";
        var registration = new WindowsStartupRegistration(
            Path.Combine(Path.GetTempPath(), "CrashScope.exe"),
            store);

        var status = registration.SetEnabled(false);

        Assert.True(status.Supported);
        Assert.False(status.Enabled);
        Assert.False(store.Values.ContainsKey(WindowsStartupRegistration.ValueName));
        Assert.Equal("do-not-touch", store.Values["OtherApp"]);
    }

    [Fact]
    public void GetStatus_DifferentExistingCommandIsNotTreatedAsEnabled()
    {
        var store = new FakeStartupStore();
        store.Values[WindowsStartupRegistration.ValueName] = "\"C:\\Old\\CrashScope.exe\" --no-browser";
        var registration = new WindowsStartupRegistration(
            Path.Combine(Path.GetTempPath(), "CrashScope.exe"),
            store);

        var status = registration.GetStatus();

        Assert.True(status.Supported);
        Assert.False(status.Enabled);
        Assert.Contains("different CrashScope startup command", status.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SetEnabled_GracefullyReportsAccessFailure()
    {
        var registration = new WindowsStartupRegistration(
            Path.Combine(Path.GetTempPath(), "CrashScope.exe"),
            new ThrowingStartupStore());

        var status = registration.SetEnabled(true);

        Assert.True(status.Supported);
        Assert.False(status.Enabled);
        Assert.Contains("did not allow", status.Detail, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeStartupStore : IUserStartupStore
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

        public string? Read(string name) =>
            Values.TryGetValue(name, out var value) ? value : null;

        public void Write(string name, string command) =>
            Values[name] = command;

        public void Delete(string name) =>
            Values.Remove(name);
    }

    private sealed class ThrowingStartupStore : IUserStartupStore
    {
        public string? Read(string name) => throw new UnauthorizedAccessException();
        public void Write(string name, string command) => throw new UnauthorizedAccessException();
        public void Delete(string name) => throw new UnauthorizedAccessException();
    }
}

using CrashScope.Agent.Settings;

namespace CrashScope.Agent.Tests;

public sealed class CrashScopeConfigTraceSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "CrashScope-configtrace-settings-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Version2MigratesToVersion3WithConfigTraceOff()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            SettingsPath,
            """
            {"schemaVersion":2,"autoAssistEnabled":false,"retentionDays":90}
            """);

        var snapshot = CreateState().Snapshot();

        Assert.Equal(3, snapshot.SchemaVersion);
        Assert.False(snapshot.AutoAssistEnabled);
        Assert.Equal(90, snapshot.RetentionDays);
        Assert.False(snapshot.ConfigTraceEnabled);
        Assert.Null(snapshot.ConfigTraceRootPath);
    }

    [Fact]
    public async Task ConfigTraceRootAndEnabledStatePersistAcrossReload()
    {
        var configRoot = Path.Combine(Path.GetDirectoryName(_root)!, Path.GetFileName(_root) + "-game-config");
        Directory.CreateDirectory(configRoot);

        var state = CreateState();
        var rooted = await state.UpdateConfigTraceRootAsync(configRoot);
        var enabled = await state.UpdateConfigTraceEnabledAsync(true);
        var reloaded = CreateState().Snapshot();

        Assert.Equal(Path.GetFullPath(configRoot), rooted.ConfigTraceRootPath);
        Assert.False(rooted.ConfigTraceEnabled);
        Assert.True(enabled.ConfigTraceEnabled);
        Assert.True(reloaded.ConfigTraceEnabled);
        Assert.Equal(Path.GetFullPath(configRoot), reloaded.ConfigTraceRootPath);
    }

    [Fact]
    public async Task EnablingWithoutConfiguredRootIsRejected()
    {
        var state = CreateState();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await state.UpdateConfigTraceEnabledAsync(true);
        });

        Assert.False(state.Snapshot().ConfigTraceEnabled);
    }

    [Fact]
    public async Task ClearingRootAlsoDisablesConfigTrace()
    {
        var configRoot = Path.Combine(Path.GetDirectoryName(_root)!, Path.GetFileName(_root) + "-game-config");
        Directory.CreateDirectory(configRoot);

        var state = CreateState();
        await state.UpdateConfigTraceRootAsync(configRoot);
        await state.UpdateConfigTraceEnabledAsync(true);

        var cleared = await state.UpdateConfigTraceRootAsync(null);

        Assert.False(cleared.ConfigTraceEnabled);
        Assert.Null(cleared.ConfigTraceRootPath);
    }

    [Fact]
    public async Task RelativeAndMissingRootsAreRejected()
    {
        var state = CreateState();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await state.UpdateConfigTraceRootAsync("relative-config");
        });

        var missing = Path.Combine(_root, "missing-config");
        await Assert.ThrowsAsync<DirectoryNotFoundException>(async () =>
        {
            await state.UpdateConfigTraceRootAsync(missing);
        });
    }

    [Fact]
    public async Task CrashScopeDataDirectoryCannotBeWatchedByConfigTrace()
    {
        Directory.CreateDirectory(_root);
        var state = CreateState();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await state.UpdateConfigTraceRootAsync(_root);
        });
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root))
        {
            return;
        }

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private string SettingsPath => Path.Combine(_root, "settings.json");

    private CrashScopeSettingsState CreateState() => new(SettingsPath);
}
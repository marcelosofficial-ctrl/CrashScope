using CrashScope.Agent.Settings;

namespace CrashScope.Agent.Tests;

public sealed class CrashScopeSettingsStateTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "CrashScope-settings-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void MissingFile_UsesSafeDefaults()
    {
        var state = CreateState();
        var snapshot = state.Snapshot();

        Assert.Equal(CrashScopeSettings.CurrentSchemaVersion, snapshot.SchemaVersion);
        Assert.True(snapshot.AutoAssistEnabled);
        Assert.Equal(CrashScopeSettings.DefaultRetentionDays, snapshot.RetentionDays);
    }

    [Fact]
    public void Version1_MigratesInMemoryAndPreservesAutoAssist()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(SettingsPath, "{\"schemaVersion\":1,\"autoAssistEnabled\":false}");

        var snapshot = CreateState().Snapshot();

        Assert.Equal(CrashScopeSettings.CurrentSchemaVersion, snapshot.SchemaVersion);
        Assert.False(snapshot.AutoAssistEnabled);
        Assert.Equal(CrashScopeSettings.DefaultRetentionDays, snapshot.RetentionDays);
    }

    [Fact]
    public async Task UpdateAutoAssistAsync_PersistsAcrossReload()
    {
        var state = CreateState();
        var updated = await state.UpdateAutoAssistAsync(false);
        var reloaded = CreateState();

        Assert.False(updated.AutoAssistEnabled);
        Assert.False(reloaded.Snapshot().AutoAssistEnabled);
        Assert.Equal(CrashScopeSettings.DefaultRetentionDays, reloaded.Snapshot().RetentionDays);
        Assert.Single(Directory.GetFiles(_root, "settings.json"));
        Assert.Empty(Directory.GetFiles(_root, ".settings.json.*.tmp"));
    }

    [Fact]
    public async Task UpdateRetentionDaysAsync_PersistsAcrossReload()
    {
        var state = CreateState();
        var updated = await state.UpdateRetentionDaysAsync(90);

        Assert.Equal(90, updated.RetentionDays);
        Assert.Equal(90, CreateState().Snapshot().RetentionDays);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(366)]
    public async Task UpdateRetentionDaysAsync_RejectsUnsafeRange(int days)
    {
        var state = CreateState();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            await state.UpdateRetentionDaysAsync(days);
        });
    }

    [Fact]
    public void MalformedJson_FallsBackToDefaults()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(SettingsPath, "{ definitely-not-json");

        var snapshot = CreateState().Snapshot();

        Assert.True(snapshot.AutoAssistEnabled);
        Assert.Equal(CrashScopeSettings.DefaultRetentionDays, snapshot.RetentionDays);
        Assert.Equal(CrashScopeSettings.CurrentSchemaVersion, snapshot.SchemaVersion);
    }

    [Fact]
    public void UnknownSchemaVersion_FallsBackToDefaults()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(SettingsPath, "{\"schemaVersion\":999,\"autoAssistEnabled\":false,\"retentionDays\":90}");

        var snapshot = CreateState().Snapshot();

        Assert.True(snapshot.AutoAssistEnabled);
        Assert.Equal(CrashScopeSettings.DefaultRetentionDays, snapshot.RetentionDays);
        Assert.Equal(CrashScopeSettings.CurrentSchemaVersion, snapshot.SchemaVersion);
    }

    [Fact]
    public async Task ConcurrentUpdates_DoNotLeaveTemporaryFilesOrInvalidJson()
    {
        var state = CreateState();
        var writes = Enumerable.Range(0, 20)
            .Select(index => index % 2 == 0
                ? state.UpdateAutoAssistAsync(index % 4 == 0).AsTask()
                : state.UpdateRetentionDaysAsync(30 + index).AsTask())
            .ToArray();
        await Task.WhenAll(writes);

        var snapshot = CreateState().Snapshot();
        Assert.Equal(CrashScopeSettings.CurrentSchemaVersion, snapshot.SchemaVersion);
        Assert.InRange(snapshot.RetentionDays, CrashScopeSettings.MinimumRetentionDays, CrashScopeSettings.MaximumRetentionDays);
        Assert.Empty(Directory.GetFiles(_root, ".settings.json.*.tmp"));
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string SettingsPath => Path.Combine(_root, "settings.json");
    private CrashScopeSettingsState CreateState() => new(SettingsPath);
}

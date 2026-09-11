using System.Text.Json;

namespace CrashScope.Agent.Settings;

internal sealed record CrashScopeSettings(
    int SchemaVersion,
    bool AutoAssistEnabled,
    int RetentionDays)
{
    public const int CurrentSchemaVersion = 2;
    public const int DefaultRetentionDays = 30;
    public const int MinimumRetentionDays = 1;
    public const int MaximumRetentionDays = 365;

    public static CrashScopeSettings Default { get; } = new(
        CurrentSchemaVersion,
        AutoAssistEnabled: true,
        RetentionDays: DefaultRetentionDays);
}

internal sealed class CrashScopeSettingsState
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private CrashScopeSettings _current;

    public CrashScopeSettingsState(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        _settingsPath = Path.GetFullPath(settingsPath);
        _current = LoadOrDefault(_settingsPath);
    }

    public string SettingsPath => _settingsPath;

    public CrashScopeSettings Snapshot()
    {
        lock (_sync)
        {
            return _current;
        }
    }

    public ValueTask<CrashScopeSettings> UpdateAutoAssistAsync(
        bool enabled,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(current => current with { AutoAssistEnabled = enabled }, cancellationToken);

    public ValueTask<CrashScopeSettings> UpdateRetentionDaysAsync(
        int retentionDays,
        CancellationToken cancellationToken = default)
    {
        if (retentionDays is < CrashScopeSettings.MinimumRetentionDays or > CrashScopeSettings.MaximumRetentionDays)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retentionDays),
                $"Retention must be between {CrashScopeSettings.MinimumRetentionDays} and {CrashScopeSettings.MaximumRetentionDays} days.");
        }

        return UpdateAsync(current => current with { RetentionDays = retentionDays }, cancellationToken);
    }

    private async ValueTask<CrashScopeSettings> UpdateAsync(
        Func<CrashScopeSettings, CrashScopeSettings> update,
        CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CrashScopeSettings updated;
            lock (_sync)
            {
                updated = update(_current) with
                {
                    SchemaVersion = CrashScopeSettings.CurrentSchemaVersion
                };
            }

            await SaveAtomicAsync(updated, cancellationToken).ConfigureAwait(false);

            lock (_sync)
            {
                _current = updated;
                return _current;
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async ValueTask SaveAtomicAsync(
        CrashScopeSettings settings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var directory = Path.GetDirectoryName(_settingsPath)
            ?? throw new InvalidOperationException("Settings path does not have a parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(_settingsPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    settings,
                    SerializerOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private static CrashScopeSettings LoadOrDefault(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return CrashScopeSettings.Default;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (!root.TryGetProperty("schemaVersion", out var versionElement)
                || !versionElement.TryGetInt32(out var version))
            {
                return CrashScopeSettings.Default;
            }

            var autoAssist = root.TryGetProperty("autoAssistEnabled", out var autoAssistElement)
                && autoAssistElement.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? autoAssistElement.GetBoolean()
                    : CrashScopeSettings.Default.AutoAssistEnabled;

            if (version == 1)
            {
                return new CrashScopeSettings(
                    CrashScopeSettings.CurrentSchemaVersion,
                    autoAssist,
                    CrashScopeSettings.DefaultRetentionDays);
            }

            if (version != CrashScopeSettings.CurrentSchemaVersion)
            {
                return CrashScopeSettings.Default;
            }

            var retentionDays = root.TryGetProperty("retentionDays", out var retentionElement)
                && retentionElement.TryGetInt32(out var parsedRetention)
                && parsedRetention is >= CrashScopeSettings.MinimumRetentionDays and <= CrashScopeSettings.MaximumRetentionDays
                    ? parsedRetention
                    : CrashScopeSettings.DefaultRetentionDays;

            return new CrashScopeSettings(
                CrashScopeSettings.CurrentSchemaVersion,
                autoAssist,
                retentionDays);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return CrashScopeSettings.Default;
        }
    }
}

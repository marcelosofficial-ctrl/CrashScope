using System.Text.Json;

namespace CrashScope.Agent.Settings;

internal sealed record CrashScopeSettings(
    int SchemaVersion,
    bool AutoAssistEnabled,
    int RetentionDays,
    bool ConfigTraceEnabled,
    string? ConfigTraceRootPath)
{
    public const int CurrentSchemaVersion = 3;
    public const int DefaultRetentionDays = 30;
    public const int MinimumRetentionDays = 1;
    public const int MaximumRetentionDays = 365;

    public static CrashScopeSettings Default { get; } = new(
        CurrentSchemaVersion,
        AutoAssistEnabled: true,
        RetentionDays: DefaultRetentionDays,
        ConfigTraceEnabled: false,
        ConfigTraceRootPath: null);
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

    public ValueTask<CrashScopeSettings> UpdateConfigTraceRootAsync(
        string? rootPath,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeConfigTraceRoot(rootPath, requireExisting: true);

        if (normalized is not null && OverlapsCrashScopeDataRoot(normalized))
        {
            throw new ArgumentException(
                "ConfigTrace root must not contain or live inside CrashScope's own local data directory.",
                nameof(rootPath));
        }

        return UpdateAsync(
            current => current with
            {
                ConfigTraceRootPath = normalized,
                ConfigTraceEnabled = normalized is not null && current.ConfigTraceEnabled
            },
            cancellationToken);
    }

    public ValueTask<CrashScopeSettings> UpdateConfigTraceEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(
            current =>
            {
                if (!enabled)
                {
                    return current with { ConfigTraceEnabled = false };
                }

                if (string.IsNullOrWhiteSpace(current.ConfigTraceRootPath))
                {
                    throw new InvalidOperationException(
                        "Configure an existing ConfigTrace root directory before enabling ConfigTrace.");
                }

                if (!Directory.Exists(current.ConfigTraceRootPath))
                {
                    throw new DirectoryNotFoundException(
                        $"Configured ConfigTrace root directory was not found: {current.ConfigTraceRootPath}");
                }

                return current with { ConfigTraceEnabled = true };
            },
            cancellationToken);

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

    private bool OverlapsCrashScopeDataRoot(string configTraceRoot)
    {
        var productDataRoot = Path.GetDirectoryName(_settingsPath)
            ?? throw new InvalidOperationException("Settings path does not have a parent directory.");

        return IsSameOrAncestor(configTraceRoot, productDataRoot) ||
            IsSameOrAncestor(productDataRoot, configTraceRoot);
    }

    private static bool IsSameOrAncestor(string ancestorPath, string candidatePath)
    {
        var ancestor = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ancestorPath));
        var candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidatePath));

        if (string.Equals(ancestor, candidate, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return candidate.StartsWith(
            ancestor + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeConfigTraceRoot(
        string? rootPath,
        bool requireExisting)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return null;
        }

        var trimmed = rootPath.Trim();
        if (!Path.IsPathFullyQualified(trimmed))
        {
            if (requireExisting)
            {
                throw new ArgumentException(
                    "ConfigTrace root must be an absolute path.",
                    nameof(rootPath));
            }

            return null;
        }

        string normalized;
        try
        {
            normalized = Path.GetFullPath(trimmed);
        }
        catch (Exception ex) when (
            ex is ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            if (requireExisting)
            {
                throw new ArgumentException(
                    "ConfigTrace root path is invalid.",
                    nameof(rootPath),
                    ex);
            }

            return null;
        }

        if (requireExisting && !Directory.Exists(normalized))
        {
            throw new DirectoryNotFoundException(
                $"ConfigTrace root directory was not found: {normalized}");
        }

        return normalized;
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
                    CrashScopeSettings.DefaultRetentionDays,
                    ConfigTraceEnabled: false,
                    ConfigTraceRootPath: null);
            }

            var retentionDays = root.TryGetProperty("retentionDays", out var retentionElement)
                && retentionElement.TryGetInt32(out var parsedRetention)
                && parsedRetention is >= CrashScopeSettings.MinimumRetentionDays and <= CrashScopeSettings.MaximumRetentionDays
                    ? parsedRetention
                    : CrashScopeSettings.DefaultRetentionDays;

            if (version == 2)
            {
                return new CrashScopeSettings(
                    CrashScopeSettings.CurrentSchemaVersion,
                    autoAssist,
                    retentionDays,
                    ConfigTraceEnabled: false,
                    ConfigTraceRootPath: null);
            }

            if (version != CrashScopeSettings.CurrentSchemaVersion)
            {
                return CrashScopeSettings.Default;
            }

            var configuredRoot = root.TryGetProperty("configTraceRootPath", out var rootElement)
                && rootElement.ValueKind == JsonValueKind.String
                    ? NormalizeConfigTraceRoot(rootElement.GetString(), requireExisting: false)
                    : null;

            var configTraceEnabled = configuredRoot is not null
                && root.TryGetProperty("configTraceEnabled", out var enabledElement)
                && enabledElement.ValueKind is JsonValueKind.True or JsonValueKind.False
                && enabledElement.GetBoolean();

            return new CrashScopeSettings(
                CrashScopeSettings.CurrentSchemaVersion,
                autoAssist,
                retentionDays,
                configTraceEnabled,
                configuredRoot);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return CrashScopeSettings.Default;
        }
    }
}
using System.Diagnostics.Eventing.Reader;
using System.Runtime.Versioning;
using CrashScope.Core.Diagnostics;

#pragma warning disable CA1416 // This implementation is Windows-only by design; public entry points runtime-guard OperatingSystem.IsWindows().

namespace CrashScope.Infrastructure.Diagnostics;

public sealed class WindowsEventLogDiagnosticSource : IRealtimeDiagnosticEventSource, IDisposable
{
    private const int RingCapacity = 2048;

    private static readonly LogDefinition[] Logs =
    {
        new(
            "Application",
            new[]
            {
                "Application Error",
                "Windows Error Reporting",
                "Application Hang"
            }),
        new(
            "System",
            new[]
            {
                "Microsoft-Windows-WHEA-Logger",
                "Microsoft-Windows-Kernel-Power",
                "Display"
            })
    };

    private readonly object _sync = new();
    private readonly List<DiagnosticEvent> _ring = new(RingCapacity);
    private readonly HashSet<string> _ringKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<EventLogWatcher> _watchers = new();
    private readonly SemaphoreSlim _eventSignal = new(0, 1);
    private readonly SemaphoreSlim _reconcileGate = new(1, 1);
    private bool _startAttempted;
    private bool _realtimeAvailable;
    private bool _seeded;
    private bool _disposed;

    public string Name => "WindowsEventLog";

    public bool RealtimeAvailable
    {
        get
        {
            EnsureRealtimeStarted();
            lock (_sync)
            {
                return _realtimeAvailable;
            }
        }
    }

    public async ValueTask<IReadOnlyList<DiagnosticEvent>> ReadSinceAsync(
        DateTimeOffset sinceUtc,
        int maximumEvents = 256,
        CancellationToken cancellationToken = default)
    {
        ValidateReadArguments(sinceUtc, maximumEvents, cancellationToken);
        EnsureWindows();
        EnsureRealtimeStarted();

        bool realtimeAvailable;
        bool needsSeed;
        lock (_sync)
        {
            ThrowIfDisposed();
            realtimeAvailable = _realtimeAvailable;
            needsSeed = !_seeded;
        }

        if (!realtimeAvailable)
        {
            return ReadHistory(sinceUtc, maximumEvents, cancellationToken);
        }

        if (needsSeed)
        {
            await ReconcileAsync(sinceUtc, Math.Max(maximumEvents, 512), cancellationToken)
                .ConfigureAwait(false);
        }

        lock (_sync)
        {
            return _ring
                .Where(x => x.ObservedAtUtc >= sinceUtc)
                .OrderByDescending(x => x.ObservedAtUtc)
                .ThenByDescending(x => x.RecordId ?? -1)
                .Take(maximumEvents)
                .OrderBy(x => x.ObservedAtUtc)
                .ThenBy(x => x.RecordId ?? -1)
                .ToArray();
        }
    }

    public async ValueTask WaitForEventAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureWindows();
        EnsureRealtimeStarted();

        if (!RealtimeAvailable)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await _eventSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask ReconcileAsync(
        DateTimeOffset sinceUtc,
        int maximumEvents = 512,
        CancellationToken cancellationToken = default)
    {
        ValidateReadArguments(sinceUtc, maximumEvents, cancellationToken);
        EnsureWindows();

        await _reconcileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var history = ReadHistory(sinceUtc, maximumEvents, cancellationToken);
            foreach (var item in history)
            {
                AddToRing(item, signal: false);
            }

            lock (_sync)
            {
                _seeded = true;
            }
        }
        finally
        {
            _reconcileGate.Release();
        }
    }

    public void Dispose()
    {
        List<EventLogWatcher> watchers;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _realtimeAvailable = false;
            watchers = _watchers.ToList();
            _watchers.Clear();
        }

        foreach (var watcher in watchers)
        {
            try
            {
                watcher.Enabled = false;
                watcher.EventRecordWritten -= OnEventRecordWritten;
            }
            catch (EventLogException)
            {
            }
            finally
            {
                watcher.Dispose();
            }
        }

        _eventSignal.Dispose();
        _reconcileGate.Dispose();
    }

    private void EnsureRealtimeStarted()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_startAttempted)
            {
                return;
            }

            _startAttempted = true;
        }

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var created = new List<EventLogWatcher>();
        try
        {
            foreach (var log in Logs)
            {
                var query = new EventLogQuery(
                    log.LogName,
                    PathType.LogName,
                    BuildProviderQuery(log.Providers))
                {
                    ReverseDirection = false,
                    TolerateQueryErrors = false
                };

                var watcher = new EventLogWatcher(query, bookmark: null, readExistingEvents: false);
                watcher.EventRecordWritten += OnEventRecordWritten;
                watcher.Enabled = true;
                created.Add(watcher);
            }

            lock (_sync)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(WindowsEventLogDiagnosticSource));
                }

                _watchers.AddRange(created);
                _realtimeAvailable = true;
            }
        }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException or InvalidOperationException)
        {
            foreach (var watcher in created)
            {
                try
                {
                    watcher.Enabled = false;
                    watcher.EventRecordWritten -= OnEventRecordWritten;
                }
                catch (EventLogException)
                {
                }
                finally
                {
                    watcher.Dispose();
                }
            }

            lock (_sync)
            {
                _realtimeAvailable = false;
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private void OnEventRecordWritten(object? sender, EventRecordWrittenEventArgs args)
    {
        if (args.EventException is not null || args.EventRecord is null)
        {
            lock (_sync)
            {
                _realtimeAvailable = false;
            }
            return;
        }

        using var record = args.EventRecord;
        if (!record.TimeCreated.HasValue)
        {
            return;
        }

        try
        {
            var logName = record.LogName;
            if (string.IsNullOrWhiteSpace(logName))
            {
                logName = "Unknown";
            }

            var snapshot = SnapshotRecord(logName ?? "Unknown", record);
            AddToRing(WindowsEventLogMapper.Map(snapshot), signal: true);
        }
        catch (Exception ex) when (ex is EventLogException or InvalidOperationException)
        {
            // A single malformed/inaccessible event must not terminate monitoring.
        }
    }

    private void AddToRing(DiagnosticEvent item, bool signal)
    {
        var key = BuildIdentity(item);
        var added = false;

        lock (_sync)
        {
            if (_disposed || !_ringKeys.Add(key))
            {
                return;
            }

            _ring.Add(item);
            added = true;

            while (_ring.Count > RingCapacity)
            {
                var oldestIndex = 0;
                for (var index = 1; index < _ring.Count; index++)
                {
                    if (_ring[index].ObservedAtUtc < _ring[oldestIndex].ObservedAtUtc)
                    {
                        oldestIndex = index;
                    }
                }

                var removed = _ring[oldestIndex];
                _ring.RemoveAt(oldestIndex);
                _ringKeys.Remove(BuildIdentity(removed));
            }
        }

        if (added && signal && _eventSignal.CurrentCount == 0)
        {
            try
            {
                _eventSignal.Release();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private static string BuildIdentity(DiagnosticEvent item) =>
        item.RecordId.HasValue
            ? $"{item.LogName}|{item.ProviderName}|{item.RecordId.Value}"
            : $"{item.LogName}|{item.ProviderName}|{item.EventId}|{item.ObservedAtUtc.UtcTicks}";

    private static IReadOnlyList<DiagnosticEvent> ReadHistory(
        DateTimeOffset sinceUtc,
        int maximumEvents,
        CancellationToken cancellationToken)
    {
        var events = new List<DiagnosticEvent>(maximumEvents * Logs.Length);

        foreach (var log in Logs)
        {
            ReadLog(log, sinceUtc, maximumEvents, events, cancellationToken);
        }

        return events
            .OrderByDescending(x => x.ObservedAtUtc)
            .ThenByDescending(x => x.RecordId ?? -1)
            .Take(maximumEvents)
            .OrderBy(x => x.ObservedAtUtc)
            .ThenBy(x => x.RecordId ?? -1)
            .ToArray();
    }

    [SupportedOSPlatform("windows")]
    private static void ReadLog(
        LogDefinition log,
        DateTimeOffset sinceUtc,
        int maximumEvents,
        ICollection<DiagnosticEvent> destination,
        CancellationToken cancellationToken)
    {
        var query = new EventLogQuery(
            log.LogName,
            PathType.LogName,
            BuildProviderQuery(log.Providers))
        {
            ReverseDirection = true,
            TolerateQueryErrors = false
        };

        using var reader = new EventLogReader(query);

        for (var count = 0; count < maximumEvents; count++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var record = reader.ReadEvent();
            if (record is null)
            {
                break;
            }

            if (!record.TimeCreated.HasValue)
            {
                continue;
            }

            var observedAtUtc = new DateTimeOffset(record.TimeCreated.Value.ToUniversalTime());
            if (observedAtUtc < sinceUtc)
            {
                break;
            }

            destination.Add(WindowsEventLogMapper.Map(SnapshotRecord(log.LogName, record)));
        }
    }

    [SupportedOSPlatform("windows")]
    private static WindowsEventRecordSnapshot SnapshotRecord(
        string logName,
        EventRecord record) =>
        new(
            logName,
            record.ProviderName ?? "Unknown",
            record.Id,
            record.RecordId,
            new DateTimeOffset(record.TimeCreated!.Value.ToUniversalTime()),
            TryGetLevel(record),
            TryFormatDescription(record));

    internal static string BuildProviderQuery(IReadOnlyList<string> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        if (providers.Count == 0)
        {
            throw new ArgumentException(
                "At least one Event Log provider is required.",
                nameof(providers));
        }

        var providerExpression = string.Join(
            " or ",
            providers.Select(provider =>
                $"Provider[@Name='{EscapeXPathLiteral(provider)}']"));

        return $"*[System[({providerExpression})]]";
    }

    private static string EscapeXPathLiteral(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value.Contains('\''))
        {
            throw new ArgumentException(
                "Event Log provider names containing apostrophes are not supported.",
                nameof(value));
        }

        return value;
    }

    [SupportedOSPlatform("windows")]
    private static string? TryGetLevel(EventRecord record)
    {
        try
        {
            return record.LevelDisplayName;
        }
        catch (EventLogException)
        {
            return record.Level?.ToString();
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? TryFormatDescription(EventRecord record)
    {
        try
        {
            return record.FormatDescription();
        }
        catch (EventLogException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static void ValidateReadArguments(
        DateTimeOffset sinceUtc,
        int maximumEvents,
        CancellationToken cancellationToken)
    {
        if (sinceUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Diagnostic event query time must be UTC.",
                nameof(sinceUtc));
        }

        if (maximumEvents <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumEvents));
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Windows Event Log diagnostics require Windows.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed record LogDefinition(
        string LogName,
        IReadOnlyList<string> Providers);
}

internal sealed record WindowsEventRecordSnapshot(
    string LogName,
    string ProviderName,
    int EventId,
    long? RecordId,
    DateTimeOffset ObservedAtUtc,
    string? Level,
    string? Message);

internal static class WindowsEventLogMapper
{
    public static DiagnosticEvent Map(WindowsEventRecordSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new DiagnosticEvent(
            "WindowsEventLog",
            snapshot.LogName,
            snapshot.ProviderName,
            snapshot.EventId,
            snapshot.RecordId,
            snapshot.ObservedAtUtc,
            sourceOccurredAtUtc: null,
            MapKind(snapshot.ProviderName),
            snapshot.Level,
            snapshot.Message);
    }

    internal static DiagnosticEventKind MapKind(string providerName) =>
        providerName switch
        {
            "Application Error" => DiagnosticEventKind.ApplicationFault,
            "Application Hang" => DiagnosticEventKind.ApplicationHang,
            "Windows Error Reporting" => DiagnosticEventKind.WindowsErrorReport,
            "Microsoft-Windows-WHEA-Logger" => DiagnosticEventKind.HardwareError,
            "Microsoft-Windows-Kernel-Power" => DiagnosticEventKind.KernelPower,
            "Display" => DiagnosticEventKind.DisplayDriver,
            _ => DiagnosticEventKind.Other
        };
}

#pragma warning restore CA1416

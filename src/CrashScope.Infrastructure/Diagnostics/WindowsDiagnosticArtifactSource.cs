using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CrashScope.Core.Diagnostics;

namespace CrashScope.Infrastructure.Diagnostics;

public sealed partial class WindowsDiagnosticArtifactSource : IDiagnosticArtifactSource
{
    private readonly IReadOnlyList<string> _werRoots;
    private readonly string _liveKernelRoot;
    private readonly IDiagnosticEventSource? _eventFallbackSource;

    public WindowsDiagnosticArtifactSource(
        IEnumerable<string>? werRoots = null,
        string? liveKernelRoot = null,
        IDiagnosticEventSource? eventFallbackSource = null)
    {
        var commonData = Environment.GetFolderPath(
            Environment.SpecialFolder.CommonApplicationData);
        var systemRoot = Environment.GetEnvironmentVariable("SystemRoot")
            ?? @"C:\Windows";
        var usingDefaultLocations = werRoots is null && liveKernelRoot is null;

        _werRoots = (werRoots ?? new[]
        {
            Path.Combine(commonData, "Microsoft", "Windows", "WER", "ReportArchive"),
            Path.Combine(commonData, "Microsoft", "Windows", "WER", "ReportQueue")
        }).ToArray();

        _liveKernelRoot = liveKernelRoot
            ?? Path.Combine(systemRoot, "LiveKernelReports");

        _eventFallbackSource = eventFallbackSource
            ?? (usingDefaultLocations && OperatingSystem.IsWindows()
                ? new WindowsEventLogDiagnosticSource()
                : null);
    }

    public string Name => "WindowsDiagnosticArtifacts";

    public async ValueTask<IReadOnlyList<DiagnosticArtifact>> ReadSinceAsync(
        DateTimeOffset sinceUtc,
        int maximumArtifacts = 256,
        CancellationToken cancellationToken = default)
    {
        if (sinceUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Diagnostic artifact query time must be UTC.",
                nameof(sinceUtc));
        }

        if (maximumArtifacts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumArtifacts));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var artifacts = new List<DiagnosticArtifact>();

        foreach (var root in _werRoots)
        {
            ReadWerRoot(root, sinceUtc, artifacts, cancellationToken);
        }

        ReadLiveKernelRoot(
            _liveKernelRoot,
            sinceUtc,
            artifacts,
            cancellationToken);

        if (_eventFallbackSource is not null)
        {
            await ReadEventLogFallbackAsync(
                _eventFallbackSource,
                sinceUtc,
                maximumArtifacts,
                artifacts,
                cancellationToken);
        }

        IReadOnlyList<DiagnosticArtifact> result = artifacts
            .DistinctBy(BuildArtifactIdentity, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(x => x.ObservedAtUtc)
            .Take(maximumArtifacts)
            .OrderBy(x => x.ObservedAtUtc)
            .ToArray();

        return result;
    }

    private static async ValueTask ReadEventLogFallbackAsync(
        IDiagnosticEventSource eventSource,
        DateTimeOffset sinceUtc,
        int maximumArtifacts,
        ICollection<DiagnosticArtifact> destination,
        CancellationToken cancellationToken)
    {
        var maximumEvents = maximumArtifacts >= 1024
            ? 4096
            : Math.Max(256, maximumArtifacts * 4);

        IReadOnlyList<DiagnosticEvent> events;
        try
        {
            events = await eventSource.ReadSinceAsync(
                sinceUtc,
                maximumEvents,
                cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }
        catch (PlatformNotSupportedException)
        {
            return;
        }

        foreach (var diagnosticEvent in events)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var artifact = MapWerEventArtifact(diagnosticEvent);
            if (artifact is not null)
            {
                destination.Add(artifact);
            }
        }
    }

    internal static DiagnosticArtifact? MapWerEventArtifact(
        DiagnosticEvent diagnosticEvent)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);

        if (diagnosticEvent.Kind != DiagnosticEventKind.WindowsErrorReport &&
            !diagnosticEvent.ProviderName.Equals(
                "Windows Error Reporting",
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var message = diagnosticEvent.Message;
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var reportId = MatchValue(ReportIdRegex(), message);
        var eventType = MatchValue(EventNameRegex(), message);
        var dumpPath = MatchValue(DumpPathRegex(), message);
        var reportPath = MatchValue(ReportLocationRegex(), message);
        var signature = CreateMessageSignature(message, eventType);

        if (reportId is null &&
            eventType is null &&
            dumpPath is null &&
            reportPath is null)
        {
            return null;
        }

        var artifactPath = reportPath
            ?? dumpPath
            ?? $"eventlog://{diagnosticEvent.LogName}/" +
                $"{diagnosticEvent.ProviderName}/" +
                $"{diagnosticEvent.RecordId?.ToString() ?? diagnosticEvent.ObservedAtUtc.UtcTicks.ToString()}";

        var properties = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["EvidenceOrigin"] = "WindowsEventLog",
            ["ProviderName"] = diagnosticEvent.ProviderName,
            ["EventId"] = diagnosticEvent.EventId.ToString()
        };

        if (diagnosticEvent.RecordId.HasValue)
        {
            properties["EventRecordId"] = diagnosticEvent.RecordId.Value.ToString();
        }

        foreach (Match match in SignatureFieldRegex().Matches(message))
        {
            properties[match.Groups["name"].Value.Trim()] =
                match.Groups["value"].Value.Trim();
        }

        return new DiagnosticArtifact(
            "WindowsErrorReportingEventLog",
            DiagnosticArtifactKind.WindowsErrorReport,
            artifactPath,
            diagnosticEvent.ObservedAtUtc,
            diagnosticEvent.SourceOccurredAtUtc,
            reportId,
            eventType,
            dumpPath,
            signature,
            properties);
    }

    private static void ReadWerRoot(
        string root,
        DateTimeOffset sinceUtc,
        ICollection<DiagnosticArtifact> destination,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return;
        }

        var candidates = new List<string>();
        var directReport = Path.Combine(root, "Report.wer");
        if (File.Exists(directReport))
        {
            candidates.Add(directReport);
        }

        try
        {
            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var reportPath = Path.Combine(directory, "Report.wer");
                if (File.Exists(reportPath))
                {
                    candidates.Add(reportPath);
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }
        catch (IOException)
        {
            return;
        }

        foreach (var reportPath in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var observedAtUtc = new DateTimeOffset(
                    File.GetLastWriteTimeUtc(reportPath));

                if (observedAtUtc < sinceUtc)
                {
                    continue;
                }

                destination.Add(ParseWerReport(reportPath, observedAtUtc));
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }
        }
    }

    private static DiagnosticArtifact ParseWerReport(
        string reportPath,
        DateTimeOffset observedAtUtc)
    {
        var properties = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var line in File.ReadLines(reportPath))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            if (key.Length > 0)
            {
                properties[key] = value;
            }
        }

        var reportId = GetFirst(
            properties,
            "ReportIdentifier",
            "ReportId",
            "CabGuid");
        var eventType = GetFirst(properties, "EventType");
        var relatedDumpPath = properties.Values.FirstOrDefault(
            value => value.Contains(".dmp", StringComparison.OrdinalIgnoreCase));
        var sourceOccurredAtUtc = ParseWerEventTime(
            GetFirst(properties, "EventTime"));
        var signature = CreateSignature(properties, eventType);

        return new DiagnosticArtifact(
            "WindowsErrorReporting",
            DiagnosticArtifactKind.WindowsErrorReport,
            reportPath,
            observedAtUtc,
            sourceOccurredAtUtc,
            reportId,
            eventType,
            relatedDumpPath,
            signature,
            properties);
    }

    private static void ReadLiveKernelRoot(
        string root,
        DateTimeOffset sinceUtc,
        ICollection<DiagnosticArtifact> destination,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return;
        }

        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();

            string[] files;
            try
            {
                files = Directory.GetFiles(current, "*.dmp");
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var observedAtUtc = new DateTimeOffset(
                        File.GetLastWriteTimeUtc(file));
                    if (observedAtUtc < sinceUtc)
                    {
                        continue;
                    }

                    destination.Add(new DiagnosticArtifact(
                        "WindowsLiveKernelReports",
                        DiagnosticArtifactKind.LiveKernelDump,
                        file,
                        observedAtUtc,
                        sourceOccurredAtUtc: null,
                        reportId: null,
                        eventType: null,
                        relatedPath: file,
                        signature: null));
                }
                catch (UnauthorizedAccessException)
                {
                }
                catch (IOException)
                {
                }
            }

            try
            {
                foreach (var directory in Directory.GetDirectories(current))
                {
                    pending.Push(directory);
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }
        }
    }

    private static string BuildArtifactIdentity(DiagnosticArtifact artifact)
    {
        if (!string.IsNullOrWhiteSpace(artifact.ReportId))
        {
            return $"report:{artifact.ReportId.Trim()}";
        }

        if (!string.IsNullOrWhiteSpace(artifact.RelatedPath) &&
            artifact.RelatedPath.Contains(".dmp", StringComparison.OrdinalIgnoreCase))
        {
            return $"dump:{NormalizePath(artifact.RelatedPath)}";
        }

        return $"artifact:{NormalizePath(artifact.ArtifactPath)}";
    }

    private static string NormalizePath(string value) =>
        value.Trim()
            .Trim('"')
            .Replace('/', '\\')
            .ToUpperInvariant();

    private static string? MatchValue(Regex regex, string value)
    {
        var match = regex.Match(value);
        return match.Success
            ? match.Groups["value"].Value.Trim().Trim('"')
            : null;
    }

    private static string? GetFirst(
        IReadOnlyDictionary<string, string> properties,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            if (properties.TryGetValue(key, out var value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static DateTimeOffset? ParseWerEventTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !long.TryParse(value, out var fileTime))
        {
            return null;
        }

        try
        {
            return new DateTimeOffset(DateTime.FromFileTimeUtc(fileTime));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string? CreateSignature(
        IReadOnlyDictionary<string, string> properties,
        string? eventType)
    {
        var parts = properties
            .Where(x =>
                x.Key.StartsWith("Sig[", StringComparison.OrdinalIgnoreCase) ||
                x.Key.StartsWith("DynamicSig[", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => $"{x.Key}={x.Value}")
            .ToList();

        if (!string.IsNullOrWhiteSpace(eventType))
        {
            parts.Insert(0, $"EventType={eventType.Trim()}");
        }

        return HashParts(parts);
    }

    private static string? CreateMessageSignature(
        string message,
        string? eventType)
    {
        var parts = SignatureFieldRegex()
            .Matches(message)
            .Select(match =>
                $"{match.Groups["name"].Value.Trim()}={match.Groups["value"].Value.Trim()}")
            .ToList();

        if (!string.IsNullOrWhiteSpace(eventType))
        {
            parts.Insert(0, $"EventType={eventType.Trim()}");
        }

        return HashParts(parts);
    }

    private static string? HashParts(IReadOnlyCollection<string> parts)
    {
        if (parts.Count == 0)
        {
            return null;
        }

        var payload = string.Join("\n", parts);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash);
    }

    [GeneratedRegex(
        @"(?im)^\s*Report\s+(?:Id|Identifier)\s*:\s*(?<value>[^\r\n]+)")]
    private static partial Regex ReportIdRegex();

    [GeneratedRegex(
        @"(?im)^\s*Event\s+Name\s*:\s*(?<value>[^\r\n]+)")]
    private static partial Regex EventNameRegex();

    [GeneratedRegex(
        @"(?i)(?:\\\\\?\\)?(?<value>[A-Z]:\\[^\r\n]*?\.dmp)")]
    private static partial Regex DumpPathRegex();

    [GeneratedRegex(
        @"(?im)^\s*These\s+files\s+may\s+be\s+available\s+here\s*:\s*(?:\\\\\?\\)?(?<value>[A-Z]:\\[^\r\n]+)")]
    private static partial Regex ReportLocationRegex();

    [GeneratedRegex(
        @"(?im)^\s*(?<name>P\d+)\s*:\s*(?<value>[^\r\n]+)")]
    private static partial Regex SignatureFieldRegex();
}

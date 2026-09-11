using System.Globalization;
using CrashScope.Agent.Processes;
using CrashScope.Core.Diagnostics;
using CrashScope.Core.Evidence;
using CrashScope.Core.Incidents;
using CrashScope.Core.Telemetry;

namespace CrashScope.Agent.Incidents;

internal sealed class IncidentReportBuilder
{
    private static readonly TimeSpan DefaultEvidenceWindow = TimeSpan.FromMinutes(2);
    private const int MaximumProviderEvidenceItems = 256;

    private readonly TimeSpan _evidenceWindow;

    public IncidentReportBuilder(TimeSpan? evidenceWindow = null)
    {
        _evidenceWindow = evidenceWindow ?? DefaultEvidenceWindow;
        if (_evidenceWindow < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(evidenceWindow));
        }
    }

    internal TimeSpan EvidenceWindow => _evidenceWindow;

    public IncidentReport Build(
        IncidentCapture capture,
        IReadOnlyList<DiagnosticEvent> events,
        IReadOnlyList<DiagnosticArtifact> artifacts,
        ProcessObservation? processObservation = null,
        IReadOnlyList<EvidenceEvent>? providerEvidence = null)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(artifacts);

        var incidentTimeUtc = capture.Trigger.SourceOccurredAtUtc
            ?? capture.Trigger.ObservedAtUtc;

        var evidence = new List<IncidentEvidenceItem>
        {
            new(
                incidentTimeUtc,
                capture.Trigger.ObservedAtUtc,
                IncidentEvidenceRole.Trigger,
                "CrashScope",
                "Trigger",
                capture.Trigger.Summary,
                capture.Trigger.EvidenceKey)
        };

        evidence.AddRange(events
            .Where(item => IsNearIncident(EventTime(item), incidentTimeUtc))
            .Select(ToEvidenceItem));

        evidence.AddRange(artifacts
            .Where(item => IsNearIncident(ArtifactTime(item), incidentTimeUtc))
            .Select(ToEvidenceItem));

        // External provider context can enrich the timeline, but it must never
        // change CrashScope's classification of Windows/process evidence.
        var classification = Classify(evidence, processObservation);

        if (providerEvidence is not null)
        {
            evidence.AddRange(BuildProviderEvidenceItems(
                providerEvidence,
                incidentTimeUtc));
        }

        var processContext = processObservation is null
            ? null
            : new IncidentProcessContext(
                processObservation.Target.Identity.ProcessId,
                processObservation.Target.Identity.StartTimeUtc,
                processObservation.Target.Name,
                processObservation.State.ToString(),
                processObservation.ObservedAtUtc);

        var telemetry = SummarizeTelemetry(capture.TelemetryFrames);
        var title = BuildTitle(classification, processObservation);
        var summary = BuildSummary(evidence.Count, telemetry.FrameCount, processObservation);
        var assessment = BuildAssessment(classification, evidence, processObservation);

        return new IncidentReport(
            capture.Trigger.IncidentId,
            classification,
            title,
            summary,
            assessment,
            incidentTimeUtc,
            processContext,
            telemetry,
            evidence);
    }

    private bool IsNearIncident(DateTimeOffset evidenceTimeUtc, DateTimeOffset incidentTimeUtc) =>
        (evidenceTimeUtc - incidentTimeUtc).Duration() <= _evidenceWindow;

    private static DateTimeOffset EventTime(DiagnosticEvent item) =>
        item.SourceOccurredAtUtc ?? item.ObservedAtUtc;

    private static DateTimeOffset ArtifactTime(DiagnosticArtifact item)
    {
        if (item.SourceOccurredAtUtc.HasValue)
        {
            return item.SourceOccurredAtUtc.Value;
        }

        if (TryGetWatchdogSourceTimeUtc(item.RelatedPath, out var sourceTimeUtc))
        {
            return sourceTimeUtc;
        }

        return item.ObservedAtUtc;
    }

    internal static bool TryGetWatchdogSourceTimeUtc(
        string? path,
        out DateTimeOffset sourceTimeUtc)
    {
        sourceTimeUtc = default;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var name = Path.GetFileNameWithoutExtension(path.Trim().Trim('"'));
        if (!name.StartsWith("WATCHDOG-", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var timestamp = name["WATCHDOG-".Length..];
        if (!DateTime.TryParseExact(
                timestamp,
                "yyyyMMdd-HHmm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var localClock))
        {
            return false;
        }

        localClock = DateTime.SpecifyKind(localClock, DateTimeKind.Unspecified);
        if (TimeZoneInfo.Local.IsInvalidTime(localClock))
        {
            return false;
        }

        var utc = TimeZoneInfo.ConvertTimeToUtc(localClock, TimeZoneInfo.Local);
        sourceTimeUtc = new DateTimeOffset(utc);
        return true;
    }

    private static IncidentEvidenceItem ToEvidenceItem(DiagnosticEvent item) =>
        new(
            EventTime(item),
            item.ObservedAtUtc,
            IncidentEvidenceRole.Corroborating,
            item.ProviderName,
            item.Kind.ToString(),
            BuildEventSummary(item),
            item.RecordId.HasValue
                ? $"event:{item.LogName}:{item.ProviderName}:{item.RecordId.Value}"
                : null);

    internal IReadOnlyList<IncidentEvidenceItem> BuildProviderEvidenceItems(
        IReadOnlyList<EvidenceEvent> providerEvidence,
        DateTimeOffset incidentTimeUtc)
    {
        ArgumentNullException.ThrowIfNull(providerEvidence);

        return providerEvidence
            .Where(item => IsNearIncident(item.TimestampUtc, incidentTimeUtc))
            .OrderBy(item => (item.TimestampUtc - incidentTimeUtc).Duration())
            .ThenBy(item => item.TimestampUtc)
            .ThenBy(item => item.ObservedAtUtc)
            .Take(MaximumProviderEvidenceItems)
            .Select(item => ToEvidenceItem(item, incidentTimeUtc))
            .ToArray();
    }
    private static IncidentEvidenceItem ToEvidenceItem(DiagnosticArtifact item) =>
        new(
            ArtifactTime(item),
            item.ObservedAtUtc,
            IncidentEvidenceRole.Corroborating,
            item.Source,
            item.Kind.ToString(),
            BuildArtifactSummary(item),
            !string.IsNullOrWhiteSpace(item.ReportId)
                ? $"report:{item.ReportId}"
                : !string.IsNullOrWhiteSpace(item.RelatedPath)
                    ? $"path:{item.RelatedPath}"
                    : $"artifact:{item.ArtifactPath}");

    private static IncidentEvidenceItem ToEvidenceItem(
        EvidenceEvent item,
        DateTimeOffset incidentTimeUtc) =>
        new(
            item.TimestampUtc,
            item.ObservedAtUtc,
            IncidentEvidenceRole.Context,
            item.Source,
            item.Kind,
            BuildProviderEvidenceSummary(item, incidentTimeUtc),
            $"provider:{item.Source}:{item.Kind}:{item.TimestampUtc.UtcTicks}:{item.ObservedAtUtc.UtcTicks}");

    private static string BuildProviderEvidenceSummary(
        EvidenceEvent item,
        DateTimeOffset incidentTimeUtc)
    {
        var summary = item.Summary.Trim();
        if (summary.EndsWith(".", StringComparison.Ordinal))
        {
            summary = summary[..^1];
        }

        var relativeSeconds = (item.TimestampUtc - incidentTimeUtc).TotalSeconds;
        if (Math.Abs(relativeSeconds) < 0.05)
        {
            return $"{summary} at approximately the incident time.";
        }

        var seconds = Math.Abs(relativeSeconds).ToString(
            "0.0",
            CultureInfo.InvariantCulture);

        return relativeSeconds < 0
            ? $"{summary} {seconds} seconds before this incident."
            : $"{summary} {seconds} seconds after this incident.";
    }

    private static string BuildEventSummary(DiagnosticEvent item) =>
        item.Kind switch
        {
            DiagnosticEventKind.ApplicationFault =>
                $"Application fault event {item.EventId} was recorded.",
            DiagnosticEventKind.ApplicationHang =>
                $"Application hang event {item.EventId} was recorded.",
            DiagnosticEventKind.WindowsErrorReport =>
                $"Windows Error Reporting event {item.EventId} was recorded.",
            DiagnosticEventKind.HardwareError =>
                $"Windows hardware-error evidence event {item.EventId} was recorded.",
            DiagnosticEventKind.KernelPower =>
                $"Kernel-Power event {item.EventId} was recorded.",
            DiagnosticEventKind.DisplayDriver =>
                $"Display-driver event {item.EventId} was recorded.",
            _ => $"Windows diagnostic event {item.EventId} was recorded."
        };

    private static string BuildArtifactSummary(DiagnosticArtifact item)
    {
        if (string.Equals(item.EventType, "LiveKernelEvent", StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrWhiteSpace(item.RelatedPath)
                ? $"Windows reported LiveKernelEvent evidence with dump {Path.GetFileName(item.RelatedPath)}."
                : "Windows reported LiveKernelEvent evidence.";
        }

        if (string.Equals(item.EventType, "BlueScreen", StringComparison.OrdinalIgnoreCase))
        {
            return "Windows Error Reporting recorded BlueScreen evidence.";
        }

        return !string.IsNullOrWhiteSpace(item.EventType)
            ? $"Windows Error Reporting recorded {item.EventType} evidence."
            : $"Diagnostic artifact {Path.GetFileName(item.ArtifactPath)} was observed.";
    }

    private static IncidentTelemetrySummary SummarizeTelemetry(
        IReadOnlyList<TelemetryFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);

        return new IncidentTelemetrySummary(
            frames.Count,
            frames.Count == 0 ? null : frames.Min(x => x.TimestampUtc),
            frames.Count == 0 ? null : frames.Max(x => x.TimestampUtc),
            Peak(frames.Select(x => x.Cpu.TotalUtilizationPercent)),
            Peak(frames.SelectMany(x => x.Gpus.Select(gpu => gpu.CoreUtilizationPercent))),
            Peak(frames.SelectMany(x => x.Gpus.Select(gpu => gpu.HotspotTemperatureCelsius))),
            Peak(frames.SelectMany(x => x.Gpus.Select(gpu => gpu.DedicatedMemoryUsedMiB))),
            Peak(frames.Select(x => x.System.PhysicalMemoryLoadPercent)));
    }

    private static double? Peak(IEnumerable<MetricReading> readings)
    {
        var available = readings
            .Where(x => x.IsAvailable)
            .Select(x => x.Value!.Value)
            .ToArray();

        return available.Length == 0 ? null : available.Max();
    }

    private static IncidentClassification Classify(
        IReadOnlyList<IncidentEvidenceItem> evidence,
        ProcessObservation? processObservation)
    {
        var hasHardware = evidence.Any(x =>
            x.Kind == DiagnosticEventKind.HardwareError.ToString());
        var hasKernelWatchdog = evidence.Any(x =>
            x.Kind == DiagnosticEventKind.DisplayDriver.ToString() ||
            x.Summary.Contains("LiveKernelEvent", StringComparison.OrdinalIgnoreCase));
        var hasUnexpectedShutdown = evidence.Any(x =>
            x.Kind == DiagnosticEventKind.KernelPower.ToString());
        var hasApplicationEvidence = evidence.Any(x =>
            x.Kind == DiagnosticEventKind.ApplicationFault.ToString() ||
            x.Kind == DiagnosticEventKind.ApplicationHang.ToString());

        var categoryCount = new[]
        {
            hasHardware,
            hasKernelWatchdog,
            hasUnexpectedShutdown,
            hasApplicationEvidence
        }.Count(x => x);

        if (categoryCount > 1)
        {
            return IncidentClassification.Mixed;
        }

        if (hasHardware)
        {
            return IncidentClassification.HardwareError;
        }

        if (hasKernelWatchdog)
        {
            return IncidentClassification.KernelOrDriverWatchdog;
        }

        if (hasUnexpectedShutdown)
        {
            return IncidentClassification.UnexpectedShutdown;
        }

        if (hasApplicationEvidence ||
            processObservation?.State == ProcessObservationState.Exited)
        {
            return IncidentClassification.ApplicationFailure;
        }

        return IncidentClassification.Unclassified;
    }

    private static string BuildTitle(
        IncidentClassification classification,
        ProcessObservation? processObservation)
    {
        var processName = processObservation?.Target.Name;
        var subject = string.IsNullOrWhiteSpace(processName)
            ? "Incident"
            : processName;

        return classification switch
        {
            IncidentClassification.ApplicationFailure => $"{subject} application failure evidence",
            IncidentClassification.KernelOrDriverWatchdog => $"{subject} kernel/driver watchdog evidence",
            IncidentClassification.HardwareError => $"{subject} hardware-error evidence",
            IncidentClassification.UnexpectedShutdown => $"{subject} unexpected-shutdown evidence",
            IncidentClassification.Mixed => $"{subject} mixed crash evidence",
            _ => $"{subject} diagnostic incident"
        };
    }

    private static string BuildSummary(
        int evidenceCount,
        int telemetryFrameCount,
        ProcessObservation? processObservation)
    {
        var processClause = processObservation is null
            ? "No monitored-process observation was attached."
            : $"The monitored process was observed as {processObservation.State}.";

        return $"CrashScope correlated {evidenceCount} evidence items with " +
            $"{telemetryFrameCount} telemetry frames around the incident. {processClause}";
    }

    private static string BuildAssessment(
        IncidentClassification classification,
        IReadOnlyList<IncidentEvidenceItem> evidence,
        ProcessObservation? processObservation)
    {
        var processExit = processObservation?.State == ProcessObservationState.Exited;

        return classification switch
        {
            IncidentClassification.KernelOrDriverWatchdog when processExit =>
                "A monitored-process exit and nearby Windows kernel/driver watchdog evidence were observed. " +
                "This correlation is consistent with a GPU/driver instability event, but CrashScope does not treat it as proof of root cause.",

            IncidentClassification.KernelOrDriverWatchdog =>
                "Windows kernel/driver watchdog evidence was observed near the incident. " +
                "This is evidence of a low-level graphics or driver problem, not by itself proof of what caused it.",

            IncidentClassification.HardwareError =>
                "Windows hardware-error evidence was observed near the incident. " +
                "The report preserves that evidence without assigning a component-level cause that Windows did not establish.",

            IncidentClassification.UnexpectedShutdown =>
                "Kernel-Power evidence was observed near the incident. " +
                "This indicates an abnormal power or shutdown transition but does not establish why the system stopped cleanly reporting events.",

            IncidentClassification.ApplicationFailure =>
                "Application failure or process-exit evidence was observed without stronger nearby kernel or hardware evidence. " +
                "CrashScope therefore reports an application-level failure and does not infer a hardware cause.",

            IncidentClassification.Mixed =>
                "Multiple categories of diagnostic evidence were observed in the correlation window. " +
                "They are presented together because they may describe one incident, but causality between them has not been assumed.",

            _ => evidence.Count > 1
                ? "Diagnostic evidence was correlated around the trigger, but it is not specific enough for a stronger classification."
                : "The trigger was captured, but there is not yet enough corroborating evidence for a stronger classification."
        };
    }
}

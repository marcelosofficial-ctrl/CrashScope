using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CrashScope.Core.Incidents;
using CrashScope.Core.Sessions;

namespace CrashScope.Agent.Support;

internal sealed record SupportBundleRuntimeSnapshot(
    string CrashScopeVersion,
    string SamplingMode,
    int SchemaVersion,
    int SettingsSchemaVersion,
    int RetentionDays,
    bool AutomaticMonitoringEnabled,
    string AutomaticMonitoringState,
    long StreamDroppedStaleFrames,
    long StreamDeliveryMisses);

internal sealed record SupportBundlePreview(
    string FileName,
    IReadOnlyList<string> Entries,
    IReadOnlyList<string> PrivacyNotes);

internal sealed record SupportBundleResult(
    string FileName,
    byte[] Content,
    IReadOnlyList<string> Entries);

internal sealed record PrivacyRedactionContext(
    string? UserProfilePath,
    string? UserName,
    string? MachineName)
{
    public static PrivacyRedactionContext Current { get; } = new(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.UserName,
        Environment.MachineName);
}

internal sealed partial class SupportBundlePrivacyRedactor
{
    private const int MaximumTextLength = 4096;
    private readonly PrivacyRedactionContext _context;

    public SupportBundlePrivacyRedactor(PrivacyRedactionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var result = value;
        result = ReplaceLiteral(result, _context.UserProfilePath, "%USERPROFILE%");
        result = ReplaceLiteral(result, NormalizeSlashes(_context.UserProfilePath), "%USERPROFILE%");
        result = ReplaceLiteral(result, _context.UserName, "<redacted-user>");
        result = ReplaceLiteral(result, _context.MachineName, "<redacted-machine>");
        result = EmailRegex().Replace(result, "<redacted-email>");
        result = MacAddressRegex().Replace(result, "<redacted-mac>");
        result = Ipv4Regex().Replace(result, "<redacted-ip>");
        result = Ipv6CandidateRegex().Replace(result, match =>
            IPAddress.TryParse(match.Value, out var address) && address.AddressFamily == AddressFamily.InterNetworkV6
                ? "<redacted-ip>"
                : match.Value);
        result = BearerTokenRegex().Replace(result, "Bearer <redacted-secret>");
        result = NamedSecretRegex().Replace(result, match =>
            $"{match.Groups["name"].Value}{match.Groups["separator"].Value}<redacted-secret>");

        return result.Length <= MaximumTextLength
            ? result
            : result[..MaximumTextLength] + "… <truncated>";
    }

    private static string ReplaceLiteral(string input, string? value, string replacement)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return input;
        }

        return input.Replace(value, replacement, StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeSlashes(string? value) =>
        string.IsNullOrWhiteSpace(value) ? value : value.Replace('\\', '/');

    [GeneratedRegex(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"\b(?:[0-9A-F]{2}[:-]){5}[0-9A-F]{2}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MacAddressRegex();

    [GeneratedRegex(@"(?<!\d)(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex Ipv4Regex();

    [GeneratedRegex(@"(?<![0-9A-F:])(?:[0-9A-F]{0,4}:){2,7}[0-9A-F]{0,4}(?![0-9A-F:])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Ipv6CandidateRegex();

    [GeneratedRegex(@"\bBearer\s+[A-Z0-9._~+/=-]{8,}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex(@"\b(?<name>api[_-]?key|access[_-]?token|auth[_-]?token|token|password|passwd|secret)(?<separator>\s*[:=]\s*)(?<value>[^\s,;]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NamedSecretRegex();
}

internal sealed class SupportBundleService
{
    internal const int FormatVersion = 1;
    internal const int MaximumEvidenceItems = 256;

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private static readonly IReadOnlyList<string> PrivacyNotes = new[]
    {
        "No CrashScope database is included.",
        "No dump, ETL, WER report, or arbitrary log file bytes are included.",
        "User profile paths, username, machine name, email addresses, IP addresses, MAC addresses, and obvious credential-style values are redacted from exported text.",
        "Only curated incident/session/runtime fields are exported; environment variables and hardware serial identifiers are never enumerated.",
        "Redaction is defense-in-depth rather than a guarantee for arbitrary free-form text; preview the bundle contents before sharing it."
    };

    private readonly SupportBundlePrivacyRedactor _redactor;
    private readonly Func<DateTimeOffset> _utcNow;

    public SupportBundleService(
        SupportBundlePrivacyRedactor redactor,
        Func<DateTimeOffset>? utcNow = null)
    {
        ArgumentNullException.ThrowIfNull(redactor);
        _redactor = redactor;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public SupportBundlePreview Preview(IncidentReport incident, WorkloadSession? session)
    {
        ArgumentNullException.ThrowIfNull(incident);

        var entries = BuildEntryNames(session);
        return new SupportBundlePreview(
            BuildFileName(incident),
            entries,
            PrivacyNotes);
    }

    public SupportBundleResult Create(
        IncidentReport incident,
        WorkloadSession? session,
        SupportBundleRuntimeSnapshot runtime)
    {
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentNullException.ThrowIfNull(runtime);

        var entries = BuildEntryNames(session);
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteTextEntry(archive, "README.txt", BuildReadme(incident, session, runtime));
            WriteJsonEntry(archive, "manifest.json", BuildManifest(incident, entries));
            WriteJsonEntry(archive, "incident.json", BuildIncidentExport(incident));
            if (session is not null)
            {
                WriteJsonEntry(archive, "session.json", BuildSessionExport(session));
            }
            WriteJsonEntry(archive, "runtime.json", BuildRuntimeExport(runtime));
        }

        return new SupportBundleResult(
            BuildFileName(incident),
            memory.ToArray(),
            entries);
    }

    private object BuildManifest(IncidentReport incident, IReadOnlyList<string> entries) => new
    {
        formatVersion = FormatVersion,
        createdAtUtc = _utcNow().ToUniversalTime(),
        incidentId = incident.IncidentId,
        entries,
        privacy = PrivacyNotes
    };

    private object BuildIncidentExport(IncidentReport report) => new
    {
        incidentId = report.IncidentId,
        incidentTimeUtc = report.IncidentTimeUtc,
        classification = report.Classification,
        title = _redactor.Redact(report.Title),
        summary = _redactor.Redact(report.Summary),
        assessment = _redactor.Redact(report.Assessment),
        process = report.Process is null ? null : new
        {
            processId = report.Process.ProcessId,
            startTimeUtc = report.Process.StartTimeUtc,
            name = _redactor.Redact(report.Process.Name),
            observationState = _redactor.Redact(report.Process.ObservationState),
            observedAtUtc = report.Process.ObservedAtUtc
        },
        telemetry = report.Telemetry,
        evidence = report.Evidence
            .Take(MaximumEvidenceItems)
            .Select(item => new
            {
                occurredAtUtc = item.OccurredAtUtc,
                observedAtUtc = item.ObservedAtUtc,
                role = item.Role,
                source = _redactor.Redact(item.Source),
                kind = _redactor.Redact(item.Kind),
                summary = _redactor.Redact(item.Summary)
            })
            .ToArray(),
        evidenceWasTruncated = report.Evidence.Count > MaximumEvidenceItems
    };

    private object BuildSessionExport(WorkloadSession session) => new
    {
        sessionId = session.SessionId,
        processId = session.ProcessId,
        processName = _redactor.Redact(session.ProcessName),
        executableName = SafeFileName(session.ExecutablePath),
        processStartTimeUtc = session.ProcessStartTimeUtc,
        startedAtUtc = session.StartedAtUtc,
        endedAtUtc = session.EndedAtUtc,
        endReason = session.EndReason,
        telemetry = session.Telemetry,
        environment = session.Environment is null ? null : new
        {
            capturedAtUtc = session.Environment.CapturedAtUtc,
            operatingSystem = _redactor.Redact(session.Environment.OperatingSystem),
            osArchitecture = session.Environment.OsArchitecture,
            processArchitecture = session.Environment.ProcessArchitecture,
            runtimeDescription = _redactor.Redact(session.Environment.RuntimeDescription),
            crashScopeVersion = session.Environment.CrashScopeVersion,
            cpuName = _redactor.Redact(session.Environment.CpuName),
            logicalProcessorCount = session.Environment.LogicalProcessorCount,
            physicalMemoryMiB = session.Environment.PhysicalMemoryMiB,
            gpus = session.Environment.Gpus.Select(gpu => new
            {
                name = _redactor.Redact(gpu.Name),
                driverVersion = _redactor.Redact(gpu.DriverVersion)
            }).ToArray()
        }
    };

    private object BuildRuntimeExport(SupportBundleRuntimeSnapshot runtime) => new
    {
        crashScopeVersion = runtime.CrashScopeVersion,
        samplingMode = runtime.SamplingMode,
        schemaVersion = runtime.SchemaVersion,
        settingsSchemaVersion = runtime.SettingsSchemaVersion,
        retentionDays = runtime.RetentionDays,
        automaticMonitoringEnabled = runtime.AutomaticMonitoringEnabled,
        automaticMonitoringState = _redactor.Redact(runtime.AutomaticMonitoringState),
        streamDroppedStaleFrames = runtime.StreamDroppedStaleFrames,
        streamDeliveryMisses = runtime.StreamDeliveryMisses
    };

    private string BuildReadme(
        IncidentReport incident,
        WorkloadSession? session,
        SupportBundleRuntimeSnapshot runtime)
    {
        var builder = new StringBuilder();
        builder.AppendLine("CrashScope privacy-safe support bundle");
        builder.AppendLine("=====================================");
        builder.AppendLine();
        builder.AppendLine($"CrashScope version: {runtime.CrashScopeVersion}");
        builder.AppendLine($"Incident: {_redactor.Redact(incident.Title)}");
        builder.AppendLine($"Classification: {incident.Classification}");
        builder.AppendLine($"Incident time (UTC): {incident.IncidentTimeUtc:O}");
        if (session is not null)
        {
            builder.AppendLine($"Related workload: {_redactor.Redact(session.ProcessName)}");
        }
        builder.AppendLine();
        builder.AppendLine("Assessment");
        builder.AppendLine("----------");
        builder.AppendLine(_redactor.Redact(incident.Assessment));
        builder.AppendLine();
        builder.AppendLine("Important: CrashScope reports observed evidence and context. This bundle does not prove a hardware component, driver, or application is the root cause unless the evidence itself establishes that fact.");
        builder.AppendLine();
        builder.AppendLine("Privacy");
        builder.AppendLine("-------");
        foreach (var note in PrivacyNotes)
        {
            builder.AppendLine($"- {note}");
        }
        builder.AppendLine();
        builder.AppendLine("Nothing in this bundle was uploaded by CrashScope. The user explicitly created this local export.");
        return builder.ToString();
    }

    private string? SafeFileName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return _redactor.Redact(Path.GetFileName(path));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> BuildEntryNames(WorkloadSession? session) =>
        session is null
            ? new[] { "README.txt", "manifest.json", "incident.json", "runtime.json" }
            : new[] { "README.txt", "manifest.json", "incident.json", "session.json", "runtime.json" };

    private static string BuildFileName(IncidentReport incident) =>
        $"CrashScope-support-{incident.IncidentId:N}.zip";

    private static void WriteTextEntry(ZipArchive archive, string name, string text)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(text);
    }

    private static void WriteJsonEntry(ZipArchive archive, string name, object value)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        using var stream = entry.Open();
        JsonSerializer.Serialize(stream, value, value.GetType(), JsonOptions);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

using System.Globalization;
using System.Text.Json;
using CrashScope.Core.Evidence;

namespace CrashScope.Agent.Evidence.ConfigTrace;

internal sealed record ConfigTraceJournalReadResult(
    bool HasSessionStart,
    IReadOnlyList<EvidenceEvent> Events,
    int MalformedRecordCount,
    int UnsupportedSchemaCount);

internal sealed class ConfigTraceJournalReader
{
    private const int SupportedSchemaVersion = 1;

    public ConfigTraceJournalReadResult Read(
        string journalPath,
        DateTimeOffset? startUtc = null,
        DateTimeOffset? endUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalPath);

        if (startUtc.HasValue && startUtc.Value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Evidence window start must be UTC.", nameof(startUtc));
        }

        if (endUtc.HasValue && endUtc.Value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Evidence window end must be UTC.", nameof(endUtc));
        }

        if (startUtc.HasValue && endUtc.HasValue && endUtc.Value < startUtc.Value)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endUtc),
                endUtc,
                "Evidence window end cannot be earlier than its start.");
        }

        if (!File.Exists(journalPath))
        {
            return new ConfigTraceJournalReadResult(
                false,
                Array.Empty<EvidenceEvent>(),
                0,
                0);
        }

        var events = new List<EvidenceEvent>();
        var hasSessionStart = false;
        var malformed = 0;
        var unsupportedSchema = 0;

        using var stream = new FileStream(
            journalPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);

        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                malformed++;
                continue;
            }

            using (document)
            {
                var root = document.RootElement;

                if (!TryGetInt32(root, "schema_version", out var schemaVersion))
                {
                    malformed++;
                    continue;
                }

                if (schemaVersion != SupportedSchemaVersion)
                {
                    unsupportedSchema++;
                    continue;
                }

                if (!TryGetString(root, "record_type", out var recordType))
                {
                    malformed++;
                    continue;
                }

                if (string.Equals(recordType, "session_start", StringComparison.Ordinal))
                {
                    hasSessionStart = true;
                    continue;
                }

                if (!string.Equals(recordType, "change", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!TryGetInt64(root, "observed_unix_ms", out var observedUnixMs) ||
                    !TryGetInt64(root, "sequence", out var sequence) ||
                    !root.TryGetProperty("file", out var file) ||
                    file.ValueKind != JsonValueKind.Object ||
                    !TryGetString(file, "path", out var filePath) ||
                    !TryGetString(file, "change", out var fileChange))
                {
                    malformed++;
                    continue;
                }

                DateTimeOffset occurredAtUtc;
                try
                {
                    occurredAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(observedUnixMs);
                }
                catch (ArgumentOutOfRangeException)
                {
                    malformed++;
                    continue;
                }

                if (startUtc.HasValue && occurredAtUtc < startUtc.Value)
                {
                    continue;
                }

                if (endUtc.HasValue && occurredAtUtc > endUtc.Value)
                {
                    continue;
                }

                var journalRoot = TryGetString(root, "root", out var parsedRoot)
                    ? parsedRoot
                    : string.Empty;

                var fieldSummaries = new List<string>();
                var fieldKeys = new List<string>();
                var sensitiveFieldCount = 0;

                if (file.TryGetProperty("fields", out var fields) &&
                    fields.ValueKind == JsonValueKind.Array)
                {
                    foreach (var field in fields.EnumerateArray())
                    {
                        if (field.ValueKind != JsonValueKind.Object ||
                            !TryGetString(field, "key", out var key) ||
                            !TryGetString(field, "change", out var change))
                        {
                            continue;
                        }

                        fieldKeys.Add(key);

                        var sensitive =
                            field.TryGetProperty("sensitive", out var sensitiveElement) &&
                            sensitiveElement.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                            sensitiveElement.GetBoolean();

                        if (sensitive)
                        {
                            sensitiveFieldCount++;
                            fieldSummaries.Add(
                                $"{DisplayFieldName(key)} {DescribeChange(change)} (sensitive value redacted)");
                            continue;
                        }

                        var before = TryGetString(field, "before", out var beforeValue)
                            ? beforeValue
                            : null;
                        var after = TryGetString(field, "after", out var afterValue)
                            ? afterValue
                            : null;

                        fieldSummaries.Add(BuildNonSensitiveFieldSummary(
                            key,
                            change,
                            before,
                            after));
                    }
                }

                var summary = BuildFileSummary(
                    filePath,
                    fileChange,
                    fieldSummaries);

                var details = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["root"] = journalRoot,
                    ["file"] = filePath,
                    ["change"] = fileChange,
                    ["sequence"] = sequence.ToString(CultureInfo.InvariantCulture),
                    ["field_count"] = fieldKeys.Count.ToString(CultureInfo.InvariantCulture),
                    ["sensitive_field_count"] = sensitiveFieldCount.ToString(CultureInfo.InvariantCulture)
                };

                if (fieldKeys.Count > 0)
                {
                    details["fields"] = string.Join(",", fieldKeys);
                }

                events.Add(new EvidenceEvent(
                    occurredAtUtc,
                    occurredAtUtc,
                    "ConfigTrace",
                    "ConfigChange",
                    EvidenceSeverity.Information,
                    summary,
                    details));
            }
        }

        var ordered = events
            .OrderBy(item => item.TimestampUtc)
            .ThenBy(item => item.ObservedAtUtc)
            .ToArray();

        return new ConfigTraceJournalReadResult(
            hasSessionStart,
            ordered,
            malformed,
            unsupportedSchema);
    }

    private static string BuildFileSummary(
        string filePath,
        string fileChange,
        IReadOnlyList<string> fields)
    {
        var fileName = string.IsNullOrWhiteSpace(filePath)
            ? "A configuration file"
            : filePath;

        if (fields.Count == 0)
        {
            return $"{fileName} was {DescribeChange(fileChange)}.";
        }

        return $"{fileName}: {string.Join("; ", fields)}.";
    }

    private static string BuildNonSensitiveFieldSummary(
        string key,
        string change,
        string? before,
        string? after)
    {
        var name = DisplayFieldName(key);

        if (string.Equals(change, "modified", StringComparison.OrdinalIgnoreCase) &&
            before is not null &&
            after is not null)
        {
            return $"{name} changed from {before} to {after}";
        }

        if (string.Equals(change, "added", StringComparison.OrdinalIgnoreCase) &&
            after is not null)
        {
            return $"{name} was added as {after}";
        }

        if (string.Equals(change, "removed", StringComparison.OrdinalIgnoreCase) &&
            before is not null)
        {
            return $"{name} was removed (previously {before})";
        }

        return $"{name} {DescribeChange(change)}";
    }

    private static string DisplayFieldName(string key)
    {
        var trimmed = key.Trim();
        if (trimmed.Length == 0)
        {
            return "Configuration field";
        }

        if (trimmed.StartsWith("/", StringComparison.Ordinal))
        {
            trimmed = trimmed[1..];
        }

        return trimmed.Length == 0
            ? "Configuration field"
            : trimmed;
    }

    private static string DescribeChange(string change) =>
        change.Trim().ToLowerInvariant() switch
        {
            "modified" => "changed",
            "added" => "added",
            "removed" => "removed",
            _ => "changed"
        };

    private static bool TryGetString(
        JsonElement element,
        string propertyName,
        out string value)
    {
        value = string.Empty;

        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return true;
    }

    private static bool TryGetInt32(
        JsonElement element,
        string propertyName,
        out int value)
    {
        value = default;
        return element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt32(out value);
    }

    private static bool TryGetInt64(
        JsonElement element,
        string propertyName,
        out long value)
    {
        value = default;
        return element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt64(out value);
    }
}
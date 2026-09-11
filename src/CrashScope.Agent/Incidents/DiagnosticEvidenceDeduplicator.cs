using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CrashScope.Core.Diagnostics;

namespace CrashScope.Agent.Incidents;

internal sealed partial class DiagnosticEvidenceDeduplicator
{
    private readonly object _sync = new();
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);

    public bool TryAccept(DiagnosticEvent diagnosticEvent)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);
        return TryAcceptAliases(BuildAliases(diagnosticEvent));
    }

    public bool TryAccept(DiagnosticArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        return TryAcceptAliases(BuildAliases(artifact));
    }

    public void Clear()
    {
        lock (_sync)
        {
            _seen.Clear();
        }
    }

    private bool TryAcceptAliases(IReadOnlyCollection<string> aliases)
    {
        lock (_sync)
        {
            if (aliases.Any(alias => _seen.Contains(alias)))
            {
                foreach (var alias in aliases)
                {
                    _seen.Add(alias);
                }

                return false;
            }

            foreach (var alias in aliases)
            {
                _seen.Add(alias);
            }

            return true;
        }
    }

    private static IReadOnlyCollection<string> BuildAliases(
        DiagnosticArtifact artifact)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            $"artifact:{NormalizePath(artifact.ArtifactPath)}"
        };

        if (!string.IsNullOrWhiteSpace(artifact.ReportId))
        {
            aliases.Add($"report:{NormalizeToken(artifact.ReportId)}");
        }

        if (!string.IsNullOrWhiteSpace(artifact.RelatedPath))
        {
            aliases.Add($"dump:{NormalizePath(artifact.RelatedPath)}");
        }

        return aliases.ToArray();
    }

    private static IReadOnlyCollection<string> BuildAliases(
        DiagnosticEvent diagnosticEvent)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var message = diagnosticEvent.Message ?? string.Empty;

        if (diagnosticEvent.ProviderName.Equals(
                "Windows Error Reporting",
                StringComparison.OrdinalIgnoreCase))
        {
            var reportId = ReportIdRegex().Match(message);
            if (reportId.Success)
            {
                aliases.Add(
                    $"report:{NormalizeToken(reportId.Groups["value"].Value)}");
            }

            foreach (Match match in DumpPathRegex().Matches(message))
            {
                aliases.Add(
                    $"dump:{NormalizePath(match.Groups["value"].Value)}");
            }
        }

        if (diagnosticEvent.RecordId.HasValue)
        {
            aliases.Add(
                $"event:{diagnosticEvent.Source}|{diagnosticEvent.LogName}|" +
                $"{diagnosticEvent.ProviderName}|{diagnosticEvent.RecordId.Value}");
        }

        if (aliases.Count == 0)
        {
            aliases.Add(
                $"event-fallback:{Hash(
                    $"{diagnosticEvent.Source}|{diagnosticEvent.LogName}|" +
                    $"{diagnosticEvent.ProviderName}|{diagnosticEvent.EventId}|" +
                    $"{diagnosticEvent.ObservedAtUtc.UtcTicks}|{message}")}");
        }

        return aliases.ToArray();
    }

    private static string NormalizePath(string value) =>
        value.Trim()
            .Trim('"')
            .Replace('/', '\\')
            .ToUpperInvariant();

    private static string NormalizeToken(string value) =>
        value.Trim().Trim('{', '}', '(', ')', '"').ToUpperInvariant();

    private static string Hash(string value) =>
        Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    [GeneratedRegex(
        @"(?im)^\s*Report\s+(?:Id|Identifier)\s*:\s*(?<value>[^\r\n]+)")]
    private static partial Regex ReportIdRegex();

    [GeneratedRegex(
        @"(?i)(?<value>[A-Z]:\\[^\r\n]*?\.dmp)")]
    private static partial Regex DumpPathRegex();
}

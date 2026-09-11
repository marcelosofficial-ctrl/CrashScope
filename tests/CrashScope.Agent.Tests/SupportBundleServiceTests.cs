using System.IO.Compression;
using CrashScope.Agent.Support;
using CrashScope.Core.Incidents;
using CrashScope.Core.Sessions;

namespace CrashScope.Agent.Tests;

public sealed class SupportBundleServiceTests
{
    [Fact]
    public void Create_RedactsSensitiveTextAndIncludesOnlyCuratedEntries()
    {
        const string profile = @"C:\Users\private-user";
        const string user = "private-user";
        const string machine = "GAMING-RIG";
        const string email = "person@example.com";
        const string ipv4 = "192.168.1.44";
        const string ipv6 = "2001:db8:85a3::8a2e:370:7334";
        const string mac = "AA:BB:CC:DD:EE:FF";
        const string bearer = "Bearer abcdefghijklmnopqrstuvwxyz012345";
        const string apiKey = "api_key=super-private-key";
        const string password = "password:correct-horse-battery-staple";

        var redactor = new SupportBundlePrivacyRedactor(
            new PrivacyRedactionContext(profile, user, machine));
        var service = new SupportBundleService(
            redactor,
            () => Utc(2026, 9, 9, 4, 0, 0));

        var sensitiveText = $"Observed near {profile} on {machine}; contact {email}; peers {ipv4} and {ipv6}; adapter {mac}; {bearer}; {apiKey}; {password}.";
        var incident = CreateIncident(sensitiveText);
        var session = CreateSession(profile);
        var runtime = new SupportBundleRuntimeSnapshot(
            "0.1.0",
            "Background",
            3,
            2,
            90,
            true,
            $"Ready on {machine}",
            0,
            0);

        var result = service.Create(incident, session, runtime);

        using var archiveStream = new MemoryStream(result.Content);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
        var names = archive.Entries.Select(entry => entry.FullName).Order().ToArray();

        Assert.Equal(
            new[] { "README.txt", "incident.json", "manifest.json", "runtime.json", "session.json" }.Order(),
            names);
        Assert.DoesNotContain(names, name => name.EndsWith(".db", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.EndsWith(".etl", StringComparison.OrdinalIgnoreCase));

        var combined = string.Join("\n", archive.Entries.Select(ReadEntry));
        foreach (var original in new[] { profile, user, machine, email, ipv4, ipv6, mac, "abcdefghijklmnopqrstuvwxyz012345", "super-private-key", "correct-horse-battery-staple" })
        {
            Assert.DoesNotContain(original, combined, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("%USERPROFILE%", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<redacted-email>", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<redacted-ip>", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<redacted-mac>", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<redacted-secret>", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No CrashScope database is included", combined, StringComparison.Ordinal);
        Assert.Contains("Nothing in this bundle was uploaded by CrashScope", combined, StringComparison.Ordinal);

        var runtimeJson = ReadEntry(archive.GetEntry("runtime.json")!);
        Assert.Contains("\"schemaVersion\": 3", runtimeJson, StringComparison.Ordinal);
        Assert.Contains("\"settingsSchemaVersion\": 2", runtimeJson, StringComparison.Ordinal);
        Assert.Contains("\"retentionDays\": 90", runtimeJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Redact_DoesNotMistakeOrdinaryColonDelimitedTextForIpv6()
    {
        var redactor = new SupportBundlePrivacyRedactor(
            new PrivacyRedactionContext(null, null, null));

        var result = redactor.Redact("Event 41 at 04:15:22; ratio 12:34; driver 32.0.31041.1004");

        Assert.Contains("04:15:22", result, StringComparison.Ordinal);
        Assert.Contains("12:34", result, StringComparison.Ordinal);
        Assert.Contains("32.0.31041.1004", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Preview_DoesNotCreateOrAdvertiseSensitiveFileTypes()
    {
        var service = new SupportBundleService(
            new SupportBundlePrivacyRedactor(new PrivacyRedactionContext(null, null, null)));
        var preview = service.Preview(CreateIncident("safe"), session: null);

        Assert.Equal(4, preview.Entries.Count);
        Assert.Contains("incident.json", preview.Entries);
        Assert.DoesNotContain(preview.Entries, entry => entry.EndsWith(".db", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(preview.Entries, entry => entry.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(preview.PrivacyNotes, note => note.Contains("No dump", StringComparison.Ordinal));
        Assert.Contains(preview.PrivacyNotes, note => note.Contains("No CrashScope database", StringComparison.Ordinal));
        Assert.Contains(preview.PrivacyNotes, note => note.Contains("defense-in-depth", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Create_BoundsEvidenceCountAndMarksTruncation()
    {
        var evidence = Enumerable.Range(0, SupportBundleService.MaximumEvidenceItems + 10)
            .Select(index => new IncidentEvidenceItem(
                Utc(2026, 9, 9, 4, 0, 0).AddSeconds(index),
                Utc(2026, 9, 9, 4, 0, 0).AddSeconds(index),
                IncidentEvidenceRole.Context,
                "Test",
                "Synthetic",
                $"Evidence {index}"))
            .ToArray();

        var incident = new IncidentReport(
            Guid.NewGuid(),
            IncidentClassification.Unclassified,
            "Bounded export",
            "summary",
            "assessment",
            Utc(2026, 9, 9, 4, 0, 0),
            process: null,
            new IncidentTelemetrySummary(1, null, null, null, null, null, null, null),
            evidence);

        var service = new SupportBundleService(
            new SupportBundlePrivacyRedactor(new PrivacyRedactionContext(null, null, null)));
        var result = service.Create(
            incident,
            session: null,
            new SupportBundleRuntimeSnapshot("0.1.0", "Background", 3, 2, 30, true, "Ready", 0, 0));

        using var archiveStream = new MemoryStream(result.Content);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
        var incidentJson = ReadEntry(archive.GetEntry("incident.json")!);

        Assert.Contains("\"evidenceWasTruncated\": true", incidentJson, StringComparison.Ordinal);
        Assert.DoesNotContain($"Evidence {SupportBundleService.MaximumEvidenceItems + 1}", incidentJson, StringComparison.Ordinal);
    }

    private static IncidentReport CreateIncident(string sensitiveText) =>
        new(
            Guid.Parse("11111111-2222-3333-4444-555555555555"),
            IncidentClassification.ApplicationFailure,
            $"Application failure {sensitiveText}",
            $"Summary {sensitiveText}",
            $"Assessment {sensitiveText}",
            Utc(2026, 9, 9, 4, 0, 0),
            new IncidentProcessContext(
                1234,
                Utc(2026, 9, 9, 3, 55, 0),
                $"game-{sensitiveText}",
                "Exited",
                Utc(2026, 9, 9, 4, 0, 1)),
            new IncidentTelemetrySummary(
                42,
                Utc(2026, 9, 9, 3, 59, 0),
                Utc(2026, 9, 9, 4, 0, 30),
                80,
                95,
                72,
                8128,
                68),
            new[]
            {
                new IncidentEvidenceItem(
                    Utc(2026, 9, 9, 4, 0, 0),
                    Utc(2026, 9, 9, 4, 0, 1),
                    IncidentEvidenceRole.Trigger,
                    "Application Error",
                    "Fault",
                    sensitiveText,
                    evidenceKey: sensitiveText)
            });

    private static WorkloadSession CreateSession(string profile) =>
        new(
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            1234,
            Utc(2026, 9, 9, 3, 55, 0),
            "SampleGame",
            $@"{profile}\Games\SampleGame.exe",
            Utc(2026, 9, 9, 3, 55, 0),
            Utc(2026, 9, 9, 4, 0, 2),
            SessionEndReason.ProcessExited,
            new SessionTelemetrySummary(12, 80, 95, 72, 8128, 68),
            new[] { Guid.Parse("11111111-2222-3333-4444-555555555555") },
            new SessionEnvironmentSnapshot(
                Utc(2026, 9, 9, 3, 55, 0),
                "Windows 11 Pro on GAMING-RIG",
                "X64",
                "X64",
                ".NET 10 private-user",
                "0.1.0",
                "AMD Ryzen test@example.com",
                12,
                32768,
                new[] { new EnvironmentGpuSnapshot("GPU 192.168.1.44", "AA:BB:CC:DD:EE:FF") }));

    private static string ReadEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute, int second) =>
        new(year, month, day, hour, minute, second, TimeSpan.Zero);
}

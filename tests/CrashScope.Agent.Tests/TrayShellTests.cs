using CrashScope.Agent.Incidents;
using CrashScope.Agent.Runtime;
using CrashScope.Core.Incidents;

namespace CrashScope.Agent.Tests;

public sealed class TrayShellTests
{
    [Fact]
    public void DisplayState_ShowsReadyWithoutActiveWorkload()
    {
        var state = TrayDisplayState.Create(
            null,
            autoAssistEnabled: true,
            new StartupRegistrationStatus(true, false, null));

        Assert.Equal("Ready", state.StatusText);
        Assert.Equal("CrashScope - Ready", state.ToolTip);
        Assert.True(state.AutoAssistEnabled);
        Assert.False(state.Startup.Enabled);
    }

    [Fact]
    public void DisplayState_ShowsActiveProcessWithoutPollingHardware()
    {
        var state = TrayDisplayState.Create(
            "Cyberpunk2077",
            autoAssistEnabled: false,
            new StartupRegistrationStatus(true, true, null));

        Assert.Equal("Monitoring Cyberpunk2077", state.StatusText);
        Assert.Equal("CrashScope - Monitoring Cyberpunk2077", state.ToolTip);
        Assert.False(state.AutoAssistEnabled);
        Assert.True(state.Startup.Enabled);
    }

    [Fact]
    public async Task IncidentSink_RaisesEventOnlyForNewReports()
    {
        var sink = new InMemoryIncidentReportSink();
        var first = CreateReport(Guid.NewGuid(), "first");
        var added = new List<Guid>();
        sink.ReportAdded += report => added.Add(report.IncidentId);

        sink.Replace(new[] { first });
        await sink.WriteAsync(CreateReport(first.IncidentId, "updated"));
        var second = CreateReport(Guid.NewGuid(), "second");
        await sink.WriteAsync(second);

        Assert.Single(added);
        Assert.Equal(second.IncidentId, added[0]);
    }

    private static IncidentReport CreateReport(Guid id, string title) => new(
        id,
        IncidentClassification.ApplicationFailure,
        title,
        "summary",
        "assessment",
        new DateTimeOffset(2026, 9, 9, 7, 0, 0, TimeSpan.Zero),
        null,
        new IncidentTelemetrySummary(0, null, null, null, null, null, null, null),
        Array.Empty<IncidentEvidenceItem>());
}

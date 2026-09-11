using CrashScope.Agent.Incidents;
using CrashScope.Core.Incidents;

namespace CrashScope.Agent.Tests;

public sealed class PersistentIncidentReportSinkTests
{
    [Fact]
    public async Task Initialize_LoadsPersistedReportsIntoMemory()
    {
        var report = CreateReport(Guid.NewGuid());
        var repository = new FakeRepository(new[] { report });
        var memory = new InMemoryIncidentReportSink();
        var sink = new PersistentIncidentReportSink(repository, memory);

        await sink.InitializeAsync();

        Assert.True(repository.Initialized);
        Assert.Equal(report, Assert.Single(memory.Snapshot()));
    }

    [Fact]
    public async Task Write_PersistsBeforePublishingToMemory()
    {
        var repository = new FakeRepository();
        var memory = new InMemoryIncidentReportSink();
        var sink = new PersistentIncidentReportSink(repository, memory);
        var report = CreateReport(Guid.NewGuid());

        await sink.WriteAsync(report);

        Assert.Equal(report, Assert.Single(repository.Saved));
        Assert.Equal(report, Assert.Single(memory.Snapshot()));
    }

    [Fact]
    public async Task Write_WhenPersistenceFails_DoesNotPublishTransientIncident()
    {
        var repository = new FakeRepository { ThrowOnSave = true };
        var memory = new InMemoryIncidentReportSink();
        var sink = new PersistentIncidentReportSink(repository, memory);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await sink.WriteAsync(CreateReport(Guid.NewGuid())));

        Assert.Empty(memory.Snapshot());
    }

    private static IncidentReport CreateReport(Guid id)
    {
        var time = new DateTimeOffset(2026, 9, 8, 8, 0, 0, TimeSpan.Zero);
        return new IncidentReport(
            id,
            IncidentClassification.Unclassified,
            "Diagnostic incident",
            "Summary",
            "Assessment",
            time,
            null,
            new IncidentTelemetrySummary(0, null, null, null, null, null, null, null),
            new[]
            {
                new IncidentEvidenceItem(
                    time,
                    time,
                    IncidentEvidenceRole.Trigger,
                    "CrashScope",
                    "Trigger",
                    "Evidence")
            });
    }

    private sealed class FakeRepository : IIncidentReportRepository
    {
        private readonly IReadOnlyList<IncidentReport> _existing;

        public FakeRepository(IReadOnlyList<IncidentReport>? existing = null)
        {
            _existing = existing ?? Array.Empty<IncidentReport>();
        }

        public bool Initialized { get; private set; }
        public bool ThrowOnSave { get; init; }
        public List<IncidentReport> Saved { get; } = new();

        public ValueTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Initialized = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask SaveAsync(
            IncidentReport report,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ThrowOnSave)
            {
                throw new InvalidOperationException("Persistence failed.");
            }

            Saved.Add(report);
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<IncidentReport>> LoadAllAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_existing);
        }
    }
}

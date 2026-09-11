using CrashScope.Core.Incidents;

namespace CrashScope.Agent.Incidents;

internal sealed class PersistentIncidentReportSink : IIncidentReportSink
{
    private readonly IIncidentReportRepository _repository;
    private readonly InMemoryIncidentReportSink _memory;

    public PersistentIncidentReportSink(
        IIncidentReportRepository repository,
        InMemoryIncidentReportSink memory)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(memory);

        _repository = repository;
        _memory = memory;
    }

    public async ValueTask InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        await _repository.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var reports = await _repository.LoadAllAsync(cancellationToken)
            .ConfigureAwait(false);

        _memory.Replace(reports);
    }

    public async ValueTask WriteAsync(
        IncidentReport report,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);

        await _repository.SaveAsync(report, cancellationToken).ConfigureAwait(false);
        await _memory.WriteAsync(report, cancellationToken).ConfigureAwait(false);
    }
}

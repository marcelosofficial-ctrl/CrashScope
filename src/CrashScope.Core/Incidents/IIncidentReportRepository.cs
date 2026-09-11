namespace CrashScope.Core.Incidents;

public interface IIncidentReportRepository
{
    ValueTask InitializeAsync(CancellationToken cancellationToken = default);

    ValueTask SaveAsync(
        IncidentReport report,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<IncidentReport>> LoadAllAsync(
        CancellationToken cancellationToken = default);
}

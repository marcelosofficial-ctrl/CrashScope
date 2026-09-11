using CrashScope.Core.Incidents;

namespace CrashScope.Agent.Incidents;

internal interface IIncidentReportSink
{
    ValueTask WriteAsync(
        IncidentReport report,
        CancellationToken cancellationToken = default);
}

internal sealed class InMemoryIncidentReportSink : IIncidentReportSink
{
    private readonly object _sync = new();
    private readonly List<IncidentReport> _reports = new();

    public event Action<IncidentReport>? ReportAdded;

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _reports.Count;
            }
        }
    }

    public IReadOnlyList<IncidentReport> Snapshot()
    {
        lock (_sync)
        {
            return _reports.ToArray();
        }
    }

    public void Replace(IEnumerable<IncidentReport> reports)
    {
        ArgumentNullException.ThrowIfNull(reports);

        var replacement = reports
            .OrderBy(x => x.IncidentTimeUtc)
            .ThenBy(x => x.IncidentId)
            .ToArray();

        lock (_sync)
        {
            _reports.Clear();
            _reports.AddRange(replacement);
        }
    }

    public ValueTask WriteAsync(
        IncidentReport report,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        cancellationToken.ThrowIfCancellationRequested();

        var added = false;
        lock (_sync)
        {
            var existingIndex = _reports.FindIndex(
                candidate => candidate.IncidentId == report.IncidentId);

            if (existingIndex >= 0)
            {
                _reports[existingIndex] = report;
            }
            else
            {
                _reports.Add(report);
                added = true;
            }

            _reports.Sort(static (left, right) =>
            {
                var timeComparison = left.IncidentTimeUtc.CompareTo(right.IncidentTimeUtc);
                return timeComparison != 0
                    ? timeComparison
                    : left.IncidentId.CompareTo(right.IncidentId);
            });
        }

        if (added)
        {
            ReportAdded?.Invoke(report);
        }

        return ValueTask.CompletedTask;
    }
}

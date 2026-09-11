using CrashScope.Agent.Incidents;
using CrashScope.Agent.Product;
using CrashScope.Agent.Sampling;
using CrashScope.Agent.Sessions;
using CrashScope.Agent.Settings;
using CrashScope.Infrastructure.Persistence;

namespace CrashScope.Agent.Runtime;

internal sealed class CrashScopeRuntimeHostedService : BackgroundService
{
    private readonly CentralTelemetrySampler _sampler;
    private readonly LiveIncidentMonitor _incidentMonitor;
    private readonly PersistentIncidentReportSink _incidentSink;
    private readonly WorkloadSessionManager _sessions;
    private readonly AutomaticWorkloadMonitor _automaticWorkloads;
    private readonly SqliteSessionEnvironmentStore _environmentStore;
    private readonly SqliteIncidentReportRepository _incidentRepository;
    private readonly SqliteWorkloadSessionRepository _sessionRepository;
    private readonly SqliteRetentionPruner _retentionPruner;
    private readonly CrashScopeSettingsState _settings;
    private readonly PersistenceMaintenanceGate _maintenanceGate;

    public CrashScopeRuntimeHostedService(
        CentralTelemetrySampler sampler,
        LiveIncidentMonitor incidentMonitor,
        PersistentIncidentReportSink incidentSink,
        WorkloadSessionManager sessions,
        AutomaticWorkloadMonitor automaticWorkloads,
        SqliteSessionEnvironmentStore environmentStore,
        SqliteIncidentReportRepository incidentRepository,
        SqliteWorkloadSessionRepository sessionRepository,
        SqliteRetentionPruner retentionPruner,
        CrashScopeSettingsState settings,
        PersistenceMaintenanceGate maintenanceGate)
    {
        ArgumentNullException.ThrowIfNull(sampler);
        ArgumentNullException.ThrowIfNull(incidentMonitor);
        ArgumentNullException.ThrowIfNull(incidentSink);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(automaticWorkloads);
        ArgumentNullException.ThrowIfNull(environmentStore);
        ArgumentNullException.ThrowIfNull(incidentRepository);
        ArgumentNullException.ThrowIfNull(sessionRepository);
        ArgumentNullException.ThrowIfNull(retentionPruner);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(maintenanceGate);

        _sampler = sampler;
        _incidentMonitor = incidentMonitor;
        _incidentSink = incidentSink;
        _sessions = sessions;
        _automaticWorkloads = automaticWorkloads;
        _environmentStore = environmentStore;
        _incidentRepository = incidentRepository;
        _sessionRepository = sessionRepository;
        _retentionPruner = retentionPruner;
        _settings = settings;
        _maintenanceGate = maintenanceGate;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _incidentRepository.InitializeAsync(stoppingToken).ConfigureAwait(false);
        await _sessionRepository.InitializeAsync(stoppingToken).ConfigureAwait(false);
        await _environmentStore.InitializeAsync(stoppingToken).ConfigureAwait(false);

        var retention = _settings.Snapshot();
        var cutoffUtc = DateTimeOffset.UtcNow.AddDays(-retention.RetentionDays);
        using (await _maintenanceGate.EnterAsync(stoppingToken).ConfigureAwait(false))
        {
            await _retentionPruner.PruneAsync(cutoffUtc, stoppingToken).ConfigureAwait(false);
        }

        await _incidentSink.InitializeAsync(stoppingToken).ConfigureAwait(false);
        await _sessions.InitializeAsync(stoppingToken).ConfigureAwait(false);

        var samplingTask = _sampler.RunAsync(stoppingToken);
        var incidentTask = RunIncidentMonitorAsync(stoppingToken);
        var sessionTask = RunSessionMonitorAsync(stoppingToken);
        var automaticWorkloadTask = RunAutomaticWorkloadMonitorAsync(stoppingToken);

        await Task.WhenAll(
            samplingTask,
            incidentTask,
            sessionTask,
            automaticWorkloadTask).ConfigureAwait(false);
    }

    private async Task RunIncidentMonitorAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _incidentMonitor.RunAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task RunSessionMonitorAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _sessions.RunAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task RunAutomaticWorkloadMonitorAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _automaticWorkloads.RunAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}

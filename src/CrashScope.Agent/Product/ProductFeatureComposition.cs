using CrashScope.Agent.Evidence.ConfigTrace;
using CrashScope.Agent.Incidents;
using CrashScope.Agent.Runtime;
using CrashScope.Agent.Sampling;
using CrashScope.Agent.Sessions;
using CrashScope.Agent.Settings;
using CrashScope.Agent.Streaming;
using CrashScope.Agent.Support;
using CrashScope.Core.Diagnostics;
using CrashScope.Core.Evidence;
using CrashScope.Core.Incidents;
using CrashScope.Core.Sessions;
using CrashScope.Infrastructure.Diagnostics;
using CrashScope.Infrastructure.Persistence;

namespace CrashScope.Agent.Product;

internal sealed record ConfigTraceRootUpdate(string? RootPath);

internal static class ProductFeatureComposition
{
    public static void AddServices(IServiceCollection services, string localDataRoot)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(localDataRoot);

        var productDataRoot = Path.Combine(localDataRoot, "CrashScope");
        var settingsPath = Path.Combine(productDataRoot, "settings.json");
        var databasePath = Path.Combine(productDataRoot, "crashscope.db");
        var executablePath = Environment.ProcessPath
            ?? Path.Combine(AppContext.BaseDirectory, "CrashScope.exe");

        services.AddSingleton(new CrashScopeSettingsState(settingsPath));

        services.AddSingleton<ConfigTraceEvidenceProvider>(sp =>
        {
            var settings = sp.GetRequiredService<CrashScopeSettingsState>();

            return new ConfigTraceEvidenceProvider(() =>
            {
                var snapshot = settings.Snapshot();

                return new ConfigTraceEvidenceProviderOptions
                {
                    Enabled = snapshot.ConfigTraceEnabled,
                    ExecutablePath = ConfigTraceRuntimePaths.ResolveExecutablePath(),
                    RootPath = snapshot.ConfigTraceRootPath ?? string.Empty,
                    JournalDirectory = ConfigTraceRuntimePaths.ResolveJournalDirectory(productDataRoot)
                };
            });
        });
        services.AddSingleton<IEvidenceProvider>(sp =>
            sp.GetRequiredService<ConfigTraceEvidenceProvider>());

        services.AddSingleton(new SqliteRetentionPruner(databasePath));
        services.AddSingleton<PersistenceMaintenanceGate>();
        services.AddSingleton(new WindowsStartupRegistration(executablePath));
        services.AddSingleton<TrayControlService>();
        services.AddSingleton<WindowsTrayHost>();
        services.AddHostedService(sp => sp.GetRequiredService<WindowsTrayHost>());
        services.AddSingleton<WindowsWerReportStoreSource>();
        services.AddSingleton(PrivacyRedactionContext.Current);
        services.AddSingleton<SupportBundlePrivacyRedactor>();
        services.AddSingleton<SupportBundleService>();
    }

    public static void MapEndpoints(WebApplication app, string crashScopeVersion)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentException.ThrowIfNullOrWhiteSpace(crashScopeVersion);

        app.MapGet("/api/settings", (CrashScopeSettingsState settings) =>
            Results.Ok(settings.Snapshot()));

        app.MapPut("/api/settings/auto-assist/{enabled:bool}", async (
            bool enabled,
            CrashScopeSettingsState settings,
            AutomaticWorkloadMonitor automaticWorkloads,
            CancellationToken cancellationToken) =>
        {
            var updated = await settings.UpdateAutoAssistAsync(enabled, cancellationToken).ConfigureAwait(false);
            return Results.Ok(new { settings = updated, automaticMonitoring = automaticWorkloads.StatusSnapshot() });
        });

        app.MapPut("/api/settings/retention/{days:int}", async (
            int days,
            CrashScopeSettingsState settings,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await settings.UpdateRetentionDaysAsync(days, cancellationToken).ConfigureAwait(false));
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPut("/api/settings/configtrace/root", async (
            ConfigTraceRootUpdate update,
            CrashScopeSettingsState settings,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(
                    await settings
                        .UpdateConfigTraceRootAsync(update.RootPath, cancellationToken)
                        .ConfigureAwait(false));
            }
            catch (Exception ex) when (
                ex is ArgumentException or
                DirectoryNotFoundException)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPut("/api/settings/configtrace/{enabled:bool}", async (
            bool enabled,
            CrashScopeSettingsState settings,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(
                    await settings
                        .UpdateConfigTraceEnabledAsync(enabled, cancellationToken)
                        .ConfigureAwait(false));
            }
            catch (Exception ex) when (
                ex is InvalidOperationException or
                DirectoryNotFoundException)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
        app.MapGet("/api/settings/startup", (WindowsStartupRegistration startup) =>
            Results.Ok(startup.GetStatus()));

        app.MapPut("/api/settings/startup/{enabled:bool}", (bool enabled, WindowsStartupRegistration startup) =>
            Results.Ok(startup.SetEnabled(enabled)));

        app.MapGet("/api/diagnostics/wer-store/probe", async (
            int? days,
            WindowsWerReportStoreSource nativeWer,
            IDiagnosticArtifactSource existingArtifacts,
            CancellationToken cancellationToken) =>
        {
            var windowDays = Math.Clamp(days ?? 7, 1, 30);
            var sinceUtc = DateTimeOffset.UtcNow.AddDays(-windowDays);
            var native = await nativeWer
                .ReadSinceAsync(sinceUtc, 512, cancellationToken)
                .ConfigureAwait(false);
            var existing = await existingArtifacts
                .ReadSinceAsync(sinceUtc, 512, cancellationToken)
                .ConfigureAwait(false);

            var existingWer = existing
                .Where(item => item.Kind == DiagnosticArtifactKind.WindowsErrorReport)
                .ToArray();
            var existingIds = existingWer
                .Select(item => item.ReportId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var nativeIds = native
                .Select(item => item.ReportId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var matchingReportIds = nativeIds.Count(id => existingIds.Contains(id));
            var nativeOnly = native
                .Where(item => string.IsNullOrWhiteSpace(item.ReportId) || !existingIds.Contains(item.ReportId))
                .Take(50)
                .Select(item => new
                {
                    item.ReportId,
                    item.EventType,
                    occurredAtUtc = item.SourceOccurredAtUtc,
                    store = item.Properties.TryGetValue("Store", out var store) ? store : null,
                    numberOfFiles = item.Properties.TryGetValue("NumberOfFiles", out var fileCount) ? fileCount : null
                })
                .ToArray();
            var existingOnly = existingWer
                .Where(item => string.IsNullOrWhiteSpace(item.ReportId) || !nativeIds.Contains(item.ReportId))
                .Take(50)
                .Select(item => new
                {
                    item.ReportId,
                    item.EventType,
                    occurredAtUtc = item.SourceOccurredAtUtc,
                    item.Source
                })
                .ToArray();

            return Results.Ok(new
            {
                mode = "on-demand-read-only-comparison",
                windowDays,
                nativeProbe = nativeWer.LastProbeSnapshot,
                nativeReportCount = native.Count,
                existingWerReportCount = existingWer.Length,
                matchingReportIds,
                nativeOnly,
                existingOnly,
                note = "Native WER metadata is not part of live incident triggering yet. This endpoint performs an explicit read-only comparison and never reads dump bytes."
            });
        });

        app.MapGet("/api/incidents/{incidentId:guid}/support-bundle/preview", async (
            Guid incidentId,
            InMemoryIncidentReportSink incidents,
            WorkloadSessionManager sessions,
            SqliteSessionEnvironmentStore environments,
            SupportBundleService bundles,
            PersistenceMaintenanceGate maintenanceGate,
            CancellationToken cancellationToken) =>
        {
            using var lease = await maintenanceGate.EnterAsync(cancellationToken).ConfigureAwait(false);
            var incident = incidents.Snapshot().FirstOrDefault(item => item.IncidentId == incidentId);
            if (incident is null) return Results.NotFound(new { error = "Incident was not found." });

            var session = await FindRelatedSessionAsync(incidentId, sessions, environments, cancellationToken).ConfigureAwait(false);
            return Results.Ok(bundles.Preview(incident, session));
        });

        app.MapPost("/api/incidents/{incidentId:guid}/support-bundle", async (
            Guid incidentId,
            InMemoryIncidentReportSink incidents,
            WorkloadSessionManager sessions,
            SqliteSessionEnvironmentStore environments,
            SamplingModeController sampling,
            AutomaticWorkloadMonitor automaticWorkloads,
            CrashScopeSettingsState settings,
            TelemetryStreamHub stream,
            SupportBundleService bundles,
            PersistenceMaintenanceGate maintenanceGate,
            CancellationToken cancellationToken) =>
        {
            using var lease = await maintenanceGate.EnterAsync(cancellationToken).ConfigureAwait(false);
            var incident = incidents.Snapshot().FirstOrDefault(item => item.IncidentId == incidentId);
            if (incident is null) return Results.NotFound(new { error = "Incident was not found." });

            var session = await FindRelatedSessionAsync(incidentId, sessions, environments, cancellationToken).ConfigureAwait(false);
            var automatic = automaticWorkloads.StatusSnapshot();
            var settingsSnapshot = settings.Snapshot();
            var streamDiagnostics = stream.Diagnostics;
            var runtime = new SupportBundleRuntimeSnapshot(
                crashScopeVersion,
                sampling.Current.ToString(),
                SqliteSessionEnvironmentStore.CurrentSchemaVersion,
                settingsSnapshot.SchemaVersion,
                settingsSnapshot.RetentionDays,
                automatic.Enabled,
                automatic.State,
                streamDiagnostics.DroppedStaleFrames,
                streamDiagnostics.DeliveryMisses);

            var result = bundles.Create(incident, session, runtime);
            return Results.File(result.Content, "application/zip", result.FileName, enableRangeProcessing: false);
        });
    }

    private static async ValueTask<WorkloadSession?> FindRelatedSessionAsync(
        Guid incidentId,
        WorkloadSessionManager sessions,
        SqliteSessionEnvironmentStore environments,
        CancellationToken cancellationToken)
    {
        var session = sessions.Snapshot().FirstOrDefault(item => item.IncidentIds.Contains(incidentId));
        if (session is null) return null;

        var environment = await environments.LoadAsync(session.SessionId, cancellationToken).ConfigureAwait(false);
        return new WorkloadSession(
            session.SessionId,
            session.ProcessId,
            session.ProcessStartTimeUtc,
            session.ProcessName,
            session.ExecutablePath,
            session.StartedAtUtc,
            session.EndedAtUtc,
            session.EndReason,
            session.Telemetry,
            session.IncidentIds,
            environment);
    }
}

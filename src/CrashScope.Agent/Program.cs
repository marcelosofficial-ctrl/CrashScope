using System.Net.WebSockets;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using CrashScope.Agent.Buffering;
using CrashScope.Agent.Incidents;
using CrashScope.Agent.Processes;
using CrashScope.Agent.Runtime;
using CrashScope.Agent.Sampling;
using CrashScope.Agent.Security;
using CrashScope.Agent.Sessions;
using CrashScope.Agent.Streaming;
using CrashScope.Core.Diagnostics;
using CrashScope.Core.Incidents;
using CrashScope.Core.Sessions;
using CrashScope.Core.Telemetry;
using CrashScope.Infrastructure.Diagnostics;
using CrashScope.Infrastructure.Persistence;
using CrashScope.Infrastructure.Sessions;
using CrashScope.Infrastructure.Telemetry.LibreHardwareMonitor;

const int port = 5077;
var launchOptions = CrashScopeLaunchOptions.Parse(args);
var dashboardUri = new Uri($"http://localhost:{port}/");
var crashScopeVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "unknown";

using (var existingInstanceClient = new HttpClient
{
    Timeout = TimeSpan.FromMilliseconds(750)
})
{
    var existingInstanceProbe = new ExistingCrashScopeInstanceProbe(existingInstanceClient);
    if (await existingInstanceProbe.IsRunningAsync(dashboardUri))
    {
        Console.WriteLine($"CrashScope is already running at {dashboardUri}");
        if (launchOptions.OpenBrowser
            && !ExistingCrashScopeInstanceProbe.TryOpenDashboard(dashboardUri))
        {
            Console.WriteLine("Open the existing CrashScope dashboard in your browser.");
        }

        return;
    }
}

var packagedWebRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = launchOptions.HostArguments,
    WebRootPath = Directory.Exists(packagedWebRoot) ? packagedWebRoot : null
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(port);
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddSingleton<LibreHardwareMonitorTelemetryProvider>();
builder.Services.AddSingleton<IHardwareTelemetryProvider>(sp =>
    sp.GetRequiredService<LibreHardwareMonitorTelemetryProvider>());

builder.Services.AddSingleton<SystemSamplingClock>();
builder.Services.AddSingleton<ISamplingClock>(sp =>
    sp.GetRequiredService<SystemSamplingClock>());
builder.Services.AddSingleton<SamplingModeController>();
builder.Services.AddSingleton<ISamplingModeSource>(sp =>
    sp.GetRequiredService<SamplingModeController>());
builder.Services.AddSingleton(SamplingIntervals.Default);

builder.Services.AddSingleton<SystemProcessProbe>();
builder.Services.AddSingleton<IProcessProbe>(sp =>
    sp.GetRequiredService<SystemProcessProbe>());
builder.Services.AddSingleton<MonitoredProcessTracker>();
builder.Services.AddSingleton<ForegroundProcessObserver>();
builder.Services.AddSingleton<IForegroundProcessObserver>(sp =>
    sp.GetRequiredService<ForegroundProcessObserver>());
builder.Services.AddSingleton<WorkloadDiscoveryService>();

builder.Services.AddSingleton<WindowsSessionEnvironmentSnapshotProvider>();
builder.Services.AddSingleton<ISessionEnvironmentSnapshotProvider>(sp =>
    sp.GetRequiredService<WindowsSessionEnvironmentSnapshotProvider>());

var localDataRoot = Environment.GetFolderPath(
    Environment.SpecialFolder.LocalApplicationData);
var databasePath = Path.Combine(
    localDataRoot,
    "CrashScope",
    "crashscope.db");

CrashScope.Agent.Product.ProductFeatureComposition.AddServices(
    builder.Services,
    localDataRoot);

builder.Services.AddSingleton(new SqliteIncidentReportRepository(databasePath));
builder.Services.AddSingleton<IIncidentReportRepository>(sp =>
    sp.GetRequiredService<SqliteIncidentReportRepository>());
builder.Services.AddSingleton(new SqliteWorkloadSessionRepository(databasePath));
builder.Services.AddSingleton<IWorkloadSessionRepository>(sp =>
    sp.GetRequiredService<SqliteWorkloadSessionRepository>());
builder.Services.AddSingleton(new SqliteSessionEnvironmentStore(databasePath));

builder.Services.AddSingleton<WorkloadSessionManager>();
builder.Services.AddSingleton<AutomaticWorkloadMonitor>();

builder.Services.AddSingleton<TelemetryRingBuffer>();
builder.Services.AddSingleton<IncidentCoordinator>();
builder.Services.AddSingleton<TelemetryStreamHub>();

builder.Services.AddSingleton<CentralTelemetrySampler>(sp =>
    new CentralTelemetrySampler(
        sp.GetRequiredService<IHardwareTelemetryProvider>(),
        new ITelemetryFrameSink[]
        {
            sp.GetRequiredService<IncidentCoordinator>(),
            sp.GetRequiredService<WorkloadSessionManager>(),
            sp.GetRequiredService<TelemetryStreamHub>()
        },
        sp.GetRequiredService<ISamplingModeSource>(),
        sp.GetRequiredService<SamplingIntervals>(),
        sp.GetRequiredService<ISamplingClock>()));

builder.Services.AddSingleton<WindowsEventLogDiagnosticSource>();
builder.Services.AddSingleton<IDiagnosticEventSource>(sp =>
    sp.GetRequiredService<WindowsEventLogDiagnosticSource>());

builder.Services.AddSingleton<WindowsDiagnosticArtifactSource>(sp =>
    new WindowsDiagnosticArtifactSource(
        eventFallbackSource: sp.GetRequiredService<IDiagnosticEventSource>()));
builder.Services.AddSingleton<IDiagnosticArtifactSource>(sp =>
    sp.GetRequiredService<WindowsDiagnosticArtifactSource>());

builder.Services.AddSingleton<DiagnosticEvidenceDeduplicator>();
builder.Services.AddSingleton<IncidentReportBuilder>();
builder.Services.AddSingleton<InMemoryIncidentReportSink>();
builder.Services.AddSingleton<PersistentIncidentReportSink>();
builder.Services.AddSingleton<SessionAssociatingIncidentReportSink>();
builder.Services.AddSingleton<IIncidentReportSink>(sp =>
    sp.GetRequiredService<SessionAssociatingIncidentReportSink>());
builder.Services.AddSingleton<ManualDiagnosticCaptureService>();

builder.Services.AddSingleton<LiveIncidentMonitor>(sp =>
    new LiveIncidentMonitor(
        sp.GetRequiredService<IDiagnosticEventSource>(),
        sp.GetRequiredService<IDiagnosticArtifactSource>(),
        sp.GetRequiredService<DiagnosticEvidenceDeduplicator>(),
        sp.GetRequiredService<IncidentCoordinator>(),
        sp.GetRequiredService<IncidentReportBuilder>(),
        sp.GetRequiredService<IIncidentReportSink>(),
        sp.GetRequiredService<ISamplingClock>(),
        scanInterval: TimeSpan.FromSeconds(5),
        processObservationProvider: sp
            .GetRequiredService<WorkloadSessionManager>()
            .ObserveActiveProcessAsync));

builder.Services.AddHostedService<CrashScopeRuntimeHostedService>();

var app = builder.Build();
var originPolicy = new LoopbackBrowserOriginPolicy(port);

app.Use(async (context, next) =>
{
    BrowserSecurityHeaders.Apply(context.Response.Headers, port);

    var origin = context.Request.Headers.Origin.ToString();
    if (!originPolicy.IsAllowed(origin))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            error = "Cross-origin browser requests are not allowed."
        });
        return;
    }

    await next();
});

app.UseWebSockets();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api", () => Results.Ok(new
{
    name = "CrashScope.Agent",
    version = crashScopeVersion,
    status = "running",
    api = "/api/status"
}));

app.MapGet("/api/status", (
    TelemetryRingBuffer buffer,
    SamplingModeController mode,
    InMemoryIncidentReportSink incidents,
    WorkloadSessionManager sessions,
    AutomaticWorkloadMonitor automaticWorkloads,
    TelemetryStreamHub stream,
    ManualDiagnosticCaptureService manualCapture) =>
{
    var snapshot = buffer.Snapshot();
    var latest = snapshot.LastOrDefault();
    var active = sessions.ActiveSnapshot();
    var streamDiagnostics = stream.Diagnostics;
    var automatic = automaticWorkloads.StatusSnapshot();

    return Results.Ok(new
    {
        status = "running",
        crashScopeVersion,
        samplingMode = mode.Current.ToString(),
        telemetryFramesBuffered = snapshot.Count,
        latestSequence = latest?.Sequence,
        latestTimestampUtc = latest?.TimestampUtc,
        incidentCount = incidents.Count,
        sessionCount = sessions.Snapshot().Count,
        activeSessionId = active?.SessionId,
        activeProcessName = active?.ProcessName,
        automaticMonitoringEnabled = automatic.Enabled,
        automaticMonitoringState = automatic.State,
        automaticCandidateProcessName = automatic.CandidateProcessName,
        automaticCandidateScore = automatic.CandidateScore,
        automaticConfirmationCount = automatic.ConfirmationCount,
        automaticSuppressedUntilUtc = automatic.SuppressedUntilUtc,
        automaticLastAttachReason = automatic.LastAutoAttachReason,
        automaticObservationIntervalSeconds = automatic.ObservationIntervalSeconds,
        manualCaptureInProgress = manualCapture.IsCaptureInProgress,
        streamSubscribers = streamDiagnostics.SubscriberCount,
        streamFramesPublished = streamDiagnostics.FramesPublished,
        streamDeliveryAttempts = streamDiagnostics.DeliveryAttempts,
        streamDroppedStaleFrames = streamDiagnostics.DroppedStaleFrames,
        streamDeliveryMisses = streamDiagnostics.DeliveryMisses,
        persistence = "sqlite",
        schemaVersion = SqliteSessionEnvironmentStore.CurrentSchemaVersion,
        diagnosticsScanSeconds = 5,
        backgroundSampleSeconds = SamplingIntervals.Default.Background.TotalSeconds,
        activeSampleSeconds = SamplingIntervals.Default.Active.TotalSeconds
    });
});

CrashScope.Agent.Product.ProductFeatureComposition.MapEndpoints(
    app,
    crashScopeVersion);

app.MapGet("/api/telemetry/latest", (TelemetryRingBuffer buffer) =>
{
    var latest = buffer.Snapshot().LastOrDefault();
    return latest is null
        ? Results.NoContent()
        : Results.Ok(latest);
});

app.MapGet("/api/incidents", (InMemoryIncidentReportSink incidents) =>
    Results.Ok(incidents.Snapshot()));

app.MapGet("/api/incidents/{incidentId:guid}", (
    Guid incidentId,
    InMemoryIncidentReportSink incidents) =>
{
    var report = incidents.Snapshot().FirstOrDefault(x => x.IncidentId == incidentId);
    return report is null ? Results.NotFound() : Results.Ok(report);
});

app.MapPost("/api/incidents/capture-marker", async (
    ManualDiagnosticCaptureService capture,
    CancellationToken cancellationToken) =>
{
    try
    {
        var report = await capture.CaptureAsync(cancellationToken);
        return Results.Ok(report);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
});

app.MapGet("/api/workloads", (
    int? maximum,
    bool? includeUnlikely,
    WorkloadDiscoveryService discovery) =>
    Results.Ok(discovery.Discover(maximum ?? 50, includeUnlikely ?? false)));

app.MapGet("/api/sessions", async (
    WorkloadSessionManager sessions,
    SqliteSessionEnvironmentStore environments,
    CancellationToken cancellationToken) =>
{
    var result = new List<WorkloadSession>();
    foreach (var session in sessions.Snapshot())
    {
        var environment = await environments.LoadAsync(session.SessionId, cancellationToken);
        result.Add(WithEnvironment(session, environment));
    }

    return Results.Ok(result);
});

app.MapGet("/api/sessions/active", async (
    WorkloadSessionManager sessions,
    SqliteSessionEnvironmentStore environments,
    CancellationToken cancellationToken) =>
{
    var active = sessions.ActiveSnapshot();
    if (active is null)
    {
        return Results.NoContent();
    }

    var environment = await environments.LoadAsync(active.SessionId, cancellationToken);
    return Results.Ok(WithEnvironment(active, environment));
});

app.MapGet("/api/sessions/{sessionId:guid}", async (
    Guid sessionId,
    WorkloadSessionManager sessions,
    SqliteSessionEnvironmentStore environments,
    CancellationToken cancellationToken) =>
{
    var session = sessions.Snapshot().FirstOrDefault(x => x.SessionId == sessionId);
    if (session is null)
    {
        return Results.NotFound();
    }

    var environment = await environments.LoadAsync(sessionId, cancellationToken);
    return Results.Ok(WithEnvironment(session, environment));
});

app.MapGet("/api/live", async (
    HttpContext context,
    TelemetryStreamHub stream,
    SamplingModeController mode,
    WorkloadSessionManager sessions,
    InMemoryIncidentReportSink incidents) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(
            new { error = "WebSocket upgrade required." },
            context.RequestAborted);
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    using var subscription = stream.Subscribe();
    var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    jsonOptions.Converters.Add(new JsonStringEnumConverter());

    try
    {
        await foreach (var frame in subscription.Reader.ReadAllAsync(context.RequestAborted))
        {
            var active = sessions.ActiveSnapshot();
            var payload = JsonSerializer.SerializeToUtf8Bytes(new
            {
                type = "telemetry",
                telemetry = frame,
                status = new
                {
                    samplingMode = mode.Current.ToString(),
                    activeSessionId = active?.SessionId,
                    activeProcessName = active?.ProcessName,
                    incidentCount = incidents.Count
                }
            }, jsonOptions);

            await socket.SendAsync(
                payload,
                WebSocketMessageType.Text,
                endOfMessage: true,
                context.RequestAborted);
        }
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
    }
    catch (WebSocketException)
    {
    }
});

app.MapPost("/api/sessions/attach/{processId:int}", async (
    int processId,
    WorkloadSessionManager sessions,
    ISessionEnvironmentSnapshotProvider environmentProvider,
    SqliteSessionEnvironmentStore environments,
    CancellationToken cancellationToken) =>
{
    var result = await sessions.AttachAsync(processId, cancellationToken);
    return await CompleteAttachAsync(
        result,
        environmentProvider,
        environments,
        cancellationToken);
});

app.MapPost("/api/sessions/attach-candidate/{processId:int}", async (
    int processId,
    DateTimeOffset processStartTimeUtc,
    WorkloadSessionManager sessions,
    ISessionEnvironmentSnapshotProvider environmentProvider,
    SqliteSessionEnvironmentStore environments,
    CancellationToken cancellationToken) =>
{
    if (processStartTimeUtc.Offset != TimeSpan.Zero)
    {
        return Results.BadRequest(new
        {
            error = "processStartTimeUtc must include a UTC (+00:00 or Z) offset."
        });
    }

    var result = await sessions.AttachCandidateAsync(
        processId,
        processStartTimeUtc,
        cancellationToken);
    return await CompleteAttachAsync(
        result,
        environmentProvider,
        environments,
        cancellationToken);
});

app.MapPost("/api/sessions/stop", async (
    WorkloadSessionManager sessions,
    AutomaticWorkloadMonitor automaticWorkloads,
    CancellationToken cancellationToken) =>
{
    var stopped = await sessions.StopAsync(cancellationToken);
    if (stopped is null)
    {
        return Results.Conflict(new { error = "No workload session is active." });
    }

    automaticWorkloads.SuppressAfterManualStop();
    return Results.Ok(stopped);
});

app.MapPost("/api/sampling/{mode}", (
    string mode,
    SamplingModeController controller,
    WorkloadSessionManager sessions) =>
{
    if (mode.Equals("background", StringComparison.OrdinalIgnoreCase))
    {
        if (sessions.ActiveSnapshot() is not null)
        {
            return Results.Conflict(new
            {
                error = "Background sampling cannot be forced while a workload session is active."
            });
        }

        controller.SetMode(SamplingMode.Background);
        return Results.Ok(new { samplingMode = "Background" });
    }

    if (mode.Equals("active", StringComparison.OrdinalIgnoreCase))
    {
        controller.SetMode(SamplingMode.Active);
        return Results.Ok(new { samplingMode = "Active" });
    }

    return Results.BadRequest(new
    {
        error = "Sampling mode must be 'background' or 'active'."
    });
});

if (launchOptions.OpenBrowser)
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        if (!ExistingCrashScopeInstanceProbe.TryOpenDashboard(dashboardUri))
        {
            Console.WriteLine($"CrashScope is running at {dashboardUri}");
        }
    });
}

app.Run();

static async Task<IResult> CompleteAttachAsync(
    SessionAttachResult result,
    ISessionEnvironmentSnapshotProvider environmentProvider,
    SqliteSessionEnvironmentStore environments,
    CancellationToken cancellationToken)
{
    if (!result.IsAttached)
    {
        return Results.Conflict(new { error = result.Detail });
    }

    SessionEnvironmentSnapshot? environment = null;
    try
    {
        environment = await environmentProvider.CaptureAsync(cancellationToken);
        await environments.SaveAsync(result.Session!.SessionId, environment, cancellationToken);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        environment = null;
    }

    return Results.Ok(WithEnvironment(result.Session!, environment));
}

static WorkloadSession WithEnvironment(
    WorkloadSession session,
    SessionEnvironmentSnapshot? environment) =>
    new(
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

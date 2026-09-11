using CrashScope.Core.Evidence;

namespace CrashScope.Agent.Evidence.ConfigTrace;

internal sealed record ConfigTraceEvidenceProviderOptions
{
    public bool Enabled { get; init; }

    public string ExecutablePath { get; init; } = string.Empty;

    public string RootPath { get; init; } = string.Empty;

    public string JournalDirectory { get; init; } = string.Empty;

    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(3);

    public TimeSpan SettleTime { get; init; } = TimeSpan.FromMilliseconds(200);

    public TimeSpan ReadinessPollInterval { get; init; } = TimeSpan.FromMilliseconds(50);
}

internal sealed class ConfigTraceEvidenceProvider : IEvidenceProvider
{
    private readonly Func<ConfigTraceEvidenceProviderOptions> _optionsFactory;
    private readonly IConfigTraceProcessLauncher _launcher;
    private readonly ConfigTraceJournalReader _journalReader;

    public ConfigTraceEvidenceProvider(
        ConfigTraceEvidenceProviderOptions options,
        IConfigTraceProcessLauncher? launcher = null,
        ConfigTraceJournalReader? journalReader = null)
        : this(
            () => options,
            launcher,
            journalReader)
    {
        ArgumentNullException.ThrowIfNull(options);
    }

    public ConfigTraceEvidenceProvider(
        Func<ConfigTraceEvidenceProviderOptions> optionsFactory,
        IConfigTraceProcessLauncher? launcher = null,
        ConfigTraceJournalReader? journalReader = null)
    {
        ArgumentNullException.ThrowIfNull(optionsFactory);

        _optionsFactory = optionsFactory;
        _launcher = launcher ?? new ConfigTraceProcessLauncher();
        _journalReader = journalReader ?? new ConfigTraceJournalReader();
    }

    public string ProviderName => "ConfigTrace";

    public bool IsEnabled => ResolveOptions().Enabled;

    public async ValueTask<IEvidenceProviderSession> StartAsync(
        EvidenceProviderSessionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = ResolveOptions();
        if (!options.Enabled)
        {
            throw new InvalidOperationException(
                "ConfigTrace provider cannot start while disabled.");
        }

        ValidateOptions(options);

        if (!File.Exists(options.ExecutablePath))
        {
            throw new FileNotFoundException(
                "ConfigTrace executable was not found.",
                options.ExecutablePath);
        }

        if (!Directory.Exists(options.RootPath))
        {
            throw new DirectoryNotFoundException(
                $"ConfigTrace root directory was not found: {options.RootPath}");
        }

        Directory.CreateDirectory(options.JournalDirectory);

        var journalPath = Path.Combine(
            options.JournalDirectory,
            $"configtrace-{context.SessionId:N}.jsonl");

        if (File.Exists(journalPath))
        {
            File.Delete(journalPath);
        }

        var process = _launcher.Start(new ConfigTraceProcessStartRequest(
            options.ExecutablePath,
            options.RootPath,
            journalPath,
            options.SettleTime,
            $"CrashScope-{context.SessionId:N}"));

        var session = new ConfigTraceEvidenceProviderSession(
            process,
            journalPath,
            _journalReader);

        try
        {
            await WaitForSessionStartAsync(
                    session,
                    options,
                    cancellationToken)
                .ConfigureAwait(false);

            return session;
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private ConfigTraceEvidenceProviderOptions ResolveOptions() =>
        _optionsFactory()
        ?? throw new InvalidOperationException(
            "ConfigTrace options factory returned no options.");

    private static async ValueTask WaitForSessionStartAsync(
        ConfigTraceEvidenceProviderSession session,
        ConfigTraceEvidenceProviderOptions options,
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(options.StartupTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (session.HasSessionStart())
            {
                return;
            }

            if (session.ProcessHasExited)
            {
                throw new InvalidOperationException(
                    $"ConfigTrace exited before session_start " +
                    $"(exit code {session.ProcessExitCode?.ToString() ?? "unknown"}).");
            }

            try
            {
                await Task.Delay(
                        options.ReadinessPollInterval,
                        linked.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (timeout.IsCancellationRequested &&
                    !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    "ConfigTrace did not write session_start before the startup timeout.");
            }
        }
    }

    private static void ValidateOptions(ConfigTraceEvidenceProviderOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ExecutablePath))
        {
            throw new InvalidOperationException(
                "ConfigTrace executable path is required.");
        }

        if (string.IsNullOrWhiteSpace(options.RootPath))
        {
            throw new InvalidOperationException(
                "ConfigTrace root path is required.");
        }

        if (string.IsNullOrWhiteSpace(options.JournalDirectory))
        {
            throw new InvalidOperationException(
                "ConfigTrace journal directory is required.");
        }

        if (options.StartupTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "ConfigTrace startup timeout must be positive.");
        }

        if (options.SettleTime <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "ConfigTrace settle time must be positive.");
        }

        if (options.ReadinessPollInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "ConfigTrace readiness poll interval must be positive.");
        }
    }
}
internal sealed class ConfigTraceEvidenceProviderSession : IEvidenceProviderSession
{
    private readonly IConfigTraceProcess _process;
    private readonly string _journalPath;
    private readonly ConfigTraceJournalReader _journalReader;
    private readonly SemaphoreSlim _stopGate = new(1, 1);
    private bool _stopped;
    private bool _disposed;

    public ConfigTraceEvidenceProviderSession(
        IConfigTraceProcess process,
        string journalPath,
        ConfigTraceJournalReader journalReader)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentException.ThrowIfNullOrWhiteSpace(journalPath);
        ArgumentNullException.ThrowIfNull(journalReader);

        _process = process;
        _journalPath = journalPath;
        _journalReader = journalReader;
    }

    public string ProviderName => "ConfigTrace";

    internal bool ProcessHasExited => _process.HasExited;

    internal int? ProcessExitCode => _process.ExitCode;

    internal bool HasSessionStart() =>
        _journalReader.Read(_journalPath).HasSessionStart;

    public ValueTask<IReadOnlyList<EvidenceEvent>> ReadWindowAsync(
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = _journalReader.Read(
            _journalPath,
            startUtc,
            endUtc);

        return ValueTask.FromResult(result.Events);
    }

    public async ValueTask StopAsync(
        CancellationToken cancellationToken = default)
    {
        await _stopGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_stopped)
            {
                return;
            }

            try
            {
                await _process
                    .StopAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                _stopped = true;
            }
        }
        finally
        {
            _stopGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            await _process.DisposeAsync().ConfigureAwait(false);
            _stopGate.Dispose();
        }
    }
}
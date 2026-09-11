using System.Text;
using CrashScope.Agent.Evidence.ConfigTrace;
using CrashScope.Core.Evidence;

namespace CrashScope.Agent.Tests;

public sealed class ConfigTraceEvidenceProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "CrashScope-ConfigTraceEvidenceProviderTests",
        Guid.NewGuid().ToString("N"));

    public ConfigTraceEvidenceProviderTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task StartAsync_MissingExecutableFailsBeforeLaunch()
    {
        var launcher = new FakeLauncher();
        var provider = Provider(
            executablePath: Path.Combine(_root, "missing.exe"),
            launcher: launcher);

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => provider.StartAsync(Context()).AsTask());

        Assert.Equal(0, launcher.StartCount);
    }

    [Fact]
    public async Task StartAsync_SessionStartCreatesReadableSessionAndStopCleansProcess()
    {
        var executable = FakeExecutable();
        var launcher = new FakeLauncher
        {
            OnStart = request =>
            {
                File.WriteAllText(
                    request.JournalPath,
                    SessionStart() + Environment.NewLine + Change(1, 2000));
                return new FakeProcess();
            }
        };

        var provider = Provider(executable, launcher);
        await using var session = await provider.StartAsync(Context());

        var events = await session.ReadWindowAsync(
            DateTimeOffset.FromUnixTimeMilliseconds(1500),
            DateTimeOffset.FromUnixTimeMilliseconds(2500));

        Assert.Single(events);
        Assert.Equal(1, launcher.StartCount);

        var process = Assert.IsType<FakeProcess>(launcher.LastProcess);
        await session.StopAsync();

        Assert.Equal(1, process.StopCount);
    }

    [Fact]
    public async Task StartAsync_ReadinessTimeoutStopsAndDisposesProcess()
    {
        var executable = FakeExecutable();
        var launcher = new FakeLauncher
        {
            OnStart = _ => new FakeProcess()
        };

        var provider = Provider(
            executable,
            launcher,
            startupTimeout: TimeSpan.FromMilliseconds(50),
            pollInterval: TimeSpan.FromMilliseconds(5));

        await Assert.ThrowsAsync<TimeoutException>(
            () => provider.StartAsync(Context()).AsTask());

        var process = Assert.IsType<FakeProcess>(launcher.LastProcess);
        Assert.Equal(1, process.StopCount);
        Assert.Equal(1, process.DisposeCount);
    }

    [Fact]
    public async Task StartAsync_EarlyChildExitFailsAndDisposesProcess()
    {
        var executable = FakeExecutable();
        var launcher = new FakeLauncher
        {
            OnStart = _ => new FakeProcess(hasExited: true, exitCode: 23)
        };

        var provider = Provider(executable, launcher);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.StartAsync(Context()).AsTask());

        Assert.Contains("exit code 23", exception.Message, StringComparison.Ordinal);

        var process = Assert.IsType<FakeProcess>(launcher.LastProcess);
        Assert.Equal(1, process.DisposeCount);
    }

    [Fact]
    public async Task SessionRead_ToleratesMalformedTrailingJournalLine()
    {
        var executable = FakeExecutable();
        var launcher = new FakeLauncher
        {
            OnStart = request =>
            {
                File.WriteAllText(
                    request.JournalPath,
                    SessionStart() + Environment.NewLine +
                    Change(1, 2000) + Environment.NewLine +
                    """{"record_type":"change","schema_version":1""");
                return new FakeProcess();
            }
        };

        var provider = Provider(executable, launcher);
        await using var session = await provider.StartAsync(Context());

        var events = await session.ReadWindowAsync(
            DateTimeOffset.FromUnixTimeMilliseconds(1000),
            DateTimeOffset.FromUnixTimeMilliseconds(3000));

        Assert.Single(events);
    }

    [Fact]
    public async Task RealConfigTraceSidecar_SmokeTestWhenConfiguredByHarness()
    {
        var executable = Environment.GetEnvironmentVariable("CRASHSCOPE_A6_CONFIGTRACE_EXE");
        var root = Environment.GetEnvironmentVariable("CRASHSCOPE_A6_CONFIGTRACE_ROOT");

        if (string.IsNullOrWhiteSpace(executable) ||
            string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        Directory.CreateDirectory(root);
        var settings = Path.Combine(root, "settings.json");
        File.WriteAllText(
            settings,
            """{"Renderer":"DX11","HDR":false}""",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var journalDirectory = root + "-journals";
        Directory.CreateDirectory(journalDirectory);

        try
        {
            var provider = new ConfigTraceEvidenceProvider(
                new ConfigTraceEvidenceProviderOptions
                {
                    Enabled = true,
                    ExecutablePath = executable,
                    RootPath = root,
                    JournalDirectory = journalDirectory,
                    StartupTimeout = TimeSpan.FromSeconds(5),
                    SettleTime = TimeSpan.FromMilliseconds(200),
                    ReadinessPollInterval = TimeSpan.FromMilliseconds(50)
                });

            await using var session = await provider.StartAsync(Context());

            var changedAt = DateTimeOffset.UtcNow;
            File.WriteAllText(
                settings,
                """{"Renderer":"Vulkan","HDR":false}""",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            IReadOnlyList<EvidenceEvent> events = Array.Empty<EvidenceEvent>();
            var deadline = DateTimeOffset.UtcNow.AddSeconds(4);

            while (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(100);
                events = await session.ReadWindowAsync(
                    changedAt.AddSeconds(-1),
                    DateTimeOffset.UtcNow.AddSeconds(1));

                if (events.Count > 0)
                {
                    break;
                }
            }

            var item = Assert.Single(events);
            Assert.Equal("ConfigTrace", item.Source);
            Assert.Contains("Renderer", item.Summary, StringComparison.Ordinal);
            Assert.Contains("Vulkan", item.Summary, StringComparison.Ordinal);

            await session.StopAsync();
        }
        finally
        {
            try
            {
                Directory.Delete(journalDirectory, recursive: true);
            }
            catch
            {
            }
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }

    private ConfigTraceEvidenceProvider Provider(
        string executablePath,
        FakeLauncher launcher,
        TimeSpan? startupTimeout = null,
        TimeSpan? pollInterval = null)
    {
        var watchedRoot = Path.Combine(_root, "watched");
        var journals = Path.Combine(_root, "journals");
        Directory.CreateDirectory(watchedRoot);

        return new ConfigTraceEvidenceProvider(
            new ConfigTraceEvidenceProviderOptions
            {
                Enabled = true,
                ExecutablePath = executablePath,
                RootPath = watchedRoot,
                JournalDirectory = journals,
                StartupTimeout = startupTimeout ?? TimeSpan.FromSeconds(1),
                ReadinessPollInterval = pollInterval ?? TimeSpan.FromMilliseconds(5),
                SettleTime = TimeSpan.FromMilliseconds(200)
            },
            launcher);
    }

    private string FakeExecutable()
    {
        var path = Path.Combine(_root, "configtrace.exe");
        File.WriteAllBytes(path, new byte[] { 0 });
        return path;
    }

    private static EvidenceProviderSessionContext Context() =>
        new(
            Guid.NewGuid(),
            new DateTimeOffset(2026, 9, 11, 14, 0, 0, TimeSpan.Zero),
            4242,
            @"C:\Games\game.exe");

    private static string SessionStart() =>
        """{"record_type":"session_start","schema_version":1,"observed_unix_ms":1000,"root":"C:/game","label":"test","settle_ms":200}""";

    private static string Change(long sequence, long observedUnixMs) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            record_type = "change",
            schema_version = 1,
            sequence,
            observed_unix_ms = observedUnixMs,
            root = "C:/game",
            file = new
            {
                path = "settings.json",
                change = "modified",
                before_sha256 = "before",
                after_sha256 = "after",
                fields = new[]
                {
                    new
                    {
                        key = "/Renderer",
                        change = "modified",
                        before = "DX11",
                        after = "Vulkan",
                        sensitive = false
                    }
                }
            }
        });

    private sealed class FakeLauncher : IConfigTraceProcessLauncher
    {
        public Func<ConfigTraceProcessStartRequest, IConfigTraceProcess>? OnStart { get; init; }

        public int StartCount { get; private set; }

        public IConfigTraceProcess? LastProcess { get; private set; }

        public IConfigTraceProcess Start(ConfigTraceProcessStartRequest request)
        {
            StartCount++;
            LastProcess = OnStart?.Invoke(request) ?? new FakeProcess();
            return LastProcess;
        }
    }

    private sealed class FakeProcess : IConfigTraceProcess
    {
        private bool _hasExited;
        private readonly int? _exitCode;

        public FakeProcess(bool hasExited = false, int? exitCode = null)
        {
            _hasExited = hasExited;
            _exitCode = exitCode;
        }

        public int StopCount { get; private set; }

        public int DisposeCount { get; private set; }

        public bool HasExited => _hasExited;

        public int? ExitCode => _hasExited ? _exitCode : null;

        public ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StopCount++;
            _hasExited = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            _hasExited = true;
            return ValueTask.CompletedTask;
        }
    }
}
using CrashScope.Agent.Evidence;
using CrashScope.Core.Evidence;

namespace CrashScope.Agent.Tests;

public sealed class EvidenceProviderHostTests
{
    [Fact]
    public async Task StartAsyncIsolatesProviderFailureAndKeepsHealthyProvider()
    {
        var healthySession = new FakeSession(
            "healthy",
            (_, _, _) => ValueTask.FromResult<IReadOnlyList<EvidenceEvent>>(
                new[] { Event("healthy", -2) }));

        var broken = new FakeProvider(
            "broken",
            true,
            (_, _) => throw new InvalidOperationException("start failed"));

        var healthy = new FakeProvider(
            "healthy",
            true,
            (_, _) => ValueTask.FromResult<IEvidenceProviderSession>(healthySession));

        await using var sessions = await new EvidenceProviderHost(new[] { broken, healthy })
            .StartAsync(Context());

        Assert.Equal(1, sessions.ActiveProviderCount);
        var failure = Assert.Single(sessions.StartFailures);
        Assert.Equal("broken", failure.ProviderName);
        Assert.Equal(EvidenceProviderFailureStage.Start, failure.Stage);

        var window = await sessions.ReadWindowAsync(At(-5), At(0));
        Assert.Single(window.Events);
        Assert.Empty(window.Failures);
    }

    [Fact]
    public async Task DisabledProviderIsNotStarted()
    {
        var provider = new FakeProvider(
            "disabled",
            false,
            (_, _) => throw new InvalidOperationException("must not start"));

        await using var sessions = await new EvidenceProviderHost(new[] { provider })
            .StartAsync(Context());

        Assert.Equal(0, provider.StartCount);
        Assert.Equal(0, sessions.ActiveProviderCount);
        Assert.Empty(sessions.StartFailures);
    }

    [Fact]
    public async Task ReadWindowSortsEventsAndEnforcesRequestedBounds()
    {
        var first = new FakeSession(
            "first",
            (_, _, _) => ValueTask.FromResult<IReadOnlyList<EvidenceEvent>>(
                new[]
                {
                    Event("first", -2),
                    Event("first", -20)
                }));

        var second = new FakeSession(
            "second",
            (_, _, _) => ValueTask.FromResult<IReadOnlyList<EvidenceEvent>>(
                new[]
                {
                    Event("second", -5),
                    Event("second", 1)
                }));

        await using var sessions = await new EvidenceProviderHost(new IEvidenceProvider[]
        {
            Provider("first", first),
            Provider("second", second)
        }).StartAsync(Context());

        var result = await sessions.ReadWindowAsync(At(-10), At(0));

        Assert.Empty(result.Failures);
        Assert.Equal(2, result.Events.Count);
        Assert.Equal("second", result.Events[0].Source);
        Assert.Equal("first", result.Events[1].Source);
        Assert.All(result.Events, item =>
            Assert.InRange(item.TimestampUtc, At(-10), At(0)));
    }

    [Fact]
    public async Task ReadWindowIsolatesOneProviderFailure()
    {
        var broken = new FakeSession(
            "broken",
            (_, _, _) => throw new InvalidDataException("journal malformed"));

        var healthy = new FakeSession(
            "healthy",
            (_, _, _) => ValueTask.FromResult<IReadOnlyList<EvidenceEvent>>(
                new[] { Event("healthy", -1) }));

        await using var sessions = await new EvidenceProviderHost(new IEvidenceProvider[]
        {
            Provider("broken", broken),
            Provider("healthy", healthy)
        }).StartAsync(Context());

        var result = await sessions.ReadWindowAsync(At(-5), At(0));

        Assert.Single(result.Events);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("broken", failure.ProviderName);
        Assert.Equal(EvidenceProviderFailureStage.Read, failure.Stage);
        Assert.Equal("InvalidDataException", failure.ErrorType);
    }

    [Fact]
    public async Task StopAsyncAttemptsEveryProviderAndIsIdempotent()
    {
        var first = new FakeSession(
            "first",
            (_, _, _) => ValueTask.FromResult<IReadOnlyList<EvidenceEvent>>(
                Array.Empty<EvidenceEvent>()),
            throwOnStop: true);

        var second = new FakeSession(
            "second",
            (_, _, _) => ValueTask.FromResult<IReadOnlyList<EvidenceEvent>>(
                Array.Empty<EvidenceEvent>()));

        var sessions = await new EvidenceProviderHost(new IEvidenceProvider[]
        {
            Provider("first", first),
            Provider("second", second)
        }).StartAsync(Context());

        var firstStop = await sessions.StopAsync();
        var secondStop = await sessions.StopAsync();

        var failure = Assert.Single(firstStop);
        Assert.Equal("first", failure.ProviderName);
        Assert.Equal(EvidenceProviderFailureStage.Stop, failure.Stage);
        Assert.Same(firstStop, secondStop);

        Assert.Equal(1, first.StopCount);
        Assert.Equal(1, second.StopCount);
        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(1, second.DisposeCount);

        await sessions.DisposeAsync();

        Assert.Equal(1, first.StopCount);
        Assert.Equal(1, second.StopCount);
        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(1, second.DisposeCount);
    }

    private static FakeProvider Provider(
        string name,
        IEvidenceProviderSession session) =>
        new(
            name,
            true,
            (_, _) => ValueTask.FromResult(session));

    private static EvidenceProviderSessionContext Context() =>
        new(
            Guid.Parse("11111111-2222-3333-4444-555555555555"),
            At(-30),
            processId: 4242,
            executablePath: @"C:\Games\Example\game.exe");

    private static EvidenceEvent Event(string source, int seconds) =>
        new(
            At(seconds),
            At(seconds),
            source,
            "test.event",
            EvidenceSeverity.Information,
            $"{source} event");

    private static DateTimeOffset At(int seconds) =>
        new DateTimeOffset(2026, 9, 11, 10, 0, 0, TimeSpan.Zero)
            .AddSeconds(seconds);

    private sealed class FakeProvider : IEvidenceProvider
    {
        private readonly Func<
            EvidenceProviderSessionContext,
            CancellationToken,
            ValueTask<IEvidenceProviderSession>> _start;

        public FakeProvider(
            string providerName,
            bool isEnabled,
            Func<
                EvidenceProviderSessionContext,
                CancellationToken,
                ValueTask<IEvidenceProviderSession>> start)
        {
            ProviderName = providerName;
            IsEnabled = isEnabled;
            _start = start;
        }

        public string ProviderName { get; }

        public bool IsEnabled { get; }

        public int StartCount { get; private set; }

        public ValueTask<IEvidenceProviderSession> StartAsync(
            EvidenceProviderSessionContext context,
            CancellationToken cancellationToken = default)
        {
            StartCount++;
            return _start(context, cancellationToken);
        }
    }

    private sealed class FakeSession : IEvidenceProviderSession
    {
        private readonly Func<
            DateTimeOffset,
            DateTimeOffset,
            CancellationToken,
            ValueTask<IReadOnlyList<EvidenceEvent>>> _read;
        private readonly bool _throwOnStop;

        public FakeSession(
            string providerName,
            Func<
                DateTimeOffset,
                DateTimeOffset,
                CancellationToken,
                ValueTask<IReadOnlyList<EvidenceEvent>>> read,
            bool throwOnStop = false)
        {
            ProviderName = providerName;
            _read = read;
            _throwOnStop = throwOnStop;
        }

        public string ProviderName { get; }

        public int StopCount { get; private set; }

        public int DisposeCount { get; private set; }

        public ValueTask<IReadOnlyList<EvidenceEvent>> ReadWindowAsync(
            DateTimeOffset startUtc,
            DateTimeOffset endUtc,
            CancellationToken cancellationToken = default) =>
            _read(startUtc, endUtc, cancellationToken);

        public ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;

            if (_throwOnStop)
            {
                throw new InvalidOperationException("stop failed");
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
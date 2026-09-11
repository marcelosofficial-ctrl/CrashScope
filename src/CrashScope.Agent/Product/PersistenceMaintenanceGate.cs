namespace CrashScope.Agent.Product;

internal sealed class PersistenceMaintenanceGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async ValueTask<IDisposable> EnterAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Lease(_gate);
    }

    private sealed class Lease : IDisposable
    {
        private SemaphoreSlim? _gate;

        public Lease(SemaphoreSlim gate) => _gate = gate;

        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}

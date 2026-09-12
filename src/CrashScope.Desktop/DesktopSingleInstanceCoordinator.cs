using System.Threading;

namespace CrashScope.Desktop;

internal sealed class DesktopSingleInstanceCoordinator : IDisposable
{
    internal const string ProductionScope = "CrashScope.Desktop.v1";

    private readonly Semaphore _primaryGate;
    private readonly EventWaitHandle _activationSignal;
    private readonly EventWaitHandle _shutdownSignal =
        new(false, EventResetMode.ManualReset);
    private Thread? _listenerThread;
    private bool _ownsPrimaryGate;
    private bool _disposed;

    public DesktopSingleInstanceCoordinator(
        string scopeName = ProductionScope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeName);

        _primaryGate = new Semaphore(
            initialCount: 1,
            maximumCount: 1,
            name: $@"Local\{scopeName}.Primary");

        _activationSignal = new EventWaitHandle(
            initialState: false,
            mode: EventResetMode.AutoReset,
            name: $@"Local\{scopeName}.Activate");
    }

    public bool TryAcquirePrimary()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_ownsPrimaryGate)
        {
            return true;
        }

        _ownsPrimaryGate = _primaryGate.WaitOne(0);
        return _ownsPrimaryGate;
    }

    public void SignalPrimary()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _activationSignal.Set();
    }

    public void StartListening(Action activate)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(activate);

        if (!_ownsPrimaryGate)
        {
            throw new InvalidOperationException(
                "Only the primary desktop instance can listen for activation.");
        }

        if (_listenerThread is not null)
        {
            throw new InvalidOperationException(
                "Desktop activation listener is already running.");
        }

        _listenerThread = new Thread(() =>
        {
            var handles = new WaitHandle[]
            {
                _activationSignal,
                _shutdownSignal
            };

            while (WaitHandle.WaitAny(handles) == 0)
            {
                try
                {
                    activate();
                }
                catch
                {
                    // Activation failures must never crash the desktop process.
                }
            }
        })
        {
            IsBackground = true,
            Name = "CrashScope Desktop activation"
        };

        _listenerThread.Start();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shutdownSignal.Set();

        if (_listenerThread is not null
            && _listenerThread != Thread.CurrentThread)
        {
            _listenerThread.Join(TimeSpan.FromSeconds(1));
        }

        if (_ownsPrimaryGate)
        {
            try
            {
                _primaryGate.Release();
            }
            catch (SemaphoreFullException)
            {
            }

            _ownsPrimaryGate = false;
        }

        _activationSignal.Dispose();
        _shutdownSignal.Dispose();
        _primaryGate.Dispose();
    }
}
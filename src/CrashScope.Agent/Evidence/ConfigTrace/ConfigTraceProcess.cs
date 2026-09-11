using System.Diagnostics;

namespace CrashScope.Agent.Evidence.ConfigTrace;

internal sealed record ConfigTraceProcessStartRequest(
    string ExecutablePath,
    string RootPath,
    string JournalPath,
    TimeSpan SettleTime,
    string Label);

internal interface IConfigTraceProcess : IAsyncDisposable
{
    bool HasExited { get; }

    int? ExitCode { get; }

    ValueTask StopAsync(CancellationToken cancellationToken = default);
}

internal interface IConfigTraceProcessLauncher
{
    IConfigTraceProcess Start(ConfigTraceProcessStartRequest request);
}

internal sealed class ConfigTraceProcessLauncher : IConfigTraceProcessLauncher
{
    public IConfigTraceProcess Start(ConfigTraceProcessStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startInfo = new ProcessStartInfo
        {
            FileName = request.ExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("watch");
        startInfo.ArgumentList.Add(request.RootPath);
        startInfo.ArgumentList.Add("--out");
        startInfo.ArgumentList.Add(request.JournalPath);
        startInfo.ArgumentList.Add("--settle");
        startInfo.ArgumentList.Add(
            $"{Math.Max(1, (int)Math.Round(request.SettleTime.TotalMilliseconds))}ms");
        startInfo.ArgumentList.Add("--label");
        startInfo.ArgumentList.Add(request.Label);

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("ConfigTrace process did not start.");
            }

            return new SystemConfigTraceProcess(process);
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    private sealed class SystemConfigTraceProcess : IConfigTraceProcess
    {
        private readonly Process _process;
        private int _disposed;

        public SystemConfigTraceProcess(Process process)
        {
            _process = process;
        }

        public bool HasExited
        {
            get
            {
                try
                {
                    return _process.HasExited;
                }
                catch (InvalidOperationException)
                {
                    return true;
                }
            }
        }

        public int? ExitCode
        {
            get
            {
                if (!HasExited)
                {
                    return null;
                }

                try
                {
                    return _process.ExitCode;
                }
                catch (InvalidOperationException)
                {
                    return null;
                }
            }
        }

        public async ValueTask StopAsync(
            CancellationToken cancellationToken = default)
        {
            if (HasExited)
            {
                return;
            }

            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                return;
            }

            try
            {
                await _process
                    .WaitForExitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                await StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _process.Dispose();
            }
        }
    }
}
using System.ComponentModel;
using System.Diagnostics;

namespace CrashScope.Agent.Processes;

internal sealed class SystemProcessProbe : IProcessProbe
{
    public ValueTask<ProcessProbeResult> ProbeAsync(
        int processId,
        CancellationToken cancellationToken = default)
    {
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId));
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var process = Process.GetProcessById(processId);

            if (process.HasExited)
            {
                return ValueTask.FromResult(NotFound(processId));
            }

            var startTimeUtc = new DateTimeOffset(
                process.StartTime.ToUniversalTime());
            var name = process.ProcessName;
            var executablePath = TryGetExecutablePath(process);

            return ValueTask.FromResult(new ProcessProbeResult(
                ProcessProbeState.Running,
                processId,
                startTimeUtc,
                name,
                executablePath,
                null));
        }
        catch (ArgumentException)
        {
            return ValueTask.FromResult(NotFound(processId));
        }
        catch (InvalidOperationException)
        {
            return ValueTask.FromResult(NotFound(processId));
        }
        catch (Win32Exception ex)
        {
            return ValueTask.FromResult(Unavailable(processId, ex));
        }
        catch (NotSupportedException ex)
        {
            return ValueTask.FromResult(Unavailable(processId, ex));
        }
    }

    private static string? TryGetExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static ProcessProbeResult NotFound(int processId) => new(
        ProcessProbeState.NotFound,
        processId,
        null,
        null,
        null,
        "Process was not found.");

    private static ProcessProbeResult Unavailable(
        int processId,
        Exception exception) => new(
            ProcessProbeState.Unavailable,
            processId,
            null,
            null,
            null,
            $"Process identity was unavailable: {exception.Message}");
}

using System.ComponentModel;
using System.Diagnostics;

namespace CrashScope.Agent.Processes;

internal enum ProcessExitWaitState
{
    Exited,
    PidReused,
    NotFound,
    Unavailable
}

internal sealed record ProcessExitWaitResult(
    ProcessExitWaitState State,
    string? Detail = null);

internal interface IProcessExitWaiter
{
    ValueTask<ProcessExitWaitResult> WaitForExitAsync(
        ProcessInstance target,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Waits on the Windows process handle instead of periodically polling the PID.
/// The PID/start-time identity is checked before the wait begins so a recycled PID
/// can never be mistaken for the workload CrashScope originally attached to.
/// </summary>
internal sealed class SystemProcessExitWaiter : IProcessExitWaiter
{
    public async ValueTask<ProcessExitWaitResult> WaitForExitAsync(
        ProcessInstance target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var process = Process.GetProcessById(target.Identity.ProcessId);

            if (process.HasExited)
            {
                return new ProcessExitWaitResult(ProcessExitWaitState.Exited);
            }

            var currentStartTimeUtc = new DateTimeOffset(process.StartTime.ToUniversalTime());
            if (currentStartTimeUtc != target.Identity.StartTimeUtc)
            {
                return new ProcessExitWaitResult(
                    ProcessExitWaitState.PidReused,
                    "The monitored PID now belongs to a different process instance.");
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return new ProcessExitWaitResult(ProcessExitWaitState.Exited);
        }
        catch (ArgumentException)
        {
            return new ProcessExitWaitResult(
                ProcessExitWaitState.NotFound,
                "The monitored process no longer exists.");
        }
        catch (InvalidOperationException)
        {
            return new ProcessExitWaitResult(
                ProcessExitWaitState.NotFound,
                "The monitored process is no longer available.");
        }
        catch (Win32Exception ex)
        {
            return new ProcessExitWaitResult(
                ProcessExitWaitState.Unavailable,
                $"Windows would not expose the process exit handle: {ex.Message}");
        }
        catch (NotSupportedException ex)
        {
            return new ProcessExitWaitResult(
                ProcessExitWaitState.Unavailable,
                $"Process exit waiting is unavailable: {ex.Message}");
        }
    }
}

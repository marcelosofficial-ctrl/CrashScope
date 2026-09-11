using System.ComponentModel;
using System.Diagnostics;
using CrashScope.Core.Sessions;

namespace CrashScope.Agent.Sessions;

internal sealed class WorkloadDiscoveryService
{
    private static readonly HashSet<string> ExcludedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Idle", "System", "Registry", "Memory Compression", "CrashScope.Agent"
    };

    public IReadOnlyList<WorkloadCandidate> Discover(
        int maximum = 50,
        bool includeUnlikely = false)
    {
        maximum = Math.Clamp(maximum, 1, 200);
        var candidates = new List<WorkloadCandidate>();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                var candidate = TryCreateCandidate(process);
                if (candidate is null)
                {
                    continue;
                }

                if (!includeUnlikely &&
                    candidate.Recommendation == WorkloadRecommendation.Unlikely)
                {
                    continue;
                }

                candidates.Add(candidate);
            }
        }

        return candidates
            .OrderByDescending(x => x.RecommendationScore)
            .ThenByDescending(x => x.WorkingSetMiB)
            .ThenBy(x => x.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Take(maximum)
            .ToArray();
    }

    public WorkloadCandidate? DiscoverProcess(int processId)
    {
        if (processId <= 0 || processId == Environment.ProcessId)
        {
            return null;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            return TryCreateCandidate(process);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static WorkloadCandidate? TryCreateCandidate(Process process)
    {
        try
        {
            if (process.Id == Environment.ProcessId || ExcludedNames.Contains(process.ProcessName))
            {
                return null;
            }

            var start = new DateTimeOffset(process.StartTime.ToUniversalTime());
            var title = string.IsNullOrWhiteSpace(process.MainWindowTitle)
                ? null
                : process.MainWindowTitle.Trim();
            var path = TryGetPath(process);
            var workingSetMiB = process.WorkingSet64 / 1024d / 1024d;
            var classification = WorkloadClassifier.Classify(
                process.ProcessName,
                path,
                title,
                workingSetMiB);

            return new WorkloadCandidate(
                process.Id,
                start,
                process.ProcessName,
                path,
                title,
                workingSetMiB,
                classification.Kind,
                classification.Recommendation,
                classification.Score,
                classification.Reasons);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static string? TryGetPath(Process process)
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
}

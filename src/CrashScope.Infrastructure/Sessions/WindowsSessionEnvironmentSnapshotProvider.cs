using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CrashScope.Core.Sessions;

namespace CrashScope.Infrastructure.Sessions;

public sealed class WindowsSessionEnvironmentSnapshotProvider : ISessionEnvironmentSnapshotProvider
{
    public ValueTask<SessionEnvironmentSnapshot> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var operatingSystem = RuntimeInformation.OSDescription.Trim();
        string? cpuName = null;
        ulong? memoryBytes = null;
        IReadOnlyList<EnvironmentGpuSnapshot> gpus = Array.Empty<EnvironmentGpuSnapshot>();

        if (OperatingSystem.IsWindows())
        {
            // RuntimeInformation.OSDescription exposes the underlying NT version and
            // can report a Windows 11 installation as "Microsoft Windows 10.0.x".
            // Prefer Windows' own product caption for human-facing historical
            // snapshots, while retaining the runtime description as a safe fallback.
            operatingSystem = QuerySingleString("Win32_OperatingSystem", "Caption")
                ?? operatingSystem;
            cpuName = QuerySingleString("Win32_Processor", "Name");
            memoryBytes = QuerySingleUInt64("Win32_ComputerSystem", "TotalPhysicalMemory");
            gpus = QueryGpus();
        }

        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "unknown";

        return ValueTask.FromResult(new SessionEnvironmentSnapshot(
            DateTimeOffset.UtcNow,
            operatingSystem,
            RuntimeInformation.OSArchitecture.ToString(),
            RuntimeInformation.ProcessArchitecture.ToString(),
            RuntimeInformation.FrameworkDescription.Trim(),
            version,
            cpuName,
            Environment.ProcessorCount,
            memoryBytes.HasValue ? checked((long)(memoryBytes.Value / 1024UL / 1024UL)) : null,
            gpus));
    }

    [SupportedOSPlatform("windows")]
    private static string? QuerySingleString(string className, string propertyName)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT {propertyName} FROM {className}");
            foreach (ManagementObject item in searcher.Get())
            {
                using (item)
                {
                    var value = Convert.ToString(item[propertyName]);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value.Trim();
                    }
                }
            }
        }
        catch (ManagementException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return null;
    }

    [SupportedOSPlatform("windows")]
    private static ulong? QuerySingleUInt64(string className, string propertyName)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT {propertyName} FROM {className}");
            foreach (ManagementObject item in searcher.Get())
            {
                using (item)
                {
                    if (item[propertyName] is not null &&
                        ulong.TryParse(Convert.ToString(item[propertyName]), out var value) &&
                        value > 0)
                    {
                        return value;
                    }
                }
            }
        }
        catch (ManagementException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return null;
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<EnvironmentGpuSnapshot> QueryGpus()
    {
        var result = new List<EnvironmentGpuSnapshot>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, DriverVersion FROM Win32_VideoController");
            foreach (ManagementObject item in searcher.Get())
            {
                using (item)
                {
                    var name = Convert.ToString(item["Name"]);
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    var driver = Convert.ToString(item["DriverVersion"]);
                    result.Add(new EnvironmentGpuSnapshot(
                        name.Trim(),
                        string.IsNullOrWhiteSpace(driver) ? null : driver.Trim()));
                }
            }
        }
        catch (ManagementException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return result;
    }
}

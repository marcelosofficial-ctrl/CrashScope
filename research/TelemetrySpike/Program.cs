using System.Diagnostics;
using LibreHardwareMonitor.Hardware;

string runLabel = args.Length > 0 ? args[0] : "unspecified";

Console.WriteLine("==================================================");
Console.WriteLine("CrashScope Phase 0 - Telemetry Research Spike");
Console.WriteLine("==================================================");
Console.WriteLine($"Run label: {runLabel}");
Console.WriteLine($"UTC: {DateTime.UtcNow:O}");
Console.WriteLine($".NET runtime: {Environment.Version}");
Console.WriteLine($"OS: {Environment.OSVersion}");
Console.WriteLine($"Logical processors: {Environment.ProcessorCount}");
Console.WriteLine();

var computer = new Computer
{
    IsCpuEnabled = true,
    IsGpuEnabled = true,
    IsMemoryEnabled = true,

    // Not needed for our first CrashScope experiment.
    IsMotherboardEnabled = false,
    IsStorageEnabled = false,
    IsNetworkEnabled = false,
    IsControllerEnabled = false
};

try
{
    var openTimer = Stopwatch.StartNew();

    computer.Open();

    openTimer.Stop();

    Console.WriteLine(
        $"Computer.Open(): {openTimer.Elapsed.TotalMilliseconds:F2} ms");

    UpdateAll(computer.Hardware);

    Console.WriteLine();
    Console.WriteLine("==================================================");
    Console.WriteLine("DETECTED HARDWARE AND SENSORS");
    Console.WriteLine("==================================================");

    foreach (IHardware hardware in computer.Hardware)
    {
        PrintHardware(hardware, 0);
    }

    Console.WriteLine();
    Console.WriteLine("==================================================");
    Console.WriteLine("1 HZ PERFORMANCE TEST");
    Console.WriteLine("==================================================");
    Console.WriteLine("20 refreshes, approximately one second apart.");
    Console.WriteLine();

    const int sampleCount = 20;

    var refreshTimes = new List<double>(sampleCount);

    using Process process = Process.GetCurrentProcess();

    process.Refresh();

    TimeSpan cpuBefore = process.TotalProcessorTime;

    var testTimer = Stopwatch.StartNew();

    for (int i = 1; i <= sampleCount; i++)
    {
        var refreshTimer = Stopwatch.StartNew();

        UpdateAll(computer.Hardware);

        refreshTimer.Stop();

        refreshTimes.Add(refreshTimer.Elapsed.TotalMilliseconds);

        Console.WriteLine(
            $"Sample {i,2}/{sampleCount}: " +
            $"{refreshTimer.Elapsed.TotalMilliseconds,8:F2} ms");

        if (i < sampleCount)
        {
            Thread.Sleep(1000);
        }
    }

    testTimer.Stop();

    process.Refresh();

    TimeSpan cpuAfter = process.TotalProcessorTime;
    TimeSpan cpuUsed = cpuAfter - cpuBefore;

    double averageProcessCpu =
        cpuUsed.TotalMilliseconds /
        (testTimer.Elapsed.TotalMilliseconds *
         Environment.ProcessorCount) *
        100.0;

    Console.WriteLine();
    Console.WriteLine("==================================================");
    Console.WriteLine("PERFORMANCE SUMMARY");
    Console.WriteLine("==================================================");

    Console.WriteLine(
        $"Average refresh time: {refreshTimes.Average():F2} ms");

    Console.WriteLine(
        $"Fastest refresh:      {refreshTimes.Min():F2} ms");

    Console.WriteLine(
        $"Slowest refresh:      {refreshTimes.Max():F2} ms");

    Console.WriteLine(
        $"Process avg CPU:      {averageProcessCpu:F4}%");

    Console.WriteLine(
        $"Working set:          " +
        $"{process.WorkingSet64 / 1024.0 / 1024.0:F2} MB");

    Console.WriteLine(
        $"Private memory:       " +
        $"{process.PrivateMemorySize64 / 1024.0 / 1024.0:F2} MB");

    Console.WriteLine(
        $"Managed GC memory:    " +
        $"{GC.GetTotalMemory(false) / 1024.0 / 1024.0:F2} MB");

    Console.WriteLine();
    Console.WriteLine("==================================================");
    Console.WriteLine("FINAL SENSOR VALUES");
    Console.WriteLine("==================================================");

    UpdateAll(computer.Hardware);

    foreach (IHardware hardware in computer.Hardware)
    {
        PrintHardware(hardware, 0);
    }
}
catch (Exception ex)
{
    Console.WriteLine();
    Console.WriteLine("==================================================");
    Console.WriteLine("ERROR");
    Console.WriteLine("==================================================");
    Console.WriteLine(ex);
}
finally
{
    computer.Close();
}

static void UpdateAll(IEnumerable<IHardware> hardwareCollection)
{
    foreach (IHardware hardware in hardwareCollection)
    {
        UpdateHardware(hardware);
    }
}

static void UpdateHardware(IHardware hardware)
{
    hardware.Update();

    foreach (IHardware subHardware in hardware.SubHardware)
    {
        UpdateHardware(subHardware);
    }
}

static void PrintHardware(IHardware hardware, int depth)
{
    string indent = new(' ', depth * 2);

    Console.WriteLine();
    Console.WriteLine(
        $"{indent}[{hardware.HardwareType}] {hardware.Name}");

    if (hardware.Sensors.Length == 0)
    {
        Console.WriteLine(
            $"{indent}  No direct sensors reported.");
    }

    foreach (ISensor sensor in hardware.Sensors)
    {
        string value = sensor.Value.HasValue
            ? sensor.Value.Value.ToString("0.###")
            : "NULL";

        Console.WriteLine(
            $"{indent}  {sensor.SensorType,-14} | " +
            $"{sensor.Name,-38} | {value}");
    }

    foreach (IHardware subHardware in hardware.SubHardware)
    {
        PrintHardware(subHardware, depth + 1);
    }
}
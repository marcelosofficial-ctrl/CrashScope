using System.Diagnostics;
using System.Runtime.InteropServices;
using CrashScope.Core.Diagnostics;

namespace CrashScope.Infrastructure.Diagnostics;

public sealed record WerReportStoreProbeStatus(
    string Store,
    bool Available,
    int ReportCount,
    string? Error);

public sealed record WerReportStoreProbeSnapshot(
    DateTimeOffset ObservedAtUtc,
    double ElapsedMilliseconds,
    IReadOnlyList<WerReportStoreProbeStatus> Stores)
{
    public static WerReportStoreProbeSnapshot Empty { get; } = new(
        DateTimeOffset.UnixEpoch,
        0,
        Array.Empty<WerReportStoreProbeStatus>());
}

public sealed class WindowsWerReportStoreSource : IDiagnosticArtifactSource
{
    private readonly IWerReportStoreReader _reader;
    private readonly TimeProvider _timeProvider;
    private WerReportStoreProbeSnapshot _lastProbe = WerReportStoreProbeSnapshot.Empty;

    public WindowsWerReportStoreSource()
        : this(new WindowsWerReportStoreReader(new WindowsWerNativeApi()), TimeProvider.System)
    {
    }

    internal WindowsWerReportStoreSource(
        IWerReportStoreReader reader,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string Name => "WindowsWerReportStore";

    public WerReportStoreProbeSnapshot LastProbeSnapshot => Volatile.Read(ref _lastProbe);

    public ValueTask<IReadOnlyList<DiagnosticArtifact>> ReadSinceAsync(
        DateTimeOffset sinceUtc,
        int maximumArtifacts = 256,
        CancellationToken cancellationToken = default)
    {
        if (sinceUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("WER report-store query time must be UTC.", nameof(sinceUtc));
        }

        if (maximumArtifacts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumArtifacts));
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows() && _reader is WindowsWerReportStoreReader)
        {
            Volatile.Write(
                ref _lastProbe,
                new WerReportStoreProbeSnapshot(
                    _timeProvider.GetUtcNow().ToUniversalTime(),
                    0,
                    new[] { new WerReportStoreProbeStatus("platform", false, 0, "Windows only") }));
            return ValueTask.FromResult<IReadOnlyList<DiagnosticArtifact>>(Array.Empty<DiagnosticArtifact>());
        }

        var started = Stopwatch.GetTimestamp();
        var observedAtUtc = _timeProvider.GetUtcNow().ToUniversalTime();
        var read = _reader.Read(cancellationToken);

        var artifacts = read.Reports
            .Where(report => report.CreationTimeUtc >= sinceUtc)
            .OrderByDescending(report => report.CreationTimeUtc)
            .Take(maximumArtifacts)
            .Select(report => Map(report, observedAtUtc))
            .OrderBy(artifact => artifact.SourceOccurredAtUtc ?? artifact.ObservedAtUtc)
            .ToArray();

        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        Volatile.Write(
            ref _lastProbe,
            new WerReportStoreProbeSnapshot(observedAtUtc, elapsed, read.Stores));

        return ValueTask.FromResult<IReadOnlyList<DiagnosticArtifact>>(artifacts);
    }

    internal static DiagnosticArtifact Map(
        WerReportStoreRecord report,
        DateTimeOffset observedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(report);

        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["EvidenceOrigin"] = "WindowsWerReportStore",
            ["Store"] = report.Store,
            ["BucketId"] = report.BucketId.ToString("D"),
            ["ReportStatus"] = report.ReportStatus.ToString(),
            ["SizeInBytes"] = report.SizeInBytes.ToString(),
            ["NumberOfFiles"] = report.NumberOfFiles.ToString()
        };

        for (var index = 0; index < report.SignatureParameters.Count; index++)
        {
            var parameter = report.SignatureParameters[index];
            var key = string.IsNullOrWhiteSpace(parameter.Name)
                ? $"Signature.P{index}"
                : $"Signature.{parameter.Name.Trim()}";
            properties[key] = parameter.Value;
        }

        if (report.AssociatedFiles.Count > 0)
        {
            properties["AssociatedFiles"] = string.Join(
                "; ",
                report.AssociatedFiles.Select(Path.GetFileName).Where(name => !string.IsNullOrWhiteSpace(name)));
        }

        return new DiagnosticArtifact(
            "WindowsWerReportStore",
            DiagnosticArtifactKind.WindowsErrorReport,
            $"werstore://{report.Store}/{report.ReportId:D}",
            observedAtUtc,
            report.CreationTimeUtc,
            report.ReportId.ToString("D"),
            report.EventName,
            null,
            BuildSignature(report),
            properties);
    }

    private static string? BuildSignature(WerReportStoreRecord report)
    {
        var fields = new List<string>();
        if (!string.IsNullOrWhiteSpace(report.EventName))
        {
            fields.Add(report.EventName.Trim());
        }

        fields.AddRange(report.SignatureParameters
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .Select(item => string.IsNullOrWhiteSpace(item.Name)
                ? item.Value.Trim()
                : $"{item.Name.Trim()}={item.Value.Trim()}"));

        return fields.Count == 0 ? null : string.Join("|", fields);
    }
}

internal sealed record WerSignatureParameter(string Name, string Value);

internal sealed record WerReportStoreRecord(
    string Store,
    Guid ReportId,
    Guid BucketId,
    DateTimeOffset CreationTimeUtc,
    ulong SizeInBytes,
    uint ReportStatus,
    uint NumberOfFiles,
    string? EventName,
    IReadOnlyList<WerSignatureParameter> SignatureParameters,
    IReadOnlyList<string> AssociatedFiles);

internal sealed record WerReportStoreReadResult(
    IReadOnlyList<WerReportStoreRecord> Reports,
    IReadOnlyList<WerReportStoreProbeStatus> Stores);

internal interface IWerReportStoreReader
{
    WerReportStoreReadResult Read(CancellationToken cancellationToken);
}

internal interface IWindowsWerNativeApi
{
    int StoreOpen(WerReportStoreKind store, out nint handle);
    void StoreClose(nint handle);
    int GetFirstReportKey(nint handle, out nint reportKey);
    int GetNextReportKey(nint handle, out nint reportKey);
    void FreeString(nint value);
    int QueryReportMetadataV2(nint handle, string reportKey, ref WerReportMetadataV2 metadata);
}

internal enum WerReportStoreKind
{
    UserArchive = 0,
    UserQueue = 1,
    MachineArchive = 2,
    MachineQueue = 3
}

internal sealed class WindowsWerReportStoreReader : IWerReportStoreReader
{
    private const int S_OK = 0;
    private const int ErrorNoMoreFiles = 18;
    private const int HResultNoMoreFiles = unchecked((int)0x80070012);
    private const int ErrorInsufficientBuffer = 122;
    private const int HResultInsufficientBuffer = unchecked((int)0x8007007A);

    private static readonly WerReportStoreKind[] Stores =
    {
        WerReportStoreKind.UserQueue,
        WerReportStoreKind.UserArchive,
        WerReportStoreKind.MachineQueue,
        WerReportStoreKind.MachineArchive
    };

    private readonly IWindowsWerNativeApi _native;

    public WindowsWerReportStoreReader(IWindowsWerNativeApi native)
    {
        ArgumentNullException.ThrowIfNull(native);
        _native = native;
    }

    public WerReportStoreReadResult Read(CancellationToken cancellationToken)
    {
        var reports = new List<WerReportStoreRecord>();
        var statuses = new List<WerReportStoreProbeStatus>();

        foreach (var store in Stores)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadStore(store, reports, statuses, cancellationToken);
        }

        return new WerReportStoreReadResult(reports, statuses);
    }

    private void ReadStore(
        WerReportStoreKind store,
        ICollection<WerReportStoreRecord> reports,
        ICollection<WerReportStoreProbeStatus> statuses,
        CancellationToken cancellationToken)
    {
        nint handle = 0;
        var count = 0;

        try
        {
            var openResult = _native.StoreOpen(store, out handle);
            if (openResult != S_OK || handle == 0)
            {
                statuses.Add(new WerReportStoreProbeStatus(store.ToString(), false, 0, FormatError(openResult)));
                return;
            }

            var result = _native.GetFirstReportKey(handle, out var reportKey);
            while (result == S_OK)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var key = Marshal.PtrToStringUni(reportKey);
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        var report = Query(handle, store, key);
                        if (report is not null)
                        {
                            reports.Add(report);
                            count++;
                        }
                    }
                }
                finally
                {
                    if (reportKey != 0)
                    {
                        _native.FreeString(reportKey);
                    }
                }

                result = _native.GetNextReportKey(handle, out reportKey);
            }

            var error = IsNoMoreFiles(result) ? null : FormatError(result);
            statuses.Add(new WerReportStoreProbeStatus(store.ToString(), true, count, error));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or SEHException)
        {
            statuses.Add(new WerReportStoreProbeStatus(store.ToString(), false, count, ex.GetType().Name));
        }
        finally
        {
            if (handle != 0)
            {
                _native.StoreClose(handle);
            }
        }
    }

    private WerReportStoreRecord? Query(nint handle, WerReportStoreKind store, string key)
    {
        var metadata = WerReportMetadataV2.Create();
        var result = _native.QueryReportMetadataV2(handle, key, ref metadata);
        nint fileNames = 0;

        try
        {
            if (IsInsufficientBuffer(result) && metadata.SizeOfFileNames > 0)
            {
                var bytes = checked((int)metadata.SizeOfFileNames * sizeof(char));
                fileNames = Marshal.AllocHGlobal(bytes);
                metadata.FileNames = fileNames;
                result = _native.QueryReportMetadataV2(handle, key, ref metadata);
            }

            if (result != S_OK || metadata.ReportId == Guid.Empty)
            {
                return null;
            }

            var creationTime = metadata.CreationTime.ToDateTimeOffsetUtc();
            var parameters = (metadata.Signature.Parameters ?? Array.Empty<WerReportParameter>())
                .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Name) || !string.IsNullOrWhiteSpace(parameter.Value))
                .Select(parameter => new WerSignatureParameter(parameter.Name ?? string.Empty, parameter.Value ?? string.Empty))
                .ToArray();

            return new WerReportStoreRecord(
                store.ToString(),
                metadata.ReportId,
                metadata.BucketId,
                creationTime,
                metadata.SizeInBytes,
                metadata.ReportStatus,
                metadata.NumberOfFiles,
                metadata.Signature.EventName,
                parameters,
                ReadMultiString(metadata.FileNames, metadata.SizeOfFileNames));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
        finally
        {
            if (fileNames != 0)
            {
                Marshal.FreeHGlobal(fileNames);
            }
        }
    }

    private static IReadOnlyList<string> ReadMultiString(nint pointer, uint characterCount)
    {
        if (pointer == 0 || characterCount == 0 || characterCount > 1_048_576)
        {
            return Array.Empty<string>();
        }

        var text = Marshal.PtrToStringUni(pointer, checked((int)characterCount));
        if (string.IsNullOrEmpty(text))
        {
            return Array.Empty<string>();
        }

        return text
            .Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    private static bool IsNoMoreFiles(int result) =>
        result is ErrorNoMoreFiles or HResultNoMoreFiles;

    private static bool IsInsufficientBuffer(int result) =>
        result is ErrorInsufficientBuffer or HResultInsufficientBuffer;

    private static string FormatError(int result) => $"0x{unchecked((uint)result):X8}";
}

internal sealed class WindowsWerNativeApi : IWindowsWerNativeApi
{
    public int StoreOpen(WerReportStoreKind store, out nint handle) =>
        WerStoreOpen(store, out handle);

    public void StoreClose(nint handle) => WerStoreClose(handle);

    public int GetFirstReportKey(nint handle, out nint reportKey) =>
        WerStoreGetFirstReportKey(handle, out reportKey);

    public int GetNextReportKey(nint handle, out nint reportKey) =>
        WerStoreGetNextReportKey(handle, out reportKey);

    public void FreeString(nint value) => WerFreeString(value);

    public int QueryReportMetadataV2(nint handle, string reportKey, ref WerReportMetadataV2 metadata) =>
        WerStoreQueryReportMetadataV2(handle, reportKey, ref metadata);

    [DllImport("wer.dll", ExactSpelling = true)]
    private static extern int WerStoreOpen(WerReportStoreKind repStoreType, out nint phReportStore);

    [DllImport("wer.dll", ExactSpelling = true)]
    private static extern void WerStoreClose(nint hReportStore);

    [DllImport("wer.dll", ExactSpelling = true)]
    private static extern int WerStoreGetFirstReportKey(nint hReportStore, out nint ppszReportKey);

    [DllImport("wer.dll", ExactSpelling = true)]
    private static extern int WerStoreGetNextReportKey(nint hReportStore, out nint ppszReportKey);

    [DllImport("wer.dll", ExactSpelling = true)]
    private static extern void WerFreeString(nint pwszStr);

    [DllImport("wer.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int WerStoreQueryReportMetadataV2(
        nint hReportStore,
        string pszReportKey,
        [In, Out] ref WerReportMetadataV2 pReportMetadata);
}

[StructLayout(LayoutKind.Sequential)]
internal struct WerFileTime
{
    public uint LowDateTime;
    public uint HighDateTime;

    public readonly DateTimeOffset ToDateTimeOffsetUtc()
    {
        var fileTime = ((long)HighDateTime << 32) | LowDateTime;
        return DateTimeOffset.FromFileTime(fileTime).ToUniversalTime();
    }
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WerReportParameter
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 129)]
    public string? Name;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
    public string? Value;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WerReportSignature
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 65)]
    public string? EventName;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
    public WerReportParameter[]? Parameters;

    public static WerReportSignature Create() => new()
    {
        EventName = string.Empty,
        Parameters = Enumerable.Range(0, 10)
            .Select(_ => new WerReportParameter { Name = string.Empty, Value = string.Empty })
            .ToArray()
    };
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WerReportMetadataV2
{
    public WerReportSignature Signature;
    public Guid BucketId;
    public Guid ReportId;
    public WerFileTime CreationTime;
    public ulong SizeInBytes;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
    public string? CabId;

    public uint ReportStatus;
    public Guid ReportIntegratorId;
    public uint NumberOfFiles;
    public uint SizeOfFileNames;
    public nint FileNames;

    public static WerReportMetadataV2 Create() => new()
    {
        Signature = WerReportSignature.Create(),
        CabId = string.Empty,
        FileNames = 0
    };
}

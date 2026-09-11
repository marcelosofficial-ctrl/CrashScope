using System.Runtime.InteropServices;
using CrashScope.Infrastructure.Diagnostics;

namespace CrashScope.Infrastructure.Tests;

public sealed class WindowsWerReportStoreSourceTests
{
    [Fact]
    public void Reader_ReleasesReportKeysBuffersAndStoreHandles()
    {
        var native = new FakeWerNativeApi();
        var reader = new WindowsWerReportStoreReader(native);

        var result = reader.Read(CancellationToken.None);

        var report = Assert.Single(result.Reports);
        Assert.Equal(Guid.Parse("11111111-2222-3333-4444-555555555555"), report.ReportId);
        Assert.Equal("APPCRASH", report.EventName);
        Assert.Equal(new[] { @"C:\Private\game.dmp", @"C:\WER\Report.wer" }, report.AssociatedFiles);
        Assert.Equal(1, native.CloseCount);
        Assert.Equal(1, native.FreeStringCount);
        Assert.Equal(1, native.SecondPassQueryCount);
        Assert.Equal(4, result.Stores.Count);
        Assert.True(result.Stores.Single(item => item.Store == WerReportStoreKind.UserQueue.ToString()).Available);
        Assert.False(result.Stores.Single(item => item.Store == WerReportStoreKind.MachineQueue.ToString()).Available);
    }

    [Fact]
    public async Task Source_MapsMetadataWithoutExposingFullAssociatedPaths()
    {
        var report = new WerReportStoreRecord(
            WerReportStoreKind.UserArchive.ToString(),
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff"),
            new DateTimeOffset(2026, 9, 9, 6, 0, 0, TimeSpan.Zero),
            1234,
            7,
            2,
            "LiveKernelEvent",
            new[]
            {
                new WerSignatureParameter("P1", "193"),
                new WerSignatureParameter("P2", "80e")
            },
            new[]
            {
                @"C:\Users\Private\WATCHDOG.dmp",
                @"C:\ProgramData\Microsoft\Windows\WER\Report.wer"
            });
        var reader = new StubReader(report);
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 9, 9, 7, 0, 0, TimeSpan.Zero));
        var source = new WindowsWerReportStoreSource(reader, time);

        var artifacts = await source.ReadSinceAsync(
            new DateTimeOffset(2026, 9, 9, 5, 0, 0, TimeSpan.Zero));

        var artifact = Assert.Single(artifacts);
        Assert.Equal("WindowsWerReportStore", artifact.Source);
        Assert.Equal("LiveKernelEvent", artifact.EventType);
        Assert.Equal(report.ReportId.ToString("D"), artifact.ReportId);
        Assert.Equal(report.CreationTimeUtc, artifact.SourceOccurredAtUtc);
        Assert.Equal("WATCHDOG.dmp; Report.wer", artifact.Properties["AssociatedFiles"]);
        Assert.DoesNotContain("C:\\Users\\Private", artifact.Properties["AssociatedFiles"], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("P1=193", artifact.Signature);
        Assert.Equal(1, source.LastProbeSnapshot.Stores.Single().ReportCount);
    }

    [Fact]
    public async Task Source_FiltersReportsOlderThanRequestedWindow()
    {
        var report = new WerReportStoreRecord(
            WerReportStoreKind.UserQueue.ToString(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateTimeOffset(2026, 9, 8, 1, 0, 0, TimeSpan.Zero),
            1,
            0,
            0,
            "APPCRASH",
            Array.Empty<WerSignatureParameter>(),
            Array.Empty<string>());
        var source = new WindowsWerReportStoreSource(
            new StubReader(report),
            new FixedTimeProvider(new DateTimeOffset(2026, 9, 9, 7, 0, 0, TimeSpan.Zero)));

        var artifacts = await source.ReadSinceAsync(
            new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero));

        Assert.Empty(artifacts);
    }

    private sealed class StubReader : IWerReportStoreReader
    {
        private readonly IReadOnlyList<WerReportStoreRecord> _reports;

        public StubReader(params WerReportStoreRecord[] reports)
        {
            _reports = reports;
        }

        public WerReportStoreReadResult Read(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new WerReportStoreReadResult(
                _reports,
                new[] { new WerReportStoreProbeStatus("UserArchive", true, _reports.Count, null) });
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private sealed class FakeWerNativeApi : IWindowsWerNativeApi
    {
        private const int S_OK = 0;
        private const int E_ACCESSDENIED = unchecked((int)0x80070005);
        private const int HResultNoMoreFiles = unchecked((int)0x80070012);
        private const int HResultInsufficientBuffer = unchecked((int)0x8007007A);

        private bool _returnedUserQueueKey;

        public int CloseCount { get; private set; }
        public int FreeStringCount { get; private set; }
        public int SecondPassQueryCount { get; private set; }

        public int StoreOpen(WerReportStoreKind store, out nint handle)
        {
            if (store == WerReportStoreKind.UserQueue)
            {
                handle = new nint(42);
                return S_OK;
            }

            handle = 0;
            return E_ACCESSDENIED;
        }

        public void StoreClose(nint handle)
        {
            Assert.Equal(new nint(42), handle);
            CloseCount++;
        }

        public int GetFirstReportKey(nint handle, out nint reportKey)
        {
            if (_returnedUserQueueKey)
            {
                reportKey = 0;
                return HResultNoMoreFiles;
            }

            _returnedUserQueueKey = true;
            reportKey = Marshal.StringToHGlobalUni("ReportKey-1");
            return S_OK;
        }

        public int GetNextReportKey(nint handle, out nint reportKey)
        {
            reportKey = 0;
            return HResultNoMoreFiles;
        }

        public void FreeString(nint value)
        {
            Marshal.FreeHGlobal(value);
            FreeStringCount++;
        }

        public int QueryReportMetadataV2(
            nint handle,
            string reportKey,
            ref WerReportMetadataV2 metadata)
        {
            metadata.Signature = WerReportSignature.Create();
            metadata.Signature.EventName = "APPCRASH";
            metadata.Signature.Parameters![0] = new WerReportParameter { Name = "P1", Value = "game.exe" };
            metadata.ReportId = Guid.Parse("11111111-2222-3333-4444-555555555555");
            metadata.BucketId = Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb");
            var creation = new DateTimeOffset(2026, 9, 9, 5, 30, 0, TimeSpan.Zero).ToFileTime();
            metadata.CreationTime = new WerFileTime
            {
                LowDateTime = unchecked((uint)creation),
                HighDateTime = unchecked((uint)(creation >> 32))
            };
            metadata.SizeInBytes = 9876;
            metadata.ReportStatus = 3;
            metadata.NumberOfFiles = 2;

            var multiString = "C:\\Private\\game.dmp\0C:\\WER\\Report.wer\0\0";
            metadata.SizeOfFileNames = (uint)multiString.Length;

            if (metadata.FileNames == 0)
            {
                return HResultInsufficientBuffer;
            }

            var bytes = System.Text.Encoding.Unicode.GetBytes(multiString);
            Marshal.Copy(bytes, 0, metadata.FileNames, bytes.Length);
            SecondPassQueryCount++;
            return S_OK;
        }
    }
}

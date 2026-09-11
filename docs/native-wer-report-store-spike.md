# Native WER report-store spike

This spike evaluates Windows Error Reporting's official report-store APIs as an additional metadata source for CrashScope.

## Why this exists

CrashScope already correlates Windows Event Log evidence, `Report.wer` metadata and LiveKernelReports artifacts. The official WER store can expose report identity and source time directly, which may improve deduplication and coverage when filesystem access is incomplete.

Microsoft documents `WerStoreOpen`, report-key enumeration, and `WerStoreQueryReportMetadataV2/V3` for stored WER reports. Metadata includes a locally unique ReportId, UTC CreationTime, report signature/event parameters, bucket ID, status, report size and associated file names.

References:
- https://learn.microsoft.com/windows/win32/api/werapi/nf-werapi-werstoreopen
- https://learn.microsoft.com/windows/win32/api/werapi/nf-werapi-werstoregetfirstreportkey
- https://learn.microsoft.com/windows/win32/api/werapi/nf-werapi-werstoregetnextreportkey
- https://learn.microsoft.com/windows/win32/api/werapi/nf-werapi-werstorequeryreportmetadatav2
- https://learn.microsoft.com/windows/win32/api/werapi/ns-werapi-wer_report_metadata_v2
- https://learn.microsoft.com/windows/win32/api/werapi/ns-werapi-wer_report_signature

## Safety boundary

The native source is **not** part of live incident triggering yet.

It is registered only for an explicit localhost comparison endpoint:

```text
GET /api/diagnostics/wer-store/probe?days=7
```

That endpoint:
- opens WER stores read-only
- enumerates report keys
- queries metadata only
- never calls WER purge/upload/modify APIs
- never opens or reads dump-file bytes
- compares native ReportIds against CrashScope's existing WER evidence source
- returns sanitized differences without full associated paths
- records per-store availability and total native call time

The live `IDiagnosticArtifactSource` used by `LiveIncidentMonitor` remains the existing `WindowsDiagnosticArtifactSource`.

## Store handling

The probe attempts, in order:
1. user queue
2. user archive
3. machine queue
4. machine archive

An inaccessible store is reported as unavailable and does not fail the Agent. Every successfully opened store is closed in `finally`, and each report-key string returned by WER is released with `WerFreeString`.

Microsoft explicitly warns that applications cannot depend on how long reports remain in the machine archive, so native WER is treated as opportunistic evidence rather than a durable historical database.

## Privacy

Native metadata can contain file paths. CrashScope does not return those paths from the probe endpoint. Associated files are reduced to file names before they become diagnostic properties, and no native WER metadata is automatically added to support bundles by this spike.

## Real-machine validation

After a CI artifact containing this spike is running on Windows 11, use:

```powershell
.\scripts\Probe-WerReportStore.ps1 -Days 7 -OutputPath .\wer-probe.json
```

Record:
- which of the four stores are accessible as a normal user
- native call duration
- native report count
- existing WER report count
- matching ReportIds
- native-only candidates
- existing-only candidates

Run the same command once as Administrator only for comparison if normal-user machine-store access is restricted. Administrator access must not become a product requirement.

## Promotion gate

Do not feed native WER into live incident triggering until real-machine evidence shows:
- useful incremental or equal coverage
- acceptable on-demand/reconciliation cost
- stable normal-user behavior
- no duplicate-incident regression
- correct ReportId/source-time correlation

If promoted later, prefer sparse or event-triggered reconciliation rather than continuous WER polling.

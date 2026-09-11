export interface MetricReading {
  value: number | null;
  rawValue: number | null;
  state: number;
  detail: string | null;
  isAvailable: boolean;
}

export interface LogicalProcessorLoad {
  logicalProcessorIndex: number;
  utilizationPercent: MetricReading;
}

export interface SystemTelemetry {
  physicalMemoryUsedMiB: MetricReading;
  physicalMemoryAvailableMiB: MetricReading;
  physicalMemoryLoadPercent: MetricReading;
}

export interface CpuTelemetry {
  totalUtilizationPercent: MetricReading;
  logicalProcessorUtilization: LogicalProcessorLoad[];
  temperatureCelsius: MetricReading;
  packagePowerWatts: MetricReading;
  averageClockMHz: MetricReading;
}

export interface GpuTelemetry {
  deviceId: string;
  name: string;
  coreUtilizationPercent: MetricReading;
  memoryUtilizationPercent: MetricReading;
  coreTemperatureCelsius: MetricReading;
  hotspotTemperatureCelsius: MetricReading;
  memoryTemperatureCelsius: MetricReading;
  packagePowerWatts: MetricReading;
  coreClockMHz: MetricReading;
  memoryClockMHz: MetricReading;
  dedicatedMemoryUsedMiB: MetricReading;
  dedicatedMemoryTotalMiB: MetricReading;
  fanSpeedRpm: MetricReading;
  fanControlPercent: MetricReading;
}

export interface TelemetryFrame {
  sequence: number;
  timestampUtc: string;
  monotonicTimestampTicks: number;
  system: SystemTelemetry;
  cpu: CpuTelemetry;
  gpus: GpuTelemetry[];
}

export interface AgentStatus {
  status: string;
  crashScopeVersion: string;
  samplingMode: 'Background' | 'Active';
  telemetryFramesBuffered: number;
  latestSequence: number | null;
  latestTimestampUtc: string | null;
  incidentCount: number;
  sessionCount: number;
  activeSessionId: string | null;
  activeProcessName: string | null;
  automaticMonitoringEnabled: boolean;
  automaticMonitoringState: 'Ready' | 'Confirming' | 'Monitoring' | 'PausedByUser' | 'Disabled' | string;
  automaticCandidateProcessName: string | null;
  automaticCandidateScore: number | null;
  automaticConfirmationCount: number;
  automaticSuppressedUntilUtc: string | null;
  automaticLastAttachReason: string | null;
  automaticObservationIntervalSeconds: number;
  manualCaptureInProgress: boolean;
  streamSubscribers: number;
  streamFramesPublished: number;
  streamDeliveryAttempts: number;
  streamDroppedStaleFrames: number;
  streamDeliveryMisses: number;
  persistence: string;
  schemaVersion: number;
  diagnosticsScanSeconds: number;
  backgroundSampleSeconds: number;
  activeSampleSeconds: number;
}

export interface CrashScopeSettings {
  schemaVersion: number;
  autoAssistEnabled: boolean;
  retentionDays: number;
  configTraceEnabled: boolean;
  configTraceRootPath: string | null;
}

export interface StartupRegistrationStatus {
  supported: boolean;
  enabled: boolean;
  detail: string | null;
}

export interface SupportBundlePreview {
  fileName: string;
  entries: string[];
  privacyNotes: string[];
}

export type WorkloadKind =
  | 'Game'
  | 'BenchmarkOrStressTest'
  | 'AiOrCompute'
  | 'CreativeOrRenderer'
  | 'GeneralApplication'
  | 'HelperOrSystem';

export type WorkloadRecommendation = 'Recommended' | 'Possible' | 'Unlikely';

export interface WorkloadCandidate {
  processId: number;
  processStartTimeUtc: string;
  processName: string;
  executablePath: string | null;
  windowTitle: string | null;
  workingSetMiB: number;
  kind: WorkloadKind;
  recommendation: WorkloadRecommendation;
  recommendationScore: number;
  recommendationReasons: string[];
}

export interface EnvironmentGpuSnapshot {
  name: string;
  driverVersion: string | null;
}

export interface SessionEnvironmentSnapshot {
  capturedAtUtc: string;
  operatingSystem: string;
  osArchitecture: string;
  processArchitecture: string;
  runtimeDescription: string;
  crashScopeVersion: string;
  cpuName: string | null;
  logicalProcessorCount: number;
  physicalMemoryMiB: number | null;
  gpus: EnvironmentGpuSnapshot[];
}

export interface SessionTelemetrySummary {
  frameCount: number;
  peakCpuUtilizationPercent: number | null;
  peakGpuUtilizationPercent: number | null;
  peakGpuHotspotCelsius: number | null;
  peakGpuMemoryUsedMiB: number | null;
  peakSystemMemoryLoadPercent: number | null;
}

export interface WorkloadSession {
  sessionId: string;
  processId: number;
  processStartTimeUtc: string;
  processName: string;
  executablePath: string | null;
  startedAtUtc: string;
  endedAtUtc: string | null;
  endReason: string | null;
  telemetry: SessionTelemetrySummary;
  incidentIds: string[];
  environment: SessionEnvironmentSnapshot | null;
  isActive: boolean;
}

export type IncidentClassification =
  | 'Unclassified'
  | 'ApplicationFailure'
  | 'KernelOrDriverWatchdog'
  | 'HardwareError'
  | 'UnexpectedShutdown'
  | 'Mixed'
  | 'UserDiagnosticMarker';

export type IncidentEvidenceRole = 'Trigger' | 'Corroborating' | 'Context';

export interface IncidentEvidence {
  role: IncidentEvidenceRole;
  source: string;
  kind: string;
  occurredAtUtc: string;
  observedAtUtc: string | null;
  summary: string;
  evidenceKey: string | null;
}

export interface IncidentProcessContext {
  processId: number;
  processStartTimeUtc: string;
  name: string;
  executablePath: string | null;
  observationState: string;
}

export interface IncidentTelemetrySummary {
  frameCount: number;
  peakCpuUtilizationPercent: number | null;
  peakGpuUtilizationPercent: number | null;
  peakGpuHotspotCelsius: number | null;
  peakGpuMemoryUsedMiB: number | null;
  peakSystemMemoryLoadPercent: number | null;
  windowStartedAtUtc: string | null;
  windowEndedAtUtc: string | null;
}

export interface IncidentReport {
  incidentId: string;
  incidentTimeUtc: string;
  classification: IncidentClassification;
  title: string;
  summary: string;
  assessment: string;
  process: IncidentProcessContext | null;
  telemetry: IncidentTelemetrySummary;
  evidence: IncidentEvidence[];
}

export interface LiveMessage {
  type: 'telemetry';
  telemetry: TelemetryFrame;
  status: {
    samplingMode: 'Background' | 'Active';
    activeSessionId: string | null;
    activeProcessName: string | null;
    incidentCount?: number;
    sessionCount?: number;
  };
}

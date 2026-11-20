namespace SoftReset.Core.Models;

/// <summary>
/// A point-in-time capture of everything the diagnostic engine reasons about.
/// Collectors fill what they can; anything they could not read is recorded in
/// <see cref="CollectionIssues"/> so rules can state their uncertainty honestly.
/// </summary>
public sealed class SystemSnapshot
{
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.Now;
    public string MachineName { get; set; } = "";
    public bool CollectedElevated { get; set; }
    public string PlatformName { get; set; } = "";
    public OsInfo Os { get; set; } = new();
    public HardwareInfo Hardware { get; set; } = new();
    public PerformanceSample Performance { get; set; } = new();
    public List<DriveStatus> Drives { get; set; } = new();
    public List<PhysicalDiskInfo> PhysicalDisks { get; set; } = new();
    public List<ProcessSample> TopProcesses { get; set; } = new();
    public int ProcessCount { get; set; }
    public int ThreadCount { get; set; }
    public int HandleCount { get; set; }
    public List<StartupEntry> StartupEntries { get; set; } = new();
    public ReliabilityData Reliability { get; set; } = new();
    public List<DeviceProblem> DeviceProblems { get; set; } = new();
    public List<GpuInfo> Gpus { get; set; } = new();
    public List<DisplayInfo> Displays { get; set; } = new();
    public PowerStatus Power { get; set; } = new();
    public NetworkStatus Network { get; set; } = new();
    public SecurityStatus Security { get; set; } = new();
    public UpdateStatus Updates { get; set; } = new();
    public GamingConfig Gaming { get; set; } = new();
    public CleanupCandidates Cleanup { get; set; } = new();
    public List<ServiceSummary> NonRunningAutoServices { get; set; } = new();
    public List<SuspiciousProcessIndicator> ProcessIndicators { get; set; } = new();
    public ThermalData Thermal { get; set; } = new();
    public List<CollectionIssue> CollectionIssues { get; set; } = new();
    public TimeSpan CollectionDuration { get; set; }

    public DriveStatus? SystemDrive =>
        Drives.FirstOrDefault(d => d.IsSystemDrive) ?? Drives.FirstOrDefault();

    public void AddIssue(string area, string message, bool requiresAdmin = false) =>
        CollectionIssues.Add(new CollectionIssue { Area = area, Message = message, RequiresAdmin = requiresAdmin });

    public bool HasIssue(string area) => CollectionIssues.Any(i => i.Area.Equals(area, StringComparison.OrdinalIgnoreCase));
}

public sealed class CollectionIssue
{
    public string Area { get; set; } = "";
    public string Message { get; set; } = "";
    public bool RequiresAdmin { get; set; }
}

public sealed class OsInfo
{
    public string ProductName { get; set; } = "";
    public string DisplayVersion { get; set; } = "";
    public string Edition { get; set; } = "";
    public int Build { get; set; }
    public int Ubr { get; set; }
    public string Architecture { get; set; } = "";
    public DateTimeOffset? InstallDate { get; set; }
    public DateTimeOffset? LastBootTime { get; set; }
    public bool IsWindows11 => Build >= 22000;
    public TimeSpan? Uptime(DateTimeOffset now) => LastBootTime is { } b ? now - b : null;
    public string FriendlyName => string.IsNullOrWhiteSpace(ProductName)
        ? $"Build {Build}"
        : $"{(IsWindows11 && ProductName.Contains("Windows 10") ? ProductName.Replace("Windows 10", "Windows 11") : ProductName)} {DisplayVersion} (build {Build}.{Ubr})".Trim();
}

public sealed class HardwareInfo
{
    public string Manufacturer { get; set; } = "";
    public string Model { get; set; } = "";
    public string CpuName { get; set; } = "";
    public int CpuCores { get; set; }
    public int CpuLogicalProcessors { get; set; }
    public int CpuMaxClockMHz { get; set; }
    public long TotalMemoryBytes { get; set; }
    public List<MemoryModule> MemoryModules { get; set; } = new();
    public string BiosVersion { get; set; } = "";
    public DateTimeOffset? BiosDate { get; set; }
    public string Motherboard { get; set; } = "";
    public bool IsLaptop { get; set; }
    public BatteryInfo? Battery { get; set; }
    public string SystemType => IsLaptop ? "Laptop / portable" : "Desktop";
}

public sealed class MemoryModule
{
    public string Slot { get; set; } = "";
    public long CapacityBytes { get; set; }
    public int SpeedMHz { get; set; }
    public int ConfiguredSpeedMHz { get; set; }
    public string Manufacturer { get; set; } = "";
    public string PartNumber { get; set; } = "";
}

public sealed class BatteryInfo
{
    public int? ChargePercent { get; set; }
    public bool OnAcPower { get; set; }
    public long? DesignCapacityMWh { get; set; }
    public long? FullChargeCapacityMWh { get; set; }
    public int? CycleCount { get; set; }
    public double? WearPercent => DesignCapacityMWh is > 0 && FullChargeCapacityMWh is > 0
        ? Math.Round(100.0 * (1 - (double)FullChargeCapacityMWh.Value / DesignCapacityMWh.Value), 1)
        : null;
}

public sealed class PerformanceSample
{
    public double SampleSeconds { get; set; }
    public double CpuAveragePercent { get; set; }
    public double CpuPeakPercent { get; set; }
    public List<double> CpuSeries { get; set; } = new();
    public long MemoryTotalBytes { get; set; }
    public long MemoryAvailableBytes { get; set; }
    public long CommitTotalBytes { get; set; }
    public long CommitLimitBytes { get; set; }
    public double? CpuFrequencyPercentOfMax { get; set; }
    public double MemoryUsedPercent => MemoryTotalBytes > 0
        ? Math.Round(100.0 * (MemoryTotalBytes - MemoryAvailableBytes) / MemoryTotalBytes, 1)
        : 0;
    public double CommitPercent => CommitLimitBytes > 0
        ? Math.Round(100.0 * CommitTotalBytes / CommitLimitBytes, 1)
        : 0;
}

public sealed class DriveStatus
{
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";
    public string FileSystem { get; set; } = "";
    public long TotalBytes { get; set; }
    public long FreeBytes { get; set; }
    public bool IsSystemDrive { get; set; }
    public string DriveType { get; set; } = "Fixed";
    public double FreePercent => TotalBytes > 0 ? Math.Round(100.0 * FreeBytes / TotalBytes, 1) : 0;
}

public sealed class PhysicalDiskInfo
{
    public string FriendlyName { get; set; } = "";
    public string MediaType { get; set; } = "Unknown"; // SSD, HDD, SCM, Unknown
    public string BusType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string HealthStatus { get; set; } = "Unknown"; // Healthy, Warning, Unhealthy
    public List<string> OperationalStatus { get; set; } = new();
    public bool? PredictFailure { get; set; }
    public int? TemperatureC { get; set; }
    public int? WearPercent { get; set; }
    public long? ReadErrorsUncorrected { get; set; }
    public long? PowerOnHours { get; set; }
    public bool ReliabilityCountersAvailable { get; set; }
}

public sealed class ProcessSample
{
    public int Pid { get; set; }
    public string Name { get; set; } = "";
    public string? Path { get; set; }
    public double CpuPercent { get; set; }
    public long WorkingSetBytes { get; set; }
    public long PrivateBytes { get; set; }
    public string? Publisher { get; set; }
    public string? Description { get; set; }
    public SignatureState Signature { get; set; } = SignatureState.NotChecked;
    public int SessionId { get; set; }
}

public enum SignatureState
{
    NotChecked,
    Signed,
    SignedMicrosoftWindows,
    Unsigned,
    InvalidOrUntrusted,
    Expired,
    Revoked,
    AccessDenied,
    FileNotFound,
}

public sealed class StartupEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public string? ExecutablePath { get; set; }
    public StartupLocation Location { get; set; }
    public string LocationDetail { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string? Publisher { get; set; }
    public SignatureState Signature { get; set; } = SignatureState.NotChecked;
    public bool IsMicrosoft { get; set; }
    public bool MachineWide { get; set; }
    public bool FileExists { get; set; } = true;
    public string? KnowledgeKey { get; set; }
}

public enum StartupLocation
{
    RegistryRunUser,
    RegistryRunMachine,
    RegistryRunMachineWow64,
    StartupFolderUser,
    StartupFolderCommon,
    ScheduledTaskLogon,
    Service,
}

public sealed class ReliabilityData
{
    public int LookbackDays { get; set; } = 30;
    public List<EventEntry> Events { get; set; } = new();
    public bool SystemLogReadable { get; set; } = true;
    public bool ApplicationLogReadable { get; set; } = true;
    public List<DumpFileInfo> CrashDumps { get; set; } = new();
    public bool CrashDumpsReadable { get; set; } = true;
    public List<BootPerformance> BootRecords { get; set; } = new();
    public bool BootRecordsReadable { get; set; } = true;

    public IEnumerable<EventEntry> Find(string provider, params int[] ids) =>
        Events.Where(e => e.Provider.Contains(provider, StringComparison.OrdinalIgnoreCase)
                          && (ids.Length == 0 || ids.Contains(e.Id)));

    public IEnumerable<EventEntry> FindIds(string log, params int[] ids) =>
        Events.Where(e => e.Log.Equals(log, StringComparison.OrdinalIgnoreCase) && ids.Contains(e.Id));
}

public sealed class EventEntry
{
    public string Log { get; set; } = "";
    public string Provider { get; set; } = "";
    public int Id { get; set; }
    public string Level { get; set; } = "";
    public DateTimeOffset Time { get; set; }
    public string Message { get; set; } = "";
    public Dictionary<string, string> Data { get; set; } = new();

    public string? Get(string key) => Data.TryGetValue(key, out var v) ? v : null;
}

public sealed class DumpFileInfo
{
    public string Path { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTimeOffset Created { get; set; }
    public string Kind { get; set; } = "Minidump"; // Minidump, Full, LiveKernelReport
}

public sealed class BootPerformance
{
    public DateTimeOffset Time { get; set; }
    public int BootTimeMs { get; set; }
    public int? MainPathBootTimeMs { get; set; }
    public int? PostBootTimeMs { get; set; }
    public List<string> Degradations { get; set; } = new();
}

public sealed class DeviceProblem
{
    public string Name { get; set; } = "";
    public string DeviceClass { get; set; } = "";
    public string PnpDeviceId { get; set; } = "";
    public int ErrorCode { get; set; }
    public string Manufacturer { get; set; } = "";
    public string ErrorDescription => DeviceErrorCodes.Describe(ErrorCode);
}

public static class DeviceErrorCodes
{
    public static string Describe(int code) => code switch
    {
        1 => "Code 1: device is not configured correctly",
        3 => "Code 3: driver may be corrupted or the system is low on memory",
        10 => "Code 10: device cannot start",
        12 => "Code 12: not enough free resources",
        14 => "Code 14: restart required",
        18 => "Code 18: drivers need to be reinstalled",
        19 => "Code 19: registry configuration is incomplete or damaged",
        21 => "Code 21: Windows is removing the device",
        22 => "Code 22: device is disabled",
        24 => "Code 24: device is not present, not working properly, or drivers are missing",
        28 => "Code 28: drivers are not installed",
        29 => "Code 29: device is disabled by firmware",
        31 => "Code 31: Windows cannot load the required drivers",
        32 => "Code 32: driver service is disabled",
        33 => "Code 33: Windows cannot determine required resources",
        34 => "Code 34: device requires manual configuration",
        35 => "Code 35: firmware lacks information to configure the device",
        37 => "Code 37: driver returned a failure during initialization",
        38 => "Code 38: previous driver instance still in memory",
        39 => "Code 39: driver is corrupted or missing",
        40 => "Code 40: driver registry entry is invalid",
        41 => "Code 41: driver loaded but hardware not found",
        42 => "Code 42: duplicate device detected",
        43 => "Code 43: device reported a problem and Windows stopped it",
        44 => "Code 44: an application or service shut the device down",
        45 => "Code 45: device is not connected",
        47 => "Code 47: device prepared for safe removal",
        48 => "Code 48: driver blocked from starting due to known problems",
        49 => "Code 49: system registry hive is too large",
        52 => "Code 52: driver digital signature cannot be verified",
        _ => $"Code {code}: device reported a configuration problem",
    };
}

public sealed class GpuInfo
{
    public string Name { get; set; } = "";
    public GpuVendor Vendor { get; set; }
    public string DriverVersion { get; set; } = "";
    public string? VendorDriverVersion { get; set; }
    public DateTimeOffset? DriverDate { get; set; }
    public long? DedicatedMemoryBytes { get; set; }
    public string PnpDeviceId { get; set; } = "";
    public bool IsIntegrated { get; set; }
    public bool HasAttachedDisplay { get; set; }
    public int? ConfigManagerErrorCode { get; set; }
    public GpuTelemetry? Telemetry { get; set; }
}

public enum GpuVendor { Unknown, Nvidia, Amd, Intel, Microsoft, Other }

public sealed class GpuTelemetry
{
    public string Source { get; set; } = ""; // NVML, nvidia-smi, D3DKMT, PerfCounter
    public double? TemperatureC { get; set; }
    public double? UtilizationPercent { get; set; }
    public double? MemoryUtilizationPercent { get; set; }
    public long? MemoryUsedBytes { get; set; }
    public long? MemoryTotalBytes { get; set; }
    public double? PowerWatts { get; set; }
    public double? PowerLimitWatts { get; set; }
    public int? FanPercent { get; set; }
    public int? FanRpm { get; set; }
    public int? GraphicsClockMHz { get; set; }
    public int? MemoryClockMHz { get; set; }
    public string? PerformanceState { get; set; }
    public string? PcieLink { get; set; }
    public List<string> ThrottleReasons { get; set; } = new();
}

public sealed class DisplayInfo
{
    public string DeviceName { get; set; } = "";
    public string MonitorName { get; set; } = "";
    public string AdapterName { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public int RefreshHz { get; set; }
    public int MaxRefreshHzAtCurrentResolution { get; set; }
    public int BitsPerPixel { get; set; }
    public bool IsPrimary { get; set; }
    public int? ScalePercent { get; set; }
    public List<DisplayMode> SupportedModes { get; set; } = new();
}

public sealed record DisplayMode(int Width, int Height, int RefreshHz, int BitsPerPixel)
{
    public override string ToString() => $"{Width}x{Height} @ {RefreshHz} Hz";
}

public sealed class PowerStatus
{
    public string ActiveSchemeGuid { get; set; } = "";
    public string ActiveSchemeName { get; set; } = "";
    public List<PowerScheme> Schemes { get; set; } = new();
    public bool? OnAcPower { get; set; }
    public bool? BatterySaverOn { get; set; }
    public int? ProcessorMinStateAc { get; set; }
    public int? ProcessorMaxStateAc { get; set; }
    public int? ProcessorMaxStateDc { get; set; }
    public bool? FastStartupEnabled { get; set; }
    public bool? HibernationEnabled { get; set; }
    public bool? ModernStandby { get; set; }
    public string? PowerModeOverlay { get; set; }
}

public sealed class PowerScheme
{
    public string Guid { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Active { get; set; }
}

public static class WellKnownPowerSchemes
{
    public const string Balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";
    public const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    public const string PowerSaver = "a1841308-3541-4fab-bc81-f71556f20b4a";
    public const string UltimatePerformance = "e9a42b02-d5df-448d-aa00-03f14749eb61";
}

public sealed class NetworkStatus
{
    public List<NetworkAdapterInfo> Adapters { get; set; } = new();
    public bool ProxyEnabled { get; set; }
    public string? ProxyServer { get; set; }
    public string? AutoConfigUrl { get; set; }
    public int HostsFileEntryCount { get; set; }
    public List<string> HostsFileNotableEntries { get; set; } = new();
    public WifiInfo? Wifi { get; set; }
    public ConnectivityTest? Connectivity { get; set; }
}

public sealed class NetworkAdapterInfo
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Type { get; set; } = "";
    public bool IsUp { get; set; }
    public long SpeedBitsPerSecond { get; set; }
    public List<string> IPv4Addresses { get; set; } = new();
    public List<string> Gateways { get; set; } = new();
    public List<string> DnsServers { get; set; } = new();
    public bool DhcpEnabled { get; set; }
    public bool IsVirtual { get; set; }
    public string MacAddress { get; set; } = "";
}

public sealed class WifiInfo
{
    public string Ssid { get; set; } = "";
    public int? SignalPercent { get; set; }
    public string RadioType { get; set; } = "";
    public string Band { get; set; } = "";
    public string Channel { get; set; } = "";
    public double? ReceiveRateMbps { get; set; }
    public double? TransmitRateMbps { get; set; }
    public string Authentication { get; set; } = "";
}

public sealed class ConnectivityTest
{
    public DateTimeOffset Time { get; set; }
    public bool? GatewayReachable { get; set; }
    public long? GatewayLatencyMs { get; set; }
    public bool? DnsResolutionWorks { get; set; }
    public long? DnsLatencyMs { get; set; }
    public bool? InternetReachable { get; set; }
    public long? InternetLatencyMs { get; set; }
    public string Notes { get; set; } = "";
}

public sealed class SecurityStatus
{
    public bool? DefenderAntivirusEnabled { get; set; }
    public bool? DefenderRealTimeEnabled { get; set; }
    public int? DefenderSignatureAgeDays { get; set; }
    public bool? DefenderTamperProtection { get; set; }
    public List<string> ThirdPartyAntivirus { get; set; } = new();
    public Dictionary<string, bool> FirewallProfiles { get; set; } = new();
    public bool? UacEnabled { get; set; }
    public int? UacConsentPromptBehaviorAdmin { get; set; }
    public bool? SecureBootEnabled { get; set; }
    public bool? TpmPresent { get; set; }
    public bool? MemoryIntegrityEnabled { get; set; }
    public string? BitLockerSystemDrive { get; set; }
    public bool? SmartScreenEnabled { get; set; }
}

public sealed class UpdateStatus
{
    public bool? RebootPending { get; set; }
    public List<string> RebootPendingReasons { get; set; } = new();
    public DateTimeOffset? LastSuccessfulInstall { get; set; }
    public List<UpdateHistoryItem> RecentHistory { get; set; } = new();
    public string? WindowsUpdateServiceStartMode { get; set; }
    public string? WindowsUpdateServiceState { get; set; }
    public bool? UpdatesPausedByPolicyOrUser { get; set; }
    public DateTimeOffset? PausedUntil { get; set; }
    public bool ManagedByPolicy { get; set; }
    public long? SoftwareDistributionDownloadBytes { get; set; }
}

public sealed class UpdateHistoryItem
{
    public string Title { get; set; } = "";
    public DateTimeOffset Date { get; set; }
    public string Result { get; set; } = ""; // Succeeded, Failed, Aborted, InProgress
    public string? HResult { get; set; }
}

public sealed class GamingConfig
{
    public bool? GameModeEnabled { get; set; }
    public bool? GameDvrBackgroundRecording { get; set; }
    public bool? GameDvrEnabled { get; set; }
    public bool? HardwareAcceleratedGpuScheduling { get; set; }
    public bool? HagsSupported { get; set; }
    public bool? VariableRefreshRateOptimization { get; set; }
    public bool? WindowedGameOptimizations { get; set; }
    public List<string> OverlayProcessesRunning { get; set; } = new();
}

public sealed class CleanupCandidates
{
    public long? UserTempBytes { get; set; }
    public int? UserTempFiles { get; set; }
    public long? WindowsTempBytes { get; set; }
    public long? RecycleBinBytes { get; set; }
    public long? RecycleBinItems { get; set; }
    public long? WindowsUpdateCacheBytes { get; set; }
    public long? CrashDumpBytes { get; set; }
    public long? DeliveryOptimizationBytes { get; set; }
    public long? HibernationFileBytes { get; set; }
    public long? PageFileBytes { get; set; }
    public long? DownloadsFolderBytes { get; set; }
    public long? ShaderCacheBytes { get; set; }
}

public sealed class ServiceSummary
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string StartMode { get; set; } = "";
    public string State { get; set; } = "";
    public string? PathName { get; set; }
    public int? ExitCode { get; set; }
}

public sealed class SuspiciousProcessIndicator
{
    public int Pid { get; set; }
    public string Name { get; set; } = "";
    public string? Path { get; set; }
    public List<string> Reasons { get; set; } = new();
    public SignatureState Signature { get; set; }
}

public sealed class ThermalData
{
    public List<ThermalReading> Readings { get; set; } = new();
    public int ThrottlingEvents { get; set; }
    public bool CpuSensorReliable { get; set; }
    public string Notes { get; set; } = "";
}

public sealed class ThermalReading
{
    public string Sensor { get; set; } = "";
    public double Celsius { get; set; }
    public string Source { get; set; } = "";
    public bool Reliable { get; set; }
}

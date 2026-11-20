using SoftReset.Core.Models;

namespace SoftReset.Core.Platform;

/// <summary>Aggregates every operating-system service the console needs.</summary>
public interface ISystemPlatform
{
    string Name { get; }
    bool IsWindows { get; }
    bool IsElevated { get; }
    int OsBuild { get; }
    IRegistryAccess Registry { get; }
    IKnownFolders Folders { get; }
    ISnapshotCollector Collector { get; }
    IProcessService Processes { get; }
    IServiceManager Services { get; }
    IScheduledTaskService Tasks { get; }
    IPowerService Power { get; }
    IDisplayService Display { get; }
    IGpuService Gpu { get; }
    INetworkService Network { get; }
    IAppInventoryService Apps { get; }
    IShellService Shell { get; }
    ICommandRunner Commands { get; }
    IMaintenanceService Maintenance { get; }
    IElevationService Elevation { get; }
    ISettingProbe SettingProbe { get; }
}

// ---------------------------------------------------------------- Registry

public enum RegHive { CurrentUser, LocalMachine, ClassesRoot, Users }

public enum RegView { Default, Registry64, Registry32 }

public enum RegKind { String, ExpandString, DWord, QWord, Binary, MultiString, None }

public sealed record RegValue(RegKind Kind, object? Data)
{
    public int? AsInt() => Data switch
    {
        int i => i,
        long l => unchecked((int)l),
        uint u => unchecked((int)u),
        string s when int.TryParse(s, out var p) => p,
        _ => null,
    };

    public long? AsLong() => Data switch
    {
        int i => i,
        long l => l,
        string s when long.TryParse(s, out var p) => p,
        _ => null,
    };

    public string? AsString() => Data switch
    {
        null => null,
        string s => s,
        string[] arr => string.Join("; ", arr),
        byte[] b => Convert.ToHexString(b),
        _ => Data.ToString(),
    };

    public byte[]? AsBytes() => Data as byte[];

    public static RegValue DWord(int v) => new(RegKind.DWord, v);
    public static RegValue Str(string v) => new(RegKind.String, v);
}

public interface IRegistryAccess
{
    bool KeyExists(RegHive hive, string path, RegView view = RegView.Default);
    RegValue? GetValue(RegHive hive, string path, string name, RegView view = RegView.Default);
    void SetValue(RegHive hive, string path, string name, RegValue value, RegView view = RegView.Default);
    void DeleteValue(RegHive hive, string path, string name, RegView view = RegView.Default);
    IReadOnlyList<string> GetValueNames(RegHive hive, string path, RegView view = RegView.Default);
    IReadOnlyList<string> GetSubKeyNames(RegHive hive, string path, RegView view = RegView.Default);
}

// ---------------------------------------------------------------- Folders

public interface IKnownFolders
{
    string UserProfile { get; }
    string Desktop { get; }
    string Downloads { get; }
    string Documents { get; }
    string Pictures { get; }
    string Videos { get; }
    string Music { get; }
    string AppDataRoaming { get; }
    string AppDataLocal { get; }
    string Temp { get; }
    string WindowsDir { get; }
    string System32 { get; }
    string ProgramFiles { get; }
    string ProgramFilesX86 { get; }
    string ProgramData { get; }
    string StartupUser { get; }
    string StartupCommon { get; }
}

// ---------------------------------------------------------------- Snapshot

public sealed class CollectionOptions
{
    public TimeSpan CpuSampleDuration { get; set; } = TimeSpan.FromSeconds(5);
    public bool IncludeConnectivityTests { get; set; }
    public bool IncludeSignatureChecks { get; set; } = true;
    public int EventLookbackDays { get; set; } = 30;
    public bool IncludeFolderSizes { get; set; } = true;
    public int TopProcessCount { get; set; } = 25;
}

public sealed record CollectionProgress(string Stage, int Percent);

public interface ISnapshotCollector
{
    Task<SystemSnapshot> CollectAsync(CollectionOptions options, IProgress<CollectionProgress>? progress, CancellationToken ct);
}

// ---------------------------------------------------------------- Processes

public sealed class ProcessInfo
{
    public int Pid { get; set; }
    public int? ParentPid { get; set; }
    public string Name { get; set; } = "";
    public string? Path { get; set; }
    public int SessionId { get; set; }
    public int Threads { get; set; }
    public int Handles { get; set; }
    public long WorkingSetBytes { get; set; }
    public long PrivateBytes { get; set; }
    public TimeSpan? TotalProcessorTime { get; set; }
    public DateTimeOffset? StartTime { get; set; }
    public bool PathAccessible { get; set; }
}

public sealed class FileDetails
{
    public string? Description { get; set; }
    public string? Company { get; set; }
    public string? Product { get; set; }
    public string? FileVersion { get; set; }
    public SignatureState Signature { get; set; } = SignatureState.NotChecked;
    public string? Signer { get; set; }
    public bool CatalogSigned { get; set; }
}

public interface IProcessService
{
    IReadOnlyList<ProcessInfo> Enumerate();
    FileDetails GetFileDetails(string path, bool checkSignature);
    IReadOnlyDictionary<int, IReadOnlyList<string>> GetServicesByPid();
    bool TryTerminate(int pid, out string error);
}

// ---------------------------------------------------------------- Services

public sealed class ServiceInfo
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public string State { get; set; } = "";
    public string StartMode { get; set; } = ""; // Auto, Manual, Disabled, Boot, System
    public bool DelayedAutoStart { get; set; }
    public string? PathName { get; set; }
    public int ProcessId { get; set; }
    public string? Account { get; set; }
    public int? ExitCode { get; set; }
    public bool IsMicrosoft { get; set; }
}

public interface IServiceManager
{
    IReadOnlyList<ServiceInfo> GetServices();
    ServiceInfo? Get(string name);
    void SetStartMode(string name, string startMode, bool delayedAutoStart);
    void Start(string name, TimeSpan timeout);
    void Stop(string name, TimeSpan timeout);
}

// ---------------------------------------------------------------- Tasks

public sealed class ScheduledTaskInfo
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public string State { get; set; } = "";
    public bool Enabled { get; set; }
    public string Author { get; set; } = "";
    public string Triggers { get; set; } = "";
    public string Action { get; set; } = "";
    public DateTimeOffset? LastRun { get; set; }
    public string LastResult { get; set; } = "";
    public DateTimeOffset? NextRun { get; set; }
    public bool HasLogonTrigger { get; set; }
    public bool HasBootTrigger { get; set; }
    public bool IsMicrosoft => Path.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase)
                               || Author.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase);
}

public interface IScheduledTaskService
{
    IReadOnlyList<ScheduledTaskInfo> GetTasks();
    void SetEnabled(string taskPath, bool enabled);
}

// ---------------------------------------------------------------- Power

public interface IPowerService
{
    PowerStatus GetStatus();
    void SetActiveScheme(string guid);
    string DuplicateScheme(string sourceGuid);
    void DeleteScheme(string guid);
    int? GetSettingIndex(string schemeGuid, string subgroupGuid, string settingGuid, bool ac);
    void SetSettingIndex(string schemeGuid, string subgroupGuid, string settingGuid, bool ac, int value);
    void SetHibernation(bool enabled);
}

public static class PowerSettingIds
{
    public const string SubProcessor = "54533251-82be-4824-96c1-47b60b740d00";
    public const string ProcThrottleMin = "893dee8e-2bef-41e0-89c6-b55d0929964c";
    public const string ProcThrottleMax = "bc5038f7-23e0-4960-96da-33abaf5935ec";
    public const string SubUsb = "2a737441-1930-4402-8d77-b2bebba308a3";
    public const string UsbSelectiveSuspend = "48e6b7a6-50f5-4782-a5d4-53bb8f07e226";
    public const string SubPciExpress = "501a4d13-42af-4429-9fd1-a8218c268e20";
    public const string PcieLinkStatePm = "ee12f906-d277-404b-b6da-e5fa1a576df5";
    public const string SubSleep = "238c9fa8-0aad-41ed-83f4-97be242c8f20";
    public const string StandbyIdle = "29f6c1db-86da-48c5-9fdb-f2b67b1f44da";
    public const string SubDisk = "0012ee47-9041-4b5d-9b77-535fba8b1442";
    public const string DiskIdle = "6738e2c4-e8a5-4a42-b16a-e040e769756e";
}

// ---------------------------------------------------------------- Display

public sealed class DisplayChangeResult
{
    public bool Success { get; set; }
    public bool RestartRequired { get; set; }
    public string Message { get; set; } = "";
}

public interface IDisplayService
{
    IReadOnlyList<DisplayInfo> GetDisplays();
    DisplayChangeResult SetMode(string deviceName, DisplayMode mode, bool testOnly);
}

// ---------------------------------------------------------------- GPU

public sealed class NvidiaDriverSetting
{
    public uint SettingId { get; set; }
    public string Name { get; set; } = "";
    public uint? Value { get; set; }
    public string? StringValue { get; set; }
    public bool IsPredefined { get; set; }
    public string Location { get; set; } = ""; // Current, Global, Base, Default
}

public sealed class NvidiaProfileSummary
{
    public string Name { get; set; } = "";
    public bool IsPredefined { get; set; }
    public int ApplicationCount { get; set; }
    public int SettingCount { get; set; }
    public int UserModifiedSettingCount { get; set; }
    public List<string> Applications { get; set; } = new();
    public List<NvidiaDriverSetting> UserSettings { get; set; } = new();
}

public sealed class NvidiaDriverSettingsResult
{
    public bool Available { get; set; }
    public string Status { get; set; } = "";
    public string? DriverVersion { get; set; }
    public string? DriverBranch { get; set; }
    public string GlobalProfileName { get; set; } = "";
    public List<NvidiaDriverSetting> GlobalSettings { get; set; } = new();
    public List<NvidiaProfileSummary> CustomizedProfiles { get; set; } = new();
}

public interface IGpuService
{
    IReadOnlyList<GpuInfo> GetGpus(bool includeTelemetry);
    Task<NvidiaDriverSettingsResult> ReadNvidiaDriverSettingsAsync(CancellationToken ct);
    VendorSoftwareStatus GetVendorSoftware();
}

public sealed class VendorSoftwareStatus
{
    public string? NvidiaControlPanel { get; set; }
    public string? NvidiaApp { get; set; }
    public string? GeForceExperience { get; set; }
    public string? AmdSoftware { get; set; }
    public string? IntelGraphicsSoftware { get; set; }
    public string? NvidiaSmiPath { get; set; }
}

// ---------------------------------------------------------------- Network

public interface INetworkService
{
    NetworkStatus GetStatus(bool runConnectivityTests);
}

// ---------------------------------------------------------------- Apps

public enum AppKind { Desktop, Store, Framework, SystemComponent }

public sealed class InstalledApp
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Version { get; set; } = "";
    public AppKind Kind { get; set; }
    public DateTimeOffset? InstallDate { get; set; }
    public string? InstallLocation { get; set; }
    public long? EstimatedSizeBytes { get; set; }
    public string? UninstallCommand { get; set; }
    public string? QuietUninstallCommand { get; set; }
    public string? ModifyCommand { get; set; }
    public bool IsMsi { get; set; }
    public string? MsiProductCode { get; set; }
    public string? PackageFamilyName { get; set; }
    public string? PackageFullName { get; set; }
    public bool IsInboxWindowsApp { get; set; }
    public bool MachineWide { get; set; }
    public string? RegistryKey { get; set; }
    public bool NoRemove { get; set; }
    public bool NoRepair { get; set; } = true;
    public string? DisplayIcon { get; set; }
}

public sealed class OptionalFeatureInfo
{
    public string Name { get; set; } = "";
    public string Caption { get; set; } = "";
    public string State { get; set; } = ""; // Enabled, Disabled, Absent, Unknown
}

public interface IAppInventoryService
{
    IReadOnlyList<InstalledApp> GetInstalledApps();
    IReadOnlyList<OptionalFeatureInfo> GetOptionalFeatures();
    long? ComputeFolderSize(string path, CancellationToken ct);
}

// ---------------------------------------------------------------- Shell

public enum LaunchKind
{
    /// <summary>A URI with an allow-listed scheme (ms-settings:, windowsdefender:, https:).</summary>
    Uri,
    /// <summary>An executable resolved from System32/Windows (no arbitrary paths).</summary>
    SystemTool,
    /// <summary>control.exe /name Microsoft.X</summary>
    ControlPanelItem,
    /// <summary>A .cpl applet from System32.</summary>
    ControlPanelApplet,
    /// <summary>An MMC snap-in (.msc) from System32.</summary>
    Msc,
    /// <summary>Open a folder in File Explorer.</summary>
    Folder,
    /// <summary>Select a file in File Explorer.</summary>
    RevealFile,
    /// <summary>A vendor application found on disk (NVIDIA/AMD/Intel control panels, Sysinternals).</summary>
    InstalledApplication,
    /// <summary>Opens a Store app by AUMID via shell:AppsFolder.</summary>
    AppsFolder,
}

public sealed record LaunchTarget(LaunchKind Kind, string Target, string? Arguments = null)
{
    public override string ToString() => Arguments is null ? $"{Kind}:{Target}" : $"{Kind}:{Target} {Arguments}";
}

public sealed class LaunchResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public static LaunchResult Ok(string m = "Opened.") => new() { Success = true, Message = m };
    public static LaunchResult Fail(string m) => new() { Success = false, Message = m };
}

public interface IShellService
{
    LaunchResult Launch(LaunchTarget target);
    bool IsAvailable(LaunchTarget target, out string reason);
}

// ---------------------------------------------------------------- Commands

public sealed class CommandResult
{
    public int ExitCode { get; set; }
    public string StdOut { get; set; } = "";
    public string StdErr { get; set; } = "";
    public bool TimedOut { get; set; }
    public bool Started { get; set; } = true;
    public string? StartError { get; set; }
    public bool Success => Started && !TimedOut && ExitCode == 0;
}

public interface ICommandRunner
{
    /// <summary>Runs an allow-listed system tool (resolved from System32) and captures its output.</summary>
    Task<CommandResult> RunAsync(string tool, string arguments, TimeSpan timeout, CancellationToken ct);
}

// ---------------------------------------------------------------- Maintenance

public sealed class RestorePointResult
{
    public bool Created { get; set; }
    public bool Skipped { get; set; }
    public string Message { get; set; } = "";
}

public interface IMaintenanceService
{
    bool? IsSystemRestoreEnabled();
    RestorePointResult CreateRestorePoint(string description);
    (long Bytes, long Items)? GetRecycleBinInfo();
    bool EmptyRecycleBin(out string error);
    bool SendToRecycleBin(string path, out string error);
    void BroadcastSettingsChanged();
    bool RestartExplorer(out string error);
    bool SetMouseAcceleration(bool enabled, out string error);
    bool? GetMouseAcceleration();

    /// <summary>Visual effects controlled through documented SystemParametersInfo flags (see <see cref="UiEffects"/>).</summary>
    bool? GetUiEffect(string name);
    bool SetUiEffect(string name, bool enabled, out string error);
}

/// <summary>Visual effects that SystemParametersInfo can read and write (same switches as "Performance Options").</summary>
public static class UiEffects
{
    public const string WindowAnimation = "WindowAnimation";           // SPI_SETANIMATION
    public const string ClientAreaAnimation = "ClientAreaAnimation";   // SPI_SETCLIENTAREAANIMATION
    public const string MenuAnimation = "MenuAnimation";               // SPI_SETMENUANIMATION
    public const string TooltipAnimation = "TooltipAnimation";         // SPI_SETTOOLTIPANIMATION
    public const string ComboBoxAnimation = "ComboBoxAnimation";       // SPI_SETCOMBOBOXANIMATION
    public const string ListBoxSmoothScrolling = "ListBoxSmoothScrolling"; // SPI_SETLISTBOXSMOOTHSCROLLING
    public const string SelectionFade = "SelectionFade";               // SPI_SETSELECTIONFADE
    public const string CursorShadow = "CursorShadow";                 // SPI_SETCURSORSHADOW
    public const string DropShadow = "DropShadow";                     // SPI_SETDROPSHADOW
    public const string DragFullWindows = "DragFullWindows";           // SPI_SETDRAGFULLWINDOWS

    public static readonly string[] All =
    {
        WindowAnimation, ClientAreaAnimation, MenuAnimation, TooltipAnimation, ComboBoxAnimation,
        ListBoxSmoothScrolling, SelectionFade, CursorShadow, DropShadow, DragFullWindows,
    };

    public static string Describe(string name) => name switch
    {
        WindowAnimation => "Animate windows when minimizing and maximizing",
        ClientAreaAnimation => "Animate controls and elements inside windows",
        MenuAnimation => "Fade or slide menus into view",
        TooltipAnimation => "Fade or slide ToolTips into view",
        ComboBoxAnimation => "Slide open combo boxes",
        ListBoxSmoothScrolling => "Smooth-scroll list boxes",
        SelectionFade => "Fade out menu items after clicking",
        CursorShadow => "Show shadows under mouse pointer",
        DropShadow => "Show shadows under windows",
        DragFullWindows => "Show window contents while dragging",
        _ => name,
    };
}

// ---------------------------------------------------------------- Elevation

public sealed class ElevatedRequest
{
    public string Operation { get; set; } = "apply"; // apply | rollback | maintenance
    public string ActionId { get; set; } = "";
    public Dictionary<string, string> Parameters { get; set; } = new();
    public List<string> SelectedItems { get; set; } = new();
    public string? ChangeRecordId { get; set; }
    public string DataDirectory { get; set; } = "";
    public string ResultFile { get; set; } = "";
    public string? SessionId { get; set; }
}

public sealed class ElevatedResult
{
    public bool Launched { get; set; }
    public bool UserCancelled { get; set; }
    public int ExitCode { get; set; }
    public string? ResultJson { get; set; }
    public string Message { get; set; } = "";
}

public interface IElevationService
{
    bool IsElevated { get; }
    bool CanElevate { get; }
    Task<ElevatedResult> RunElevatedAsync(ElevatedRequest request, CancellationToken ct);
    bool RelaunchElevated(out string error);
}

// ---------------------------------------------------------------- Setting probes

public sealed class SettingReadResult
{
    public bool Readable { get; set; }
    public string Display { get; set; } = "";
    public string? Raw { get; set; }
    public string? Note { get; set; }

    public static SettingReadResult Value(string display, string? raw = null, string? note = null) =>
        new() { Readable = true, Display = display, Raw = raw ?? display, Note = note };

    public static SettingReadResult Unreadable(string why) => new() { Readable = false, Display = "Not readable", Note = why };
}

public interface ISettingProbe
{
    SettingReadResult Read(string probeId);
}

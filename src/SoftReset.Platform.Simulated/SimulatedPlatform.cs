using SoftReset.Core.Apps;
using SoftReset.Core.Diagnostics;
using SoftReset.Core.Models;
using SoftReset.Core.Optimization;
using SoftReset.Core.Optimization.Actions;
using SoftReset.Core.Platform;
using SoftReset.Core.Startup;

namespace SoftReset.Platform.Simulated;

/// <summary>
/// A stateful simulation of a typical customer PC (a gaming desktop with common problems).
/// Every optimization action changes this state, so re-scans reflect applied fixes.
/// Used by automated tests and the console's --demo training mode. It never touches the
/// host operating system except for files inside its own sandbox directory.
/// </summary>
public sealed class SimulatedPlatform : ISystemPlatform, IDisposable
{
    public SimulatedPlatform(string? sandboxRoot = null, bool elevated = false)
    {
        SandboxRoot = sandboxRoot ?? Path.Combine(Path.GetTempPath(), "softreset-sim-" + Guid.NewGuid().ToString("N")[..8]);
        IsElevated = elevated;
        Registry = new InMemoryRegistry();
        Folders = new SimFolders(SandboxRoot);
        State = new SimState();
        Scenario.Seed(this);
        Collector = new SimCollector(this);
        Processes = new SimProcesses(this);
        Services = new SimServices(this);
        Tasks = new SimTasks(this);
        Power = new SimPower(this);
        Display = new SimDisplay(this);
        Gpu = new SimGpu(this);
        Network = new SimNetwork(this);
        Apps = new SimApps(this);
        Shell = new SimShell(this);
        Commands = new SimCommands(this);
        Maintenance = new SimMaintenance(this);
        Elevation = new SimElevation(this);
        SettingProbe = new SimProbe(this);
    }

    public string SandboxRoot { get; }
    public SimState State { get; }
    public InMemoryRegistry Registry { get; }
    IRegistryAccess ISystemPlatform.Registry => Registry;

    public string Name => "Simulated (demo PC)";
    public bool IsWindows => false;
    public bool IsElevated { get; set; }
    public int OsBuild { get; set; } = 22631;
    public IKnownFolders Folders { get; }
    public ISnapshotCollector Collector { get; }
    public IProcessService Processes { get; }
    public IServiceManager Services { get; }
    public IScheduledTaskService Tasks { get; }
    public IPowerService Power { get; }
    public IDisplayService Display { get; }
    public IGpuService Gpu { get; }
    public INetworkService Network { get; }
    public IAppInventoryService Apps { get; }
    public IShellService Shell { get; }
    public ICommandRunner Commands { get; }
    public IMaintenanceService Maintenance { get; }
    public IElevationService Elevation { get; }
    public ISettingProbe SettingProbe { get; }
    public ILiveMetrics Live { get; } = new SimLive();

    /// <summary>When true, the next elevation request behaves as if the user clicked "No" on the UAC prompt.</summary>
    public bool SimulateUacDecline { get; set; }

    /// <summary>
    /// The demo PC's programs are not real files. One startup entry is a deliberate leftover from an
    /// uninstalled program (Opera GX's assistant), a common real-world case the startup tools detect.
    /// </summary>
    public bool FileExists(string path) => !path.Contains("Opera GX", StringComparison.OrdinalIgnoreCase);
    public List<LaunchTarget> LaunchedTargets { get; } = new();
    public List<string> CommandLog { get; } = new();

    public void Dispose()
    {
        try { if (Directory.Exists(SandboxRoot)) Directory.Delete(SandboxRoot, true); } catch { }
    }
}

public sealed class SimState
{
    public List<PowerScheme> Schemes { get; } = new();
    public string ActiveScheme { get; set; } = WellKnownPowerSchemes.PowerSaver;
    public Dictionary<string, int> PowerSettings { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool HibernationEnabled { get; set; } = true;
    public List<DisplayInfo> Displays { get; } = new();
    public List<ProcessInfo> Processes { get; } = new();
    public Dictionary<string, FileDetails> FileDetails { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ServiceInfo> Services { get; } = new();
    public List<ScheduledTaskInfo> Tasks { get; } = new();
    public List<EventEntry> Events { get; } = new();
    public long RecycleBinBytes { get; set; } = 1_350_000_000;
    public long RecycleBinItems { get; set; } = 214;
    public long SystemDriveTotal { get; set; } = 499_570_000_000;
    public long SystemDriveBaseFree { get; set; } = 31_400_000_000;
    public long BytesFreedInSandbox { get; set; }
    public Dictionary<string, bool> UiEffects { get; } = new();
    public bool MouseAcceleration { get; set; } = true;
    public bool SystemRestoreEnabled { get; set; } = true;
    public int RestorePointsCreated { get; set; }
    public List<InstalledApp> StorePackages { get; } = new();
    public List<OptionalFeatureInfo> Features { get; } = new();
    public HashSet<int> ProtectedPids { get; } = new();
    public int BroadcastCount { get; set; }
    public int ExplorerRestarts { get; set; }
}

internal sealed class SimLive : ILiveMetrics
{
    private int _t;
    public LiveSample Sample()
    {
        _t++;
        var cpu = 18 + 9 * Math.Sin(_t / 3.0) + 5 * Math.Sin(_t / 1.3);
        return new LiveSample(Math.Round(Math.Clamp(cpu, 2, 100), 1), 16L * 1024 * 1024 * 1024, (long)((3.4 + 0.2 * Math.Sin(_t / 5.0)) * 1024 * 1024 * 1024));
    }
}

internal sealed class SimFolders : IKnownFolders
{
    public SimFolders(string root)
    {
        string D(params string[] parts)
        {
            var p = Path.Combine(new[] { root }.Concat(parts).ToArray());
            Directory.CreateDirectory(p);
            return p;
        }
        UserProfile = D("Users", "Customer");
        Desktop = D("Users", "Customer", "Desktop");
        Downloads = D("Users", "Customer", "Downloads");
        Documents = D("Users", "Customer", "Documents");
        Pictures = D("Users", "Customer", "Pictures");
        Videos = D("Users", "Customer", "Videos");
        Music = D("Users", "Customer", "Music");
        AppDataRoaming = D("Users", "Customer", "AppData", "Roaming");
        AppDataLocal = D("Users", "Customer", "AppData", "Local");
        Temp = D("Users", "Customer", "AppData", "Local", "Temp");
        WindowsDir = D("Windows");
        System32 = D("Windows", "System32");
        ProgramFiles = D("Program Files");
        ProgramFilesX86 = D("Program Files (x86)");
        ProgramData = D("ProgramData");
        StartupUser = D("Users", "Customer", "AppData", "Roaming", "Microsoft", "Windows", "Start Menu", "Programs", "Startup");
        StartupCommon = D("ProgramData", "Microsoft", "Windows", "Start Menu", "Programs", "StartUp");
    }

    public string UserProfile { get; }
    public string Desktop { get; }
    public string Downloads { get; }
    public string Documents { get; }
    public string Pictures { get; }
    public string Videos { get; }
    public string Music { get; }
    public string AppDataRoaming { get; }
    public string AppDataLocal { get; }
    public string Temp { get; }
    public string WindowsDir { get; }
    public string System32 { get; }
    public string ProgramFiles { get; }
    public string ProgramFilesX86 { get; }
    public string ProgramData { get; }
    public string StartupUser { get; }
    public string StartupCommon { get; }
}

internal sealed class SimCollector : ISnapshotCollector
{
    private readonly SimulatedPlatform _p;
    public SimCollector(SimulatedPlatform p) => _p = p;

    public async Task<SystemSnapshot> CollectAsync(CollectionOptions options, IProgress<CollectionProgress>? progress, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var stages = new[] { "System information", "Performance sample", "Processes", "Startup", "Event logs", "Storage", "Drivers", "Graphics", "Network", "Security", "Updates" };
        for (var i = 0; i < stages.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new CollectionProgress(stages[i], i * 100 / stages.Length));
            await Task.Delay(options.CpuSampleDuration > TimeSpan.Zero ? 15 : 0, ct);
        }
        var s = Scenario.BuildSnapshot(_p, options);
        s.CollectionDuration = sw.Elapsed;
        progress?.Report(new CollectionProgress("Done", 100));
        return s;
    }
}

internal sealed class SimProcesses : IProcessService
{
    private readonly SimulatedPlatform _p;
    public SimProcesses(SimulatedPlatform p) => _p = p;

    public IReadOnlyList<ProcessInfo> Enumerate()
    {
        // Advance CPU time so samplers see realistic usage.
        foreach (var proc in _p.State.Processes)
        {
            var rate = Scenario.CpuRate(proc.Name);
            proc.TotalProcessorTime = (proc.TotalProcessorTime ?? TimeSpan.Zero) + TimeSpan.FromMilliseconds(rate * 40);
        }
        return _p.State.Processes.Select(Clone).ToList();
    }

    private static ProcessInfo Clone(ProcessInfo x) => new()
    {
        Pid = x.Pid, ParentPid = x.ParentPid, Name = x.Name, Path = x.Path, SessionId = x.SessionId, Threads = x.Threads,
        Handles = x.Handles, WorkingSetBytes = x.WorkingSetBytes, PrivateBytes = x.PrivateBytes, TotalProcessorTime = x.TotalProcessorTime,
        StartTime = x.StartTime, PathAccessible = x.PathAccessible,
    };

    public FileDetails GetFileDetails(string path, bool checkSignature) =>
        _p.State.FileDetails.TryGetValue(path, out var d) ? d : new FileDetails { Signature = checkSignature ? SignatureState.Unsigned : SignatureState.NotChecked };

    public IReadOnlyDictionary<int, IReadOnlyList<string>> GetServicesByPid() =>
        _p.State.Services.Where(s => s.ProcessId > 0).GroupBy(s => s.ProcessId).ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(s => s.Name).ToList());

    public bool TryTerminate(int pid, out string error)
    {
        var proc = _p.State.Processes.FirstOrDefault(x => x.Pid == pid);
        if (proc is null) { error = "Process not found."; return false; }
        if (_p.State.ProtectedPids.Contains(pid) || Core.Safety.ProtectedComponents.IsCriticalProcess(proc.Name)) { error = "Access is denied."; return false; }
        _p.State.Processes.Remove(proc);
        error = "";
        return true;
    }
}

internal sealed class SimServices : IServiceManager
{
    private readonly SimulatedPlatform _p;
    public SimServices(SimulatedPlatform p) => _p = p;
    public IReadOnlyList<ServiceInfo> GetServices() => _p.State.Services.ToList();
    public ServiceInfo? Get(string name) => _p.State.Services.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public void SetStartMode(string name, string startMode, bool delayedAutoStart)
    {
        if (!_p.IsElevated) throw new UnauthorizedAccessException("Changing services requires administrator rights.");
        var s = Get(name) ?? throw new InvalidOperationException("Service not found.");
        s.StartMode = startMode;
        s.DelayedAutoStart = delayedAutoStart;
    }

    public void Start(string name, TimeSpan timeout)
    {
        var s = Get(name) ?? throw new InvalidOperationException("Service not found.");
        if (s.StartMode == "Disabled") throw new InvalidOperationException("The service is disabled.");
        s.State = "Running";
    }

    public void Stop(string name, TimeSpan timeout)
    {
        var s = Get(name) ?? throw new InvalidOperationException("Service not found.");
        s.State = "Stopped";
    }
}

internal sealed class SimTasks : IScheduledTaskService
{
    private readonly SimulatedPlatform _p;
    public SimTasks(SimulatedPlatform p) => _p = p;
    public IReadOnlyList<ScheduledTaskInfo> GetTasks() => _p.State.Tasks.ToList();

    public void SetEnabled(string taskPath, bool enabled)
    {
        if (!_p.IsElevated) throw new UnauthorizedAccessException("Access is denied.");
        var t = _p.State.Tasks.FirstOrDefault(x => x.Path.Equals(taskPath, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException("Task not found.");
        t.Enabled = enabled;
        t.State = enabled ? "Ready" : "Disabled";
    }
}

internal sealed class SimPower : IPowerService
{
    private readonly SimulatedPlatform _p;
    public SimPower(SimulatedPlatform p) => _p = p;

    public PowerStatus GetStatus()
    {
        var st = _p.State;
        foreach (var s in st.Schemes) s.Active = s.Guid.Equals(st.ActiveScheme, StringComparison.OrdinalIgnoreCase);
        return new PowerStatus
        {
            ActiveSchemeGuid = st.ActiveScheme,
            ActiveSchemeName = st.Schemes.First(s => s.Active).Name,
            Schemes = st.Schemes.Select(s => new PowerScheme { Guid = s.Guid, Name = s.Name, Active = s.Active }).ToList(),
            OnAcPower = true,
            BatterySaverOn = false,
            ProcessorMinStateAc = GetSettingIndex(st.ActiveScheme, PowerSettingIds.SubProcessor, PowerSettingIds.ProcThrottleMin, true),
            ProcessorMaxStateAc = GetSettingIndex(st.ActiveScheme, PowerSettingIds.SubProcessor, PowerSettingIds.ProcThrottleMax, true),
            ProcessorMaxStateDc = GetSettingIndex(st.ActiveScheme, PowerSettingIds.SubProcessor, PowerSettingIds.ProcThrottleMax, false),
            FastStartupEnabled = _p.Registry.GetValue(RegHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled")?.AsInt() != 0 && st.HibernationEnabled,
            HibernationEnabled = st.HibernationEnabled,
            ModernStandby = false,
        };
    }

    public void SetActiveScheme(string guid)
    {
        if (!_p.State.Schemes.Any(s => s.Guid.Equals(guid, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Power scheme does not exist.");
        _p.State.ActiveScheme = guid.ToLowerInvariant();
    }

    public string DuplicateScheme(string sourceGuid)
    {
        var name = sourceGuid == WellKnownPowerSchemes.HighPerformance ? "High performance" : sourceGuid == WellKnownPowerSchemes.UltimatePerformance ? "Ultimate Performance" : "Custom plan";
        var g = Guid.NewGuid().ToString();
        _p.State.Schemes.Add(new PowerScheme { Guid = g, Name = name });
        return g;
    }

    public void DeleteScheme(string guid)
    {
        if (guid.Equals(_p.State.ActiveScheme, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Cannot delete the active scheme.");
        _p.State.Schemes.RemoveAll(s => s.Guid.Equals(guid, StringComparison.OrdinalIgnoreCase));
    }

    private static string Key(string scheme, string sub, string setting, bool ac) => $"{scheme}|{sub}|{setting}|{(ac ? "ac" : "dc")}";

    public int? GetSettingIndex(string schemeGuid, string subgroupGuid, string settingGuid, bool ac)
    {
        if (_p.State.PowerSettings.TryGetValue(Key(schemeGuid, subgroupGuid, settingGuid, ac), out var v)) return v;
        return settingGuid switch
        {
            PowerSettingIds.ProcThrottleMax => 100,
            PowerSettingIds.ProcThrottleMin => schemeGuid == WellKnownPowerSchemes.HighPerformance ? 100 : 5,
            PowerSettingIds.UsbSelectiveSuspend => 1,
            _ => null,
        };
    }

    public void SetSettingIndex(string schemeGuid, string subgroupGuid, string settingGuid, bool ac, int value) =>
        _p.State.PowerSettings[Key(schemeGuid, subgroupGuid, settingGuid, ac)] = value;

    public void SetHibernation(bool enabled)
    {
        if (!_p.IsElevated) throw new UnauthorizedAccessException("powercfg /hibernate requires administrator rights.");
        _p.State.HibernationEnabled = enabled;
    }
}

internal sealed class SimDisplay : IDisplayService
{
    private readonly SimulatedPlatform _p;
    public SimDisplay(SimulatedPlatform p) => _p = p;

    public IReadOnlyList<DisplayInfo> GetDisplays() => _p.State.Displays.Select(d =>
    {
        var copy = new DisplayInfo
        {
            DeviceName = d.DeviceName, MonitorName = d.MonitorName, AdapterName = d.AdapterName, Width = d.Width, Height = d.Height,
            RefreshHz = d.RefreshHz, BitsPerPixel = d.BitsPerPixel, IsPrimary = d.IsPrimary, ScalePercent = d.ScalePercent,
            SupportedModes = d.SupportedModes.ToList(),
        };
        copy.MaxRefreshHzAtCurrentResolution = copy.SupportedModes.Where(m => m.Width == copy.Width && m.Height == copy.Height).Select(m => m.RefreshHz).DefaultIfEmpty(copy.RefreshHz).Max();
        return copy;
    }).ToList();

    public DisplayChangeResult SetMode(string deviceName, DisplayMode mode, bool testOnly)
    {
        var d = _p.State.Displays.FirstOrDefault(x => x.DeviceName == deviceName);
        if (d is null) return new DisplayChangeResult { Success = false, Message = "Display not found." };
        if (!d.SupportedModes.Any(m => m.Width == mode.Width && m.Height == mode.Height && m.RefreshHz == mode.RefreshHz))
            return new DisplayChangeResult { Success = false, Message = "DISP_CHANGE_BADMODE" };
        if (!testOnly)
        {
            d.Width = mode.Width;
            d.Height = mode.Height;
            d.RefreshHz = mode.RefreshHz;
        }
        return new DisplayChangeResult { Success = true, Message = testOnly ? "Mode test passed." : "Mode applied." };
    }
}

internal sealed class SimGpu : IGpuService
{
    private readonly SimulatedPlatform _p;
    public SimGpu(SimulatedPlatform p) => _p = p;

    public IReadOnlyList<GpuInfo> GetGpus(bool includeTelemetry) => Scenario.Gpus(includeTelemetry);

    public Task<NvidiaDriverSettingsResult> ReadNvidiaDriverSettingsAsync(CancellationToken ct) => Task.FromResult(Scenario.NvidiaSettings());

    public VendorSoftwareStatus GetVendorSoftware() => new()
    {
        NvidiaControlPanel = "Microsoft Store app NVIDIACorp.NVIDIAControlPanel",
        NvidiaApp = @"C:\Program Files\NVIDIA Corporation\NVIDIA app\CEF\NVIDIA app.exe",
        NvidiaSmiPath = @"C:\Windows\System32\nvidia-smi.exe",
    };
}

internal sealed class SimNetwork : INetworkService
{
    private readonly SimulatedPlatform _p;
    public SimNetwork(SimulatedPlatform p) => _p = p;

    public NetworkStatus GetStatus(bool runConnectivityTests)
    {
        var proxyEnabled = _p.Registry.GetValue(RegHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", "ProxyEnable")?.AsInt() == 1;
        var st = new NetworkStatus
        {
            ProxyEnabled = proxyEnabled,
            ProxyServer = _p.Registry.GetValue(RegHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", "ProxyServer")?.AsString(),
            HostsFileEntryCount = 0,
            Adapters =
            {
                new NetworkAdapterInfo { Name = "Ethernet", Description = "Realtek PCIe 2.5GbE Family Controller", Type = "Ethernet", IsUp = true, SpeedBitsPerSecond = 1_000_000_000, IPv4Addresses = { "192.168.1.42" }, Gateways = { "192.168.1.1" }, DnsServers = { "192.168.1.1" }, DhcpEnabled = true, MacAddress = "00-1A-2B-3C-4D-5E" },
                new NetworkAdapterInfo { Name = "Wi-Fi", Description = "Intel(R) Wi-Fi 6 AX200 160MHz", Type = "Wireless", IsUp = false, DhcpEnabled = true },
                new NetworkAdapterInfo { Name = "vEthernet (WSL)", Description = "Hyper-V Virtual Ethernet Adapter", Type = "Ethernet", IsUp = true, IsVirtual = true, IPv4Addresses = { "172.24.80.1" }, DhcpEnabled = false },
            },
        };
        if (runConnectivityTests)
            st.Connectivity = new ConnectivityTest { Time = DateTimeOffset.Now, GatewayReachable = true, GatewayLatencyMs = 1, DnsResolutionWorks = true, DnsLatencyMs = 14, InternetReachable = true, InternetLatencyMs = 23, Notes = "Simulated" };
        return st;
    }
}

internal sealed class SimApps : IAppInventoryService
{
    private readonly SimulatedPlatform _p;
    public SimApps(SimulatedPlatform p) => _p = p;

    public IReadOnlyList<InstalledApp> GetInstalledApps() =>
        new UninstallRegistryReader(_p.Registry).Read().Concat(_p.State.StorePackages).OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public IReadOnlyList<OptionalFeatureInfo> GetOptionalFeatures() => _p.State.Features.ToList();

    public long? ComputeFolderSize(string path, CancellationToken ct) =>
        Directory.Exists(path) ? FileCleaner.Measure(path, TimeSpan.Zero, ct).BytesFound : null;
}

internal sealed class SimShell : IShellService
{
    private readonly SimulatedPlatform _p;
    public SimShell(SimulatedPlatform p) => _p = p;

    public LaunchResult Launch(LaunchTarget target)
    {
        if (!IsAvailable(target, out var reason)) return LaunchResult.Fail(reason);
        _p.LaunchedTargets.Add(target);
        return LaunchResult.Ok($"(Demo mode) Would open {target.Target}.");
    }

    public bool IsAvailable(LaunchTarget target, out string reason)
    {
        reason = "";
        if (target.Kind == LaunchKind.Uri && !Scenario.AllowedSchemes.Any(s => target.Target.StartsWith(s, StringComparison.OrdinalIgnoreCase)))
        {
            reason = "URI scheme is not allow-listed.";
            return false;
        }
        return true;
    }
}

internal sealed class SimCommands : ICommandRunner
{
    private readonly SimulatedPlatform _p;
    public SimCommands(SimulatedPlatform p) => _p = p;

    public Task<CommandResult> RunAsync(string tool, string arguments, TimeSpan timeout, CancellationToken ct)
    {
        _p.CommandLog.Add($"{tool} {arguments}");
        var t = tool.ToLowerInvariant().Replace(".exe", "");
        var adminTools = new[] { "sfc", "dism", "chkdsk", "netsh" };
        if (adminTools.Contains(t) && !_p.IsElevated)
            return Task.FromResult(new CommandResult { ExitCode = 5, StdErr = "Access is denied. This tool requires administrator rights." });
        var output = t switch
        {
            "sfc" => "Beginning system scan.  This process will take some time.\r\n\r\nVerification 100% complete.\r\n\r\nWindows Resource Protection did not find any integrity violations.",
            "dism" when arguments.Contains("RestoreHealth", StringComparison.OrdinalIgnoreCase) => "[==========================100.0%==========================]\r\nThe restore operation completed successfully.\r\nThe operation completed successfully.",
            "dism" => "[==========================100.0%==========================]\r\nThe operation completed successfully.",
            "chkdsk" => "Stage 1: Examining basic file system structure ...\r\nWindows has scanned the file system and found no problems.\r\nNo further action is required.",
            "ipconfig" when arguments.Contains("flushdns") => "Windows IP Configuration\r\n\r\nSuccessfully flushed the DNS Resolver Cache.",
            "ipconfig" => "Windows IP Configuration\r\n\r\nEthernet adapter Ethernet:\r\n   IPv4 Address. . . . . . . . . . . : 192.168.1.42",
            "netsh" => "Sucessfully reset the Winsock Catalog.\r\nYou must restart the computer in order to complete the reset.",
            "winget" when arguments.StartsWith("upgrade --id", StringComparison.OrdinalIgnoreCase) => "Found an existing package already installed. Trying to upgrade the installed package...\r\nSuccessfully installed",
            "winget" => Scenario.WingetUpgradeOutput,
            _ => "",
        };
        return Task.FromResult(new CommandResult { ExitCode = 0, StdOut = output });
    }
}

internal sealed class SimMaintenance : IMaintenanceService
{
    private readonly SimulatedPlatform _p;
    public SimMaintenance(SimulatedPlatform p) => _p = p;

    public bool? IsSystemRestoreEnabled() => _p.State.SystemRestoreEnabled;

    public RestorePointResult CreateRestorePoint(string description)
    {
        if (!_p.IsElevated) throw new UnauthorizedAccessException("Creating a restore point requires administrator rights.");
        if (!_p.State.SystemRestoreEnabled) return new RestorePointResult { Created = false, Message = "System Protection is off." };
        _p.State.RestorePointsCreated++;
        return new RestorePointResult { Created = true, Message = $"Restore point \"{description}\" created." };
    }

    public (long Bytes, long Items)? GetRecycleBinInfo() => (_p.State.RecycleBinBytes, _p.State.RecycleBinItems);

    public bool EmptyRecycleBin(out string error)
    {
        _p.State.BytesFreedInSandbox += _p.State.RecycleBinBytes;
        _p.State.RecycleBinBytes = 0;
        _p.State.RecycleBinItems = 0;
        error = "";
        return true;
    }

    public bool SendToRecycleBin(string path, out string error)
    {
        error = "";
        if (File.Exists(path)) { var len = new FileInfo(path).Length; File.Delete(path); _p.State.RecycleBinBytes += len; _p.State.RecycleBinItems++; return true; }
        if (Directory.Exists(path)) { Directory.Delete(path, true); _p.State.RecycleBinItems++; return true; }
        error = "Not found";
        return false;
    }

    public void BroadcastSettingsChanged() => _p.State.BroadcastCount++;

    public bool RestartExplorer(out string error)
    {
        _p.State.ExplorerRestarts++;
        error = "";
        return true;
    }

    public bool SetMouseAcceleration(bool enabled, out string error) { _p.State.MouseAcceleration = enabled; error = ""; return true; }
    public bool? GetMouseAcceleration() => _p.State.MouseAcceleration;
    public bool? GetUiEffect(string name) => _p.State.UiEffects.TryGetValue(name, out var v) ? v : null;
    public bool SetUiEffect(string name, bool enabled, out string error) { _p.State.UiEffects[name] = enabled; error = ""; return true; }
}

internal sealed class SimElevation : IElevationService
{
    private readonly SimulatedPlatform _p;
    public SimElevation(SimulatedPlatform p) => _p = p;
    public bool IsElevated => _p.IsElevated;
    public bool CanElevate => true;
    public int ElevationRequests { get; private set; }

    public async Task<ElevatedResult> RunElevatedAsync(ElevatedRequest request, CancellationToken ct)
    {
        ElevationRequests++;
        if (_p.SimulateUacDecline)
        {
            _p.SimulateUacDecline = false;
            return new ElevatedResult { Launched = false, UserCancelled = true, Message = "The operation was canceled by the user." };
        }
        // Run the real elevated-helper code path in-process with elevation simulated.
        var was = _p.IsElevated;
        _p.IsElevated = true;
        try
        {
            var code = await ElevatedHelper.RunAsync(request, _p, ct);
            var json = File.Exists(request.ResultFile) ? await File.ReadAllTextAsync(request.ResultFile, ct) : null;
            return new ElevatedResult { Launched = true, ExitCode = code, ResultJson = json };
        }
        finally
        {
            _p.IsElevated = was;
        }
    }

    public bool RelaunchElevated(out string error)
    {
        _p.IsElevated = true;
        error = "";
        return true;
    }
}

internal sealed class SimProbe : ISettingProbe
{
    private readonly SimulatedPlatform _p;
    public SimProbe(SimulatedPlatform p) => _p = p;

    public SettingReadResult Read(string probeId)
    {
        var power = _p.Power.GetStatus();
        var primary = _p.Display.GetDisplays().FirstOrDefault(d => d.IsPrimary);
        return probeId switch
        {
            "power.active-scheme" => SettingReadResult.Value(power.ActiveSchemeName, power.ActiveSchemeGuid),
            "power.hibernation" => SettingReadResult.Value(power.HibernationEnabled == true ? "On" : "Off"),
            "power.fast-startup" => SettingReadResult.Value(power.FastStartupEnabled == true ? "On" : "Off"),
            "power.usb-selective-suspend" => SettingReadResult.Value(_p.Power.GetSettingIndex(power.ActiveSchemeGuid, PowerSettingIds.SubUsb, PowerSettingIds.UsbSelectiveSuspend, true) == 0 ? "Disabled" : "Enabled"),
            "power.processor-max" => SettingReadResult.Value($"{power.ProcessorMaxStateAc}%"),
            "power.processor-min" => SettingReadResult.Value($"{power.ProcessorMinStateAc}%"),
            "display.refresh" => primary is null ? SettingReadResult.Unreadable("No display") : SettingReadResult.Value($"{primary.RefreshHz} Hz (highest supported: {primary.MaxRefreshHzAtCurrentResolution} Hz)", primary.RefreshHz.ToString()),
            "display.resolution" => primary is null ? SettingReadResult.Unreadable("No display") : SettingReadResult.Value($"{primary.Width} × {primary.Height}"),
            "display.scale" => SettingReadResult.Value("100%"),
            "display.count" => SettingReadResult.Value(_p.State.Displays.Count.ToString()),
            "input.mouse-acceleration" => SettingReadResult.Value(_p.State.MouseAcceleration ? "On" : "Off"),
            "visual.animations" => SettingReadResult.Value(_p.Maintenance.GetUiEffect(UiEffects.WindowAnimation) == true ? "On" : "Off"),
            "security.defender-realtime" => SettingReadResult.Value("On"),
            "security.defender-signatures" => SettingReadResult.Value("Updated 1 day ago"),
            "security.firewall" => SettingReadResult.Value("On for Domain, Private, and Public networks"),
            "security.secure-boot" => SettingReadResult.Value("On"),
            "security.tpm" => SettingReadResult.Value("TPM 2.0 present and ready"),
            "security.bitlocker" => SettingReadResult.Value("Off (C:)"),
            "security.memory-integrity" => SettingReadResult.Value("Off"),
            "system.restore" => SettingReadResult.Value(_p.State.SystemRestoreEnabled ? "On for C:" : "Off"),
            "system.pagefile" => SettingReadResult.Value("Automatically managed (C:)"),
            "system.uptime" => SettingReadResult.Value("9 days"),
            "storage.free" => SettingReadResult.Value("31.4 GB free of 465 GB (C:)"),
            "network.adapters" => SettingReadResult.Value("Ethernet connected at 1 Gbps"),
            "network.dns" => SettingReadResult.Value("192.168.1.1 (from router)"),
            "update.last-install" => SettingReadResult.Value("20 days ago"),
            "update.reboot-pending" => SettingReadResult.Value("No"),
            "gpu.driver" => SettingReadResult.Value("NVIDIA 536.23 (June 2023)"),
            "audio.default-device" => SettingReadResult.Unreadable("Audio endpoints are not simulated."),
            "bluetooth.radio" => SettingReadResult.Value("Present, on"),
            _ => SettingReadResult.Unreadable("Not available in demo mode."),
        };
    }
}

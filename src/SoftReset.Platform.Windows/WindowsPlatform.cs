using System.Runtime.Versioning;
using SoftReset.Core.Platform;

namespace SoftReset.Platform.Windows;

internal sealed class WindowsLiveMetrics : ILiveMetrics
{
    private ulong _idle, _kernel, _user;

    public LiveSample Sample()
    {
        double cpu = 0;
        if (Native.Win32.GetSystemTimes(out var i, out var k, out var u))
        {
            var total = (k.Value - _kernel) + (u.Value - _user);
            var idle = i.Value - _idle;
            if (_kernel != 0 && total > 0) cpu = Math.Clamp(100.0 * (total - idle) / total, 0, 100);
            (_idle, _kernel, _user) = (i.Value, k.Value, u.Value);
        }
        var mem = new Native.Win32.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.Win32.MEMORYSTATUSEX>() };
        return Native.Win32.GlobalMemoryStatusEx(ref mem) ? new LiveSample(cpu, (long)mem.ullTotalPhys, (long)mem.ullAvailPhys) : new LiveSample(cpu, 0, 0);
    }
}

/// <summary>The real Windows implementation of every platform service.</summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class WindowsPlatform : ISystemPlatform
{
    public WindowsPlatform()
    {
        var registry = new WindowsRegistry();
        var commands = new WindowsCommandRunner();
        Registry = registry;
        Folders = new WindowsKnownFolders();
        Commands = commands;
        Processes = new WindowsProcessService();
        Services = new WindowsServiceManager();
        Tasks = new WindowsTaskService();
        Power = new WindowsPowerService(commands);
        Display = new WindowsDisplayService();
        Gpu = new WindowsGpuService(commands, Display);
        Network = new WindowsNetworkService(commands, registry);
        Apps = new WindowsAppInventory(registry);
        Shell = new WindowsShellService(Folders);
        Maintenance = new WindowsMaintenanceService();
        var elevation = new WindowsElevationService();
        Elevation = elevation;
        IsElevated = elevation.IsElevated;
        SettingProbe = new WindowsSettingProbe(this);
        Collector = new WindowsSnapshotCollector(this);
        OsBuild = Environment.OSVersion.Version.Build;
    }

    public string Name => "Windows";
    public bool IsWindows => true;
    public bool IsElevated { get; }
    public int OsBuild { get; }
    public IRegistryAccess Registry { get; }
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
    public ILiveMetrics Live { get; } = new WindowsLiveMetrics();

    /// <summary>Entry point for the out-of-process NVIDIA settings probe.</summary>
    public static int RunNvApiProbe(string outFile) => WindowsGpuService.RunProbe(outFile);
}

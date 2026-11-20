using SoftReset.Core.Diagnostics;
using SoftReset.Core.Diagnostics.Rules;
using SoftReset.Core.Models;

namespace SoftReset.Core.Tests;

/// <summary>Validates each diagnostic rule against hand-built snapshots (diagnostic validation tests).</summary>
public class RuleFixtureTests
{
    private static readonly DiagnosticContext Ctx = new() { Now = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero) };
    private static List<Finding> Run(IDiagnosticRule rule, SystemSnapshot s) => rule.Evaluate(s, Ctx).ToList();

    private static EventEntry Ev(string log, string provider, int id, int daysAgo = 1, string msg = "", Dictionary<string, string>? data = null) =>
        new() { Log = log, Provider = provider, Id = id, Time = Ctx.Now.AddDays(-daysAgo), Message = msg, Data = data ?? new() };

    [Theory]
    [InlineData(50, 0)]
    [InlineData(80, 1)]
    [InlineData(95, 1)]
    public void CpuSustainedHigh_Thresholds(double avg, int expected)
    {
        var s = new SystemSnapshot { Performance = { SampleSeconds = 5, CpuAveragePercent = avg, CpuPeakPercent = avg + 5 } };
        var f = Run(new CpuSustainedHighRule(), s);
        Assert.Equal(expected, f.Count);
        if (avg >= 92) Assert.Equal(Severity.High, f[0].Severity);
    }

    [Fact]
    public void CpuSustainedHigh_MentionsTransientMaintenance()
    {
        var s = new SystemSnapshot { Performance = { SampleSeconds = 5, CpuAveragePercent = 88 } };
        s.TopProcesses.Add(new ProcessSample { Name = "TiWorker.exe", CpuPercent = 40 });
        var f = Assert.Single(Run(new CpuSustainedHighRule(), s));
        Assert.Contains(f.LikelyCauses, c => c.Contains("TiWorker.exe"));
        Assert.Contains(f.Uncertainties, u => u.Contains("maintenance"));
    }

    [Theory]
    [InlineData(16.0, 7.0, false)]   // 56% used
    [InlineData(16.0, 1.5, true)]    // 90.6% used
    public void MemoryPressure(double totalGb, double availGb, bool expected)
    {
        var s = new SystemSnapshot { Performance = { MemoryTotalBytes = (long)(totalGb * 1073741824), MemoryAvailableBytes = (long)(availGb * 1073741824), CommitLimitBytes = 100, CommitTotalBytes = 10 } };
        Assert.Equal(expected, Run(new MemoryPressureRule(), s).Count == 1);
    }

    [Theory]
    [InlineData(4.0, 500.0, Severity.Critical)]
    [InlineData(40.0, 500.0, Severity.High)]   // 8% free
    [InlineData(70.0, 500.0, Severity.Medium)] // 14% free
    [InlineData(150.0, 500.0, null)]
    public void LowDiskSpace_SystemDrive(double freeGb, double totalGb, Severity? expected)
    {
        var s = new SystemSnapshot();
        s.Drives.Add(new DriveStatus { Name = @"C:\", IsSystemDrive = true, FreeBytes = (long)(freeGb * 1073741824), TotalBytes = (long)(totalGb * 1073741824) });
        var f = Run(new StorageLowSpaceRule(), s);
        if (expected is null) Assert.Empty(f);
        else Assert.Equal(expected, Assert.Single(f).Severity);
    }

    [Fact]
    public void UnexpectedShutdown_DistinguishesBugcheckFromPowerLoss()
    {
        var s = new SystemSnapshot();
        s.Reliability.Events.Add(Ev("System", "Microsoft-Windows-Kernel-Power", 41, 2, data: new() { ["BugcheckCode"] = "278" }));
        s.Reliability.Events.Add(Ev("System", "Microsoft-Windows-Kernel-Power", 41, 4, data: new() { ["BugcheckCode"] = "0" }));
        var f = Assert.Single(Run(new UnexpectedShutdownRule(), s));
        Assert.Contains(f.Evidence, e => e.Value.Contains("VIDEO_TDR_FAILURE"));
        Assert.Contains(f.LikelyCauses, c => c.Contains("Blue-screen"));
        Assert.Contains(f.LikelyCauses, c => c.Contains("power loss", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(f.Uncertainties, u => u.Contains("not why"));
    }

    [Fact]
    public void BlueScreen_ParsesStopCodesFromMessage()
    {
        var s = new SystemSnapshot();
        s.Reliability.Events.Add(Ev("System", "Microsoft-Windows-WER-SystemErrorReporting", 1001, 1, "The computer has rebooted from a bugcheck.  The bugcheck was: 0x00000133 (0x0000000000000001, 0x0000000000001e00, 0xfffff80078cfb320, 0x0000000000000000)."));
        s.Reliability.Events.Add(Ev("System", "Microsoft-Windows-WER-SystemErrorReporting", 1001, 3, "The bugcheck was: 0x00000133 (...)"));
        s.Reliability.Events.Add(Ev("System", "Microsoft-Windows-WER-SystemErrorReporting", 1001, 5, "The bugcheck was: 0x00000124 (...)"));
        var f = Assert.Single(Run(new BlueScreenRule(), s));
        Assert.Equal(Severity.High, f.Severity);
        Assert.Contains(f.Evidence, e => e.Value.Contains("DPC_WATCHDOG_VIOLATION") && e.Value.Contains("× 2"));
        Assert.Contains(f.Evidence, e => e.Value.Contains("WHEA_UNCORRECTABLE_ERROR"));
        Assert.Contains(f.Recommendations, r => r.ShortcutTarget == "mdsched.exe");
    }

    [Fact]
    public void Whea_FatalIsHigh_CorrectedFewIsLow()
    {
        var fatal = new SystemSnapshot();
        fatal.Reliability.Events.Add(Ev("System", "Microsoft-Windows-WHEA-Logger", 18, msg: "A fatal hardware error has occurred."));
        Assert.Equal(Severity.High, Run(new WheaErrorRule(), fatal).Single().Severity);
        var corrected = new SystemSnapshot();
        corrected.Reliability.Events.Add(Ev("System", "Microsoft-Windows-WHEA-Logger", 19, msg: "A corrected hardware error has occurred."));
        Assert.Equal(Severity.Low, Run(new WheaErrorRule(), corrected).Single().Severity);
    }

    [Fact]
    public void AppCrashes_GroupByAppAndFlagGraphicsDriverModule()
    {
        var s = new SystemSnapshot();
        for (var i = 0; i < 4; i++)
            s.Reliability.Events.Add(Ev("Application", "Application Error", 1000, i, data: new() { ["AppName"] = "game.exe", ["ModuleName"] = "nvwgf2umx.dll" }));
        s.Reliability.Events.Add(Ev("Application", "Application Error", 1000, 1, data: new() { ["AppName"] = "other.exe", ["ModuleName"] = "ntdll.dll" }));
        var f = Assert.Single(Run(new AppCrashRule(), s));
        Assert.Contains("game.exe", f.Title);
        Assert.Contains(f.Evidence, e => e.Value.Contains("NVIDIA Direct3D driver"));
    }

    [Fact]
    public void GpuResets_FromDisplay4101()
    {
        var s = new SystemSnapshot();
        s.Reliability.Events.Add(Ev("System", "Display", 4101, msg: "Display driver amdkmdag stopped responding and has successfully recovered."));
        var f = Assert.Single(Run(new GpuDriverResetRule(), s));
        Assert.Contains(f.Evidence, e => e.Value.Contains("amdkmdag"));
    }

    [Fact]
    public void DeviceProblems_ImportantClassIsHigh_DisabledIsInfo_NotConnectedIgnored()
    {
        var s = new SystemSnapshot();
        s.DeviceProblems.Add(new DeviceProblem { Name = "Ethernet", DeviceClass = "Net", ErrorCode = 10, PnpDeviceId = "a" });
        s.DeviceProblems.Add(new DeviceProblem { Name = "Card reader", DeviceClass = "SDHost", ErrorCode = 22, PnpDeviceId = "b" });
        s.DeviceProblems.Add(new DeviceProblem { Name = "Old USB", DeviceClass = "USB", ErrorCode = 45, PnpDeviceId = "c" });
        var f = Run(new DeviceProblemRule(), s);
        Assert.Equal(2, f.Count);
        Assert.Equal(Severity.High, f.Single(x => x.Title.StartsWith("Ethernet")).Severity);
        Assert.Equal(Severity.Info, f.Single(x => x.Title.StartsWith("Card reader")).Severity);
    }

    [Fact]
    public void BasicDisplayAdapter_IsHigh()
    {
        var s = new SystemSnapshot { Gpus = { new GpuInfo { Name = "Microsoft Basic Display Adapter", Vendor = GpuVendor.Microsoft } } };
        Assert.Equal(Severity.High, Assert.Single(Run(new GpuDriverRule(), s)).Severity);
    }

    [Fact]
    public void OldGpuDriver_Severity()
    {
        var s = new SystemSnapshot { Gpus = { new GpuInfo { Name = "RTX", Vendor = GpuVendor.Nvidia, DriverDate = Ctx.Now.AddDays(-800), PnpDeviceId = "x" } } };
        Assert.Equal(Severity.Medium, Assert.Single(Run(new GpuDriverRule(), s)).Severity);
        s.Gpus[0].DriverDate = Ctx.Now.AddDays(-100);
        Assert.Empty(Run(new GpuDriverRule(), s));
    }

    [Fact]
    public void DiskHealth_WarningIsCritical()
    {
        var s = new SystemSnapshot { PhysicalDisks = { new PhysicalDiskInfo { FriendlyName = "Disk", HealthStatus = "Warning" } } };
        var f = Assert.Single(Run(new DiskHealthRule(), s));
        Assert.Equal(Severity.Critical, f.Severity);
        Assert.Contains(f.Recommendations, r => r.Text.Contains("Back up"));
    }

    [Fact]
    public void DiskErrors_BadBlockIsHigh()
    {
        var s = new SystemSnapshot();
        s.Reliability.Events.Add(Ev("System", "disk", 7, msg: @"The device, \Device\Harddisk0\DR0, has a bad block."));
        Assert.Equal(Severity.High, Assert.Single(Run(new DiskErrorEventsRule(), s)).Severity);
    }

    [Fact]
    public void Network_ApipaAndProxyAndSlowLink()
    {
        var s = new SystemSnapshot();
        s.Network.Adapters.Add(new NetworkAdapterInfo { Name = "Ethernet", Type = "Ethernet", IsUp = true, DhcpEnabled = true, IPv4Addresses = { "169.254.10.2" }, SpeedBitsPerSecond = 100_000_000, Gateways = { "192.168.1.1" } });
        s.Network.ProxyEnabled = true;
        s.Network.ProxyServer = "127.0.0.1:8888";
        var f = Run(new NetworkConfigRule(), s);
        Assert.Contains(f, x => x.Key.StartsWith("network.apipa"));
        Assert.Contains(f, x => x.Key == "network.proxy" && x.Confidence == Confidence.Low);
        Assert.Contains(f, x => x.Key.StartsWith("network.slow-link"));
    }

    [Fact]
    public void Network_NoConnection()
    {
        var s = new SystemSnapshot();
        s.Network.Adapters.Add(new NetworkAdapterInfo { Name = "Wi-Fi", Type = "Wireless", IsUp = false });
        Assert.Contains(Run(new NetworkConfigRule(), s), x => x.Key == "network.no-connection");
    }

    [Fact]
    public void Network_RouteThroughVirtualAdapter_IsNotReportedAsDisconnected()
    {
        // Found on a real Windows run: a VM's NIC (and a Hyper-V external switch on a physical PC) is
        // a "virtual" adapter that carries the default gateway.
        var s = new SystemSnapshot();
        s.Network.Adapters.Add(new NetworkAdapterInfo { Name = "Ethernet", Description = "Realtek PCIe GbE", Type = "Ethernet", IsUp = false });
        s.Network.Adapters.Add(new NetworkAdapterInfo { Name = "vEthernet (External)", Description = "Hyper-V Virtual Ethernet Adapter", IsVirtual = true, IsUp = true, Gateways = { "192.168.1.1" } });
        Assert.DoesNotContain(Run(new NetworkConfigRule(), s), x => x.Key == "network.no-connection");
    }

    [Fact]
    public void RefreshRate_RecommendsActionWithParameters()
    {
        var s = new SystemSnapshot { Displays = { new DisplayInfo { DeviceName = @"\\.\DISPLAY1", MonitorName = "Mon", Width = 1920, Height = 1080, RefreshHz = 60, MaxRefreshHzAtCurrentResolution = 144 } } };
        var f = Assert.Single(Run(new RefreshRateRule(), s));
        var rec = f.Recommendations.First(r => r.ActionId == ActionIds.RefreshRate);
        Assert.Equal("144", rec.ActionParameters!["refresh"]);
        Assert.Equal(Severity.High, f.Severity);
    }

    [Fact]
    public void DisplayOnIgpu_OnlyForDesktops()
    {
        SystemSnapshot S(bool laptop) => new()
        {
            Hardware = { IsLaptop = laptop },
            Gpus = { new GpuInfo { Name = "NVIDIA GeForce RTX 4070", Vendor = GpuVendor.Nvidia }, new GpuInfo { Name = "Intel(R) UHD Graphics 770", Vendor = GpuVendor.Intel, IsIntegrated = true } },
            Displays = { new DisplayInfo { DeviceName = "D1", AdapterName = "Intel(R) UHD Graphics 770" } },
        };
        Assert.Single(Run(new DisplayOnIntegratedGpuRule(), S(false)));
        Assert.Empty(Run(new DisplayOnIntegratedGpuRule(), S(true)));
    }

    [Fact]
    public void PowerSaverOnAc_AndCpuCap()
    {
        var s = new SystemSnapshot { Power = { ActiveSchemeGuid = WellKnownPowerSchemes.PowerSaver, ActiveSchemeName = "Power saver", OnAcPower = true, ProcessorMaxStateAc = 70 } };
        var f = Run(new PowerPlanRule(), s);
        Assert.Contains(f, x => x.Key == "power.saver-plan" && x.Severity == Severity.High);
        Assert.Contains(f, x => x.Key == "power.cpu-capped");
    }

    [Fact]
    public void Security_NoAntivirusIsCritical_AndNeverAutoChanged()
    {
        var s = new SystemSnapshot { Security = { DefenderAntivirusEnabled = false } };
        var f = Assert.Single(Run(new SecurityPostureRule(), s));
        Assert.Equal(Severity.Critical, f.Severity);
        Assert.All(f.Recommendations, r => Assert.Null(r.ActionId)); // security is only ever opened for the technician
    }

    [Fact]
    public void Security_ThirdPartyAvSuppressesDefenderFinding()
    {
        var s = new SystemSnapshot { Security = { DefenderAntivirusEnabled = false, DefenderRealTimeEnabled = false, ThirdPartyAntivirus = { "Bitdefender" } } };
        Assert.Empty(Run(new SecurityPostureRule(), s));
    }

    [Fact]
    public void UpdateFailures_DescribeHresult()
    {
        var s = new SystemSnapshot();
        s.Updates.RecentHistory.Add(new UpdateHistoryItem { Title = "KB1", Date = Ctx.Now.AddDays(-2), Result = "Failed", HResult = "0x80073712" });
        s.Updates.RecentHistory.Add(new UpdateHistoryItem { Title = "KB1", Date = Ctx.Now.AddDays(-4), Result = "Failed", HResult = "0x80073712" });
        var f = Assert.Single(Run(new UpdateFailuresRule(), s));
        Assert.Equal(Severity.High, f.Severity);
        Assert.Contains(f.Evidence, e => e.Value.Contains("COMPONENT_STORE_CORRUPT"));
    }

    [Fact]
    public void UpdateServiceDisabled_IsHigh()
    {
        var s = new SystemSnapshot { Updates = { WindowsUpdateServiceStartMode = "Disabled" } };
        var f = Run(new UpdatesStaleRule(), s);
        Assert.Contains(f, x => x.Key == "updates.service-disabled" && x.Severity == Severity.High);
    }

    [Fact]
    public void SlowBoot_UsesMedian()
    {
        var s = new SystemSnapshot();
        foreach (var ms in new[] { 30_000, 130_000, 125_000 })
            s.Reliability.BootRecords.Add(new BootPerformance { Time = Ctx.Now.AddDays(-ms / 10000), BootTimeMs = ms });
        Assert.Equal(Severity.High, Assert.Single(Run(new SlowBootRule(), s)).Severity);
    }

    [Fact]
    public void GpuThermal_ThrottleReasonEscalates()
    {
        var s = new SystemSnapshot { Gpus = { new GpuInfo { Name = "GPU", PnpDeviceId = "p", Telemetry = new GpuTelemetry { Source = "NVML", TemperatureC = 84, ThrottleReasons = { "Thermal slowdown (hardware)" } } } } };
        Assert.Equal(Severity.High, Assert.Single(Run(new GpuThermalRule(), s)).Severity);
    }

    [Fact]
    public void Throttling_ExplainsMissingCpuSensor()
    {
        var s = new SystemSnapshot { Thermal = { CpuSensorReliable = false, Notes = "n/a" } };
        for (var i = 0; i < 5; i++) s.Reliability.Events.Add(Ev("System", "Microsoft-Windows-Kernel-Processor-Power", 37, i));
        var f = Assert.Single(Run(new ThrottlingRule(), s));
        Assert.Contains(f.Uncertainties, u => u.Contains("CPU temperature"));
    }

    [Fact]
    public void ServiceFailures_GroupedByService()
    {
        var s = new SystemSnapshot();
        for (var i = 0; i < 4; i++) s.Reliability.Events.Add(Ev("System", "Service Control Manager", 7031, i, data: new() { ["param1"] = "Foo Service" }));
        var f = Assert.Single(Run(new ServiceFailureRule(), s));
        Assert.Contains(f.Evidence, e => e.Value.StartsWith("Foo Service"));
    }

    [Fact]
    public void BrokenStartupEntries()
    {
        var s = new SystemSnapshot { StartupEntries = { new StartupEntry { Name = "Gone", Enabled = true, FileExists = false, ExecutablePath = @"C:\gone.exe" } } };
        Assert.Single(Run(new StartupBrokenEntriesRule(), s));
    }

    [Fact]
    public void SingleChannelMemory_LowConfidence()
    {
        var s = new SystemSnapshot { Hardware = { MemoryModules = { new MemoryModule { Slot = "A1", CapacityBytes = 16L << 30, SpeedMHz = 3200, ConfiguredSpeedMHz = 3200 } } } };
        var f = Assert.Single(Run(new MemoryConfigurationRule(), s));
        Assert.Equal(Confidence.Low, f.Confidence);
    }
}

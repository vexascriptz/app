using System.Diagnostics;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using SoftReset.Core.Apps;
using SoftReset.Core.Collection;
using SoftReset.Core.Knowledge;
using SoftReset.Core.Models;
using SoftReset.Core.Optimization.Actions;
using SoftReset.Core.Parsing;
using SoftReset.Core.Platform;
using SoftReset.Core.Storage;
using SoftReset.Platform.Windows.Native;

namespace SoftReset.Platform.Windows;

internal sealed partial class WindowsGpuService : IGpuService
{
    private readonly WindowsCommandRunner _commands;
    private readonly IDisplayService _display;
    public WindowsGpuService(WindowsCommandRunner commands, IDisplayService display) { _commands = commands; _display = display; }

    public IReadOnlyList<GpuInfo> GetGpus(bool includeTelemetry)
    {
        var list = new List<GpuInfo>();
        var memory = AdapterMemoryFromRegistry();
        List<string> attached;
        try { attached = _display.GetDisplays().Select(d => d.AdapterName).ToList(); } catch { attached = new(); }
        foreach (var o in Wmi.Query("SELECT Name, AdapterCompatibility, DriverVersion, DriverDate, PNPDeviceID, ConfigManagerErrorCode, AdapterRAM FROM Win32_VideoController"))
        {
            var name = o.S("Name") ?? "Unknown GPU";
            var pnp = o.S("PNPDeviceID") ?? "";
            var g = new GpuInfo
            {
                Name = name,
                PnpDeviceId = pnp,
                DriverVersion = o.S("DriverVersion") ?? "",
                DriverDate = o.D("DriverDate"),
                ConfigManagerErrorCode = o.I("ConfigManagerErrorCode"),
                Vendor = VendorOf(pnp, name),
                DedicatedMemoryBytes = memory.TryGetValue(name, out var mem) ? mem : (o.L("AdapterRAM") is > 0 and var ram ? ram : null),
                HasAttachedDisplay = attached.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase)),
            };
            g.IsIntegrated = IsIntegrated(g);
            if (g.Vendor == GpuVendor.Nvidia) g.VendorDriverVersion = NvidiaVersion(g.DriverVersion);
            list.Add(g);
        }
        if (includeTelemetry) AddTelemetry(list);
        return list;
    }

    internal static GpuVendor VendorOf(string pnp, string name)
    {
        if (pnp.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase)) return GpuVendor.Nvidia;
        if (pnp.Contains("VEN_1002", StringComparison.OrdinalIgnoreCase) || pnp.Contains("VEN_1022", StringComparison.OrdinalIgnoreCase)) return GpuVendor.Amd;
        if (pnp.Contains("VEN_8086", StringComparison.OrdinalIgnoreCase)) return GpuVendor.Intel;
        if (pnp.Contains("VEN_1414", StringComparison.OrdinalIgnoreCase) || name.Contains("Microsoft", StringComparison.OrdinalIgnoreCase)) return GpuVendor.Microsoft;
        return GpuVendor.Other;
    }

    [GeneratedRegex(@"Arc(\(TM\))?\s+[AB]\d{3}", RegexOptions.IgnoreCase)]
    private static partial Regex IntelDiscrete();

    internal static bool IsIntegrated(GpuInfo g) => g.Vendor switch
    {
        GpuVendor.Intel => !IntelDiscrete().IsMatch(g.Name),
        GpuVendor.Amd => !g.Name.Contains(" RX ", StringComparison.OrdinalIgnoreCase) && !g.Name.Contains("Radeon Pro", StringComparison.OrdinalIgnoreCase)
                         && !g.Name.Contains("FirePro", StringComparison.OrdinalIgnoreCase) && (g.DedicatedMemoryBytes is null or < 3L * 1024 * 1024 * 1024),
        _ => false,
    };

    /// <summary>NVIDIA's version number is the last five digits of the Windows driver version: 31.0.15.3623 → 536.23.</summary>
    internal static string? NvidiaVersion(string windowsVersion)
    {
        var digits = new string(windowsVersion.Split('.').TakeLast(2).SelectMany(p => p).Where(char.IsDigit).ToArray());
        if (digits.Length < 5) return null;
        var last5 = digits[^5..];
        return $"{int.Parse(last5[..3])}.{last5[3..]}";
    }

    private static Dictionary<string, long> AdapterMemoryFromRegistry()
    {
        var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var reg = new WindowsRegistry();
            const string cls = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
            foreach (var sub in reg.GetSubKeyNames(RegHive.LocalMachine, cls).Where(s => s.Length == 4 && s.All(char.IsDigit)))
            {
                var desc = reg.GetValue(RegHive.LocalMachine, $@"{cls}\{sub}", "DriverDesc")?.AsString();
                var mem = reg.GetValue(RegHive.LocalMachine, $@"{cls}\{sub}", "HardwareInformation.qwMemorySize");
                if (desc is null || mem is null) continue;
                long bytes = mem.Data switch { long l => l, int i => (uint)i, byte[] b when b.Length >= 8 => BitConverter.ToInt64(b), byte[] b when b.Length >= 4 => BitConverter.ToUInt32(b), _ => 0 };
                if (bytes > 0) map[desc] = bytes;
            }
        }
        catch { }
        return map;
    }

    private void AddTelemetry(List<GpuInfo> gpus)
    {
        var nvidia = gpus.Where(g => g.Vendor == GpuVendor.Nvidia).ToList();
        if (nvidia.Count > 0)
        {
            List<(string Name, string Driver, GpuTelemetry Telemetry)> readings;
            try { readings = Nvml.Read(out _); } catch { readings = new(); }
            if (readings.Count == 0)
            {
                var r = _commands.RunAsync("nvidia-smi.exe", ToolOutputParsers.NvidiaSmiQuery, TimeSpan.FromSeconds(10), CancellationToken.None).GetAwaiter().GetResult();
                if (r.Success) readings = ToolOutputParsers.ParseNvidiaSmi(r.StdOut);
            }
            for (var i = 0; i < nvidia.Count; i++)
            {
                var match = readings.FirstOrDefault(x => x.Name.Equals(nvidia[i].Name, StringComparison.OrdinalIgnoreCase));
                if (match.Telemetry is null && i < readings.Count) match = readings[i];
                if (match.Telemetry is not null)
                {
                    nvidia[i].Telemetry = match.Telemetry;
                    if (!string.IsNullOrEmpty(match.Driver)) nvidia[i].VendorDriverVersion = match.Driver;
                }
            }
        }
        var others = gpus.Where(g => g.Vendor is GpuVendor.Amd or GpuVendor.Intel).ToList();
        if (others.Count > 0)
        {
            try
            {
                var perf = D3dkmt.Read().Where(p => p.TemperatureC is not null).ToList();
                if (perf.Count == others.Count)
                    for (var i = 0; i < others.Count; i++)
                        others[i].Telemetry = new GpuTelemetry { Source = "WDDM adapter performance data", TemperatureC = perf[i].TemperatureC, FanRpm = perf[i].FanRpm };
            }
            catch { }
        }
    }

    public async Task<NvidiaDriverSettingsResult> ReadNvidiaDriverSettingsAsync(CancellationToken ct)
    {
        var ids = KnowledgeBase.Default.GpuSettingsFor("NVIDIA").Where(g => g.DrsId is not null).Select(g => (g.DrsId!.Value, g.Name)).ToList();
        var exe = Environment.ProcessPath;
        if (exe is not null && Path.GetFileName(exe).Equals("SoftResetConsole.exe", StringComparison.OrdinalIgnoreCase))
        {
            // Out-of-process probe: a driver fault cannot crash the console.
            var outFile = Path.Combine(Path.GetTempPath(), $"softreset-nvapi-{Guid.NewGuid():N}.json");
            try
            {
                using var p = Process.Start(new ProcessStartInfo(exe, $"--probe nvapi-drs \"{outFile}\"") { UseShellExecute = false, CreateNoWindow = true })!;
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(30));
                try { await p.WaitForExitAsync(cts.Token); }
                catch (OperationCanceledException) { try { p.Kill(true); } catch { } return new NvidiaDriverSettingsResult { Status = "The NVIDIA driver did not respond in time." }; }
                if (p.ExitCode != 0 || !File.Exists(outFile))
                    return new NvidiaDriverSettingsResult { Status = $"The NVIDIA settings probe failed (exit code {p.ExitCode}). Driver settings can still be viewed in NVIDIA Control Panel." };
                return Json.ReadFile<NvidiaDriverSettingsResult>(outFile) ?? new NvidiaDriverSettingsResult { Status = "No data." };
            }
            finally { try { File.Delete(outFile); } catch { } }
        }
        return await Task.Run(() =>
        {
            try { return NvApiDrs.Read(ids); }
            catch (DllNotFoundException) { return new NvidiaDriverSettingsResult { Status = "NVAPI is not available (no NVIDIA driver installed)." }; }
            catch (Exception ex) { return new NvidiaDriverSettingsResult { Status = $"Could not read NVIDIA settings: {ex.Message}" }; }
        }, ct);
    }

    /// <summary>Entry point for the probe process (SoftResetConsole.exe --probe nvapi-drs file).</summary>
    public static int RunProbe(string outFile)
    {
        try
        {
            var ids = KnowledgeBase.Default.GpuSettingsFor("NVIDIA").Where(g => g.DrsId is not null).Select(g => (g.DrsId!.Value, g.Name)).ToList();
            var r = NvApiDrs.Read(ids);
            Json.WriteFile(outFile, r);
            return 0;
        }
        catch (DllNotFoundException)
        {
            Json.WriteFile(outFile, new NvidiaDriverSettingsResult { Status = "NVAPI is not available (no NVIDIA driver installed)." });
            return 0;
        }
        catch (Exception ex)
        {
            try { Json.WriteFile(outFile, new NvidiaDriverSettingsResult { Status = $"Could not read NVIDIA settings: {ex.Message}" }); } catch { }
            return 3;
        }
    }

    public VendorSoftwareStatus GetVendorSoftware()
    {
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string? F(params string[] parts) { var p = Path.Combine(parts); return File.Exists(p) ? p : null; }
        var status = new VendorSoftwareStatus
        {
            NvidiaApp = F(pf, "NVIDIA Corporation", "NVIDIA app", "CEF", "NVIDIA app.exe"),
            GeForceExperience = F(pf, "NVIDIA Corporation", "NVIDIA GeForce Experience", "NVIDIA GeForce Experience.exe"),
            AmdSoftware = F(pf, "AMD", "CNext", "CNext", "RadeonSoftware.exe"),
            IntelGraphicsSoftware = F(pf, "Intel", "Intel Graphics Software", "IntelGraphicsSoftware.exe"),
            NvidiaSmiPath = F(Environment.SystemDirectory, "nvidia-smi.exe"),
        };
        try
        {
            var pm = new global::Windows.Management.Deployment.PackageManager();
            foreach (var pkg in pm.FindPackagesForUser(string.Empty))
            {
                var fam = pkg.Id.FamilyName;
                if (fam.StartsWith("NVIDIACorp.NVIDIAControlPanel", StringComparison.OrdinalIgnoreCase)) status.NvidiaControlPanel = AumidOf(pkg) ?? fam;
                else if (fam.StartsWith("AppUp.IntelGraphicsExperience", StringComparison.OrdinalIgnoreCase) || fam.StartsWith("AppUp.IntelArcSoftware", StringComparison.OrdinalIgnoreCase))
                    status.IntelGraphicsSoftware ??= AumidOf(pkg) ?? fam;
            }
        }
        catch { }
        if (status.NvidiaControlPanel is null && File.Exists(Path.Combine(pf, "NVIDIA Corporation", "Control Panel Client", "nvcplui.exe")))
            status.NvidiaControlPanel = Path.Combine(pf, "NVIDIA Corporation", "Control Panel Client", "nvcplui.exe");
        return status;
    }

    internal static string? AumidOf(global::Windows.ApplicationModel.Package pkg)
    {
        try { return pkg.GetAppListEntries().FirstOrDefault()?.AppUserModelId; }
        catch { return null; }
    }
}

internal sealed class WindowsNetworkService : INetworkService
{
    private readonly WindowsCommandRunner _commands;
    private readonly IRegistryAccess _reg;
    public WindowsNetworkService(WindowsCommandRunner commands, IRegistryAccess reg) { _commands = commands; _reg = reg; }

    private static readonly string[] VirtualMarkers = { "Hyper-V", "VMware", "VirtualBox", "TAP-", "WireGuard", "Loopback", "vEthernet", "Npcap", "ZeroTier", "Tailscale", "Wintun", "OpenVPN", "Kernel Debug", "WAN Miniport", "Teredo", "isatap" };

    public NetworkStatus GetStatus(bool runConnectivityTests)
    {
        var st = new NetworkStatus();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            var a = new NetworkAdapterInfo
            {
                Name = ni.Name,
                Description = ni.Description,
                Type = ni.NetworkInterfaceType switch
                {
                    NetworkInterfaceType.Wireless80211 => "Wireless",
                    NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.Ethernet3Megabit => "Ethernet",
                    _ => ni.NetworkInterfaceType.ToString(),
                },
                IsUp = ni.OperationalStatus == OperationalStatus.Up,
                IsVirtual = VirtualMarkers.Any(m => ni.Description.Contains(m, StringComparison.OrdinalIgnoreCase) || ni.Name.Contains(m, StringComparison.OrdinalIgnoreCase)),
                MacAddress = string.Join("-", ni.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2"))),
            };
            try { a.SpeedBitsPerSecond = ni.Speed > 0 ? ni.Speed : 0; } catch { }
            try
            {
                var ip = ni.GetIPProperties();
                a.IPv4Addresses = ip.UnicastAddresses.Where(u => u.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).Select(u => u.Address.ToString()).ToList();
                a.Gateways = ip.GatewayAddresses.Select(g => g.Address).Where(g => g.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !g.Equals(System.Net.IPAddress.Any)).Select(g => g.ToString()).ToList();
                a.DnsServers = ip.DnsAddresses.Select(d => d.ToString()).ToList();
                try { a.DhcpEnabled = ip.GetIPv4Properties()?.IsDhcpEnabled ?? false; } catch { }
            }
            catch { }
            st.Adapters.Add(a);
        }
        var proxy = RegistryReaders.ReadProxy(_reg);
        st.ProxyEnabled = proxy.Enabled;
        st.ProxyServer = proxy.Server;
        st.AutoConfigUrl = proxy.AutoConfig;
        try
        {
            var hosts = Path.Combine(Environment.SystemDirectory, "drivers", "etc", "hosts");
            if (File.Exists(hosts))
            {
                var (count, notable) = RegistryReaders.AnalyzeHosts(File.ReadLines(hosts).Take(20_000));
                st.HostsFileEntryCount = count;
                st.HostsFileNotableEntries = notable;
            }
        }
        catch { }
        if (st.Adapters.Any(x => x.Type == "Wireless"))
        {
            var r = _commands.RunAsync("netsh.exe", "wlan show interfaces", TimeSpan.FromSeconds(10), CancellationToken.None).GetAwaiter().GetResult();
            if (r.Started) st.Wifi = ToolOutputParsers.ParseNetshWlanInterfaces(r.StdOut + r.StdErr, out _);
        }
        if (runConnectivityTests) st.Connectivity = TestConnectivity(st);
        return st;
    }

    private static ConnectivityTest TestConnectivity(NetworkStatus st)
    {
        var t = new ConnectivityTest { Time = DateTimeOffset.Now };
        var gw = st.Adapters.Where(a => a.IsUp && !a.IsVirtual).SelectMany(a => a.Gateways).FirstOrDefault()
                 ?? st.Adapters.Where(a => a.IsUp).SelectMany(a => a.Gateways).FirstOrDefault();
        if (gw is not null)
        {
            try
            {
                using var ping = new Ping();
                var reply = ping.Send(gw, 1500);
                t.GatewayReachable = reply.Status == IPStatus.Success;
                if (t.GatewayReachable == true) t.GatewayLatencyMs = reply.RoundtripTime;
            }
            catch { t.GatewayReachable = false; }
        }
        var sw = Stopwatch.StartNew();
        try
        {
            var task = System.Net.Dns.GetHostAddressesAsync("www.msftconnecttest.com");
            t.DnsResolutionWorks = task.Wait(TimeSpan.FromSeconds(5)) && task.Result.Length > 0;
            if (t.DnsResolutionWorks == true) t.DnsLatencyMs = sw.ElapsedMilliseconds;
        }
        catch { t.DnsResolutionWorks = false; }
        if (t.DnsResolutionWorks == true)
        {
            try
            {
                // Microsoft's documented Network Connectivity Status Indicator endpoint.
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
                sw.Restart();
                var body = http.GetStringAsync("http://www.msftconnecttest.com/connecttest.txt").GetAwaiter().GetResult();
                t.InternetReachable = body.Contains("Microsoft Connect Test", StringComparison.Ordinal);
                if (t.InternetReachable == true) t.InternetLatencyMs = sw.ElapsedMilliseconds;
                else t.Notes = "Unexpected response — a captive portal or filtering proxy may be intercepting traffic.";
            }
            catch { t.InternetReachable = false; }
        }
        return t;
    }
}

internal sealed class WindowsAppInventory : IAppInventoryService
{
    private readonly IRegistryAccess _reg;
    public WindowsAppInventory(IRegistryAccess reg) => _reg = reg;

    public IReadOnlyList<InstalledApp> GetInstalledApps()
    {
        var apps = new UninstallRegistryReader(_reg).Read();
        try
        {
            var pm = new global::Windows.Management.Deployment.PackageManager();
            foreach (var pkg in pm.FindPackagesForUser(string.Empty))
            {
                try
                {
                    if (pkg.IsFramework || pkg.IsResourcePackage) continue;
                    var name = pkg.DisplayName;
                    if (string.IsNullOrWhiteSpace(name) || name.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)) continue;
                    string? location = null;
                    try { location = pkg.InstalledLocation?.Path; } catch { }
                    var v = pkg.Id.Version;
                    apps.Add(new InstalledApp
                    {
                        Id = "store:" + pkg.Id.FamilyName,
                        Name = name,
                        Publisher = pkg.PublisherDisplayName,
                        Version = $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}",
                        Kind = AppKind.Store,
                        PackageFamilyName = pkg.Id.FamilyName,
                        PackageFullName = pkg.Id.FullName,
                        IsInboxWindowsApp = pkg.SignatureKind == global::Windows.ApplicationModel.PackageSignatureKind.System,
                        InstallDate = pkg.InstalledDate.Year > 2000 ? pkg.InstalledDate : null,
                        InstallLocation = location,
                        AppUserModelId = WindowsGpuService.AumidOf(pkg),
                    });
                }
                catch { /* skip broken package registrations */ }
            }
        }
        catch { /* WinRT unavailable */ }
        return apps.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IReadOnlyList<OptionalFeatureInfo> GetOptionalFeatures() =>
        Wmi.Query("SELECT Name, Caption, InstallState FROM Win32_OptionalFeature")
            .Select(o => new OptionalFeatureInfo
            {
                Name = o.S("Name") ?? "",
                Caption = o.S("Caption") ?? "",
                State = o.I("InstallState") switch { 1 => "Enabled", 2 => "Disabled", 3 => "Absent", _ => "Unknown" },
            })
            .OrderBy(f => f.Caption).ToList();

    public long? ComputeFolderSize(string path, CancellationToken ct) =>
        Directory.Exists(path) ? FileCleaner.Measure(path, TimeSpan.Zero, ct).BytesFound : null;
}

internal sealed class WindowsSettingProbe : ISettingProbe
{
    private readonly WindowsPlatform _p;
    public WindowsSettingProbe(WindowsPlatform p) => _p = p;

    public SettingReadResult Read(string probeId)
    {
        try { return ReadInner(probeId); }
        catch (UnauthorizedAccessException) { return SettingReadResult.Unreadable("Requires administrator rights to read."); }
        catch (Exception ex) { return SettingReadResult.Unreadable(ex.Message); }
    }

    private SettingReadResult ReadInner(string id)
    {
        PowerStatus Pw() => _p.Power.GetStatus();
        DisplayInfo? Primary() => _p.Display.GetDisplays().FirstOrDefault(d => d.IsPrimary) ?? _p.Display.GetDisplays().FirstOrDefault();
        var reg = _p.Registry;
        switch (id)
        {
            case "power.active-scheme": { var s = Pw(); return SettingReadResult.Value(s.ActiveSchemeName, s.ActiveSchemeGuid); }
            case "power.hibernation": return SettingReadResult.Value(Pw().HibernationEnabled switch { true => "On", false => "Off", _ => "Unknown" });
            case "power.fast-startup": return SettingReadResult.Value(Pw().FastStartupEnabled switch { true => "On", false => "Off", _ => "Unknown" });
            case "power.usb-selective-suspend":
            {
                var s = Pw();
                var v = _p.Power.GetSettingIndex(s.ActiveSchemeGuid, PowerSettingIds.SubUsb, PowerSettingIds.UsbSelectiveSuspend, true);
                return v is null ? SettingReadResult.Unreadable("Not present in the active plan.") : SettingReadResult.Value(v == 0 ? "Disabled" : "Enabled");
            }
            case "power.processor-max": return SettingReadResult.Value($"{Pw().ProcessorMaxStateAc}% (plugged in)");
            case "power.processor-min": return SettingReadResult.Value($"{Pw().ProcessorMinStateAc}% (plugged in)");
            case "power.sleep-ac":
            {
                var s = Pw();
                var v = _p.Power.GetSettingIndex(s.ActiveSchemeGuid, PowerSettingIds.SubSleep, PowerSettingIds.StandbyIdle, true);
                return v is null ? SettingReadResult.Unreadable("Not available.") : SettingReadResult.Value(v == 0 ? "Never (plugged in)" : $"After {v / 60} minutes (plugged in)");
            }
            case "display.refresh":
            {
                var d = Primary();
                return d is null ? SettingReadResult.Unreadable("No display found.") : SettingReadResult.Value($"{d.RefreshHz} Hz (highest supported at this resolution: {d.MaxRefreshHzAtCurrentResolution} Hz)", d.RefreshHz.ToString());
            }
            case "display.resolution": { var d = Primary(); return d is null ? SettingReadResult.Unreadable("No display found.") : SettingReadResult.Value($"{d.Width} × {d.Height}"); }
            case "display.scale": { var d = Primary(); return d?.ScalePercent is { } sc ? SettingReadResult.Value($"{sc}%") : SettingReadResult.Unreadable("Could not read DPI."); }
            case "display.count": return SettingReadResult.Value(_p.Display.GetDisplays().Count.ToString());
            case "input.mouse-acceleration": return SettingReadResult.Value(_p.Maintenance.GetMouseAcceleration() switch { true => "On", false => "Off", _ => "Unknown" });
            case "visual.animations": return SettingReadResult.Value(_p.Maintenance.GetUiEffect(UiEffects.WindowAnimation) switch { true => "On", false => "Off", _ => "Unknown" });
            case "security.defender-realtime":
            {
                var o = Wmi.Query(@"root\Microsoft\Windows\Defender", "SELECT RealTimeProtectionEnabled, AntivirusEnabled FROM MSFT_MpComputerStatus", out var err, 15).FirstOrDefault();
                return o is null ? SettingReadResult.Unreadable(err ?? "Defender status unavailable (another antivirus may be active).") : SettingReadResult.Value(o.B("RealTimeProtectionEnabled") == true ? "On" : "Off");
            }
            case "security.defender-signatures":
            {
                var o = Wmi.Query(@"root\Microsoft\Windows\Defender", "SELECT AntivirusSignatureAge FROM MSFT_MpComputerStatus", out var err, 15).FirstOrDefault();
                return o?.I("AntivirusSignatureAge") is { } age ? SettingReadResult.Value(age == 0 ? "Updated today" : $"{age} day(s) old") : SettingReadResult.Unreadable(err ?? "Unavailable.");
            }
            case "security.firewall":
            {
                var p = WindowsSnapshotCollector.FirewallProfiles(reg);
                return p.Count == 0 ? SettingReadResult.Unreadable("Unavailable.") : SettingReadResult.Value(string.Join(", ", p.Select(x => $"{x.Key}: {(x.Value ? "On" : "Off")}")));
            }
            case "security.secure-boot":
            {
                var v = reg.GetValue(RegHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled")?.AsInt();
                return SettingReadResult.Value(v switch { 1 => "On", 0 => "Off (supported by firmware)", _ => "Not supported (legacy BIOS boot)" });
            }
            case "security.tpm":
            {
                var o = Wmi.Query(@"root\cimv2\Security\MicrosoftTpm", "SELECT IsEnabled_InitialValue, IsActivated_InitialValue, SpecVersion FROM Win32_Tpm", out var err, 10).FirstOrDefault();
                if (o is null) return SettingReadResult.Unreadable(err ?? "No TPM found (or disabled in firmware).");
                var spec = (o.S("SpecVersion") ?? "").Split(',').FirstOrDefault()?.Trim();
                return SettingReadResult.Value($"TPM {spec} — {(o.B("IsEnabled_InitialValue") == true ? "enabled" : "disabled")}, {(o.B("IsActivated_InitialValue") == true ? "activated" : "not activated")}");
            }
            case "security.bitlocker":
            {
                var drive = Path.GetPathRoot(Environment.SystemDirectory)!.TrimEnd('\\');
                var o = Wmi.Query(@"root\cimv2\Security\MicrosoftVolumeEncryption", $"SELECT ProtectionStatus FROM Win32_EncryptableVolume WHERE DriveLetter = '{drive}'", out var err, 10).FirstOrDefault();
                return o is null ? SettingReadResult.Unreadable(err ?? "Unavailable.") : SettingReadResult.Value(o.I("ProtectionStatus") switch { 1 => $"On ({drive})", 0 => $"Off ({drive})", _ => "Unknown" });
            }
            case "security.memory-integrity":
            {
                var v = reg.GetValue(RegHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled")?.AsInt();
                return SettingReadResult.Value(v == 1 ? "On" : "Off");
            }
            case "system.restore": return SettingReadResult.Value(_p.Maintenance.IsSystemRestoreEnabled() switch { true => "On", false => "Off", _ => "Unknown" });
            case "system.pagefile":
            {
                var cs = Wmi.Query("SELECT AutomaticManagedPagefile FROM Win32_ComputerSystem").FirstOrDefault();
                var usage = Wmi.Query("SELECT Name, AllocatedBaseSize FROM Win32_PageFileUsage").Select(o => $"{o.S("Name")} ({o.I("AllocatedBaseSize")} MB)").ToList();
                var auto = cs?.B("AutomaticManagedPagefile") == true ? "System managed" : "Custom";
                return SettingReadResult.Value(usage.Count == 0 ? $"{auto}, no page file present" : $"{auto}: {string.Join(", ", usage)}");
            }
            case "system.uptime": return SettingReadResult.Value(ToolOutputParsers.FormatDuration(TimeSpan.FromMilliseconds(Environment.TickCount64)));
            case "system.computer-name":
            {
                var cs = Wmi.Query("SELECT Domain, PartOfDomain FROM Win32_ComputerSystem").FirstOrDefault();
                var where = cs?.B("PartOfDomain") == true ? $"domain {cs.S("Domain")}" : $"workgroup {cs?.S("Domain")}";
                return SettingReadResult.Value($"{Environment.MachineName} ({where})");
            }
            case "system.edition":
            {
                var os = Wmi.Query("SELECT Caption FROM Win32_OperatingSystem").FirstOrDefault();
                var lic = Wmi.Query(@"root\cimv2", "SELECT LicenseStatus FROM SoftwareLicensingProduct WHERE PartialProductKey IS NOT NULL AND ApplicationID='55c92734-d682-4d71-983e-d6ec3f16059f'", out _, 30).FirstOrDefault();
                var status = lic?.I("LicenseStatus") switch { 1 => "activated", 0 => "not activated", 5 => "notification mode (not activated)", null => "activation unknown", _ => "grace period" };
                return SettingReadResult.Value($"{os?.S("Caption")?.Replace("Microsoft ", "")} — {status}");
            }
            case "storage.free":
            {
                var d = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
                return SettingReadResult.Value($"{ToolOutputParsers.FormatBytes(d.AvailableFreeSpace)} free of {ToolOutputParsers.FormatBytes(d.TotalSize)} ({d.Name})");
            }
            case "network.adapters":
            {
                var n = _p.Network.GetStatus(false);
                var up = n.Adapters.Where(a => a.IsUp && !a.IsVirtual).Select(a => $"{a.Name}: {(a.SpeedBitsPerSecond > 0 ? $"{a.SpeedBitsPerSecond / 1_000_000} Mbps" : "connected")}, {string.Join(", ", a.IPv4Addresses)}").ToList();
                return SettingReadResult.Value(up.Count == 0 ? "No connected adapters" : string.Join("; ", up));
            }
            case "network.dns":
            {
                var n = _p.Network.GetStatus(false);
                var dns = n.Adapters.Where(a => a.IsUp && !a.IsVirtual).SelectMany(a => a.DnsServers).Distinct().ToList();
                return SettingReadResult.Value(dns.Count == 0 ? "None" : string.Join(", ", dns));
            }
            case "network.wifi":
            {
                var w = _p.Network.GetStatus(false).Wifi;
                return w is null ? SettingReadResult.Unreadable("Not connected to Wi-Fi, or Location permission is required to read Wi-Fi details.") : SettingReadResult.Value($"{w.Ssid}: {w.SignalPercent}% signal, {w.Band}, {w.RadioType}");
            }
            case "update.last-install":
            {
                var last = WindowsSnapshotCollector.ReadUpdateHistory().LastSuccessfulInstall;
                return last is null ? SettingReadResult.Unreadable("Update history unavailable.") : SettingReadResult.Value($"{last.Value.LocalDateTime:d} ({(int)(DateTimeOffset.Now - last.Value).TotalDays} days ago)");
            }
            case "update.reboot-pending":
            {
                var r = RegistryReaders.ReadRebootPending(reg);
                return SettingReadResult.Value(r.Pending ? "Yes — " + string.Join("; ", r.Reasons) : "No");
            }
            case "gpu.driver":
            {
                var gpus = _p.Gpu.GetGpus(false);
                return SettingReadResult.Value(string.Join("; ", gpus.Select(g => $"{g.Name}: {g.VendorDriverVersion ?? g.DriverVersion} ({g.DriverDate?.ToString("yyyy-MM-dd") ?? "date unknown"})")));
            }
            case "audio.devices":
            {
                var devs = Wmi.Query("SELECT Name, Status FROM Win32_SoundDevice").Select(o => $"{o.S("Name")} ({o.S("Status")})").ToList();
                return SettingReadResult.Value(devs.Count == 0 ? "No sound devices" : string.Join("; ", devs));
            }
            case "bluetooth.radio":
            {
                var bt = Wmi.Query("SELECT Name, Status, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE PNPClass = 'Bluetooth'").ToList();
                var radio = bt.FirstOrDefault(o => (o.S("Name") ?? "").Contains("Bluetooth", StringComparison.OrdinalIgnoreCase) && !(o.S("Name") ?? "").Contains("Enumerator", StringComparison.OrdinalIgnoreCase));
                return SettingReadResult.Value(bt.Count == 0 ? "No Bluetooth adapter" : radio is null ? "Bluetooth devices present" : $"{radio.S("Name")} — {(radio.I("ConfigManagerErrorCode") == 0 ? "working" : $"problem code {radio.I("ConfigManagerErrorCode")}")}");
            }
            case "printers.list":
            {
                var printers = Wmi.Query("SELECT Name, Default, WorkOffline FROM Win32_Printer").Select(o => $"{o.S("Name")}{(o.B("Default") == true ? " (default)" : "")}{(o.B("WorkOffline") == true ? " [offline]" : "")}").ToList();
                return SettingReadResult.Value(printers.Count == 0 ? "No printers" : string.Join("; ", printers));
            }
            case "accounts.current":
            {
                using var idn = System.Security.Principal.WindowsIdentity.GetCurrent();
                return SettingReadResult.Value($"{idn.Name} — console running {(_p.IsElevated ? "as administrator" : "without administrator rights")}");
            }
            default:
                return SettingReadResult.Unreadable("No reader for this value.");
        }
    }
}

using SoftReset.Core.Models;

namespace SoftReset.Core.Diagnostics.Rules;

internal sealed class NetworkConfigRule : Rule
{
    public override string Id => "network.configuration";
    public override string Name => "Network configuration";
    public override FindingCategory Category => FindingCategory.Network;

    public override IEnumerable<Finding> Evaluate(SystemSnapshot s, DiagnosticContext ctx)
    {
        var physical = s.Network.Adapters.Where(a => !a.IsVirtual).ToList();
        var up = physical.Where(a => a.IsUp).ToList();
        if (s.Network.Adapters.Count == 0) yield break;

        if (up.Count == 0 || !up.Any(a => a.Gateways.Count > 0))
        {
            var f = New("No active network connection with a gateway", Severity.High, Confidence.Medium,
                    "No physical adapter is connected with a default gateway, so the PC cannot reach the internet.", key: "network.no-connection");
            foreach (var a in physical) f.Ev("Adapter", $"{a.Description}: {(a.IsUp ? "up" : "down")}, gateway {(a.Gateways.Count > 0 ? string.Join(", ", a.Gateways) : "none")}", "NetworkInterface");
            f.Cause("Cable unplugged, Wi-Fi disconnected, or the router is not handing out an address.")
             .Rec("Check the physical connection / Wi-Fi network, then renew the IP address.", ActionIds.RenewDhcp)
             .Shortcut("Open network status.", "ms-settings:network-status", "Open Network settings")
             .Why("Read from the adapter configuration at scan time.");
            yield return f;
        }

        foreach (var a in up.Where(a => a.DhcpEnabled && a.IPv4Addresses.Any(ip => ip.StartsWith("169.254.", StringComparison.Ordinal))))
        {
            yield return New($"{a.Name} did not get an address from the router", Severity.High, Confidence.High,
                    "The adapter has a 169.254.x.x (APIPA) address, which Windows assigns itself when DHCP fails. The PC will have no internet on this adapter.", key: $"network.apipa:{a.Name}")
                .Ev("Address", string.Join(", ", a.IPv4Addresses), "NetworkInterface")
                .Ev("DHCP", "Enabled", "NetworkInterface")
                .Cause("The router's DHCP server is not responding.", "Faulty cable/Wi-Fi association.", "A third-party firewall blocking DHCP.")
                .Rec("Restart the router, then renew the DHCP lease.", ActionIds.RenewDhcp)
                .Rec("If it persists, reset the network stack (requires restart).", ActionIds.ResetNetworkStack)
                .Why("169.254.0.0/16 addresses are only self-assigned when DHCP fails.");
        }

        foreach (var a in up.Where(a => a.Type == "Ethernet" && a.SpeedBitsPerSecond > 0 && a.SpeedBitsPerSecond <= 100_000_000))
        {
            yield return New($"{a.Name} is connected at only {a.SpeedBitsPerSecond / 1_000_000} Mbps", Severity.Low, Confidence.Medium,
                    "Gigabit Ethernet ports normally link at 1000 Mbps. A 100 Mbps link usually means a damaged cable, a cheap two-pair cable, or an old switch.", key: $"network.slow-link:{a.Name}")
                .Ev("Link speed", $"{a.SpeedBitsPerSecond / 1_000_000} Mbps", "NetworkInterface.Speed")
                .Ev("Adapter", a.Description, "NetworkInterface")
                .Cause("Damaged or low-quality cable.", "Old switch/router port.", "Power-saving link speed negotiation.")
                .Rec("Try a known-good Cat5e/Cat6 cable and a different router port.")
                .Why("Negotiated link speed is reported by the adapter.")
                .Unsure("Some adapters and routers are 100 Mbps by design.");
        }

        if (s.Network.ProxyEnabled && !string.IsNullOrEmpty(s.Network.ProxyServer))
        {
            yield return New("A manual web proxy is configured", Severity.Medium, Confidence.Low,
                    "Traffic is routed through a proxy server. On home PCs this is unusual — it is often left behind by VPNs, ad-blocking or 'privacy' tools, or adware, and causes 'no internet' in browsers.", key: "network.proxy")
                .Ev("Proxy server", s.Network.ProxyServer!, @"HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings")
                .Ev("Auto-config script", s.Network.AutoConfigUrl ?? "None", "Internet Settings")
                .Cause("A VPN, filtering tool, or unwanted software set a proxy.", "It may be required by the customer's employer/school.")
                .Rec("Confirm with the customer. If it is not required, turn the proxy off (backed up and reversible).", ActionIds.DisableProxy)
                .Shortcut("Open proxy settings.", "ms-settings:network-proxy", "Open Proxy settings")
                .Why("The setting is read directly; whether it is wanted can only be confirmed with the customer.")
                .Unsure("Workplace or school PCs legitimately use proxies.");
        }

        if (s.Network.HostsFileNotableEntries.Count > 0)
        {
            var f = New("The hosts file redirects well-known websites", Severity.Medium, Confidence.Low,
                "The hosts file overrides DNS. Entries for Microsoft, Google, or security vendors can block updates or redirect traffic. Ad-blocking lists also use the hosts file legitimately.", key: "network.hosts");
            foreach (var e in s.Network.HostsFileNotableEntries.Take(10)) f.Ev("Entry", e, @"C:\Windows\System32\drivers\etc\hosts");
            f.Ev("Total active entries", s.Network.HostsFileEntryCount.ToString(), "hosts file")
             .Cause("An ad-blocking/privacy hosts list.", "Software activation cracks blocking vendor servers.", "Malware blocking security sites.")
             .Rec("Review the entries with the customer; entries blocking Windows Update or security vendors should be removed manually in an elevated text editor after confirming.")
             .Why("The file content is read directly; intent cannot be determined automatically.")
             .Unsure("Many entries are harmless ad-blocking rules.");
            yield return f;
        }

        var dnsTimeouts = s.Reliability.Find("DNS-Client", 1014).ToList();
        if (dnsTimeouts.Count >= 20)
        {
            yield return New($"DNS lookups timed out {dnsTimeouts.Count} times", Severity.Medium, Confidence.Medium,
                    "Name resolution timeouts make websites slow to start loading or fail intermittently.", key: "network.dns-timeouts")
                .Ev("DNS Client 1014 events", dnsTimeouts.Count.ToString(), "System event log")
                .Ev("DNS servers", string.Join(", ", up.SelectMany(a => a.DnsServers).Distinct()), "NetworkInterface")
                .Cause("The router's DNS forwarder or the ISP's DNS servers are unreliable.", "Wi-Fi drops.")
                .Rec("Flush the DNS cache.", ActionIds.FlushDns)
                .Rec("Discuss using a reliable public DNS service on the router or adapter.")
                .Why("Logged by the Windows DNS client.");
        }

        if (s.Network.Wifi is { SignalPercent: { } sig } wifi && sig < 45 && up.Any(a => a.Type == "Wireless"))
        {
            yield return New($"Weak Wi-Fi signal ({sig}%)", sig < 30 ? Severity.Medium : Severity.Low, Confidence.Medium,
                    "A weak signal causes low speeds, high latency, and disconnects — especially noticeable in games and video calls.", key: "network.wifi-weak")
                .Ev("Signal", $"{sig}%", "netsh wlan show interfaces")
                .Ev("Network", $"{wifi.Ssid} ({wifi.RadioType}, {wifi.Band}, channel {wifi.Channel})", "netsh wlan")
                .Ev("Link rate", wifi.ReceiveRateMbps is { } rx ? $"{rx:0} Mbps receive / {wifi.TransmitRateMbps:0} Mbps transmit" : "Unknown", "netsh wlan")
                .Cause("Distance or walls between PC and router.", "Interference on 2.4 GHz.")
                .Rec("Move closer, use 5/6 GHz, add a mesh node, or use Ethernet for gaming PCs.")
                .Why("Signal quality reported by the Wi-Fi driver at scan time.");
        }

        if (s.Network.Connectivity is { } c)
        {
            if (c.GatewayReachable == false || c.DnsResolutionWorks == false || c.InternetReachable == false)
            {
                var f = New("Connectivity test failed", Severity.High, Confidence.High,
                        "The step-by-step connectivity test identified where the connection breaks.", key: "network.connectivity")
                    .Ev("Router (gateway) reachable", Yn(c.GatewayReachable) + (c.GatewayLatencyMs is { } g ? $" ({g} ms)" : ""), "ICMP ping")
                    .Ev("DNS resolution", Yn(c.DnsResolutionWorks) + (c.DnsLatencyMs is { } d ? $" ({d} ms)" : ""), "DNS query (www.msftconnecttest.com)")
                    .Ev("Internet reachable", Yn(c.InternetReachable) + (c.InternetLatencyMs is { } i ? $" ({i} ms)" : ""), "HTTP connectivity check");
                if (c.GatewayReachable == false) f.Cause("The PC cannot reach the router: cable/Wi-Fi/adapter problem.").Rec("Check the local connection and renew DHCP.", ActionIds.RenewDhcp);
                else if (c.DnsResolutionWorks == false) f.Cause("The router is reachable but DNS fails.").Rec("Flush DNS and check DNS server settings.", ActionIds.FlushDns);
                else f.Cause("Local network works but the internet is unreachable: ISP outage, router WAN problem, proxy or firewall.").Rec("Check the router's internet status and any proxy/VPN software.");
                f.Why("Measured live during the scan.");
                yield return f;
            }
        }
    }

    private static string Yn(bool? b) => b switch { true => "Yes", false => "No", _ => "Not tested" };
}

internal sealed class RefreshRateRule : Rule
{
    public override string Id => "gaming.refresh-rate";
    public override string Name => "Display running below its maximum refresh rate";
    public override FindingCategory Category => FindingCategory.Display;

    public override IEnumerable<Finding> Evaluate(SystemSnapshot s, DiagnosticContext ctx)
    {
        foreach (var d in s.Displays.Where(d => d.MaxRefreshHzAtCurrentResolution > d.RefreshHz + 5 && d.RefreshHz > 0))
        {
            var sev = d.MaxRefreshHzAtCurrentResolution >= 100 && d.RefreshHz <= 75 ? Severity.High : Severity.Medium;
            yield return New($"{(string.IsNullOrEmpty(d.MonitorName) ? d.DeviceName : d.MonitorName)} runs at {d.RefreshHz} Hz but supports {d.MaxRefreshHzAtCurrentResolution} Hz", sev, Confidence.High,
                    "The monitor is capable of a higher refresh rate at its current resolution. Windows often defaults to 60 Hz, so customers who bought a high-refresh monitor never get its benefit — motion looks less smooth and games feel less responsive.",
                    key: $"{Id}:{d.DeviceName}")
                .Ev("Current mode", $"{d.Width}x{d.Height} @ {d.RefreshHz} Hz", "EnumDisplaySettings (current)")
                .Ev("Highest refresh at this resolution", $"{d.MaxRefreshHzAtCurrentResolution} Hz", "EnumDisplaySettings (supported modes)")
                .Ev("Connected to", d.AdapterName, "EnumDisplayDevices")
                .Cause("Windows selected a conservative default refresh rate.", "The cable or port limits bandwidth at higher rates (if the higher mode is missing).")
                .Rec($"Switch to {d.MaxRefreshHzAtCurrentResolution} Hz (tested first, backed up, and reversible).", ActionIds.RefreshRate,
                    new() { ["device"] = d.DeviceName, ["refresh"] = d.MaxRefreshHzAtCurrentResolution.ToString() })
                .Shortcut("Open advanced display settings.", "ms-settings:display-advanced", "Open Advanced display")
                .Why("Supported modes are reported by the display driver for this exact monitor.")
                .Unsure("If the screen flickers or goes blank at the higher rate, the cable may not support it; the change reverts automatically if it fails the driver test.");
        }
    }
}

internal sealed class DisplayOnIntegratedGpuRule : Rule
{
    public override string Id => "gaming.display-on-igpu";
    public override string Name => "Monitor connected to the motherboard instead of the graphics card";
    public override FindingCategory Category => FindingCategory.Gaming;

    public override IEnumerable<Finding> Evaluate(SystemSnapshot s, DiagnosticContext ctx)
    {
        if (s.Hardware.IsLaptop) yield break; // laptops route through the iGPU by design (hybrid graphics)
        var discrete = s.Gpus.Where(g => !g.IsIntegrated && g.Vendor is GpuVendor.Nvidia or GpuVendor.Amd or GpuVendor.Intel).ToList();
        var integrated = s.Gpus.Where(g => g.IsIntegrated).ToList();
        if (discrete.Count == 0 || integrated.Count == 0 || s.Displays.Count == 0) yield break;
        var onDiscrete = s.Displays.Any(d => discrete.Any(g => d.AdapterName.Contains(g.Name, StringComparison.OrdinalIgnoreCase) || g.Name.Contains(d.AdapterName, StringComparison.OrdinalIgnoreCase)));
        var onIntegrated = s.Displays.Where(d => integrated.Any(g => d.AdapterName.Contains(g.Name, StringComparison.OrdinalIgnoreCase) || g.Name.Contains(d.AdapterName, StringComparison.OrdinalIgnoreCase))).ToList();
        if (onDiscrete || onIntegrated.Count == 0) yield break;

        yield return New("The monitor is plugged into the motherboard, not the graphics card", Severity.High, Confidence.Medium,
                $"This desktop has a dedicated {discrete[0].Name}, but every display is driven by the integrated graphics. Games then render on the much slower integrated GPU or pay a copy penalty.")
            .Ev("Dedicated GPU", string.Join(", ", discrete.Select(g => g.Name)), "WMI Win32_VideoController")
            .Ev("Displays attached to", string.Join(", ", onIntegrated.Select(d => $"{d.DeviceName} → {d.AdapterName}")), "EnumDisplayDevices")
            .Cause("The display cable is connected to the motherboard's video output.")
            .Rec("Move the display cable to an output on the graphics card (the horizontal ports lower on the back of the case).")
            .Why("Display-to-adapter mapping is reported by Windows; some desktops intentionally use iGPU passthrough.")
            .Unsure("A few setups deliberately route through the integrated GPU (e.g. for Quick Sync), but it is almost always unintentional for gaming PCs.");
    }
}

internal sealed class GamingConfigRule : Rule
{
    public override string Id => "gaming.config";
    public override string Name => "Gaming-related Windows settings";
    public override FindingCategory Category => FindingCategory.Gaming;

    public override IEnumerable<Finding> Evaluate(SystemSnapshot s, DiagnosticContext ctx)
    {
        var g = s.Gaming;
        var hasDedicated = s.Gpus.Any(x => !x.IsIntegrated && x.Vendor is GpuVendor.Nvidia or GpuVendor.Amd);
        if (g.GameDvrBackgroundRecording == true)
        {
            yield return New("Background game recording is on", Severity.Low, Confidence.Medium,
                    "Xbox Game Bar continuously records the last few minutes of gameplay. This uses GPU encoder time, memory, and disk writes; the cost is small on modern GPUs but measurable on low-end systems.", key: "gaming.background-recording")
                .Ev("Record what happened (background recording)", "On", @"HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR\HistoricalCaptureEnabled")
                .Cause("Enabled by the customer or a Game Bar default.")
                .Rec("Turn background recording off unless the customer uses it to capture clips.", ActionIds.BackgroundRecording)
                .Why("Read from the Game DVR settings.");
        }
        if (g.GameModeEnabled == false && hasDedicated)
        {
            yield return New("Game Mode is turned off", Severity.Low, Confidence.Medium,
                    "Game Mode prioritises the active game and prevents Windows Update from installing drivers or showing restart notifications during play. It is on by default.", key: "gaming.game-mode")
                .Ev("Game Mode", "Off", @"HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled")
                .Cause("Turned off manually or by a tweaking tool.")
                .Rec("Turn Game Mode back on (Windows default).", ActionIds.GameMode)
                .Why("Read from the Game Bar settings.")
                .Unsure("Measured FPS gains from Game Mode are usually small; its main benefit is fewer interruptions.");
        }
        var overlays = g.OverlayProcessesRunning.Distinct().ToList();
        if (overlays.Count >= 3)
        {
            var f = New($"{overlays.Count} overlay or capture tools are running", Severity.Low, Confidence.Low,
                "Overlays inject into games to draw on top of them. Several at once can cause stutter, crashes, or anti-cheat conflicts.", key: "gaming.overlays");
            foreach (var o in overlays) f.Ev("Running", o, "Process list");
            f.Cause("Multiple launchers and utilities each enable their own overlay.")
             .Rec("Keep the overlay the customer actually uses and disable the others in each app's settings.")
             .Why("Based on running processes; whether each overlay is enabled in-game is configured inside each app.")
             .Unsure("A running process does not prove its overlay is active.");
            yield return f;
        }
    }
}

internal sealed class GpuThermalRule : Rule
{
    public override string Id => "thermal.gpu";
    public override string Name => "GPU temperature and throttling";
    public override FindingCategory Category => FindingCategory.Thermal;

    public override IEnumerable<Finding> Evaluate(SystemSnapshot s, DiagnosticContext ctx)
    {
        foreach (var g in s.Gpus.Where(g => g.Telemetry is not null))
        {
            var t = g.Telemetry!;
            var thermalThrottle = t.ThrottleReasons.Any(r => r.Contains("Thermal", StringComparison.OrdinalIgnoreCase));
            if (t.TemperatureC is not { } temp || (temp < 83 && !thermalThrottle)) continue;
            var sev = temp >= 90 || thermalThrottle ? Severity.High : Severity.Medium;
            var f = New($"{g.Name} is running hot ({temp:0} °C)", sev, Confidence.High,
                    "High GPU temperatures reduce boost clocks (lower FPS), increase fan noise, and in extreme cases trigger driver resets or shutdowns.", key: $"{Id}:{g.PnpDeviceId}")
                .Ev("Temperature", $"{temp:0} °C", t.Source)
                .Ev("Utilization at time of reading", t.UtilizationPercent is { } u ? $"{u:0}%" : "Unknown", t.Source);
            if (t.FanPercent is { } fan) f.Ev("Fan speed", $"{fan}%", t.Source);
            foreach (var r in t.ThrottleReasons) f.Ev("Clock limit reason", r, t.Source);
            f.Cause("Dust buildup in the GPU heatsink or case filters.", "Poor case airflow.", "Dried thermal paste on older cards.")
             .Rec("Clean dust from the GPU and case, and check that case fans are spinning.")
             .Rec("Consider a frame-rate cap so the GPU does not run at 100% unnecessarily.")
             .Why($"Read from the GPU's own sensor via {t.Source}.")
             .Unsure(t.UtilizationPercent is < 30 ? "The GPU was not under heavy load during the reading; temperatures under gaming load will be higher." : "A single reading; temperatures vary with workload.");
            yield return f;
        }
    }
}

internal sealed class MemoryConfigurationRule : Rule
{
    public override string Id => "gaming.memory-config";
    public override string Name => "Memory configuration";
    public override FindingCategory Category => FindingCategory.Gaming;

    public override IEnumerable<Finding> Evaluate(SystemSnapshot s, DiagnosticContext ctx)
    {
        var mods = s.Hardware.MemoryModules;
        if (mods.Count == 0 || s.Hardware.IsLaptop) yield break;
        if (mods.Count == 1)
        {
            yield return New("Only one memory module is installed", Severity.Low, Confidence.Low,
                    "A single module normally runs in single-channel mode, halving memory bandwidth. This noticeably lowers performance in CPU-bound games and with integrated graphics.", key: "gaming.single-channel")
                .Ev("Modules", $"{mods[0].Slot}: {Bytes(mods[0].CapacityBytes)} @ {mods[0].ConfiguredSpeedMHz} MHz", "WMI Win32_PhysicalMemory")
                .Cause("The PC was built or upgraded with one stick.")
                .Rec("Recommend a matched pair of modules for dual-channel operation.")
                .Why("Module count is reported by firmware; channel mode itself is not exposed through a documented Windows API.")
                .Unsure("Some platforms (and all soldered-memory designs) can run dual-channel with one physical module; confirm with CPU-Z or the BIOS.");
        }
        var rated = mods.Where(m => m.SpeedMHz > 0 && m.ConfiguredSpeedMHz > 0).ToList();
        if (rated.Count > 0 && rated.All(m => m.ConfiguredSpeedMHz < m.SpeedMHz * 0.85))
        {
            yield return New("Memory is running below its rated speed", Severity.Info, Confidence.Low,
                    "The modules report a higher supported speed than they are currently running at. Often XMP/EXPO is not enabled in the BIOS.", key: "gaming.memory-speed")
                .Ev("Running at", $"{rated[0].ConfiguredSpeedMHz} MHz", "WMI Win32_PhysicalMemory.ConfiguredClockSpeed")
                .Ev("Module reports", $"{rated[0].SpeedMHz} MHz", "WMI Win32_PhysicalMemory.Speed")
                .Cause("XMP/EXPO profile not enabled.")
                .Rec("If stable, enable XMP/EXPO in the BIOS (this is a firmware change the console does not make).")
                .Why("Speed values come from SPD data, which may describe JEDEC rather than XMP speeds.")
                .Unsure("Many systems report the same value for both fields; enabling XMP can cause instability on some boards.");
        }
    }
}

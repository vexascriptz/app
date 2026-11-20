using SoftReset.Core.Models;

namespace SoftReset.Core.Diagnostics.Rules;

internal sealed class PowerPlanRule : Rule
{
    public override string Id => "power.plan";
    public override string Name => "Power configuration";
    public override FindingCategory Category => FindingCategory.Power;

    public override IEnumerable<Finding> Evaluate(SystemSnapshot s, DiagnosticContext ctx)
    {
        var p = s.Power;
        var onAc = p.OnAcPower ?? !s.Hardware.IsLaptop;
        if (p.ActiveSchemeGuid.Equals(WellKnownPowerSchemes.PowerSaver, StringComparison.OrdinalIgnoreCase) && onAc)
        {
            yield return New("The Power saver plan is active while plugged in", s.Hardware.IsLaptop ? Severity.Medium : Severity.High, Confidence.High,
                    "Power saver limits CPU speed and dims the display to save energy. On a desktop or a plugged-in laptop it makes everything slower.", key: "power.saver-plan")
                .Ev("Active plan", $"{p.ActiveSchemeName} ({p.ActiveSchemeGuid})", "PowerGetActiveScheme")
                .Ev("Power source", onAc ? "AC (mains)" : "Battery", "GetSystemPowerStatus")
                .Cause("Selected manually or by a battery utility.")
                .Rec("Switch to the Balanced plan (Windows default; backed up and reversible).", ActionIds.PowerPlan, new() { ["scheme"] = WellKnownPowerSchemes.Balanced })
                .Why("Read from the active power scheme.");
        }
        if (p.ProcessorMaxStateAc is { } max && max < 100 && onAc)
        {
            yield return New($"CPU is capped at {max}% of its maximum speed", Severity.Medium, Confidence.High,
                    "The active power plan limits the maximum processor state. This is sometimes set to reduce heat or noise and then forgotten.", key: "power.cpu-capped")
                .Ev("Maximum processor state (plugged in)", $"{max}%", "Power plan setting PROCTHROTTLEMAX")
                .Cause("A tweak to reduce temperatures/fan noise, or a vendor utility.")
                .Rec("Restore the maximum processor state to 100% (backed up and reversible).", ActionIds.ProcessorState)
                .Why("Read from the active power plan.")
                .Unsure("If it was lowered deliberately to control overheating, fix the cooling first.");
        }
        if (s.Hardware.Battery is { WearPercent: { } wear } bat && wear >= 35)
        {
            yield return New($"Battery has lost about {wear:0}% of its original capacity", wear >= 50 ? Severity.Medium : Severity.Low, Confidence.Medium,
                    "Lithium-ion batteries wear with age and charge cycles. Reduced capacity shortens battery life and can cause sudden shutdowns at reported charge levels above zero.", key: "power.battery-wear")
                .Ev("Design capacity", $"{bat.DesignCapacityMWh:N0} mWh", "WMI BatteryStaticData")
                .Ev("Full-charge capacity", $"{bat.FullChargeCapacityMWh:N0} mWh", "WMI BatteryFullChargedCapacity")
                .Ev("Cycle count", bat.CycleCount?.ToString() ?? "Not reported", "WMI BatteryCycleCount")
                .Cause("Normal battery aging.")
                .Rec("Recommend a battery replacement if battery life matters to the customer.")
                .Shortcut("Generate a detailed battery report.", "powercfg.exe /batteryreport", "Battery report")
                .Why("Reported by the battery's controller, which can be imprecise.");
        }
    }
}

internal sealed class ThrottlingRule : Rule
{
    public override string Id => "thermal.cpu-throttling";
    public override string Name => "CPU speed limited by firmware";
    public override FindingCategory Category => FindingCategory.Thermal;

    public override IEnumerable<Finding> Evaluate(SystemSnapshot s, DiagnosticContext ctx)
    {
        var ev = s.Reliability.Find("Kernel-Processor-Power", 37).ToList();
        var lowFreq = s.Performance.CpuFrequencyPercentOfMax is { } fq && fq < 60 && s.Performance.CpuAveragePercent > 50;
        var hot = s.Thermal.Readings.Where(r => r.Reliable && r.Celsius >= 95).ToList();
        if (ev.Count < 3 && !lowFreq && hot.Count == 0) yield break;
        var sev = hot.Count > 0 || ev.Count >= 20 ? Severity.High : Severity.Medium;
        var f = New("The processor is being slowed down by the firmware", sev, ev.Count >= 3 ? Confidence.Medium : Confidence.Low,
            "Windows reports that system firmware limited the CPU's speed. This happens when the CPU is too hot, or when power delivery is limited (weak charger, power-saving firmware settings).");
        if (ev.Count > 0) f.Ev("Kernel-Processor-Power 37", $"{ev.Count} event(s), last {When(ev.Max(e => e.Time))}", "System event log");
        if (s.Performance.CpuFrequencyPercentOfMax is { } pct) f.Ev("CPU frequency during sample", $"{pct:0}% of maximum", "Processor performance counter");
        foreach (var r in hot) f.Ev("Temperature", $"{r.Sensor}: {r.Celsius:0} °C", r.Source);
        f.Cause("Dust-clogged heatsink or fans, dried thermal paste.", s.Hardware.IsLaptop ? "Low-wattage or non-original charger; laptop used on soft surfaces." : "Inadequate CPU cooler or case airflow.", "Firmware power limits set by the manufacturer.")
         .Rec("Clean the cooling system and check fans; on laptops verify the original charger is used.")
         .Rec("Update the BIOS/UEFI from the manufacturer.")
         .Why("Event 37 is written by Windows when firmware reports a performance limit.")
         .Unsure("Some laptops log event 37 routinely on battery or in quiet modes; it matters most if performance drops while plugged in.");
        if (!s.Thermal.CpuSensorReliable)
            f.Unsure("Windows has no reliable, documented CPU temperature interface on most consumer PCs, so CPU temperature itself was not measured. " + s.Thermal.Notes);
        yield return f;
    }
}

internal sealed class SecurityPostureRule : Rule
{
    public override string Id => "security.posture";
    public override string Name => "Security protections";
    public override FindingCategory Category => FindingCategory.Security;

    public override IEnumerable<Finding> Evaluate(SystemSnapshot s, DiagnosticContext ctx)
    {
        var sec = s.Security;
        var thirdParty = sec.ThirdPartyAntivirus.Count > 0;

        if (sec.DefenderAntivirusEnabled == false && !thirdParty)
        {
            yield return New("No active antivirus protection detected", Severity.Critical, Confidence.Medium,
                    "Microsoft Defender Antivirus is off and no other antivirus is registered with Windows Security Center.", key: "security.no-antivirus")
                .Ev("Microsoft Defender", "Disabled", "WMI MSFT_MpComputerStatus")
                .Ev("Other antivirus registered", "None", "WMI SecurityCenter2.AntiVirusProduct")
                .Cause("A third-party antivirus was uninstalled incompletely, a tweak tool disabled Defender, or malware disabled protection.")
                .Shortcut("Open Windows Security to turn protection back on.", "windowsdefender:", "Open Windows Security")
                .Why("Read from Windows Security Center and Defender status.")
                .Unsure("The console never changes security settings automatically — protection must be restored by the technician in Windows Security.");
        }
        else if (sec.DefenderRealTimeEnabled == false && !thirdParty)
        {
            yield return New("Real-time protection is off", Severity.High, Confidence.High,
                    "Defender is installed but not scanning files as they are opened.", key: "security.realtime-off")
                .Ev("Real-time protection", "Off", "WMI MSFT_MpComputerStatus.RealTimeProtectionEnabled")
                .Cause("Turned off manually (it turns back on automatically after a while) or by policy/tweak tool.")
                .Shortcut("Turn real-time protection on in Windows Security.", "windowsdefender://threat", "Open Virus & threat protection")
                .Why("Read from Defender status.");
        }
        if (sec.DefenderSignatureAgeDays is >= 7 && sec.DefenderAntivirusEnabled == true)
        {
            yield return New($"Antivirus definitions are {sec.DefenderSignatureAgeDays} days old", Severity.Medium, Confidence.High,
                    "Defender updates definitions daily through Windows Update. Old definitions miss new threats.", key: "security.signatures")
                .Ev("Definition age", $"{sec.DefenderSignatureAgeDays} days", "WMI MSFT_MpComputerStatus.AntivirusSignatureAge")
                .Cause("Windows Update not working or the PC rarely online.")
                .Shortcut("Update definitions in Windows Security.", "windowsdefender://threat", "Open Virus & threat protection")
                .Why("Read from Defender status.");
        }
        if (sec.ThirdPartyAntivirus.Count >= 2)
        {
            var f = New($"{sec.ThirdPartyAntivirus.Count} antivirus products are registered", Severity.Medium, Confidence.Medium,
                "Running more than one real-time antivirus causes slowdowns, conflicts, and crashes. Windows allows only one to be active, but leftovers still run services.", key: "security.multiple-av");
            foreach (var av in sec.ThirdPartyAntivirus) f.Ev("Antivirus", av, "WMI SecurityCenter2.AntiVirusProduct");
            f.Cause("Trial antivirus bundled with the PC plus one installed later.")
             .Rec("Keep one product (Microsoft Defender is sufficient for most customers) and remove others with their official removal tools.")
             .Why("Read from Windows Security Center registrations, which can include stale entries.");
            yield return f;
        }
        var offProfiles = sec.FirewallProfiles.Where(p => !p.Value).Select(p => p.Key).ToList();
        if (offProfiles.Count > 0)
        {
            yield return New($"Windows Firewall is off for: {string.Join(", ", offProfiles)}", thirdParty ? Severity.Low : Severity.High, Confidence.Medium,
                    "The firewall blocks unsolicited inbound connections. Turning it off exposes network services.", key: "security.firewall")
                .Ev("Disabled profiles", string.Join(", ", offProfiles), @"HKLM\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy")
                .Cause("Disabled manually for troubleshooting or by a third-party security suite with its own firewall.")
                .Shortcut("Turn the firewall back on in Windows Security.", "windowsdefender://network", "Open Firewall & network protection")
                .Why("Read from the firewall policy.")
                .Unsure(thirdParty ? "A third-party security suite may provide its own firewall." : "No third-party firewall is registered with Security Center.");
        }
        if (sec.UacEnabled == false)
        {
            yield return New("User Account Control is disabled", Severity.High, Confidence.High,
                    "With UAC off, every program runs with full administrator rights, so malware can change the system without any prompt.", key: "security.uac")
                .Ev("EnableLUA", "0", @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System")
                .Cause("Disabled by a tweaking guide or tool.")
                .Shortcut("Open UAC settings (re-enabling requires a restart).", "UserAccountControlSettings.exe", "Open UAC settings")
                .Why("Read from the registry value Windows uses.");
        }
        else if (sec.UacConsentPromptBehaviorAdmin == 0)
        {
            yield return New("UAC is set to never notify", Severity.Medium, Confidence.High,
                    "Administrator actions are elevated silently, so software can make system changes without the user noticing.", key: "security.uac-never")
                .Ev("ConsentPromptBehaviorAdmin", "0 (elevate without prompting)", @"HKLM\...\Policies\System")
                .Shortcut("Open UAC settings.", "UserAccountControlSettings.exe", "Open UAC settings")
                .Why("Read from the registry.");
        }
    }
}

internal sealed class SuspiciousProcessRule : Rule
{
    public override string Id => "processes.investigate";
    public override string Name => "Processes requiring investigation";
    public override FindingCategory Category => FindingCategory.Security;

    public override IEnumerable<Finding> Evaluate(SystemSnapshot s, DiagnosticContext ctx)
    {
        foreach (var p in s.ProcessIndicators.Where(p => p.Reasons.Count > 0))
        {
            var sev = p.Reasons.Count >= 2 ? Severity.Medium : Severity.Low;
            var f = New($"{p.Name} should be investigated", sev, Confidence.Low,
                    "This process has characteristics that are worth a closer look. These indicators are common in unwanted software but also occur in legitimate portable tools, games, and in-house apps. This is NOT a malware verdict.",
                    key: $"{Id}:{p.Name.ToLowerInvariant()}:{p.Path?.ToLowerInvariant()}")
                .Ev("Process", $"{p.Name} (PID {p.Pid})", "Process enumeration")
                .Ev("Location", p.Path ?? "Unknown", "Process image path")
                .Ev("Digital signature", p.Signature.ToString(), "WinVerifyTrust");
            foreach (var r in p.Reasons) f.Ev("Indicator", r, "Heuristic");
            f.Cause("Legitimate unsigned or portable software.", "Potentially unwanted software or malware.")
             .Rec("Ask the customer whether they recognise the program.")
             .Rec("Run a Microsoft Defender full scan (or Offline scan) and check the file with Sysinternals Process Explorer / Sigcheck.")
             .Shortcut("Open Windows Security scan options.", "windowsdefender://threat", "Open Virus & threat protection")
             .Why("Heuristic indicators only — names and locations can be legitimate.")
             .Unsure("The console does not and cannot classify a process as malware based on its name, location, signature, or resource usage.");
            yield return f;
        }
    }
}

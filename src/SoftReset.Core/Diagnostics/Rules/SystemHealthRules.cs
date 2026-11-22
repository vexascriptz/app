using SoftReset.Core.Models;

namespace SoftReset.Core.Diagnostics.Rules;

internal sealed class UpdateFailuresRule : Rule
{
    public override string Id => "updates.failures";
    public override string Name => "Windows Update installation failures";
    public override FindingCategory Category => FindingCategory.Updates;

    public override IEnumerable<Finding> Evaluate(SystemSnapshot s, DiagnosticContext ctx)
    {
        var cutoff = ctx.Now.AddDays(-s.Reliability.LookbackDays);
        var failed = s.Updates.RecentHistory.Where(h => h.Date >= cutoff && h.Result is "Failed" or "Aborted").ToList();
        var events = s.Reliability.Events.Where(e => e.Id == 20 && e.Provider.Contains("WindowsUpdateClient", StringComparison.OrdinalIgnoreCase)).ToList();
        if (failed.Count < 2 && events.Count < 2) yield break;

        var codes = failed.Select(h => WindowsUpdateErrors.Parse(h.HResult)).Concat(events.Select(e => WindowsUpdateErrors.Parse(e.Message)))
            .Where(c => c is not null).Select(c => c!.Value).Distinct().ToList();
        var repeated = failed.GroupBy(h => h.Title).Any(g => g.Count() >= 2);
        var f = New("Windows updates are failing to install", repeated ? Severity.High : Severity.Medium, Confidence.High,
            "One or more updates failed repeatedly. Failed updates leave security fixes uninstalled and can cause repeated download/restart loops.");
        foreach (var h in failed.OrderByDescending(h => h.Date).Take(8))
            f.Ev("Failed update", $"{When(h.Date)} — {h.Title}{(h.HResult is { Length: > 0 } hr ? $" ({hr})" : "")}", "Windows Update history (COM API)");
        foreach (var e in events.OrderByDescending(e => e.Time).Take(4))
            f.Ev("WindowsUpdateClient 20", $"{When(e.Time)} — {Shorten(e.Message)}", "System event log");
        foreach (var c in codes.Take(5)) f.Ev("Error code meaning", WindowsUpdateErrors.Describe(c), "Microsoft error reference");
        f.Cause("A corrupted update cache or component store.", "Insufficient free space on the system or recovery partition.", "Third-party antivirus or VPN interfering.")
         .Rec("Run the built-in Windows Update troubleshooter.", null)
         .Rec("Reset Windows Update components (renames the cache folders — reversible).", ActionIds.ResetWindowsUpdate)
         .Rec("Repair the component store (DISM) and system files (SFC).", ActionIds.DismRestoreHealth)
         .Shortcut("Open Windows Update.", "ms-settings:windowsupdate", "Open Windows Update")
         .Why("Read from the Windows Update Agent history and the System log.")
         .Unsure("Some failures resolve on their own on the next attempt; repeated failures of the same update are the strongest signal.");
        yield return f;
    }

    private static string Shorten(string m) => m.Length > 160 ? m[..160].Replace('\n', ' ') + "…" : m.Replace('\n', ' ');
}

internal sealed class UpdatesStaleRule : Rule
{
    public override string Id => "updates.stale";
    public override string Name => "Updates not installed recently";
    public override FindingCategory Category => FindingCategory.Updates;

    public override IEnumerable<Finding> Evaluate(SystemSnapshot s, DiagnosticContext ctx)
    {
        var u = s.Updates;
        if (u.WindowsUpdateServiceStartMode?.Equals("Disabled", StringComparison.OrdinalIgnoreCase) == true)
        {
            yield return New("The Windows Update service is disabled", Severity.High, Confidence.High,
                    "With the service disabled, Windows cannot receive security fixes. Third-party 'debloat' tools commonly do this.", key: "updates.service-disabled")
                .Ev("wuauserv start type", "Disabled", "Service Control Manager")
                .Cause("A tweaking/debloat tool or manual change disabled updates.")
                .Rec("Discuss with the user, then set the Windows Update service back to Manual (the Windows default) in Services.", ActionIds.ServiceStartMode, new() { ["service"] = "wuauserv", ["startMode"] = "Manual" })
                .Why("Read from the service configuration.");
        }

        if (u.LastSuccessfulInstall is not { } last) yield break;
        var age = (ctx.Now - last).TotalDays;
        if (age < 45) yield break;
        var f = New($"No update has installed successfully in {(int)age} days", age > 90 ? Severity.High : Severity.Medium, Confidence.Medium,
                "Windows normally installs security updates monthly. Long gaps mean the PC is missing security fixes.")
            .Ev("Last successful update", When(last), "Windows Update history (COM API)");
        if (u.UpdatesPausedByPolicyOrUser == true) f.Ev("Updates paused", u.PausedUntil is { } p ? $"until {When(p)}" : "Yes", "Windows Update settings");
        if (u.ManagedByPolicy) f.Ev("Managed by policy", "Yes — Group Policy or MDM controls updates", @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate");
        f.Cause("Updates are paused, failing, or blocked by policy/third-party tools.", "The PC is rarely online or rarely left on.")
         .Rec("Open Windows Update and check for updates with the user present.")
         .Shortcut("Open Windows Update.", "ms-settings:windowsupdate-action", "Check for updates")
         .Why("Based on the update agent's history; history can be cleared by resets, which would make this look older than reality.")
         .Unsure(u.ManagedByPolicy ? "Updates are managed by an organization — confirm with the user before changing anything." : "Update history can be reset by cleanup tools, which makes this estimate less certain.");
        yield return f;
    }
}

internal sealed class DeviceProblemRule : Rule
{
    public override string Id => "drivers.device-problems";
    public override string Name => "Devices with driver problems";
    public override FindingCategory Category => FindingCategory.Drivers;

    private static readonly HashSet<string> ImportantClasses = new(StringComparer.OrdinalIgnoreCase)
    { "Display", "Net", "HDC", "SCSIAdapter", "DiskDrive", "Bluetooth", "USB", "MEDIA", "AudioEndpoint", "System", "Processor" };

    public override IEnumerable<Finding> Evaluate(SystemSnapshot s, DiagnosticContext ctx)
    {
        foreach (var d in s.DeviceProblems.Where(d => d.ErrorCode != 0 && d.ErrorCode != 45))
        {
            var disabled = d.ErrorCode == 22;
            var sev = disabled ? Severity.Info : ImportantClasses.Contains(d.DeviceClass) ? Severity.High : Severity.Medium;
            var f = New($"{d.Name}: {(disabled ? "disabled" : "not working")}", sev, Confidence.High,
                    disabled ? "This device was disabled in Device Manager. That may be intentional." : $"Device Manager reports a problem with this device ({d.ErrorDescription}).",
                    key: $"{Id}:{d.PnpDeviceId}")
                .Ev("Device", d.Name, "WMI Win32_PnPEntity")
                .Ev("Class", d.DeviceClass, "WMI Win32_PnPEntity")
                .Ev("Status", d.ErrorDescription, "ConfigManagerErrorCode")
                .Ev("Hardware ID", d.PnpDeviceId, "WMI Win32_PnPEntity");
            switch (d.ErrorCode)
            {
                case 28:
                    f.Cause("No driver is installed for this device.").Rec("Install the driver from the PC/motherboard manufacturer's support page (search by the hardware ID), or check Windows Update optional driver updates.")
                     .Shortcut("Check optional driver updates.", "ms-settings:windowsupdate-optionalupdates", "Open optional updates");
                    break;
                case 43:
                    f.Cause("The device reported a failure — common with USB devices, GPUs, and Bluetooth adapters.").Rec("Reconnect the device, try another port, and reinstall its driver. For GPUs, a clean driver install; persistent Code 43 can mean hardware failure.");
                    break;
                case 10:
                case 31:
                case 39:
                case 52:
                    f.Cause("The driver failed to load or is corrupted/unsigned.").Rec("Uninstall the device in Device Manager (with 'delete driver' where offered), restart, and install the current driver.");
                    break;
                case 22:
                    f.Cause("Disabled manually.").Rec("Ask the user whether it should be enabled.");
                    break;
                default:
                    f.Cause("Driver or configuration problem.").Rec("Update or reinstall the driver for this device.");
                    break;
            }
            f.Shortcut("Open Device Manager.", "devmgmt.msc", "Open Device Manager").Why("Reported by Plug and Play.");
            yield return f;
        }
    }
}

internal sealed class GpuDriverRule : Rule
{
    public override string Id => "drivers.gpu";
    public override string Name => "Graphics driver status";
    public override FindingCategory Category => FindingCategory.Drivers;

    public override IEnumerable<Finding> Evaluate(SystemSnapshot s, DiagnosticContext ctx)
    {
        foreach (var g in s.Gpus)
        {
            if (g.Name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase) || g.Name.Contains("Basic Render", StringComparison.OrdinalIgnoreCase))
            {
                yield return New("No graphics driver is installed", Severity.High, Confidence.High,
                        "Windows is using the Microsoft Basic Display Adapter fallback. Games, video playback, multiple monitors, high refresh rates, and sleep may not work properly.", key: $"{Id}:basic")
                    .Ev("Adapter", g.Name, "WMI Win32_VideoController")
                    .Ev("Hardware ID", g.PnpDeviceId, "WMI Win32_VideoController")
                    .Cause("The GPU driver was never installed, failed to install, or was removed.")
                    .Rec("Install the driver from the GPU vendor (NVIDIA, AMD, or Intel) or the laptop manufacturer.")
                    .Why("The adapter name is reported by Windows.");
                continue;
            }
            if (g.Vendor is not (GpuVendor.Nvidia or GpuVendor.Amd or GpuVendor.Intel) || g.DriverDate is not { } date) continue;
            var ageDays = (ctx.Now - date).TotalDays;
            if (ageDays < 365) continue;
            var sev = ageDays > 730 ? Severity.Medium : Severity.Low;
            yield return New($"{g.Name} driver is {(int)(ageDays / 30)} months old", sev, Confidence.Medium,
                    "Older GPU drivers miss game optimizations and bug fixes. Newer drivers frequently fix crashes and stutter in recent games.", key: $"{Id}:{g.PnpDeviceId}")
                .Ev("Driver version", g.VendorDriverVersion is { } v ? $"{v} (Windows: {g.DriverVersion})" : g.DriverVersion, "WMI Win32_VideoController")
                .Ev("Driver date", date.ToString("yyyy-MM-dd"), "WMI Win32_VideoController")
                .Cause("The driver has not been updated.")
                .Rec(g.Vendor switch
                {
                    GpuVendor.Nvidia => "Update through the NVIDIA App or nvidia.com/drivers.",
                    GpuVendor.Amd => "Update through AMD Software: Adrenalin Edition or amd.com/support.",
                    _ => "Update through Intel Graphics Software / Driver & Support Assistant, or the laptop manufacturer.",
                })
                .Why("Driver dates are reported by Windows; OEM laptop drivers are sometimes intentionally older.")
                .Unsure("On laptops, the manufacturer's driver can be newer in practice than its date suggests and is sometimes required for features like switchable graphics.");
        }
    }
}

internal sealed class DiskHealthRule : Rule
{
    public override string Id => "disk.health";
    public override string Name => "Disk health";
    public override FindingCategory Category => FindingCategory.DiskHealth;

    public override IEnumerable<Finding> Evaluate(SystemSnapshot s, DiagnosticContext ctx)
    {
        foreach (var d in s.PhysicalDisks)
        {
            var bad = d.HealthStatus is "Warning" or "Unhealthy" || d.PredictFailure == true;
            if (bad)
            {
                var f = New($"{d.FriendlyName} reports a health problem", Severity.Critical, Confidence.High,
                        "The drive itself is reporting that it may fail. Back up the user's data before doing anything else.", key: $"{Id}:{d.FriendlyName}")
                    .Ev("Health status", d.HealthStatus, "WMI MSFT_PhysicalDisk.HealthStatus")
                    .Ev("Operational status", d.OperationalStatus.Count > 0 ? string.Join(", ", d.OperationalStatus) : "Unknown", "WMI MSFT_PhysicalDisk.OperationalStatus");
                if (d.PredictFailure == true) f.Ev("S.M.A.R.T. failure prediction", "Predicts failure", "WMI MSStorageDriver_FailurePredictStatus");
                if (d.ReadErrorsUncorrected is > 0) f.Ev("Uncorrected read errors", d.ReadErrorsUncorrected.Value.ToString(), "MSFT_StorageReliabilityCounter");
                f.Cause("The drive is wearing out or has developed bad sectors.")
                 .Rec("Back up important data immediately.")
                 .Rec("Replace the drive; clone it to a new drive while it still reads.")
                 .Why("Reported by the drive's own self-monitoring through Windows Storage Management.");
                yield return f;
                continue;
            }
            if (d.WearPercent is >= 80)
            {
                yield return New($"{d.FriendlyName} has used {d.WearPercent}% of its rated life", d.WearPercent >= 95 ? Severity.Critical : Severity.High, Confidence.High,
                        "SSDs have a finite number of write cycles. Near the end of rated endurance the drive may become read-only or fail.", key: $"{Id}:wear:{d.FriendlyName}")
                    .Ev("Wear", $"{d.WearPercent}%", "MSFT_StorageReliabilityCounter.Wear")
                    .Ev("Power-on hours", d.PowerOnHours?.ToString() ?? "Unknown", "MSFT_StorageReliabilityCounter")
                    .Cause("Heavy write workload over the drive's lifetime.")
                    .Rec("Plan a replacement and ensure backups are current.")
                    .Why("Reported by the drive.");
            }
            if (d.TemperatureC is >= 70)
            {
                yield return New($"{d.FriendlyName} is running hot ({d.TemperatureC} °C)", Severity.Medium, Confidence.Medium,
                        "High drive temperatures shorten lifespan and cause NVMe drives to throttle.", key: $"{Id}:temp:{d.FriendlyName}")
                    .Ev("Temperature", $"{d.TemperatureC} °C", "MSFT_StorageReliabilityCounter.Temperature")
                    .Cause("Poor airflow, no heatsink on an NVMe drive, or a drive placed next to the GPU.")
                    .Rec("Improve case airflow or fit an M.2 heatsink.")
                    .Why("Temperature reported by the drive.")
                    .Unsure("A single reading during heavy activity can be momentarily high.");
            }
        }
        if (!s.PhysicalDisks.Any(d => d.ReliabilityCountersAvailable) && s.PhysicalDisks.Count > 0 && !s.CollectedElevated)
        {
            // Informational: reliability counters require admin.
        }
    }
}

internal sealed class DiskErrorEventsRule : Rule
{
    public override string Id => "disk.error-events";
    public override string Name => "Storage errors in the event log";
    public override FindingCategory Category => FindingCategory.DiskHealth;

    public override IEnumerable<Finding> Evaluate(SystemSnapshot s, DiagnosticContext ctx)
    {
        var ev = s.Reliability.Events.Where(e =>
            (e.Provider.Equals("disk", StringComparison.OrdinalIgnoreCase) && e.Id is 7 or 51 or 153) ||
            (e.Provider.Contains("Ntfs", StringComparison.OrdinalIgnoreCase) && e.Id is 55 or 98 or 140) ||
            (e.Provider is "storahci" or "stornvme" or "iaStorAC" or "iaStorVD" or "storport" && e.Id is 129 or 11)).ToList();
        if (ev.Count == 0) yield break;
        var badBlocks = ev.Count(e => e.Provider.Equals("disk", StringComparison.OrdinalIgnoreCase) && e.Id == 7);
        var sev = badBlocks > 0 || ev.Count >= 10 ? Severity.High : Severity.Medium;
        var f = New($"{ev.Count} storage error(s) recorded", sev, Confidence.Medium,
            "Windows logged disk or file system errors. Bad blocks and repeated I/O retries are early warnings of drive failure or connection problems.");
        foreach (var g in ev.GroupBy(e => (e.Provider, e.Id)).OrderByDescending(g => g.Count()))
            f.Ev($"{g.Key.Provider} {g.Key.Id}", $"{g.Count()} × (last {When(g.Max(e => e.Time))}) — {Describe(g.Key.Provider, g.Key.Id)}", "System event log");
        f.Cause("Failing drive or bad sectors.", "Loose/damaged SATA cable or failing USB enclosure (for external drives).", "Storage driver or SSD firmware issue (event 129 resets).")
         .Rec("Back up important data, then run an online file system check.", ActionIds.ChkdskScan)
         .Rec("Check the drive's health status and update SSD firmware and storage drivers.")
         .Why("Logged by the Windows storage stack.")
         .Unsure("Errors may come from a USB stick or external drive rather than the internal disk — check which device the events refer to.");
        yield return f;
    }

    private static string Describe(string provider, int id) => (provider.ToLowerInvariant(), id) switch
    {
        ("disk", 7) => "bad block detected",
        ("disk", 51) => "error during a paging operation",
        ("disk", 153) => "I/O operation retried",
        (_, 55) => "file system structure corrupt",
        (_, 98) => "volume needs a repair scan",
        (_, 140) => "failed to flush data to the transaction log",
        (_, 129) => "storage controller reset the device",
        (_, 11) => "controller error",
        _ => "storage error",
    };
}

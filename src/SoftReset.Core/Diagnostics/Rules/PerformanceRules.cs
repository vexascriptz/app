using SoftReset.Core.Models;
using SoftReset.Core.Safety;

namespace SoftReset.Core.Diagnostics.Rules;

internal static class Known
{
    /// <summary>Windows maintenance processes whose high CPU is usually temporary.</summary>
    public static readonly HashSet<string> TransientMaintenance = new(StringComparer.OrdinalIgnoreCase)
    {
        "TiWorker.exe", "TrustedInstaller.exe", "MoUsoCoreWorker.exe", "usocoreworker.exe", "MsMpEng.exe",
        "SearchIndexer.exe", "SearchProtocolHost.exe", "CompatTelRunner.exe", "wuauclt.exe", "dism.exe",
        "DismHost.exe", "sppsvc.exe", "MpDefenderCoreService.exe", "WmiPrvSE.exe", "msiexec.exe", "mscorsvw.exe", "ngen.exe",
    };

    public static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome.exe", "msedge.exe", "firefox.exe", "brave.exe", "opera.exe", "vivaldi.exe", "msedgewebview2.exe",
    };

    public static readonly Dictionary<string, string> Overlays = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GameBar.exe"] = "Xbox Game Bar",
        ["Discord.exe"] = "Discord (in-game overlay if enabled)",
        ["GameOverlayUI.exe"] = "Steam overlay",
        ["NVIDIA Overlay.exe"] = "NVIDIA overlay",
        ["nvsphelper64.exe"] = "NVIDIA ShadowPlay helper",
        ["RTSS.exe"] = "RivaTuner Statistics Server",
        ["MSIAfterburner.exe"] = "MSI Afterburner",
        ["Overwolf.exe"] = "Overwolf",
        ["EpicOverlay.exe"] = "Epic Games overlay",
        ["AMDRSServ.exe"] = "AMD Software overlay service",
        ["RadeonSoftware.exe"] = "AMD Software",
        ["obs64.exe"] = "OBS Studio",
        ["medal.exe"] = "Medal.tv",
    };
}

internal sealed class CpuSustainedHighRule : Rule
{
    public override string Id => "cpu.sustained-high";
    public override string Name => "Sustained high CPU usage";
    public override FindingCategory Category => FindingCategory.Performance;

    public override IEnumerable<Finding> Evaluate(SystemSnapshot s, DiagnosticContext ctx)
    {
        var p = s.Performance;
        if (p.SampleSeconds <= 0 || p.CpuAveragePercent < 75) yield break;

        var sev = p.CpuAveragePercent >= 92 ? Severity.High : Severity.Medium;
        var conf = p.SampleSeconds >= 5 ? Confidence.Medium : Confidence.Low;
        var top = s.TopProcesses.OrderByDescending(t => t.CpuPercent).Take(5).ToList();
        var transient = top.Where(t => t.CpuPercent >= 10 && Known.TransientMaintenance.Contains(t.Name)).Select(t => t.Name).ToList();

        var f = New("CPU is heavily loaded", sev, conf,
                $"The processor averaged {Pct(p.CpuAveragePercent)} during a {p.SampleSeconds:0}-second sample. Sustained load above ~75% makes the PC feel slow and noisy.")
            .Ev("Average CPU load", Pct(p.CpuAveragePercent), "Performance sample (GetSystemTimes)")
            .Ev("Peak CPU load", Pct(p.CpuPeakPercent), "Performance sample")
            .Ev("CPU", $"{s.Hardware.CpuName} ({s.Hardware.CpuCores} cores / {s.Hardware.CpuLogicalProcessors} threads)", "WMI Win32_Processor");
        foreach (var t in top)
            f.Ev("Top process", $"{t.Name} — {Pct(t.CpuPercent)} CPU", "Process sample");

        f.Cause("One or more applications or background tasks are using the processor continuously.");
        if (transient.Count > 0)
            f.Cause($"Windows maintenance is running ({string.Join(", ", transient)}); this is usually temporary after updates, scans, or indexing.");
        if (s.Hardware.CpuLogicalProcessors is > 0 and <= 4)
            f.Cause("The CPU has few threads, so normal multitasking can saturate it.");

        f.Rec("Open Background Processes, sort by CPU, and identify what is using the processor before ending anything.")
         .Rec("If the top consumers are startup apps the customer does not need, disable them at startup.", ActionIds.StartupUserItems)
         .Why(conf == Confidence.Medium
             ? "Based on a multi-second sample of real CPU counters; high-load periods can be short-lived."
             : "Sample was very short, so this may be a momentary spike.")
         .Unsure("A single sample cannot distinguish a temporary burst (update installation, antivirus scan) from a persistent problem. Re-run diagnostics after a few minutes to confirm.");
        if (transient.Count > 0)
            f.Unsure("Windows maintenance processes were among the top consumers — waiting for them to finish may resolve this on its own.");
        yield return f;
    }
}

internal sealed class CpuProcessHogRule : Rule
{
    public override string Id => "cpu.process-hog";
    public override string Name => "Single process using a lot of CPU";
    public override FindingCategory Category => FindingCategory.Processes;

    public override IEnumerable<Finding> Evaluate(SystemSnapshot s, DiagnosticContext ctx)
    {
        if (s.Performance.SampleSeconds <= 0) yield break;
        foreach (var t in s.TopProcesses.Where(t => t.CpuPercent >= 25 && !string.Equals(t.Name, "Idle", StringComparison.OrdinalIgnoreCase)))
        {
            var k = ctx.Knowledge.FindProcess(t.Name);
            var transient = Known.TransientMaintenance.Contains(t.Name);
            var sev = t.CpuPercent >= 50 && !transient ? Severity.Medium : Severity.Low;
            var f = New($"{(k?.DisplayName is { Length: > 0 } dn ? dn : t.Name)} is using {Pct(t.CpuPercent)} of the CPU", sev, Confidence.Medium,
                    $"{t.Name} used {Pct(t.CpuPercent)} of total CPU capacity during the sample.", key: $"{Id}:{t.Name.ToLowerInvariant()}")
                .Ev("Process", $"{t.Name} (PID {t.Pid})", "Process sample")
                .Ev("CPU share", Pct(t.CpuPercent), "Process sample (processor time delta)")
                .Ev("Location", t.Path ?? "Not accessible without administrator rights", "Process image path")
                .Ev("Publisher", t.Publisher ?? "Unknown", "File version information");
            if (k?.HighCpu is { Length: > 0 } note) f.Cause(note);
            else f.Cause("The application is busy (working, stuck in a loop, or waiting on a resource it keeps retrying).");
            if (transient) f.Cause("This is a Windows maintenance process; high usage is normally temporary.");
            f.Rec($"Review {t.Name} in Background Processes for its publisher, signature, and recommendations.")
             .Why("Measured from processor-time deltas over the sample window.")
             .Unsure("Some applications legitimately use high CPU while working (games, video export, compiling). Confirm with the customer what was running.");
            yield return f;
        }
    }
}

internal sealed class MemoryPressureRule : Rule
{
    public override string Id => "memory.pressure";
    public override string Name => "High memory usage";
    public override FindingCategory Category => FindingCategory.Memory;

    public override IEnumerable<Finding> Evaluate(SystemSnapshot s, DiagnosticContext ctx)
    {
        var p = s.Performance;
        if (p.MemoryTotalBytes <= 0) yield break;
        var used = p.MemoryUsedPercent;
        var commit = p.CommitPercent;
        if (used < 85 && commit < 90) yield break;

        var sev = used >= 93 || commit >= 95 ? Severity.High : Severity.Medium;
        var top = s.TopProcesses.OrderByDescending(t => t.PrivateBytes).Take(6).ToList();
        var f = New("Memory is nearly full", sev, Confidence.High,
                $"{Pct(used)} of {Bytes(p.MemoryTotalBytes)} RAM is in use. When memory runs out Windows pages to disk, which causes stutter and slow app switching.")
            .Ev("Physical memory in use", $"{Pct(used)} ({Bytes(p.MemoryTotalBytes - p.MemoryAvailableBytes)} of {Bytes(p.MemoryTotalBytes)})", "GlobalMemoryStatusEx")
            .Ev("Commit charge", $"{Pct(commit)} ({Bytes(p.CommitTotalBytes)} of {Bytes(p.CommitLimitBytes)})", "GlobalMemoryStatusEx");
        foreach (var t in top)
            f.Ev("Large process", $"{t.Name} — {Bytes(t.PrivateBytes)} private", "Process sample");

        var browserBytes = s.TopProcesses.Where(t => Known.Browsers.Contains(t.Name)).Sum(t => t.PrivateBytes);
        f.Cause("Open applications and background programs are using most of the installed RAM.");
        if (browserBytes > p.MemoryTotalBytes * 0.25)
            f.Cause($"Web browsers are using about {Bytes(browserBytes)} — many open tabs or extensions.");
        if (p.MemoryTotalBytes <= 8L * 1024 * 1024 * 1024)
            f.Cause("The PC has 8 GB of RAM or less, which modern browsers and games fill quickly.");

        f.Rec("Close unused applications and browser tabs; disable startup apps that keep running in the background.", ActionIds.StartupUserItems)
         .Rec("Review the largest processes in Background Processes.")
         .Why("Read directly from Windows memory counters at the time of the scan.")
         .Unsure("Memory use depends on what the customer had open during the scan. A RAM upgrade is only justified if usage stays high during their normal workload.");
        yield return f;
    }
}

internal sealed class LowInstalledMemoryRule : Rule
{
    public override string Id => "memory.low-installed";
    public override string Name => "Low installed memory";
    public override FindingCategory Category => FindingCategory.Memory;

    public override IEnumerable<Finding> Evaluate(SystemSnapshot s, DiagnosticContext ctx)
    {
        var total = s.Hardware.TotalMemoryBytes > 0 ? s.Hardware.TotalMemoryBytes : s.Performance.MemoryTotalBytes;
        if (total <= 0) yield break;
        var gb = total / 1073741824.0;
        if (gb >= 7.5) yield break;
        var sev = gb < 3.8 ? Severity.High : Severity.Medium;
        yield return New($"Only {gb:0.#} GB of RAM installed", sev, Confidence.High,
                "Windows 11 requires 4 GB and runs comfortably with 8 GB or more. Low RAM is a common cause of general slowness.")
            .Ev("Installed memory", $"{gb:0.#} GB", "WMI Win32_ComputerSystem")
            .Ev("Memory modules", s.Hardware.MemoryModules.Count > 0 ? string.Join(", ", s.Hardware.MemoryModules.Select(m => $"{m.Slot}: {Bytes(m.CapacityBytes)}")) : "Not reported", "WMI Win32_PhysicalMemory")
            .Cause("The hardware has limited memory for current software.")
            .Rec("Reduce background apps and startup items to free memory.", ActionIds.StartupUserItems)
            .Rec("Discuss a memory upgrade with the customer if the motherboard has free slots or supports larger modules.")
            .Why("Installed capacity is reported by firmware and is reliable.");
    }
}

internal sealed class ProcessCountRule : Rule
{
    public override string Id => "processes.many-background";
    public override string Name => "Many background processes";
    public override FindingCategory Category => FindingCategory.Processes;

    public override IEnumerable<Finding> Evaluate(SystemSnapshot s, DiagnosticContext ctx)
    {
        if (s.ProcessCount < 280) yield break;
        var nonMsStartup = s.StartupEntries.Count(e => e.Enabled && !e.IsMicrosoft);
        yield return New($"{s.ProcessCount} processes are running", s.ProcessCount >= 350 ? Severity.Medium : Severity.Low, Confidence.Medium,
                "A typical Windows 11 desktop runs roughly 150–250 processes. Large counts usually come from background apps, updaters, and browser helpers.")
            .Ev("Running processes", s.ProcessCount.ToString(), "Process enumeration")
            .Ev("Threads", s.ThreadCount.ToString(), "Process enumeration")
            .Ev("Enabled non-Microsoft startup items", nonMsStartup.ToString(), "Startup inventory")
            .Cause("Background utilities, vendor tools, and app updaters start with Windows and stay resident.", "Each browser tab and extension runs as separate processes.")
            .Rec("Review startup items and disable the ones the customer does not need.", ActionIds.StartupUserItems)
            .Rec("Review Background Processes grouped by publisher to spot redundant vendor utilities.")
            .Why("Counts are exact; what counts as 'too many' depends on the customer's software.")
            .Unsure("Process count alone does not prove a performance problem; it is a pointer for review.");
    }
}

internal sealed class StorageLowSpaceRule : Rule
{
    public override string Id => "storage.low-space";
    public override string Name => "Low free disk space";
    public override FindingCategory Category => FindingCategory.Storage;

    public override IEnumerable<Finding> Evaluate(SystemSnapshot s, DiagnosticContext ctx)
    {
        foreach (var d in s.Drives.Where(d => d.TotalBytes > 0 && d.DriveType == "Fixed"))
        {
            var freeGb = d.FreeBytes / 1073741824.0;
            Severity? sev = null;
            if (d.IsSystemDrive)
            {
                if (d.FreePercent < 5 || freeGb < 5) sev = Severity.Critical;
                else if (d.FreePercent < 10 || freeGb < 15) sev = Severity.High;
                else if (d.FreePercent < 15 || freeGb < 25) sev = Severity.Medium;
            }
            else if (d.FreePercent < 5) sev = Severity.Medium;
            else if (d.FreePercent < 10) sev = Severity.Low;
            if (sev is null) continue;

            var f = New($"Drive {d.Name} is running out of space", sev.Value, Confidence.High,
                    d.IsSystemDrive
                        ? $"The Windows drive has {Bytes(d.FreeBytes)} free ({Pct(d.FreePercent)}). Windows needs free space for updates, the page file, and temporary files; below ~10% updates fail and performance drops."
                        : $"Drive {d.Name} has {Bytes(d.FreeBytes)} free ({Pct(d.FreePercent)}).",
                    key: $"{Id}:{d.Name}")
                .Ev("Free space", $"{Bytes(d.FreeBytes)} of {Bytes(d.TotalBytes)} ({Pct(d.FreePercent)})", "DriveInfo")
                .Ev("System drive", d.IsSystemDrive ? "Yes" : "No", "DriveInfo");
            if (d.IsSystemDrive)
            {
                var c = s.Cleanup;
                if (c.UserTempBytes is > 0) f.Ev("User temporary files", Bytes(c.UserTempBytes), "%TEMP% folder size");
                if (c.WindowsTempBytes is > 0) f.Ev("Windows temporary files", Bytes(c.WindowsTempBytes), @"C:\Windows\Temp size");
                if (c.RecycleBinBytes is > 0) f.Ev("Recycle Bin", Bytes(c.RecycleBinBytes), "SHQueryRecycleBin");
                if (c.WindowsUpdateCacheBytes is > 0) f.Ev("Windows Update download cache", Bytes(c.WindowsUpdateCacheBytes), @"SoftwareDistribution\Download size");
                if (c.HibernationFileBytes is > 0) f.Ev("Hibernation file", Bytes(c.HibernationFileBytes), "hiberfil.sys size");
                if (c.DownloadsFolderBytes is > 0) f.Ev("Downloads folder", Bytes(c.DownloadsFolderBytes), "Downloads folder size");
                f.Rec("Clear temporary files (preview first; nothing personal is deleted).", ActionIds.UserTemp)
                 .Rec("Empty the Recycle Bin after confirming with the customer.", ActionIds.RecycleBin)
                 .Rec("Clear the Windows Update download cache.", ActionIds.UpdateCache)
                 .Rec("Use File Organizer to find large and duplicate files in Downloads with the customer.")
                 .Shortcut("Review large files and apps in Windows Storage settings.", "ms-settings:storagesense", "Open Storage settings");
            }
            f.Cause("Accumulated downloads, media, temporary files, and installed applications.")
             .Why("Free space is read directly from the file system.");
            yield return f;
        }
    }
}

internal sealed class CleanupOpportunityRule : Rule
{
    public override string Id => "storage.cleanup-opportunity";
    public override string Name => "Reclaimable temporary data";
    public override FindingCategory Category => FindingCategory.Storage;

    public override IEnumerable<Finding> Evaluate(SystemSnapshot s, DiagnosticContext ctx)
    {
        var total = DiagnosticEngine.Reclaimable(s);
        if (total is null || total < 2L * 1073741824) yield break;
        if (s.SystemDrive is { } sd && (sd.FreePercent < 15)) yield break; // covered by low-space rule
        var c = s.Cleanup;
        var f = New($"About {Bytes(total)} of temporary data can be cleared", Severity.Low, Confidence.High,
                "Temporary files, the Recycle Bin, and the Windows Update cache can be cleared safely. This frees space but does not by itself make Windows faster.")
            .Ev("Estimated reclaimable", Bytes(total), "Folder size measurement");
        if (c.UserTempBytes is > 0) f.Ev("User temp", Bytes(c.UserTempBytes), "%TEMP%");
        if (c.WindowsTempBytes is > 0) f.Ev("Windows temp", Bytes(c.WindowsTempBytes), @"C:\Windows\Temp");
        if (c.RecycleBinBytes is > 0) f.Ev("Recycle Bin", Bytes(c.RecycleBinBytes), "SHQueryRecycleBin");
        if (c.WindowsUpdateCacheBytes is > 0) f.Ev("Update cache", Bytes(c.WindowsUpdateCacheBytes), "SoftwareDistribution");
        f.Cause("Windows and applications leave temporary files behind; the Recycle Bin is not emptied automatically unless Storage Sense is configured.")
         .Rec("Clear user temporary files.", ActionIds.UserTemp)
         .Rec("Empty the Recycle Bin (confirm with the customer first).", ActionIds.RecycleBin)
         .Why("Sizes are measured directly; files in use are skipped during cleanup so the final amount can be lower.")
         .Unsure("Some files will be locked by running programs and cannot be removed until they close.");
        yield return f;
    }
}

internal sealed class HddSystemDriveRule : Rule
{
    public override string Id => "storage.hdd-system-drive";
    public override string Name => "Windows installed on a hard disk";
    public override FindingCategory Category => FindingCategory.Storage;

    public override IEnumerable<Finding> Evaluate(SystemSnapshot s, DiagnosticContext ctx)
    {
        var hdd = s.PhysicalDisks.Where(d => d.MediaType.Equals("HDD", StringComparison.OrdinalIgnoreCase)).ToList();
        var ssd = s.PhysicalDisks.Where(d => d.MediaType.Equals("SSD", StringComparison.OrdinalIgnoreCase)).ToList();
        if (hdd.Count == 0 || ssd.Count > 0) yield break; // can't tell which disk holds C: when both exist
        yield return New("Windows runs from a mechanical hard drive", Severity.Medium, Confidence.Medium,
                "Hard disk drives are 10–50× slower than SSDs for the small random reads Windows performs at startup and when launching apps. This is the single biggest cause of slow boots on older PCs.")
            .Ev("Disk", string.Join(", ", hdd.Select(d => $"{d.FriendlyName} ({Bytes(d.SizeBytes)}, {d.MediaType})")), "WMI MSFT_PhysicalDisk")
            .Ev("SSDs detected", "None", "WMI MSFT_PhysicalDisk")
            .Cause("Mechanical storage limits boot and application load times regardless of software tuning.")
            .Rec("Recommend an SSD upgrade with a cloned system drive — this improves responsiveness far more than any software change.")
            .Rec("Reduce startup items to lessen disk load during boot.", ActionIds.StartupUserItems)
            .Why("Media type is reported by the storage stack; some USB bridges and RAID controllers report it incorrectly.")
            .Unsure("Media type may show as Unspecified or wrong behind some controllers or in virtual machines.");
    }
}

internal sealed class StartupManyItemsRule : Rule
{
    public override string Id => "startup.many-items";
    public override string Name => "Many startup programs";
    public override FindingCategory Category => FindingCategory.Startup;

    public override IEnumerable<Finding> Evaluate(SystemSnapshot s, DiagnosticContext ctx)
    {
        var enabled = s.StartupEntries.Where(e => e.Enabled && !e.IsMicrosoft && e.Location != StartupLocation.Service).ToList();
        if (enabled.Count < 6) yield break;
        var sev = enabled.Count >= 12 ? Severity.Medium : Severity.Low;
        var f = New($"{enabled.Count} non-Microsoft programs start with Windows", sev, Confidence.High,
                "Every startup program delays the desktop becoming usable and keeps using memory afterwards. Most vendor utilities and updaters do not need to start automatically.");
        foreach (var e in enabled.Take(15))
            f.Ev("Startup item", $"{e.Name} — {e.Publisher ?? "unknown publisher"} ({LocationText(e.Location)})", "Startup inventory (Run keys, Startup folders, logon tasks)");
        var latestBoot = s.Reliability.BootRecords.OrderByDescending(b => b.Time).FirstOrDefault();
        if (latestBoot is not null) f.Ev("Most recent boot duration", $"{latestBoot.BootTimeMs / 1000.0:0.#} s", "Diagnostics-Performance event 100");
        f.Cause("Applications register themselves to start automatically during installation.")
         .Rec("Disable startup items the customer does not need. Disabling is reversible and does not uninstall anything.", ActionIds.StartupUserItems)
         .Rec("Machine-wide startup items require administrator approval.", ActionIds.StartupMachineItems)
         .Why("The startup list is read from the same locations Task Manager uses.")
         .Unsure("Windows does not expose Task Manager's per-app 'startup impact' through a documented API, so impact is inferred from the number of items and boot duration.");
        if (!s.Reliability.BootRecordsReadable)
            f.Unsure("Boot-time records could not be read (administrator rights are needed), so actual boot duration was not measured.");
        yield return f;
    }

    internal static string LocationText(StartupLocation l) => l switch
    {
        StartupLocation.RegistryRunUser => "user Run key",
        StartupLocation.RegistryRunMachine => "machine Run key",
        StartupLocation.RegistryRunMachineWow64 => "machine Run key (32-bit)",
        StartupLocation.StartupFolderUser => "user Startup folder",
        StartupLocation.StartupFolderCommon => "all-users Startup folder",
        StartupLocation.ScheduledTaskLogon => "logon scheduled task",
        _ => l.ToString(),
    };
}

internal sealed class StartupBrokenEntriesRule : Rule
{
    public override string Id => "startup.broken-entries";
    public override string Name => "Startup entries pointing to missing files";
    public override FindingCategory Category => FindingCategory.Startup;

    public override IEnumerable<Finding> Evaluate(SystemSnapshot s, DiagnosticContext ctx)
    {
        var broken = s.StartupEntries.Where(e => e.Enabled && !e.FileExists).ToList();
        if (broken.Count == 0) yield break;
        var f = New($"{broken.Count} startup item(s) point to files that no longer exist", Severity.Low, Confidence.High,
            "Leftovers from uninstalled software. They do nothing except produce errors or brief delays at sign-in.");
        foreach (var e in broken) f.Ev("Missing target", $"{e.Name}: {e.ExecutablePath}", "Startup inventory + file check");
        f.Cause("The program was uninstalled or moved without removing its startup registration.")
         .Rec("Disable these entries (reversible).", ActionIds.StartupUserItems)
         .Why("The referenced file path was checked on disk.");
        yield return f;
    }
}

internal sealed class SlowBootRule : Rule
{
    public override string Id => "startup.slow-boot";
    public override string Name => "Slow boot";
    public override FindingCategory Category => FindingCategory.Startup;

    public override IEnumerable<Finding> Evaluate(SystemSnapshot s, DiagnosticContext ctx)
    {
        var records = s.Reliability.BootRecords.OrderByDescending(b => b.Time).Take(5).ToList();
        if (records.Count == 0) yield break;
        var median = records.Select(r => r.BootTimeMs).OrderBy(x => x).ElementAt(records.Count / 2);
        if (median < 60_000) yield break;
        var sev = median >= 120_000 ? Severity.High : Severity.Medium;
        var f = New($"Windows takes about {median / 1000} seconds to start", sev, Confidence.High,
            "Boot duration measured by Windows itself (from power-on handoff until the desktop is usable). Under 30–40 seconds is typical for an SSD-based PC.");
        foreach (var r in records)
            f.Ev("Boot", $"{When(r.Time)} — {r.BootTimeMs / 1000.0:0.#} s total", "Diagnostics-Performance event 100");
        foreach (var d in records.SelectMany(r => r.Degradations).Distinct().Take(8))
            f.Ev("Slowed startup", d, "Diagnostics-Performance events 101–110");
        f.Cause("Too many startup programs or services.", "A slow or failing system drive.", "A driver or service that waits for a timeout during boot.")
         .Rec("Disable unnecessary startup items.", ActionIds.StartupUserItems)
         .Rec("Check disk health and media type in this report.")
         .Why("Windows records boot timing in its Diagnostics-Performance log.");
        if (s.PhysicalDisks.Any(d => d.MediaType == "HDD")) f.Cause("A mechanical hard drive is present.");
        yield return f;
    }
}

internal sealed class FastStartupUptimeRule : Rule
{
    public override string Id => "startup.fast-startup-uptime";
    public override string Name => "Long uptime with Fast Startup";
    public override FindingCategory Category => FindingCategory.Stability;

    public override IEnumerable<Finding> Evaluate(SystemSnapshot s, DiagnosticContext ctx)
    {
        if (s.Os.Uptime(ctx.Now) is not { } up || up.TotalDays < 7) yield break;
        var fast = s.Power.FastStartupEnabled == true;
        var sev = up.TotalDays >= 21 || s.Updates.RebootPending == true ? Severity.Medium : Severity.Low;
        var f = New($"Windows has not fully restarted in {(int)up.TotalDays} days", sev, Confidence.High,
                fast
                    ? "Fast Startup is enabled, so 'Shut down' hibernates the Windows kernel instead of restarting it. Driver and memory problems accumulate until the PC is restarted with Restart."
                    : "Long uptime lets memory leaks and driver problems accumulate, and delays update installation.")
            .Ev("Last full boot", s.Os.LastBootTime is { } b ? When(b) : "Unknown", "WMI Win32_OperatingSystem.LastBootUpTime")
            .Ev("Uptime", $"{up.TotalDays:0.#} days", "Calculated")
            .Ev("Fast Startup", s.Power.FastStartupEnabled switch { true => "Enabled", false => "Disabled", _ => "Unknown" }, @"HKLM\...\Session Manager\Power\HiberbootEnabled");
        if (s.Updates.RebootPending == true) f.Ev("Restart pending for updates", "Yes", "Component Based Servicing / Windows Update registry");
        f.Cause(fast ? "The customer uses Shut down, which does not reset the kernel when Fast Startup is on." : "The PC is rarely restarted.")
         .Rec("Restart Windows (Start → Power → Restart) at the end of the session.")
         .Rec("Optionally disable Fast Startup so Shut down performs a full restart.", ActionIds.FastStartup)
         .Why("Boot time is reported by Windows.");
        yield return f;
    }
}

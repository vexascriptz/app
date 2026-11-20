using System.Text.RegularExpressions;
using SoftReset.Core.Models;

namespace SoftReset.Core.Diagnostics.Rules;

internal sealed class UnexpectedShutdownRule : Rule
{
    public override string Id => "stability.unexpected-shutdowns";
    public override string Name => "Unexpected shutdowns or restarts";
    public override FindingCategory Category => FindingCategory.Stability;

    public override IEnumerable<Finding> Evaluate(SystemSnapshot s, DiagnosticContext ctx)
    {
        var kp = s.Reliability.Find("Kernel-Power", 41).ToList();
        var dirty = s.Reliability.Find("EventLog", 6008).ToList();
        var count = Math.Max(kp.Count, dirty.Count);
        if (count == 0) yield break;

        var withBugcheck = kp.Where(e => BugCheckCodes.ParseNumber(e.Get("BugcheckCode")) is > 0).ToList();
        var noBugcheck = kp.Count - withBugcheck.Count;
        var sev = count >= 3 ? Severity.High : Severity.Medium;
        var f = New($"{count} unexpected shutdown(s) in the last {s.Reliability.LookbackDays} days", sev, Confidence.High,
            "Windows recorded that it restarted without shutting down cleanly. Each event is a crash, freeze, power loss, or the power button being held.");
        foreach (var e in kp.OrderByDescending(e => e.Time).Take(8))
        {
            var code = BugCheckCodes.ParseNumber(e.Get("BugcheckCode"));
            f.Ev("Kernel-Power 41", $"{When(e.Time)}{(code is > 0 ? $" — after stop code {BugCheckCodes.Describe(code.Value)}" : " — no stop code recorded")}", "System event log");
        }
        foreach (var e in dirty.OrderByDescending(e => e.Time).Take(4))
            f.Ev("EventLog 6008", $"{When(e.Time)} — previous shutdown was unexpected", "System event log");

        if (withBugcheck.Count > 0) f.Cause("Blue-screen crashes (stop codes were recorded — see the blue screen finding).");
        if (noBugcheck > 0)
        {
            f.Cause("Sudden power loss, a power supply or battery problem, overheating shutdown, or a hard freeze that required holding the power button.");
            if (s.Hardware.IsLaptop) f.Cause("On laptops: a worn battery or faulty charger can cause instant power-off.");
        }
        f.Rec("Ask the customer what happened at those times (freeze, black screen, sudden power-off, power cut).")
         .Rec("Check temperatures and clean dust from fans and heatsinks if shutdowns happen under load.")
         .Rec("If stop codes are present, follow the blue screen finding first.")
         .Shortcut("View the history in Reliability Monitor.", "perfmon.exe /rel", "Open Reliability Monitor")
         .Why("Recorded by the Windows kernel at the next boot.")
         .Unsure("Kernel-Power 41 says that a clean shutdown did not happen, not why. A power cut and a hardware fault look identical in the log.");
        yield return f;
    }
}

internal sealed class BlueScreenRule : Rule
{
    public override string Id => "stability.bluescreens";
    public override string Name => "Blue screen crashes";
    public override FindingCategory Category => FindingCategory.Stability;

    public override IEnumerable<Finding> Evaluate(SystemSnapshot s, DiagnosticContext ctx)
    {
        var codes = new List<(DateTimeOffset Time, uint Code, string Source)>();
        foreach (var e in s.Reliability.Events.Where(e => e.Id == 1001 && (e.Provider.Contains("WER-SystemErrorReporting", StringComparison.OrdinalIgnoreCase) || e.Provider.Equals("BugCheck", StringComparison.OrdinalIgnoreCase))))
            if (BugCheckCodes.ParseFromText(e.Message) is { } c) codes.Add((e.Time, c, "System event log (BugCheck 1001)"));
        foreach (var e in s.Reliability.Find("Kernel-Power", 41))
            if (BugCheckCodes.ParseNumber(e.Get("BugcheckCode")) is { } c && c > 0 && !codes.Any(x => Math.Abs((x.Time - e.Time).TotalMinutes) < 15))
                codes.Add((e.Time, c, "Kernel-Power 41"));
        var dumps = s.Reliability.CrashDumps.Where(d => d.Kind == "Minidump").ToList();
        if (codes.Count == 0 && dumps.Count == 0) yield break;

        var sev = codes.Count + dumps.Count >= 3 ? Severity.High : Severity.Medium;
        if (codes.Count >= 5) sev = Severity.Critical;
        var distinct = codes.GroupBy(c => c.Code).OrderByDescending(g => g.Count()).ToList();
        var f = New($"{Math.Max(codes.Count, dumps.Count)} blue screen crash(es) recorded", sev, Confidence.High,
            "Windows stopped with a bug check (blue screen). The stop code points to the type of failure.");
        foreach (var g in distinct)
            f.Ev("Stop code", $"{BugCheckCodes.Describe(g.Key)} × {g.Count()} (last {When(g.Max(x => x.Time))})", g.First().Source);
        foreach (var d in dumps.OrderByDescending(d => d.Created).Take(5))
            f.Ev("Crash dump", $"{Path.GetFileName(d.Path)} — {When(d.Created)}", @"C:\Windows\Minidump");

        foreach (var g in distinct.Take(3)) f.Cause(BugCheckCodes.Hint(g.Key));
        if (distinct.Any(g => BugCheckCodes.IsGraphicsRelated(g.Key)))
            f.Rec("Clean-install the latest GPU driver from the GPU vendor and remove any GPU overclock.");
        if (distinct.Any(g => BugCheckCodes.IsHardwareRelated(g.Key)))
            f.Rec("Disable CPU/RAM overclocks (including XMP/EXPO temporarily) and run Windows Memory Diagnostic.", null)
             .Shortcut("Schedule a memory test at next restart.", "mdsched.exe", "Open Windows Memory Diagnostic");
        f.Rec("Update chipset, storage, and network drivers from the PC or motherboard manufacturer.")
         .Rec("Repair system files (SFC) if crashes started after an update or disk error.", ActionIds.Sfc)
         .Why("Stop codes are written by Windows when the crash happens.")
         .Unsure("The stop code identifies the failure type, not always the culprit. Pinpointing the driver requires analysing the minidump with WinDbg (Microsoft, free).");
        if (!s.Reliability.CrashDumpsReadable)
            f.Unsure("The Minidump folder could not be listed without administrator rights.");
        yield return f;
    }
}

internal sealed class WheaErrorRule : Rule
{
    public override string Id => "stability.whea-errors";
    public override string Name => "Hardware error reports (WHEA)";
    public override FindingCategory Category => FindingCategory.Stability;

    public override IEnumerable<Finding> Evaluate(SystemSnapshot s, DiagnosticContext ctx)
    {
        var whea = s.Reliability.Find("WHEA-Logger").ToList();
        if (whea.Count == 0) yield break;
        var fatal = whea.Where(e => e.Id is 1 or 18 or 20 or 46).ToList();
        var corrected = whea.Count - fatal.Count;
        var sev = fatal.Count > 0 ? Severity.High : corrected >= 10 ? Severity.Medium : Severity.Low;
        var f = New($"Hardware reported {whea.Count} error(s) to Windows", sev, Confidence.High,
            fatal.Count > 0
                ? "The CPU, memory, or PCIe hardware reported uncorrectable errors. These cause crashes and data corruption."
                : "The hardware reported errors that it corrected automatically. Occasional corrected errors are harmless; frequent ones indicate instability.");
        foreach (var g in whea.GroupBy(e => e.Id).OrderByDescending(g => g.Count()))
            f.Ev($"WHEA event {g.Key}", $"{g.Count()} occurrence(s), last {When(g.Max(e => e.Time))} — {Trim(g.First().Message)}", "System event log (WHEA-Logger)");
        f.Cause("Unstable overclock, undervolt, or memory XMP/EXPO profile.", "PCIe link problems (risers, power-saving states, GPU or NVMe seating).", "Failing CPU, RAM, or motherboard.")
         .Rec("Return CPU and memory to stock settings and check whether errors continue.")
         .Rec("Reseat the reported PCIe device and update BIOS/UEFI and chipset drivers.")
         .Why("WHEA events come directly from hardware error reporting.")
         .Unsure("Identifying the exact component requires the event details (error source and bus/device numbers).");
        yield return f;
    }

    private static string Trim(string m) => m.Length > 140 ? m[..140].Replace('\n', ' ') + "…" : m.Replace('\n', ' ');
}

internal sealed partial class AppCrashRule : Rule
{
    public override string Id => "stability.app-crashes";
    public override string Name => "Recurring application crashes";
    public override FindingCategory Category => FindingCategory.Stability;

    private static readonly Dictionary<string, string> DriverModules = new(StringComparer.OrdinalIgnoreCase)
    {
        ["nvwgf2umx.dll"] = "NVIDIA Direct3D driver", ["nvoglv64.dll"] = "NVIDIA OpenGL driver", ["nvldumdx.dll"] = "NVIDIA driver loader",
        ["amdxx64.dll"] = "AMD driver", ["atidxx64.dll"] = "AMD Direct3D driver", ["amdvlk64.dll"] = "AMD Vulkan driver",
        ["igd10iumd64.dll"] = "Intel graphics driver", ["igdumdim64.dll"] = "Intel graphics driver", ["igxelpicd64.dll"] = "Intel graphics driver",
        ["d3d11.dll"] = "Direct3D runtime", ["dxgi.dll"] = "DirectX graphics infrastructure",
    };

    public override IEnumerable<Finding> Evaluate(SystemSnapshot s, DiagnosticContext ctx)
    {
        var crashes = s.Reliability.Events.Where(e => e.Id == 1000 && e.Provider.Equals("Application Error", StringComparison.OrdinalIgnoreCase)).ToList();
        var hangs = s.Reliability.Events.Where(e => e.Id == 1002 && e.Provider.Equals("Application Hang", StringComparison.OrdinalIgnoreCase)).ToList();
        var groups = crashes.GroupBy(e => (e.Get("AppName") ?? AppFromMessage(e.Message) ?? "unknown").ToLowerInvariant()).Where(g => g.Count() >= 3).ToList();
        foreach (var g in groups)
        {
            var app = g.First().Get("AppName") ?? g.Key;
            var modules = g.Select(e => e.Get("ModuleName") ?? "").Where(m => m.Length > 0).GroupBy(m => m, StringComparer.OrdinalIgnoreCase).OrderByDescending(m => m.Count()).ToList();
            var sev = g.Count() >= 10 ? Severity.High : Severity.Medium;
            var f = New($"{app} crashed {g.Count()} times", sev, Confidence.High,
                    $"Windows Error Reporting recorded {g.Count()} crashes of {app} in the last {s.Reliability.LookbackDays} days.", key: $"{Id}:{g.Key}")
                .Ev("Crashes", g.Count().ToString(), "Application event log (Application Error 1000)")
                .Ev("Most recent", When(g.Max(e => e.Time)), "Application event log");
            foreach (var m in modules.Take(3))
                f.Ev("Faulting module", $"{m.Key} × {m.Count()}{(DriverModules.TryGetValue(m.Key, out var d) ? $" ({d})" : "")}", "Application Error 1000");
            var code = g.Select(e => e.Get("ExceptionCode")).FirstOrDefault(c => !string.IsNullOrEmpty(c));
            if (code is not null) f.Ev("Exception code", code, "Application Error 1000");

            if (modules.Any(m => DriverModules.ContainsKey(m.Key)))
                f.Cause("The crash happens inside a graphics driver component — a driver update or clean reinstall is the usual fix.")
                 .Rec("Update or clean-install the graphics driver.");
            f.Cause("A bug or corrupted installation in the application, or an incompatible add-on/plugin.")
             .Rec("Update the application, then use Repair/Reset from its page in the Application Library.")
             .Rec("Check the application's guide for known causes.")
             .Why("Recorded by Windows Error Reporting at the time of each crash.")
             .Unsure("The faulting module is where the crash surfaced; the root cause may be elsewhere (e.g. overlays injecting into the process).");
            yield return f;
        }

        var hangGroups = hangs.GroupBy(e => (e.Get("AppName") ?? AppFromMessage(e.Message) ?? "unknown").ToLowerInvariant()).Where(g => g.Count() >= 5).ToList();
        foreach (var g in hangGroups)
        {
            yield return New($"{g.First().Get("AppName") ?? g.Key} stopped responding {g.Count()} times", Severity.Low, Confidence.High,
                    "Windows recorded the application as hung (not responding) and it was closed.", key: $"{Id}:hang:{g.Key}")
                .Ev("Hangs", g.Count().ToString(), "Application event log (Application Hang 1002)")
                .Ev("Most recent", When(g.Max(e => e.Time)), "Application event log")
                .Cause("The application waits on disk, network, or a stuck add-on.", "Low memory or a slow system drive.")
                .Rec("Update the application and review its guide in the Application Library.")
                .Why("Recorded by Windows when the user closed a non-responding window.");
        }
    }

    [GeneratedRegex(@"Faulting application name:\s*([^,\r\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex AppNameRegex();

    private static string? AppFromMessage(string msg)
    {
        var m = AppNameRegex().Match(msg);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }
}

internal sealed partial class GpuDriverResetRule : Rule
{
    public override string Id => "stability.gpu-driver-resets";
    public override string Name => "Graphics driver timeouts (TDR)";
    public override FindingCategory Category => FindingCategory.Drivers;

    public override IEnumerable<Finding> Evaluate(SystemSnapshot s, DiagnosticContext ctx)
    {
        var tdr = s.Reliability.Find("Display", 4101).ToList();
        var live = s.Reliability.Events.Where(e => e.Id == 1001 && e.Provider.Contains("Windows Error Reporting", StringComparison.OrdinalIgnoreCase)
                                                   && e.Message.Contains("LiveKernelEvent", StringComparison.OrdinalIgnoreCase)
                                                   && (e.Message.Contains("141", StringComparison.Ordinal) || e.Message.Contains("117", StringComparison.Ordinal))).ToList();
        var lkr = s.Reliability.CrashDumps.Where(d => d.Kind == "LiveKernelReport").ToList();
        var count = tdr.Count + live.Count;
        if (count == 0 && lkr.Count == 0) yield break;
        var sev = count >= 5 ? Severity.High : Severity.Medium;
        var f = New($"The graphics driver stopped responding {Math.Max(count, lkr.Count)} time(s)", sev, Confidence.High,
            "Windows detected the GPU was not responding and reset the driver (screen goes black briefly, games crash, or 'Display driver stopped responding and has recovered').");
        foreach (var e in tdr.OrderByDescending(e => e.Time).Take(6))
            f.Ev("Display 4101", $"{When(e.Time)} — {DriverName(e.Message) ?? "display driver"} recovered", "System event log");
        foreach (var e in live.OrderByDescending(e => e.Time).Take(4))
            f.Ev("LiveKernelEvent", When(e.Time), "Application event log (Windows Error Reporting)");
        foreach (var d in lkr.Take(3)) f.Ev("Live kernel report", $"{Path.GetFileName(d.Path)} ({When(d.Created)})", @"C:\Windows\LiveKernelReports");
        f.Cause("Outdated or corrupted GPU driver.", "GPU overclock/undervolt instability.", "Overheating or insufficient power to the GPU.", "Faulty GPU hardware (if it persists after a clean driver install).")
         .Rec("Clean-install the current GPU driver from NVIDIA/AMD/Intel.")
         .Rec("Remove GPU overclocks and check GPU temperature under load in the Graphics section.")
         .Why("Logged by the Windows display subsystem when it resets the GPU.");
        yield return f;
    }

    [GeneratedRegex(@"Display driver (\S+) stopped", RegexOptions.IgnoreCase)]
    private static partial Regex Driver();

    private static string? DriverName(string msg) => Driver().Match(msg) is { Success: true } m ? m.Groups[1].Value : null;
}

internal sealed class ServiceFailureRule : Rule
{
    public override string Id => "stability.service-failures";
    public override string Name => "Services crashing or failing to start";
    public override FindingCategory Category => FindingCategory.Stability;

    public override IEnumerable<Finding> Evaluate(SystemSnapshot s, DiagnosticContext ctx)
    {
        var scm = s.Reliability.Events.Where(e => e.Provider.Equals("Service Control Manager", StringComparison.OrdinalIgnoreCase)
                                                  && e.Id is 7000 or 7001 or 7009 or 7011 or 7023 or 7024 or 7031 or 7034).ToList();
        var groups = scm.GroupBy(e => (e.Get("param1") ?? e.Message.Split('\n')[0]).Trim()).Where(g => g.Count() >= 3).OrderByDescending(g => g.Count()).Take(6).ToList();
        if (groups.Count == 0) yield break;
        var f = New($"{groups.Count} service(s) repeatedly failed", groups.Max(g => g.Count()) >= 10 ? Severity.Medium : Severity.Low, Confidence.Medium,
            "Windows services crashed or failed to start several times. Some failures are harmless (e.g. a vendor service for hardware that is not present); others break features.");
        foreach (var g in groups)
            f.Ev("Service", $"{g.Key} — {g.Count()} events (IDs {string.Join(", ", g.Select(e => e.Id).Distinct())}), last {When(g.Max(e => e.Time))}", "System event log (Service Control Manager)");
        f.Cause("A third-party service is broken or left over from removed software.", "A Windows component is damaged.")
         .Rec("Identify the owning application in Services and update, repair, or uninstall it.")
         .Rec("If the failing services belong to Windows, repair system files.", ActionIds.Sfc)
         .Shortcut("Open the Services console.", "services.msc", "Open Services")
         .Why("Logged by the Service Control Manager.")
         .Unsure("Some services fail at boot by design when their hardware or dependency is missing; check the service's purpose before acting.");
        yield return f;
    }
}

internal sealed class LongUptimeRule : Rule
{
    public override string Id => "stability.uptime";
    public override string Name => "Pending restart";
    public override FindingCategory Category => FindingCategory.Updates;

    public override IEnumerable<Finding> Evaluate(SystemSnapshot s, DiagnosticContext ctx)
    {
        if (s.Updates.RebootPending != true) yield break;
        var f = New("A restart is pending", Severity.Medium, Confidence.High,
            "Windows has installed updates or changes that only take effect after a restart. Until then some updates are incomplete and further updates may fail.");
        foreach (var r in s.Updates.RebootPendingReasons) f.Ev("Pending because", r, "Registry");
        if (s.Os.Uptime(ctx.Now) is { } up) f.Ev("Uptime", $"{up.TotalDays:0.#} days", "WMI Win32_OperatingSystem");
        f.Cause("Updates or driver installs are waiting for a restart.")
         .Rec("Restart Windows before running further fixes, then re-run diagnostics.")
         .Why("Read from the registry keys Windows uses to track pending restarts.");
        yield return f;
    }
}

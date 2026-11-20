using System.Diagnostics;
using SoftReset.Core.Diagnostics;
using SoftReset.Core.Knowledge;
using SoftReset.Core.Optimization;
using SoftReset.Core.Platform;
using SoftReset.Core.Reports;
using SoftReset.Core.Safety;
using SoftReset.Core.Storage;

namespace SoftReset.App.Services;

/// <summary>
/// Read-only end-to-end checks against the real platform (SoftResetConsole.exe --self-test &lt;folder&gt;).
/// Nothing on the PC is changed. Used to validate the Windows build on a real Windows machine.
/// </summary>
public static class SelfTest
{
    public sealed class Check
    {
        public string Name { get; set; } = "";
        public bool Passed { get; set; }
        public string Detail { get; set; } = "";
        public double Seconds { get; set; }
    }

    public static int Run(string outDir, bool demo)
    {
        Directory.CreateDirectory(outDir);
        var checks = new List<Check>();
        var log = new List<string>();
        void Do(string name, Func<string> body)
        {
            var sw = Stopwatch.StartNew();
            var c = new Check { Name = name };
            try { c.Detail = body(); c.Passed = true; }
            catch (Exception ex) { c.Detail = ex.ToString(); c.Passed = false; }
            c.Seconds = Math.Round(sw.Elapsed.TotalSeconds, 2);
            checks.Add(c);
            log.Add($"[{(c.Passed ? "PASS" : "FAIL")}] {name} ({c.Seconds}s): {c.Detail.Split('\n')[0]}");
        }

        ISystemPlatform platform = null!;
        Do("Create platform", () => { platform = Program.CreatePlatform(demo); return $"{platform.Name}, build {platform.OsBuild}, elevated={platform.IsElevated}"; });
        if (platform is null) return Finish(outDir, checks, log);

        Core.Models.SystemSnapshot? snap = null;
        Do("Collect snapshot", () =>
        {
            snap = platform.Collector.CollectAsync(new CollectionOptions { CpuSampleDuration = TimeSpan.FromSeconds(3) }, null, CancellationToken.None).GetAwaiter().GetResult();
            Json.WriteFile(Path.Combine(outDir, "snapshot.json"), snap);
            return $"{snap.Os.FriendlyName}; CPU {snap.Hardware.CpuName}; RAM {snap.Hardware.TotalMemoryBytes / 1073741824.0:0.#} GB; {snap.ProcessCount} processes; {snap.StartupEntries.Count} startup entries; " +
                   $"{snap.Reliability.Events.Count} events; {snap.Gpus.Count} GPU(s); {snap.Displays.Count} display(s); {snap.PhysicalDisks.Count} disk(s); {snap.CollectionIssues.Count} collection note(s) in {snap.CollectionDuration.TotalSeconds:0.#}s";
        });
        if (snap is not null)
        {
            Do("Collection issues (informational)", () => string.Join(" | ", snap.CollectionIssues.Select(i => $"{i.Area}: {i.Message}")));
            Do("Diagnostics", () =>
            {
                var report = new DiagnosticEngine().Analyze(snap);
                Json.WriteFile(Path.Combine(outDir, "diagnostics.json"), report.Findings);
                if (report.RuleErrors.Count > 0) throw new Exception("Rule errors: " + string.Join("; ", report.RuleErrors.Select(e => $"{e.RuleId}: {e.Message}")));
                File.WriteAllBytes(Path.Combine(outDir, "diagnostic-report.pdf"), ReportBuilder.BuildDiagnosticReport(report));
                return $"{report.RulesEvaluated} rules, {report.Findings.Count} findings, health {report.HealthScore}: " + string.Join("; ", report.Findings.Take(12).Select(f => $"[{f.Severity}] {f.Title}"));
            });
        }

        Do("Settings catalog read", () =>
        {
            var reader = new SettingsReader(platform, platform.Registry.GetValue(RegHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID")?.AsString() ?? "");
            var states = KnowledgeBase.Default.Settings.Select(reader.Evaluate).ToList();
            var readable = states.Count(s => s.Current.Readable);
            var na = states.Count(s => s.Availability != SettingAvailability.Available);
            var errors = states.Where(s => s.Current.Note?.StartsWith("Could not read", StringComparison.Ordinal) == true).Select(s => $"{s.Definition.Id}: {s.Current.Note}").ToList();
            Json.WriteFile(Path.Combine(outDir, "settings.json"), states.Select(s => new { s.Definition.Id, s.Definition.Name, s.Availability, s.Current.Readable, s.Current.Display, s.Current.Note, s.PolicyNote }));
            if (errors.Count > 0) throw new Exception($"{errors.Count} read errors: " + string.Join("; ", errors.Take(10)));
            return $"{states.Count} settings: {readable} readable, {na} not applicable to this PC";
        });

        Do("Process details and signatures", () =>
        {
            var procs = platform.Processes.Enumerate();
            var withPath = procs.Where(p => p.Path is not null).GroupBy(p => p.Path!).Select(g => g.First()).Take(40).ToList();
            var details = withPath.Select(p => (p.Name, platform.Processes.GetFileDetails(p.Path!, true))).ToList();
            var notepad = Path.Combine(platform.Folders.System32, "notepad.exe");
            var np = File.Exists(notepad) ? platform.Processes.GetFileDetails(notepad, true).Signature.ToString() : "absent";
            var explorer = platform.Processes.GetFileDetails(Path.Combine(platform.Folders.WindowsDir, "explorer.exe"), true);
            return $"{procs.Count} processes, {procs.Count(p => p.Path is not null)} with paths; signatures: " +
                   string.Join(", ", details.GroupBy(d => d.Item2.Signature).Select(g => $"{g.Key}={g.Count()}")) + $"; notepad.exe={np}; explorer.exe={explorer.Signature} ({explorer.Signer})";
        });

        Do("Installed applications", () =>
        {
            var apps = platform.Apps.GetInstalledApps();
            var features = platform.Apps.GetOptionalFeatures();
            var guides = apps.Count(a => KnowledgeBase.Default.FindAppGuide(a) is not null);
            return $"{apps.Count} apps ({apps.Count(a => a.Kind == AppKind.Store)} Store, {apps.Count(a => a.Kind == AppKind.Desktop)} desktop), {guides} with guides; {features.Count} optional features";
        });

        Do("Graphics", () =>
        {
            var gpus = platform.Gpu.GetGpus(true);
            var nv = platform.Gpu.ReadNvidiaDriverSettingsAsync(CancellationToken.None).GetAwaiter().GetResult();
            var vendor = platform.Gpu.GetVendorSoftware();
            return string.Join("; ", gpus.Select(g => $"{g.Name} [{g.Vendor}] driver {g.VendorDriverVersion ?? g.DriverVersion} temp {g.Telemetry?.TemperatureC?.ToString() ?? "n/a"}")) +
                   $" | NVIDIA DRS: {nv.Status} ({nv.GlobalSettings.Count} settings) | NVCP: {vendor.NvidiaControlPanel ?? "none"}";
        });

        Do("Displays", () => string.Join("; ", platform.Display.GetDisplays().Select(d => $"{d.DeviceName} {d.MonitorName} {d.Width}x{d.Height}@{d.RefreshHz} (max {d.MaxRefreshHzAtCurrentResolution}) on {d.AdapterName}, {d.SupportedModes.Count} modes")));
        Do("Power", () => { var p = platform.Power.GetStatus(); return $"{p.ActiveSchemeName} ({p.Schemes.Count} plans), AC={p.OnAcPower}, max CPU {p.ProcessorMaxStateAc}%, hibernation={p.HibernationEnabled}, modern standby={p.ModernStandby}"; });
        Do("Services and tasks", () => $"{platform.Services.GetServices().Count} services; {platform.Tasks.GetTasks().Count} scheduled tasks");
        Do("Network", () => { var n = platform.Network.GetStatus(false); return $"{n.Adapters.Count} adapters ({n.Adapters.Count(a => a.IsUp)} up), proxy={n.ProxyEnabled}, hosts entries={n.HostsFileEntryCount}"; });
        Do("Live metrics", () => { platform.Live.Sample(); Thread.Sleep(500); var s = platform.Live.Sample(); return $"CPU {s.CpuPercent:0.#}%, memory {s.MemoryUsedPercent:0.#}%"; });

        Do("Optimization actions: Inspect (read-only)", () =>
        {
            var paths = new AppPaths(Path.Combine(outDir, "data"), false);
            paths.EnsureCreated();
            var ctx = new ActionContext { Platform = platform, Journal = new ChangeJournal(paths.Journal), Paths = paths };
            var results = new List<string>();
            var failures = new List<string>();
            foreach (var a in ActionRegistry.CreateDefault().All.Where(a => !a.Internal && a.Id != Core.Diagnostics.ActionIds.WingetUpgrade))
            {
                try
                {
                    var req = new ActionRequest { ActionId = a.Id, Parameters = a.Id == Core.Diagnostics.ActionIds.ClearAppCache ? new() { ["app"] = "discord" } : new() };
                    var insp = a.InspectAsync(ctx, req, CancellationToken.None).GetAwaiter().GetResult();
                    results.Add($"{a.Id}: {(insp.Applicable ? insp.CurrentState : "n/a — " + insp.NotApplicableReason)}");
                }
                catch (Exception ex) { failures.Add($"{a.Id}: {ex.GetType().Name}: {ex.Message}"); }
            }
            File.WriteAllLines(Path.Combine(outDir, "action-inspections.txt"), results.Concat(failures.Select(f => "FAILED " + f)));
            if (failures.Count > 0) throw new Exception(string.Join("; ", failures));
            return $"{results.Count} actions inspected without errors";
        });

        return Finish(outDir, checks, log);
    }

    private static int Finish(string outDir, List<Check> checks, List<string> log)
    {
        Json.WriteFile(Path.Combine(outDir, "self-test.json"), checks);
        File.WriteAllLines(Path.Combine(outDir, "self-test.log"), log);
        return checks.All(c => c.Passed) ? 0 : 2;
    }
}

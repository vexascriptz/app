using SoftReset.Core.Diagnostics;
using SoftReset.Core.Knowledge;
using SoftReset.Core.Models;
using SoftReset.Core.Optimization;
using SoftReset.Core.Platform;
using SoftReset.Core.Safety;
using SoftReset.Core.Startup;
using SoftReset.Core.Storage;
using SoftReset.Platform.Windows;

namespace SoftReset.Windows.Tests;

/// <summary>Read-only checks of every Windows integration against the real OS.</summary>
public class PlatformTests
{
    private static readonly Lazy<WindowsPlatform> Shared = new(() => new WindowsPlatform());
    private static WindowsPlatform P => Shared.Value;

    private static readonly Lazy<SystemSnapshot> Snapshot = new(() =>
        P.Collector.CollectAsync(new CollectionOptions { CpuSampleDuration = TimeSpan.FromSeconds(2) }, null, CancellationToken.None).GetAwaiter().GetResult());

    [WindowsFact]
    public void Platform_reports_a_supported_windows_build()
    {
        Assert.True(P.IsWindows);
        Assert.True(P.OsBuild >= 17763, $"Build {P.OsBuild}");
    }

    [WindowsFact]
    public void Snapshot_collection_reads_real_hardware_and_os_data()
    {
        var s = Snapshot.Value;
        Assert.False(string.IsNullOrWhiteSpace(s.Os.FriendlyName));
        Assert.False(string.IsNullOrWhiteSpace(s.Hardware.CpuName));
        Assert.True(s.Hardware.TotalMemoryBytes > 1L << 30);
        Assert.NotNull(s.SystemDrive);
        Assert.True(s.SystemDrive!.TotalBytes > 0);
        Assert.True(s.ProcessCount > 20);
        Assert.NotEmpty(s.Drives);
        Assert.NotEmpty(s.TopProcesses);
        Assert.True(s.CollectionDuration < TimeSpan.FromMinutes(3), $"Collection took {s.CollectionDuration}");
    }

    [WindowsFact]
    public void Diagnostic_rules_run_on_real_data_without_errors()
    {
        var report = new DiagnosticEngine().Analyze(Snapshot.Value);
        Assert.Empty(report.RuleErrors);
        Assert.InRange(report.HealthScore, 0, 100);
        Assert.All(report.Findings, f => Assert.NotEmpty(f.Evidence));
    }

    [WindowsFact]
    public void Every_catalogued_setting_reads_without_errors()
    {
        var edition = P.Registry.GetValue(RegHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID")?.AsString() ?? "";
        var reader = new SettingsReader(P, edition);
        var states = KnowledgeBase.Default.Settings.Select(reader.Evaluate).ToList();
        var errors = states.Where(s => s.Current.Note?.StartsWith("Could not read", StringComparison.Ordinal) == true).Select(s => $"{s.Definition.Id}: {s.Current.Note}").ToList();
        Assert.True(errors.Count == 0, string.Join("\n", errors));
        Assert.True(states.Count(s => s.Current.Readable) > states.Count / 2, "Fewer than half the settings were readable.");
    }

    [WindowsFact]
    public void Windows_binaries_verify_as_microsoft_signed()
    {
        var explorer = Path.Combine(P.Folders.WindowsDir, "explorer.exe");
        var d = P.Processes.GetFileDetails(explorer, true);
        Assert.Equal(SignatureState.SignedMicrosoftWindows, d.Signature);
        Assert.Contains("Microsoft", d.Signer ?? "");
    }

    [WindowsFact]
    public void Unsigned_files_are_reported_unsigned_not_trusted()
    {
        var path = Path.Combine(Path.GetTempPath(), $"softreset-unsigned-{Guid.NewGuid():N}.exe");
        File.WriteAllBytes(path, new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03 });
        try
        {
            var d = P.Processes.GetFileDetails(path, true);
            Assert.NotEqual(SignatureState.Signed, d.Signature);
            Assert.NotEqual(SignatureState.SignedMicrosoftWindows, d.Signature);
        }
        finally { File.Delete(path); }
    }

    [WindowsFact]
    public void Processes_services_and_tasks_enumerate()
    {
        var procs = P.Processes.Enumerate();
        Assert.Contains(procs, p => p.Pid == Environment.ProcessId);
        Assert.Contains(P.Services.GetServices(), s => s.Name.Equals("Winmgmt", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(P.Tasks.GetTasks());
        Assert.NotNull(P.Processes.GetServicesByPid());
    }

    [WindowsFact]
    public void Startup_inventory_and_installed_apps_read()
    {
        var startup = new StartupInventory(P.Registry, P.Folders, ((ISystemPlatform)P).FileExists).Enumerate(P.Tasks.GetTasks());
        Assert.NotNull(startup);
        var apps = P.Apps.GetInstalledApps();
        Assert.NotEmpty(apps);
        Assert.All(apps, a => Assert.False(string.IsNullOrWhiteSpace(a.Name)));
    }

    [WindowsFact]
    public void Power_display_network_and_gpu_read()
    {
        var power = P.Power.GetStatus();
        Assert.NotEmpty(power.Schemes);
        Assert.False(string.IsNullOrWhiteSpace(power.ActiveSchemeGuid));
        _ = P.Display.GetDisplays(); // headless CI runners may have none; must not throw
        Assert.NotNull(P.Network.GetStatus(false));
        Assert.NotNull(P.Gpu.GetGpus(true));
        var nv = P.Gpu.ReadNvidiaDriverSettingsAsync(CancellationToken.None).GetAwaiter().GetResult();
        Assert.False(string.IsNullOrWhiteSpace(nv.Status)); // "not available" is a valid answer without an NVIDIA GPU
    }

    [WindowsFact]
    public void Live_metrics_return_plausible_values()
    {
        P.Live.Sample();
        Thread.Sleep(400);
        var s = P.Live.Sample();
        Assert.InRange(s.CpuPercent, 0, 100);
        Assert.InRange(s.MemoryUsedPercent, 1, 100);
    }

    [WindowsFact]
    public async Task Every_optimization_action_inspects_without_changing_anything()
    {
        var root = Path.Combine(Path.GetTempPath(), "softreset-wt-" + Guid.NewGuid().ToString("N")[..8]);
        var paths = new AppPaths(root, false);
        paths.EnsureCreated();
        var ctx = new ActionContext { Platform = P, Journal = new ChangeJournal(paths.Journal), Paths = paths };
        var failures = new List<string>();
        foreach (var a in ActionRegistry.CreateDefault().All.Where(a => !a.Internal && a.Id != ActionIds.WingetUpgrade))
        {
            try
            {
                var req = new ActionRequest { ActionId = a.Id, Parameters = a.Id == ActionIds.ClearAppCache ? new() { ["app"] = "discord" } : new() };
                var insp = await a.InspectAsync(ctx, req, CancellationToken.None);
                if (insp.Applicable) await a.PreviewAsync(ctx, req, CancellationToken.None);
            }
            catch (Exception ex) { failures.Add($"{a.Id}: {ex.GetType().Name}: {ex.Message}"); }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
        Assert.Empty(ctx.Journal.LoadAll()); // inspection and preview never record or change anything
        Directory.Delete(root, true);
    }
}

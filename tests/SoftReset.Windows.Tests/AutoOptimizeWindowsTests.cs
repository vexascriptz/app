using SoftReset.Core.Diagnostics;
using SoftReset.Core.Optimization;
using SoftReset.Core.Platform;
using SoftReset.Core.Safety;
using SoftReset.Core.Storage;
using SoftReset.Platform.Windows;
using Xunit.Abstractions;

namespace SoftReset.Windows.Tests;

/// <summary>Auto optimize against a real Windows installation.</summary>
public class AutoOptimizeWindowsTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "softreset-auto-" + Guid.NewGuid().ToString("N")[..8]);

    public AutoOptimizeWindowsTests(ITestOutputHelper output) => _out = output;

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private (WindowsPlatform P, ActionContext Ctx, ActionExecutor Exec, AutoOptimizer Auto) Rig()
    {
        var p = new WindowsPlatform();
        var paths = new AppPaths(_root, false);
        paths.EnsureCreated();
        var ctx = new ActionContext { Platform = p, Journal = new ChangeJournal(paths.Journal), Paths = paths };
        var registry = ActionRegistry.CreateDefault();
        return (p, ctx, new ActionExecutor(registry, ctx), new AutoOptimizer(registry, ctx));
    }

    [WindowsFact]
    public async Task Plan_builds_from_the_real_pc_without_errors_and_changes_nothing()
    {
        var (p, ctx, _, auto) = Rig();
        var hw = HardwareProfile.Detect(p);
        _out.WriteLine($"Hardware: {hw.TotalMemoryBytes >> 20} MB RAM, {hw.LogicalProcessors} threads, laptop={hw.IsLaptop}, dGPU={hw.HasDedicatedGpu}, low-end={hw.IsLowEnd}");
        var plan = await auto.BuildAsync(AutoScope.Everything, hw);
        foreach (var s in plan.Steps) _out.WriteLine($"PLAN  {s.Title} — {s.Detail} (admin={s.RequiresAdmin}, undo={s.Reversibility})");
        foreach (var g in plan.AlreadyGood) _out.WriteLine($"GOOD  {g}");
        Assert.All(plan.Steps, s => Assert.False(string.IsNullOrWhiteSpace(s.Title)));
        Assert.Empty(ctx.Journal.LoadAll());
        if (!hw.IsLowEnd) Assert.DoesNotContain(plan.Steps, s => s.Request.ActionId is ActionIds.VisualEffects or ActionIds.Transparency);
    }

    [WindowsChangeFact]
    public async Task Auto_optimize_applies_on_real_windows_and_undo_all_restores_it()
    {
        var (p, ctx, exec, auto) = Rig();
        var plan = await auto.BuildAsync(AutoScope.Everything, HardwareProfile.Detect(p));
        // Settings and power are compared before/after; display changes are left out on a headless runner.
        plan.Steps.RemoveAll(s => s.IsDisplayChange);
        var schemeBefore = p.Power.GetStatus().ActiveSchemeGuid;
        var settingsBefore = AutoOptimizer.RecommendedSettings.Keys.ToDictionary(id => id, id => ReadSetting(ctx, id));

        var records = await auto.ApplyAsync(plan, exec);
        foreach (var r in records) _out.WriteLine($"APPLY {r.Title}: {r.Status} {r.Message} {r.VerificationMessage}");
        Assert.DoesNotContain(records, r => r.Status is ChangeStatus.Failed or ChangeStatus.Cancelled or ChangeStatus.Pending);
        Assert.Contains(records, r => r.Status == ChangeStatus.Verified);

        // Running it again finds nothing left to change.
        var again = await auto.BuildAsync(AutoScope.Everything, HardwareProfile.Detect(p));
        again.Steps.RemoveAll(s => s.IsDisplayChange);
        foreach (var s in again.Steps) _out.WriteLine($"AGAIN {s.Title} — {s.Detail}");
        Assert.Equal(0, again.ChangeCount);

        var undone = await exec.RollbackManyAsync(records.Select(r => r.Id).ToList());
        foreach (var r in undone) _out.WriteLine($"UNDO  {r.Title}: {r.Status} {r.RollbackMessage}");
        Assert.All(undone, r => Assert.Equal(ChangeStatus.RolledBack, r.Status));
        Assert.Equal(schemeBefore, p.Power.GetStatus().ActiveSchemeGuid, StringComparer.OrdinalIgnoreCase);
        foreach (var (id, before) in settingsBefore) Assert.Equal(before, ReadSetting(ctx, id));
    }

    private static string? ReadSetting(ActionContext ctx, string id)
    {
        var def = ctx.Knowledge.FindSetting(id);
        if (def?.Read.Hive is null) return null;
        return ctx.Platform.Registry.GetValue(Core.Safety.RegistryBackup.ParseHive(def.Read.Hive), def.Read.Path!, def.Read.Value!)?.ToString();
    }
}

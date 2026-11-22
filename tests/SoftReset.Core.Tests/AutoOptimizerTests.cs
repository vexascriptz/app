using SoftReset.Core.Diagnostics;
using SoftReset.Core.Models;
using SoftReset.Core.Platform;
using SoftReset.Core.Optimization;
using SoftReset.Core.Safety;
using SoftReset.Core.Startup;

namespace SoftReset.Core.Tests;

public class AutoOptimizerTests
{
    private static readonly HardwareProfile GamingDesktop = new() { TotalMemoryBytes = 16L << 30, LogicalProcessors = 16, IsLaptop = false, HasDedicatedGpu = true };
    private static readonly HardwareProfile WeakLaptop = new() { TotalMemoryBytes = 4L << 30, LogicalProcessors = 2, IsLaptop = true, HasDedicatedGpu = false };

    private static List<StartupEntry> Startup(TestRig rig) =>
        new StartupInventory(rig.Platform.Registry, rig.Platform.Folders, rig.Platform.FileExists).Enumerate();

    [Fact]
    public async Task Plan_for_a_gaming_desktop_targets_real_gains_and_keeps_the_looks()
    {
        using var rig = new TestRig();
        var plan = await new AutoOptimizer(rig.Registry, rig.Context).BuildAsync(AutoScope.Everything, GamingDesktop);
        var ids = plan.Steps.Select(s => s.Request.ActionId).ToList();

        Assert.Contains(ActionIds.PowerPlan, ids);                                           // power saver on a desktop
        Assert.Equal(WellKnownPowerSchemes.HighPerformance, plan.Steps.First(s => s.Request.ActionId == ActionIds.PowerPlan).Request.Parameters["scheme"]);
        Assert.Contains(ActionIds.RefreshRate, ids);                                         // 165 Hz monitor stuck at 60
        Assert.Contains(ActionIds.StartupUserItems, ids);
        Assert.Contains(ActionIds.SettingRegistrySet, ids);
        Assert.DoesNotContain(ActionIds.VisualEffects, ids);                                 // strong hardware: looks are kept
        Assert.DoesNotContain(ActionIds.Transparency, ids);
        Assert.DoesNotContain(ActionIds.RecycleBin, ids);                                    // never deletes the user's files
        Assert.DoesNotContain(ActionIds.EndProcesses, ids);
        Assert.Equal(ActionIds.RestorePoint, plan.Steps[0].Request.ActionId);                // safety first
        Assert.True(plan.ChangeCount >= 5, $"Only {plan.ChangeCount} changes planned");
    }

    [Fact]
    public async Task Startup_cleanup_disables_launchers_and_leftovers_but_keeps_chat_sync_drivers_and_security()
    {
        using var rig = new TestRig();
        var plan = await new AutoOptimizer(rig.Registry, rig.Context).BuildAsync(AutoScope.Startup, GamingDesktop);
        var chosen = plan.Steps.SelectMany(s => s.Request.SelectedItems).ToHashSet();
        var entries = Startup(rig).ToDictionary(e => e.Id);
        var names = chosen.Where(entries.ContainsKey).Select(id => entries[id].Name).ToList();

        Assert.Contains(names, n => n.StartsWith("Steam"));
        Assert.Contains(names, n => n.StartsWith("EpicGamesLauncher"));
        Assert.Contains(names, n => n.StartsWith("MicrosoftEdgeAutoLaunch"));
        Assert.Contains(names, n => n.StartsWith("Opera GX"));          // leftover pointing at a deleted program
        Assert.Contains(names, n => n.StartsWith("CCleaner"));
        foreach (var keep in new[] { "Discord", "OneDrive", "SecurityHealth", "RtkAudUService", "Corsair iCUE5 Software", "Razer Synapse" })
            Assert.DoesNotContain(names, n => n.StartsWith(keep));
    }

    [Fact]
    public async Task Weak_hardware_trades_animations_for_speed_but_keeps_shadows_and_window_dragging()
    {
        using var rig = new TestRig();
        var plan = await new AutoOptimizer(rig.Registry, rig.Context).BuildAsync(AutoScope.Everything, WeakLaptop);
        var fx = plan.Steps.Single(s => s.Request.ActionId == ActionIds.VisualEffects);
        Assert.Contains(UiEffects.MenuAnimation, fx.Request.SelectedItems);
        Assert.DoesNotContain(UiEffects.DropShadow, fx.Request.SelectedItems);
        Assert.DoesNotContain(UiEffects.DragFullWindows, fx.Request.SelectedItems);
        Assert.Contains(plan.Steps, s => s.Request.ActionId == ActionIds.Transparency);
        // Laptops leave Power saver for Balanced, not High performance.
        Assert.Equal(WellKnownPowerSchemes.Balanced, plan.Steps.First(s => s.Request.ActionId == ActionIds.PowerPlan).Request.Parameters["scheme"]);
        Assert.DoesNotContain(plan.Steps, s => s.Request.ActionId == ActionIds.Hags); // no dedicated GPU
    }

    [Fact]
    public async Task Applying_uses_one_permission_prompt_changes_the_pc_and_undo_all_restores_it()
    {
        using var rig = new TestRig(elevated: false);
        var optimizer = new AutoOptimizer(rig.Registry, rig.Context);
        var plan = await optimizer.BuildAsync(AutoScope.Everything, GamingDesktop);
        var enabledBefore = Startup(rig).Where(e => e.Enabled).Select(e => e.Id).ToHashSet();
        var settingBefore = rig.Platform.Registry.GetValue(Platform.RegHive.CurrentUser, @"Control Panel\Desktop", "MenuShowDelay")?.ToString();

        var records = await optimizer.ApplyAsync(plan, rig.Executor);

        Assert.Equal(plan.Steps.Count, records.Count);
        Assert.Equal(1, rig.Platform.ElevationPromptCount);
        Assert.All(records.Where(r => r.ActionId != ActionIds.RestorePoint), r => Assert.True(r.Status == ChangeStatus.Verified, $"{r.Title}: {r.Status} {r.Message} {r.VerificationMessage}"));
        Assert.Contains("High performance", rig.Platform.Power.GetStatus().ActiveSchemeName); // created from the template if it was missing
        Assert.Equal(165, rig.Platform.Display.GetDisplays()[0].RefreshHz);
        Assert.True(Startup(rig).Count(e => e.Enabled) < enabledBefore.Count);
        Assert.Equal(1, rig.Platform.State.RestorePointsCreated);

        // Running it again finds nothing left to do.
        var again = await optimizer.BuildAsync(AutoScope.Everything, GamingDesktop);
        Assert.True(again.ChangeCount == 0, "Still planned: " + string.Join(" | ", again.Steps.Select(x => $"{x.Title} [{x.Detail}]")));

        // Undo all: one more prompt, everything reversible goes back.
        var undone = await rig.Executor.RollbackManyAsync(records.Select(r => r.Id).ToList());
        Assert.All(undone, r => Assert.Equal(ChangeStatus.RolledBack, r.Status));
        Assert.Equal(2, rig.Platform.ElevationPromptCount);
        Assert.Equal(WellKnownPowerSchemes.PowerSaver, rig.Platform.Power.GetStatus().ActiveSchemeGuid, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(60, rig.Platform.Display.GetDisplays()[0].RefreshHz);
        Assert.Equal(enabledBefore, Startup(rig).Where(e => e.Enabled).Select(e => e.Id).ToHashSet());
        Assert.Equal(settingBefore, rig.Platform.Registry.GetValue(Platform.RegHive.CurrentUser, @"Control Panel\Desktop", "MenuShowDelay")?.ToString());
    }

    [Fact]
    public async Task Declining_the_permission_prompt_still_applies_the_changes_that_need_no_admin()
    {
        using var rig = new TestRig(elevated: false);
        var optimizer = new AutoOptimizer(rig.Registry, rig.Context);
        var plan = await optimizer.BuildAsync(AutoScope.Everything, GamingDesktop);
        rig.Platform.SimulateUacDecline = true;
        var records = await optimizer.ApplyAsync(plan, rig.Executor);
        Assert.Contains(records, r => r.Status == ChangeStatus.Cancelled);
        Assert.Contains(records, r => r.Status == ChangeStatus.Verified);
        Assert.All(records.Where(r => r.Status == ChangeStatus.Cancelled), r => Assert.True(r.Elevated));
    }

    [Theory]
    [InlineData("Steam", @"""C:\Program Files (x86)\Steam\steam.exe"" -silent", true, true)]
    [InlineData("Discord", @"""C:\Users\a\AppData\Local\Discord\Update.exe""", true, false)]
    [InlineData("OneDrive", @"""C:\Users\a\AppData\Local\Microsoft\OneDrive\OneDrive.exe"" /background", true, false)]
    [InlineData("SecurityHealth", @"%windir%\system32\SecurityHealthSystray.exe", true, false)]
    [InlineData("Some unknown tool", @"C:\Tools\thing.exe", true, false)]          // unknown → left alone
    [InlineData("Old helper", @"C:\Gone\helper.exe", false, true)]                // missing program → off
    public void Startup_policy(string name, string command, bool exists, bool disable) =>
        Assert.Equal(disable, AutoOptimizePolicy.ShouldDisableStartup(name, command, exists));

    [Fact]
    public void Browser_and_security_updater_tasks_are_never_disabled()
    {
        Assert.False(AutoOptimizePolicy.ShouldDisableTask("GoogleUpdateTaskMachineUA", @"C:\Program Files\Google\Update\GoogleUpdate.exe", true));
        Assert.False(AutoOptimizePolicy.ShouldDisableTask("MicrosoftEdgeUpdateTaskMachineCore", @"C:\Program Files (x86)\Microsoft\EdgeUpdate\MicrosoftEdgeUpdate.exe", true));
        Assert.True(AutoOptimizePolicy.ShouldDisableTask("Opera GX scheduled assistant", @"C:\Users\a\AppData\Local\Programs\Opera GX\launcher.exe", true));
    }

    [Fact]
    public void Recommended_settings_exist_and_never_touch_security_or_accessibility()
    {
        foreach (var (id, label) in AutoOptimizer.RecommendedSettings)
        {
            var def = Knowledge.KnowledgeBase.Default.FindSetting(id);
            Assert.NotNull(def);
            Assert.Contains(def!.Controls, c => c.Label == label);
            Assert.DoesNotContain(def.Category, new[] { "Security", "Accessibility", "Windows Update", "Accounts" });
        }
    }
}

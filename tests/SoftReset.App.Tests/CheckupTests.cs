using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using SoftReset.Core.Diagnostics;

namespace SoftReset.App.Tests;

/// <summary>The Home check-up: every reading has a plain Good / Warm / Problem label, and problems have a button.</summary>
public class CheckupTests
{
    [AvaloniaFact]
    public async Task Home_checks_the_pc_on_open_and_labels_every_reading()
    {
        using var h = new UiHarness(height: 2000, elevated: true);
        await h.Vm.ActivateCurrentAsync();
        await h.WaitAsync(() => h.Vm.Dashboard.HasCheckup && !h.Vm.Dashboard.IsChecking, 30_000);
        h.Vm.Dashboard.Tick();
        h.Pump(60);
        h.Screenshot("17-checkup");

        var rows = h.Vm.Dashboard.Checkup.SelectMany(g => g.Items).ToList();
        Assert.Contains(h.Vm.Dashboard.Checkup, g => g.Title == PcCheckup.Temperatures);
        Assert.Contains(rows, r => r.Name.Contains("RTX") && r.Value.EndsWith("°C") && r.Rating.Level == RatingLevel.Good);
        Assert.Contains(rows, r => r.Name.Contains("Samsung") && r.Value.EndsWith("°C"));   // drive temperature (admin)

        // Every row on screen shows its label; problem rows have a button.
        var pills = h.Find<ItemsControl>("CheckupGroups").GetVisualDescendants().OfType<RatingPill>().ToList();
        Assert.Equal(rows.Count, pills.Count);
        Assert.All(pills, p => Assert.False(string.IsNullOrWhiteSpace(p.Text)));
        Assert.Contains(rows, r => r.ShowFix && r.FixLabel == "Fix");

        Assert.False(string.IsNullOrEmpty(h.Find<RatingPill>("CpuPill").Text));
        Assert.False(string.IsNullOrEmpty(h.Find<RatingPill>("MemoryPill").Text));
        Assert.False(string.IsNullOrEmpty(h.Find<RatingPill>("HealthPill").Text));
    }

    [AvaloniaFact]
    public async Task Fix_on_the_power_plan_line_fixes_it_and_the_line_turns_good()
    {
        using var h = new UiHarness(height: 2000);
        await h.Vm.ActivateCurrentAsync();
        await h.WaitAsync(() => h.Vm.Dashboard.HasCheckup && !h.Vm.Dashboard.IsChecking, 30_000);
        var power = h.Vm.Dashboard.Checkup.SelectMany(g => g.Items).Single(r => r.Name == "Power plan");
        Assert.True(power.Rating.IsProblem);

        power.FixCommand.Execute(null);
        await h.WaitAsync(() => h.Vm.CurrentDialog is ViewModels.AutoPlanDialogViewModel, 30_000);
        h.Click(h.Find<Button>("AutoOptimizeConfirmButton"));
        await h.WaitAsync(() => h.Vm.CurrentDialog is ViewModels.AutoResultDialogViewModel, 30_000);
        h.Click(h.Find<Button>("AutoOkButton"));
        await h.WaitAsync(() => h.Vm.Dashboard.Checkup.SelectMany(g => g.Items).Single(r => r.Name == "Power plan").Rating.Level == RatingLevel.Good, 30_000);
    }

    [AvaloniaFact]
    public async Task Without_admin_drive_temperatures_are_honestly_missing()
    {
        using var h = new UiHarness(height: 2000);
        await h.Vm.ActivateCurrentAsync();
        await h.WaitAsync(() => h.Vm.Dashboard.HasCheckup && !h.Vm.Dashboard.IsChecking, 30_000);
        var rows = h.Vm.Dashboard.Checkup.SelectMany(g => g.Items).ToList();
        Assert.DoesNotContain(rows, r => r.Name.Contains("Samsung") && r.Value.EndsWith("°C"));
        Assert.Contains(rows, r => r.Name == "CPU" && r.Rating.Level == RatingLevel.Unknown);
    }
}

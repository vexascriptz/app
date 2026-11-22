using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using SoftReset.App.ViewModels;
using SoftReset.Core.Models;
using SoftReset.Core.Startup;

namespace SoftReset.App.Tests;

/// <summary>The one-click flows, driven with real clicks against the simulated PC.</summary>
public class AutoOptimizeTests
{
    private static int EnabledStartup(UiHarness h) =>
        new StartupInventory(h.Platform.Registry, h.Platform.Folders, h.Platform.FileExists).Enumerate().Count(e => e.Enabled);

    [AvaloniaFact]
    public async Task Auto_optimize_from_home_changes_the_pc_with_one_permission_prompt_and_undo_all_puts_it_back()
    {
        using var h = new UiHarness();
        await h.Vm.ActivateCurrentAsync();
        var startupBefore = EnabledStartup(h);
        Assert.Equal(WellKnownPowerSchemes.PowerSaver, h.Platform.State.ActiveScheme);

        h.Click(h.Find<Button>("AutoOptimizeButton"));
        await h.WaitAsync(() => h.Vm.CurrentDialog is AutoPlanDialogViewModel, 30_000);
        var plan = (AutoPlanDialogViewModel)h.Vm.CurrentDialog!;
        Assert.True(plan.Items.Count >= 5);
        h.Screenshot("15-auto-plan");
        h.Click(h.Find<Button>("AutoOptimizeConfirmButton"));

        // The new refresh rate has to be confirmed on screen.
        await h.WaitAsync(() => h.Vm.CurrentDialog is KeepChangesDialogViewModel, 30_000);
        h.Click(h.Find<Button>("KeepButton"));
        await h.WaitAsync(() => h.Vm.CurrentDialog is AutoResultDialogViewModel, 30_000);
        h.Screenshot("16-auto-done");

        Assert.Equal(1, h.Platform.ElevationPromptCount);
        Assert.Contains("High performance", h.Platform.Power.GetStatus().ActiveSchemeName);
        Assert.Equal(165, h.Platform.Display.GetDisplays()[0].RefreshHz);
        Assert.True(EnabledStartup(h) < startupBefore);
        Assert.Equal(1, h.Platform.State.RestorePointsCreated);

        h.Click(h.Find<Button>("AutoUndoAllButton"));
        await h.WaitAsync(() => h.Vm.CurrentDialog is null && h.Platform.State.ActiveScheme == WellKnownPowerSchemes.PowerSaver, 30_000);
        Assert.Equal(60, h.Platform.Display.GetDisplays()[0].RefreshHz);
        Assert.Equal(startupBefore, EnabledStartup(h));
    }

    [AvaloniaFact]
    public async Task Running_auto_optimize_twice_says_already_optimized()
    {
        using var h = new UiHarness();
        await h.Vm.ActivateCurrentAsync();
        h.Click(h.Find<Button>("AutoOptimizeButton"));
        await h.AnswerDialogAsync<AutoPlanDialogViewModel>("AutoOptimizeConfirmButton", 30_000);
        await h.AnswerDialogAsync<KeepChangesDialogViewModel>("KeepButton", 30_000);
        await h.AnswerDialogAsync<AutoResultDialogViewModel>("AutoOkButton", 30_000);

        h.Click(h.Find<Button>("AutoOptimizeButton"));
        await h.WaitAsync(() => h.Vm.Toasts.Any(t => t.Text.StartsWith("Already optimized")), 30_000);
        Assert.Null(h.Vm.CurrentDialog);
    }

    [AvaloniaFact]
    public async Task Graphics_one_click_only_touches_graphics_and_gaming()
    {
        using var h = new UiHarness();
        await h.NavigateAsync(h.Vm.Graphics);
        await h.WaitAsync(() => h.Vm.Graphics.Gpus.Count > 0);
        var startupBefore = EnabledStartup(h);
        h.Click(h.Find<Button>("AutoGraphicsButton"));
        await h.WaitAsync(() => h.Vm.CurrentDialog is AutoPlanDialogViewModel, 30_000);
        var plan = (AutoPlanDialogViewModel)h.Vm.CurrentDialog!;
        Assert.DoesNotContain(plan.Items, i => i.Step.Request.ActionId.StartsWith("startup") || i.Step.Request.ActionId.StartsWith("power"));
        h.Click(h.Find<Button>("AutoOptimizeConfirmButton"));
        await h.AnswerDialogAsync<KeepChangesDialogViewModel>("KeepButton", 30_000);
        await h.AnswerDialogAsync<AutoResultDialogViewModel>("AutoOkButton", 30_000);
        Assert.Equal(165, h.Platform.Display.GetDisplays()[0].RefreshHz);
        Assert.Equal(startupBefore, EnabledStartup(h));
        Assert.Equal(WellKnownPowerSchemes.PowerSaver, h.Platform.State.ActiveScheme);
    }

    [AvaloniaFact]
    public async Task Every_setting_row_has_apply_or_open_and_apply_sets_the_recommended_value()
    {
        using var h = new UiHarness();
        await h.NavigateAsync(h.Vm.SettingsExplorer);
        await h.WaitAsync(() => h.Vm.SettingsExplorer.Items.Count > 0 && h.Vm.SettingsExplorer.Items.All(i => i.State is not null), 30_000);
        Assert.All(h.Vm.SettingsExplorer.Items, r => Assert.Contains(r.QuickLabel, new[] { "Apply", "Open" }));

        h.Type(h.Find<TextBox>("SettingsSearch"), "Tips and suggestions");
        await h.WaitAsync(() => h.Vm.SettingsExplorer.Items.Any(i => i.Definition.Id == "notifications.tips" && i.State is not null));
        var row = h.Vm.SettingsExplorer.Items.First(i => i.Definition.Id == "notifications.tips");
        Assert.Equal("Apply", row.QuickLabel);
        h.Click(h.All<Button>().First(b => b.IsEffectivelyVisible && "Apply".Equals(b.Content) && b.DataContext == row));
        await h.WaitAsync(() => h.Vm.CurrentDialog is ApprovalDialogViewModel);
        h.Click(h.Find<CheckBox>("ApproveCheck"));
        h.Click(h.Find<Button>("ApplyButton"));
        await h.WaitAsync(() => row.QuickLabel == "Open", 15_000);
    }

    [AvaloniaFact]
    public async Task Every_problem_has_a_fix_or_open_button_and_fix_all_rescans()
    {
        using var h = new UiHarness();
        h.Services.Settings.ScanSampleSeconds = 1;
        await h.NavigateAsync(h.Vm.Diagnostics);
        h.Click(h.Find<Button>("ScanButton"));
        await h.WaitAsync(() => h.Vm.Diagnostics.HasReport, 60_000);
        var before = h.Vm.Diagnostics.Report!.Findings.Count;
        Assert.All(h.Vm.Diagnostics.Findings, f => Assert.Contains(f.PrimaryLabel, new[] { "Fix", "Open" }));
        Assert.Contains(h.Vm.Diagnostics.Findings, f => f.PrimaryLabel == "Fix");

        // A problem the app can't fix opens the right Windows page.
        var manual = h.Vm.Diagnostics.Findings.FirstOrDefault(f => f.PrimaryLabel == "Open");
        if (manual is not null)
        {
            var launches = h.Platform.LaunchedTargets.Count;
            await manual.PrimaryCommand.ExecuteAsync(null);
            Assert.True(h.Platform.LaunchedTargets.Count > launches);
        }

        h.Click(h.Find<Button>("FixAllButton"));
        await h.AnswerDialogAsync<AutoPlanDialogViewModel>("AutoOptimizeConfirmButton", 30_000);
        await h.AnswerDialogAsync<KeepChangesDialogViewModel>("KeepButton", 30_000);
        await h.AnswerDialogAsync<AutoResultDialogViewModel>("AutoOkButton", 30_000);
        await h.WaitAsync(() => !h.Vm.Diagnostics.IsScanning && h.Vm.Diagnostics.Report!.Findings.Count < before, 60_000);
    }

    [AvaloniaFact]
    public async Task Undo_all_on_the_undo_page_restores_every_change()
    {
        using var h = new UiHarness();
        await h.NavigateAsync(h.Vm.Processes);
        var before = EnabledStartup(h);
        h.Click(h.Find<Button>("CleanStartupButton"));
        await h.AnswerDialogAsync<AutoPlanDialogViewModel>("AutoOptimizeConfirmButton", 30_000);
        await h.AnswerDialogAsync<AutoResultDialogViewModel>("AutoOkButton", 30_000);
        Assert.True(EnabledStartup(h) < before);

        await h.NavigateAsync(h.Vm.History);
        h.Vm.History.ReloadCommand.Execute(null);
        h.Click(h.Find<Button>("UndoAllButton"));
        await h.AnswerDialogAsync<ConfirmDialogViewModel>("ConfirmButton");
        await h.WaitAsync(() => EnabledStartup(h) == before, 30_000);
    }
}

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using SoftReset.App.ViewModels;
using SoftReset.Core.Diagnostics;
using SoftReset.Core.Models;
using SoftReset.Core.Platform;
using SoftReset.Core.Safety;

namespace SoftReset.App.Tests;

/// <summary>
/// End-to-end UI tests: the real window and view models, driven by headless mouse clicks and
/// keyboard input, against the simulated PC. Assertions check the simulated system state, so a
/// button that "looks" right but does nothing fails these tests.
/// </summary>
public class InteractionTests
{
    private static async Task SelectPowerPlanAsync(UiHarness h)
    {
        await h.NavigateAsync(h.Vm.Optimization);
        await h.Vm.Optimization.SelectActionAsync(ActionIds.PowerPlan);
        await h.WaitAsync(() => h.Vm.Optimization.Inspection is not null && !h.Vm.Optimization.Inspecting);
        Assert.True(h.Vm.Optimization.Applicable, h.Vm.Optimization.StateText);
    }

    [AvaloniaFact]
    public async Task Approved_change_is_applied_verified_and_can_be_undone()
    {
        using var h = new UiHarness();
        Assert.Equal(WellKnownPowerSchemes.PowerSaver, h.Platform.State.ActiveScheme);
        await SelectPowerPlanAsync(h);

        h.Click(h.Find<Button>("ApplyActionButton"));
        await h.WaitAsync(() => h.Vm.CurrentDialog is ApprovalDialogViewModel);
        h.Screenshot("11-approval-dialog");

        var apply = h.Find<Button>("ApplyButton");
        Assert.False(apply.IsEffectivelyEnabled); // nothing happens until the customer ticks approval
        h.Click(h.Find<CheckBox>("ApproveCheck"));
        Assert.True(apply.IsEffectivelyEnabled);
        h.Click(apply);

        await h.WaitAsync(() => h.Vm.Optimization.LastRecord is not null);
        var record = h.Vm.Optimization.LastRecord!;
        Assert.Equal(ChangeStatus.Verified, record.Status);
        Assert.NotEqual(WellKnownPowerSchemes.PowerSaver, h.Platform.State.ActiveScheme);
        Assert.NotEmpty(record.Backups);

        h.Click(h.Find<Button>("RollbackLastButton"));
        await h.AnswerDialogAsync<ConfirmDialogViewModel>("ConfirmButton");
        await h.WaitAsync(() => h.Vm.Optimization.LastRecord!.Status == ChangeStatus.RolledBack);
        Assert.Equal(WellKnownPowerSchemes.PowerSaver, h.Platform.State.ActiveScheme);
    }

    [AvaloniaFact]
    public async Task Declining_approval_changes_nothing()
    {
        using var h = new UiHarness();
        await SelectPowerPlanAsync(h);
        h.Click(h.Find<Button>("ApplyActionButton"));
        await h.AnswerDialogAsync<ApprovalDialogViewModel>("CancelButton");
        h.Pump(100);
        Assert.Equal(WellKnownPowerSchemes.PowerSaver, h.Platform.State.ActiveScheme);
        Assert.Empty(h.Services.Journal.LoadAll());
        Assert.Null(h.Vm.Optimization.LastRecord);
    }

    [AvaloniaFact]
    public async Task Declined_UAC_prompt_is_reported_and_nothing_changes()
    {
        using var h = new UiHarness();
        h.Platform.SimulateUacDecline = true;
        await h.NavigateAsync(h.Vm.Optimization);
        await h.Vm.Optimization.SelectActionAsync(ActionIds.Hibernation);
        await h.WaitAsync(() => h.Vm.Optimization.Inspection is not null && !h.Vm.Optimization.Inspecting);
        Assert.True(h.Vm.Optimization.Applicable, h.Vm.Optimization.StateText);
        h.Click(h.Find<Button>("ApplyActionButton"));
        await h.WaitAsync(() => h.Vm.CurrentDialog is ApprovalDialogViewModel);
        h.Click(h.Find<CheckBox>("ApproveCheck"));
        h.Click(h.Find<Button>("ApplyButton"));
        await h.WaitAsync(() => h.Vm.Optimization.LastRecord is not null);
        Assert.Equal(ChangeStatus.Cancelled, h.Vm.Optimization.LastRecord!.Status);
        Assert.True(h.Platform.State.HibernationEnabled);
    }

    [AvaloniaFact]
    public async Task Settings_explorer_write_is_rolled_back_from_change_history()
    {
        using var h = new UiHarness();
        const string path = @"Software\Microsoft\Clipboard";
        var before = h.Platform.Registry.GetValue(RegHive.CurrentUser, path, "EnableClipboardHistory");

        await h.NavigateAsync(h.Vm.SettingsExplorer);
        h.Type(h.Find<TextBox>("SettingsSearch"), "Clipboard history");
        await h.WaitAsync(() => h.Vm.SettingsExplorer.Items.Any(i => i.Definition.Id == "system.clipboard-history"));
        h.Vm.SettingsExplorer.Selected = h.Vm.SettingsExplorer.Items.First(i => i.Definition.Id == "system.clipboard-history");
        await h.WaitAsync(() => h.Vm.SettingsExplorer.Controls.Count > 0);

        h.Click(h.FindButton("Turn on"));
        await h.WaitAsync(() => h.Vm.CurrentDialog is ApprovalDialogViewModel);
        h.Click(h.Find<CheckBox>("ApproveCheck"));
        h.Click(h.Find<Button>("ApplyButton"));
        await h.WaitAsync(() => h.Platform.Registry.GetValue(RegHive.CurrentUser, path, "EnableClipboardHistory")?.AsInt() == 1);
        await h.WaitAsync(() => h.Vm.SettingsExplorer.Selected?.Current == "On");

        await h.NavigateAsync(h.Vm.History);
        await h.WaitAsync(() => h.Vm.History.Records.Count == 1);
        h.Vm.History.Selected = h.Vm.History.Records[0];
        h.Screenshot("13-history");
        h.Click(h.Find<Button>("RollbackButton"));
        await h.AnswerDialogAsync<ConfirmDialogViewModel>("ConfirmButton");
        await h.WaitAsync(() => h.Vm.History.Selected?.Status == ChangeStatus.RolledBack);

        var after = h.Platform.Registry.GetValue(RegHive.CurrentUser, path, "EnableClipboardHistory");
        Assert.Equal(before?.ToString(), after?.ToString()); // restored exactly, including "value did not exist"
    }

    [AvaloniaFact]
    public async Task Refresh_rate_change_reverts_unless_the_technician_keeps_it()
    {
        using var h = new UiHarness();
        var display = h.Platform.Display.GetDisplays()[0];
        var original = display.RefreshHz;
        Assert.True(display.MaxRefreshHzAtCurrentResolution > original);

        await h.NavigateAsync(h.Vm.Graphics);
        await h.WaitAsync(() => h.Vm.Graphics.Displays.Count > 0);
        h.Click(h.FindButton($"Use {display.MaxRefreshHzAtCurrentResolution} Hz…"));
        await h.WaitAsync(() => h.Vm.CurrentDialog is ApprovalDialogViewModel);
        h.Click(h.Find<CheckBox>("ApproveCheck"));
        h.Click(h.Find<Button>("ApplyButton"));

        await h.WaitAsync(() => h.Vm.CurrentDialog is KeepChangesDialogViewModel);
        Assert.Equal(display.MaxRefreshHzAtCurrentResolution, h.Platform.State.Displays[0].RefreshHz);
        h.Click(h.Find<Button>("RevertButton"));
        await h.WaitAsync(() => h.Services.Journal.LoadAll().FirstOrDefault()?.Status == ChangeStatus.RolledBack);
        Assert.Equal(original, h.Platform.State.Displays[0].RefreshHz);
    }

    [AvaloniaFact]
    public async Task File_organizer_moves_only_after_approval_and_undo_restores_everything()
    {
        using var h = new UiHarness();
        var downloads = h.Platform.Folders.Downloads;
        var original = Directory.GetFiles(downloads).Select(Path.GetFileName).OrderBy(x => x).ToList();

        await h.NavigateAsync(h.Vm.Files);
        h.Click(h.Find<Button>("AnalyzeButton"));
        await h.WaitAsync(() => h.Vm.Files.HasAnalysis && !h.Vm.Files.Busy);
        h.Click(h.Find<Button>("PreviewMovesButton"));
        await h.WaitAsync(() => h.Vm.Files.HasPlan);
        Assert.Equal(original, Directory.GetFiles(downloads).Select(Path.GetFileName).OrderBy(x => x).ToList()); // preview changes nothing

        // Cancelling the confirmation leaves everything in place.
        h.Click(h.Find<Button>("ApplyPlanButton"));
        await h.AnswerDialogAsync<ConfirmDialogViewModel>("CancelButton");
        Assert.Equal(original.Count, Directory.GetFiles(downloads).Length);

        h.Click(h.Find<Button>("ApplyPlanButton"));
        await h.AnswerDialogAsync<ConfirmDialogViewModel>("ConfirmButton");
        await h.WaitAsync(() => h.Vm.Files.Logs.Count == 1 && !h.Vm.Files.Busy);
        var after = Directory.GetFiles(downloads, "*", SearchOption.AllDirectories);
        Assert.Equal(original.Count, after.Length);            // nothing deleted
        Assert.True(Directory.GetFiles(downloads).Length < original.Count);

        h.Click(h.FindButton("Undo"));
        await h.AnswerDialogAsync<ConfirmDialogViewModel>("ConfirmButton");
        await h.WaitAsync(() => !h.Vm.Files.Busy && h.Vm.Files.Logs[0].Status == "Undone");
        Assert.Equal(original, Directory.GetFiles(downloads).Select(Path.GetFileName).OrderBy(x => x).ToList());
    }

    [AvaloniaFact]
    public async Task Removing_all_data_requires_typing_DELETE()
    {
        using var h = new UiHarness();
        File.WriteAllText(Path.Combine(h.Services.Paths.Scans, "scan.json"), "{}");
        Assert.NotEmpty(Directory.GetFiles(h.Services.Paths.Scans));

        await h.NavigateAsync(h.Vm.Data);
        h.Click(h.Find<Button>("RemoveDataButton"));
        await h.WaitAsync(() => h.Vm.CurrentDialog is ConfirmDialogViewModel);
        var confirm = h.Find<Button>("ConfirmButton");
        Assert.False(confirm.IsEffectivelyEnabled);
        h.Type(h.Find<TextBox>("TypedBox"), "delete");
        Assert.False(confirm.IsEffectivelyEnabled); // exact text only
        h.Type(h.Find<TextBox>("TypedBox"), "DELETE");
        Assert.True(confirm.IsEffectivelyEnabled);
        h.Click(confirm);
        await h.WaitAsync(() => h.Vm.CurrentDialog is null);
        Assert.Empty(Directory.GetFiles(h.Services.Paths.Root, "*.json", SearchOption.AllDirectories));
    }

    [AvaloniaFact]
    public async Task Protected_processes_cannot_be_ended_and_lookalikes_are_flagged_without_a_verdict()
    {
        using var h = new UiHarness();
        await h.NavigateAsync(h.Vm.Processes);
        await h.WaitAsync(() => h.Vm.Processes.Rows.Count > 0);

        h.Vm.Processes.Selected = h.Vm.Processes.Rows.First(r => r.Name.Equals("csrss.exe", StringComparison.OrdinalIgnoreCase));
        h.Pump(50);
        Assert.True(h.Vm.Processes.IsProtected);
        Assert.DoesNotContain(h.All<Button>(), b => b.IsEffectivelyVisible && "End process".Equals(b.Content));

        h.Vm.Processes.Selected = h.Vm.Processes.Rows.First(r => r.Name.Equals("syshelper.exe", StringComparison.OrdinalIgnoreCase));
        await h.WaitAsync(() => h.Vm.Processes.HasIndicators);
        Assert.False(h.Vm.Processes.IsProtected);
        Assert.DoesNotContain(h.Vm.Processes.Indicators, i => i.Contains("malware", StringComparison.OrdinalIgnoreCase) && !i.Contains("not", StringComparison.OrdinalIgnoreCase));
    }

    [AvaloniaFact]
    public async Task Every_navigation_item_opens_its_page()
    {
        using var h = new UiHarness();
        foreach (var item in h.Vm.NavItems.ToList())
        {
            h.Vm.SelectedNav = item;
            h.Pump(30);
            await Task.Delay(30);
            Assert.Same(item.Page, h.Vm.CurrentPage);
            var host = h.All<ContentControl>().First(c => ReferenceEquals(c.Content, item.Page));
            Assert.NotNull(host.Presenter?.Child);
            Assert.IsNotType<TextBlock>(host.Presenter!.Child); // a real view was located, not the "not found" fallback
        }
    }
}

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using SoftReset.App.ViewModels;
using SoftReset.Core.Diagnostics;
using SoftReset.Core.Optimization;

namespace SoftReset.App.Tests;

/// <summary>Every page puts a plain label (Good / Warm / Problem…) next to its readings.</summary>
public class LabelsTests
{
    private static List<RatingPill> Pills(Control root) => root.GetVisualDescendants().OfType<RatingPill>().Where(p => p.IsVisible).ToList();

    [AvaloniaFact]
    public async Task Graphics_page_labels_temperature_load_memory_driver_and_monitors()
    {
        using var h = new UiHarness();
        await h.NavigateAsync(h.Vm.Graphics);
        await h.WaitAsync(() => h.Vm.Graphics.Gpus.Count > 0 && h.Vm.Graphics.Displays.Count > 0);
        h.Pump(60);
        Assert.Equal("Good", h.Find<RatingPill>("GpuTempPill").Text);
        var texts = Pills(h.Window).Select(p => p.Text).ToList();
        Assert.Contains("Idle", texts);          // load
        Assert.Contains("Very old", texts);      // driver from 2023
        Assert.Contains("Not at max", texts);    // 60 Hz monitor that can do 165
        Assert.Contains("Best", texts);          // monitor already at its max
    }

    [AvaloniaFact]
    public async Task Running_programs_shows_how_heavy_each_one_is_and_who_made_it()
    {
        using var h = new UiHarness();
        await h.NavigateAsync(h.Vm.Processes);
        await h.WaitAsync(() => h.Vm.Processes.Rows.Count > 0);
        Assert.All(h.Vm.Processes.Rows, r => Assert.False(string.IsNullOrEmpty(r.UsageRating.Label)));
        h.Pump(60);
        Assert.NotEmpty(Pills(h.Find<DataGrid>("ProcessGrid")));

        h.Vm.Processes.Selected = h.Vm.Processes.Rows.First(r => r.Name.Equals("syshelper.exe", StringComparison.OrdinalIgnoreCase));
        await h.WaitAsync(() => h.Vm.Processes.SignerRating is not null);
        Assert.Equal("Not signed", h.Vm.Processes.SignerRating!.Label);
        Assert.DoesNotContain("malware", h.Vm.Processes.SignerRating.Meaning, StringComparison.OrdinalIgnoreCase);

        h.Vm.Processes.Selected = h.Vm.Processes.Rows.First(r => r.Name.Equals("Discord.exe", StringComparison.OrdinalIgnoreCase));
        await h.WaitAsync(() => h.Vm.Processes.SignerRating?.Label == "Verified");
        h.Pump(30);
        Assert.Equal("Verified", h.Find<RatingPill>("SignerPill").Text);
    }

    [AvaloniaFact]
    public async Task Windows_settings_say_good_or_can_be_better_and_apply_makes_it_good()
    {
        using var h = new UiHarness();
        await h.NavigateAsync(h.Vm.SettingsExplorer);
        h.Type(h.Find<TextBox>("SettingsSearch"), "advertising");
        await h.WaitAsync(() => h.Vm.SettingsExplorer.Items.Any(i => i.Definition.Id == "privacy.advertising-id" && i.State is not null));
        var row = h.Vm.SettingsExplorer.Items.First(i => i.Definition.Id == "privacy.advertising-id");
        Assert.Equal("Can be better", row.StatusRating?.Label);
        h.Pump(30);
        Assert.Contains(Pills(h.Find<ListBox>("SettingsList")), p => p.Text == "Can be better");

        _ = h.Vm.SettingsExplorer.QuickCommand.ExecuteAsync(row); // waits for the approval dialog below
        await h.WaitAsync(() => h.Vm.CurrentDialog is ApprovalDialogViewModel);
        h.Click(h.Find<CheckBox>("ApproveCheck"));
        h.Click(h.Find<Button>("ApplyButton"));
        await h.WaitAsync(() => h.Vm.SettingsExplorer.Items.First(i => i.Definition.Id == "privacy.advertising-id").StatusRating?.Label == "Good", 20_000);
    }

    [AvaloniaFact]
    public async Task Speed_up_page_says_if_a_fix_is_needed()
    {
        using var h = new UiHarness();
        await h.NavigateAsync(h.Vm.Optimization);
        await h.Vm.Optimization.SelectActionAsync(ActionIds.PowerPlan);
        await h.WaitAsync(() => h.Vm.Optimization.StateRating is not null);
        h.Pump(30);
        Assert.Equal("Can improve", h.Find<RatingPill>("StatePill").Text);
    }

    [AvaloniaFact]
    public async Task Find_problems_uses_plain_words_for_how_serious_each_problem_is()
    {
        using var h = new UiHarness();
        await h.NavigateAsync(h.Vm.Diagnostics);
        await h.Vm.Diagnostics.ScanCommand.ExecuteAsync(null);
        await h.WaitAsync(() => h.Vm.Diagnostics.Findings.Count > 0);
        h.Pump(60);
        var words = h.Window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains(words, w => w is "SERIOUS" or "MEDIUM" or "MINOR" or "URGENT");
        Assert.DoesNotContain("HIGH", words);
        Assert.DoesNotContain("LOW", words);
    }
}

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using SoftReset.Core.Diagnostics;

namespace SoftReset.App.Tests;

/// <summary>
/// Renders every page of the real window against the simulated PC and saves PNGs. These are the
/// screenshots shipped in docs/screenshots; each test also asserts the page actually populated.
/// </summary>
public class ScreenshotTests
{
    [AvaloniaFact]
    public async Task Every_page_renders_with_data()
    {
        using var h = new UiHarness();
        h.Services.Settings.ScanSampleSeconds = 1;
        await h.Vm.ActivateCurrentAsync();
        for (var i = 0; i < 40; i++) h.Vm.Dashboard.Tick();
        Assert.True(new FileInfo(h.Screenshot("01-home")).Length > 10_000);

        // Diagnostics: run the real engine through the Scan button.
        await h.NavigateAsync(h.Vm.Diagnostics);
        h.Click(h.Find<Button>("ScanButton"));
        await h.WaitAsync(() => h.Vm.Diagnostics.HasReport, 60_000);
        Assert.NotEmpty(h.Vm.Diagnostics.Findings);
        h.Screenshot("02-diagnostics");
        h.Vm.Diagnostics.Findings[0].IsExpanded = true;
        h.Screenshot("03-diagnostics-finding");

        // Processes: select the unsigned look-alike seeded in the scenario.
        await h.NavigateAsync(h.Vm.Processes);
        await h.WaitAsync(() => h.Vm.Processes.Rows.Count > 0);
        h.Vm.Processes.Selected = h.Vm.Processes.Rows.First(r => r.Name.Equals("syshelper.exe", StringComparison.OrdinalIgnoreCase));
        await h.WaitAsync(() => h.Vm.Processes.HasIndicators);
        h.Screenshot("05-processes");

        // Settings explorer.
        await h.NavigateAsync(h.Vm.SettingsExplorer);
        h.Vm.SettingsExplorer.Search = "refresh rate";
        await h.WaitAsync(() => h.Vm.SettingsExplorer.Items.Count > 0);
        h.Vm.SettingsExplorer.Selected = h.Vm.SettingsExplorer.Items[0];
        await h.WaitAsync(() => h.Vm.SettingsExplorer.Controls.Count > 0);
        h.Screenshot("06-settings-explorer");

        // Graphics overview and NVIDIA driver settings.
        await h.NavigateAsync(h.Vm.Graphics);
        await h.WaitAsync(() => h.Vm.Graphics.Gpus.Count > 0);
        h.Screenshot("07-graphics");
        h.Find<TabControl>("GraphicsTabs").SelectedIndex = 1;
        h.Pump(50);
        h.Click(h.Find<Button>("ReadNvidiaButton"));
        await h.WaitAsync(() => h.Vm.Graphics.NvidiaSettings.Count > 0);
        h.Screenshot("08-nvidia");

        // Applications.
        await h.NavigateAsync(h.Vm.Apps);
        await h.WaitAsync(() => h.Vm.Apps.Apps.Count > 0);
        h.Vm.Apps.Selected = h.Vm.Apps.Apps.First(a => a.Name.Contains("Discord"));
        h.Screenshot("09-apps");

        // Optimization.
        await h.NavigateAsync(h.Vm.Optimization);
        await h.Vm.Optimization.SelectActionAsync(ActionIds.StartupUserItems);
        await h.WaitAsync(() => h.Vm.Optimization.Inspection is not null);
        h.Screenshot("10-optimization");

        // File organizer.
        await h.NavigateAsync(h.Vm.Files);
        h.Click(h.Find<Button>("AnalyzeButton"));
        await h.WaitAsync(() => h.Vm.Files.HasAnalysis && !h.Vm.Files.Busy);
        h.Click(h.Find<Button>("FindDuplicatesButton"));
        await h.WaitAsync(() => h.Vm.Files.HasDuplicates && !h.Vm.Files.Busy);
        h.Click(h.Find<Button>("PreviewMovesButton"));
        await h.WaitAsync(() => h.Vm.Files.HasPlan);
        h.Screenshot("12-files");

        await h.NavigateAsync(h.Vm.History);
        h.Screenshot("13-history-empty");
        await h.NavigateAsync(h.Vm.Data);
        h.Screenshot("14-data");
    }
}

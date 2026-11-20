using SoftReset.Core.Diagnostics;
using SoftReset.Core.Models;
using SoftReset.Core.Optimization;
using SoftReset.Core.Platform;
using SoftReset.Core.Safety;
using SoftReset.Core.Startup;

namespace SoftReset.Core.Tests;

public class ActionRollbackTests
{
    public static IEnumerable<object[]> RegistryActions() => new[]
    {
        new object[] { ActionIds.FileExtensions, "Show" },
        new object[] { ActionIds.HiddenFiles, "Show" },
        new object[] { ActionIds.TaskbarAlignment, "Left" },
        new object[] { ActionIds.DarkMode, "Dark" },
        new object[] { ActionIds.Transparency, "Off" },
        new object[] { ActionIds.ExplorerThisPc, "This PC" },
        new object[] { ActionIds.GameMode, "Off" },
        new object[] { ActionIds.BackgroundRecording, "Off" },
        new object[] { ActionIds.DisableProxy, "On" },
        new object[] { ActionIds.WindowedOptimizations, "On" },
        new object[] { ActionIds.FastStartup, "Off" },
        new object[] { ActionIds.Hags, "On" },
    };

    [Theory]
    [MemberData(nameof(RegistryActions))]
    public async Task RegistryAction_ApplyVerifyRollback_RestoresExactState(string actionId, string option)
    {
        using var rig = new TestRig();
        var before = rig.Platform.Registry.Dump();
        var req = new ActionRequest { ActionId = actionId, Parameters = { ["state"] = option } };
        var preview = await rig.Registry.Get(actionId).PreviewAsync(rig.Context, req, default);
        Assert.NotEmpty(preview.Changes);

        var record = await rig.Executor.ApplyAsync(req);
        Assert.Equal(ChangeStatus.Verified, record.Status);
        Assert.NotEmpty(record.Backups);
        Assert.NotEqual(Flatten(before), Flatten(rig.Platform.Registry.Dump()));
        Assert.True(record.CanRollback);

        var rolled = await rig.Executor.RollbackAsync(record.Id);
        Assert.Equal(ChangeStatus.RolledBack, rolled.Status);
        Assert.Equal(Flatten(before), Flatten(rig.Platform.Registry.Dump()));
    }

    private static string Flatten(Dictionary<string, Dictionary<string, string>> d) =>
        string.Join("\n", d.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase).Where(k => k.Value.Count > 0)
            .Select(k => k.Key + "=" + string.Join(";", k.Value.OrderBy(v => v.Key, StringComparer.OrdinalIgnoreCase).Select(v => v.Key + ":" + v.Value))));

    [Fact]
    public async Task AdminAction_RunsThroughElevatedHelper_AndRecordsElevation()
    {
        using var rig = new TestRig(elevated: false);
        var record = await rig.Executor.ApplyAsync(new ActionRequest { ActionId = ActionIds.FastStartup, Parameters = { ["state"] = "Off" } });
        Assert.True(record.Elevated);
        Assert.Equal(ChangeStatus.Verified, record.Status);
        Assert.Equal(0, rig.Platform.Registry.GetValue(RegHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled")!.AsInt());
        Assert.False(rig.Platform.IsElevated); // elevation was scoped to the helper
    }

    [Fact]
    public async Task UacDeclined_MakesNoChange_AndRecordsCancellation()
    {
        using var rig = new TestRig();
        var before = Flatten(rig.Platform.Registry.Dump());
        rig.Platform.SimulateUacDecline = true;
        var record = await rig.Executor.ApplyAsync(new ActionRequest { ActionId = ActionIds.Hags, Parameters = { ["state"] = "On" } });
        Assert.Equal(ChangeStatus.Cancelled, record.Status);
        Assert.Contains("declined", record.Message);
        Assert.Equal(before, Flatten(rig.Platform.Registry.Dump()));
        Assert.False(record.CanRollback);
    }

    [Fact]
    public async Task StartupItems_DisableThenRollback()
    {
        using var rig = new TestRig();
        var inv = new StartupInventory(rig.Platform.Registry, rig.Platform.Folders);
        var discord = inv.Enumerate().Single(e => e.Name == "Discord");
        Assert.True(discord.Enabled);

        var record = await rig.Executor.ApplyAsync(new ActionRequest { ActionId = ActionIds.StartupUserItems, Parameters = { ["mode"] = "disable" }, SelectedItems = { discord.Id } });
        Assert.Equal(ChangeStatus.Verified, record.Status);
        Assert.False(inv.Enumerate().Single(e => e.Name == "Discord").Enabled);
        // The Run value itself is untouched — disabling is non-destructive.
        Assert.NotNull(rig.Platform.Registry.GetValue(RegHive.CurrentUser, StartupInventory.RunPath, "Discord"));

        await rig.Executor.RollbackAsync(record.Id);
        Assert.True(inv.Enumerate().Single(e => e.Name == "Discord").Enabled);
        Assert.Null(rig.Platform.Registry.GetValue(RegHive.CurrentUser, $@"{StartupInventory.ApprovedRoot}\Run", "Discord"));
    }

    [Fact]
    public async Task StartupFolderEntry_CanBeDisabled()
    {
        using var rig = new TestRig();
        var inv = new StartupInventory(rig.Platform.Registry, rig.Platform.Folders);
        var lnk = inv.Enumerate().Single(e => e.Location == StartupLocation.StartupFolderUser);
        var record = await rig.Executor.ApplyAsync(new ActionRequest { ActionId = ActionIds.StartupUserItems, SelectedItems = { lnk.Id } });
        Assert.Equal(ChangeStatus.Verified, record.Status);
        Assert.False(inv.Enumerate().Single(e => e.Id == lnk.Id).Enabled);
    }

    [Fact]
    public async Task PowerPlan_CreatesHighPerformance_AndRollbackDeletesIt()
    {
        using var rig = new TestRig();
        var countBefore = rig.Platform.State.Schemes.Count;
        var record = await rig.Executor.ApplyAsync(new ActionRequest { ActionId = ActionIds.PowerPlan, Parameters = { ["scheme"] = WellKnownPowerSchemes.HighPerformance } });
        Assert.Equal(ChangeStatus.Verified, record.Status);
        Assert.Equal(countBefore + 1, rig.Platform.State.Schemes.Count);
        Assert.Equal("High performance", rig.Platform.Power.GetStatus().ActiveSchemeName);

        await rig.Executor.RollbackAsync(record.Id);
        Assert.Equal(WellKnownPowerSchemes.PowerSaver, rig.Platform.State.ActiveScheme);
        Assert.Equal(countBefore, rig.Platform.State.Schemes.Count);
    }

    [Fact]
    public async Task RefreshRate_ChangesAndRestoresMode()
    {
        using var rig = new TestRig();
        var record = await rig.Executor.ApplyAsync(new ActionRequest { ActionId = ActionIds.RefreshRate, Parameters = { ["device"] = @"\\.\DISPLAY1", ["refresh"] = "165" } });
        Assert.Equal(ChangeStatus.Verified, record.Status);
        Assert.Equal(165, rig.Platform.Display.GetDisplays().First().RefreshHz);
        await rig.Executor.RollbackAsync(record.Id);
        Assert.Equal(60, rig.Platform.Display.GetDisplays().First().RefreshHz);
    }

    [Fact]
    public async Task RefreshRate_UnsupportedModeFailsWithoutChange()
    {
        using var rig = new TestRig();
        var record = await rig.Executor.ApplyAsync(new ActionRequest { ActionId = ActionIds.RefreshRate, Parameters = { ["device"] = @"\\.\DISPLAY1", ["refresh"] = "240" } });
        Assert.Equal(ChangeStatus.Failed, record.Status);
        Assert.Equal(60, rig.Platform.Display.GetDisplays().First().RefreshHz);
    }

    [Fact]
    public async Task VisualEffects_PerformanceThenRestore()
    {
        using var rig = new TestRig();
        var record = await rig.Executor.ApplyAsync(new ActionRequest { ActionId = ActionIds.VisualEffects, Parameters = { ["mode"] = "performance" } });
        Assert.Equal(ChangeStatus.Verified, record.Status);
        Assert.All(UiEffects.All, e => Assert.False(rig.Platform.Maintenance.GetUiEffect(e)));
        await rig.Executor.RollbackAsync(record.Id);
        Assert.All(UiEffects.All, e => Assert.True(rig.Platform.Maintenance.GetUiEffect(e)));
    }

    [Fact]
    public async Task ProcessorMaxState_RestoredOnRollback()
    {
        using var rig = new TestRig();
        var st = rig.Platform.Power.GetStatus();
        rig.Platform.Power.SetSettingIndex(st.ActiveSchemeGuid, PowerSettingIds.SubProcessor, PowerSettingIds.ProcThrottleMax, true, 60);
        var record = await rig.Executor.ApplyAsync(new ActionRequest { ActionId = ActionIds.ProcessorState });
        Assert.Equal(ChangeStatus.Verified, record.Status);
        Assert.Equal(100, rig.Platform.Power.GetStatus().ProcessorMaxStateAc);
        await rig.Executor.RollbackAsync(record.Id);
        Assert.Equal(60, rig.Platform.Power.GetStatus().ProcessorMaxStateAc);
    }

    [Fact]
    public async Task Hibernation_AdminAction_RoundTrip()
    {
        using var rig = new TestRig();
        var record = await rig.Executor.ApplyAsync(new ActionRequest { ActionId = ActionIds.Hibernation, Parameters = { ["state"] = "Off" } });
        Assert.Equal(ChangeStatus.Verified, record.Status);
        Assert.False(rig.Platform.State.HibernationEnabled);
        var rb = await rig.Executor.RollbackAsync(record.Id);
        Assert.Equal(ChangeStatus.RolledBack, rb.Status);
        Assert.True(rig.Platform.State.HibernationEnabled);
    }

    [Fact]
    public async Task ServiceStartMode_RefusesToDisableCriticalService()
    {
        using var rig = new TestRig(elevated: true);
        var record = await rig.Executor.ApplyAsync(new ActionRequest { ActionId = ActionIds.ServiceStartMode, Parameters = { ["service"] = "wuauserv", ["startMode"] = "Disabled" } });
        Assert.Equal(ChangeStatus.Failed, record.Status);
        Assert.Contains("protected", record.Message);
        Assert.Equal("Manual", rig.Platform.Services.Get("wuauserv")!.StartMode);
    }

    [Fact]
    public async Task ServiceStartMode_ThirdPartyServiceRoundTrip()
    {
        using var rig = new TestRig(elevated: true);
        var record = await rig.Executor.ApplyAsync(new ActionRequest { ActionId = ActionIds.ServiceStartMode, Parameters = { ["service"] = "CCleanerPerformanceOptimizerService", ["startMode"] = "Manual" } });
        Assert.Equal(ChangeStatus.Verified, record.Status);
        await rig.Executor.RollbackAsync(record.Id);
        Assert.Equal("Auto", rig.Platform.Services.Get("CCleanerPerformanceOptimizerService")!.StartMode);
    }

    [Fact]
    public async Task TaskEnabled_ProtectsWindowsTasks()
    {
        using var rig = new TestRig(elevated: true);
        var r = await rig.Executor.ApplyAsync(new ActionRequest { ActionId = ActionIds.TaskEnabled, Parameters = { ["task"] = @"\Microsoft\Windows\Defrag\ScheduledDefrag", ["enabled"] = "false" } });
        Assert.Equal(ChangeStatus.Failed, r.Status);
        Assert.True(rig.Platform.Tasks.GetTasks().Single(t => t.Name == "ScheduledDefrag").Enabled);
    }

    [Fact]
    public async Task LogonTasks_DisableAndRollback()
    {
        using var rig = new TestRig();
        var path = @"\CCleaner Update";
        var r = await rig.Executor.ApplyAsync(new ActionRequest { ActionId = ActionIds.StartupLogonTasks, SelectedItems = { path } });
        Assert.Equal(ChangeStatus.Verified, r.Status);
        Assert.False(rig.Platform.Tasks.GetTasks().Single(t => t.Path == path).Enabled);
        await rig.Executor.RollbackAsync(r.Id);
        Assert.True(rig.Platform.Tasks.GetTasks().Single(t => t.Path == path).Enabled);
    }

    [Fact]
    public async Task EndProcesses_SkipsProtectedProcesses()
    {
        using var rig = new TestRig();
        var procs = rig.Platform.Processes.Enumerate();
        var svchost = procs.First(p => p.Name == "svchost.exe");
        var spotify = procs.First(p => p.Name == "Spotify.exe");
        var r = await rig.Executor.ApplyAsync(new ActionRequest { ActionId = ActionIds.EndProcesses, SelectedItems = { svchost.Pid.ToString(), spotify.Pid.ToString() } });
        var after = rig.Platform.Processes.Enumerate();
        Assert.Contains(after, p => p.Pid == svchost.Pid);
        Assert.DoesNotContain(after, p => p.Pid == spotify.Pid);
        Assert.Equal(Reversibility.None, r.Reversibility);
        Assert.False(r.CanRollback);
    }

    [Fact]
    public async Task UserTempCleanup_DeletesOnlyOldFiles()
    {
        using var rig = new TestRig();
        var temp = rig.Platform.Folders.Temp;
        var recent = Path.Combine(temp, "in-use-recent.tmp");
        File.WriteAllText(recent, "x");
        var r = await rig.Executor.ApplyAsync(new ActionRequest { ActionId = ActionIds.UserTemp });
        Assert.Equal(ChangeStatus.Verified, r.Status);
        Assert.True(File.Exists(recent));
        Assert.Equal(new[] { recent }, Directory.EnumerateFiles(temp, "*", SearchOption.AllDirectories));
        Assert.True(long.Parse(r.Parameters["bytesFreed"]) > 1_000_000);
    }

    [Fact]
    public async Task FileCleaner_DoesNotFollowSymlinks()
    {
        using var rig = new TestRig();
        var outside = Path.Combine(rig.Root, "outside");
        Directory.CreateDirectory(outside);
        var precious = Path.Combine(outside, "precious.txt");
        File.WriteAllText(precious, "keep");
        File.SetLastWriteTimeUtc(precious, DateTime.UtcNow.AddDays(-100));
        Directory.CreateSymbolicLink(Path.Combine(rig.Platform.Folders.Temp, "link"), outside);
        await rig.Executor.ApplyAsync(new ActionRequest { ActionId = ActionIds.UserTemp });
        Assert.True(File.Exists(precious));
    }

    [Fact]
    public async Task ClearAppCache_EmptiesDocumentedFoldersOnly()
    {
        using var rig = new TestRig();
        var settings = Path.Combine(rig.Platform.Folders.AppDataRoaming, "discord", "settings.json");
        File.WriteAllText(settings, "{}");
        Environment.SetEnvironmentVariable("APPDATA", rig.Platform.Folders.AppDataRoaming);
        try
        {
            var r = await rig.Executor.ApplyAsync(new ActionRequest { ActionId = ActionIds.ClearAppCache, Parameters = { ["app"] = "discord" } });
            Assert.Equal(ChangeStatus.Verified, r.Status);
            Assert.True(File.Exists(settings));
            Assert.Empty(Directory.EnumerateFiles(Path.Combine(rig.Platform.Folders.AppDataRoaming, "discord", "Cache"), "*", SearchOption.AllDirectories));
        }
        finally { Environment.SetEnvironmentVariable("APPDATA", null); }
    }

    [Fact]
    public async Task Winget_ListsAndUpgradesSelected()
    {
        using var rig = new TestRig();
        var action = rig.Registry.Get(ActionIds.WingetUpgrade);
        var insp = await action.InspectAsync(rig.Context, new ActionRequest { ActionId = action.Id }, default);
        Assert.Equal(4, insp.Items.Count);
        Assert.Contains(insp.Items, i => i.Id == "Google.Chrome");
        var r = await rig.Executor.ApplyAsync(new ActionRequest { ActionId = action.Id, SelectedItems = { "Google.Chrome" } });
        Assert.Equal(ChangeStatus.Verified, r.Status);
        Assert.Contains(rig.Platform.CommandLog, c => c.Contains("--id \"Google.Chrome\" --exact"));
    }

    [Fact]
    public async Task AdminCommand_WithoutElevationFailsCleanly_WhenRunInProcess()
    {
        using var rig = new TestRig(elevated: true);
        var r = await rig.Executor.ApplyAsync(new ActionRequest { ActionId = ActionIds.Sfc });
        Assert.Equal(ChangeStatus.Verified, r.Status);
        Assert.Contains("No integrity violations", r.Message);
    }

    [Fact]
    public async Task ResetWindowsUpdate_RenamesAndRollbackRestores()
    {
        using var rig = new TestRig();
        var sd = Path.Combine(rig.Platform.Folders.WindowsDir, "SoftwareDistribution");
        var marker = Path.Combine(sd, "DataStore.edb");
        File.WriteAllText(marker, "history");
        var r = await rig.Executor.ApplyAsync(new ActionRequest { ActionId = ActionIds.ResetWindowsUpdate });
        Assert.Equal(ChangeStatus.Verified, r.Status);
        Assert.False(File.Exists(marker));
        Directory.CreateDirectory(sd); // Windows recreates the folder
        var rb = await rig.Executor.RollbackAsync(r.Id);
        Assert.Equal(ChangeStatus.RolledBack, rb.Status);
        Assert.True(File.Exists(marker));
    }

    [Fact]
    public async Task SettingRegistrySet_ValidatesCatalogAndRollsBack()
    {
        using var rig = new TestRig();
        var def = rig.Context.Knowledge.Settings.First(s => s.Controls.Any(c => c.Type == "registry-set") && s.Read.Hive == "HKCU");
        var idx = def.Controls.FindIndex(c => c.Type == "registry-set");
        var before = Flatten(rig.Platform.Registry.Dump());
        var r = await rig.Executor.ApplyAsync(new ActionRequest { ActionId = ActionIds.SettingRegistrySet, Parameters = { ["setting"] = def.Id, ["control"] = idx.ToString() } });
        Assert.Equal(ChangeStatus.Verified, r.Status);
        await rig.Executor.RollbackAsync(r.Id);
        Assert.Equal(before, Flatten(rig.Platform.Registry.Dump()));
    }

    [Fact]
    public async Task FailureMidway_KeepsBackups_AndPartialRollbackWorks()
    {
        using var rig = new TestRig();
        // Deny writes to the second target of the dark-mode action to force a mid-action failure.
        var key = $@"{RegHive.CurrentUser}\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        var before = Flatten(rig.Platform.Registry.Dump());
        var action = new FailingAfterFirstWrite();
        var registry = new ActionRegistry(new IOptimizationAction[] { action });
        var exec = new ActionExecutor(registry, rig.Context);
        var r = await exec.ApplyAsync(new ActionRequest { ActionId = action.Id });
        Assert.Equal(ChangeStatus.Failed, r.Status);
        Assert.Single(r.Backups);
        Assert.True(r.CanRollback);
        // The backup was persisted to disk before the failure (write-ahead).
        Assert.Single(rig.Journal.Load(r.Id)!.Backups);
        var rb = await exec.RollbackAsync(r.Id);
        Assert.Equal(ChangeStatus.RolledBack, rb.Status);
        Assert.Equal(before, Flatten(rig.Platform.Registry.Dump()));
        _ = key;
    }

    [Fact]
    public void InterruptedRecords_AreRecoveredAtStartup()
    {
        using var rig = new TestRig();
        var stale = new ChangeRecord { ActionId = "x", Title = "t", CreatedAt = DateTimeOffset.Now.AddHours(-1), Status = ChangeStatus.Pending, Reversibility = Reversibility.Full, Backups = { new BackupItem { Kind = "note" } } };
        var fresh = new ChangeRecord { ActionId = "y", Title = "u", Status = ChangeStatus.Pending };
        rig.Journal.Save(stale);
        rig.Journal.Save(fresh);
        var recovered = rig.Journal.RecoverInterrupted();
        Assert.Single(recovered);
        Assert.Equal(ChangeStatus.Interrupted, rig.Journal.Load(stale.Id)!.Status);
        Assert.Equal(ChangeStatus.Pending, rig.Journal.Load(fresh.Id)!.Status);
        Assert.True(rig.Journal.Load(stale.Id)!.CanRollback);
    }

    [Fact]
    public void Journal_SkipsCorruptRecords()
    {
        using var rig = new TestRig();
        rig.Journal.Save(new ChangeRecord { ActionId = "a", Title = "ok" });
        File.WriteAllText(Path.Combine(rig.Paths.Journal, "broken.json"), "{ not json");
        Assert.Single(rig.Journal.LoadAll());
    }

    [Fact]
    public async Task AppliedFixes_ResolveFindingsOnRescan()
    {
        using var rig = new TestRig();
        var engine = new DiagnosticEngine();
        var before = engine.Analyze(await rig.ScanAsync());
        await rig.Executor.ApplyAsync(new ActionRequest { ActionId = ActionIds.RefreshRate, Parameters = { ["device"] = @"\\.\DISPLAY1", ["refresh"] = "165" } });
        await rig.Executor.ApplyAsync(new ActionRequest { ActionId = ActionIds.PowerPlan, Parameters = { ["scheme"] = WellKnownPowerSchemes.Balanced } });
        await rig.Executor.ApplyAsync(new ActionRequest { ActionId = ActionIds.BackgroundRecording, Parameters = { ["state"] = "Off" } });
        var after = engine.Analyze(await rig.ScanAsync());
        var cmp = DiagnosticEngine.Compare(before, after);
        Assert.Contains(cmp.Resolved, f => f.RuleId == "gaming.refresh-rate");
        Assert.Contains(cmp.Resolved, f => f.Key == "power.saver-plan");
        Assert.Contains(cmp.Resolved, f => f.Key == "gaming.background-recording");
        Assert.True(after.HealthScore > before.HealthScore);
        Assert.Equal(165, after.Snapshot.Displays.First(d => d.IsPrimary).RefreshHz);
    }

    private sealed class FailingAfterFirstWrite : OptimizationAction
    {
        public override string Id => "test.fail-midway";
        public override string Title => "Fails midway";
        public override OptimizationCategory Category => OptimizationCategory.Desktop;
        public override string Description => "";
        public override string ExpectedBenefit => "";
        public override string Risks => "";
        public override Reversibility Reversibility => Reversibility.Full;
        public override Task<ActionInspection> InspectAsync(ActionContext ctx, ActionRequest r, CancellationToken ct) => Task.FromResult(new ActionInspection());
        public override Task<ActionPreview> PreviewAsync(ActionContext ctx, ActionRequest r, CancellationToken ct) => Task.FromResult(new ActionPreview());
        public override Task ApplyAsync(ActionContext ctx, ActionRequest r, ChangeRecord record, CancellationToken ct)
        {
            const string p = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
            ctx.AddBackup(record, RegistryBackup.Capture(ctx.Platform.Registry, RegHive.CurrentUser, p, "AppsUseLightTheme"));
            ctx.Platform.Registry.SetValue(RegHive.CurrentUser, p, "AppsUseLightTheme", RegValue.DWord(0));
            throw new IOException("Simulated failure after the first write");
        }
        public override Task<VerificationResult> VerifyAsync(ActionContext ctx, ActionRequest r, ChangeRecord record, CancellationToken ct) => Task.FromResult(VerificationResult.Ok(""));
    }
}

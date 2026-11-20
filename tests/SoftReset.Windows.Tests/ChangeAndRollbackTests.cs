using SoftReset.Core.Diagnostics;
using SoftReset.Core.Models;
using SoftReset.Core.Optimization;
using SoftReset.Core.Platform;
using SoftReset.Core.Safety;
using SoftReset.Core.Storage;
using SoftReset.Platform.Windows;

namespace SoftReset.Windows.Tests;

/// <summary>Writes to the real registry / power configuration, verifies, and rolls back.</summary>
public class ChangeAndRollbackTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "softreset-wt-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _testKey = $@"Software\softreset-tests\{Guid.NewGuid():N}";

    public void Dispose()
    {
        if (OperatingSystem.IsWindows())
            try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(_testKey, false); } catch { }
        try { Directory.Delete(_root, true); } catch { }
    }

    private (ActionExecutor Exec, ActionContext Ctx) Rig(ISystemPlatform p)
    {
        var paths = new AppPaths(_root, false);
        paths.EnsureCreated();
        var ctx = new ActionContext { Platform = p, Journal = new ChangeJournal(paths.Journal), Paths = paths };
        return (new ActionExecutor(ActionRegistry.CreateDefault(), ctx), ctx);
    }

    [WindowsFact]
    public void Registry_round_trips_every_value_kind_in_a_private_test_key()
    {
        var reg = new WindowsPlatform().Registry;
        var values = new Dictionary<string, RegValue>
        {
            ["dw"] = RegValue.DWord(42),
            ["qw"] = new(RegKind.QWord, 1L << 40),
            ["sz"] = RegValue.Str("hello"),
            ["esz"] = new(RegKind.ExpandString, "%SystemRoot%\\x"),
            ["msz"] = new(RegKind.MultiString, new[] { "a", "b" }),
            ["bin"] = new(RegKind.Binary, new byte[] { 1, 2, 3 }),
        };
        foreach (var (n, v) in values) reg.SetValue(RegHive.CurrentUser, _testKey, n, v);
        foreach (var (n, v) in values) Assert.Equal(RegistryBackup.Encode(v), RegistryBackup.Encode(reg.GetValue(RegHive.CurrentUser, _testKey, n)!));
        Assert.Equal(values.Keys.OrderBy(x => x), reg.GetValueNames(RegHive.CurrentUser, _testKey).OrderBy(x => x));
        reg.DeleteValue(RegHive.CurrentUser, _testKey, "dw");
        Assert.Null(reg.GetValue(RegHive.CurrentUser, _testKey, "dw"));
    }

    [WindowsFact]
    public void Registry_backup_restores_previous_value_and_previous_absence()
    {
        var reg = new WindowsPlatform().Registry;
        reg.SetValue(RegHive.CurrentUser, _testKey, "existing", RegValue.DWord(1));
        var b1 = RegistryBackup.Capture(reg, RegHive.CurrentUser, _testKey, "existing");
        var b2 = RegistryBackup.Capture(reg, RegHive.CurrentUser, _testKey, "absent");
        reg.SetValue(RegHive.CurrentUser, _testKey, "existing", RegValue.DWord(2));
        reg.SetValue(RegHive.CurrentUser, _testKey, "absent", RegValue.Str("new"));
        RegistryBackup.Restore(reg, b1);
        RegistryBackup.Restore(reg, b2);
        Assert.Equal(1, reg.GetValue(RegHive.CurrentUser, _testKey, "existing")!.AsInt());
        Assert.Null(reg.GetValue(RegHive.CurrentUser, _testKey, "absent"));
        Assert.True(RegistryBackup.Matches(reg, b1) && RegistryBackup.Matches(reg, b2));
    }

    [WindowsChangeFact]
    public async Task Explorer_option_is_applied_verified_and_rolled_back_on_real_windows()
    {
        var p = new WindowsPlatform();
        var (exec, ctx) = Rig(p);
        const string adv = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        var before = p.Registry.GetValue(RegHive.CurrentUser, adv, "HideFileExt");
        var action = ActionRegistry.CreateDefault().Get(ActionIds.FileExtensions);
        var insp = await action.InspectAsync(ctx, new ActionRequest { ActionId = action.Id }, CancellationToken.None);
        var other = insp.Options.First(o => !o.IsCurrent);

        var rec = await exec.ApplyAsync(new ActionRequest { ActionId = action.Id, Parameters = { [insp.OptionParameter] = other.Value } });
        Assert.Equal(ChangeStatus.Verified, rec.Status);
        Assert.NotEqual(before?.ToString(), p.Registry.GetValue(RegHive.CurrentUser, adv, "HideFileExt")?.ToString());

        var rb = await exec.RollbackAsync(rec.Id);
        Assert.Equal(ChangeStatus.RolledBack, rb.Status);
        Assert.Equal(before?.ToString(), p.Registry.GetValue(RegHive.CurrentUser, adv, "HideFileExt")?.ToString());
    }

    [WindowsChangeFact]
    public async Task Power_plan_change_is_verified_and_rolled_back_on_real_windows()
    {
        var p = new WindowsPlatform();
        var (exec, _) = Rig(p);
        var original = p.Power.GetStatus().ActiveSchemeGuid;
        var target = p.Power.GetStatus().Schemes.First(s => !s.Guid.Equals(original, StringComparison.OrdinalIgnoreCase)).Guid;

        var rec = await exec.ApplyAsync(new ActionRequest { ActionId = ActionIds.PowerPlan, Parameters = { ["scheme"] = target } });
        Assert.True(rec.Status == ChangeStatus.Verified, $"{rec.Status}: {rec.Message} {rec.VerificationMessage}");
        Assert.Equal(target, p.Power.GetStatus().ActiveSchemeGuid, StringComparer.OrdinalIgnoreCase);

        var rb = await exec.RollbackAsync(rec.Id);
        Assert.Equal(ChangeStatus.RolledBack, rb.Status);
        Assert.Equal(original, p.Power.GetStatus().ActiveSchemeGuid, StringComparer.OrdinalIgnoreCase);
    }

    [WindowsChangeFact]
    public async Task Flushing_the_dns_cache_runs_the_real_command()
    {
        var p = new WindowsPlatform();
        var (exec, _) = Rig(p);
        var rec = await exec.ApplyAsync(new ActionRequest { ActionId = ActionIds.FlushDns });
        Assert.True(rec.Status is ChangeStatus.Verified or ChangeStatus.Applied, $"{rec.Status}: {rec.Message}");
    }
}

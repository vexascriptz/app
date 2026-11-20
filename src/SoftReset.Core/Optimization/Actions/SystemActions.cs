using SoftReset.Core.Diagnostics;
using SoftReset.Core.Models;
using SoftReset.Core.Platform;
using SoftReset.Core.Safety;
using SoftReset.Core.Startup;

namespace SoftReset.Core.Optimization.Actions;

// =====================================================================  Startup

/// <summary>Enables/disables Run-key and Startup-folder entries through StartupApproved (Task Manager's mechanism).</summary>
public abstract class StartupItemsActionBase : OptimizationAction
{
    protected abstract bool MachineScope { get; }
    public override OptimizationCategory Category => OptimizationCategory.Startup;
    public override Reversibility Reversibility => Reversibility.Full;
    public override string ExpectedBenefit => "Shorter time from sign-in to a usable desktop and less memory used in the background. Measured by fewer enabled startup items and, after a restart, boot duration.";
    public override string Risks => "A disabled app will not start automatically (e.g. chat notifications, cloud sync, peripheral lighting software). Nothing is uninstalled; re-enable it here or in Task Manager.";
    public override string Measurement => "Startup items are re-read after the change; boot time improvement is visible after the next restart.";

    protected List<StartupEntry> Entries(ActionContext ctx) =>
        new StartupInventory(ctx.Platform.Registry, ctx.Platform.Folders, ctx.Platform.FileExists).Enumerate()
            .Where(e => e.MachineWide == MachineScope && e.Location != StartupLocation.ScheduledTaskLogon)
            .ToList();

    public override Task<ActionInspection> InspectAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        var entries = Entries(ctx);
        var insp = new ActionInspection
        {
            CurrentState = $"{entries.Count(e => e.Enabled)} enabled, {entries.Count(e => !e.Enabled)} disabled",
            OptionParameter = "mode",
            Options = { new() { Value = "disable", Label = "Disable selected", IsRecommended = true }, new() { Value = "enable", Label = "Enable selected" } },
        };
        foreach (var e in entries.OrderBy(e => e.Name))
        {
            var exe = e.ExecutablePath is { } p ? Path.GetFileName(p) : null;
            var k = exe is null ? null : ctx.Knowledge.FindProcess(exe);
            var guide = ctx.Knowledge.FindAppGuide(e.Name);
            insp.Items.Add(new ActionItem
            {
                Id = e.Id,
                Label = e.Name,
                Detail = e.Command,
                CurrentState = e.Enabled ? "Enabled" : "Disabled",
                Recommendation = k?.Startup ?? guide?.StartupBehavior,
                Warning = !e.FileExists ? "Target file is missing — safe to disable." :
                          k?.Critical == true ? "Associated with a core component — leave enabled." : null,
                Selectable = k?.Critical != true,
            });
        }
        return Task.FromResult(insp);
    }

    public override Task<ActionPreview> PreviewAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        var enable = request.Param("mode", "disable") == "enable";
        var entries = Entries(ctx).ToDictionary(e => e.Id);
        var p = NewPreview();
        foreach (var id in request.SelectedItems)
        {
            if (!entries.TryGetValue(id, out var e)) { p.Warnings.Add($"Startup item no longer exists: {id}"); continue; }
            if (e.Enabled == enable) continue;
            p.Changes.Add(new PreviewChange($"{e.Name} ({e.LocationDetail})", e.Enabled ? "Enabled" : "Disabled", enable ? "Enabled" : "Disabled"));
        }
        return Task.FromResult(p);
    }

    public override Task ApplyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        var enable = request.Param("mode", "disable") == "enable";
        var inv = new StartupInventory(ctx.Platform.Registry, ctx.Platform.Folders, ctx.Platform.FileExists);
        var entries = Entries(ctx).ToDictionary(e => e.Id);
        foreach (var id in request.SelectedItems)
        {
            ct.ThrowIfCancellationRequested();
            if (!entries.TryGetValue(id, out var e) || e.Enabled == enable) continue;
            var target = StartupInventory.ParseApprovedTarget(id) ?? throw new InvalidOperationException($"Unsupported entry {id}");
            ctx.AddBackup(record, RegistryBackup.Capture(ctx.Platform.Registry, target.Hive, $@"{StartupInventory.ApprovedRoot}\{target.ApprovedSub}", target.ValueName, $"Startup approval for {e.Name}"));
            inv.SetEnabled(id, enable);
            record.ChangesApplied.Add($"{(enable ? "Enabled" : "Disabled")} startup item '{e.Name}'");
        }
        record.Message = $"{record.ChangesApplied.Count} startup item(s) {(enable ? "enabled" : "disabled")}.";
        return Task.CompletedTask;
    }

    public override Task<VerificationResult> VerifyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        var enable = request.Param("mode", "disable") == "enable";
        var entries = Entries(ctx).ToDictionary(e => e.Id);
        var wrong = request.SelectedItems.Where(id => entries.TryGetValue(id, out var e) && e.Enabled != enable).ToList();
        return Task.FromResult(wrong.Count == 0
            ? VerificationResult.Ok($"All selected items now read as {(enable ? "enabled" : "disabled")}. Enabled startup items: {entries.Values.Count(e => e.Enabled)}.")
            : VerificationResult.Fail($"{wrong.Count} item(s) did not change."));
    }
}

public sealed class UserStartupItemsAction : StartupItemsActionBase
{
    public override string Id => ActionIds.StartupUserItems;
    public override string Title => "Startup apps (this user)";
    public override string Description => "Programs that start when this user signs in (HKCU Run key and the user's Startup folder).";
    protected override bool MachineScope => false;
}

public sealed class MachineStartupItemsAction : StartupItemsActionBase
{
    public override string Id => ActionIds.StartupMachineItems;
    public override string Title => "Startup apps (all users)";
    public override string Description => "Programs that start for every user (HKLM Run keys and the all-users Startup folder). Changing these requires administrator approval.";
    protected override bool MachineScope => true;
    public override bool RequiresAdmin => true;
}

public sealed class LogonTasksAction : OptimizationAction
{
    public override string Id => ActionIds.StartupLogonTasks;
    public override string Title => "Third-party tasks that run at sign-in";
    public override OptimizationCategory Category => OptimizationCategory.Startup;
    public override string Description => "Non-Microsoft scheduled tasks with a logon trigger — many updaters and launchers use these instead of Run keys, so they don't appear in Task Manager's Startup tab.";
    public override string ExpectedBenefit => "Fewer background processes after sign-in.";
    public override string Risks => "Disabling an updater task stops that app from updating itself automatically. Microsoft tasks are never shown here.";
    public override bool RequiresAdmin => true;
    public override Reversibility Reversibility => Reversibility.Full;

    private static List<ScheduledTaskInfo> Tasks(ActionContext ctx) =>
        ctx.Platform.Tasks.GetTasks().Where(t => !t.IsMicrosoft && (t.HasLogonTrigger || t.HasBootTrigger)).ToList();

    public override Task<ActionInspection> InspectAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        var tasks = Tasks(ctx);
        var insp = new ActionInspection
        {
            CurrentState = $"{tasks.Count(t => t.Enabled)} enabled of {tasks.Count}",
            OptionParameter = "mode",
            Options = { new() { Value = "disable", Label = "Disable selected", IsRecommended = true }, new() { Value = "enable", Label = "Enable selected" } },
        };
        foreach (var t in tasks)
            insp.Items.Add(new ActionItem { Id = t.Path, Label = t.Name, Detail = $"{t.Action} — triggers: {t.Triggers}", CurrentState = t.Enabled ? "Enabled" : "Disabled" });
        return Task.FromResult(insp);
    }

    public override Task<ActionPreview> PreviewAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        var enable = request.Param("mode", "disable") == "enable";
        var tasks = Tasks(ctx).ToDictionary(t => t.Path, StringComparer.OrdinalIgnoreCase);
        var p = NewPreview();
        foreach (var id in request.SelectedItems)
            if (tasks.TryGetValue(id, out var t) && t.Enabled != enable)
                p.Changes.Add(new PreviewChange(t.Path, t.Enabled ? "Enabled" : "Disabled", enable ? "Enabled" : "Disabled"));
        return Task.FromResult(p);
    }

    public override Task ApplyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        var enable = request.Param("mode", "disable") == "enable";
        var tasks = Tasks(ctx).ToDictionary(t => t.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var id in request.SelectedItems)
        {
            ct.ThrowIfCancellationRequested();
            if (!tasks.TryGetValue(id, out var t) || t.Enabled == enable) continue;
            if (t.IsMicrosoft) throw new InvalidOperationException("Microsoft tasks are protected.");
            ctx.AddBackup(record, new BackupItem { Kind = "scheduled-task", Description = t.Path, Data = { ["path"] = t.Path, ["enabled"] = t.Enabled.ToString() } });
            ctx.Platform.Tasks.SetEnabled(t.Path, enable);
            record.ChangesApplied.Add($"{(enable ? "Enabled" : "Disabled")} task {t.Path}");
        }
        return Task.CompletedTask;
    }

    public override Task<VerificationResult> VerifyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        var enable = request.Param("mode", "disable") == "enable";
        var tasks = Tasks(ctx).ToDictionary(t => t.Path, StringComparer.OrdinalIgnoreCase);
        var wrong = request.SelectedItems.Where(id => tasks.TryGetValue(id, out var t) && t.Enabled != enable).ToList();
        return Task.FromResult(wrong.Count == 0 ? VerificationResult.Ok("Task states confirmed.") : VerificationResult.Fail($"{wrong.Count} task(s) did not change."));
    }
}

// =====================================================================  Power

public sealed class PowerPlanAction : OptimizationAction
{
    public override string Id => ActionIds.PowerPlan;
    public override string Title => "Power plan";
    public override OptimizationCategory Category => OptimizationCategory.Power;
    public override string Description => "Selects the active power plan. Balanced is the Windows default and lets modern CPUs boost fully when needed.";
    public override string ExpectedBenefit => "Leaving Power saver restores full CPU performance. High performance keeps the CPU at higher clocks, which can help older desktops in CPU-bound games at the cost of power and heat.";
    public override string Risks => "High performance increases power use, heat, and fan noise; on laptops it shortens battery life. The previous plan is restored on rollback.";
    public override Reversibility Reversibility => Reversibility.Full;

    public override Task<ActionInspection> InspectAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        var st = ctx.Platform.Power.GetStatus();
        var insp = new ActionInspection { CurrentState = $"{st.ActiveSchemeName}", OptionParameter = "scheme" };
        foreach (var s in st.Schemes)
            insp.Options.Add(new ActionOption { Value = s.Guid, Label = s.Name, IsCurrent = s.Active, IsRecommended = s.Guid.Equals(WellKnownPowerSchemes.Balanced, StringComparison.OrdinalIgnoreCase) });
        if (!st.Schemes.Any(s => s.Guid.Equals(WellKnownPowerSchemes.HighPerformance, StringComparison.OrdinalIgnoreCase)))
        {
            insp.Options.Add(new ActionOption { Value = WellKnownPowerSchemes.HighPerformance, Label = "High performance (will be created)" });
            if (st.ModernStandby == true) insp.Notes.Add("This PC uses Modern Standby, which normally only exposes the Balanced plan; use the power mode slider in Settings instead of extra plans.");
        }
        insp.AlreadyOptimal = !st.ActiveSchemeGuid.Equals(WellKnownPowerSchemes.PowerSaver, StringComparison.OrdinalIgnoreCase);
        return Task.FromResult(insp);
    }

    public override Task<ActionPreview> PreviewAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        var st = ctx.Platform.Power.GetStatus();
        var target = request.Param("scheme", WellKnownPowerSchemes.Balanced).ToLowerInvariant();
        var p = NewPreview();
        if (!st.ActiveSchemeGuid.Equals(target, StringComparison.OrdinalIgnoreCase))
        {
            var name = st.Schemes.FirstOrDefault(s => s.Guid.Equals(target, StringComparison.OrdinalIgnoreCase))?.Name ?? (target == WellKnownPowerSchemes.HighPerformance ? "High performance (new copy)" : target);
            p.Changes.Add(new PreviewChange("Active power plan", st.ActiveSchemeName, name));
        }
        return Task.FromResult(p);
    }

    public override Task ApplyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        var power = ctx.Platform.Power;
        var st = power.GetStatus();
        var target = request.Param("scheme", WellKnownPowerSchemes.Balanced).ToLowerInvariant();
        var backup = new BackupItem { Kind = "power-active-scheme", Description = $"Active plan: {st.ActiveSchemeName}", Data = { ["guid"] = st.ActiveSchemeGuid } };
        ctx.AddBackup(record, backup);
        if (!st.Schemes.Any(s => s.Guid.Equals(target, StringComparison.OrdinalIgnoreCase)))
        {
            var created = power.DuplicateScheme(target);
            backup.Data["createdScheme"] = created;
            ctx.Checkpoint(record);
            record.ChangesApplied.Add($"Created power plan {created} from {target}");
            target = created;
        }
        power.SetActiveScheme(target);
        record.Parameters["appliedScheme"] = target;
        record.ChangesApplied.Add($"Active power plan set to {target}");
        return Task.CompletedTask;
    }

    public override Task<VerificationResult> VerifyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        var st = ctx.Platform.Power.GetStatus();
        var want = record.Parameters.TryGetValue("appliedScheme", out var a) ? a : request.Param("scheme");
        return Task.FromResult(st.ActiveSchemeGuid.Equals(want, StringComparison.OrdinalIgnoreCase)
            ? VerificationResult.Ok($"Active plan is now {st.ActiveSchemeName}.")
            : VerificationResult.Fail($"Active plan is {st.ActiveSchemeName}; a vendor utility may have switched it back."));
    }
}

/// <summary>Writes one power-plan setting index on the active plan.</summary>
public abstract class PowerSettingAction : OptimizationAction
{
    protected abstract string Subgroup { get; }
    protected abstract string Setting { get; }
    protected abstract bool Ac { get; }
    protected abstract int TargetValue(ActionRequest r);
    protected abstract string Describe(int? value);
    public override Reversibility Reversibility => Reversibility.Full;

    public override Task<ActionInspection> InspectAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        var st = ctx.Platform.Power.GetStatus();
        var v = ctx.Platform.Power.GetSettingIndex(st.ActiveSchemeGuid, Subgroup, Setting, Ac);
        return Task.FromResult(new ActionInspection { CurrentState = $"{Describe(v)} (plan: {st.ActiveSchemeName})", AlreadyOptimal = v == TargetValue(request) });
    }

    public override Task<ActionPreview> PreviewAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        var st = ctx.Platform.Power.GetStatus();
        var v = ctx.Platform.Power.GetSettingIndex(st.ActiveSchemeGuid, Subgroup, Setting, Ac);
        var p = NewPreview();
        var t = TargetValue(request);
        if (v != t) p.Changes.Add(new PreviewChange($"{Title} ({(Ac ? "plugged in" : "on battery")}, plan {st.ActiveSchemeName})", Describe(v), Describe(t)));
        return Task.FromResult(p);
    }

    public override Task ApplyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        var power = ctx.Platform.Power;
        var st = power.GetStatus();
        var cur = power.GetSettingIndex(st.ActiveSchemeGuid, Subgroup, Setting, Ac) ?? throw new InvalidOperationException("The setting is not present in the active power plan.");
        ctx.AddBackup(record, new BackupItem
        {
            Kind = "power-setting", Description = $"{Title}: {Describe(cur)}",
            Data = { ["scheme"] = st.ActiveSchemeGuid, ["subgroup"] = Subgroup, ["setting"] = Setting, ["ac"] = Ac.ToString(), ["value"] = cur.ToString() },
        });
        var t = TargetValue(request);
        power.SetSettingIndex(st.ActiveSchemeGuid, Subgroup, Setting, Ac, t);
        power.SetActiveScheme(st.ActiveSchemeGuid); // re-apply so the change takes effect immediately
        record.ChangesApplied.Add($"{Title}: {Describe(cur)} → {Describe(t)}");
        return Task.CompletedTask;
    }

    public override Task<VerificationResult> VerifyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        var st = ctx.Platform.Power.GetStatus();
        var v = ctx.Platform.Power.GetSettingIndex(st.ActiveSchemeGuid, Subgroup, Setting, Ac);
        return Task.FromResult(v == TargetValue(request) ? VerificationResult.Ok($"Now {Describe(v)}.") : VerificationResult.Fail($"Reads {Describe(v)}."));
    }
}

public sealed class ProcessorMaxStateAction : PowerSettingAction
{
    public override string Id => ActionIds.ProcessorState;
    public override string Title => "Maximum processor state";
    public override OptimizationCategory Category => OptimizationCategory.Power;
    public override string Description => "Restores the active plan's maximum processor state (plugged in) to 100% so the CPU can reach full speed.";
    public override string ExpectedBenefit => "Removes an artificial CPU speed cap; measurable in CPU-heavy tasks.";
    public override string Risks => "More heat and fan noise under load if the cap was set deliberately to manage temperatures.";
    protected override string Subgroup => PowerSettingIds.SubProcessor;
    protected override string Setting => PowerSettingIds.ProcThrottleMax;
    protected override bool Ac => true;
    protected override int TargetValue(ActionRequest r) => int.TryParse(r.Param("value", "100"), out var v) ? Math.Clamp(v, 5, 100) : 100;
    protected override string Describe(int? v) => v is null ? "Not set" : $"{v}%";
}

public sealed class UsbSelectiveSuspendAction : PowerSettingAction
{
    public override string Id => ActionIds.UsbSelectiveSuspend;
    public override string Title => "USB selective suspend";
    public override OptimizationCategory Category => OptimizationCategory.Power;
    public override string Description => "Allows Windows to power down idle USB devices. Disabling it (plugged in) is a standard fix for USB devices that disconnect, lag, or fail to wake.";
    public override string ExpectedBenefit => "Fixes intermittent USB disconnects (headsets, controllers, audio interfaces, VR). No performance gain otherwise.";
    public override string Risks => "Slightly higher idle power use when plugged in.";
    protected override string Subgroup => PowerSettingIds.SubUsb;
    protected override string Setting => PowerSettingIds.UsbSelectiveSuspend;
    protected override bool Ac => true;
    protected override int TargetValue(ActionRequest r) => r.Param("state", "Disabled") == "Enabled" ? 1 : 0;
    protected override string Describe(int? v) => v switch { 0 => "Disabled", 1 => "Enabled", null => "Not available", _ => v.ToString()! };
}

public sealed class HibernationAction : OptimizationAction
{
    public override string Id => ActionIds.Hibernation;
    public override string Title => "Hibernation";
    public override OptimizationCategory Category => OptimizationCategory.Storage;
    public override string Description => "Turns hibernation off (powercfg /hibernate off), which deletes hiberfil.sys and frees disk space roughly 40% of installed RAM.";
    public override string ExpectedBenefit => "Frees several GB on small SSDs.";
    public override string Risks => "Also disables Fast Startup and the Hibernate power option. Laptops that run out of battery during sleep will lose unsaved work instead of hibernating. Fully reversible with powercfg /hibernate on.";
    public override bool RequiresAdmin => true;
    public override Reversibility Reversibility => Reversibility.Full;
    public override string Measurement => "Free space on the system drive is re-measured.";

    public override Task<ActionInspection> InspectAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        var st = ctx.Platform.Power.GetStatus();
        return Task.FromResult(new ActionInspection
        {
            CurrentState = st.HibernationEnabled switch { true => "On", false => "Off", _ => "Unknown" },
            Options = { new() { Value = "Off", Label = "Off", IsCurrent = st.HibernationEnabled == false }, new() { Value = "On", Label = "On", IsCurrent = st.HibernationEnabled == true, IsRecommended = true } },
            AlreadyOptimal = true,
            Notes = { "Only turn hibernation off when disk space is genuinely tight." },
        });
    }

    public override Task<ActionPreview> PreviewAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        var st = ctx.Platform.Power.GetStatus();
        var on = request.Param("state", "Off") == "On";
        var p = NewPreview();
        if (st.HibernationEnabled != on) p.Changes.Add(new PreviewChange("Hibernation", st.HibernationEnabled == true ? "On" : "Off", on ? "On" : "Off"));
        if (!on) p.Warnings.Add("Fast Startup will also stop working while hibernation is off.");
        return Task.FromResult(p);
    }

    public override Task ApplyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        var st = ctx.Platform.Power.GetStatus();
        var on = request.Param("state", "Off") == "On";
        ctx.AddBackup(record, new BackupItem { Kind = "hibernation", Description = "Hibernation state", Data = { ["enabled"] = (st.HibernationEnabled ?? true).ToString() } });
        ctx.Platform.Power.SetHibernation(on);
        record.ChangesApplied.Add($"Hibernation turned {(on ? "on" : "off")}");
        return Task.CompletedTask;
    }

    public override Task<VerificationResult> VerifyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        var on = request.Param("state", "Off") == "On";
        var st = ctx.Platform.Power.GetStatus();
        return Task.FromResult(st.HibernationEnabled == on ? VerificationResult.Ok($"Hibernation is {(on ? "on" : "off")}.") : VerificationResult.Fail("Hibernation state did not change."));
    }
}

// =====================================================================  Display

public sealed class RefreshRateAction : OptimizationAction
{
    public override string Id => ActionIds.RefreshRate;
    public override string Title => "Display refresh rate";
    public override OptimizationCategory Category => OptimizationCategory.Display;
    public override string Description => "Sets a monitor to a refresh rate it supports at its current resolution. The mode is tested by the driver before it is applied.";
    public override string ExpectedBenefit => "On high-refresh monitors stuck at 60 Hz: visibly smoother motion and lower input latency. Measured by re-reading the display mode.";
    public override string Risks => "If the cable cannot carry the higher rate the screen may flicker or go blank — the console asks for confirmation and reverts automatically after 15 seconds without it.";
    public override Reversibility Reversibility => Reversibility.Full;
    public override string Measurement => "The active display mode is re-read after the change.";

    public override Task<ActionInspection> InspectAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        var displays = ctx.Platform.Display.GetDisplays();
        if (displays.Count == 0) return Task.FromResult(ActionInspection.NotApplicable("No displays could be enumerated."));
        var insp = new ActionInspection { CurrentState = string.Join("; ", displays.Select(d => $"{d.DeviceName}: {d.Width}x{d.Height} @ {d.RefreshHz} Hz")), OptionParameter = "refresh" };
        foreach (var d in displays)
        {
            var rates = d.SupportedModes.Where(m => m.Width == d.Width && m.Height == d.Height).Select(m => m.RefreshHz).Distinct().OrderByDescending(r => r).ToList();
            insp.Items.Add(new ActionItem
            {
                Id = d.DeviceName,
                Label = string.IsNullOrEmpty(d.MonitorName) ? d.DeviceName : $"{d.MonitorName} ({d.DeviceName})",
                Detail = $"Supported at {d.Width}x{d.Height}: {string.Join(", ", rates.Select(r => r + " Hz"))}",
                CurrentState = $"{d.RefreshHz} Hz",
                Recommendation = d.MaxRefreshHzAtCurrentResolution > d.RefreshHz ? $"Can run at {d.MaxRefreshHzAtCurrentResolution} Hz" : "Already at its highest refresh rate",
                Selected = d.MaxRefreshHzAtCurrentResolution > d.RefreshHz,
            });
        }
        insp.AlreadyOptimal = displays.All(d => d.MaxRefreshHzAtCurrentResolution <= d.RefreshHz);
        return Task.FromResult(insp);
    }

    private static IEnumerable<(DisplayInfo Display, int Target)> Plan(ActionContext ctx, ActionRequest r)
    {
        var displays = ctx.Platform.Display.GetDisplays();
        var devices = r.SelectedItems.Count > 0 ? r.SelectedItems : new List<string> { r.Param("device") };
        foreach (var dev in devices.Where(d => !string.IsNullOrEmpty(d)))
        {
            var d = displays.FirstOrDefault(x => x.DeviceName == dev);
            if (d is null) continue;
            var target = int.TryParse(r.Param("refresh"), out var hz) && devices.Count == 1 ? hz : d.MaxRefreshHzAtCurrentResolution;
            yield return (d, target);
        }
    }

    public override Task<ActionPreview> PreviewAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        var p = NewPreview();
        foreach (var (d, target) in Plan(ctx, request))
        {
            if (!d.SupportedModes.Any(m => m.Width == d.Width && m.Height == d.Height && m.RefreshHz == target))
            {
                p.Warnings.Add($"{d.DeviceName} does not report {target} Hz at {d.Width}x{d.Height}.");
                continue;
            }
            if (target != d.RefreshHz) p.Changes.Add(new PreviewChange($"{d.MonitorName} ({d.DeviceName})", $"{d.Width}x{d.Height} @ {d.RefreshHz} Hz", $"{d.Width}x{d.Height} @ {target} Hz"));
        }
        p.Warnings.Add("You will be asked to confirm the picture is OK; the previous mode is restored automatically if you don't.");
        return Task.FromResult(p);
    }

    public override Task ApplyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        foreach (var (d, target) in Plan(ctx, request).ToList())
        {
            if (target == d.RefreshHz) continue;
            var mode = d.SupportedModes.FirstOrDefault(m => m.Width == d.Width && m.Height == d.Height && m.RefreshHz == target)
                       ?? throw new InvalidOperationException($"{target} Hz is not supported by {d.DeviceName} at the current resolution.");
            var test = ctx.Platform.Display.SetMode(d.DeviceName, mode with { BitsPerPixel = d.BitsPerPixel > 0 ? d.BitsPerPixel : mode.BitsPerPixel }, testOnly: true);
            if (!test.Success) throw new InvalidOperationException($"The display driver rejected {target} Hz: {test.Message}");
            ctx.AddBackup(record, new BackupItem
            {
                Kind = "display-mode", Description = $"{d.DeviceName}: {d.Width}x{d.Height} @ {d.RefreshHz} Hz",
                Data = { ["device"] = d.DeviceName, ["width"] = d.Width.ToString(), ["height"] = d.Height.ToString(), ["refresh"] = d.RefreshHz.ToString(), ["bpp"] = d.BitsPerPixel.ToString() },
            });
            var r = ctx.Platform.Display.SetMode(d.DeviceName, mode with { BitsPerPixel = d.BitsPerPixel > 0 ? d.BitsPerPixel : mode.BitsPerPixel }, testOnly: false);
            if (!r.Success) throw new InvalidOperationException($"Could not apply {target} Hz: {r.Message}");
            record.ChangesApplied.Add($"{d.DeviceName}: {d.RefreshHz} Hz → {target} Hz");
        }
        return Task.CompletedTask;
    }

    public override Task<VerificationResult> VerifyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        var displays = ctx.Platform.Display.GetDisplays();
        var msgs = new List<string>();
        var ok = true;
        foreach (var b in record.Backups.Where(b => b.Kind == "display-mode"))
        {
            var d = displays.FirstOrDefault(x => x.DeviceName == b.Get("device"));
            var want = record.ChangesApplied.FirstOrDefault(c => c.StartsWith(b.Get("device"), StringComparison.Ordinal))?.Split('→').Last().Trim();
            msgs.Add($"{b.Get("device")}: now {d?.RefreshHz} Hz");
            if (d is null || want is null || !want.StartsWith(d.RefreshHz.ToString(), StringComparison.Ordinal)) ok = false;
        }
        return Task.FromResult(ok ? VerificationResult.Ok(string.Join("; ", msgs)) : VerificationResult.Fail(string.Join("; ", msgs)));
    }
}

public sealed class VisualEffectsAction : OptimizationAction
{
    public override string Id => ActionIds.VisualEffects;
    public override string Title => "Visual effects";
    public override OptimizationCategory Category => OptimizationCategory.Display;
    public override string Description => "Turns Windows animations and shadows off ('Adjust for best performance') or back on, using the same documented switches as System Properties → Performance Options. Font smoothing (ClearType) is never changed.";
    public override string ExpectedBenefit => "Snappier-feeling menus and windows on low-end PCs (integrated graphics, 4 GB RAM). Little to no benefit on modern hardware.";
    public override string Risks => "Windows looks plainer. Every effect's previous state is backed up and restored on rollback.";
    public override Reversibility Reversibility => Reversibility.Full;

    public override Task<ActionInspection> InspectAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        var insp = new ActionInspection
        {
            OptionParameter = "mode",
            Options = { new() { Value = "performance", Label = "Best performance (effects off)" }, new() { Value = "appearance", Label = "Windows default (effects on)", IsRecommended = true } },
        };
        var on = 0;
        foreach (var e in UiEffects.All)
        {
            var v = ctx.Platform.Maintenance.GetUiEffect(e);
            if (v == true) on++;
            insp.Items.Add(new ActionItem { Id = e, Label = UiEffects.Describe(e), CurrentState = v switch { true => "On", false => "Off", _ => "Unknown" }, Selected = true });
        }
        insp.CurrentState = $"{on} of {UiEffects.All.Length} effects on";
        insp.AlreadyOptimal = true;
        return Task.FromResult(insp);
    }

    private static IEnumerable<string> Selected(ActionRequest r) => r.SelectedItems.Count > 0 ? r.SelectedItems.Intersect(UiEffects.All) : UiEffects.All;

    public override Task<ActionPreview> PreviewAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        var on = request.Param("mode", "performance") == "appearance";
        var p = NewPreview();
        foreach (var e in Selected(request))
        {
            var cur = ctx.Platform.Maintenance.GetUiEffect(e);
            if (cur != on) p.Changes.Add(new PreviewChange(UiEffects.Describe(e), cur == true ? "On" : cur == false ? "Off" : "Unknown", on ? "On" : "Off"));
        }
        return Task.FromResult(p);
    }

    public override Task ApplyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        var on = request.Param("mode", "performance") == "appearance";
        foreach (var e in Selected(request))
        {
            var cur = ctx.Platform.Maintenance.GetUiEffect(e);
            if (cur is null || cur == on) continue;
            ctx.AddBackup(record, new BackupItem { Kind = "ui-effect", Description = UiEffects.Describe(e), Data = { ["name"] = e, ["enabled"] = cur.Value.ToString() } });
            if (!ctx.Platform.Maintenance.SetUiEffect(e, on, out var err)) throw new InvalidOperationException($"{UiEffects.Describe(e)}: {err}");
            record.ChangesApplied.Add($"{UiEffects.Describe(e)}: {(on ? "On" : "Off")}");
        }
        return Task.CompletedTask;
    }

    public override Task<VerificationResult> VerifyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        var on = request.Param("mode", "performance") == "appearance";
        var wrong = record.Backups.Where(b => ctx.Platform.Maintenance.GetUiEffect(b.Get("name")) != on).Select(b => b.Description).ToList();
        return Task.FromResult(wrong.Count == 0 ? VerificationResult.Ok("All selected effects confirmed.") : VerificationResult.Fail($"Did not change: {string.Join(", ", wrong)}"));
    }
}

public sealed class MouseAccelerationAction : OptimizationAction
{
    public override string Id => "input.mouse-acceleration";
    public override string Title => "Enhance pointer precision (mouse acceleration)";
    public override OptimizationCategory Category => OptimizationCategory.Gaming;
    public override string Description => "Windows mouse acceleration makes the pointer move further when the mouse is moved quickly.";
    public override string ExpectedBenefit => "Turning it off gives consistent 1:1 aim in games (muscle memory). Many players prefer it off; for office use with a cheap mouse it can be helpful on.";
    public override string Risks => "None — preference only. Previous state restored on rollback.";
    public override Reversibility Reversibility => Reversibility.Full;

    public override Task<ActionInspection> InspectAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        var v = ctx.Platform.Maintenance.GetMouseAcceleration();
        return Task.FromResult(new ActionInspection
        {
            CurrentState = v switch { true => "On", false => "Off", _ => "Unknown" },
            Options = { new() { Value = "Off", Label = "Off (games)", IsCurrent = v == false, IsRecommended = true }, new() { Value = "On", Label = "On (Windows default)", IsCurrent = v == true } },
        });
    }

    public override Task<ActionPreview> PreviewAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        var v = ctx.Platform.Maintenance.GetMouseAcceleration();
        var on = request.Param("state", "Off") == "On";
        var p = NewPreview();
        if (v != on) p.Changes.Add(new PreviewChange("Enhance pointer precision", v == true ? "On" : "Off", on ? "On" : "Off"));
        return Task.FromResult(p);
    }

    public override Task ApplyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        var v = ctx.Platform.Maintenance.GetMouseAcceleration() ?? true;
        var on = request.Param("state", "Off") == "On";
        ctx.AddBackup(record, new BackupItem { Kind = "mouse-acceleration", Description = "Enhance pointer precision", Data = { ["enabled"] = v.ToString() } });
        if (!ctx.Platform.Maintenance.SetMouseAcceleration(on, out var err)) throw new InvalidOperationException(err);
        record.ChangesApplied.Add($"Enhance pointer precision: {(on ? "On" : "Off")}");
        return Task.CompletedTask;
    }

    public override Task<VerificationResult> VerifyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct) =>
        Task.FromResult(ctx.Platform.Maintenance.GetMouseAcceleration() == (request.Param("state", "Off") == "On")
            ? VerificationResult.Ok("Confirmed.") : VerificationResult.Fail("Did not change."));
}

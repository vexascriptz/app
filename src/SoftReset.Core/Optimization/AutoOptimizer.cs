using SoftReset.Core.Diagnostics;
using SoftReset.Core.Models;
using SoftReset.Core.Platform;
using SoftReset.Core.Safety;
using SoftReset.Core.Startup;

namespace SoftReset.Core.Optimization;

/// <summary>Which group of optimizations a one-click button covers.</summary>
public enum AutoScope { Everything, Graphics, Startup, Settings, Cleanup, Power }

/// <summary>The few hardware facts that decide how far auto-optimization should go.</summary>
public sealed class HardwareProfile
{
    public long TotalMemoryBytes { get; init; }
    public int LogicalProcessors { get; init; }
    public bool IsLaptop { get; init; }
    public bool HasDedicatedGpu { get; init; }

    /// <summary>
    /// Too weak to run Windows' visual effects smoothly: under 6 GB of RAM or 2 or fewer CPU threads.
    /// Only then does auto-optimization trade looks for speed.
    /// </summary>
    public bool IsLowEnd => (TotalMemoryBytes > 0 && TotalMemoryBytes < 6L * 1024 * 1024 * 1024) || (LogicalProcessors > 0 && LogicalProcessors <= 2);

    public static HardwareProfile Detect(ISystemPlatform p)
    {
        long mem = 0;
        try { mem = p.Live.Sample().MemoryTotalBytes; } catch { }
        bool laptop = false;
        try { laptop = p.Power.GetStatus().HasBattery == true; } catch { }
        bool dgpu = false;
        try { dgpu = p.Gpu.GetGpus(false).Any(g => !g.IsIntegrated && g.Vendor is GpuVendor.Nvidia or GpuVendor.Amd); } catch { }
        return new HardwareProfile { TotalMemoryBytes = mem, LogicalProcessors = Environment.ProcessorCount, IsLaptop = laptop, HasDedicatedGpu = dgpu };
    }
}

/// <summary>One change in an auto-optimization plan.</summary>
public sealed class AutoStep
{
    public required string Title { get; init; }
    public required string Detail { get; init; }
    public required ActionRequest Request { get; init; }
    public Reversibility Reversibility { get; init; }
    public bool RequiresAdmin { get; init; }
    public bool RequiresRestart { get; init; }
    public bool IsDisplayChange => Request.ActionId == ActionIds.RefreshRate;
    public bool IsSafetyStep => Request.ActionId == ActionIds.RestorePoint;
}

public sealed class AutoPlan
{
    public AutoScope Scope { get; init; }
    public HardwareProfile Hardware { get; init; } = new();
    public List<AutoStep> Steps { get; } = new();
    /// <summary>Things that were checked and are already optimal or not applicable (shown as "already good").</summary>
    public List<string> AlreadyGood { get; } = new();
    public int ChangeCount => Steps.Count(s => !s.IsSafetyStep);
}

/// <summary>
/// Builds and applies a one-click optimization plan. Every step is an existing action, chosen only
/// when inspection of this PC shows it would help, applied through the normal backup → apply →
/// verify pipeline. Visual quality is kept unless the hardware is too weak for it. Security
/// settings, Windows Update, updaters, user files, and peripheral/sync/security software are never
/// touched.
/// </summary>
public sealed class AutoOptimizer
{
    private readonly ActionRegistry _registry;
    private readonly ActionContext _ctx;

    public AutoOptimizer(ActionRegistry registry, ActionContext ctx)
    {
        _registry = registry;
        _ctx = ctx;
    }

    /// <summary>Windows settings applied by auto-optimization: setting id → control label. None of them changes how Windows looks.</summary>
    public static readonly IReadOnlyDictionary<string, string> RecommendedSettings = new Dictionary<string, string>
    {
        ["notifications.tips"] = "Turn off",
        ["notifications.welcome-experience"] = "Turn off",
        ["notifications.finish-setup"] = "Turn off",
        ["personalization.lockscreen-tips"] = "Turn off",
        ["personalization.start-suggestions-win10"] = "Turn off",
        ["explorer.sync-provider-notifications"] = "Turn off",
        ["privacy.advertising-id"] = "Turn off",
        ["privacy.tailored-experiences"] = "Turn off",
        ["privacy.feedback-frequency"] = "Never ask",
        ["registry.menu-show-delay"] = "Set 200 ms",
    };

    public async Task<AutoPlan> BuildAsync(AutoScope scope, HardwareProfile hw, CancellationToken ct = default)
    {
        var plan = new AutoPlan { Scope = scope, Hardware = hw };
        bool In(params AutoScope[] s) => scope == AutoScope.Everything || s.Contains(scope);

        if (In(AutoScope.Power))
        {
            var target = hw.IsLaptop ? WellKnownPowerSchemes.Balanced : WellKnownPowerSchemes.HighPerformance;
            await AddPowerPlanAsync(plan, target, ct);
            await AddOptionAsync(plan, ActionIds.ProcessorState, "value", "100", "CPU at full speed", ct);
        }
        if (In(AutoScope.Graphics))
        {
            await AddRefreshRateAsync(plan, ct);
            await AddOptionAsync(plan, ActionIds.GameMode, "state", "On", "Game Mode on", ct);
            await AddOptionAsync(plan, ActionIds.BackgroundRecording, "state", "Off", "Background game recording off", ct);
            await AddOptionAsync(plan, ActionIds.WindowedOptimizations, "state", "On", "Faster windowed games", ct);
            if (hw.HasDedicatedGpu && _ctx.Platform.OsBuild >= 19041)
                await AddOptionAsync(plan, ActionIds.Hags, "state", "On", "GPU scheduling on", ct);
            else plan.AlreadyGood.Add("GPU scheduling (needs a dedicated graphics card)");
        }
        if (In(AutoScope.Startup))
        {
            await AddStartupAsync(plan, ActionIds.StartupUserItems, machine: false, ct);
            await AddStartupAsync(plan, ActionIds.StartupMachineItems, machine: true, ct);
            await AddLogonTasksAsync(plan, ct);
        }
        if (In(AutoScope.Cleanup))
        {
            await AddSimpleAsync(plan, ActionIds.UserTemp, "Delete temporary files", ct);
            await AddSimpleAsync(plan, ActionIds.WindowsTemp, "Delete Windows temp files", ct);
        }
        if (In(AutoScope.Settings))
        {
            foreach (var (id, label) in RecommendedSettings)
                await AddSettingAsync(plan, id, label, ct);
        }
        if (scope == AutoScope.Everything && hw.IsLowEnd)
        {
            // Only on weak hardware: trade some looks for responsiveness.
            await AddVisualEffectsAsync(plan, ct);
            await AddOptionAsync(plan, ActionIds.Transparency, "state", "Off", "Transparency off (weak hardware)", ct);
        }

        if (plan.Steps.Any(s => s.RequiresAdmin || s.Reversibility != Reversibility.Full) && plan.ChangeCount > 0)
            await AddRestorePointAsync(plan, ct);
        return plan;
    }

    /// <summary>Applies the plan; admin steps share one UAC prompt. Returns one record per step.</summary>
    public Task<IReadOnlyList<ChangeRecord>> ApplyAsync(AutoPlan plan, ActionExecutor executor, IProgress<(int Done, int Total, string Title)>? progress = null, CancellationToken ct = default) =>
        executor.ApplyManyAsync(plan.Steps.Select(s => s.Request).ToList(), progress, ct);

    // ------------------------------------------------------------------ helpers

    private async Task<bool> TryAddAsync(AutoPlan plan, ActionRequest request, string title, CancellationToken ct, Func<ActionPreview, string>? detail = null)
    {
        if (!_registry.TryGet(request.ActionId, out var action)) return false;
        ActionPreview preview;
        try { preview = await action.PreviewAsync(_ctx, request, ct); }
        catch { return false; }
        if (preview.NothingToDo) { plan.AlreadyGood.Add(title); return false; }
        plan.Steps.Add(new AutoStep
        {
            Title = title,
            Detail = detail?.Invoke(preview) ?? (preview.Changes.Count == 1 ? $"{preview.Changes[0].From} → {preview.Changes[0].To}" : $"{preview.Changes.Count} changes"),
            Request = request,
            Reversibility = preview.Reversibility,
            RequiresAdmin = preview.RequiresAdmin || ActionExecutor.RequiresAdmin(action, request),
            RequiresRestart = preview.RequiresRestart || action.RequiresRestart,
        });
        return true;
    }

    private static bool IsRaw(string value) => value.Length > 0 && value.All(c => char.IsDigit(c) || c is '-' or 'x');

    private async Task<ActionInspection?> InspectAsync(string actionId, ActionRequest request, CancellationToken ct)
    {
        if (!_registry.TryGet(actionId, out var action)) return null;
        try
        {
            var insp = await action.InspectAsync(_ctx, request, ct);
            return insp.Applicable ? insp : null;
        }
        catch { return null; }
    }

    private async Task AddOptionAsync(AutoPlan plan, string actionId, string parameter, string value, string title, CancellationToken ct)
    {
        var request = new ActionRequest { ActionId = actionId, Parameters = { [parameter] = value } };
        if (await InspectAsync(actionId, request, ct) is null) { plan.AlreadyGood.Add(title); return; }
        // The title already says what happens ("Game Mode on"); raw registry values would only confuse.
        await TryAddAsync(plan, request, title, ct, p => p.Changes.Count == 1 && !IsRaw(p.Changes[0].From) ? $"{p.Changes[0].From} → {p.Changes[0].To}" : "");
    }

    private async Task AddSimpleAsync(AutoPlan plan, string actionId, string title, CancellationToken ct)
    {
        var request = new ActionRequest { ActionId = actionId };
        var insp = await InspectAsync(actionId, request, ct);
        if (insp is null || insp.AlreadyOptimal) { plan.AlreadyGood.Add(title); return; }
        await TryAddAsync(plan, request, title, ct, p => p.EstimatedBytesFreed is > 0 ? $"Frees {Parsing.ToolOutputParsers.FormatBytes(p.EstimatedBytesFreed)}" : "Clean up");
    }

    private async Task AddPowerPlanAsync(AutoPlan plan, string target, CancellationToken ct)
    {
        const string title = "Best power plan";
        var status = _ctx.Platform.Power.GetStatus();
        var active = status.ActiveSchemeGuid;
        // Laptops: only leave Power saver. Desktops: High performance unless a custom/OEM plan is active.
        var isBuiltIn = active.Equals(WellKnownPowerSchemes.Balanced, StringComparison.OrdinalIgnoreCase)
                        || active.Equals(WellKnownPowerSchemes.PowerSaver, StringComparison.OrdinalIgnoreCase);
        var change = plan.Hardware.IsLaptop
            ? active.Equals(WellKnownPowerSchemes.PowerSaver, StringComparison.OrdinalIgnoreCase)
            : isBuiltIn && !active.Equals(target, StringComparison.OrdinalIgnoreCase);
        if (!change) { plan.AlreadyGood.Add(title); return; }
        await TryAddAsync(plan, new ActionRequest { ActionId = ActionIds.PowerPlan, Parameters = { ["scheme"] = target } }, title, ct);
    }

    private async Task AddRefreshRateAsync(AutoPlan plan, CancellationToken ct)
    {
        const string title = "Smoothest monitor refresh rate";
        var insp = await InspectAsync(ActionIds.RefreshRate, new ActionRequest { ActionId = ActionIds.RefreshRate }, ct);
        var items = insp?.Items.Where(i => i.Selected && i.Selectable).Select(i => i.Id).ToList() ?? new();
        if (items.Count == 0) { plan.AlreadyGood.Add(title); return; }
        await TryAddAsync(plan, new ActionRequest { ActionId = ActionIds.RefreshRate, SelectedItems = items }, title, ct);
    }

    private async Task AddStartupAsync(AutoPlan plan, string actionId, bool machine, CancellationToken ct)
    {
        var title = machine ? "Turn off unneeded startup apps (all users)" : "Turn off unneeded startup apps";
        var entries = new StartupInventory(_ctx.Platform.Registry, _ctx.Platform.Folders, _ctx.Platform.FileExists).Enumerate()
            .Where(e => e.MachineWide == machine && e.Enabled && e.Location != StartupLocation.ScheduledTaskLogon)
            .Where(e => AutoOptimizePolicy.ShouldDisableStartup(e.Name, e.Command, e.FileExists))
            .Select(e => e.Id).ToList();
        if (entries.Count == 0) { plan.AlreadyGood.Add(title); return; }
        await TryAddAsync(plan, new ActionRequest { ActionId = actionId, Parameters = { ["mode"] = "disable" }, SelectedItems = entries }, title, ct,
            p => p.Changes.Count == 1 ? p.Changes[0].Target.Split(" (")[0] : string.Join(", ", p.Changes.Take(4).Select(c => c.Target.Split(" (")[0])) + (p.Changes.Count > 4 ? $" +{p.Changes.Count - 4} more" : ""));
    }

    private async Task AddLogonTasksAsync(AutoPlan plan, CancellationToken ct)
    {
        const string title = "Turn off leftover startup tasks";
        var tasks = _ctx.Platform.Tasks.GetTasks()
            .Where(t => !t.IsMicrosoft && t.Enabled && (t.HasLogonTrigger || t.HasBootTrigger))
            .Where(t => AutoOptimizePolicy.ShouldDisableTask(t.Name, t.Action, CommandLine.ExtractExecutable(t.Action) is not { } exe || _ctx.Platform.FileExists(Environment.ExpandEnvironmentVariables(exe))))
            .Select(t => t.Path).ToList();
        if (tasks.Count == 0) { plan.AlreadyGood.Add(title); return; }
        await TryAddAsync(plan, new ActionRequest { ActionId = ActionIds.StartupLogonTasks, Parameters = { ["mode"] = "disable" }, SelectedItems = tasks }, title, ct);
    }

    private async Task AddSettingAsync(AutoPlan plan, string settingId, string controlLabel, CancellationToken ct)
    {
        var def = _ctx.Knowledge.FindSetting(settingId);
        if (def is null) return;
        var build = _ctx.Platform.OsBuild;
        if ((def.MinBuild > 0 && build < def.MinBuild) || (def.MaxBuild is { } max && build > max)) return;
        var index = def.Controls.FindIndex(c => c.Label == controlLabel);
        if (index < 0) return;
        var title = def.Controls[index].ResultLabel is { Length: > 0 } r ? $"{def.Name}: {r}" : def.Name;
        await TryAddAsync(plan, new ActionRequest { ActionId = ActionIds.SettingRegistrySet, Parameters = { ["setting"] = settingId, ["control"] = index.ToString() } }, title, ct);
    }

    private async Task AddVisualEffectsAsync(AutoPlan plan, CancellationToken ct)
    {
        const string title = "Lighter animations (weak hardware)";
        var insp = await InspectAsync(ActionIds.VisualEffects, new ActionRequest { ActionId = ActionIds.VisualEffects }, ct);
        if (insp is null) return;
        // Keep the effects that make text and windows look right; turn off the costly animations.
        var items = insp.Items.Where(i => i.CurrentState == "On" && AutoOptimizePolicy.IsCostlyEffect(i.Id)).Select(i => i.Id).ToList();
        if (items.Count == 0) { plan.AlreadyGood.Add(title); return; }
        await TryAddAsync(plan, new ActionRequest { ActionId = ActionIds.VisualEffects, Parameters = { ["mode"] = "performance" }, SelectedItems = items }, title, ct);
    }

    private async Task AddRestorePointAsync(AutoPlan plan, CancellationToken ct)
    {
        var request = new ActionRequest { ActionId = ActionIds.RestorePoint, Parameters = { ["description"] = "Before softreset Auto optimize" } };
        if (await InspectAsync(ActionIds.RestorePoint, request, ct) is null) return;
        if (!_registry.TryGet(ActionIds.RestorePoint, out var action)) return;
        plan.Steps.Insert(0, new AutoStep
        {
            Title = "Create a restore point first",
            Detail = "Safety backup",
            Request = request,
            Reversibility = Reversibility.None,
            RequiresAdmin = action.RequiresAdmin,
        });
    }
}

/// <summary>The decisions behind auto-optimization, kept separate so they can be tested and reviewed.</summary>
public static class AutoOptimizePolicy
{
    /// <summary>Startup programs that are fine to stop auto-starting: launchers, store helpers, updaters' tray apps, and known clutter. They still work when opened.</summary>
    private static readonly string[] DisableStartup =
    {
        "steam", "epicgames", "epic games", "eadesktop", "ea desktop", "origin", "ubisoft", "uplay", "battle.net", "gog galaxy", "galaxyclient",
        "spotify", "microsoftedgeautolaunch", "msedge", "opera", "browser_assistant", "ccleaner", "creative cloud", "adobe acc", "adobegcclient",
        "overwolf", "skype", "ituneshelper", "itunes", "cortana", "utorrent", "bittorrent", "jusched", "java update", "wargaming", "riotclient",
        "amazon music", "deezer", "tidal", "wallpaper engine", "msi center", "armoury crate",
    };

    /// <summary>Never auto-disabled: security, drivers, input devices, cloud sync, chat, password managers, peripherals.</summary>
    private static readonly string[] KeepStartup =
    {
        "securityhealth", "defender", "windows security", "realtek", "rtkaud", "audio", "nvidia", "amd", "radeon", "intel", "igfx", "synaptics", "elan", "touchpad",
        "onedrive", "dropbox", "google drive", "googledrive", "icloud", "box drive", "mega", "sync",
        "discord", "teams", "slack", "zoom", "whatsapp", "telegram", "signal",
        "logitech", "lghub", "razer", "corsair", "icue", "steelseries", "hyperx", "wacom",
        "antivirus", "malwarebytes", "avast", "avg", "norton", "mcafee", "kaspersky", "eset", "bitdefender", "sophos", "webroot",
        "1password", "bitwarden", "lastpass", "keepass", "dashlane", "vpn", "nordvpn", "expressvpn",
    };

    /// <summary>Scheduled sign-in tasks that are known clutter (updaters for browsers and security software are kept for safety).</summary>
    private static readonly string[] DisableTasks = { "opera", "browser assistant", "ccleaner", "overwolf" };

    private static bool Matches(string text, IEnumerable<string> words) => words.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));

    public static bool ShouldDisableStartup(string name, string command, bool targetExists)
    {
        if (!targetExists) return true; // points at a deleted program
        var text = $"{name} {command}";
        if (Matches(text, KeepStartup)) return false;
        return Matches(text, DisableStartup);
    }

    public static bool ShouldDisableTask(string name, string action, bool targetExists)
    {
        if (!targetExists) return true;
        var text = $"{name} {action}";
        if (Matches(text, KeepStartup)) return false;
        return Matches(text, DisableTasks);
    }

    /// <summary>Animations that cost the most on weak hardware; window shadows and window contents while dragging stay on.</summary>
    public static bool IsCostlyEffect(string effectId) =>
        effectId is UiEffects.WindowAnimation or UiEffects.ClientAreaAnimation or UiEffects.MenuAnimation or UiEffects.TooltipAnimation
            or UiEffects.ComboBoxAnimation or UiEffects.ListBoxSmoothScrolling or UiEffects.SelectionFade or UiEffects.CursorShadow;
}

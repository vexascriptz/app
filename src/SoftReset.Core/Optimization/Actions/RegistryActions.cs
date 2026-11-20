using SoftReset.Core.Diagnostics;
using SoftReset.Core.Platform;
using SoftReset.Core.Safety;

namespace SoftReset.Core.Optimization.Actions;

internal static class Paths
{
    public const string ExplorerAdvanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    public const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    public const string GameBar = @"Software\Microsoft\GameBar";
    public const string GameDvr = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";
    public const string GraphicsDrivers = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    public const string SessionPower = @"SYSTEM\CurrentControlSet\Control\Session Manager\Power";
    public const string InternetSettings = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    public const string UserGpuPreferences = @"Software\Microsoft\DirectX\UserGpuPreferences";
}

public sealed class ShowFileExtensionsAction : RegistryOptionsAction
{
    public override string Id => ActionIds.FileExtensions;
    public override string Title => "Show file name extensions";
    public override OptimizationCategory Category => OptimizationCategory.Desktop;
    public override string Description => "Shows extensions such as .exe, .pdf, and .docx in File Explorer.";
    public override string ExpectedBenefit => "Customers can tell documents from programs — this helps them spot fake 'invoice.pdf.exe' attachments.";
    public override string Risks => "None. Purely visual; renaming files becomes slightly more error-prone because the extension is visible.";
    protected override IReadOnlyList<RegTarget> Targets { get; } = new[] { new RegTarget(RegHive.CurrentUser, Paths.ExplorerAdvanced, "HideFileExt", RegKind.DWord) };
    protected override IReadOnlyDictionary<string, object?[]> OptionValues { get; } = new Dictionary<string, object?[]> { ["Show"] = new object?[] { 0 }, ["Hide"] = new object?[] { 1 } };
    protected override string RecommendedOption => "Show";
    protected override string? DefaultWhenMissing => "Hide";
    public override string Measurement => "Open File Explorer (press F5 to refresh open windows).";
}

public sealed class ShowHiddenFilesAction : RegistryOptionsAction
{
    public override string Id => ActionIds.HiddenFiles;
    public override string Title => "Show hidden files and folders";
    public override OptimizationCategory Category => OptimizationCategory.Desktop;
    public override string Description => "Shows items marked hidden (for example the AppData folder) in File Explorer. Protected operating-system files stay hidden.";
    public override string ExpectedBenefit => "Easier troubleshooting and access to application data folders.";
    public override string Risks => "Customers may see and accidentally modify hidden application folders. Usually best switched back to Hide at the end of a session.";
    protected override IReadOnlyList<RegTarget> Targets { get; } = new[] { new RegTarget(RegHive.CurrentUser, Paths.ExplorerAdvanced, "Hidden", RegKind.DWord) };
    protected override IReadOnlyDictionary<string, object?[]> OptionValues { get; } = new Dictionary<string, object?[]> { ["Show"] = new object?[] { 1 }, ["Hide"] = new object?[] { 2 } };
    protected override string RecommendedOption => "Hide";
    protected override string? DefaultWhenMissing => "Hide";
}

public sealed class TaskbarAlignmentAction : RegistryOptionsAction
{
    public override string Id => ActionIds.TaskbarAlignment;
    public override string Title => "Taskbar alignment (Windows 11)";
    public override OptimizationCategory Category => OptimizationCategory.Desktop;
    public override string Description => "Places the Start button and taskbar icons in the centre (Windows 11 default) or on the left like Windows 10.";
    public override string ExpectedBenefit => "Familiar layout for customers upgrading from Windows 10.";
    public override string Risks => "None. Purely cosmetic.";
    public override int MinBuild => 22000;
    protected override IReadOnlyList<RegTarget> Targets { get; } = new[] { new RegTarget(RegHive.CurrentUser, Paths.ExplorerAdvanced, "TaskbarAl", RegKind.DWord) };
    protected override IReadOnlyDictionary<string, object?[]> OptionValues { get; } = new Dictionary<string, object?[]> { ["Center"] = new object?[] { 1 }, ["Left"] = new object?[] { 0 } };
    protected override string RecommendedOption => "Center";
    protected override string? DefaultWhenMissing => "Center";
}

public sealed class DarkModeAction : RegistryOptionsAction
{
    public override string Id => ActionIds.DarkMode;
    public override string Title => "Light or dark mode";
    public override OptimizationCategory Category => OptimizationCategory.Desktop;
    public override string Description => "Switches Windows and app colour mode between light and dark (Settings → Personalization → Colors).";
    public override string ExpectedBenefit => "Customer preference; dark mode can reduce glare in dim rooms.";
    public override string Risks => "None. Some older apps ignore the setting.";
    protected override IReadOnlyList<RegTarget> Targets { get; } = new[]
    {
        new RegTarget(RegHive.CurrentUser, Paths.Personalize, "AppsUseLightTheme", RegKind.DWord),
        new RegTarget(RegHive.CurrentUser, Paths.Personalize, "SystemUsesLightTheme", RegKind.DWord),
    };
    protected override IReadOnlyDictionary<string, object?[]> OptionValues { get; } = new Dictionary<string, object?[]>
    {
        ["Light"] = new object?[] { 1, 1 },
        ["Dark"] = new object?[] { 0, 0 },
        ["Dark taskbar, light apps"] = new object?[] { 1, 0 },
    };
    protected override string RecommendedOption => "Light";
    protected override string? DefaultWhenMissing => "Light";
}

public sealed class TransparencyAction : RegistryOptionsAction
{
    public override string Id => ActionIds.Transparency;
    public override string Title => "Transparency effects";
    public override OptimizationCategory Category => OptimizationCategory.Desktop;
    public override string Description => "Translucent (acrylic/mica) backgrounds on the taskbar, Start, and windows.";
    public override string ExpectedBenefit => "Turning it off slightly reduces GPU work on very low-end or integrated-graphics PCs and can improve readability.";
    public override string Risks => "None. Purely visual.";
    protected override IReadOnlyList<RegTarget> Targets { get; } = new[] { new RegTarget(RegHive.CurrentUser, Paths.Personalize, "EnableTransparency", RegKind.DWord) };
    protected override IReadOnlyDictionary<string, object?[]> OptionValues { get; } = new Dictionary<string, object?[]> { ["On"] = new object?[] { 1 }, ["Off"] = new object?[] { 0 } };
    protected override string RecommendedOption => "On";
    protected override string? DefaultWhenMissing => "On";
}

public sealed class ExplorerThisPcAction : RegistryOptionsAction
{
    public override string Id => ActionIds.ExplorerThisPc;
    public override string Title => "File Explorer opens to";
    public override OptimizationCategory Category => OptimizationCategory.Desktop;
    public override string Description => "Chooses whether File Explorer opens to Home/Quick access or This PC (drives and folders).";
    public override string ExpectedBenefit => "This PC shows drives and free space immediately; Home shows recent files.";
    public override string Risks => "None.";
    protected override IReadOnlyList<RegTarget> Targets { get; } = new[] { new RegTarget(RegHive.CurrentUser, Paths.ExplorerAdvanced, "LaunchTo", RegKind.DWord) };
    protected override IReadOnlyDictionary<string, object?[]> OptionValues { get; } = new Dictionary<string, object?[]> { ["This PC"] = new object?[] { 1 }, ["Home / Quick access"] = new object?[] { 2 } };
    protected override string RecommendedOption => "This PC";
    protected override string? DefaultWhenMissing => "Home / Quick access";
}

public sealed class GameModeAction : RegistryOptionsAction
{
    public override string Id => ActionIds.GameMode;
    public override string Title => "Game Mode";
    public override OptimizationCategory Category => OptimizationCategory.Gaming;
    public override string Description => "Game Mode prioritises the foreground game and stops Windows Update from installing drivers or showing restart prompts while playing.";
    public override string ExpectedBenefit => "Fewer interruptions and background installs during games. Measured FPS changes are usually small.";
    public override string Risks => "None known; it is the Windows default.";
    protected override IReadOnlyList<RegTarget> Targets { get; } = new[] { new RegTarget(RegHive.CurrentUser, Paths.GameBar, "AutoGameModeEnabled", RegKind.DWord) };
    protected override IReadOnlyDictionary<string, object?[]> OptionValues { get; } = new Dictionary<string, object?[]> { ["On"] = new object?[] { 1 }, ["Off"] = new object?[] { 0 } };
    protected override string RecommendedOption => "On";
    protected override string? DefaultWhenMissing => "On";
}

public sealed class BackgroundRecordingAction : RegistryOptionsAction
{
    public override string Id => ActionIds.BackgroundRecording;
    public override string Title => "Xbox Game Bar background recording";
    public override OptimizationCategory Category => OptimizationCategory.Gaming;
    public override string Description => "'Record what happened' keeps a rolling video buffer of gameplay so the last minutes can be saved.";
    public override string ExpectedBenefit => "Turning it off frees the GPU video encoder, some memory, and avoids constant disk writes. Gains are small on modern GPUs, more noticeable on low-end systems.";
    public override string Risks => "The customer can no longer save clips of something that already happened. Manual recording still works.";
    protected override IReadOnlyList<RegTarget> Targets { get; } = new[] { new RegTarget(RegHive.CurrentUser, Paths.GameDvr, "HistoricalCaptureEnabled", RegKind.DWord) };
    protected override IReadOnlyDictionary<string, object?[]> OptionValues { get; } = new Dictionary<string, object?[]> { ["Off"] = new object?[] { 0 }, ["On"] = new object?[] { 1 } };
    protected override string RecommendedOption => "Off";
    protected override string? DefaultWhenMissing => "Off";
}

public sealed class HagsAction : RegistryOptionsAction
{
    public override string Id => ActionIds.Hags;
    public override string Title => "Hardware-accelerated GPU scheduling";
    public override OptimizationCategory Category => OptimizationCategory.Gaming;
    public override string Description => "Lets the GPU manage its own video memory scheduling (Settings → System → Display → Graphics → Advanced). Required for DLSS Frame Generation on NVIDIA RTX 40/50 GPUs.";
    public override string ExpectedBenefit => "Can slightly reduce latency and CPU overhead; required for frame generation. Effects vary by game and driver — measure before and after.";
    public override string Risks => "Some older games and capture tools stutter with it on. Has no effect if the GPU or driver does not support it.";
    public override bool RequiresRestart => true;
    protected override IReadOnlyList<RegTarget> Targets { get; } = new[] { new RegTarget(RegHive.LocalMachine, Paths.GraphicsDrivers, "HwSchMode", RegKind.DWord) };
    protected override IReadOnlyDictionary<string, object?[]> OptionValues { get; } = new Dictionary<string, object?[]> { ["On"] = new object?[] { 2 }, ["Off"] = new object?[] { 1 } };
    protected override string RecommendedOption => "On";
    protected override void AddPreviewWarnings(ActionPreview preview, string target) =>
        preview.Warnings.Add("Only takes effect on GPUs and drivers that support it (WDDM 2.7+). A restart is required.");
}

public sealed class FastStartupAction : RegistryOptionsAction
{
    public override string Id => ActionIds.FastStartup;
    public override string Title => "Fast Startup";
    public override OptimizationCategory Category => OptimizationCategory.Power;
    public override string Description => "Fast Startup makes 'Shut down' save the Windows kernel to disk instead of fully closing it, so the next boot is quicker.";
    public override string ExpectedBenefit => "Turning it off makes every shutdown a clean restart, which resolves driver glitches that survive shutdowns, Wake-on-LAN issues, and dual-boot file system problems.";
    public override string Risks => "Cold boots may take a few seconds longer. Turning it on again restores the previous behaviour.";
    protected override IReadOnlyList<RegTarget> Targets { get; } = new[] { new RegTarget(RegHive.LocalMachine, Paths.SessionPower, "HiberbootEnabled", RegKind.DWord) };
    protected override IReadOnlyDictionary<string, object?[]> OptionValues { get; } = new Dictionary<string, object?[]> { ["Off"] = new object?[] { 0 }, ["On"] = new object?[] { 1 } };
    protected override string RecommendedOption => "Off";
    protected override string? DefaultWhenMissing => "On";
    public override string Measurement => "Takes effect at the next shutdown.";
}

public sealed class DisableProxyAction : RegistryOptionsAction
{
    public override string Id => ActionIds.DisableProxy;
    public override string Title => "Manual proxy server";
    public override OptimizationCategory Category => OptimizationCategory.Network;
    public override string Description => "Turns the manually configured web proxy off or on. The proxy address is kept, so it can be turned back on.";
    public override string ExpectedBenefit => "Fixes 'no internet' in browsers caused by a stale proxy left by VPNs, filters, or unwanted software.";
    public override string Risks => "If the customer's workplace or school requires the proxy, sites will stop loading until it is re-enabled.";
    protected override IReadOnlyList<RegTarget> Targets { get; } = new[] { new RegTarget(RegHive.CurrentUser, Paths.InternetSettings, "ProxyEnable", RegKind.DWord) };
    protected override IReadOnlyDictionary<string, object?[]> OptionValues { get; } = new Dictionary<string, object?[]> { ["Off"] = new object?[] { 0 }, ["On"] = new object?[] { 1 } };
    protected override string RecommendedOption => "Off";
    protected override string? DefaultWhenMissing => "Off";
}

/// <summary>Edits one key in the semicolon-delimited DirectXUserGlobalSettings string without touching the others.</summary>
public sealed class WindowedOptimizationsAction : OptimizationAction
{
    public override string Id => ActionIds.WindowedOptimizations;
    public override string Title => "Optimizations for windowed games";
    public override OptimizationCategory Category => OptimizationCategory.Gaming;
    public override string Description => "Lets DirectX 10/11 games running in a window or borderless window use the modern flip presentation model (Settings → Display → Graphics → Change default graphics settings).";
    public override string ExpectedBenefit => "Lower latency in windowed/borderless games and enables Auto HDR and VRR for them.";
    public override string Risks => "A small number of older games show glitches; turn it back off for those.";
    public override Reversibility Reversibility => Reversibility.Full;
    public override int MinBuild => 22621;
    private const string ValueName = "DirectXUserGlobalSettings";
    private const string Key = "SwapEffectUpgradeEnable";

    internal static string? GetFlag(string? settings, string key) =>
        (settings ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2)).Where(p => p.Length == 2 && p[0].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
            .Select(p => p[1].Trim()).FirstOrDefault();

    internal static string SetFlag(string? settings, string key, string value)
    {
        var parts = (settings ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        var idx = parts.FindIndex(p => p.Split('=', 2)[0].Equals(key, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0) parts[idx] = $"{key}={value}"; else parts.Add($"{key}={value}");
        return string.Join(";", parts) + ";";
    }

    private string Current(ActionContext ctx) =>
        GetFlag(ctx.Platform.Registry.GetValue(RegHive.CurrentUser, Paths.UserGpuPreferences, ValueName)?.AsString(), Key) == "1" ? "On" : "Off";

    public override Task<ActionInspection> InspectAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        if (ctx.Platform.OsBuild < MinBuild) return Task.FromResult(ActionInspection.NotApplicable("Requires Windows 11 version 22H2 or later."));
        var cur = Current(ctx);
        return Task.FromResult(new ActionInspection
        {
            CurrentState = cur,
            AlreadyOptimal = cur == "On",
            Options = { new() { Value = "On", Label = "On", IsCurrent = cur == "On", IsRecommended = true }, new() { Value = "Off", Label = "Off", IsCurrent = cur == "Off" } },
        });
    }

    public override Task<ActionPreview> PreviewAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        var p = NewPreview();
        var target = request.Param("state", "On");
        var cur = Current(ctx);
        if (cur != target) p.Changes.Add(new PreviewChange($@"HKCU\{Paths.UserGpuPreferences}\{ValueName} → {Key}", cur, target));
        return Task.FromResult(p);
    }

    public override Task ApplyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        var reg = ctx.Platform.Registry;
        var target = request.Param("state", "On") == "On" ? "1" : "0";
        ctx.AddBackup(record, RegistryBackup.Capture(reg, RegHive.CurrentUser, Paths.UserGpuPreferences, ValueName));
        var existing = reg.GetValue(RegHive.CurrentUser, Paths.UserGpuPreferences, ValueName)?.AsString();
        var updated = SetFlag(existing, Key, target);
        reg.SetValue(RegHive.CurrentUser, Paths.UserGpuPreferences, ValueName, RegValue.Str(updated));
        record.ChangesApplied.Add($"{ValueName} = {updated}");
        return Task.CompletedTask;
    }

    public override Task<VerificationResult> VerifyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct) =>
        Task.FromResult(Current(ctx) == request.Param("state", "On")
            ? VerificationResult.Ok("Setting confirmed. Restart running games for it to apply.")
            : VerificationResult.Fail("The value did not change."));
}

/// <summary>Per-application GPU preference, the same value Settings → Display → Graphics writes.</summary>
public sealed class GpuPreferenceAction : OptimizationAction
{
    public override string Id => ActionIds.GpuPreference;
    public override string Title => "Per-app graphics preference";
    public override OptimizationCategory Category => OptimizationCategory.Gaming;
    public override string Description => "Tells Windows which GPU an application should use (Settings → System → Display → Graphics). Useful on laptops where a game runs on the integrated GPU.";
    public override string ExpectedBenefit => "Games forced onto the dedicated GPU can run several times faster on hybrid-graphics laptops.";
    public override string Risks => "Higher power use and heat for that app on battery. Some apps ignore the preference.";
    public override Reversibility Reversibility => Reversibility.Full;

    private static string Label(string? v) => WindowedOptimizationsAction.GetFlag(v, "GpuPreference") switch
    {
        "2" => "High performance",
        "1" => "Power saving",
        "0" => "Let Windows decide",
        _ => "Let Windows decide (default)",
    };

    public override Task<ActionInspection> InspectAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        var reg = ctx.Platform.Registry;
        var insp = new ActionInspection { CurrentState = "Choose an application executable", OptionParameter = "preference" };
        foreach (var name in reg.GetValueNames(RegHive.CurrentUser, Paths.UserGpuPreferences).Where(n => n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
            insp.Items.Add(new ActionItem { Id = name, Label = Path.GetFileName(name), Detail = name, CurrentState = Label(reg.GetValue(RegHive.CurrentUser, Paths.UserGpuPreferences, name)?.AsString()) });
        insp.Options.Add(new ActionOption { Value = "2", Label = "High performance", IsRecommended = true });
        insp.Options.Add(new ActionOption { Value = "1", Label = "Power saving" });
        insp.Options.Add(new ActionOption { Value = "0", Label = "Let Windows decide" });
        return Task.FromResult(insp);
    }

    private static IEnumerable<string> Targets(ActionRequest r)
    {
        var exe = r.Param("exePath");
        if (!string.IsNullOrWhiteSpace(exe)) yield return exe;
        foreach (var s in r.SelectedItems) yield return s;
    }

    public override Task<ActionPreview> PreviewAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        var p = NewPreview();
        var pref = request.Param("preference", "2");
        foreach (var exe in Targets(request).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) { p.Warnings.Add($"Not an executable: {exe}"); continue; }
            var cur = ctx.Platform.Registry.GetValue(RegHive.CurrentUser, Paths.UserGpuPreferences, exe)?.AsString();
            p.Changes.Add(new PreviewChange(exe, Label(cur), Label($"GpuPreference={pref};")));
        }
        return Task.FromResult(p);
    }

    public override Task ApplyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        var pref = request.Param("preference", "2");
        if (pref is not ("0" or "1" or "2")) throw new ArgumentException("Preference must be 0, 1, or 2.");
        foreach (var exe in Targets(request).Distinct(StringComparer.OrdinalIgnoreCase).Where(e => e.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
        {
            var reg = ctx.Platform.Registry;
            ctx.AddBackup(record, RegistryBackup.Capture(reg, RegHive.CurrentUser, Paths.UserGpuPreferences, exe));
            var existing = reg.GetValue(RegHive.CurrentUser, Paths.UserGpuPreferences, exe)?.AsString();
            reg.SetValue(RegHive.CurrentUser, Paths.UserGpuPreferences, exe, RegValue.Str(WindowedOptimizationsAction.SetFlag(existing, "GpuPreference", pref)));
            record.ChangesApplied.Add($"{exe}: {Label($"GpuPreference={pref};")}");
        }
        return Task.CompletedTask;
    }

    public override Task<VerificationResult> VerifyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        var pref = request.Param("preference", "2");
        var bad = Targets(request).Where(e => WindowedOptimizationsAction.GetFlag(ctx.Platform.Registry.GetValue(RegHive.CurrentUser, Paths.UserGpuPreferences, e)?.AsString(), "GpuPreference") != pref).ToList();
        return Task.FromResult(bad.Count == 0 ? VerificationResult.Ok("Preference stored. Restart the application for it to take effect.") : VerificationResult.Fail($"Not stored for: {string.Join(", ", bad)}"));
    }
}

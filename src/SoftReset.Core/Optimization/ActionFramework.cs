using SoftReset.Core.Knowledge;
using SoftReset.Core.Platform;
using SoftReset.Core.Safety;
using SoftReset.Core.Storage;

namespace SoftReset.Core.Optimization;

public enum OptimizationCategory
{
    Startup,
    Storage,
    Background,
    Power,
    Gaming,
    Display,
    Maintenance,
    AppMaintenance,
    Network,
    Desktop,
    Settings,
}

public sealed class ActionItem
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Detail { get; set; } = "";
    public string? CurrentState { get; set; }
    public string? Recommendation { get; set; }
    public string? Warning { get; set; }
    public bool Selected { get; set; }
    public bool Selectable { get; set; } = true;
    public long? SizeBytes { get; set; }
}

public sealed class ActionOption
{
    public string Value { get; set; } = "";
    public string Label { get; set; } = "";
    public bool IsCurrent { get; set; }
    public bool IsRecommended { get; set; }
}

public sealed class ActionInspection
{
    public bool Applicable { get; set; } = true;
    public string? NotApplicableReason { get; set; }
    public string CurrentState { get; set; } = "";
    public bool AlreadyOptimal { get; set; }
    public List<ActionItem> Items { get; set; } = new();
    /// <summary>For single-choice actions (e.g. power plan, on/off), the selectable target states.</summary>
    public List<ActionOption> Options { get; set; } = new();
    public string OptionParameter { get; set; } = "state";
    public List<string> Notes { get; set; } = new();

    public static ActionInspection NotApplicable(string reason) => new() { Applicable = false, NotApplicableReason = reason, CurrentState = reason };
}

public sealed class PreviewChange
{
    public PreviewChange() { }
    public PreviewChange(string target, string from, string to) { Target = target; From = from; To = to; }
    public string Target { get; set; } = "";
    public string From { get; set; } = "";
    public string To { get; set; } = "";
}

public sealed class ActionPreview
{
    public List<PreviewChange> Changes { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public bool RequiresAdmin { get; set; }
    public bool RequiresRestart { get; set; }
    public Reversibility Reversibility { get; set; }
    public long? EstimatedBytesFreed { get; set; }
    public bool NothingToDo => Changes.Count == 0;
}

public sealed class ActionRequest
{
    public string ActionId { get; set; } = "";
    public Dictionary<string, string> Parameters { get; set; } = new();
    public List<string> SelectedItems { get; set; } = new();
    public string? SessionId { get; set; }

    public string Param(string key, string fallback = "") => Parameters.TryGetValue(key, out var v) ? v : fallback;
}

public sealed class VerificationResult
{
    public bool Verified { get; set; }
    public string Message { get; set; } = "";
    public static VerificationResult Ok(string m) => new() { Verified = true, Message = m };
    public static VerificationResult Fail(string m) => new() { Verified = false, Message = m };
}

public sealed class ActionContext
{
    public required ISystemPlatform Platform { get; init; }
    public required ChangeJournal Journal { get; init; }
    public required AppPaths Paths { get; init; }
    public KnowledgeBase Knowledge { get; init; } = KnowledgeBase.Default;
    public Action<string> Log { get; init; } = _ => { };

    /// <summary>Persists the record so backups captured so far survive a crash (write-ahead).</summary>
    public void Checkpoint(ChangeRecord record) => Journal.Save(record);

    public void AddBackup(ChangeRecord record, BackupItem item)
    {
        record.Backups.Add(item);
        Checkpoint(record);
    }
}

public interface IOptimizationAction
{
    string Id { get; }
    string Title { get; }
    OptimizationCategory Category { get; }
    string Description { get; }
    string ExpectedBenefit { get; }
    string Risks { get; }
    bool RequiresAdmin { get; }
    bool RequiresRestart { get; }
    Reversibility Reversibility { get; }
    /// <summary>How the benefit can be measured afterwards.</summary>
    string Measurement { get; }
    /// <summary>Hidden from the optimization catalog (used by other screens, e.g. Settings Explorer).</summary>
    bool Internal { get; }
    int MinBuild { get; }

    Task<ActionInspection> InspectAsync(ActionContext ctx, ActionRequest request, CancellationToken ct);
    Task<ActionPreview> PreviewAsync(ActionContext ctx, ActionRequest request, CancellationToken ct);
    Task ApplyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct);
    Task<VerificationResult> VerifyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct);
    Task RollbackAsync(ActionContext ctx, ChangeRecord record, CancellationToken ct);
}

public abstract class OptimizationAction : IOptimizationAction
{
    public abstract string Id { get; }
    public abstract string Title { get; }
    public abstract OptimizationCategory Category { get; }
    public abstract string Description { get; }
    public abstract string ExpectedBenefit { get; }
    public abstract string Risks { get; }
    public virtual bool RequiresAdmin => false;
    public virtual bool RequiresRestart => false;
    public abstract Reversibility Reversibility { get; }
    public virtual string Measurement => "Verified by re-reading the changed configuration.";
    public virtual bool Internal => false;
    public virtual int MinBuild => 0;

    public abstract Task<ActionInspection> InspectAsync(ActionContext ctx, ActionRequest request, CancellationToken ct);
    public abstract Task<ActionPreview> PreviewAsync(ActionContext ctx, ActionRequest request, CancellationToken ct);
    public abstract Task ApplyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct);
    public abstract Task<VerificationResult> VerifyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct);

    public virtual Task RollbackAsync(ActionContext ctx, ChangeRecord record, CancellationToken ct)
    {
        if (Reversibility == Reversibility.None)
            throw new InvalidOperationException("This change cannot be undone automatically.");
        foreach (var b in Enumerable.Reverse(record.Backups))
            BackupRestorer.Restore(ctx, b);
        AfterRegistryChange(ctx);
        return Task.CompletedTask;
    }

    protected virtual void AfterRegistryChange(ActionContext ctx) { }

    protected ActionPreview NewPreview() => new()
    {
        RequiresAdmin = RequiresAdmin,
        RequiresRestart = RequiresRestart,
        Reversibility = Reversibility,
    };
}

/// <summary>Restores every backup kind the console creates.</summary>
public static class BackupRestorer
{
    public static void Restore(ActionContext ctx, BackupItem b)
    {
        var p = ctx.Platform;
        switch (b.Kind)
        {
            case RegistryBackup.Kind:
                RegistryBackup.Restore(p.Registry, b);
                break;
            case "power-active-scheme":
                p.Power.SetActiveScheme(b.Get("guid"));
                if (b.GetOrNull("createdScheme") is { Length: > 0 } created)
                {
                    try { p.Power.DeleteScheme(created); } catch { /* scheme may already be gone */ }
                }
                break;
            case "power-setting":
                p.Power.SetSettingIndex(b.Get("scheme"), b.Get("subgroup"), b.Get("setting"), bool.Parse(b.Get("ac")), int.Parse(b.Get("value")));
                if (b.Get("scheme").Equals(p.Power.GetStatus().ActiveSchemeGuid, StringComparison.OrdinalIgnoreCase))
                    p.Power.SetActiveScheme(b.Get("scheme"));
                break;
            case "hibernation":
                p.Power.SetHibernation(bool.Parse(b.Get("enabled")));
                break;
            case "service-start":
                p.Services.SetStartMode(b.Get("name"), b.Get("startMode"), bool.Parse(b.Get("delayed")));
                break;
            case "service-running":
                var running = bool.Parse(b.Get("running"));
                var svc = p.Services.Get(b.Get("name"));
                if (svc is not null && running && svc.State != "Running") p.Services.Start(b.Get("name"), TimeSpan.FromSeconds(30));
                break;
            case "scheduled-task":
                p.Tasks.SetEnabled(b.Get("path"), bool.Parse(b.Get("enabled")));
                break;
            case "display-mode":
                var mode = new Models.DisplayMode(int.Parse(b.Get("width")), int.Parse(b.Get("height")), int.Parse(b.Get("refresh")), int.Parse(b.Get("bpp")));
                var r = p.Display.SetMode(b.Get("device"), mode, testOnly: false);
                if (!r.Success) throw new InvalidOperationException($"Could not restore display mode: {r.Message}");
                break;
            case "ui-effect":
                if (!p.Maintenance.SetUiEffect(b.Get("name"), bool.Parse(b.Get("enabled")), out var err))
                    throw new InvalidOperationException(err);
                break;
            case "mouse-acceleration":
                if (!p.Maintenance.SetMouseAcceleration(bool.Parse(b.Get("enabled")), out var merr))
                    throw new InvalidOperationException(merr);
                break;
            case "folder-rename":
                FolderRename.Restore(ctx, b);
                break;
            case "note":
                break;
            default:
                throw new NotSupportedException($"No restore handler for backup kind '{b.Kind}'.");
        }
    }
}

internal static class FolderRename
{
    public static void Restore(ActionContext ctx, BackupItem b)
    {
        var original = b.Get("original");
        var renamed = b.Get("renamed");
        if (!Directory.Exists(renamed)) throw new InvalidOperationException($"Backup folder {renamed} no longer exists.");
        foreach (var svc in b.Get("services").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            try { ctx.Platform.Services.Stop(svc, TimeSpan.FromSeconds(30)); } catch { }
        }
        if (Directory.Exists(original))
        {
            var aside = original + ".softreset-recreated-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            Directory.Move(original, aside);
        }
        Directory.Move(renamed, original);
        foreach (var svc in b.Get("services").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            try { ctx.Platform.Services.Start(svc, TimeSpan.FromSeconds(30)); } catch { }
        }
    }
}

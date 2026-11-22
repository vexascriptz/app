using System.Collections.ObjectModel;
using SoftReset.App.ViewModels;
using SoftReset.Core.Diagnostics;
using SoftReset.Core.Optimization;
using SoftReset.Core.Safety;

namespace SoftReset.App.Services;

/// <summary>
/// The one-click flow behind every "Auto optimize" button: check the PC, show a short ticked list,
/// apply everything with backups (one Windows permission prompt), confirm display changes, then
/// show what happened with a single "Undo all".
/// </summary>
public sealed class AutoRunner
{
    private readonly AppServices _s;
    private readonly IDialogService _dialogs;

    public AutoRunner(AppServices services, IDialogService dialogs)
    {
        _s = services;
        _dialogs = dialogs;
    }

    public event EventHandler? Completed;

    /// <summary>Records from the most recent run, for "Undo last auto optimize".</summary>
    public IReadOnlyList<string> LastRunRecordIds { get; private set; } = Array.Empty<string>();

    public async Task<IReadOnlyList<ChangeRecord>> RunAsync(AutoScope scope, string heading)
    {
        AutoPlan plan;
        var optimizer = new AutoOptimizer(_s.Actions, _s.ActionContext);
        using (_dialogs.Busy("Checking your PC…"))
            plan = await Task.Run(() => optimizer.BuildAsync(scope, HardwareProfile.Detect(_s.Platform)));

        if (plan.ChangeCount == 0)
        {
            _dialogs.Toast("Already optimized — nothing to change.", ToastKind.Success);
            return Array.Empty<ChangeRecord>();
        }

        var dialog = new AutoPlanDialogViewModel
        {
            Heading = heading,
            Items = new ObservableCollection<AutoStepItem>(plan.Steps.Select(s => new AutoStepItem(s))),
            AlreadyGoodCount = plan.AlreadyGood.Count,
            NeedsPermission = plan.Steps.Any(s => s.RequiresAdmin) && !_s.Platform.IsElevated,
            NeedsRestart = plan.Steps.Any(s => s.RequiresRestart),
        };
        if (!await _dialogs.ShowAsync(dialog)) return Array.Empty<ChangeRecord>();

        var chosen = dialog.Items.Where(i => i.IsSelected).Select(i => i.Step).ToList();
        if (chosen.All(s => s.IsSafetyStep)) return Array.Empty<ChangeRecord>();
        var finalPlan = new AutoPlan { Scope = plan.Scope, Hardware = plan.Hardware };
        finalPlan.Steps.AddRange(chosen);

        IReadOnlyList<ChangeRecord> records;
        using (_dialogs.Busy("Optimizing…"))
        {
            var progress = new Progress<(int Done, int Total, string Title)>(p =>
            {
                if (p.Total > 0 && p.Done < p.Total) _dialogs.SetBusyText($"Optimizing… {p.Done + 1} of {p.Total}");
            });
            records = await optimizer.ApplyAsync(finalPlan, _s.Executor, progress);
        }
        var list = records.ToList();

        // A new refresh rate must be confirmed on screen, or it reverts by itself.
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].ActionId != ActionIds.RefreshRate || list[i].Status is not (ChangeStatus.Verified or ChangeStatus.Applied)) continue;
            if (!await _dialogs.KeepDisplayChangesAsync(15))
                list[i] = await _s.Executor.RollbackAsync(list[i].Id);
        }

        foreach (var r in list) _s.Log($"Auto optimize: {r.Title} → {r.Status} {r.Message}");
        LastRunRecordIds = list.Where(r => r.CanRollback).Select(r => r.Id).ToList();
        Completed?.Invoke(this, EventArgs.Empty);

        var ok = list.Count(r => r.ActionId != ActionIds.RestorePoint && r.Status is ChangeStatus.Verified or ChangeStatus.Applied);
        var result = new AutoResultDialogViewModel
        {
            Heading = ok > 0 ? $"Done — {ok} change{(ok == 1 ? "" : "s")} made" : "Nothing was changed",
            Lines = new ObservableCollection<AutoResultLine>(list.Select((r, i) =>
            {
                var name = i < chosen.Count ? chosen[i].Title : r.Title;
                return new AutoResultLine(
                    r.Status is ChangeStatus.Verified or ChangeStatus.Applied,
                    r.Status switch
                    {
                        ChangeStatus.Verified or ChangeStatus.Applied => name,
                        ChangeStatus.RolledBack => $"{name} — switched back",
                        ChangeStatus.Cancelled => $"{name} — skipped (no permission)",
                        _ => $"{name} — didn't work",
                    });
            })),
            NeedsRestart = list.Any(r => r.RequiresRestart && r.Status is ChangeStatus.Verified or ChangeStatus.Applied),
            CanUndo = LastRunRecordIds.Count > 0,
        };
        if (await _dialogs.ShowAsync(result)) await UndoLastAsync(confirm: false);
        return list;
    }

    public async Task UndoLastAsync(bool confirm = true)
    {
        if (LastRunRecordIds.Count == 0) return;
        if (confirm && !await _dialogs.ConfirmAsync("Undo the last auto optimize?", "Everything it changed that can be undone will be put back.", "Undo all")) return;
        IReadOnlyList<ChangeRecord> undone;
        using (_dialogs.Busy("Undoing…"))
            undone = await _s.Executor.RollbackManyAsync(LastRunRecordIds);
        var failed = undone.Count(r => r.Status != ChangeStatus.RolledBack);
        _dialogs.Toast(failed == 0 ? "Everything was put back." : $"{failed} change(s) couldn't be undone. See Undo changes.", failed == 0 ? ToastKind.Success : ToastKind.Warning);
        LastRunRecordIds = Array.Empty<string>();
        Completed?.Invoke(this, EventArgs.Empty);
    }
}

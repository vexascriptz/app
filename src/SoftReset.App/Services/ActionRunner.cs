using System.Collections.ObjectModel;
using SoftReset.App.ViewModels;
using SoftReset.Core.Diagnostics;
using SoftReset.Core.Optimization;
using SoftReset.Core.Safety;

namespace SoftReset.App.Services;

/// <summary>
/// The one path through which the UI changes anything: preview → explicit approval → backup →
/// apply (UAC only if needed) → verify → result. Display changes additionally require the
/// technician to confirm the picture within 15 seconds, otherwise they are reverted automatically.
/// </summary>
public sealed class ActionRunner
{
    private readonly AppServices _s;
    private readonly IDialogService _dialogs;

    public ActionRunner(AppServices services, IDialogService dialogs)
    {
        _s = services;
        _dialogs = dialogs;
    }

    public event EventHandler<ChangeRecord>? ChangeCompleted;

    public async Task<ActionPreview?> PreviewAsync(ActionRequest request)
    {
        var action = _s.Actions.Get(request.ActionId);
        return await action.PreviewAsync(_s.ActionContext, request, CancellationToken.None);
    }

    public async Task<ChangeRecord?> RunAsync(ActionRequest request, bool skipApproval = false)
    {
        var action = _s.Actions.Get(request.ActionId);
        ActionPreview preview;
        try
        {
            using (_dialogs.Busy("Preparing preview…"))
                preview = await action.PreviewAsync(_s.ActionContext, request, CancellationToken.None);
        }
        catch (Exception ex)
        {
            await _dialogs.ShowMessageAsync("Cannot prepare this change", ex.Message);
            return null;
        }
        if (preview.NothingToDo && action.Reversibility != Reversibility.None && preview.Warnings.Count == 0)
        {
            _dialogs.Toast("Already set — nothing to change.", ToastKind.Info);
            return null;
        }

        if (!skipApproval)
        {
            var title = action is IDynamicTitleAction dt ? dt.TitleFor(request) : action.Title;
            var dlg = new ApprovalDialogViewModel
            {
                Title = title,
                Description = action.Description,
                ExpectedBenefit = FirstSentence(action.ExpectedBenefit),
                Risks = FirstSentence(action.Risks),
                RequiresAdmin = preview.RequiresAdmin || ActionExecutor.RequiresAdmin(action, request),
                RequiresRestart = preview.RequiresRestart,
                Reversibility = preview.Reversibility,
                EstimatedBytesFreed = preview.EstimatedBytesFreed,
                Changes = new ObservableCollection<PreviewChange>(preview.Changes),
                Warnings = new ObservableCollection<string>(preview.Warnings),
            };
            if (!await _dialogs.ApproveAsync(dlg)) return null;
        }

        ChangeRecord record;
        using (_dialogs.Busy(_s.Executor.NeedsElevation(action, request) ? "Waiting for administrator approval (UAC)…" : $"Applying: {action.Title}…"))
            record = await _s.Executor.ApplyAsync(request);
        _s.Log($"Action {record.ActionId} → {record.Status}: {record.Message} {record.VerificationMessage}");

        if (action.Id == ActionIds.RefreshRate && record.Status is ChangeStatus.Verified or ChangeStatus.Applied)
        {
            var keep = await _dialogs.KeepDisplayChangesAsync(15);
            if (!keep)
            {
                record = await _s.Executor.RollbackAsync(record.Id);
                _dialogs.Toast("Display change reverted.", ToastKind.Warning);
                ChangeCompleted?.Invoke(this, record);
                return record;
            }
        }

        Report(record);
        ChangeCompleted?.Invoke(this, record);
        return record;
    }

    /// <summary>Keeps dialogs short: the first sentence carries the point; the full text is on the page.</summary>
    internal static string FirstSentence(string text)
    {
        var t = text.Trim();
        var i = t.IndexOf(". ", StringComparison.Ordinal);
        return i > 0 ? t[..(i + 1)] : t;
    }

    private void Report(ChangeRecord r)
    {
        switch (r.Status)
        {
            case ChangeStatus.Verified:
                _dialogs.Toast($"{r.Title}: done.{(r.RequiresRestart ? " Restart the PC to finish." : "")}", ToastKind.Success);
                break;
            case ChangeStatus.Applied:
            case ChangeStatus.VerificationFailed:
                _dialogs.Toast($"{r.Title}: applied, but couldn't confirm it worked.", ToastKind.Warning);
                break;
            case ChangeStatus.Cancelled:
                _dialogs.Toast(r.Message ?? "Cancelled.", ToastKind.Info);
                break;
            default:
                _ = _dialogs.ShowMessageAsync($"{r.Title} did not complete", (r.Message ?? "Unknown error.") + (r.CanRollback ? "\n\nSome parts may have changed. You can undo them in Undo changes." : ""));
                break;
        }
    }

    public async Task<ChangeRecord?> RollbackAsync(ChangeRecord record)
    {
        if (!record.CanRollback)
        {
            await _dialogs.ShowMessageAsync("Can't undo this", record.Reversibility == Reversibility.None ? "This kind of change can't be undone." : "It has already been undone or didn't change anything.");
            return null;
        }
        var ok = await _dialogs.ConfirmAsync("Undo this change?", $"{record.Title} will be put back the way it was.", "Undo");
        if (!ok) return null;
        ChangeRecord result;
        using (_dialogs.Busy("Undoing…"))
            result = await _s.Executor.RollbackAsync(record.Id);
        _s.Log($"Rollback {result.ActionId} → {result.Status}: {result.RollbackMessage}");
        if (result.Status == ChangeStatus.RolledBack) _dialogs.Toast("Undone.", ToastKind.Success);
        else await _dialogs.ShowMessageAsync("Couldn't fully undo", result.RollbackMessage ?? "Unknown error.");
        ChangeCompleted?.Invoke(this, result);
        return result;
    }
}

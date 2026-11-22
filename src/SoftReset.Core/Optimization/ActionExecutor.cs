using SoftReset.Core.Platform;
using SoftReset.Core.Safety;
using SoftReset.Core.Storage;

namespace SoftReset.Core.Optimization;

/// <summary>
/// Applies and rolls back optimization actions. Every change follows the same path:
/// journal record (Pending) → backups captured and persisted → change applied →
/// record Applied → verification → record Verified/VerificationFailed.
/// Actions needing administrator rights run in a short-lived elevated copy of the
/// console (UAC prompt) unless the console is already elevated.
/// </summary>
public sealed class ActionExecutor
{
    private readonly ActionRegistry _registry;
    private readonly ActionContext _ctx;

    public ActionExecutor(ActionRegistry registry, ActionContext ctx)
    {
        _registry = registry;
        _ctx = ctx;
    }

    public ActionContext Context => _ctx;
    public ActionRegistry Registry => _registry;

    public async Task<ChangeRecord> ApplyAsync(ActionRequest request, CancellationToken ct = default)
    {
        var action = _registry.Get(request.ActionId);
        var record = NewRecord(action, request);
        _ctx.Journal.Save(record);

        if (NeedsElevation(action, request))
            return await RunElevatedAsync("apply", action, request, record, ct);

        return await ApplyInProcessAsync(action, request, record, ct);
    }

    public bool NeedsElevation(IOptimizationAction action, ActionRequest request) =>
        RequiresAdmin(action, request) && !_ctx.Platform.IsElevated;

    public static bool RequiresAdmin(IOptimizationAction action, ActionRequest request) =>
        action is IDynamicAdminAction dyn ? dyn.RequiresAdminFor(request) : action.RequiresAdmin;

    public ChangeRecord NewRecord(IOptimizationAction action, ActionRequest request) => new()
    {
        ActionId = action.Id,
        Title = action is IDynamicTitleAction t ? t.TitleFor(request) : action.Title,
        Category = action.Category.ToString(),
        Reversibility = action.Reversibility,
        RequiresRestart = action.RequiresRestart,
        Parameters = new Dictionary<string, string>(request.Parameters),
        SelectedItems = new List<string>(request.SelectedItems),
        SessionId = request.SessionId,
        Elevated = RequiresAdmin(action, request),
        Status = ChangeStatus.Pending,
    };

    internal async Task<ChangeRecord> ApplyInProcessAsync(IOptimizationAction action, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        record.Status = ChangeStatus.Pending;
        _ctx.Journal.Save(record);
        try
        {
            await action.ApplyAsync(_ctx, request, record, ct);
            record.Status = ChangeStatus.Applied;
            record.Message ??= "Applied.";
        }
        catch (OperationCanceledException)
        {
            record.Status = record.Backups.Count > 0 ? ChangeStatus.Failed : ChangeStatus.Cancelled;
            record.Message = record.Backups.Count > 0
                ? "Cancelled part-way. Some items may already be changed — use Roll back to restore the captured backups."
                : "Cancelled before any change was made.";
        }
        catch (UnauthorizedAccessException ex)
        {
            record.Status = ChangeStatus.Failed;
            record.Message = $"Access denied: {ex.Message}. This change may require administrator rights or be blocked by policy.";
        }
        catch (Exception ex)
        {
            record.Status = ChangeStatus.Failed;
            record.Message = $"Failed: {ex.Message}";
        }
        record.CompletedAt = DateTimeOffset.Now;
        _ctx.Journal.Save(record);

        if (record.Status == ChangeStatus.Applied)
        {
            try
            {
                var v = await action.VerifyAsync(_ctx, request, record, CancellationToken.None);
                record.Status = v.Verified ? ChangeStatus.Verified : ChangeStatus.VerificationFailed;
                record.VerificationMessage = v.Message;
            }
            catch (Exception ex)
            {
                record.Status = ChangeStatus.VerificationFailed;
                record.VerificationMessage = $"Could not verify: {ex.Message}";
            }
            _ctx.Journal.Save(record);
        }
        return record;
    }

    /// <summary>
    /// Applies several requests. Changes that need administrator rights are run together in one
    /// elevated helper (one UAC prompt) before the others; each still gets its own record, backups,
    /// verification, and rollback. A failure in one request never stops the rest.
    /// </summary>
    public async Task<IReadOnlyList<ChangeRecord>> ApplyManyAsync(IReadOnlyList<ActionRequest> requests, IProgress<(int Done, int Total, string Title)>? progress = null, CancellationToken ct = default)
    {
        var items = requests.Select(r => (Action: _registry.Get(r.ActionId), Request: r)).ToList();
        var records = items.Select(i => { var rec = NewRecord(i.Action, i.Request); _ctx.Journal.Save(rec); return rec; }).ToList();
        var results = new ChangeRecord[records.Count];
        var done = 0;
        var elevated = Enumerable.Range(0, items.Count).Where(i => NeedsElevation(items[i].Action, items[i].Request)).ToList();
        if (elevated.Count > 0)
        {
            progress?.Report((done, items.Count, "Waiting for Windows permission…"));
            var batch = await RunElevatedBatchAsync("apply-batch", elevated.Select(i => records[i]).ToList(), ct);
            foreach (var (i, rec) in elevated.Zip(batch)) results[i] = rec;
            done += elevated.Count;
        }
        for (var i = 0; i < items.Count; i++)
        {
            if (results[i] is not null) continue;
            progress?.Report((done, items.Count, records[i].Title));
            results[i] = await ApplyInProcessAsync(items[i].Action, items[i].Request, records[i], ct);
            done++;
        }
        progress?.Report((items.Count, items.Count, ""));
        return results;
    }

    /// <summary>Rolls back several records (newest first), with one UAC prompt for those that need it.</summary>
    public async Task<IReadOnlyList<ChangeRecord>> RollbackManyAsync(IReadOnlyList<string> recordIds, CancellationToken ct = default)
    {
        var records = recordIds.Select(id => _ctx.Journal.Load(id)).Where(r => r is { CanRollback: true }).Select(r => r!)
            .OrderByDescending(r => r.CreatedAt).ToList();
        var results = new List<ChangeRecord>();
        var elevated = records.Where(r => r.Elevated && !_ctx.Platform.IsElevated).ToList();
        if (elevated.Count > 0) results.AddRange(await RunElevatedBatchAsync("rollback-batch", elevated, ct));
        foreach (var r in records.Except(elevated))
            results.Add(await RollbackInProcessAsync(_registry.Get(r.ActionId), r, ct));
        return results;
    }

    private async Task<IReadOnlyList<ChangeRecord>> RunElevatedBatchAsync(string operation, IReadOnlyList<ChangeRecord> records, CancellationToken ct)
    {
        var resultFile = Path.Combine(_ctx.Paths.Temp, $"elevated-batch-{Guid.NewGuid():N}.json");
        var request = new ElevatedRequest
        {
            Operation = operation,
            BatchRecordIds = records.Select(r => r.Id).ToList(),
            DataDirectory = _ctx.Paths.Root,
            ResultFile = resultFile,
        };
        var result = await _ctx.Platform.Elevation.RunElevatedAsync(request, ct);
        var output = new List<ChangeRecord>();
        foreach (var original in records)
        {
            var r = _ctx.Journal.Load(original.Id) ?? original;
            var apply = operation == "apply-batch";
            if (result.UserCancelled)
            {
                if (apply) { r.Status = ChangeStatus.Cancelled; r.Message = "Windows permission was declined. Nothing was changed."; }
                else r.RollbackMessage = "Windows permission was declined. The undo did not run.";
                _ctx.Journal.Save(r);
            }
            else if (!result.Launched)
            {
                if (apply) { r.Status = ChangeStatus.Failed; r.Message = $"Could not start the elevated helper: {result.Message}"; }
                else r.RollbackMessage = $"Could not start the elevated helper: {result.Message}";
                _ctx.Journal.Save(r);
            }
            else if (apply && r.Status == ChangeStatus.Pending)
            {
                r.Status = ChangeStatus.Failed;
                r.Message = $"The elevated helper exited (code {result.ExitCode}) without completing this change.";
                _ctx.Journal.Save(r);
            }
            output.Add(r);
        }
        try { if (File.Exists(resultFile)) File.Delete(resultFile); } catch { }
        return output;
    }

    public async Task<ChangeRecord> RollbackAsync(string recordId, CancellationToken ct = default)
    {
        var record = _ctx.Journal.Load(recordId) ?? throw new InvalidOperationException("Change record not found.");
        if (!record.CanRollback)
            throw new InvalidOperationException(record.Reversibility == Reversibility.None
                ? "This change cannot be rolled back automatically."
                : $"This change cannot be rolled back in its current state ({record.Status}).");
        var action = _registry.Get(record.ActionId);
        var request = new ActionRequest { ActionId = record.ActionId, Parameters = record.Parameters, SelectedItems = record.SelectedItems, SessionId = record.SessionId };

        if (record.Elevated && !_ctx.Platform.IsElevated)
            return await RunElevatedAsync("rollback", action, request, record, ct);
        return await RollbackInProcessAsync(action, record, ct);
    }

    internal async Task<ChangeRecord> RollbackInProcessAsync(IOptimizationAction action, ChangeRecord record, CancellationToken ct)
    {
        try
        {
            await action.RollbackAsync(_ctx, record, ct);
            record.Status = ChangeStatus.RolledBack;
            record.RolledBackAt = DateTimeOffset.Now;
            record.RollbackMessage = "Previous configuration restored from backup.";
        }
        catch (Exception ex)
        {
            record.Status = ChangeStatus.RollbackFailed;
            record.RollbackMessage = $"Rollback failed: {ex.Message}";
        }
        _ctx.Journal.Save(record);
        return record;
    }

    private async Task<ChangeRecord> RunElevatedAsync(string operation, IOptimizationAction action, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        var resultFile = Path.Combine(_ctx.Paths.Temp, $"elevated-{record.Id}-{operation}.json");
        var elevated = new ElevatedRequest
        {
            Operation = operation,
            ActionId = action.Id,
            Parameters = request.Parameters,
            SelectedItems = request.SelectedItems,
            ChangeRecordId = record.Id,
            DataDirectory = _ctx.Paths.Root,
            ResultFile = resultFile,
            SessionId = request.SessionId,
        };
        var result = await _ctx.Platform.Elevation.RunElevatedAsync(elevated, ct);
        var reloaded = _ctx.Journal.Load(record.Id) ?? record;
        if (result.UserCancelled)
        {
            if (operation == "apply")
            {
                reloaded.Status = ChangeStatus.Cancelled;
                reloaded.Message = "Administrator approval was declined in the UAC prompt. Nothing was changed.";
            }
            else
            {
                reloaded.RollbackMessage = "Administrator approval was declined. The rollback did not run.";
            }
            _ctx.Journal.Save(reloaded);
        }
        else if (!result.Launched)
        {
            if (operation == "apply")
            {
                reloaded.Status = ChangeStatus.Failed;
                reloaded.Message = $"Could not start the elevated helper: {result.Message}";
            }
            else reloaded.RollbackMessage = $"Could not start the elevated helper: {result.Message}";
            _ctx.Journal.Save(reloaded);
        }
        else if (reloaded.Status == ChangeStatus.Pending && operation == "apply")
        {
            reloaded.Status = ChangeStatus.Failed;
            reloaded.Message = $"The elevated helper exited (code {result.ExitCode}) without completing. {result.Message}";
            _ctx.Journal.Save(reloaded);
        }
        try { if (File.Exists(resultFile)) File.Delete(resultFile); } catch { }
        return reloaded;
    }
}

/// <summary>Implemented by actions whose need for admin rights depends on the selected items.</summary>
public interface IDynamicAdminAction
{
    bool RequiresAdminFor(ActionRequest request);
}

/// <summary>Implemented by generic actions whose journal title depends on parameters.</summary>
public interface IDynamicTitleAction
{
    string TitleFor(ActionRequest request);
}

/// <summary>Entry point for the elevated helper process (SoftResetConsole.exe --elevated-run).</summary>
public static class ElevatedHelper
{
    public static async Task<int> RunAsync(ElevatedRequest request, ISystemPlatform platform, CancellationToken ct = default)
    {
        var paths = new AppPaths(request.DataDirectory, isPortable: false);
        paths.EnsureCreated();
        var journal = new ChangeJournal(paths.Journal);
        var ctx = new ActionContext { Platform = platform, Journal = journal, Paths = paths };
        var registry = ActionRegistry.CreateDefault();
        var executor = new ActionExecutor(registry, ctx);
        var status = "unknown";
        string message;
        if (request.Operation is "apply-batch" or "rollback-batch")
        {
            var count = 0;
            foreach (var id in request.BatchRecordIds)
            {
                try
                {
                    var rec = journal.Load(id) ?? throw new InvalidOperationException($"Change record {id} not found.");
                    var act = registry.Get(rec.ActionId);
                    var req = new ActionRequest { ActionId = rec.ActionId, Parameters = rec.Parameters, SelectedItems = rec.SelectedItems, SessionId = rec.SessionId };
                    if (request.Operation == "apply-batch") await executor.ApplyInProcessAsync(act, req, rec, ct);
                    else await executor.RollbackInProcessAsync(act, rec, ct);
                    count++;
                }
                catch (Exception ex)
                {
                    var rec = journal.Load(id);
                    if (rec is not null)
                    {
                        if (request.Operation == "apply-batch") { rec.Status = ChangeStatus.Failed; rec.Message = ex.Message; }
                        else { rec.Status = ChangeStatus.RollbackFailed; rec.RollbackMessage = ex.Message; }
                        journal.Save(rec);
                    }
                }
            }
            if (!string.IsNullOrEmpty(request.ResultFile))
            {
                try { Json.WriteFile(request.ResultFile, new { status = "Batch", message = $"{count} of {request.BatchRecordIds.Count} processed" }); } catch { }
            }
            return 0;
        }
        try
        {
            if (!registry.TryGet(request.ActionId, out var action))
                throw new InvalidOperationException($"Unknown action '{request.ActionId}'.");
            var record = request.ChangeRecordId is { } id ? journal.Load(id) : null;
            if (record is null) throw new InvalidOperationException("Change record not found for elevated operation.");
            var actionRequest = new ActionRequest { ActionId = request.ActionId, Parameters = request.Parameters, SelectedItems = request.SelectedItems, SessionId = request.SessionId };

            record = request.Operation switch
            {
                "apply" => await executor.ApplyInProcessAsync(action, actionRequest, record, ct),
                "rollback" => await executor.RollbackInProcessAsync(action, record, ct),
                _ => throw new InvalidOperationException($"Unknown operation '{request.Operation}'."),
            };
            status = record.Status.ToString();
            message = record.Message ?? record.RollbackMessage ?? "";
        }
        catch (Exception ex)
        {
            status = "Error";
            message = ex.Message;
        }
        if (!string.IsNullOrEmpty(request.ResultFile))
        {
            try { Json.WriteFile(request.ResultFile, new { status, message }); } catch { }
        }
        return status is "Error" ? 2 : 0;
    }
}

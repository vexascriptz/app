using SoftReset.Core.Storage;

namespace SoftReset.Core.Safety;

public enum ChangeStatus
{
    /// <summary>Backups captured, change about to be applied (write-ahead).</summary>
    Pending,
    Applied,
    Verified,
    VerificationFailed,
    Failed,
    RolledBack,
    RollbackFailed,
    /// <summary>A Pending record found at start-up: the app stopped mid-change.</summary>
    Interrupted,
    Cancelled,
}

public enum Reversibility
{
    /// <summary>Everything changed can be restored from the captured backup.</summary>
    Full,
    /// <summary>Some parts can be restored; the rest is documented.</summary>
    Partial,
    /// <summary>Cannot be undone (e.g. deleting cache files).</summary>
    None,
}

/// <summary>A captured piece of prior state, sufficient to restore it.</summary>
public sealed class BackupItem
{
    public string Kind { get; set; } = "";
    public string Description { get; set; } = "";
    public Dictionary<string, string> Data { get; set; } = new();

    public string Get(string key) => Data.TryGetValue(key, out var v) ? v : "";
    public string? GetOrNull(string key) => Data.TryGetValue(key, out var v) ? v : null;
}

public sealed class ChangeRecord
{
    public string Id { get; set; } = NewId();
    public string ActionId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? CompletedAt { get; set; }
    public ChangeStatus Status { get; set; } = ChangeStatus.Pending;
    public Reversibility Reversibility { get; set; }
    public bool Elevated { get; set; }
    public bool RequiresRestart { get; set; }
    public string? SessionId { get; set; }
    public Dictionary<string, string> Parameters { get; set; } = new();
    public List<string> SelectedItems { get; set; } = new();
    public List<BackupItem> Backups { get; set; } = new();
    public List<string> ChangesApplied { get; set; } = new();
    public string? Message { get; set; }
    public string? VerificationMessage { get; set; }
    public string? RollbackMessage { get; set; }
    public DateTimeOffset? RolledBackAt { get; set; }
    public string MachineName { get; set; } = Environment.MachineName;

    public bool CanRollback =>
        Reversibility != Reversibility.None &&
        Backups.Count > 0 &&
        Status is ChangeStatus.Applied or ChangeStatus.Verified or ChangeStatus.VerificationFailed
            or ChangeStatus.Interrupted or ChangeStatus.Failed or ChangeStatus.RollbackFailed;

    public static string NewId() => $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}";
}

/// <summary>
/// Durable, per-record change history. Each change is its own JSON file so that an
/// elevated helper process and the main UI never write the same file, and a crash
/// can corrupt at most one record. Records are written before a change is applied
/// (status Pending) so interrupted operations are detectable at the next launch.
/// </summary>
public sealed class ChangeJournal
{
    private readonly string _dir;
    private readonly object _gate = new();

    public ChangeJournal(string directory)
    {
        _dir = directory;
        Directory.CreateDirectory(_dir);
    }

    public string Directory_ => _dir;

    public event EventHandler<ChangeRecord>? Changed;

    public string PathFor(string id) => Path.Combine(_dir, $"{id}.json");

    public void Save(ChangeRecord record)
    {
        lock (_gate)
        {
            Json.WriteFile(PathFor(record.Id), record);
        }
        Changed?.Invoke(this, record);
    }

    public ChangeRecord? Load(string id)
    {
        try { return Json.ReadFile<ChangeRecord>(PathFor(id)); }
        catch { return null; }
    }

    public IReadOnlyList<ChangeRecord> LoadAll()
    {
        var list = new List<ChangeRecord>();
        if (!Directory.Exists(_dir)) return list;
        foreach (var file in Directory.EnumerateFiles(_dir, "*.json"))
        {
            try
            {
                var r = Json.ReadFile<ChangeRecord>(file);
                if (r is not null) list.Add(r);
            }
            catch
            {
                // A damaged record is skipped rather than blocking the whole history.
            }
        }
        return list.OrderByDescending(r => r.CreatedAt).ToList();
    }

    /// <summary>
    /// Marks records left in Pending state (the process ended during a change) as
    /// Interrupted and returns them so the UI can offer a rollback.
    /// </summary>
    public IReadOnlyList<ChangeRecord> RecoverInterrupted()
    {
        var interrupted = new List<ChangeRecord>();
        foreach (var r in LoadAll().Where(r => r.Status == ChangeStatus.Pending))
        {
            // Give an in-flight elevated helper a grace period before calling it interrupted.
            if (DateTimeOffset.Now - r.CreatedAt < TimeSpan.FromMinutes(10)) continue;
            r.Status = ChangeStatus.Interrupted;
            r.Message = "The console stopped before this change finished. Review the backups and roll back if the system is not in the expected state.";
            Save(r);
            interrupted.Add(r);
        }
        return interrupted;
    }

    public IReadOnlyList<ChangeRecord> ForSession(string sessionId) =>
        LoadAll().Where(r => r.SessionId == sessionId).OrderBy(r => r.CreatedAt).ToList();

    public void DeleteAll()
    {
        lock (_gate)
        {
            if (!Directory.Exists(_dir)) return;
            foreach (var f in Directory.EnumerateFiles(_dir, "*.json")) File.Delete(f);
        }
    }
}

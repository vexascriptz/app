using SoftReset.Core.Storage;

namespace SoftReset.Core.Files;

public enum OrganizeStrategy { ByCategory, ByYearMonth, ByCategoryThenYear }

public sealed class PlannedMove
{
    public string Source { get; set; } = "";
    public string Destination { get; set; } = "";
    public long Size { get; set; }
    public string Reason { get; set; } = "";
    public bool RenamedToAvoidConflict { get; set; }
}

public sealed class OrganizePlanOptions
{
    public OrganizeStrategy Strategy { get; set; } = OrganizeStrategy.ByCategory;
    /// <summary>Only organise files sitting directly in the root (recommended for Downloads/Desktop).</summary>
    public bool TopLevelOnly { get; set; } = true;
    public TimeSpan SkipModifiedWithin { get; set; } = TimeSpan.FromMinutes(15);
    public bool SkipShortcuts { get; set; } = true;
    public HashSet<FileCategory> ExcludedCategories { get; set; } = new();
}

public sealed class OrganizationPlan
{
    public string Root { get; set; } = "";
    public OrganizeStrategy Strategy { get; set; }
    public List<PlannedMove> Moves { get; set; } = new();
    public List<string> FoldersToCreate { get; set; } = new();
    public List<string> Skipped { get; set; } = new();
    public long TotalBytes => Moves.Sum(m => m.Size);
}

/// <summary>Builds a preview of file moves. Planning never touches the file system.</summary>
public sealed class OrganizationPlanner
{
    public OrganizationPlan Plan(FolderAnalysis analysis, OrganizePlanOptions options, DateTime? utcNow = null)
    {
        var now = utcNow ?? DateTime.UtcNow;
        var plan = new OrganizationPlan { Root = analysis.Root, Strategy = options.Strategy };
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var f in analysis.Files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase))
        {
            if (options.TopLevelOnly && !f.IsTopLevel) continue;
            if (f.IsCloudPlaceholder) { plan.Skipped.Add($"{f.RelativePath}: cloud-only file (moving would download it)"); continue; }
            if (now - f.ModifiedUtc < options.SkipModifiedWithin) { plan.Skipped.Add($"{f.RelativePath}: modified recently (may be in use)"); continue; }
            if (options.SkipShortcuts && f.Category == FileCategory.Shortcuts) { plan.Skipped.Add($"{f.RelativePath}: shortcut (kept in place)"); continue; }
            if (options.ExcludedCategories.Contains(f.Category)) continue;

            var subfolder = options.Strategy switch
            {
                OrganizeStrategy.ByCategory => FileCategories.FolderName(f.Category),
                OrganizeStrategy.ByYearMonth => Path.Combine(f.ModifiedUtc.ToLocalTime().ToString("yyyy"), f.ModifiedUtc.ToLocalTime().ToString("MM")),
                _ => Path.Combine(FileCategories.FolderName(f.Category), f.ModifiedUtc.ToLocalTime().ToString("yyyy")),
            };
            var currentDir = Path.GetDirectoryName(f.Path)!;
            var targetDir = Path.Combine(analysis.Root, subfolder);
            if (string.Equals(Path.GetFullPath(currentDir), Path.GetFullPath(targetDir), StringComparison.OrdinalIgnoreCase)) continue;

            var dest = Path.Combine(targetDir, Path.GetFileName(f.Path));
            var renamed = false;
            if (File.Exists(dest) || taken.Contains(dest))
            {
                dest = UniqueName(targetDir, Path.GetFileName(f.Path), taken);
                renamed = true;
            }
            taken.Add(dest);
            if (!Directory.Exists(targetDir) && folders.Add(targetDir)) plan.FoldersToCreate.Add(targetDir);
            plan.Moves.Add(new PlannedMove
            {
                Source = f.Path,
                Destination = dest,
                Size = f.Size,
                Reason = options.Strategy == OrganizeStrategy.ByYearMonth ? $"Modified {f.ModifiedUtc.ToLocalTime():MMMM yyyy}" : $"{FileCategories.FolderName(f.Category)} file",
                RenamedToAvoidConflict = renamed,
            });
        }
        return plan;
    }

    public static OrganizationPlan PlanDuplicateReview(string root, IEnumerable<DuplicateGroup> groups, IEnumerable<string>? filesToMove = null)
    {
        var plan = new OrganizationPlan { Root = root };
        var reviewRoot = Path.Combine(root, FileOperationExecutor.ReviewFolderName, "duplicates");
        var selected = filesToMove?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in groups)
        {
            var keep = g.SuggestedKeep;
            foreach (var f in g.Files)
            {
                if (ReferenceEquals(f, keep) || f.Path == keep.Path) continue;
                if (selected is not null && !selected.Contains(f.Path)) continue;
                var rel = Path.GetRelativePath(root, f.Path);
                if (rel.StartsWith("..", StringComparison.Ordinal)) rel = Path.GetFileName(f.Path);
                var dest = Path.Combine(reviewRoot, rel);
                if (File.Exists(dest) || taken.Contains(dest)) dest = UniqueName(Path.GetDirectoryName(dest)!, Path.GetFileName(dest), taken);
                taken.Add(dest);
                plan.Moves.Add(new PlannedMove { Source = f.Path, Destination = dest, Size = f.Size, Reason = $"Duplicate of {keep.RelativePath}" });
            }
        }
        return plan;
    }

    private static string UniqueName(string dir, string fileName, HashSet<string> taken)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (var i = 1; i < 10_000; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate) && !taken.Contains(candidate)) return candidate;
        }
        throw new IOException($"Could not find a free name for {fileName}");
    }
}

public enum FileOpState { Pending, Done, Failed, Undone, UndoFailed, Skipped }

public sealed class FileOperationEntry
{
    public string Source { get; set; } = "";
    public string Destination { get; set; } = "";
    public long Size { get; set; }
    public FileOpState State { get; set; } = FileOpState.Pending;
    public string? Error { get; set; }
    public DateTimeOffset? DoneAt { get; set; }
}

public sealed class FileOperationLog
{
    public string Id { get; set; } = $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}";
    public string Kind { get; set; } = "organize";
    public string Root { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? UndoneAt { get; set; }
    public string Status { get; set; } = "Pending";
    public string? SessionId { get; set; }
    public List<string> CreatedDirectories { get; set; } = new();
    public List<FileOperationEntry> Entries { get; set; } = new();

    public int Done => Entries.Count(e => e.State == FileOpState.Done);
    public int Failed => Entries.Count(e => e.State == FileOpState.Failed);
    public bool CanUndo => Entries.Any(e => e.State is FileOpState.Done or FileOpState.Pending or FileOpState.UndoFailed) && Status != "Undone";
}

/// <summary>
/// Executes approved move plans. Every operation is logged before and after it runs,
/// files are never overwritten or deleted, and any batch can be undone in reverse order.
/// </summary>
public sealed class FileOperationExecutor
{
    public const string ReviewFolderName = "_softreset_review";
    private readonly string _logDir;

    public FileOperationExecutor(string logDirectory)
    {
        _logDir = logDirectory;
        Directory.CreateDirectory(_logDir);
    }

    private string LogPath(string id) => Path.Combine(_logDir, $"{id}.json");

    public void Save(FileOperationLog log) => Json.WriteFile(LogPath(log.Id), log);

    public FileOperationLog? Load(string id) => Json.ReadFile<FileOperationLog>(LogPath(id));

    public IReadOnlyList<FileOperationLog> LoadAll() =>
        Directory.EnumerateFiles(_logDir, "*.json")
            .Select(f => { try { return Json.ReadFile<FileOperationLog>(f); } catch { return null; } })
            .Where(l => l is not null).Cast<FileOperationLog>()
            .OrderByDescending(l => l.StartedAt).ToList();

    public Task<FileOperationLog> ExecuteAsync(OrganizationPlan plan, string kind, string description, string? sessionId = null, IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default) =>
        Task.Run(() => Execute(plan, kind, description, sessionId, progress, ct), CancellationToken.None);

    private FileOperationLog Execute(OrganizationPlan plan, string kind, string description, string? sessionId, IProgress<(int, int)>? progress, CancellationToken ct)
    {
        var log = new FileOperationLog
        {
            Kind = kind,
            Root = plan.Root,
            Description = description,
            SessionId = sessionId,
            Entries = plan.Moves.Select(m => new FileOperationEntry { Source = m.Source, Destination = m.Destination, Size = m.Size }).ToList(),
            Status = "Running",
        };
        Save(log); // write-ahead: the full intended plan is on disk before anything moves

        var i = 0;
        foreach (var e in log.Entries)
        {
            if (ct.IsCancellationRequested) { log.Status = "Cancelled"; break; }
            try
            {
                if (!File.Exists(e.Source)) throw new FileNotFoundException("Source file no longer exists.");
                if (File.Exists(e.Destination)) throw new IOException("A file already exists at the destination; it was not overwritten.");
                var dir = Path.GetDirectoryName(e.Destination)!;
                if (!Directory.Exists(dir))
                {
                    // Record every directory level we create so undo can remove them if empty.
                    var toCreate = new Stack<string>();
                    for (var d = dir; !string.IsNullOrEmpty(d) && !Directory.Exists(d); d = Path.GetDirectoryName(d)!) toCreate.Push(d);
                    while (toCreate.Count > 0)
                    {
                        var d = toCreate.Pop();
                        Directory.CreateDirectory(d);
                        log.CreatedDirectories.Add(d);
                    }
                }
                File.Move(e.Source, e.Destination, overwrite: false);
                e.State = FileOpState.Done;
                e.DoneAt = DateTimeOffset.Now;
            }
            catch (Exception ex)
            {
                e.State = FileOpState.Failed;
                e.Error = ex.Message;
            }
            i++;
            if (i % 25 == 0) Save(log);
            progress?.Report((i, log.Entries.Count));
        }
        if (log.Status == "Running") log.Status = log.Failed == 0 ? "Completed" : "Completed with errors";
        log.CompletedAt = DateTimeOffset.Now;
        Save(log);
        return log;
    }

    public Task<FileOperationLog> UndoAsync(string logId, IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default) =>
        Task.Run(() => Undo(logId, progress, ct), CancellationToken.None);

    private FileOperationLog Undo(string logId, IProgress<(int, int)>? progress, CancellationToken ct)
    {
        var log = Load(logId) ?? throw new InvalidOperationException("Operation log not found.");
        var candidates = Enumerable.Reverse(log.Entries).Where(e => e.State is FileOpState.Done or FileOpState.Pending or FileOpState.UndoFailed).ToList();
        var i = 0;
        foreach (var e in candidates)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // A Pending entry may have moved just before a crash: undo it only if the evidence says so.
                if (File.Exists(e.Destination) && !File.Exists(e.Source))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(e.Source)!);
                    File.Move(e.Destination, e.Source, overwrite: false);
                    e.State = FileOpState.Undone;
                    e.Error = null;
                }
                else if (e.State == FileOpState.Pending)
                {
                    e.State = FileOpState.Skipped;
                }
                else
                {
                    e.State = FileOpState.UndoFailed;
                    e.Error = File.Exists(e.Source) ? "A file now exists at the original location; nothing was overwritten." : "The moved file is no longer at its destination.";
                }
            }
            catch (Exception ex)
            {
                e.State = FileOpState.UndoFailed;
                e.Error = ex.Message;
            }
            i++;
            progress?.Report((i, candidates.Count));
        }
        foreach (var d in log.CreatedDirectories.OrderByDescending(d => d.Length))
        {
            try { if (Directory.Exists(d) && !Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d); } catch { }
        }
        log.Status = log.Entries.Any(e => e.State == FileOpState.UndoFailed) ? "Partially undone" : "Undone";
        log.UndoneAt = DateTimeOffset.Now;
        Save(log);
        return log;
    }
}

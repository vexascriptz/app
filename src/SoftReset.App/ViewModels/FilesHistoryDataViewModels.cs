using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoftReset.Core.Files;
using SoftReset.Core.Parsing;
using SoftReset.Core.Platform;
using SoftReset.Core.Safety;
using SoftReset.Core.Storage;

namespace SoftReset.App.ViewModels;

public sealed partial class DuplicateGroupItem : ObservableObject
{
    public DuplicateGroupItem(DuplicateGroup g)
    {
        Group = g;
        var keep = g.SuggestedKeep;
        Files = g.Files.Select(f => new SelectableItem { Id = f.Path, Label = f.RelativePath, Detail = $"{f.ModifiedUtc.ToLocalTime():d}", IsSelected = f.Path != keep.Path, State = f.Path == keep.Path ? "Keep (oldest copy)" : "Duplicate" }).ToList();
    }
    public DuplicateGroup Group { get; }
    public IReadOnlyList<SelectableItem> Files { get; }
    public string Title => $"{Group.Files.Count} identical files · {ToolOutputParsers.FormatBytes(Group.Size)} each · {ToolOutputParsers.FormatBytes(Group.WastedBytes)} reclaimable";
}

public sealed class CategoryBar
{
    public string Name { get; init; } = "";
    public string Detail { get; init; } = "";
    public double Fraction { get; init; }
    public double Width => Math.Max(4, Fraction * 260);
}

public sealed partial class FilesViewModel : PageViewModel
{
    public FilesViewModel(Shell shell) : base(shell) { _folder = S.Platform.Folders.Downloads; }
    public override string Title => "File organizer";
    public override string Subtitle => "Analyse a folder, find duplicates, and reorganise it with the customer's approval. Files are moved, never deleted, and every batch can be undone.";

    [ObservableProperty] private string _folder;
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private FolderAnalysis? _analysis;
    [ObservableProperty] private OrganizationPlan? _plan;
    [ObservableProperty] private string _strategy = "By file type";
    [ObservableProperty] private bool _topLevelOnly = true;
    [ObservableProperty] private bool _skipShortcuts = true;
    public string[] Strategies { get; } = { "By file type", "By year and month", "By file type, then year" };
    public ObservableCollection<CategoryBar> Categories { get; } = new();
    public ObservableCollection<ScannedFile> Largest { get; } = new();
    public ObservableCollection<string> Suggestions { get; } = new();
    public ObservableCollection<DuplicateGroupItem> Duplicates { get; } = new();
    public ObservableCollection<PlannedMove> Moves { get; } = new();
    public ObservableCollection<FileOperationLog> Logs { get; } = new();
    public bool HasAnalysis => Analysis is not null;
    public bool HasPlan => Plan is { Moves.Count: > 0 };
    public bool HasDuplicates => Duplicates.Count > 0;
    public string AnalysisSummary => Analysis is null ? "" : $"{Analysis.FileCount:N0} files · {ToolOutputParsers.FormatBytes(Analysis.TotalBytes)} · {Analysis.FolderCount:N0} folders{(Analysis.Truncated ? " · scan limit reached" : "")}";
    public string PlanSummary => Plan is null ? "" : $"{Plan.Moves.Count} file(s) to move ({ToolOutputParsers.FormatBytes(Plan.TotalBytes)}) into {Plan.FoldersToCreate.Count} new folder(s); {Plan.Skipped.Count} skipped.";
    partial void OnAnalysisChanged(FolderAnalysis? value) { OnPropertyChanged(nameof(HasAnalysis)); OnPropertyChanged(nameof(AnalysisSummary)); }
    partial void OnPlanChanged(OrganizationPlan? value) { OnPropertyChanged(nameof(HasPlan)); OnPropertyChanged(nameof(PlanSummary)); }

    public override Task OnActivatedAsync() { LoadLogs(); return Task.CompletedTask; }

    private void LoadLogs()
    {
        Logs.Clear();
        foreach (var l in S.FileOps.LoadAll().Take(20)) Logs.Add(l);
    }

    [RelayCommand] private void UseFolder(string which) => Folder = which switch
    {
        "Desktop" => S.Platform.Folders.Desktop,
        "Documents" => S.Platform.Folders.Documents,
        "Pictures" => S.Platform.Folders.Pictures,
        "Videos" => S.Platform.Folders.Videos,
        _ => S.Platform.Folders.Downloads,
    };

    [RelayCommand]
    private async Task Browse()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } w }) return;
        var picked = await w.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose a folder to analyse", AllowMultiple = false });
        if (picked.FirstOrDefault()?.TryGetLocalPath() is { } p) Folder = p;
    }

    private bool CheckFolder()
    {
        if (!Directory.Exists(Folder)) { Dialogs.Toast("Folder not found.", ToastKind.Warning); return false; }
        if (ProtectedComponents.IsProtectedFolder(Folder, S.Platform.Folders))
        {
            _ = Dialogs.ShowMessageAsync("Protected location", "System, program, and application-data folders (and whole drives or the profile root) are never reorganised. Choose a user folder such as Downloads, Desktop, or Documents.");
            return false;
        }
        return true;
    }

    [RelayCommand]
    private async Task Analyze()
    {
        if (!CheckFolder()) return;
        Busy = true;
        Plan = null; Moves.Clear(); Duplicates.Clear(); OnPropertyChanged(nameof(HasDuplicates));
        try
        {
            var progress = new Progress<int>(n => Status = $"Scanned {n:N0} files…");
            Analysis = await Task.Run(() => new FolderAnalyzer().Analyze(Folder, new FolderScanOptions(), progress));
            Categories.Clear();
            var max = Math.Max(1, Analysis.Categories.FirstOrDefault()?.Bytes ?? 1);
            foreach (var c in Analysis.Categories)
                Categories.Add(new CategoryBar { Name = FileCategories.FolderName(c.Category), Detail = $"{c.Count:N0} · {ToolOutputParsers.FormatBytes(c.Bytes)}", Fraction = (double)c.Bytes / max });
            Largest.Clear();
            foreach (var f in Analysis.LargestFiles.Take(10)) Largest.Add(f);
            Suggestions.Clear();
            foreach (var s in Analysis.Suggestions) Suggestions.Add(s);
            Status = "Analysis complete. Nothing has been changed.";
        }
        catch (Exception ex) { Status = ex.Message; }
        finally { Busy = false; }
    }

    [RelayCommand]
    private async Task FindDuplicates()
    {
        if (Analysis is null) return;
        Busy = true;
        try
        {
            var progress = new Progress<int>(n => Status = $"Comparing contents… {n:N0} files checked");
            var groups = await Task.Run(() => new DuplicateFinder().Find(Analysis.Files, 1, progress));
            Duplicates.Clear();
            foreach (var g in groups.Take(200)) Duplicates.Add(new DuplicateGroupItem(g));
            Status = groups.Count == 0 ? "No duplicate files found (byte-for-byte comparison)." : $"{groups.Count} group(s) of identical files · {ToolOutputParsers.FormatBytes(groups.Sum(g => g.WastedBytes))} reclaimable.";
            OnPropertyChanged(nameof(HasDuplicates));
        }
        finally { Busy = false; }
    }

    [RelayCommand]
    private async Task MoveDuplicatesToReview()
    {
        var selected = Duplicates.SelectMany(d => d.Files).Where(f => f.IsSelected).Select(f => f.Id).ToList();
        if (selected.Count == 0 || Analysis is null) return;
        var plan = OrganizationPlanner.PlanDuplicateReview(Analysis.Root, Duplicates.Select(d => d.Group), selected);
        var review = Path.Combine(Analysis.Root, FileOperationExecutor.ReviewFolderName);
        if (!await Dialogs.ConfirmAsync("Move duplicates to a review folder?",
                $"{plan.Moves.Count} duplicate file(s) will be MOVED (not deleted) to:\n{review}\n\nThe customer can check them and delete the folder later. This can be undone from the operation log.", "Move duplicates")) return;
        await ExecuteAsync(plan, "duplicates", $"Duplicate review in {Path.GetFileName(Analysis.Root)}");
        await FindDuplicates();
    }

    [RelayCommand]
    private void BuildPlan()
    {
        if (Analysis is null) return;
        var options = new OrganizePlanOptions
        {
            Strategy = Strategy switch { "By year and month" => OrganizeStrategy.ByYearMonth, "By file type, then year" => OrganizeStrategy.ByCategoryThenYear, _ => OrganizeStrategy.ByCategory },
            TopLevelOnly = TopLevelOnly,
            SkipShortcuts = SkipShortcuts,
        };
        Plan = new OrganizationPlanner().Plan(Analysis, options);
        Moves.Clear();
        foreach (var m in Plan.Moves) Moves.Add(m);
        Status = Plan.Moves.Count == 0 ? "Nothing to move with these options." : "Preview ready. Review the moves with the customer before applying.";
    }

    [RelayCommand]
    private async Task ApplyPlan()
    {
        if (Plan is null || Plan.Moves.Count == 0) return;
        if (!await Dialogs.ConfirmAsync("Apply this organisation?",
                $"{Plan.Moves.Count} file(s) will be moved within {Plan.Root}.\n\n• Nothing is deleted or overwritten.\n• Every move is logged and the whole batch can be undone.\n• Files modified in the last 15 minutes, cloud-only files, and shortcuts are left in place.\n\nConfirm the customer has approved these moves.", "Move files")) return;
        await ExecuteAsync(Plan, "organize", $"Organised {Path.GetFileName(Plan.Root)} ({Strategy.ToLowerInvariant()})");
        Plan = null;
        Moves.Clear();
        await Analyze();
    }

    private async Task ExecuteAsync(OrganizationPlan plan, string kind, string description)
    {
        Busy = true;
        try
        {
            var progress = new Progress<(int Done, int Total)>(p => Status = $"Moving {p.Done}/{p.Total}…");
            var log = await S.FileOps.ExecuteAsync(plan, kind, description, S.ActiveSession?.Id, progress);
            if (S.ActiveSession is { } session) { session.FileOperationLogIds.Add(log.Id); S.Sessions.Save(session); }
            S.Log($"File operation {log.Id}: {log.Status}, {log.Done} moved, {log.Failed} failed");
            Dialogs.Toast($"{log.Done} file(s) moved{(log.Failed > 0 ? $", {log.Failed} skipped" : "")}. Undo is available in the log.", log.Failed == 0 ? ToastKind.Success : ToastKind.Warning);
            LoadLogs();
        }
        finally { Busy = false; }
    }

    [RelayCommand]
    private async Task Undo(FileOperationLog log)
    {
        if (!log.CanUndo) return;
        if (!await Dialogs.ConfirmAsync("Undo this file operation?", $"{log.Description}\n{log.Done} file(s) will be moved back to their original locations. Files are never overwritten.", "Undo moves")) return;
        Busy = true;
        try
        {
            var result = await S.FileOps.UndoAsync(log.Id);
            Dialogs.Toast($"Undo: {result.Status}.", result.Status == "Undone" ? ToastKind.Success : ToastKind.Warning);
            LoadLogs();
        }
        finally { Busy = false; }
    }

    [RelayCommand] private void Reveal(string path) => S.Platform.Shell.Launch(new LaunchTarget(LaunchKind.RevealFile, path));
}

// =====================================================================  History

public sealed partial class HistoryViewModel : PageViewModel
{
    private bool _stale = true;
    public HistoryViewModel(Shell shell) : base(shell) { }
    public override string Title => "Change history";
    public override string Subtitle => "Every change the console has made on this PC, with its backup, verification result, and rollback.";

    public ObservableCollection<ChangeRecord> Records { get; } = new();
    [ObservableProperty] private ChangeRecord? _selected;
    [ObservableProperty] private string _filter = "All changes";
    public string[] Filters { get; } = { "All changes", "Reversible", "This session", "Problems" };
    public bool HasSelection => Selected is not null;
    public bool HasRecords => Records.Count > 0;
    partial void OnSelectedChanged(ChangeRecord? value) => OnPropertyChanged(nameof(HasSelection));
    partial void OnFilterChanged(string value) => Load();

    public void MarkStale() => _stale = true;

    public override Task OnActivatedAsync()
    {
        if (_stale) Load();
        return Task.CompletedTask;
    }

    private void Load()
    {
        _stale = false;
        var sessionId = S.ActiveSession?.Id;
        var keep = Selected?.Id;
        Records.Clear();
        foreach (var r in S.Journal.LoadAll().Where(r => Filter switch
                 {
                     "Reversible" => r.CanRollback,
                     "This session" => sessionId is not null && r.SessionId == sessionId,
                     "Problems" => r.Status is ChangeStatus.Failed or ChangeStatus.Interrupted or ChangeStatus.RollbackFailed or ChangeStatus.VerificationFailed,
                     _ => true,
                 }))
            Records.Add(r);
        Selected = Records.FirstOrDefault(r => r.Id == keep) ?? Records.FirstOrDefault();
        OnPropertyChanged(nameof(HasRecords));
    }

    [RelayCommand]
    private async Task Rollback()
    {
        if (Selected is null) return;
        await Runner.RollbackAsync(Selected);
        Load();
    }

    [RelayCommand] private void Reload() => Load();
}

// =====================================================================  Data & about

public sealed partial class DataViewModel : PageViewModel
{
    public DataViewModel(Shell shell) : base(shell)
    {
        _technicianName = S.Settings.TechnicianName;
        _businessContact = S.Settings.BusinessContact;
        _sampleSeconds = S.Settings.ScanSampleSeconds;
    }

    public override string Title => "Data & about";
    public override string Subtitle => "Where the console stores its records, how to remove them, and licensing information.";

    [ObservableProperty] private string _technicianName;
    [ObservableProperty] private string _businessContact;
    [ObservableProperty] private int _sampleSeconds;
    [ObservableProperty] private string _dataSize = "";
    public int[] SampleOptions { get; } = { 3, 5, 10, 20 };
    public string DataRoot => S.Paths.Root;
    public string ModeText => S.Paths.IsPortable
        ? "Portable mode: all records are stored in the 'softreset-data' folder next to the console. Nothing is written to the customer's profile."
        : "The folder next to the console is not writable, so records are stored in the current user's local application data.";
    public string Version => typeof(DataViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    public string PlatformText => $"{S.Platform.Name} · Windows build {S.Platform.OsBuild} · {(S.Platform.IsElevated ? "administrator" : "standard rights")}";

    public override Task OnActivatedAsync()
    {
        DataSize = ToolOutputParsers.FormatBytes(Directory.Exists(S.Paths.Root) ? new DirectoryInfo(S.Paths.Root).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0);
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void SaveSettings()
    {
        S.Settings.TechnicianName = TechnicianName.Trim();
        S.Settings.BusinessContact = BusinessContact.Trim();
        S.Settings.ScanSampleSeconds = SampleSeconds;
        S.SaveSettings();
        Dialogs.Toast("Settings saved.", ToastKind.Success);
    }

    [RelayCommand] private void OpenDataFolder() => S.Platform.Shell.Launch(new LaunchTarget(LaunchKind.Folder, S.Paths.Root));
    [RelayCommand] private void OpenReportsFolder() => S.Platform.Shell.Launch(new LaunchTarget(LaunchKind.Folder, S.Paths.Reports));

    [RelayCommand]
    private async Task RemoveAllData()
    {
        var pending = S.Journal.LoadAll().Count(r => r.CanRollback);
        var msg = "This permanently deletes everything the console has stored on this PC or drive: sessions, reports, scans, the change journal and backups, file-operation logs, settings, and logs.\n\n" +
                  (pending > 0 ? $"⚠ {pending} change(s) can still be rolled back. After removal their backups are gone and they can no longer be undone by the console.\n\n" : "") +
                  "Not affected: changes already made to Windows, System Restore points (managed in System Protection), and any '_softreset_review' folders created by the file organizer inside customer folders (review and delete those with the customer).";
        if (!await Dialogs.ConfirmAsync("Remove all application data?", msg, "Remove everything", danger: true, requireTypedText: "DELETE")) return;
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(S.Paths.Root)) Directory.Delete(dir, recursive: true);
            foreach (var f in Directory.EnumerateFiles(S.Paths.Root)) File.Delete(f);
            S.Paths.EnsureCreated();
            Dialogs.Toast("All console data removed. You can delete the console's folder to remove the program itself.", ToastKind.Success);
        }
        catch (Exception ex) { await Dialogs.ShowMessageAsync("Some files could not be removed", ex.Message); }
        await OnActivatedAsync();
    }

    [RelayCommand] private void OpenLink(string url) => S.Platform.Shell.Launch(new LaunchTarget(LaunchKind.Uri, url));
}

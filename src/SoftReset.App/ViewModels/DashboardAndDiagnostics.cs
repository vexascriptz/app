using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoftReset.Core.Diagnostics;
using SoftReset.Core.Models;
using SoftReset.Core.Optimization;
using SoftReset.Core.Platform;
using SoftReset.Core.Reports;

namespace SoftReset.App.ViewModels;

public sealed partial class DashboardViewModel : PageViewModel
{
    private DispatcherTimer? _timer;

    public DashboardViewModel(Shell shell) : base(shell) { }
    public override string Title => "Home";
    public override string Subtitle => "What would you like to do?";

    [ObservableProperty] private IReadOnlyList<double> _cpuHistory = Array.Empty<double>();
    [ObservableProperty] private IReadOnlyList<double> _memoryHistory = Array.Empty<double>();
    [ObservableProperty] private double _cpuNow;
    [ObservableProperty] private double _memoryNow;
    [ObservableProperty] private string _memoryText = "";
    [ObservableProperty] private Rating? _cpuRating;
    [ObservableProperty] private Rating? _memoryRating;
    [ObservableProperty] private DiagnosticReport? _lastReport;
    [ObservableProperty] private bool _isChecking;

    /// <summary>The PC check-up: every reading with a Good / Warm / Problem label, grouped.</summary>
    public ObservableCollection<CheckupGroup> Checkup { get; } = new();
    public bool HasCheckup => Checkup.Count > 0;
    public Rating? HealthRating => LastReport is { } r ? Ratings.HealthScore(r.HealthScore) : null;
    [ObservableProperty] private string _checkupSummary = "";
    /// <summary>Turn off the automatic check when Home opens (tests).</summary>
    public static bool AutoCheckOnOpen { get; set; } = true;
    partial void OnLastReportChanged(DiagnosticReport? value) { OnPropertyChanged(nameof(HealthRating)); OnPropertyChanged(nameof(HasReport)); }
    public ObservableCollection<Finding> TopFindings { get; } = new();

    public int InterruptedCount => S.InterruptedChanges.Count;
    public bool HasInterrupted => InterruptedCount > 0;
    public bool HasReport => LastReport is not null;
    /// <summary>Every tool except Home and About, shown as tiles so it's clear what each one does.</summary>
    public IEnumerable<NavItem> Tools => Shell.Main.NavItems.Where(n => n.Page is not DashboardViewModel and not DataViewModel);

    private readonly List<double> _cpu = new();
    private readonly List<double> _mem = new();

    public override Task OnActivatedAsync()
    {
        LastReport = S.LastReport;
        TopFindings.Clear();
        foreach (var f in LastReport?.Findings.Take(4) ?? Enumerable.Empty<Finding>()) TopFindings.Add(f);
        if (LastReport is not null) BuildCheckup(LastReport);
        else if (AutoCheckOnOpen && !IsChecking) _ = CheckNowAsync();
        if (_timer is null)
        {
            S.Platform.Live.Sample();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, _) => Tick();
            _timer.Start();
        }
        return Task.CompletedTask;
    }

    internal void Tick()
    {
        var s = S.Platform.Live.Sample();
        _cpu.Add(s.CpuPercent);
        _mem.Add(s.MemoryUsedPercent);
        if (_cpu.Count > 60) _cpu.RemoveAt(0);
        if (_mem.Count > 60) _mem.RemoveAt(0);
        CpuHistory = _cpu.ToArray();
        MemoryHistory = _mem.ToArray();
        CpuNow = Math.Round(s.CpuPercent);
        MemoryNow = Math.Round(s.MemoryUsedPercent);
        CpuRating = Ratings.CpuUsage(s.CpuPercent);
        MemoryRating = Ratings.MemoryUsage(s.MemoryUsedPercent);
        MemoryText = s.MemoryTotalBytes > 0 ? $"{Core.Parsing.ToolOutputParsers.FormatBytes(s.MemoryTotalBytes - s.MemoryAvailableBytes)} of {Core.Parsing.ToolOutputParsers.FormatBytes(s.MemoryTotalBytes)}" : "";
    }

    [RelayCommand] private async Task RunDiagnostics() { Shell.Main.Navigate(Shell.Main.Diagnostics); await Shell.Main.Diagnostics.ScanCommand.ExecuteAsync(null); }
    [RelayCommand] private void OpenHistory() => Shell.Main.Navigate(Shell.Main.History);
    [RelayCommand] private void OpenTool(NavItem item) => Shell.Main.Navigate(item.Page);
    [RelayCommand] private async Task AutoOptimize() { await Auto.RunAsync(Core.Optimization.AutoScope.Everything, "Auto optimize this PC"); RefreshUndoAuto(); await CheckNowAsync(); }
    [RelayCommand] private async Task UndoAuto() { await Auto.UndoLastAsync(); RefreshUndoAuto(); await CheckNowAsync(); }

    /// <summary>A quick read-only scan (about a second of CPU sampling) that fills the check-up.</summary>
    [RelayCommand]
    private async Task CheckNow() => await CheckNowAsync();

    public async Task CheckNowAsync()
    {
        if (IsChecking) return;
        IsChecking = true;
        try
        {
            var options = new CollectionOptions { CpuSampleDuration = TimeSpan.FromSeconds(1), IncludeConnectivityTests = S.Settings.IncludeConnectivityTests };
            LastReport = await DiagnosticsViewModel.CollectAndAnalyzeAsync(Shell, options, new Progress<CollectionProgress>());
            BuildCheckup(LastReport);
        }
        catch (Exception ex) { S.Log($"Check-up failed: {ex}"); Dialogs.Toast("Couldn't finish the check-up. Try Find problems.", ToastKind.Warning); }
        finally { IsChecking = false; }
    }

    private void BuildCheckup(DiagnosticReport report)
    {
        var items = PcCheckup.Build(report.Snapshot, DateTimeOffset.Now);
        Checkup.Clear();
        foreach (var g in items.GroupBy(i => i.Group))
            Checkup.Add(new CheckupGroup(g.Key, g.Select(i => new CheckupRow(i, this)).ToList()));
        var problems = PcCheckup.CountProblems(items);
        CheckupSummary = problems == 0 ? "Everything looks good." : problems == 1 ? "1 thing needs a look." : $"{problems} things need a look.";
        OnPropertyChanged(nameof(HasCheckup));
    }

    internal async Task FixAsync(CheckupItem item)
    {
        string? open = null;
        switch (item.Fix)
        {
            case CheckupFix.FreeUpSpace: await Auto.RunAsync(AutoScope.Cleanup, "Free up space"); break;
            case CheckupFix.SmootherDisplay: await Auto.RunAsync(AutoScope.Graphics, "Make the screen smoother"); break;
            case CheckupFix.StartupApps: await Auto.RunAsync(AutoScope.Startup, "Stop unneeded apps starting with Windows"); break;
            case CheckupFix.PowerPlan: await Auto.RunAsync(AutoScope.Power, "Set the best power plan"); break;
            case CheckupFix.OpenGraphics: Shell.Main.Navigate(Shell.Main.Graphics); return;
            case CheckupFix.OpenSecurity: open = "windowsdefender:"; break;
            case CheckupFix.OpenUpdates: open = "ms-settings:windowsupdate"; break;
            case CheckupFix.OpenBackup: open = "ms-settings:backup"; break;
            case CheckupFix.OpenNetwork: open = "ms-settings:network-status"; break;
            default: return;
        }
        if (open is not null)
        {
            var r = S.Platform.Shell.Launch(ShortcutParser.Parse(open));
            if (!r.Success) Dialogs.Toast(r.Message, ToastKind.Warning);
            else if (S.DemoMode) Dialogs.Toast(r.Message);
            return;
        }
        RefreshUndoAuto();
        await CheckNowAsync();
    }
}

public sealed record CheckupGroup(string Title, IReadOnlyList<CheckupRow> Items);

/// <summary>One check-up line with its button.</summary>
public sealed partial class CheckupRow : ObservableObject
{
    private readonly DashboardViewModel _owner;
    public CheckupRow(CheckupItem item, DashboardViewModel owner) { Item = item; _owner = owner; }
    public CheckupItem Item { get; }
    public string Name => Item.Name;
    public string Value => Item.Value;
    public Rating Rating => Item.Rating;
    public string Meaning => Item.Rating.Meaning;
    public bool ShowFix => Item.ShowFix;
    /// <summary>"Fix" when the app can do it, "Open" when it opens the right Windows page.</summary>
    public string FixLabel => Item.Fix is CheckupFix.FreeUpSpace or CheckupFix.SmootherDisplay or CheckupFix.StartupApps or CheckupFix.PowerPlan ? "Fix" : "Open";
    public bool FixIsPrimary => FixLabel == "Fix";
    public bool ShowFixButton => ShowFix && FixIsPrimary;
    public bool ShowOpenButton => ShowFix && !FixIsPrimary;
    [RelayCommand] private Task Fix() => _owner.FixAsync(Item);
}

/// <summary>A finding with UI actions for its recommendations.</summary>
public sealed partial class FindingItem : ObservableObject
{
    private readonly Shell _shell;

    public FindingItem(Finding f, Shell shell)
    {
        Finding = f;
        _shell = shell;
        Recommendations = f.Recommendations.Select(r => new RecommendationItem(r, shell)).ToList();
    }

    private RecommendationItem? PrimaryAction => Recommendations.FirstOrDefault(r => r.HasAction);
    private RecommendationItem? PrimaryShortcut => Recommendations.FirstOrDefault(r => r.HasShortcut);

    /// <summary>Every problem gets one button: Fix when the app can fix it, otherwise Open (the right Windows page).</summary>
    public string PrimaryLabel => PrimaryAction is not null ? "Fix" : "Open";
    public bool PrimaryIsFix => PrimaryAction is not null;

    [RelayCommand]
    private async Task Primary()
    {
        if (PrimaryAction is { } a) { await a.RunCommand.ExecuteAsync(null); return; }
        if (PrimaryShortcut is { } s) { s.OpenCommand.Execute(null); return; }
        var target = Finding.Category switch
        {
            FindingCategory.Storage => "ms-settings:storagesense",
            FindingCategory.Startup => "ms-settings:startupapps",
            FindingCategory.Updates => "ms-settings:windowsupdate",
            FindingCategory.Network => "ms-settings:network-status",
            FindingCategory.Security => "windowsdefender:",
            FindingCategory.Power => "ms-settings:powersleep",
            FindingCategory.Gaming => "ms-settings:gaming-gamemode",
            FindingCategory.Display => "ms-settings:display",
            FindingCategory.Drivers => "devmgmt.msc",
            FindingCategory.Stability => "perfmon.exe /rel",
            FindingCategory.DiskHealth => "dfrgui.exe",
            _ => "taskmgr.exe",
        };
        var r = _shell.Services.Platform.Shell.Launch(ShortcutParser.Parse(target));
        if (!r.Success) _shell.Main.Toast(r.Message, ToastKind.Warning);
        else if (_shell.Services.DemoMode) _shell.Main.Toast(r.Message);
    }

    public Finding Finding { get; }
    public IReadOnlyList<RecommendationItem> Recommendations { get; }
    [ObservableProperty] private bool _isExpanded;
    public string ConfidenceText => $"{Finding.Confidence} confidence";
    public string CategoryText => Finding.Category.ToString();
    public bool HasCauses => Finding.LikelyCauses.Count > 0;
    public bool HasUncertainties => Finding.Uncertainties.Count > 0;

    [RelayCommand] private void Toggle() => IsExpanded = !IsExpanded;
}

public sealed partial class RecommendationItem : ObservableObject
{
    private readonly Shell _shell;
    public RecommendationItem(Recommendation r, Shell shell) { Recommendation = r; _shell = shell; }
    public Recommendation Recommendation { get; }
    public string Text => Recommendation.Text;
    public bool HasAction => Recommendation.ActionId is not null && _shell.Services.Actions.TryGet(Recommendation.ActionId, out _);
    public bool HasShortcut => Recommendation.ShortcutTarget is not null;
    public string ActionLabel => Recommendation.ActionId is { } id && _shell.Services.Actions.TryGet(id, out var a)
        ? (NeedsSelection(id) && id is not (ActionIds.StartupUserItems or ActionIds.StartupMachineItems or ActionIds.StartupLogonTasks) ? "Review…" : "Fix")
        : "";
    public string ShortcutLabel => Recommendation.ShortcutLabel ?? "Open";

    internal static bool NeedsSelection(string actionId) => actionId is ActionIds.StartupUserItems or ActionIds.StartupMachineItems or ActionIds.StartupLogonTasks
        or ActionIds.EndProcesses or ActionIds.GpuPreference or ActionIds.WingetUpgrade or ActionIds.ClearAppCache or ActionIds.VisualEffects;

    [RelayCommand]
    private async Task Run()
    {
        var id = Recommendation.ActionId!;
        if (id is ActionIds.StartupUserItems or ActionIds.StartupMachineItems or ActionIds.StartupLogonTasks)
        {
            await _shell.Auto.RunAsync(Core.Optimization.AutoScope.Startup, "Stop unneeded apps starting with Windows");
            return;
        }
        if (NeedsSelection(id) && Recommendation.ActionParameters is null)
        {
            _shell.Main.Navigate(_shell.Main.Optimization);
            await _shell.Main.Optimization.SelectActionAsync(id);
            return;
        }
        await _shell.Runner.RunAsync(new ActionRequest { ActionId = id, Parameters = new(Recommendation.ActionParameters ?? new()) });
    }

    [RelayCommand]
    private void Open()
    {
        var t = Recommendation.ShortcutTarget!;
        var target = ShortcutParser.Parse(t);
        var r = _shell.Services.Platform.Shell.Launch(target);
        if (!r.Success) _shell.Main.Toast(r.Message, ToastKind.Warning);
        else if (_shell.Services.DemoMode) _shell.Main.Toast(r.Message);
    }
}

public static class ShortcutParser
{
    /// <summary>Turns a finding shortcut ("ms-settings:…", "devmgmt.msc", "perfmon.exe /rel") into a validated launch target.</summary>
    public static LaunchTarget Parse(string s)
    {
        if (s.Contains(':') && !s.Contains('\\')) return new LaunchTarget(LaunchKind.Uri, s);
        var parts = s.Split(' ', 2);
        var exe = parts[0];
        if (exe.EndsWith(".msc", StringComparison.OrdinalIgnoreCase)) return new LaunchTarget(LaunchKind.Msc, exe);
        if (exe.EndsWith(".cpl", StringComparison.OrdinalIgnoreCase)) return new LaunchTarget(LaunchKind.ControlPanelApplet, exe);
        return new LaunchTarget(LaunchKind.SystemTool, exe, parts.Length > 1 ? parts[1] : null);
    }
}

public sealed partial class DiagnosticsViewModel : PageViewModel
{
    public DiagnosticsViewModel(Shell shell) : base(shell)
    {
        _sampleSeconds = S.Settings.ScanSampleSeconds;
        _includeConnectivity = S.Settings.IncludeConnectivityTests;
    }

    public override string Title => "Find problems";
    public override string Subtitle => "Scans this PC and lists what's wrong. Nothing is changed.";

    public int[] SampleOptions { get; } = { 3, 5, 10, 20 };
    [ObservableProperty] private int _sampleSeconds;
    [ObservableProperty] private bool _includeConnectivity;
    [ObservableProperty] private bool _includeSignatures = true;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private int _progress;
    [ObservableProperty] private string _stage = "";
    [ObservableProperty] private DiagnosticReport? _report;
    [ObservableProperty] private string _severityFilter = "All problems";
    [ObservableProperty] private string _categoryFilter = "All areas";
    public string[] SeverityFilters { get; } = { "All problems", "Serious only", "Serious and medium", "Minor only" };
    public ObservableCollection<string> CategoryFilters { get; } = new() { "All areas" };
    public ObservableCollection<FindingItem> Findings { get; } = new();
    public ObservableCollection<CollectionIssue> Issues { get; } = new();
    private List<FindingItem> _all = new();

    public bool HasReport => Report is not null;
    public bool NoReport => Report is null && !IsScanning;
    public string Summary => Report is null ? "" : $"{Report.Findings.Count} findings from {Report.RulesEvaluated} checks · scanned {Report.CreatedAt.LocalDateTime:g} in {Report.Snapshot.CollectionDuration.TotalSeconds:0} s";
    public int CriticalHigh => Report?.Findings.Count(f => f.Severity >= Severity.High) ?? 0;
    public int MediumCount => Report?.Count(Severity.Medium) ?? 0;
    public int LowCount => (Report?.Count(Severity.Low) ?? 0) + (Report?.Count(Severity.Info) ?? 0);
    public string MachineLine => Report is null ? "" : $"{Report.Snapshot.Os.FriendlyName} · {Report.Snapshot.Hardware.CpuName} · {Core.Parsing.ToolOutputParsers.FormatBytes(Report.Snapshot.Hardware.TotalMemoryBytes)} RAM";

    partial void OnReportChanged(DiagnosticReport? value)
    {
        OnPropertyChanged(nameof(HasReport)); OnPropertyChanged(nameof(NoReport)); OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(CriticalHigh)); OnPropertyChanged(nameof(MediumCount)); OnPropertyChanged(nameof(LowCount)); OnPropertyChanged(nameof(MachineLine));
    }
    partial void OnIsScanningChanged(bool value) => OnPropertyChanged(nameof(NoReport));
    partial void OnSeverityFilterChanged(string value) => ApplyFilter();
    partial void OnCategoryFilterChanged(string value) => ApplyFilter();

    public static async Task<DiagnosticReport> CollectAndAnalyzeAsync(Shell shell, CollectionOptions options, IProgress<CollectionProgress> progress, CancellationToken ct = default)
    {
        var snap = await shell.Services.Platform.Collector.CollectAsync(options, progress, ct);
        var report = await Task.Run(() => shell.Services.Engine.Analyze(snap), ct);
        shell.Services.LastReport = report;
        Core.Storage.Json.WriteFile(Path.Combine(shell.Services.Paths.Scans, $"scan-{report.CreatedAt:yyyyMMdd-HHmmss}.json"), report);
        return report;
    }

    /// <summary>Fix everything that can be fixed automatically, then scan again to show the result.</summary>
    [RelayCommand]
    private async Task FixAll()
    {
        var done = await Auto.RunAsync(Core.Optimization.AutoScope.Everything, "Fix everything automatically");
        RefreshUndoAuto();
        if (done.Count > 0) await Scan();
    }

    [RelayCommand]
    private async Task Scan()
    {
        if (IsScanning) return;
        IsScanning = true;
        Progress = 0;
        Stage = "Starting…";
        try
        {
            var progress = new Progress<CollectionProgress>(p => { Progress = p.Percent; Stage = p.Stage; });
            var options = new CollectionOptions { CpuSampleDuration = TimeSpan.FromSeconds(S.Settings.ScanSampleSeconds), IncludeConnectivityTests = IncludeConnectivity, IncludeSignatureChecks = IncludeSignatures };
            Report = await CollectAndAnalyzeAsync(Shell, options, progress);
            Load(Report);
            Dialogs.Toast($"Scan complete: {Report.Findings.Count} findings.", ToastKind.Success);
        }
        catch (Exception ex)
        {
            S.Log($"Scan failed: {ex}");
            await Dialogs.ShowMessageAsync("Scan failed", ex.Message);
        }
        finally { IsScanning = false; }
    }

    public void Load(DiagnosticReport report)
    {
        Report = report;
        _all = report.Findings.Select(f => new FindingItem(f, Shell)).ToList();
        if (_all.Count > 0) _all[0].IsExpanded = true;
        CategoryFilters.Clear();
        CategoryFilters.Add("All areas");
        foreach (var c in report.Findings.Select(f => f.Category.ToString()).Distinct().OrderBy(c => c)) CategoryFilters.Add(c);
        CategoryFilter = "All areas";
        Issues.Clear();
        foreach (var i in report.Snapshot.CollectionIssues) Issues.Add(i);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        Findings.Clear();
        foreach (var f in _all.Where(f => SeverityFilter switch
                 {
                     "Serious only" => f.Finding.Severity >= Severity.High,
                     "Serious and medium" => f.Finding.Severity >= Severity.Medium,
                     "Minor only" => f.Finding.Severity <= Severity.Low,
                     _ => true,
                 } && (CategoryFilter == "All areas" || f.CategoryText == CategoryFilter)))
            Findings.Add(f);
    }

    [RelayCommand]
    private async Task ExportPdf()
    {
        if (Report is null) return;
        var file = Path.Combine(S.Paths.Reports, $"diagnostic-report-{Report.CreatedAt:yyyyMMdd-HHmm}.pdf");
        await File.WriteAllBytesAsync(file, ReportBuilder.BuildDiagnosticReport(Report));
        Dialogs.Toast($"Saved {Path.GetFileName(file)} to the reports folder.", ToastKind.Success);
        S.Platform.Shell.Launch(new LaunchTarget(LaunchKind.RevealFile, file));
    }
}

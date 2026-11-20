using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoftReset.Core.Diagnostics;
using SoftReset.Core.Models;
using SoftReset.Core.Optimization;
using SoftReset.Core.Platform;
using SoftReset.Core.Reports;
using SoftReset.Core.Safety;
using SoftReset.Core.Sessions;

namespace SoftReset.App.ViewModels;

public sealed partial class StepItem : ObservableObject
{
    public int Number { get; init; }
    public string Title { get; init; } = "";
    [ObservableProperty] private bool _isDone;
    [ObservableProperty] private bool _isCurrent;
}

public sealed partial class SelectableItem : ObservableObject
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public string Detail { get; init; } = "";
    public string? State { get; init; }
    public string? Recommendation { get; init; }
    public string? Warning { get; init; }
    public bool Selectable { get; init; } = true;
    [ObservableProperty] private bool _isSelected;
}

public sealed partial class ProposedFixItem : ObservableObject
{
    public ProposedFixItem(ProposedFix fix) => Fix = fix;
    public ProposedFix Fix { get; }
    public string Title => Fix.Title;
    public string Description => Fix.Description;
    public string Benefit => Fix.ExpectedBenefit;
    public string Risk => Fix.Risk;
    public bool IsManual => Fix.IsManual;
    public string Badges => string.Join(" · ", new[]
    {
        Fix.IsManual ? "Manual task" : null,
        Fix.RequiresAdmin ? "Needs administrator" : null,
        Fix.RequiresRestart ? "Restart required" : null,
        Fix.IsManual ? null : Fix.Reversibility switch { Reversibility.Full => "Reversible", Reversibility.Partial => "Partly reversible", _ => "Not reversible" },
    }.Where(x => x is not null));
    public bool NeedsItems { get; init; }
    public ObservableCollection<SelectableItem> Items { get; } = new();
    [ObservableProperty] private bool _isSelected = true;
    [ObservableProperty] private bool _approved = true;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _manualDone;
    [ObservableProperty] private string _manualOutcome = "";
    [ObservableProperty] private bool _itemsLoaded;
}

public sealed partial class SessionViewModel : PageViewModel
{
    public SessionViewModel(Shell shell) : base(shell)
    {
        string[] titles = { "Start", "Concerns", "Diagnose", "Baseline", "Select fixes", "Approval", "Apply", "Recheck", "Report", "Close" };
        for (var i = 0; i < titles.Length; i++) Steps.Add(new StepItem { Number = i + 1, Title = titles[i] });
        _technicianName = S.Settings.TechnicianName;
        UpdateSteps();
    }

    public override string Title => "Customer session";
    public override string Subtitle => "A guided, documented workflow: concerns → diagnosis → approved fixes → measured results → report.";

    public ObservableCollection<StepItem> Steps { get; } = new();
    public ObservableCollection<CustomerSession> PastSessions { get; } = new();
    [ObservableProperty] private CustomerSession? _session;
    [ObservableProperty] private int _step = 1;

    public bool IsStep1 => Step == 1; public bool IsStep2 => Step == 2; public bool IsStep3 => Step == 3; public bool IsStep4 => Step == 4;
    public bool IsStep5 => Step == 5; public bool IsStep6 => Step == 6; public bool IsStep7 => Step == 7; public bool IsStep8 => Step == 8;
    public bool IsStep9 => Step == 9; public bool IsStep10 => Step == 10;

    partial void OnStepChanged(int value)
    {
        foreach (var n in Enumerable.Range(1, 10)) OnPropertyChanged($"IsStep{n}");
        OnPropertyChanged(nameof(ApprovedItems));
        OnPropertyChanged(nameof(SessionChanges));
        UpdateSteps();
    }

    private void UpdateSteps()
    {
        var reached = (int)(Session?.Stage ?? 0);
        foreach (var s in Steps) { s.IsCurrent = s.Number == Step; s.IsDone = s.Number < Step || s.Number <= reached && s.Number != Step; }
    }

    // ---- Step 1
    [ObservableProperty] private string _technicianName = "";
    [ObservableProperty] private string _customerName = "";
    [ObservableProperty] private string _reference = "";
    [ObservableProperty] private string _deviceLabel = "";
    [ObservableProperty] private bool _includeIdentifiers = true;

    public override Task OnActivatedAsync()
    {
        PastSessions.Clear();
        foreach (var s in S.Sessions.List().Take(12)) PastSessions.Add(s);
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void StartSession()
    {
        S.Settings.TechnicianName = TechnicianName.Trim();
        S.SaveSettings();
        Session = S.Sessions.Start(TechnicianName, CustomerName, Reference, DeviceLabel, S.DemoMode ? "DESKTOP-DEMO01" : Environment.MachineName);
        Session.IncludeDeviceIdentifiers = IncludeIdentifiers;
        S.Sessions.Save(Session);
        S.SetActiveSession(Session);
        Concerns.Clear();
        Step = 2;
    }

    public void Resume(string id)
    {
        var s = S.Sessions.Load(id);
        if (s is null) return;
        Session = s;
        S.SetActiveSession(s.IsClosed ? null : s);
        Concerns.Clear();
        foreach (var c in s.Concerns) Concerns.Add(c);
        Notes = s.Notes;
        Baseline = S.Sessions.LoadScan(s.Baseline);
        FollowUp = S.Sessions.LoadScan(s.FollowUp);
        Fixes.Clear();
        foreach (var f in s.ProposedFixes) Fixes.Add(new ProposedFixItem(f) { IsSelected = true, Approved = f.Approved ?? true, Status = f.Outcome ?? "" });
        ReportPath = s.ReportPath;
        if (Baseline is not null && FollowUp is not null) BuildComparison();
        Step = s.IsClosed ? 10 : Math.Clamp((int)s.Stage + 1, 2, 10);
    }

    [RelayCommand] private void ResumeSession(CustomerSession s) => Resume(s.Id);

    [RelayCommand] private void Back() { if (Step > 2) Step--; }

    [RelayCommand] private void GoToStep(StepItem s) { if (Session is not null && s.Number >= 2 && (s.IsDone || s.Number <= (int)Session.Stage + 1)) Step = s.Number; }

    // ---- Step 2
    public ObservableCollection<CustomerConcern> Concerns { get; } = new();
    public string[] ConcernCategories { get; } = { "Performance", "Crashes / freezes", "Games", "Internet / Wi-Fi", "Storage", "Startup", "Pop-ups / security", "Printing / devices", "Files / backup", "Other" };
    [ObservableProperty] private string _newConcern = "";
    [ObservableProperty] private string _newConcernCategory = "Performance";
    [ObservableProperty] private string _notes = "";
    [ObservableProperty] private string? _sensitiveWarning;

    partial void OnNewConcernChanged(string value) => SensitiveWarning = SensitiveTextDetector.Check(value);
    partial void OnNotesChanged(string value) => SensitiveWarning = SensitiveTextDetector.Check(value);

    [RelayCommand]
    private void AddConcern()
    {
        if (string.IsNullOrWhiteSpace(NewConcern) || Session is null) return;
        if (SensitiveTextDetector.Check(NewConcern) is { } warn) { Dialogs.Toast(warn, ToastKind.Warning); return; }
        var c = new CustomerConcern { Text = NewConcern.Trim(), Category = NewConcernCategory };
        Concerns.Add(c);
        Session.Concerns.Add(c);
        NewConcern = "";
        S.Sessions.Save(Session);
    }

    [RelayCommand]
    private void RemoveConcern(CustomerConcern c)
    {
        Concerns.Remove(c);
        Session?.Concerns.Remove(c);
        if (Session is not null) S.Sessions.Save(Session);
    }

    [RelayCommand]
    private void ConcernsDone()
    {
        if (Session is null) return;
        if (SensitiveTextDetector.Check(Notes) is { } warn) { Dialogs.Toast(warn, ToastKind.Warning); return; }
        Session.Notes = Notes;
        Session.Advance(SessionStage.ConcernsRecorded);
        Session.Log($"{Concerns.Count} concern(s) recorded.");
        S.Sessions.Save(Session);
        Step = 3;
    }

    // ---- Step 3/4
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private int _progress;
    [ObservableProperty] private string _stage = "";
    [ObservableProperty] private bool _connectivityTest;
    [ObservableProperty] private DiagnosticReport? _baseline;
    public ObservableCollection<FindingItem> BaselineFindings { get; } = new();
    public string BaselineSummary => Baseline is null ? "" : $"{Baseline.Findings.Count} findings · {Baseline.Findings.Count(f => f.Severity >= Severity.High)} high or critical · scanned {Baseline.CreatedAt.LocalDateTime:t}";
    partial void OnBaselineChanged(DiagnosticReport? value)
    {
        OnPropertyChanged(nameof(BaselineSummary));
        BaselineFindings.Clear();
        foreach (var f in value?.Findings ?? new()) BaselineFindings.Add(new FindingItem(f, Shell));
    }

    [RelayCommand]
    private async Task RunBaseline()
    {
        if (Session is null || IsScanning) return;
        IsScanning = true;
        try
        {
            var progress = new Progress<CollectionProgress>(p => { Progress = p.Percent; Stage = p.Stage; });
            Baseline = await DiagnosticsViewModel.CollectAndAnalyzeAsync(Shell, new CollectionOptions { CpuSampleDuration = TimeSpan.FromSeconds(S.Settings.ScanSampleSeconds), IncludeConnectivityTests = ConnectivityTest }, progress);
            Session.Baseline = S.Sessions.SaveScan(Session, Baseline, "baseline");
            Session.Advance(SessionStage.BaselineCaptured);
            Session.Log($"Baseline captured: health {Baseline.HealthScore}, {Baseline.Findings.Count} findings.");
            S.Sessions.Save(Session);
            Shell.Main.Diagnostics.Load(Baseline);
            BuildProposals();
            Step = 4;
        }
        catch (Exception ex) { await Dialogs.ShowMessageAsync("Scan failed", ex.Message); }
        finally { IsScanning = false; }
    }

    [RelayCommand] private void BaselineDone() { Step = 5; }

    // ---- Step 5
    public ObservableCollection<ProposedFixItem> Fixes { get; } = new();
    [ObservableProperty] private string _newManualFix = "";

    private void BuildProposals()
    {
        Fixes.Clear();
        var seen = new HashSet<string>();
        foreach (var f in Baseline!.Findings.Where(f => f.Severity >= Severity.Low))
        {
            foreach (var r in f.Recommendations.Where(r => r.ActionId is not null))
            {
                if (!S.Actions.TryGet(r.ActionId!, out var action)) continue;
                var key = r.ActionId + string.Join(",", (r.ActionParameters ?? new()).OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}"));
                if (!seen.Add(key)) continue;
                var fix = new ProposedFix
                {
                    Title = action is IDynamicTitleAction dt ? dt.TitleFor(new ActionRequest { ActionId = action.Id, Parameters = new(r.ActionParameters ?? new()) }) : action.Title,
                    Description = r.Text,
                    FindingKey = f.EffectiveKey,
                    FindingTitle = f.Title,
                    ActionId = action.Id,
                    Parameters = new(r.ActionParameters ?? new()),
                    ExpectedBenefit = action.ExpectedBenefit,
                    Risk = action.Risks,
                    RequiresAdmin = action.RequiresAdmin,
                    RequiresRestart = action.RequiresRestart,
                    Reversibility = action.Reversibility,
                };
                // Default selection: only high-confidence, reversible fixes for medium+ findings are pre-selected.
                var preselect = f.Severity >= Severity.Medium && action.Reversibility != Reversibility.None && f.Confidence != Confidence.Low;
                Fixes.Add(new ProposedFixItem(fix) { IsSelected = preselect, NeedsItems = RecommendationItem.NeedsSelection(action.Id) });
            }
        }
    }

    [RelayCommand]
    private async Task LoadItems(ProposedFixItem item)
    {
        if (item.ItemsLoaded || item.Fix.ActionId is null) return;
        var action = S.Actions.Get(item.Fix.ActionId);
        var insp = await action.InspectAsync(S.ActionContext, new ActionRequest { ActionId = action.Id, Parameters = item.Fix.Parameters }, CancellationToken.None);
        item.Items.Clear();
        foreach (var i in insp.Items)
            item.Items.Add(new SelectableItem { Id = i.Id, Label = i.Label, Detail = i.Detail, State = i.CurrentState, Recommendation = i.Recommendation, Warning = i.Warning, Selectable = i.Selectable, IsSelected = i.Selectable && (i.Warning?.Contains("missing") ?? false) });
        item.ItemsLoaded = true;
    }

    [RelayCommand]
    private void AddManualFix()
    {
        if (string.IsNullOrWhiteSpace(NewManualFix)) return;
        Fixes.Add(new ProposedFixItem(new ProposedFix { Title = NewManualFix.Trim(), Description = "Performed by the technician outside the console.", ExpectedBenefit = "", Risk = "" }) { IsSelected = true });
        NewManualFix = "";
    }

    [RelayCommand]
    private async Task FixesDone()
    {
        if (Session is null) return;
        foreach (var f in Fixes.Where(f => f.IsSelected && f.NeedsItems))
        {
            if (!f.ItemsLoaded) await LoadItems(f);
            f.Fix.SelectedItems = f.Items.Where(i => i.IsSelected).Select(i => i.Id).ToList();
            if (f.Fix.SelectedItems.Count == 0) { Dialogs.Toast($"Select at least one item for \"{f.Title}\", or untick it.", ToastKind.Warning); return; }
        }
        Session.ProposedFixes = Fixes.Where(f => f.IsSelected).Select(f => f.Fix).ToList();
        foreach (var f in Fixes.Where(f => f.IsSelected && !f.IsManual))
        {
            try
            {
                var preview = await S.Actions.Get(f.Fix.ActionId!).PreviewAsync(S.ActionContext, new ActionRequest { ActionId = f.Fix.ActionId!, Parameters = f.Fix.Parameters, SelectedItems = f.Fix.SelectedItems }, CancellationToken.None);
                f.Fix.PreviewLines = preview.Changes.Select(c => $"{c.Target}: {c.From} → {c.To}").Concat(preview.Warnings.Select(w => "⚠ " + w)).ToList();
                f.Fix.RequiresAdmin |= preview.RequiresAdmin;
            }
            catch (Exception ex) { f.Fix.PreviewLines = new() { "Preview unavailable: " + ex.Message }; }
        }
        Session.Advance(SessionStage.FixesSelected);
        Session.Log($"{Session.ProposedFixes.Count} fix(es) proposed.");
        S.Sessions.Save(Session);
        ApprovalItems.Clear();
        foreach (var f in Fixes.Where(f => f.IsSelected)) { f.Approved = true; ApprovalItems.Add(f); }
        RestorePoint = Session.ProposedFixes.Any(p => p.RequiresAdmin) && S.Platform.Maintenance.IsSystemRestoreEnabled() != false;
        Step = 6;
    }

    // ---- Step 6
    public ObservableCollection<ProposedFixItem> ApprovalItems { get; } = new();
    [ObservableProperty] private string _approvedBy = "";
    [ObservableProperty] private bool _restorePoint;

    [RelayCommand]
    private void RecordApproval()
    {
        if (Session is null) return;
        if (string.IsNullOrWhiteSpace(ApprovedBy)) { Dialogs.Toast("Enter the customer's name or initials to record approval.", ToastKind.Warning); return; }
        Session.Approval = new ApprovalRecord
        {
            ApprovedBy = ApprovedBy.Trim(),
            ApprovedFixIds = ApprovalItems.Where(a => a.Approved).Select(a => a.Fix.Id).ToList(),
            DeclinedFixIds = ApprovalItems.Where(a => !a.Approved).Select(a => a.Fix.Id).ToList(),
            RestorePointRequested = RestorePoint,
        };
        foreach (var a in ApprovalItems) a.Fix.Approved = a.Approved;
        Session.Advance(SessionStage.ApprovalPresented);
        Session.Log($"Customer ({ApprovedBy}) approved {Session.Approval.ApprovedFixIds.Count} and declined {Session.Approval.DeclinedFixIds.Count} change(s).");
        S.Sessions.Save(Session);
        Step = 7;
    }

    // ---- Step 7
    [ObservableProperty] private bool _isApplying;
    [ObservableProperty] private bool _applied;
    public IEnumerable<ProposedFixItem> ApprovedItems => ApprovalItems.Where(a => a.Approved);

    [RelayCommand]
    private async Task ApplyApproved()
    {
        if (Session?.Approval is null || IsApplying) return;
        var items = ApprovalItems.Where(a => a.Approved && !a.IsManual && !a.Fix.Completed).ToList();
        if (!await Dialogs.ConfirmAsync("Apply approved changes?", $"{items.Count} approved change(s) will be applied now. Each is backed up first where possible. Windows may show a UAC prompt for administrator changes.", "Apply now")) return;
        IsApplying = true;
        try
        {
            if (Session.Approval.RestorePointRequested && items.Any(i => i.Fix.RequiresAdmin))
            {
                var rp = await S.Executor.ApplyAsync(new ActionRequest { ActionId = ActionIds.RestorePoint, SessionId = Session.Id, Parameters = { ["description"] = $"softreset session {Session.Id}" } });
                Session.Log($"Restore point: {rp.Status} — {rp.Message}");
            }
            foreach (var item in items)
            {
                item.Status = "Applying…";
                var req = new ActionRequest { ActionId = item.Fix.ActionId!, Parameters = item.Fix.Parameters, SelectedItems = item.Fix.SelectedItems, SessionId = Session.Id };
                var record = await S.Executor.ApplyAsync(req);
                if (item.Fix.ActionId == ActionIds.RefreshRate && record.Status is ChangeStatus.Verified or ChangeStatus.Applied && !await Dialogs.KeepDisplayChangesAsync(15))
                    record = await S.Executor.RollbackAsync(record.Id);
                item.Fix.ChangeRecordId = record.Id;
                item.Fix.Completed = record.Status is ChangeStatus.Verified or ChangeStatus.Applied or ChangeStatus.VerificationFailed;
                item.Fix.Outcome = record.Status switch
                {
                    ChangeStatus.Verified => $"Done — {record.VerificationMessage}",
                    ChangeStatus.Applied => "Applied",
                    ChangeStatus.VerificationFailed => $"Applied, not verified — {record.VerificationMessage}",
                    ChangeStatus.RolledBack => "Reverted",
                    _ => $"{record.Status}: {record.Message}",
                };
                item.Status = item.Fix.Outcome;
                Session.Log($"{item.Title}: {item.Fix.Outcome}");
                S.Sessions.Save(Session);
            }
            foreach (var m in ApprovalItems.Where(a => a.Approved && a.IsManual))
            {
                m.Fix.Completed = m.ManualDone;
                m.Fix.Outcome = m.ManualDone ? (string.IsNullOrWhiteSpace(m.ManualOutcome) ? "Completed" : m.ManualOutcome) : "Not done";
            }
            Session.Advance(SessionStage.ChangesApplied);
            S.Sessions.Save(Session);
            Applied = true;
            Shell.Main.History.MarkStale();
            Dialogs.Toast("Approved changes processed. Re-check the system next.", ToastKind.Success);
        }
        finally { IsApplying = false; }
    }

    [RelayCommand] private void ApplyDone() { Step = 8; }

    // ---- Step 8
    [ObservableProperty] private DiagnosticReport? _followUp;
    [ObservableProperty] private ScanComparison? _comparison;
    public ObservableCollection<MetricComparison> Metrics { get; } = new();

    [RelayCommand]
    private async Task RunRecheck()
    {
        if (Session is null || IsScanning) return;
        IsScanning = true;
        try
        {
            var progress = new Progress<CollectionProgress>(p => { Progress = p.Percent; Stage = p.Stage; });
            FollowUp = await DiagnosticsViewModel.CollectAndAnalyzeAsync(Shell, new CollectionOptions { CpuSampleDuration = TimeSpan.FromSeconds(S.Settings.ScanSampleSeconds) }, progress);
            Session.FollowUp = S.Sessions.SaveScan(Session, FollowUp, "followup");
            Session.Advance(SessionStage.Rechecked);
            Session.Log($"Follow-up scan: health {FollowUp.HealthScore}, {FollowUp.Findings.Count} findings.");
            S.Sessions.Save(Session);
            BuildComparison();
        }
        catch (Exception ex) { await Dialogs.ShowMessageAsync("Scan failed", ex.Message); }
        finally { IsScanning = false; }
    }

    private void BuildComparison()
    {
        if (Baseline is null || FollowUp is null) return;
        Comparison = DiagnosticEngine.Compare(Baseline, FollowUp);
        Metrics.Clear();
        foreach (var m in Comparison.Metrics) Metrics.Add(m);
    }

    [RelayCommand] private void RecheckDone() { Step = 9; }

    // ---- Step 9
    [ObservableProperty] private string? _reportPath;

    [RelayCommand]
    private async Task GenerateReport()
    {
        if (Session is null) return;
        var changes = S.Journal.ForSession(Session.Id);
        var fileOps = S.FileOps.LoadAll().Where(l => l.SessionId == Session.Id).ToList();
        var bytes = ReportBuilder.BuildSessionReport(new ReportInput { Session = Session, Baseline = Baseline, FollowUp = FollowUp, Changes = changes, FileOperations = fileOps, BusinessContact = S.Settings.BusinessContact });
        var name = $"service-report-{Session.Id}.pdf";
        var path = Path.Combine(S.Sessions.SessionFolder(Session), name);
        await File.WriteAllBytesAsync(path, bytes);
        File.Copy(path, Path.Combine(S.Paths.Reports, name), overwrite: true);
        ReportPath = path;
        Session.ReportPath = path;
        Session.Advance(SessionStage.ReportGenerated);
        Session.Log("Report generated.");
        S.Sessions.Save(Session);
        Dialogs.Toast("PDF report saved.", ToastKind.Success);
    }

    [RelayCommand]
    private void OpenReport()
    {
        if (ReportPath is null) return;
        var r = S.Platform.Shell.Launch(new LaunchTarget(LaunchKind.RevealFile, ReportPath));
        if (!r.Success) Dialogs.Toast(r.Message, ToastKind.Warning);
    }

    [RelayCommand] private void ReportDone() { Step = 10; }

    // ---- Step 10
    public IReadOnlyList<ChangeRecord> SessionChanges => Session is null ? Array.Empty<ChangeRecord>() : S.Journal.ForSession(Session.Id);

    [RelayCommand]
    private async Task CloseSession()
    {
        if (Session is null) return;
        if (Session.ReportPath is null && !await Dialogs.ConfirmAsync("Close without a report?", "No PDF report has been generated for this session.", "Close anyway")) return;
        S.Sessions.Close(Session);
        S.SetActiveSession(null);
        OnPropertyChanged(nameof(SessionChanges));
        Dialogs.Toast("Session closed and saved.", ToastKind.Success);
        await OnActivatedAsync();
    }

    [RelayCommand]
    private void NewSession()
    {
        Session = null;
        Step = 1;
        CustomerName = Reference = DeviceLabel = Notes = ApprovedBy = "";
        Baseline = FollowUp = null;
        Comparison = null;
        Fixes.Clear(); ApprovalItems.Clear(); Concerns.Clear(); Metrics.Clear();
        ReportPath = null;
        Applied = false;
    }
}

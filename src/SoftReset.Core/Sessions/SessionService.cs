using System.Text.RegularExpressions;
using SoftReset.Core.Models;
using SoftReset.Core.Safety;
using SoftReset.Core.Storage;

namespace SoftReset.Core.Sessions;

public enum SessionStage
{
    Started = 1,
    ConcernsRecorded = 2,
    DiagnosticsRun = 3,
    BaselineCaptured = 4,
    FixesSelected = 5,
    ApprovalPresented = 6,
    ChangesApplied = 7,
    Rechecked = 8,
    ReportGenerated = 9,
    Closed = 10,
}

public sealed class CustomerConcern
{
    public string Text { get; set; } = "";
    public string Category { get; set; } = "General";
    public DateTimeOffset At { get; set; } = DateTimeOffset.Now;
}

public sealed class ProposedFix
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string? FindingKey { get; set; }
    public string? FindingTitle { get; set; }
    public string? ActionId { get; set; }
    public Dictionary<string, string> Parameters { get; set; } = new();
    public List<string> SelectedItems { get; set; } = new();
    public string ExpectedBenefit { get; set; } = "";
    public string Risk { get; set; } = "";
    public bool RequiresAdmin { get; set; }
    public bool RequiresRestart { get; set; }
    public Reversibility Reversibility { get; set; }
    public List<string> PreviewLines { get; set; } = new();
    /// <summary>Manual fixes are performed by the technician outside the console and recorded here.</summary>
    public bool IsManual => ActionId is null;
    public bool? Approved { get; set; }
    public string? Outcome { get; set; }
    public string? ChangeRecordId { get; set; }
    public bool Completed { get; set; }
}

public sealed class ApprovalRecord
{
    public DateTimeOffset At { get; set; } = DateTimeOffset.Now;
    public string ApprovedBy { get; set; } = "";
    public string Method { get; set; } = "On-screen approval checklist reviewed with the customer";
    public List<string> ApprovedFixIds { get; set; } = new();
    public List<string> DeclinedFixIds { get; set; } = new();
    public bool RestorePointRequested { get; set; }
}

public sealed class SessionEvent
{
    public DateTimeOffset At { get; set; } = DateTimeOffset.Now;
    public string Text { get; set; } = "";
}

public sealed class ScanSummary
{
    public string File { get; set; } = "";
    public DateTimeOffset At { get; set; }
    public int HealthScore { get; set; }
    public int Findings { get; set; }
    public int HighOrCritical { get; set; }
}

public sealed class CustomerSession
{
    public string Id { get; set; } = $"S{DateTime.Now:yyyyMMdd-HHmm}-{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}";
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? ClosedAt { get; set; }
    public string TechnicianName { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string CustomerReference { get; set; } = "";
    public string DeviceLabel { get; set; } = "";
    public bool IncludeDeviceIdentifiers { get; set; } = true;
    public string MachineName { get; set; } = "";
    public List<CustomerConcern> Concerns { get; set; } = new();
    public string Notes { get; set; } = "";
    public SessionStage Stage { get; set; } = SessionStage.Started;
    public List<SessionEvent> Timeline { get; set; } = new();
    public ScanSummary? Baseline { get; set; }
    public ScanSummary? FollowUp { get; set; }
    public List<ProposedFix> ProposedFixes { get; set; } = new();
    public ApprovalRecord? Approval { get; set; }
    public List<string> FileOperationLogIds { get; set; } = new();
    public string? ReportPath { get; set; }

    public bool IsClosed => ClosedAt is not null;

    public void Log(string text) => Timeline.Add(new SessionEvent { Text = text });

    public void Advance(SessionStage stage)
    {
        if (stage > Stage) Stage = stage;
    }
}

public sealed class SessionService
{
    private readonly AppPaths _paths;

    public SessionService(AppPaths paths)
    {
        _paths = paths;
        Directory.CreateDirectory(paths.Sessions);
        Directory.CreateDirectory(paths.Scans);
    }

    private string SessionFile(string id) => Path.Combine(_paths.Sessions, id, "session.json");

    public CustomerSession Start(string technician, string customerName, string reference, string deviceLabel, string machineName)
    {
        var s = new CustomerSession
        {
            TechnicianName = technician.Trim(),
            CustomerName = customerName.Trim(),
            CustomerReference = reference.Trim(),
            DeviceLabel = deviceLabel.Trim(),
            MachineName = machineName,
        };
        s.Log("Session started.");
        Save(s);
        return s;
    }

    public void Save(CustomerSession s) => Json.WriteFile(SessionFile(s.Id), s);

    public CustomerSession? Load(string id)
    {
        try { return Json.ReadFile<CustomerSession>(SessionFile(id)); }
        catch { return null; }
    }

    public IReadOnlyList<CustomerSession> List()
    {
        if (!Directory.Exists(_paths.Sessions)) return Array.Empty<CustomerSession>();
        return Directory.EnumerateDirectories(_paths.Sessions)
            .Select(d => Load(Path.GetFileName(d)))
            .Where(s => s is not null).Cast<CustomerSession>()
            .OrderByDescending(s => s.StartedAt).ToList();
    }

    public string SessionFolder(CustomerSession s)
    {
        var dir = Path.Combine(_paths.Sessions, s.Id);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public ScanSummary SaveScan(CustomerSession s, DiagnosticReport report, string label)
    {
        var file = Path.Combine(SessionFolder(s), $"scan-{label}.json");
        Json.WriteFile(file, report);
        return new ScanSummary
        {
            File = file,
            At = report.CreatedAt,
            HealthScore = report.HealthScore,
            Findings = report.Findings.Count,
            HighOrCritical = report.Findings.Count(f => f.Severity >= Severity.High),
        };
    }

    public DiagnosticReport? LoadScan(ScanSummary? summary)
    {
        if (summary is null || !File.Exists(summary.File)) return null;
        try { return Json.ReadFile<DiagnosticReport>(summary.File); }
        catch { return null; }
    }

    public void Close(CustomerSession s)
    {
        s.ClosedAt = DateTimeOffset.Now;
        s.Advance(SessionStage.Closed);
        s.Log("Session closed.");
        Save(s);
    }

    public void Delete(string id)
    {
        var dir = Path.Combine(_paths.Sessions, id);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }
}

/// <summary>Flags text that looks like it contains credentials so the technician can remove it.</summary>
public static partial class SensitiveTextDetector
{
    [GeneratedRegex(@"\b(pass(word|wd|code)?|pwd|pin|passphrase|login|credentials?)\s*[:=]\s*\S+", RegexOptions.IgnoreCase)]
    private static partial Regex Credential();

    [GeneratedRegex(@"\b(?:\d[ -]*?){13,19}\b")]
    private static partial Regex CardNumber();

    [GeneratedRegex(@"\b\d{3}-\d{2}-\d{4}\b")]
    private static partial Regex Ssn();

    public static string? Check(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (Credential().IsMatch(text)) return "This looks like it contains a password or PIN. Do not record customer credentials in session notes.";
        if (CardNumber().IsMatch(text)) return "This looks like it contains a payment card number. Remove it — session records must not hold payment details.";
        if (Ssn().IsMatch(text)) return "This looks like it contains a government ID number. Remove it.";
        return null;
    }
}

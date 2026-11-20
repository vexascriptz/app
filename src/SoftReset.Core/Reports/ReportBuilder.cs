using SoftReset.Core.Diagnostics;
using SoftReset.Core.Models;
using SoftReset.Core.Parsing;
using SoftReset.Core.Safety;
using SoftReset.Core.Sessions;

namespace SoftReset.Core.Reports;

public sealed class ReportInput
{
    public CustomerSession? Session { get; init; }
    public DiagnosticReport? Baseline { get; init; }
    public DiagnosticReport? FollowUp { get; init; }
    public IReadOnlyList<ChangeRecord> Changes { get; init; } = Array.Empty<ChangeRecord>();
    public IReadOnlyList<Files.FileOperationLog> FileOperations { get; init; } = Array.Empty<Files.FileOperationLog>();
    public string BusinessName { get; init; } = "softreset";
    public string? BusinessContact { get; init; }
}

/// <summary>Builds the customer-facing before/after PDF report.</summary>
public static class ReportBuilder
{
    public static PdfColor SeverityColor(Severity s) => s switch
    {
        Severity.Critical => PdfColor.FromHex("C62828"),
        Severity.High => PdfColor.FromHex("E65100"),
        Severity.Medium => PdfColor.FromHex("B7791F"),
        Severity.Low => PdfColor.FromHex("2F80ED"),
        _ => PdfColor.FromHex("6B7C93"),
    };

    public static byte[] BuildSessionReport(ReportInput input)
    {
        var s = input.Session ?? throw new ArgumentException("A session is required.");
        var doc = new PdfDocument { Title = $"Service report {s.Id}", Author = s.TechnicianName };
        var flow = new PdfFlow(doc, $"Service report · {s.Id}");
        var baseSnap = input.Baseline?.Snapshot;

        flow.Title("PC Service Report", $"{s.StartedAt.LocalDateTime:dddd d MMMM yyyy}  ·  Session {s.Id}");

        var who = new List<(string, string)>
        {
            ("Technician", Or(s.TechnicianName)),
            ("Customer", Or(s.CustomerName, "Not recorded")),
        };
        if (!string.IsNullOrWhiteSpace(s.CustomerReference)) who.Add(("Reference", s.CustomerReference));
        if (baseSnap is not null)
        {
            who.Add(("Device", Or(s.DeviceLabel, $"{baseSnap.Hardware.Manufacturer} {baseSnap.Hardware.Model}".Trim())));
            if (s.IncludeDeviceIdentifiers && !string.IsNullOrEmpty(baseSnap.MachineName)) who.Add(("Computer name", baseSnap.MachineName));
            who.Add(("Windows", baseSnap.Os.FriendlyName));
            who.Add(("Processor", baseSnap.Hardware.CpuName));
            who.Add(("Memory", ToolOutputParsers.FormatBytes(baseSnap.Hardware.TotalMemoryBytes)));
            if (baseSnap.Gpus.Count > 0) who.Add(("Graphics", string.Join(", ", baseSnap.Gpus.Select(g => g.Name))));
        }
        who.Add(("Session time", s.ClosedAt is { } closed ? $"{s.StartedAt.LocalDateTime:t} – {closed.LocalDateTime:t}" : $"Started {s.StartedAt.LocalDateTime:t}"));
        flow.KeyValues(who);

        // ---- Summary cards
        var applied = input.Changes.Where(c => c.Status is ChangeStatus.Verified or ChangeStatus.Applied or ChangeStatus.VerificationFailed).ToList();
        var cards = new List<(string, string, string?)>();
        if (input.Baseline is not null)
            cards.Add(("Health score before", input.Baseline.HealthScore.ToString(), $"{input.Baseline.Findings.Count} findings"));
        if (input.FollowUp is not null)
            cards.Add(("Health score after", input.FollowUp.HealthScore.ToString(), $"{input.FollowUp.Findings.Count} findings"));
        cards.Add(("Changes applied", applied.Count.ToString(), $"{input.Changes.Count(c => c.Status == ChangeStatus.RolledBack)} rolled back"));
        if (input.Baseline is not null && input.FollowUp is not null)
        {
            var cmp = DiagnosticEngine.Compare(input.Baseline, input.FollowUp);
            cards.Add(("Issues resolved", cmp.Resolved.Count.ToString(), $"{cmp.New.Count} new"));
        }
        flow.Space(4);
        flow.StatCards(cards);

        // ---- Concerns
        flow.Heading("What you told us");
        if (s.Concerns.Count == 0) flow.Paragraph("No specific concerns were recorded.", color: PdfFlow.Muted);
        foreach (var c in s.Concerns) flow.Bullet(c.Text);

        // ---- Findings
        if (input.Baseline is not null)
        {
            flow.Heading("What we found");
            flow.Paragraph("The console collected real measurements from this PC (performance counters, event logs, storage health, drivers, and configuration) and analysed them. Each finding below shows the evidence it is based on and how confident the analysis is.", 9.5, color: PdfFlow.Muted);
            var findings = input.Baseline.Findings.Where(f => f.Severity >= Severity.Low).ToList();
            if (findings.Count == 0) flow.Paragraph("No significant problems were detected.");
            foreach (var f in findings.Take(25)) Finding(flow, f, compact: false);
            var info = input.Baseline.Findings.Count - findings.Count;
            if (info > 0) flow.Paragraph($"{info} informational observation(s) are omitted from this summary.", 9, color: PdfFlow.Muted);
        }

        // ---- Changes
        flow.Heading("What we changed");
        if (input.Changes.Count == 0) flow.Paragraph("No configuration changes were made by the console during this session.", color: PdfFlow.Muted);
        else
        {
            flow.Table(new[] { "Change", "Result", "Undo" },
                input.Changes.OrderBy(c => c.CreatedAt).Select(c => new[]
                {
                    c.Title + (c.ChangesApplied.Count > 0 ? "\n" + string.Join("\n", c.ChangesApplied.Take(6).Select(x => "– " + x)) + (c.ChangesApplied.Count > 6 ? $"\n– … and {c.ChangesApplied.Count - 6} more" : "") : ""),
                    StatusText(c) + (c.VerificationMessage is { Length: > 0 } v ? "\n" + v : c.Message is { Length: > 0 } m && c.Status is ChangeStatus.Failed or ChangeStatus.Cancelled ? "\n" + m : ""),
                    c.Reversibility switch
                    {
                        Reversibility.Full => c.Status == ChangeStatus.RolledBack ? "Rolled back" : "Reversible (backup saved)",
                        Reversibility.Partial => "Partly reversible",
                        _ => "Not reversible",
                    },
                }), new[] { 0.56, 0.27, 0.17 });
        }

        var declined = s.ProposedFixes.Where(p => p.Approved == false).ToList();
        if (declined.Count > 0)
        {
            flow.SubHeading("Recommended but not applied");
            foreach (var p in declined) flow.Bullet($"{p.Title} — declined at your request.");
        }
        var manual = s.ProposedFixes.Where(p => p.IsManual && p.Approved == true).ToList();
        if (manual.Count > 0)
        {
            flow.SubHeading("Manual work performed by the technician");
            foreach (var p in manual) flow.Bullet($"{p.Title}{(p.Outcome is { Length: > 0 } o ? $" — {o}" : "")}");
        }

        if (input.FileOperations.Count > 0)
        {
            flow.SubHeading("File organisation");
            foreach (var op in input.FileOperations)
                flow.Bullet($"{op.Description}: {op.Done} file(s) moved{(op.Failed > 0 ? $", {op.Failed} skipped" : "")}. Status: {op.Status}. No files were deleted; moves can be undone from the console log.");
        }

        // ---- Before / after
        if (input.Baseline is not null && input.FollowUp is not null)
        {
            var cmp = DiagnosticEngine.Compare(input.Baseline, input.FollowUp);
            flow.Heading("Before and after");
            flow.Table(new[] { "Measurement", "Before", "After", "Change" },
                cmp.Metrics.Select(m => new[] { m.Name, Fmt(m.Before, m.Unit), Fmt(m.After, m.Unit), m.Assessment }),
                new[] { 0.46, 0.18, 0.18, 0.18 });
            flow.Paragraph(cmp.Caveat, 8.5, color: PdfFlow.Muted);
            if (cmp.Resolved.Count > 0)
            {
                flow.SubHeading("Resolved");
                foreach (var f in cmp.Resolved) flow.Bullet(f.Title);
            }
            var remaining = cmp.Remaining.Concat(cmp.New).Where(f => f.Severity >= Severity.Low).ToList();
            if (remaining.Count > 0)
            {
                flow.Heading("Still worth attention");
                foreach (var f in remaining.Take(15)) Finding(flow, f, compact: true);
            }
        }

        if (!string.IsNullOrWhiteSpace(s.Notes))
        {
            flow.Heading("Technician notes");
            flow.Paragraph(s.Notes);
        }

        flow.Heading("About this report");
        flow.Bullet("All data in this report was collected on this PC and is stored only on the technician's console media. No account, cloud service, or internet connection was used to produce it.");
        flow.Bullet("No passwords or personal files were collected. File names appear only where you approved file organisation.");
        flow.Bullet("Reversible changes were backed up before they were applied and can be undone by the technician. Changes marked 'Not reversible' (such as deleting temporary files) cannot be undone.");
        flow.Bullet("Measurements are point-in-time samples and can vary with background activity.");
        if (s.Approval is { } a)
            flow.Bullet($"Changes were approved by {Or(a.ApprovedBy, "the customer")} on {a.At.LocalDateTime:g} ({a.Method}).");
        if (!string.IsNullOrWhiteSpace(input.BusinessContact))
            flow.Paragraph(input.BusinessContact!, 9.5, PdfFont.Bold, PdfFlow.Navy);

        return doc.ToBytes();
    }

    public static byte[] BuildDiagnosticReport(DiagnosticReport report)
    {
        var doc = new PdfDocument { Title = "Diagnostic report" };
        var flow = new PdfFlow(doc, $"Diagnostic report · {report.CreatedAt.LocalDateTime:g}");
        var snap = report.Snapshot;
        flow.Title("Diagnostic Report", $"{report.CreatedAt.LocalDateTime:f}");
        flow.KeyValues(new[]
        {
            ("Windows", snap.Os.FriendlyName),
            ("Device", $"{snap.Hardware.Manufacturer} {snap.Hardware.Model}".Trim()),
            ("Processor", snap.Hardware.CpuName),
            ("Memory", ToolOutputParsers.FormatBytes(snap.Hardware.TotalMemoryBytes)),
            ("Collected as administrator", snap.CollectedElevated ? "Yes" : "No — some data sources were not available"),
        });
        flow.StatCards(new List<(string, string, string?)>
        {
            ("Health score", report.HealthScore.ToString(), "Indicative"),
            ("Critical / high", report.Findings.Count(f => f.Severity >= Severity.High).ToString(), null),
            ("Medium", report.Count(Severity.Medium).ToString(), null),
            ("Low / info", (report.Count(Severity.Low) + report.Count(Severity.Info)).ToString(), null),
        });
        flow.Heading("Findings");
        if (report.Findings.Count == 0) flow.Paragraph("No problems detected.");
        foreach (var f in report.Findings) Finding(flow, f, compact: false);
        if (snap.CollectionIssues.Count > 0)
        {
            flow.Heading("Data that could not be collected");
            foreach (var i in snap.CollectionIssues) flow.Bullet($"{i.Area}: {i.Message}{(i.RequiresAdmin ? " (requires administrator)" : "")}");
        }
        return doc.ToBytes();
    }

    private static void Finding(PdfFlow flow, Finding f, bool compact)
    {
        flow.Ensure(compact ? 34 : 60);
        var y = flow.Y;
        flow.Badge(f.Severity.ToString().ToUpperInvariant(), SeverityColor(f.Severity), PdfFlow.White, flow.Margin, y);
        var badgeW = PdfDocument.Measure(f.Severity.ToString().ToUpperInvariant(), PdfFont.Bold, 8) + 18;
        var titleLines = PdfDocument.Wrap(f.Title, PdfFont.Bold, 10.5, flow.ContentWidth - badgeW);
        var ty = y;
        foreach (var l in titleLines)
        {
            flow.Document.Text(flow.Margin + badgeW, ty + 10.5, l, PdfFont.Bold, 10.5, PdfFlow.Navy);
            ty += 14;
        }
        flow.Space(ty - y + 2);
        if (!compact)
        {
            flow.Paragraph(f.Summary, 9.5, indent: 6);
            foreach (var e in f.Evidence.Take(4)) flow.Paragraph($"Evidence — {e.Label}: {e.Value}", 8.5, color: PdfFlow.Muted, indent: 6);
            var rec = f.Recommendations.FirstOrDefault();
            if (rec is not null) flow.Paragraph($"Recommendation: {rec.Text}", 9, PdfFont.Bold, PdfFlow.Body, indent: 6);
            flow.Paragraph($"Confidence: {f.Confidence}. {f.ConfidenceExplanation}", 8.5, color: PdfFlow.Muted, indent: 6);
        }
        else if (f.Recommendations.FirstOrDefault() is { } r)
        {
            flow.Paragraph(r.Text, 9, indent: 6);
        }
        flow.Space(4);
    }

    private static string StatusText(ChangeRecord c) => c.Status switch
    {
        ChangeStatus.Verified => "Applied and verified",
        ChangeStatus.Applied => "Applied",
        ChangeStatus.VerificationFailed => "Applied — verification did not confirm",
        ChangeStatus.Failed => "Failed",
        ChangeStatus.Cancelled => "Not applied",
        ChangeStatus.RolledBack => "Rolled back",
        ChangeStatus.RollbackFailed => "Rollback failed",
        ChangeStatus.Interrupted => "Interrupted",
        _ => c.Status.ToString(),
    };

    private static string Fmt(double? v, string unit) => v is null ? "—" : $"{v:0.##}{(string.IsNullOrEmpty(unit) ? "" : unit == "%" ? "%" : " " + unit)}";
    private static string Or(string? s, string fallback = "—") => string.IsNullOrWhiteSpace(s) ? fallback : s;
}

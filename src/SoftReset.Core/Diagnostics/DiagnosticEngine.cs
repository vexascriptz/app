using SoftReset.Core.Knowledge;
using SoftReset.Core.Models;

namespace SoftReset.Core.Diagnostics;

public sealed class DiagnosticContext
{
    public DateTimeOffset Now { get; init; } = DateTimeOffset.Now;
    public KnowledgeBase Knowledge { get; init; } = KnowledgeBase.Default;
}

public interface IDiagnosticRule
{
    string Id { get; }
    string Name { get; }
    FindingCategory Category { get; }
    IEnumerable<Finding> Evaluate(SystemSnapshot snapshot, DiagnosticContext context);
}

/// <summary>
/// Runs every rule against a snapshot. Rules are pure functions of the snapshot, so
/// diagnostics are read-only by construction: nothing here can change the system.
/// </summary>
public sealed class DiagnosticEngine
{
    private readonly IReadOnlyList<IDiagnosticRule> _rules;

    public DiagnosticEngine(IEnumerable<IDiagnosticRule>? rules = null)
    {
        _rules = (rules ?? RuleCatalog.All()).ToList();
    }

    public IReadOnlyList<IDiagnosticRule> Rules => _rules;

    public DiagnosticReport Analyze(SystemSnapshot snapshot, DiagnosticContext? context = null)
    {
        context ??= new DiagnosticContext();
        var report = new DiagnosticReport { Snapshot = snapshot, CreatedAt = context.Now };
        foreach (var rule in _rules)
        {
            try
            {
                foreach (var f in rule.Evaluate(snapshot, context))
                {
                    if (string.IsNullOrEmpty(f.RuleId)) f.RuleId = rule.Id;
                    if (f.Evidence.Count == 0)
                        throw new InvalidOperationException($"Rule {rule.Id} produced a finding without evidence.");
                    report.Findings.Add(f);
                }
            }
            catch (Exception ex)
            {
                report.RuleErrors.Add(new RuleError { RuleId = rule.Id, Message = ex.Message });
            }
            report.RulesEvaluated++;
        }

        report.Findings = report.Findings
            .OrderByDescending(f => f.Severity)
            .ThenByDescending(f => f.Confidence)
            .ThenBy(f => f.Category)
            .ToList();
        report.HealthScore = ComputeHealthScore(report.Findings);
        return report;
    }

    /// <summary>
    /// An indicative 0-100 score. Each finding removes a share of the remaining score (critical 30%,
    /// high 15%, medium 6%, low 1%), so many small issues cannot outweigh one serious problem and the
    /// score never saturates at zero. Low-confidence findings count for less.
    /// </summary>
    public static int ComputeHealthScore(IEnumerable<Finding> findings)
    {
        double score = 100;
        foreach (var f in findings)
        {
            double share = f.Severity switch
            {
                Severity.Critical => 0.30,
                Severity.High => 0.15,
                Severity.Medium => 0.06,
                Severity.Low => 0.01,
                _ => 0,
            };
            share *= f.Confidence switch { Confidence.High => 1.0, Confidence.Medium => 0.75, _ => 0.4 };
            score *= 1 - share;
        }
        return (int)Math.Clamp(Math.Round(score), 0, 100);
    }

    /// <summary>Compares two scans to measure the effect of applied changes.</summary>
    public static ScanComparison Compare(DiagnosticReport before, DiagnosticReport after)
    {
        var cmp = new ScanComparison();
        var b = before.Snapshot;
        var a = after.Snapshot;

        void M(string name, string unit, double? x, double? y, bool lowerBetter, string note = "") =>
            cmp.Metrics.Add(new MetricComparison { Name = name, Unit = unit, Before = x, After = y, LowerIsBetter = lowerBetter, Note = note });

        M("Health score", "points", before.HealthScore, after.HealthScore, false, "Indicative score derived from findings.");
        M("Findings (all severities)", "", before.Findings.Count, after.Findings.Count, true);
        M("High or critical findings", "", before.Findings.Count(f => f.Severity >= Severity.High), after.Findings.Count(f => f.Severity >= Severity.High), true);
        M("Average CPU load during sample", "%", R(b.Performance.CpuAveragePercent), R(a.Performance.CpuAveragePercent), true, "Sampled over a few seconds; background tasks cause variance.");
        M("Memory in use", "%", b.Performance.MemoryUsedPercent, a.Performance.MemoryUsedPercent, true);
        M("System drive free space", "GB", Gb(b.SystemDrive?.FreeBytes), Gb(a.SystemDrive?.FreeBytes), false);
        M("Enabled startup items", "", b.StartupEntries.Count(s => s.Enabled), a.StartupEntries.Count(s => s.Enabled), true);
        M("Running processes", "", b.ProcessCount, a.ProcessCount, true);
        M("Reclaimable temporary data", "GB", Gb(Reclaimable(b)), Gb(Reclaimable(a)), true);
        var bDisp = b.Displays.Sum(d => d.RefreshHz);
        var aDisp = a.Displays.Sum(d => d.RefreshHz);
        if (b.Displays.Count > 0 && a.Displays.Count > 0)
            M("Display refresh rate (primary)", "Hz", b.Displays.FirstOrDefault(d => d.IsPrimary)?.RefreshHz ?? bDisp, a.Displays.FirstOrDefault(d => d.IsPrimary)?.RefreshHz ?? aDisp, false);

        var afterKeys = after.Findings.Select(f => f.EffectiveKey).ToHashSet();
        var beforeKeys = before.Findings.Select(f => f.EffectiveKey).ToHashSet();
        cmp.Resolved = before.Findings.Where(f => !afterKeys.Contains(f.EffectiveKey)).ToList();
        cmp.Remaining = after.Findings.Where(f => beforeKeys.Contains(f.EffectiveKey)).ToList();
        cmp.New = after.Findings.Where(f => !beforeKeys.Contains(f.EffectiveKey)).ToList();
        return cmp;

        static double? Gb(long? bytes) => bytes is null ? null : Math.Round(bytes.Value / 1073741824.0, 2);
        static double R(double v) => Math.Round(v, 1);
    }

    public static long? Reclaimable(SystemSnapshot s)
    {
        var c = s.Cleanup;
        long? total = null;
        foreach (var v in new[] { c.UserTempBytes, c.WindowsTempBytes, c.RecycleBinBytes, c.WindowsUpdateCacheBytes })
            if (v is not null) total = (total ?? 0) + v.Value;
        return total;
    }
}

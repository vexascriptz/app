using SoftReset.Core.Diagnostics;
using SoftReset.Core.Models;

namespace SoftReset.Core.Tests;

public class DiagnosticEngineTests
{
    [Fact]
    public async Task SimulatedPc_ProducesExpectedFindings_WithEvidenceAndNoRuleErrors()
    {
        using var rig = new TestRig();
        var snap = await rig.ScanAsync();
        var report = new DiagnosticEngine().Analyze(snap);

        Assert.Empty(report.RuleErrors);
        Assert.True(report.RulesEvaluated >= 30);
        var ids = report.Findings.Select(f => f.RuleId).ToHashSet();
        Assert.Contains("gaming.refresh-rate", ids);
        Assert.Contains("power.plan", ids);
        Assert.Contains("storage.low-space", ids);
        Assert.Contains("stability.unexpected-shutdowns", ids);
        Assert.Contains("stability.gpu-driver-resets", ids);
        Assert.Contains("stability.app-crashes", ids);
        Assert.Contains("startup.many-items", ids);
        Assert.Contains("startup.broken-entries", ids);
        Assert.Contains("drivers.device-problems", ids);
        Assert.Contains("drivers.gpu", ids);
        Assert.Contains("updates.failures", ids);
        Assert.Contains("startup.fast-startup-uptime", ids);
        Assert.Contains("processes.investigate", ids);

        foreach (var f in report.Findings)
        {
            Assert.NotEmpty(f.Evidence);
            Assert.False(string.IsNullOrWhiteSpace(f.Title));
            Assert.False(string.IsNullOrWhiteSpace(f.Summary));
            Assert.False(string.IsNullOrWhiteSpace(f.ConfidenceExplanation), $"{f.RuleId} lacks a confidence explanation");
            Assert.NotEmpty(f.Recommendations);
            Assert.All(f.Evidence, e => Assert.False(string.IsNullOrWhiteSpace(e.Source)));
        }
        Assert.InRange(report.HealthScore, 0, 100);
        // Findings are sorted most severe first.
        Assert.Equal(report.Findings.OrderByDescending(f => f.Severity).Select(f => f.Severity), report.Findings.Select(f => f.Severity));
    }

    [Fact]
    public async Task RecommendedActions_ReferenceRegisteredActions()
    {
        using var rig = new TestRig();
        var report = new DiagnosticEngine().Analyze(await rig.ScanAsync());
        foreach (var r in report.Findings.SelectMany(f => f.Recommendations).Where(r => r.ActionId is not null))
            Assert.True(rig.Registry.TryGet(r.ActionId!, out _), $"Unknown action {r.ActionId}");
    }

    [Fact]
    public void SuspiciousProcessFinding_NeverClaimsMalware()
    {
        var s = new SystemSnapshot();
        s.ProcessIndicators.Add(new SuspiciousProcessIndicator { Pid = 1, Name = "svchost.exe", Path = @"C:\Users\x\AppData\Roaming\svchost.exe", Signature = SignatureState.Unsigned, Reasons = { "a", "b" } });
        var f = Assert.Single(new DiagnosticEngine().Analyze(s).Findings);
        Assert.Equal(Confidence.Low, f.Confidence);
        Assert.Contains("NOT a malware verdict", f.Summary);
        Assert.DoesNotContain("is malware", f.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HealthScore_WeightsConfidence()
    {
        var high = new Finding { Severity = Severity.High, Confidence = Confidence.High };
        var low = new Finding { Severity = Severity.High, Confidence = Confidence.Low };
        Assert.True(DiagnosticEngine.ComputeHealthScore(new[] { high }) < DiagnosticEngine.ComputeHealthScore(new[] { low }));
        Assert.Equal(100, DiagnosticEngine.ComputeHealthScore(Array.Empty<Finding>()));
        Assert.Equal(0, DiagnosticEngine.ComputeHealthScore(Enumerable.Repeat(new Finding { Severity = Severity.Critical, Confidence = Confidence.High }, 10)));
    }

    [Fact]
    public void RuleThatThrows_IsReportedNotFatal()
    {
        var engine = new DiagnosticEngine(new IDiagnosticRule[] { new ThrowingRule(), new SoftReset.Core.Diagnostics.Rules.LowInstalledMemoryRule() });
        var s = new SystemSnapshot { Hardware = { TotalMemoryBytes = 4L * 1024 * 1024 * 1024 } };
        var r = engine.Analyze(s);
        Assert.Single(r.RuleErrors);
        Assert.Single(r.Findings);
    }

    [Fact]
    public void FindingWithoutEvidence_IsRejected()
    {
        var engine = new DiagnosticEngine(new IDiagnosticRule[] { new NoEvidenceRule() });
        var r = engine.Analyze(new SystemSnapshot());
        Assert.Empty(r.Findings);
        Assert.Contains("without evidence", r.RuleErrors.Single().Message);
    }

    [Fact]
    public void Compare_ClassifiesResolvedRemainingNew()
    {
        var before = new DiagnosticReport { Findings = { new Finding { Key = "a", Severity = Severity.High }, new Finding { Key = "b" } }, HealthScore = 70 };
        var after = new DiagnosticReport { Findings = { new Finding { Key = "b" }, new Finding { Key = "c" } }, HealthScore = 90 };
        var c = DiagnosticEngine.Compare(before, after);
        Assert.Equal("a", c.Resolved.Single().Key);
        Assert.Equal("b", c.Remaining.Single().Key);
        Assert.Equal("c", c.New.Single().Key);
        Assert.Equal("Improved", c.Metrics.First(m => m.Name == "Health score").Assessment);
    }

    private sealed class ThrowingRule : Rule
    {
        public override string Id => "test.throw";
        public override string Name => "t";
        public override FindingCategory Category => FindingCategory.Performance;
        public override IEnumerable<Finding> Evaluate(SystemSnapshot s, DiagnosticContext ctx) => throw new InvalidOperationException("boom");
    }

    private sealed class NoEvidenceRule : Rule
    {
        public override string Id => "test.noevidence";
        public override string Name => "t";
        public override FindingCategory Category => FindingCategory.Performance;
        public override IEnumerable<Finding> Evaluate(SystemSnapshot s, DiagnosticContext ctx) { yield return New("x", Severity.Low, Confidence.Low, "y"); }
    }
}

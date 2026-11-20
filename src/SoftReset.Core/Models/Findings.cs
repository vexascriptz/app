namespace SoftReset.Core.Models;

public enum Severity { Info = 0, Low = 1, Medium = 2, High = 3, Critical = 4 }

public enum Confidence { Low = 0, Medium = 1, High = 2 }

public enum FindingCategory
{
    Performance,
    Memory,
    Storage,
    Startup,
    Stability,
    Updates,
    Drivers,
    DiskHealth,
    Network,
    Gaming,
    Thermal,
    Power,
    Security,
    Processes,
    Display,
}

/// <summary>A single diagnostic conclusion, always backed by evidence.</summary>
public sealed class Finding
{
    public string RuleId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public FindingCategory Category { get; set; }
    public Severity Severity { get; set; }
    public Confidence Confidence { get; set; }
    public string ConfidenceExplanation { get; set; } = "";
    public List<Evidence> Evidence { get; set; } = new();
    public List<string> LikelyCauses { get; set; } = new();
    public List<Recommendation> Recommendations { get; set; } = new();
    public List<string> Uncertainties { get; set; } = new();

    /// <summary>Stable key used to match the same finding across before/after scans.</summary>
    public string Key { get; set; } = "";

    public string EffectiveKey => string.IsNullOrEmpty(Key) ? RuleId : Key;
}

public sealed class Evidence
{
    public Evidence() { }
    public Evidence(string label, string value, string source)
    {
        Label = label;
        Value = value;
        Source = source;
    }

    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
    public string Source { get; set; } = "";
}

public sealed class Recommendation
{
    public string Text { get; set; } = "";
    /// <summary>Optimization action that implements this recommendation, if one exists.</summary>
    public string? ActionId { get; set; }
    public Dictionary<string, string>? ActionParameters { get; set; }
    /// <summary>A shortcut (ms-settings URI, tool) to perform it manually.</summary>
    public string? ShortcutTarget { get; set; }
    public string? ShortcutLabel { get; set; }
    public bool IsManual => ActionId is null;
}

public sealed class RuleError
{
    public string RuleId { get; set; } = "";
    public string Message { get; set; } = "";
}

public sealed class DiagnosticReport
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public SystemSnapshot Snapshot { get; set; } = new();
    public List<Finding> Findings { get; set; } = new();
    public List<RuleError> RuleErrors { get; set; } = new();
    public int RulesEvaluated { get; set; }
    public int HealthScore { get; set; }

    public int Count(Severity s) => Findings.Count(f => f.Severity == s);
}

public sealed class MetricComparison
{
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
    public double? Before { get; set; }
    public double? After { get; set; }
    public bool LowerIsBetter { get; set; }
    public string Note { get; set; } = "";

    public double? Delta => Before is { } b && After is { } a ? Math.Round(a - b, 2) : null;

    public string Assessment
    {
        get
        {
            if (Delta is not { } d) return "Not measured";
            if (Math.Abs(d) < 0.0001) return "Unchanged";
            var improved = LowerIsBetter ? d < 0 : d > 0;
            return improved ? "Improved" : "Worse";
        }
    }
}

public sealed class ScanComparison
{
    public List<MetricComparison> Metrics { get; set; } = new();
    public List<Finding> Resolved { get; set; } = new();
    public List<Finding> Remaining { get; set; } = new();
    public List<Finding> New { get; set; } = new();
    public string Caveat { get; set; } =
        "Measurements are point-in-time samples. Background activity (updates, indexing, antivirus scans) can change between scans, so small differences may not be caused by the changes applied.";
}

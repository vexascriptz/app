using SoftReset.Core.Platform;

namespace SoftReset.Core.Knowledge;

// ================================================================ Settings

public sealed class SettingDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string? Subcategory { get; set; }
    public string Description { get; set; } = "";
    public SettingReadSpec Read { get; set; } = new();
    public List<string> Values { get; set; } = new();
    public List<UseCaseRecommendation> Recommendations { get; set; } = new();
    public string Performance { get; set; } = "";
    public string Risks { get; set; } = "";
    public bool AdminRequired { get; set; }
    /// <summary>none | app | explorer | signout | restart</summary>
    public string Restart { get; set; } = "none";
    public string Rollback { get; set; } = "";
    public List<SettingControl> Controls { get; set; } = new();
    public int MinBuild { get; set; }
    public int? MaxBuild { get; set; }
    /// <summary>When set, the setting only exists on these editions (e.g. Professional, Enterprise).</summary>
    public List<string>? Editions { get; set; }
    public List<string> Tags { get; set; } = new();
    public string? Reference { get; set; }

    public string RestartText => Restart switch
    {
        "none" => "No — takes effect immediately",
        "app" => "Restart the affected app",
        "explorer" => "Restart File Explorer (or sign out)",
        "signout" => "Sign out and back in",
        "restart" => "Restart Windows",
        _ => Restart,
    };
}

public sealed class SettingReadSpec
{
    /// <summary>registry | probe | service | none</summary>
    public string Type { get; set; } = "none";
    public string? Hive { get; set; }
    public string? Path { get; set; }
    public string? Value { get; set; }
    public string? View { get; set; }
    public Dictionary<string, string>? Map { get; set; }
    public string? Missing { get; set; }
    public string? Probe { get; set; }
    public string? Service { get; set; }
    public string? Reason { get; set; }
    /// <summary>Additional registry locations that override the primary one (e.g. Group Policy).</summary>
    public List<SettingReadSpec>? PolicyOverrides { get; set; }
}

public sealed class UseCaseRecommendation
{
    public string UseCase { get; set; } = "";
    public string Value { get; set; } = "";
    public string Why { get; set; } = "";
}

public sealed class SettingControl
{
    /// <summary>open | registry-set | registry-delete | action</summary>
    public string Type { get; set; } = "open";
    public string Label { get; set; } = "";
    public LaunchKind Launch { get; set; } = LaunchKind.Uri;
    public string? Target { get; set; }
    public string? Arguments { get; set; }
    public string? Kind { get; set; }
    public string? Data { get; set; }
    public string? ActionId { get; set; }
    public Dictionary<string, string>? Parameters { get; set; }
    /// <summary>For registry-set: the state this control produces (shown in previews).</summary>
    public string? ResultLabel { get; set; }

    public LaunchTarget? ToLaunchTarget() =>
        Type == "open" && Target is not null ? new LaunchTarget(Launch, Target, Arguments) : null;
}

// ================================================================ Processes

public sealed class ProcessKnowledge
{
    public string Name { get; set; } = "";
    public List<string> Aliases { get; set; } = new();
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public string Publisher { get; set; } = "";
    /// <summary>windows-core | windows-service | windows-shell | security | driver | vendor-utility | app | game-launcher | anti-cheat | updater | browser | communication | cloud-sync | creative | peripheral</summary>
    public string Category { get; set; } = "";
    public List<string> ExpectedPaths { get; set; } = new();
    public bool Critical { get; set; }
    public bool WindowsComponent { get; set; }
    public string Functionality { get; set; } = "";
    public List<string> Troubleshooting { get; set; } = new();
    public string? HighCpu { get; set; }
    public string? HighMemory { get; set; }
    public string? Startup { get; set; }
    public bool SafeToEnd { get; set; }
}

// ================================================================ Applications

public sealed class AppGuide
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> Match { get; set; } = new();
    public List<string> MatchPackages { get; set; } = new();
    public string Category { get; set; } = "";
    public string Purpose { get; set; } = "";
    public List<string> RelevantSettings { get; set; } = new();
    public string StartupBehavior { get; set; } = "";
    public List<string> StartupEntryNames { get; set; } = new();
    public List<string> ProcessNames { get; set; } = new();
    public List<CommonProblem> CommonProblems { get; set; } = new();
    public List<string> Repair { get; set; } = new();
    public List<string> Reset { get; set; } = new();
    public List<string> SafeUninstall { get; set; } = new();
    public List<GuideLink> Links { get; set; } = new();
    public List<CacheLocation> Caches { get; set; } = new();
    public bool Essential { get; set; }
    public string? EssentialReason { get; set; }
}

public sealed class CommonProblem
{
    public string Problem { get; set; } = "";
    public List<string> Fixes { get; set; } = new();
}

public sealed class GuideLink
{
    public string Label { get; set; } = "";
    public string Url { get; set; } = "";
}

public sealed class CacheLocation
{
    public string Path { get; set; } = "";
    public string Description { get; set; } = "";
}

// ================================================================ GPU

public sealed class GpuSettingKnowledge
{
    public string Id { get; set; } = "";
    public string Vendor { get; set; } = "";
    public string Category { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Effects { get; set; } = "";
    public string Tradeoffs { get; set; } = "";
    public List<UseCaseRecommendation> Recommendations { get; set; } = new();
    public string WhereToChange { get; set; } = "";
    /// <summary>Hex NVAPI DRS setting id from NvApiDriverSettings.h, when readable.</summary>
    public string? DrsSettingId { get; set; }
    public Dictionary<string, string>? ValueNames { get; set; }
    public string? DefaultValue { get; set; }
    public string? ValueFormat { get; set; }
    public string? Notes { get; set; }
    public string? OpenTarget { get; set; }
    public string? Readability { get; set; }

    public uint? DrsId => DrsSettingId is { Length: > 0 } s
        ? Convert.ToUInt32(s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? s[2..] : s, 16)
        : null;

    public string DescribeValue(uint? value)
    {
        if (value is null) return "Not set (driver default)";
        if (ValueNames is not null)
        {
            foreach (var (k, label) in ValueNames)
            {
                var key = k.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Convert.ToUInt32(k[2..], 16) : uint.Parse(k);
                if (key == value) return label;
            }
        }
        return ValueFormat switch
        {
            "fps" => value == 0 ? "Off" : $"{value} FPS",
            "frames" => value == 0 ? "Application-controlled" : $"{value} frame(s)",
            "mb" => value == 0xFFFFFFFF ? "Unlimited" : $"{value} MB",
            _ => $"0x{value:X8}",
        };
    }
}

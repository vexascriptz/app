using SoftReset.Core.Models;
using SoftReset.Core.Parsing;

namespace SoftReset.Core.Diagnostics;

public abstract class Rule : IDiagnosticRule
{
    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract FindingCategory Category { get; }
    public abstract IEnumerable<Finding> Evaluate(SystemSnapshot s, DiagnosticContext ctx);

    protected Finding New(string title, Severity severity, Confidence confidence, string summary, string? key = null) => new()
    {
        RuleId = Id,
        Category = Category,
        Title = title,
        Severity = severity,
        Confidence = confidence,
        Summary = summary,
        Key = key ?? Id,
    };

    protected static string Bytes(long? b) => ToolOutputParsers.FormatBytes(b);
    protected static string Pct(double v) => $"{v:0.#}%";
    protected static string When(DateTimeOffset t) => t.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}

public static class FindingBuilder
{
    public static Finding Ev(this Finding f, string label, string value, string source)
    {
        f.Evidence.Add(new Evidence(label, value, source));
        return f;
    }

    public static Finding Cause(this Finding f, params string[] causes)
    {
        f.LikelyCauses.AddRange(causes);
        return f;
    }

    public static Finding Rec(this Finding f, string text, string? actionId = null, Dictionary<string, string>? parameters = null)
    {
        f.Recommendations.Add(new Recommendation { Text = text, ActionId = actionId, ActionParameters = parameters });
        return f;
    }

    public static Finding Shortcut(this Finding f, string text, string target, string label)
    {
        f.Recommendations.Add(new Recommendation { Text = text, ShortcutTarget = target, ShortcutLabel = label });
        return f;
    }

    public static Finding Unsure(this Finding f, params string[] notes)
    {
        f.Uncertainties.AddRange(notes);
        return f;
    }

    public static Finding Why(this Finding f, string confidenceExplanation)
    {
        f.ConfidenceExplanation = confidenceExplanation;
        return f;
    }
}

/// <summary>Identifiers of optimization actions that findings can recommend.</summary>
public static class ActionIds
{
    public const string StartupUserItems = "startup.user-items";
    public const string StartupMachineItems = "startup.machine-items";
    public const string StartupLogonTasks = "startup.logon-tasks";
    public const string UserTemp = "storage.user-temp";
    public const string WindowsTemp = "storage.windows-temp";
    public const string RecycleBin = "storage.recycle-bin";
    public const string UpdateCache = "storage.update-cache";
    public const string ComponentCleanup = "storage.component-cleanup";
    public const string Hibernation = "power.hibernation";
    public const string EndProcesses = "background.end-processes";
    public const string PowerPlan = "power.plan";
    public const string UsbSelectiveSuspend = "power.usb-selective-suspend";
    public const string FastStartup = "power.fast-startup";
    public const string ProcessorState = "power.processor-max-state";
    public const string GameMode = "gaming.game-mode";
    public const string BackgroundRecording = "gaming.background-recording";
    public const string Hags = "gaming.hags";
    public const string GpuPreference = "gaming.gpu-preference";
    public const string WindowedOptimizations = "gaming.windowed-optimizations";
    public const string RefreshRate = "display.refresh-rate";
    public const string VisualEffects = "display.visual-effects";
    public const string RestorePoint = "maintenance.restore-point";
    public const string Sfc = "maintenance.sfc";
    public const string DismRestoreHealth = "maintenance.dism-restorehealth";
    public const string ChkdskScan = "maintenance.chkdsk-scan";
    public const string ResetWindowsUpdate = "maintenance.reset-windows-update";
    public const string FlushDns = "network.flush-dns";
    public const string DisableProxy = "network.disable-proxy";
    public const string ResetNetworkStack = "network.reset-stack";
    public const string RenewDhcp = "network.renew-dhcp";
    public const string ClearAppCache = "apps.clear-cache";
    public const string WingetUpgrade = "apps.winget-upgrade";
    public const string FileExtensions = "desktop.file-extensions";
    public const string HiddenFiles = "desktop.hidden-files";
    public const string TaskbarAlignment = "desktop.taskbar-alignment";
    public const string DarkMode = "desktop.dark-mode";
    public const string Transparency = "desktop.transparency";
    public const string ExplorerThisPc = "desktop.explorer-this-pc";
    public const string RestartExplorer = "desktop.restart-explorer";
    public const string SettingRegistrySet = "settings.registry-set";
    public const string ServiceStartMode = "services.start-mode";
    public const string TaskEnabled = "tasks.enabled";
}

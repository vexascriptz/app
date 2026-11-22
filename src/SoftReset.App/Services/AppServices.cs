using SoftReset.Core.Diagnostics;
using SoftReset.Core.Files;
using SoftReset.Core.Knowledge;
using SoftReset.Core.Models;
using SoftReset.Core.Optimization;
using SoftReset.Core.Platform;
using SoftReset.Core.Safety;
using SoftReset.Core.Storage;

namespace SoftReset.App.Services;

public sealed class AppSettings
{
    public int ScanSampleSeconds { get; set; } = 5;
    public bool IncludeConnectivityTests { get; set; }
    /// <summary>Show the Windows admin prompt as soon as the app opens.</summary>
    public bool AskForAdminOnStart { get; set; } = true;
}

/// <summary>Composition root: every service the UI uses, created once at start-up.</summary>
public sealed class AppServices
{
    public AppServices(ISystemPlatform platform, AppPaths paths, bool demoMode)
    {
        Platform = platform;
        Paths = paths;
        DemoMode = demoMode;
        paths.EnsureCreated();
        Journal = new ChangeJournal(paths.Journal);
        Knowledge = KnowledgeBase.Default;
        ActionContext = new ActionContext { Platform = platform, Journal = Journal, Paths = paths, Knowledge = Knowledge, Log = Log };
        Actions = ActionRegistry.CreateDefault();
        Executor = new ActionExecutor(Actions, ActionContext);
        Engine = new DiagnosticEngine();
        FileOps = new FileOperationExecutor(paths.FileOperations);
        Settings = Json.ReadFile<AppSettings>(paths.SettingsFile) ?? new AppSettings();
        InterruptedChanges = Journal.RecoverInterrupted();
        Edition = TryReadEdition();
    }

    public ISystemPlatform Platform { get; }
    public AppPaths Paths { get; }
    public bool DemoMode { get; }
    public ChangeJournal Journal { get; }
    public KnowledgeBase Knowledge { get; }
    public ActionContext ActionContext { get; }
    public ActionRegistry Actions { get; }
    public ActionExecutor Executor { get; }
    public DiagnosticEngine Engine { get; }
    public FileOperationExecutor FileOps { get; }
    public AppSettings Settings { get; }
    public IReadOnlyList<ChangeRecord> InterruptedChanges { get; }
    public string Edition { get; }

    /// <summary>The user said no to the admin prompt when the app opened.</summary>
    public bool AdminDeclinedAtStart { get; set; }

    /// <summary>The most recent diagnostic report, shown on the Home page.</summary>
    public DiagnosticReport? LastReport { get; set; }

    public void SaveSettings() => Json.WriteFile(Paths.SettingsFile, Settings);

    public string LogFile => Path.Combine(Paths.Logs, $"console-{DateTime.Now:yyyyMMdd}.log");

    public void Log(string message)
    {
        try { File.AppendAllText(LogFile, $"{DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}"); } catch { }
    }

    private string TryReadEdition()
    {
        try { return Platform.Registry.GetValue(RegHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID")?.AsString() ?? ""; }
        catch { return ""; }
    }
}

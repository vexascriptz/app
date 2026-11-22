using System.Text;
using SoftReset.Core.Diagnostics;
using SoftReset.Core.Knowledge;
using SoftReset.Core.Parsing;
using SoftReset.Core.Platform;
using SoftReset.Core.Safety;

namespace SoftReset.Core.Optimization.Actions;

// =====================================================================  Storage

public abstract class FolderCleanupAction : OptimizationAction
{
    protected abstract string Folder(ActionContext ctx);
    protected virtual TimeSpan MinAge => TimeSpan.FromHours(24);
    public override OptimizationCategory Category => OptimizationCategory.Storage;
    public override Reversibility Reversibility => Reversibility.None;
    public override string Measurement => "Folder size is re-measured after cleanup.";

    public override Task<ActionInspection> InspectAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        var folder = Folder(ctx);
        if (!Directory.Exists(folder)) return Task.FromResult(ActionInspection.NotApplicable($"{folder} does not exist."));
        var m = FileCleaner.Measure(folder, MinAge, ct);
        return Task.FromResult(new ActionInspection
        {
            CurrentState = $"{m.FilesFound} files older than {MinAge.TotalHours:0} h ({ToolOutputParsers.FormatBytes(m.BytesFound)}) in {folder}",
            AlreadyOptimal = m.BytesFound < 50L * 1024 * 1024,
        });
    }

    public override Task<ActionPreview> PreviewAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        var folder = Folder(ctx);
        var m = FileCleaner.Measure(folder, MinAge, ct);
        var p = NewPreview();
        p.EstimatedBytesFreed = m.BytesFound;
        if (m.FilesFound > 0) p.Changes.Add(new PreviewChange(folder, $"{m.FilesFound} files, {ToolOutputParsers.FormatBytes(m.BytesFound)}", "Deleted (files in use are skipped)"));
        p.Warnings.Add("Deleted temporary files cannot be restored. Only files untouched for at least 24 hours are removed; links and junctions are never followed.");
        return Task.FromResult(p);
    }

    public override Task ApplyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        var folder = Folder(ctx);
        var r = FileCleaner.Delete(folder, MinAge, ct);
        record.Parameters["bytesFreed"] = r.BytesDeleted.ToString();
        record.ChangesApplied.Add($"{folder}: {FileCleaner.Summary(r)}");
        record.Message = FileCleaner.Summary(r);
        return Task.CompletedTask;
    }

    public override Task<VerificationResult> VerifyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        var after = FileCleaner.Measure(Folder(ctx), MinAge, ct);
        return Task.FromResult(VerificationResult.Ok($"Freed {ToolOutputParsers.FormatBytes(long.Parse(record.Parameters.GetValueOrDefault("bytesFreed", "0")))}. Remaining eligible: {ToolOutputParsers.FormatBytes(after.BytesFound)} (locked files)."));
    }
}

public sealed class UserTempCleanupAction : FolderCleanupAction
{
    public override string Id => ActionIds.UserTemp;
    public override string Title => "Clear temporary files";
    public override string Description => "Deletes old temporary files (not touched for a day). Nothing personal.";
    public override string ExpectedBenefit => "Frees disk space. Does not noticeably speed up Windows by itself.";
    public override string Risks => "Irreversible, but temporary files are by definition disposable. Unsaved work in crashed apps' temp files could be lost — ask the user first.";
    protected override string Folder(ActionContext ctx) => ctx.Platform.Folders.Temp;
}

public sealed class WindowsTempCleanupAction : FolderCleanupAction
{
    public override string Id => ActionIds.WindowsTemp;
    public override string Title => "Clear Windows temp files";
    public override string Description => "Deletes old files in Windows' own temp folder.";
    public override string ExpectedBenefit => "Frees disk space, sometimes several GB after failed installers.";
    public override string Risks => "Irreversible. Files in use are skipped.";
    public override bool RequiresAdmin => true;
    protected override string Folder(ActionContext ctx) => Path.Combine(ctx.Platform.Folders.WindowsDir, "Temp");
}

public sealed class RecycleBinAction : OptimizationAction
{
    public override string Id => ActionIds.RecycleBin;
    public override string Title => "Empty the Recycle Bin";
    public override OptimizationCategory Category => OptimizationCategory.Storage;
    public override string Description => "Permanently deletes everything in the Recycle Bin.";
    public override string ExpectedBenefit => "Frees the space used by deleted files.";
    public override string Risks => "IRREVERSIBLE. The user must confirm nothing in the Recycle Bin is needed.";
    public override Reversibility Reversibility => Reversibility.None;

    public override Task<ActionInspection> InspectAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        var info = ctx.Platform.Maintenance.GetRecycleBinInfo();
        if (info is null) return Task.FromResult(ActionInspection.NotApplicable("The Recycle Bin size could not be read."));
        return Task.FromResult(new ActionInspection { CurrentState = $"{info.Value.Items} items, {ToolOutputParsers.FormatBytes(info.Value.Bytes)}", AlreadyOptimal = info.Value.Items == 0 });
    }

    public override Task<ActionPreview> PreviewAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        var p = NewPreview();
        var info = ctx.Platform.Maintenance.GetRecycleBinInfo();
        if (info is { Items: > 0 } i)
        {
            p.Changes.Add(new PreviewChange("Recycle Bin", $"{i.Items} items, {ToolOutputParsers.FormatBytes(i.Bytes)}", "Empty"));
            p.EstimatedBytesFreed = i.Bytes;
        }
        p.Warnings.Add("This permanently deletes the files. Ask the user to review the Recycle Bin first.");
        return Task.FromResult(p);
    }

    public override Task ApplyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        var before = ctx.Platform.Maintenance.GetRecycleBinInfo();
        if (!ctx.Platform.Maintenance.EmptyRecycleBin(out var err)) throw new InvalidOperationException(err);
        record.ChangesApplied.Add($"Emptied Recycle Bin ({before?.Items} items, {ToolOutputParsers.FormatBytes(before?.Bytes)})");
        return Task.CompletedTask;
    }

    public override Task<VerificationResult> VerifyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        var info = ctx.Platform.Maintenance.GetRecycleBinInfo();
        return Task.FromResult(info is { Items: 0 } ? VerificationResult.Ok("Recycle Bin is empty.") : VerificationResult.Fail($"{info?.Items} items remain (some may be in use)."));
    }
}

public sealed class UpdateCacheCleanupAction : OptimizationAction
{
    public override string Id => ActionIds.UpdateCache;
    public override string Title => "Clear downloaded updates";
    public override OptimizationCategory Category => OptimizationCategory.Storage;
    public override string Description => "Deletes update files Windows has downloaded. Use if updates are stuck.";
    public override string ExpectedBenefit => "Frees space and clears corrupted downloads that make updates fail.";
    public override string Risks => "Pending updates will be downloaded again. Do not run while an update is installing.";
    public override bool RequiresAdmin => true;
    public override Reversibility Reversibility => Reversibility.None;

    private static string Folder(ActionContext ctx) => Path.Combine(ctx.Platform.Folders.WindowsDir, "SoftwareDistribution", "Download");

    public override Task<ActionInspection> InspectAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        var m = FileCleaner.Measure(Folder(ctx), TimeSpan.Zero, ct);
        return Task.FromResult(new ActionInspection { CurrentState = $"{ToolOutputParsers.FormatBytes(m.BytesFound)} in {m.FilesFound} files", AlreadyOptimal = m.BytesFound < 200L * 1024 * 1024 });
    }

    public override Task<ActionPreview> PreviewAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        var m = FileCleaner.Measure(Folder(ctx), TimeSpan.Zero, ct);
        var p = NewPreview();
        p.EstimatedBytesFreed = m.BytesFound;
        p.Changes.Add(new PreviewChange("Windows Update service (wuauserv) and BITS", "Running/Stopped", "Stopped temporarily, then restored"));
        if (m.FilesFound > 0) p.Changes.Add(new PreviewChange(Folder(ctx), ToolOutputParsers.FormatBytes(m.BytesFound), "Deleted"));
        return Task.FromResult(p);
    }

    public override Task ApplyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        var services = new[] { "wuauserv", "bits" };
        foreach (var s in services)
        {
            var svc = ctx.Platform.Services.Get(s);
            if (svc is null) continue;
            ctx.AddBackup(record, new BackupItem { Kind = "service-running", Description = $"{s} state", Data = { ["name"] = s, ["running"] = (svc.State == "Running").ToString() } });
            if (svc.State == "Running") ctx.Platform.Services.Stop(s, TimeSpan.FromSeconds(60));
        }
        try
        {
            var r = FileCleaner.Delete(Folder(ctx), TimeSpan.Zero, ct);
            record.ChangesApplied.Add($"Update cache: {FileCleaner.Summary(r)}");
            record.Message = FileCleaner.Summary(r);
        }
        finally
        {
            foreach (var b in record.Backups.Where(b => b.Kind == "service-running"))
                BackupRestorer.Restore(ctx, b);
        }
        return Task.CompletedTask;
    }

    public override Task<VerificationResult> VerifyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        var m = FileCleaner.Measure(Folder(ctx), TimeSpan.Zero, ct);
        return Task.FromResult(VerificationResult.Ok($"Remaining in cache: {ToolOutputParsers.FormatBytes(m.BytesFound)}."));
    }
}

// =====================================================================  Background

public sealed class EndProcessesAction : OptimizationAction
{
    public override string Id => ActionIds.EndProcesses;
    public override string Title => "Close background programs";
    public override OptimizationCategory Category => OptimizationCategory.Background;
    public override string Description => "Closes programs you pick from the ones using the most CPU or memory. Windows' own processes can't be picked.";
    public override string ExpectedBenefit => "Frees CPU and memory immediately (measured by re-sampling). The process may start again at next sign-in — disable its startup entry for a lasting effect.";
    public override string Risks => "Unsaved work in the ended application is lost. Ending a process is not reversible, but the application can simply be started again.";
    public override Reversibility Reversibility => Reversibility.None;

    public override Task<ActionInspection> InspectAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        var procs = ctx.Platform.Processes.Enumerate().Where(p => p.SessionId != 0).OrderByDescending(p => p.PrivateBytes).Take(40).ToList();
        var insp = new ActionInspection { CurrentState = $"{procs.Count} heaviest user-session processes" };
        foreach (var p in procs)
        {
            var k = ctx.Knowledge.FindProcess(p.Name);
            var critical = ProtectedComponents.IsCriticalProcess(p.Name) || k?.Critical == true;
            insp.Items.Add(new ActionItem
            {
                Id = p.Pid.ToString(),
                Label = $"{p.Name} (PID {p.Pid})",
                Detail = p.Path ?? "",
                CurrentState = $"{ToolOutputParsers.FormatBytes(p.PrivateBytes)} private memory",
                Recommendation = k?.Functionality,
                Selectable = !critical,
                Warning = critical ? "Protected Windows/security process" : null,
            });
        }
        return Task.FromResult(insp);
    }

    public override Task<ActionPreview> PreviewAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        var procs = ctx.Platform.Processes.Enumerate().ToDictionary(p => p.Pid.ToString());
        var p = NewPreview();
        foreach (var id in request.SelectedItems)
        {
            if (!procs.TryGetValue(id, out var proc)) continue;
            if (ProtectedComponents.IsCriticalProcess(proc.Name)) { p.Warnings.Add($"{proc.Name} is protected and will be skipped."); continue; }
            p.Changes.Add(new PreviewChange($"{proc.Name} (PID {proc.Pid})", "Running", "Ended"));
        }
        p.Warnings.Add("Make sure the user has saved their work in these applications.");
        return Task.FromResult(p);
    }

    public override Task ApplyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        var procs = ctx.Platform.Processes.Enumerate().ToDictionary(p => p.Pid.ToString());
        var failures = new List<string>();
        foreach (var id in request.SelectedItems)
        {
            if (!procs.TryGetValue(id, out var proc) || ProtectedComponents.IsCriticalProcess(proc.Name)) continue;
            if (ctx.Platform.Processes.TryTerminate(proc.Pid, out var err)) record.ChangesApplied.Add($"Ended {proc.Name} (PID {proc.Pid})");
            else failures.Add($"{proc.Name}: {err}");
        }
        record.Message = failures.Count == 0 ? $"{record.ChangesApplied.Count} process(es) ended." : $"Some processes could not be ended: {string.Join("; ", failures)}";
        return Task.CompletedTask;
    }

    public override Task<VerificationResult> VerifyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        var running = ctx.Platform.Processes.Enumerate().Select(p => p.Pid.ToString()).ToHashSet();
        var still = request.SelectedItems.Where(running.Contains).ToList();
        return Task.FromResult(still.Count == 0 ? VerificationResult.Ok("Selected processes are no longer running.") : VerificationResult.Fail($"{still.Count} process(es) still running (protected, elevated, or restarted)."));
    }
}

// =====================================================================  Command-based maintenance

public abstract class CommandAction : OptimizationAction
{
    protected abstract IReadOnlyList<(string Tool, string Args)> Commands(ActionContext ctx, ActionRequest r);
    protected virtual TimeSpan Timeout => TimeSpan.FromMinutes(5);
    protected virtual bool ContinueOnError => false;
    public override Reversibility Reversibility => Reversibility.None;

    public override Task<ActionInspection> InspectAsync(ActionContext ctx, ActionRequest request, CancellationToken ct) =>
        Task.FromResult(new ActionInspection { CurrentState = "Ready to run", Notes = Commands(ctx, request).Select(c => $"{c.Tool} {c.Args}").ToList() });

    public override Task<ActionPreview> PreviewAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        var p = NewPreview();
        foreach (var (tool, args) in Commands(ctx, request)) p.Changes.Add(new PreviewChange("Run", "", $"{tool} {args}"));
        AddWarnings(ctx, p);
        return Task.FromResult(p);
    }

    protected virtual void AddWarnings(ActionContext ctx, ActionPreview p) { }

    public override async Task ApplyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        var output = new StringBuilder();
        foreach (var (tool, args) in Commands(ctx, request))
        {
            ct.ThrowIfCancellationRequested();
            var r = await ctx.Platform.Commands.RunAsync(tool, args, Timeout, ct);
            output.AppendLine($"> {tool} {args}").AppendLine(Trim(r.StdOut)).AppendLine(Trim(r.StdErr));
            record.ChangesApplied.Add($"{tool} {args} → exit {r.ExitCode}{(r.TimedOut ? " (timed out)" : "")}");
            if (!r.Started) throw new InvalidOperationException($"{tool} could not be started: {r.StartError}");
            if (!r.Success && !ContinueOnError && !AcceptExitCode(tool, r.ExitCode))
                throw new InvalidOperationException($"{tool} failed (exit {r.ExitCode}): {Trim(r.StdErr.Length > 0 ? r.StdErr : r.StdOut, 400)}");
        }
        record.Parameters["output"] = Trim(output.ToString(), 6000);
        record.Message = Summarize(output.ToString());
    }

    protected virtual bool AcceptExitCode(string tool, int code) => false;
    protected virtual string Summarize(string output) => "Completed.";

    public override Task<VerificationResult> VerifyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct) =>
        Task.FromResult(VerificationResult.Ok(record.Message ?? "Command completed."));

    protected static string Trim(string s, int max = 2000) => s.Length <= max ? s.Trim() : s[..max].Trim() + " …";
}

public sealed class RestorePointAction : OptimizationAction
{
    public override string Id => ActionIds.RestorePoint;
    public override string Title => "Create a restore point";
    public override OptimizationCategory Category => OptimizationCategory.Maintenance;
    public override string Description => "Saves a snapshot so Windows can be put back to how it is now.";
    public override string ExpectedBenefit => "A safety net before larger changes. Recommended at the start of every session.";
    public override string Risks => "Uses some disk space. Windows allows one restore point per 24 hours by default; additional requests are skipped by Windows.";
    public override bool RequiresAdmin => true;
    public override Reversibility Reversibility => Reversibility.None;

    public override Task<ActionInspection> InspectAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        var enabled = ctx.Platform.Maintenance.IsSystemRestoreEnabled();
        return Task.FromResult(enabled == false
            ? new ActionInspection { Applicable = false, NotApplicableReason = "System Protection is turned off for the system drive. Turn it on in System Properties → System Protection first.", CurrentState = "System Protection off" }
            : new ActionInspection { CurrentState = enabled == true ? "System Protection on" : "System Protection state unknown" });
    }

    public override Task<ActionPreview> PreviewAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        var p = NewPreview();
        p.Changes.Add(new PreviewChange("System Restore", "", $"New restore point: \"{PointName(request)}\""));
        return Task.FromResult(p);
    }

    private static string PointName(ActionRequest r) => r.Param("description", "softreset session " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));

    public override Task ApplyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        var r = ctx.Platform.Maintenance.CreateRestorePoint(PointName(request));
        if (!r.Created && !r.Skipped) throw new InvalidOperationException(r.Message);
        record.ChangesApplied.Add(r.Message);
        record.Message = r.Message;
        return Task.CompletedTask;
    }

    public override Task<VerificationResult> VerifyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct) =>
        Task.FromResult(VerificationResult.Ok(record.Message ?? "Done."));
}

public sealed class SfcAction : CommandAction
{
    public override string Id => ActionIds.Sfc;
    public override string Title => "Repair Windows files";
    public override OptimizationCategory Category => OptimizationCategory.Maintenance;
    public override string Description => "Checks Windows' own files and replaces any that are damaged (SFC). Takes 10–20 minutes.";
    public override string ExpectedBenefit => "Fixes crashes and broken features caused by damaged system files. Takes 5–20 minutes.";
    public override string Risks => "Low. Replaces files with Microsoft originals; cannot be undone (and does not need to be).";
    public override bool RequiresAdmin => true;
    protected override TimeSpan Timeout => TimeSpan.FromMinutes(45);
    protected override IReadOnlyList<(string, string)> Commands(ActionContext ctx, ActionRequest r) => new[] { ("sfc.exe", "/scannow") };
    protected override bool AcceptExitCode(string tool, int code) => true; // SFC's exit code is not meaningful; parse output instead

    protected override string Summarize(string output)
    {
        var o = output.Replace("\0", "");
        if (o.Contains("did not find any integrity violations", StringComparison.OrdinalIgnoreCase)) return "No integrity violations found.";
        if (o.Contains("successfully repaired", StringComparison.OrdinalIgnoreCase)) return "Corrupt files were found and repaired. Restart Windows.";
        if (o.Contains("unable to fix", StringComparison.OrdinalIgnoreCase)) return "Corrupt files were found but some could not be repaired — run DISM RestoreHealth, then SFC again.";
        if (o.Contains("could not perform", StringComparison.OrdinalIgnoreCase)) return "SFC could not run — a restart may be pending.";
        return "SFC finished. Review the output (CBS.log) for details.";
    }
}

public sealed class DismRestoreHealthAction : CommandAction
{
    public override string Id => ActionIds.DismRestoreHealth;
    public override string Title => "Deep repair Windows";
    public override OptimizationCategory Category => OptimizationCategory.Maintenance;
    public override string Description => "Repairs the files Windows uses to fix itself (DISM). Use if Repair Windows files fails. Needs internet.";
    public override string ExpectedBenefit => "Fixes update failures and lets SFC repair files it previously could not. Takes 10–30 minutes and needs internet.";
    public override string Risks => "Low. Requires an internet connection.";
    public override bool RequiresAdmin => true;
    protected override TimeSpan Timeout => TimeSpan.FromMinutes(60);
    protected override IReadOnlyList<(string, string)> Commands(ActionContext ctx, ActionRequest r) => new[] { ("dism.exe", "/Online /Cleanup-Image /RestoreHealth") };
    protected override string Summarize(string output) =>
        output.Contains("successfully", StringComparison.OrdinalIgnoreCase) ? "Component store repair completed successfully. Run SFC next." : "DISM finished — review the output.";
}

public sealed class ComponentCleanupAction : CommandAction
{
    public override string Id => ActionIds.ComponentCleanup;
    public override string Title => "Remove old update files";
    public override OptimizationCategory Category => OptimizationCategory.Storage;
    public override string Description => "Deletes leftover copies of components that updates replaced.";
    public override string ExpectedBenefit => "Frees space in the WinSxS folder, often 1–5 GB.";
    public override string Risks => "Updates installed before the cleanup can no longer be uninstalled. Takes several minutes.";
    public override bool RequiresAdmin => true;
    protected override TimeSpan Timeout => TimeSpan.FromMinutes(60);
    protected override IReadOnlyList<(string, string)> Commands(ActionContext ctx, ActionRequest r) => new[] { ("dism.exe", "/Online /Cleanup-Image /StartComponentCleanup") };
}

public sealed class ChkdskScanAction : CommandAction
{
    public override string Id => ActionIds.ChkdskScan;
    public override string Title => "Check the drive for errors";
    public override OptimizationCategory Category => OptimizationCategory.Maintenance;
    public override string Description => "Scans the C: drive for problems without changing anything.";
    public override string ExpectedBenefit => "Confirms whether file system corruption is present before deciding on repairs.";
    public override string Risks => "None — the scan does not change anything. Repairs (if needed) must be scheduled separately.";
    public override bool RequiresAdmin => true;
    protected override TimeSpan Timeout => TimeSpan.FromMinutes(30);
    protected override IReadOnlyList<(string, string)> Commands(ActionContext ctx, ActionRequest r) =>
        new[] { ("chkdsk.exe", $"{Path.GetPathRoot(ctx.Platform.Folders.WindowsDir)?.TrimEnd('\\') ?? "C:"} /scan") };
    protected override bool AcceptExitCode(string tool, int code) => code is 0 or 1 or 2 or 3;
    protected override string Summarize(string output) =>
        output.Contains("found no problems", StringComparison.OrdinalIgnoreCase) ? "No file system problems found."
        : output.Contains("found problems", StringComparison.OrdinalIgnoreCase) || output.Contains("errors", StringComparison.OrdinalIgnoreCase) ? "Problems were reported — back up data and schedule 'chkdsk /spotfix' or a full repair."
        : "Scan finished — review the output.";
}

public sealed class ResetWindowsUpdateAction : OptimizationAction
{
    public override string Id => ActionIds.ResetWindowsUpdate;
    public override string Title => "Fix Windows Update";
    public override OptimizationCategory Category => OptimizationCategory.Maintenance;
    public override string Description => "Gives Windows Update a fresh start. Fixes stuck or failing updates.";
    public override string ExpectedBenefit => "Fixes updates stuck downloading or failing with cache-related errors.";
    public override string Risks => "The update history list in Settings appears empty afterwards (the original folder is kept and restored on rollback). Updates are downloaded again.";
    public override bool RequiresAdmin => true;
    public override Reversibility Reversibility => Reversibility.Partial;

    private static string Folder(ActionContext ctx) => Path.Combine(ctx.Platform.Folders.WindowsDir, "SoftwareDistribution");

    public override Task<ActionInspection> InspectAsync(ActionContext ctx, ActionRequest request, CancellationToken ct) =>
        Task.FromResult(Directory.Exists(Folder(ctx))
            ? new ActionInspection { CurrentState = $"{Folder(ctx)} present" }
            : ActionInspection.NotApplicable("SoftwareDistribution folder not found."));

    public override Task<ActionPreview> PreviewAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        var p = NewPreview();
        p.Changes.Add(new PreviewChange("wuauserv, bits", "Running", "Stopped, then restarted"));
        p.Changes.Add(new PreviewChange(Folder(ctx), "In use", $"Renamed to SoftwareDistribution.softreset-<timestamp>"));
        p.Warnings.Add("Do not run while updates are installing.");
        return Task.FromResult(p);
    }

    public override Task ApplyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        var services = new[] { "wuauserv", "bits" };
        foreach (var s in services)
        {
            var svc = ctx.Platform.Services.Get(s);
            if (svc?.State == "Running") ctx.Platform.Services.Stop(s, TimeSpan.FromSeconds(60));
        }
        var original = Folder(ctx);
        var renamed = original + ".softreset-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        try
        {
            ctx.AddBackup(record, new BackupItem { Kind = "folder-rename", Description = $"{original} → {renamed}", Data = { ["original"] = original, ["renamed"] = renamed, ["services"] = string.Join(',', services) } });
            Directory.Move(original, renamed);
            record.ChangesApplied.Add($"Renamed {original} to {renamed}");
        }
        finally
        {
            foreach (var s in services)
            {
                try { ctx.Platform.Services.Start(s, TimeSpan.FromSeconds(60)); } catch { /* wuauserv is trigger-start and may stop again */ }
            }
        }
        record.Message = "Windows Update components reset. Open Windows Update and check for updates.";
        return Task.CompletedTask;
    }

    public override Task<VerificationResult> VerifyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        var b = record.Backups.FirstOrDefault(x => x.Kind == "folder-rename");
        return Task.FromResult(b is not null && Directory.Exists(b.Get("renamed"))
            ? VerificationResult.Ok("Cache folder renamed; Windows will rebuild it on the next update check.")
            : VerificationResult.Fail("Backup folder not found."));
    }
}

// =====================================================================  Network

public sealed class FlushDnsAction : CommandAction
{
    public override string Id => ActionIds.FlushDns;
    public override string Title => "Clear DNS cache";
    public override OptimizationCategory Category => OptimizationCategory.Network;
    public override string Description => "Fixes websites that won't load after a network change.";
    public override string ExpectedBenefit => "Fixes websites that fail to load after DNS or hosting changes.";
    public override string Risks => "None. The cache rebuilds automatically.";
    protected override IReadOnlyList<(string, string)> Commands(ActionContext ctx, ActionRequest r) => new[] { ("ipconfig.exe", "/flushdns") };
    protected override string Summarize(string output) => "DNS resolver cache cleared.";
}

public sealed class RenewDhcpAction : CommandAction
{
    public override string Id => ActionIds.RenewDhcp;
    public override string Title => "Get a new IP address";
    public override OptimizationCategory Category => OptimizationCategory.Network;
    public override string Description => "Asks the router for a fresh connection.";
    public override string ExpectedBenefit => "Fixes 169.254.x.x addresses and connectivity after router changes.";
    public override string Risks => "Connections drop for a few seconds. Adapters with static IPs are not affected.";
    public override bool RequiresAdmin => true;
    protected override bool ContinueOnError => true;
    protected override TimeSpan Timeout => TimeSpan.FromMinutes(2);
    protected override IReadOnlyList<(string, string)> Commands(ActionContext ctx, ActionRequest r) => new[] { ("ipconfig.exe", "/release"), ("ipconfig.exe", "/renew") };
    protected override string Summarize(string output) => output.Contains("169.254", StringComparison.Ordinal) ? "Renewed, but an adapter still has a 169.254.x.x address — DHCP is not responding." : "DHCP leases renewed.";
}

public sealed class ResetNetworkStackAction : CommandAction
{
    public override string Id => ActionIds.ResetNetworkStack;
    public override string Title => "Reset network settings";
    public override OptimizationCategory Category => OptimizationCategory.Network;
    public override string Description => "Puts Windows networking back to defaults. Fixes many 'no internet' problems. Needs a restart.";
    public override string ExpectedBenefit => "Fixes connectivity broken by VPN/security software leftovers.";
    public override string Risks => "Static IP addresses and custom DNS settings are reset to DHCP; VPN clients may need reinstalling. Requires a restart. Cannot be rolled back automatically — the console records the current IP configuration in the change history for manual restoration.";
    public override bool RequiresAdmin => true;
    public override bool RequiresRestart => true;
    protected override bool ContinueOnError => true;
    protected override IReadOnlyList<(string, string)> Commands(ActionContext ctx, ActionRequest r) => new[] { ("netsh.exe", "winsock reset"), ("netsh.exe", "int ip reset"), ("ipconfig.exe", "/flushdns") };

    protected override void AddWarnings(ActionContext ctx, ActionPreview p)
    {
        var status = ctx.Platform.Network.GetStatus(false);
        foreach (var a in status.Adapters.Where(a => !a.IsVirtual && !a.DhcpEnabled && a.IPv4Addresses.Count > 0))
            p.Warnings.Add($"{a.Name} uses a static address ({string.Join(", ", a.IPv4Addresses)}; gateway {string.Join(", ", a.Gateways)}; DNS {string.Join(", ", a.DnsServers)}). It will be reset — note these values.");
    }

    public override async Task ApplyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        var status = ctx.Platform.Network.GetStatus(false);
        foreach (var a in status.Adapters.Where(a => !a.IsVirtual))
            ctx.AddBackup(record, new BackupItem
            {
                Kind = "note",
                Description = $"IP configuration of {a.Name} before reset",
                Data = { ["adapter"] = a.Name, ["dhcp"] = a.DhcpEnabled.ToString(), ["ipv4"] = string.Join(",", a.IPv4Addresses), ["gateway"] = string.Join(",", a.Gateways), ["dns"] = string.Join(",", a.DnsServers) },
            });
        await base.ApplyAsync(ctx, request, record, ct);
        record.Message = "Network stack reset. Restart Windows to complete.";
    }
}

// =====================================================================  Applications

public sealed class ClearAppCacheAction : OptimizationAction
{
    public override string Id => ActionIds.ClearAppCache;
    public override string Title => "Clear an app's cache";
    public override OptimizationCategory Category => OptimizationCategory.AppMaintenance;
    public override string Description => "Deletes an app's temporary files. Logins and settings are kept. Close the app first.";
    public override string ExpectedBenefit => "Fixes apps that show blank windows, fail to update, or behave erratically because of corrupted cache; frees space.";
    public override string Risks => "Irreversible, but caches rebuild automatically. The first launch afterwards is slower.";
    public override Reversibility Reversibility => Reversibility.None;

    private static AppGuide Guide(ActionContext ctx, ActionRequest r) =>
        ctx.Knowledge.FindAppGuideByKey(r.Param("app")) ?? throw new ArgumentException($"Unknown application '{r.Param("app")}'.");

    private static IEnumerable<(CacheLocation Loc, string Path)> Locations(AppGuide g) =>
        g.Caches.Select(c => (c, Environment.ExpandEnvironmentVariables(c.Path).Replace('\\', Path.DirectorySeparatorChar)));

    private static List<string> RunningProcesses(ActionContext ctx, AppGuide g)
    {
        var names = g.ProcessNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return ctx.Platform.Processes.Enumerate().Where(p => names.Contains(p.Name)).Select(p => p.Name).Distinct().ToList();
    }

    public override Task<ActionInspection> InspectAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.Param("app")))
        {
            var insp0 = new ActionInspection { CurrentState = "Choose an application", OptionParameter = "app" };
            foreach (var g in ctx.Knowledge.AppGuides.Where(g => g.Caches.Count > 0))
                insp0.Options.Add(new ActionOption { Value = g.Key, Label = g.Name });
            return Task.FromResult(insp0);
        }
        var guide = Guide(ctx, request);
        var insp = new ActionInspection { CurrentState = guide.Name };
        foreach (var (loc, path) in Locations(guide))
        {
            var m = Directory.Exists(path) ? FileCleaner.Measure(path, TimeSpan.Zero, ct) : new CleanupResult();
            insp.Items.Add(new ActionItem { Id = loc.Path, Label = loc.Description, Detail = path, SizeBytes = m.BytesFound, CurrentState = Directory.Exists(path) ? ToolOutputParsers.FormatBytes(m.BytesFound) : "Not present", Selected = Directory.Exists(path), Selectable = Directory.Exists(path) });
        }
        var running = RunningProcesses(ctx, guide);
        if (running.Count > 0) insp.Notes.Add($"Close {guide.Name} first — running: {string.Join(", ", running)}");
        return Task.FromResult(insp);
    }

    public override Task<ActionPreview> PreviewAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        var guide = Guide(ctx, request);
        var p = NewPreview();
        long total = 0;
        foreach (var (loc, path) in Locations(guide).Where(l => request.SelectedItems.Count == 0 || request.SelectedItems.Contains(l.Loc.Path)))
        {
            if (!Directory.Exists(path)) continue;
            var m = FileCleaner.Measure(path, TimeSpan.Zero, ct);
            total += m.BytesFound;
            p.Changes.Add(new PreviewChange(path, ToolOutputParsers.FormatBytes(m.BytesFound), "Emptied"));
        }
        p.EstimatedBytesFreed = total;
        var running = RunningProcesses(ctx, guide);
        if (running.Count > 0) p.Warnings.Add($"{guide.Name} is running ({string.Join(", ", running)}). Close it before applying, or locked files will be skipped.");
        return Task.FromResult(p);
    }

    public override Task ApplyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        var guide = Guide(ctx, request);
        long freed = 0;
        foreach (var (loc, path) in Locations(guide).Where(l => request.SelectedItems.Count == 0 || request.SelectedItems.Contains(l.Loc.Path)))
        {
            if (!Directory.Exists(path)) continue;
            var r = FileCleaner.Delete(path, TimeSpan.Zero, ct);
            freed += r.BytesDeleted;
            record.ChangesApplied.Add($"{path}: {FileCleaner.Summary(r)}");
        }
        record.Parameters["bytesFreed"] = freed.ToString();
        record.Message = $"Freed {ToolOutputParsers.FormatBytes(freed)} from {guide.Name} caches.";
        return Task.CompletedTask;
    }

    public override Task<VerificationResult> VerifyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct) =>
        Task.FromResult(VerificationResult.Ok(record.Message ?? "Done."));
}

public sealed class WingetUpgradeAction : OptimizationAction
{
    public override string Id => ActionIds.WingetUpgrade;
    public override string Title => "Update apps";
    public override OptimizationCategory Category => OptimizationCategory.AppMaintenance;
    public override string Description => "Finds apps with updates and installs the ones you pick. Needs internet.";
    public override string ExpectedBenefit => "Security and bug fixes for outdated software.";
    public override string Risks => "Installers may close running apps or show their own prompts. Version upgrades cannot be rolled back automatically.";
    public override Reversibility Reversibility => Reversibility.None;

    public override async Task<ActionInspection> InspectAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        var r = await ctx.Platform.Commands.RunAsync("winget", "upgrade --include-unknown --accept-source-agreements --disable-interactivity", TimeSpan.FromMinutes(3), ct);
        if (!r.Started) return ActionInspection.NotApplicable("winget (App Installer) is not available on this PC.");
        var list = ToolOutputParsers.ParseWingetUpgrade(r.StdOut);
        var insp = new ActionInspection { CurrentState = list.Count == 0 ? "No upgrades found (or winget could not reach its sources)." : $"{list.Count} upgrade(s) available", AlreadyOptimal = list.Count == 0 };
        foreach (var u in list)
            insp.Items.Add(new ActionItem { Id = u.Id, Label = u.Name, Detail = u.Id, CurrentState = $"{u.Version} → {u.Available}", Selected = false });
        return insp;
    }

    public override Task<ActionPreview> PreviewAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        var p = NewPreview();
        foreach (var id in request.SelectedItems) p.Changes.Add(new PreviewChange(id, "Installed version", "Latest version via winget"));
        p.Warnings.Add("Save work in these applications first; installers may close them.");
        return Task.FromResult(p);
    }

    public override async Task ApplyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        var failures = 0;
        foreach (var id in request.SelectedItems)
        {
            ct.ThrowIfCancellationRequested();
            if (id.Any(ch => char.IsWhiteSpace(ch) || ch is '"' or '&' or '|' or ';')) { record.ChangesApplied.Add($"Skipped invalid id {id}"); continue; }
            var r = await ctx.Platform.Commands.RunAsync("winget", $"upgrade --id \"{id}\" --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity", TimeSpan.FromMinutes(20), ct);
            if (r.Success) record.ChangesApplied.Add($"Upgraded {id}");
            else { failures++; record.ChangesApplied.Add($"{id}: winget exit {r.ExitCode}"); }
        }
        record.Message = failures == 0 ? "Selected applications upgraded." : $"{failures} upgrade(s) failed — see details.";
    }

    public override Task<VerificationResult> VerifyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct) =>
        Task.FromResult(record.ChangesApplied.All(c => c.StartsWith("Upgraded", StringComparison.Ordinal))
            ? VerificationResult.Ok("winget reported success for all selected applications.")
            : VerificationResult.Fail("Some upgrades did not succeed."));
}

// =====================================================================  Settings Explorer generic writes

public sealed class SettingRegistrySetAction : OptimizationAction, IDynamicAdminAction, IDynamicTitleAction
{
    public override string Id => ActionIds.SettingRegistrySet;
    public override string Title => "Change a Windows setting";
    public override OptimizationCategory Category => OptimizationCategory.Settings;
    public override string Description => "Writes a documented registry-backed preference from the Settings Explorer catalog, with backup.";
    public override string ExpectedBenefit => "See the setting's entry in Settings Explorer.";
    public override string Risks => "See the setting's entry in Settings Explorer.";
    public override Reversibility Reversibility => Reversibility.Full;
    public override bool Internal => true;

    private static (SettingDefinition Def, SettingControl Control) Resolve(ActionContext? ctx, ActionRequest r)
    {
        var kb = ctx?.Knowledge ?? KnowledgeBase.Default;
        var def = kb.FindSetting(r.Param("setting")) ?? throw new ArgumentException($"Unknown setting '{r.Param("setting")}'.");
        var idx = int.Parse(r.Param("control", "0"));
        if (idx < 0 || idx >= def.Controls.Count) throw new ArgumentException("Invalid control index.");
        var c = def.Controls[idx];
        if (c.Type is not ("registry-set" or "registry-delete")) throw new ArgumentException("Control is not a registry write.");
        if (def.Read.Type != "registry") throw new ArgumentException("Setting is not registry-backed.");
        return (def, c);
    }

    public bool RequiresAdminFor(ActionRequest request)
    {
        var (def, _) = Resolve(null, request);
        return def.AdminRequired || RegistryBackup.ParseHive(def.Read.Hive ?? "HKCU") == RegHive.LocalMachine;
    }

    public string TitleFor(ActionRequest request)
    {
        try { var (def, c) = Resolve(null, request); return $"{def.Name}: {c.ResultLabel ?? c.Label}"; }
        catch { return Title; }
    }

    public override Task<ActionInspection> InspectAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        var (def, _) = Resolve(ctx, request);
        var state = new SettingsReader(ctx.Platform, "").Read(def.Read);
        return Task.FromResult(new ActionInspection { CurrentState = state.Display });
    }

    public override Task<ActionPreview> PreviewAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        var (def, c) = Resolve(ctx, request);
        var p = NewPreview();
        p.RequiresAdmin = RequiresAdminFor(request);
        p.RequiresRestart = def.Restart is "restart" or "signout";
        if (IsAlreadySet(ctx, def, c)) return Task.FromResult(p); // nothing to change
        var cur = new SettingsReader(ctx.Platform, "").Read(def.Read);
        p.Changes.Add(new PreviewChange(def.Name, cur.Display, c.ResultLabel ?? (c.Type == "registry-delete" ? "Windows default" : c.Data ?? "")));
        if (def.Restart != "none") p.Warnings.Add($"Takes effect after: {def.RestartText}.");
        return Task.FromResult(p);
    }

    /// <summary>True when the registry already holds exactly the value this control would write.</summary>
    private static bool IsAlreadySet(ActionContext ctx, SettingDefinition def, SettingControl c)
    {
        var v = ctx.Platform.Registry.GetValue(RegistryBackup.ParseHive(def.Read.Hive!), def.Read.Path!, def.Read.Value!);
        return c.Type == "registry-delete"
            ? v is null
            : v is not null && RegistryBackup.Encode(v) == RegistryBackup.Encode(RegistryBackup.Decode(Enum.Parse<RegKind>(c.Kind ?? "DWord", true), c.Data ?? ""));
    }

    public override Task ApplyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        var (def, c) = Resolve(ctx, request);
        var hive = RegistryBackup.ParseHive(def.Read.Hive!);
        var reg = ctx.Platform.Registry;
        ctx.AddBackup(record, RegistryBackup.Capture(reg, hive, def.Read.Path!, def.Read.Value!, def.Name));
        if (c.Type == "registry-delete")
        {
            if (reg.GetValue(hive, def.Read.Path!, def.Read.Value!) is not null) reg.DeleteValue(hive, def.Read.Path!, def.Read.Value!);
            record.ChangesApplied.Add($"{def.Name}: removed (Windows default)");
        }
        else
        {
            var kind = Enum.Parse<RegKind>(c.Kind ?? "DWord", true);
            reg.SetValue(hive, def.Read.Path!, def.Read.Value!, RegistryBackup.Decode(kind, c.Data ?? ""));
            record.ChangesApplied.Add($"{def.Name}: {c.ResultLabel ?? c.Data}");
        }
        ctx.Platform.Maintenance.BroadcastSettingsChanged();
        return Task.CompletedTask;
    }

    public override Task<VerificationResult> VerifyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        var (def, c) = Resolve(ctx, request);
        var v = ctx.Platform.Registry.GetValue(RegistryBackup.ParseHive(def.Read.Hive!), def.Read.Path!, def.Read.Value!);
        var ok = c.Type == "registry-delete" ? v is null : v is not null && RegistryBackup.Encode(v) == RegistryBackup.Encode(RegistryBackup.Decode(Enum.Parse<RegKind>(c.Kind ?? "DWord", true), c.Data ?? ""));
        return Task.FromResult(ok ? VerificationResult.Ok(def.Restart == "none" ? "Confirmed: Windows now reports the new value." : $"Confirmed: the new value is stored. {def.RestartText} for it to take effect.") : VerificationResult.Fail("The stored value does not match — policy may override this setting."));
    }

    protected override void AfterRegistryChange(ActionContext ctx) => ctx.Platform.Maintenance.BroadcastSettingsChanged();
}

public sealed class ServiceStartModeAction : OptimizationAction, IDynamicTitleAction
{
    public override string Id => ActionIds.ServiceStartMode;
    public override string Title => "Change a service's startup type";
    public override OptimizationCategory Category => OptimizationCategory.Settings;
    public override string Description => "Changes the startup type of a single service, with backup. Critical Windows and security services can only be restored to an enabled state, never disabled.";
    public override string ExpectedBenefit => "Restore services broken by tweak tools, or set a third-party service to Manual.";
    public override string Risks => "Disabling the wrong service breaks features. Blanket service disabling is deliberately not offered.";
    public override bool RequiresAdmin => true;
    public override Reversibility Reversibility => Reversibility.Full;
    public override bool Internal => true;

    public string TitleFor(ActionRequest r) => $"Service {r.Param("service")}: {r.Param("startMode")}";

    private static void Validate(ActionRequest r, ServiceInfo? svc)
    {
        var mode = r.Param("startMode");
        if (mode is not ("Auto" or "AutoDelayed" or "Manual" or "Disabled")) throw new ArgumentException("startMode must be Auto, AutoDelayed, Manual, or Disabled.");
        if (svc is null) throw new InvalidOperationException($"Service '{r.Param("service")}' not found.");
        if (ProtectedComponents.IsCriticalService(svc.Name) && mode == "Disabled")
            throw new InvalidOperationException($"{svc.DisplayName} is a protected Windows service and cannot be disabled by the console.");
    }

    public override Task<ActionInspection> InspectAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        var svc = ctx.Platform.Services.Get(request.Param("service"));
        return Task.FromResult(svc is null ? ActionInspection.NotApplicable("Service not found.") : new ActionInspection { CurrentState = $"{svc.StartMode}{(svc.DelayedAutoStart ? " (delayed)" : "")}, {svc.State}" });
    }

    public override Task<ActionPreview> PreviewAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        var svc = ctx.Platform.Services.Get(request.Param("service"));
        Validate(request, svc);
        var p = NewPreview();
        p.Changes.Add(new PreviewChange($"{svc!.DisplayName} ({svc.Name})", $"{svc.StartMode}{(svc.DelayedAutoStart ? " (delayed)" : "")}", request.Param("startMode")));
        if (ProtectedComponents.IsCriticalService(svc.Name)) p.Warnings.Add("This is a core Windows service.");
        return Task.FromResult(p);
    }

    public override Task ApplyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        var svc = ctx.Platform.Services.Get(request.Param("service"));
        Validate(request, svc);
        ctx.AddBackup(record, new BackupItem { Kind = "service-start", Description = svc!.DisplayName, Data = { ["name"] = svc.Name, ["startMode"] = svc.StartMode, ["delayed"] = svc.DelayedAutoStart.ToString() } });
        var mode = request.Param("startMode");
        ctx.Platform.Services.SetStartMode(svc.Name, mode == "AutoDelayed" ? "Auto" : mode, mode == "AutoDelayed");
        record.ChangesApplied.Add($"{svc.DisplayName}: {svc.StartMode} → {mode}");
        return Task.CompletedTask;
    }

    public override Task<VerificationResult> VerifyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        var svc = ctx.Platform.Services.Get(request.Param("service"));
        var mode = request.Param("startMode");
        var ok = svc is not null && (mode == "AutoDelayed" ? svc.StartMode == "Auto" && svc.DelayedAutoStart : svc.StartMode == mode);
        return Task.FromResult(ok ? VerificationResult.Ok($"Startup type is now {mode}.") : VerificationResult.Fail($"Startup type reads {svc?.StartMode}."));
    }
}

public sealed class TaskEnabledAction : OptimizationAction, IDynamicTitleAction
{
    public override string Id => ActionIds.TaskEnabled;
    public override string Title => "Enable or disable a scheduled task";
    public override OptimizationCategory Category => OptimizationCategory.Settings;
    public override string Description => "Enables or disables one scheduled task, with backup. Tasks under \\Microsoft\\Windows\\ can be enabled but not disabled.";
    public override string ExpectedBenefit => "Stop a misbehaving third-party task or restore one disabled by a tweak tool.";
    public override string Risks => "Disabling an updater stops automatic updates for that app.";
    public override bool RequiresAdmin => true;
    public override Reversibility Reversibility => Reversibility.Full;
    public override bool Internal => true;

    public string TitleFor(ActionRequest r) => $"Task {r.Param("task")}: {(r.Param("enabled") == "true" ? "enable" : "disable")}";

    private static ScheduledTaskInfo Find(ActionContext ctx, ActionRequest r)
    {
        var t = ctx.Platform.Tasks.GetTasks().FirstOrDefault(x => x.Path.Equals(r.Param("task"), StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException("Task not found.");
        if (r.Param("enabled") != "true" && t.Path.StartsWith(@"\Microsoft\Windows\", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Built-in Windows tasks are protected and cannot be disabled by the console.");
        return t;
    }

    public override Task<ActionInspection> InspectAsync(ActionContext ctx, ActionRequest request, CancellationToken ct) =>
        Task.FromResult(new ActionInspection { CurrentState = Find(ctx, request).Enabled ? "Enabled" : "Disabled" });

    public override Task<ActionPreview> PreviewAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        var t = Find(ctx, request);
        var p = NewPreview();
        var en = request.Param("enabled") == "true";
        if (t.Enabled != en) p.Changes.Add(new PreviewChange(t.Path, t.Enabled ? "Enabled" : "Disabled", en ? "Enabled" : "Disabled"));
        return Task.FromResult(p);
    }

    public override Task ApplyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        var t = Find(ctx, request);
        var en = request.Param("enabled") == "true";
        ctx.AddBackup(record, new BackupItem { Kind = "scheduled-task", Description = t.Path, Data = { ["path"] = t.Path, ["enabled"] = t.Enabled.ToString() } });
        ctx.Platform.Tasks.SetEnabled(t.Path, en);
        record.ChangesApplied.Add($"{t.Path}: {(en ? "enabled" : "disabled")}");
        return Task.CompletedTask;
    }

    public override Task<VerificationResult> VerifyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct) =>
        Task.FromResult(Find(ctx, request).Enabled == (request.Param("enabled") == "true") ? VerificationResult.Ok("Confirmed.") : VerificationResult.Fail("Did not change."));
}

public sealed class RestartExplorerAction : OptimizationAction
{
    public override string Id => ActionIds.RestartExplorer;
    public override string Title => "Restart File Explorer";
    public override OptimizationCategory Category => OptimizationCategory.Desktop;
    public override string Description => "Restarts the taskbar and desktop so changes show up.";
    public override string ExpectedBenefit => "Applies taskbar/Explorer changes; recovers a frozen taskbar.";
    public override string Risks => "Open File Explorer windows close. File copies in progress in Explorer are interrupted.";
    public override Reversibility Reversibility => Reversibility.None;

    public override Task<ActionInspection> InspectAsync(ActionContext ctx, ActionRequest request, CancellationToken ct) =>
        Task.FromResult(new ActionInspection { CurrentState = "Ready" });

    public override Task<ActionPreview> PreviewAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        var p = NewPreview();
        p.Changes.Add(new PreviewChange("explorer.exe", "Running", "Restarted"));
        p.Warnings.Add("Wait for any File Explorer copy/move operations to finish first.");
        return Task.FromResult(p);
    }

    public override Task ApplyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        if (!ctx.Platform.Maintenance.RestartExplorer(out var err)) throw new InvalidOperationException(err);
        record.ChangesApplied.Add("Restarted explorer.exe");
        return Task.CompletedTask;
    }

    public override Task<VerificationResult> VerifyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct) =>
        Task.FromResult(ctx.Platform.Processes.Enumerate().Any(p => p.Name.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase))
            ? VerificationResult.Ok("Explorer is running.") : VerificationResult.Fail("Explorer is not running — start it from Task Manager (File → Run new task → explorer.exe)."));
}

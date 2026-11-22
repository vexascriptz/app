using SoftReset.Core.Parsing;
using SoftReset.Core.Platform;
using SoftReset.Core.Safety;

namespace SoftReset.Core.Optimization.Actions;

public sealed record RegTarget(RegHive Hive, string Path, string Name, RegKind Kind)
{
    public override string ToString() => $@"{RegistryBackup.HiveName(Hive)}\{Path}\{Name}";
}

/// <summary>
/// An action that sets one or more registry values to one of several named options.
/// Every write is preceded by a backup; rollback restores the exact previous data
/// (including deleting values that did not exist).
/// </summary>
public abstract class RegistryOptionsAction : OptimizationAction
{
    protected abstract IReadOnlyList<RegTarget> Targets { get; }
    /// <summary>Option name → value per target (same order as <see cref="Targets"/>); null deletes the value.</summary>
    protected abstract IReadOnlyDictionary<string, object?[]> OptionValues { get; }
    protected abstract string RecommendedOption { get; }
    protected virtual string? DefaultWhenMissing => null;

    public override bool RequiresAdmin => Targets.Any(t => t.Hive == RegHive.LocalMachine);
    public override Reversibility Reversibility => Reversibility.Full;

    protected string? CurrentOption(IRegistryAccess reg)
    {
        foreach (var (option, values) in OptionValues)
        {
            var match = true;
            for (var i = 0; i < Targets.Count; i++)
            {
                var t = Targets[i];
                var cur = reg.GetValue(t.Hive, t.Path, t.Name);
                var want = values[i];
                if (want is null) { if (cur is not null) { match = false; break; } continue; }
                if (cur is null) { match = false; break; }
                if (RegistryBackup.Encode(cur) != RegistryBackup.Encode(new RegValue(t.Kind, want))) { match = false; break; }
            }
            if (match) return option;
        }
        // All values missing → Windows default.
        if (DefaultWhenMissing is not null && Targets.All(t => reg.GetValue(t.Hive, t.Path, t.Name) is null))
            return DefaultWhenMissing;
        return null;
    }

    public override Task<ActionInspection> InspectAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        if (MinBuild > 0 && ctx.Platform.OsBuild < MinBuild)
            return Task.FromResult(ActionInspection.NotApplicable($"Requires Windows build {MinBuild} or later."));
        var current = CurrentOption(ctx.Platform.Registry);
        var insp = new ActionInspection
        {
            CurrentState = current ?? "Custom / unrecognised value",
            AlreadyOptimal = current == RecommendedOption,
        };
        foreach (var o in OptionValues.Keys)
            insp.Options.Add(new ActionOption { Value = o, Label = o, IsCurrent = o == current, IsRecommended = o == RecommendedOption });
        foreach (var t in Targets)
        {
            var v = ctx.Platform.Registry.GetValue(t.Hive, t.Path, t.Name);
            insp.Notes.Add($"{t} = {(v is null ? "(not set)" : RegistryBackup.Encode(v))}");
        }
        return Task.FromResult(insp);
    }

    protected string TargetOption(ActionRequest r)
    {
        var o = r.Param("state", RecommendedOption);
        if (!OptionValues.ContainsKey(o)) throw new ArgumentException($"Unknown option '{o}'. Valid: {string.Join(", ", OptionValues.Keys)}");
        return o;
    }

    public override Task<ActionPreview> PreviewAsync(ActionContext ctx, ActionRequest request, CancellationToken ct)
    {
        var target = TargetOption(request);
        var preview = NewPreview();
        var values = OptionValues[target];
        for (var i = 0; i < Targets.Count; i++)
        {
            var t = Targets[i];
            var cur = ctx.Platform.Registry.GetValue(t.Hive, t.Path, t.Name);
            var from = cur is null ? "(not set)" : RegistryBackup.Encode(cur);
            var to = values[i] is null ? "(removed — Windows default)" : RegistryBackup.Encode(new RegValue(t.Kind, values[i]));
            if (from != to) preview.Changes.Add(new PreviewChange(t.ToString(), from, to));
        }
        AddPreviewWarnings(preview, target);
        return Task.FromResult(preview);
    }

    protected virtual void AddPreviewWarnings(ActionPreview preview, string target) { }

    public override Task ApplyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        var target = TargetOption(request);
        var values = OptionValues[target];
        var reg = ctx.Platform.Registry;
        for (var i = 0; i < Targets.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var t = Targets[i];
            ctx.AddBackup(record, RegistryBackup.Capture(reg, t.Hive, t.Path, t.Name));
            if (values[i] is null)
            {
                if (reg.GetValue(t.Hive, t.Path, t.Name) is not null) reg.DeleteValue(t.Hive, t.Path, t.Name);
                record.ChangesApplied.Add($"Removed {t}");
            }
            else
            {
                reg.SetValue(t.Hive, t.Path, t.Name, new RegValue(t.Kind, values[i]));
                record.ChangesApplied.Add($"Set {t} = {RegistryBackup.Encode(new RegValue(t.Kind, values[i]))}");
            }
        }
        record.Message = $"Set to '{target}'.";
        AfterRegistryChange(ctx);
        return Task.CompletedTask;
    }

    public override Task<VerificationResult> VerifyAsync(ActionContext ctx, ActionRequest request, ChangeRecord record, CancellationToken ct)
    {
        var target = TargetOption(request);
        var now = CurrentOption(ctx.Platform.Registry);
        return Task.FromResult(now == target
            ? VerificationResult.Ok($"Confirmed: setting now reads '{target}'.")
            : VerificationResult.Fail($"Expected '{target}' but the setting reads '{now ?? "unrecognised"}'. A policy or another program may be overriding it."));
    }

    protected override void AfterRegistryChange(ActionContext ctx) => ctx.Platform.Maintenance.BroadcastSettingsChanged();
}

public sealed class CleanupResult
{
    public long BytesFound { get; set; }
    public int FilesFound { get; set; }
    public long BytesDeleted { get; set; }
    public int FilesDeleted { get; set; }
    public int FilesSkipped { get; set; }
    public int DirectoriesRemoved { get; set; }
}

/// <summary>
/// Deletes files under a folder with strict safety rules: never follows junctions or
/// symbolic links, skips files modified or created recently, skips whole folders that
/// were created recently (e.g. an archive just extracted there, whose files keep their
/// original old dates), and skips anything locked.
/// </summary>
public static class FileCleaner
{
    public static CleanupResult Measure(string root, TimeSpan minAge, CancellationToken ct = default, bool? useCreationTime = null) =>
        Walk(root, minAge, delete: false, useCreationTime ?? OperatingSystem.IsWindows(), ct);

    public static CleanupResult Delete(string root, TimeSpan minAge, CancellationToken ct = default, bool? useCreationTime = null) =>
        Walk(root, minAge, delete: true, useCreationTime ?? OperatingSystem.IsWindows(), ct);

    private static CleanupResult Walk(string root, TimeSpan minAge, bool delete, bool useCreationTime, CancellationToken ct)
    {
        var result = new CleanupResult();
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return result;
        var cutoff = DateTime.UtcNow - minAge;
        WalkDir(new DirectoryInfo(root), isRoot: true);
        return result;

        void WalkDir(DirectoryInfo dir, bool isRoot)
        {
            ct.ThrowIfCancellationRequested();
            FileSystemInfo[] entries;
            try { entries = dir.GetFileSystemInfos(); }
            catch { result.FilesSkipped++; return; }

            foreach (var e in entries)
            {
                ct.ThrowIfCancellationRequested();
                if ((e.Attributes & FileAttributes.ReparsePoint) != 0) { result.FilesSkipped++; continue; }
                if (e is DirectoryInfo sub)
                {
                    if (IsNewFolder(sub, cutoff, useCreationTime)) { result.FilesSkipped++; continue; } // new folder: leave all of it
                    WalkDir(sub, isRoot: false);
                    continue;
                }
                if (e is not FileInfo f) continue;
                long len;
                try { len = f.Length; } catch { result.FilesSkipped++; continue; }
                if (IsRecent(f, cutoff, useCreationTime)) { result.FilesSkipped++; continue; }
                result.FilesFound++;
                result.BytesFound += len;
                if (!delete) continue;
                try
                {
                    if ((f.Attributes & FileAttributes.ReadOnly) != 0) f.Attributes &= ~FileAttributes.ReadOnly;
                    f.Delete();
                    result.FilesDeleted++;
                    result.BytesDeleted += len;
                }
                catch { result.FilesSkipped++; }
            }

            if (delete && !isRoot)
            {
                try
                {
                    if (!dir.EnumerateFileSystemInfos().Any() && dir.LastWriteTimeUtc <= cutoff)
                    {
                        dir.Delete();
                        result.DirectoriesRemoved++;
                    }
                }
                catch { /* in use or not empty */ }
            }
        }
    }

    /// <summary>Modified or created after the cutoff. Extracted and copied files keep their old
    /// modified date but get a new creation date, so both are checked.</summary>
    /// By default creation dates are only trusted on Windows (NTFS records them on extract and
    /// copy); other file systems may not keep them or allow setting them.
    private static bool IsRecent(FileInfo f, DateTime cutoff, bool useCreationTime)
    {
        try { return f.LastWriteTimeUtc > cutoff || (useCreationTime && f.CreationTimeUtc > cutoff); }
        catch { return true; }
    }

    /// A folder's modified date changes whenever anything inside it changes, so only its
    /// creation date says whether the folder itself is new.
    private static bool IsNewFolder(DirectoryInfo d, DateTime cutoff, bool useCreationTime)
    {
        if (!useCreationTime) return false;
        try { return d.CreationTimeUtc > cutoff; }
        catch { return true; }
    }

    public static string Summary(CleanupResult r) =>
        $"{r.FilesDeleted} files deleted ({ToolOutputParsers.FormatBytes(r.BytesDeleted)}), {r.FilesSkipped} skipped (in use, too recent, or protected).";
}

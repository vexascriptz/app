using SoftReset.Core.Models;
using SoftReset.Core.Platform;
using SoftReset.Core.Safety;

namespace SoftReset.Core.Startup;

/// <summary>
/// Enumerates startup entries the same way Task Manager's Startup tab does, and
/// reads/writes the "StartupApproved" state that Task Manager and Settings use to
/// enable or disable them. Disabling through StartupApproved never deletes the
/// original Run value or shortcut, so it is fully reversible.
/// </summary>
public sealed class StartupInventory
{
    public const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string RunPathWow64 = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    public const string ApprovedRoot = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    private readonly IRegistryAccess _reg;
    private readonly IKnownFolders _folders;
    private readonly Func<string, bool> _fileExists;

    public StartupInventory(IRegistryAccess reg, IKnownFolders folders, Func<string, bool>? fileExists = null)
    {
        _reg = reg;
        _folders = folders;
        _fileExists = fileExists ?? File.Exists;
    }

    public List<StartupEntry> Enumerate(IEnumerable<ScheduledTaskInfo>? tasks = null)
    {
        var list = new List<StartupEntry>();
        AddRunKey(list, RegHive.CurrentUser, RunPath, StartupLocation.RegistryRunUser, "Run");
        AddRunKey(list, RegHive.LocalMachine, RunPath, StartupLocation.RegistryRunMachine, "Run");
        AddRunKey(list, RegHive.LocalMachine, RunPathWow64, StartupLocation.RegistryRunMachineWow64, "Run32");
        AddFolder(list, _folders.StartupUser, StartupLocation.StartupFolderUser, RegHive.CurrentUser);
        AddFolder(list, _folders.StartupCommon, StartupLocation.StartupFolderCommon, RegHive.LocalMachine);

        if (tasks is not null)
        {
            foreach (var t in tasks.Where(t => t.HasLogonTrigger && !t.IsMicrosoft))
            {
                list.Add(new StartupEntry
                {
                    Id = "task:" + t.Path,
                    Name = t.Name,
                    Command = t.Action,
                    ExecutablePath = CommandLine.ExtractExecutable(t.Action),
                    Location = StartupLocation.ScheduledTaskLogon,
                    LocationDetail = "Task Scheduler" + t.Path,
                    Enabled = t.Enabled,
                    MachineWide = true,
                    FileExists = CommandLine.ExtractExecutable(t.Action) is not { } exe || _fileExists(exe),
                });
            }
        }
        return list;
    }

    private void AddRunKey(List<StartupEntry> list, RegHive hive, string path, StartupLocation loc, string approvedSub)
    {
        IReadOnlyList<string> names;
        try { names = _reg.GetValueNames(hive, path); }
        catch { return; }
        foreach (var name in names)
        {
            if (string.IsNullOrEmpty(name)) continue;
            var cmd = _reg.GetValue(hive, path, name)?.AsString() ?? "";
            var exe = CommandLine.ExtractExecutable(Environment.ExpandEnvironmentVariables(cmd));
            list.Add(new StartupEntry
            {
                Id = $"reg:{RegistryBackup.HiveName(hive)}:{approvedSub}:{name}",
                Name = name,
                Command = cmd,
                ExecutablePath = exe,
                Location = loc,
                LocationDetail = $@"{RegistryBackup.HiveName(hive)}\{path}",
                Enabled = IsApproved(hive, approvedSub, name),
                MachineWide = hive == RegHive.LocalMachine,
                FileExists = exe is null || _fileExists(exe),
            });
        }
    }

    private void AddFolder(List<StartupEntry> list, string folder, StartupLocation loc, RegHive approvedHive)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(folder).ToList(); }
        catch { return; }
        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
            list.Add(new StartupEntry
            {
                Id = $"folder:{RegistryBackup.HiveName(approvedHive)}:{fileName}",
                Name = Path.GetFileNameWithoutExtension(file),
                Command = file,
                ExecutablePath = file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? file : null,
                Location = loc,
                LocationDetail = folder,
                Enabled = IsApproved(approvedHive, "StartupFolder", fileName),
                MachineWide = approvedHive == RegHive.LocalMachine,
                FileExists = true,
            });
        }
    }

    /// <summary>
    /// StartupApproved values are 12-byte binaries. An odd first byte means the user
    /// (or Task Manager) disabled the entry; a missing value means enabled.
    /// </summary>
    public bool IsApproved(RegHive hive, string approvedSub, string valueName)
    {
        var v = _reg.GetValue(hive, $@"{ApprovedRoot}\{approvedSub}", valueName)?.AsBytes();
        if (v is null || v.Length == 0) return true;
        return (v[0] & 0x01) == 0;
    }

    public static (RegHive Hive, string ApprovedSub, string ValueName)? ParseApprovedTarget(string entryId)
    {
        var parts = entryId.Split(':', 4);
        if (parts.Length >= 3 && parts[0] == "folder")
            return (RegistryBackup.ParseHive(parts[1]), "StartupFolder", string.Join(':', parts.Skip(2)));
        if (parts.Length == 4 && parts[0] == "reg")
            return (RegistryBackup.ParseHive(parts[1]), parts[2], parts[3]);
        return null;
    }

    public static byte[] BuildApprovedValue(bool enabled, DateTime utcNow)
    {
        var data = new byte[12];
        data[0] = enabled ? (byte)0x02 : (byte)0x03;
        if (!enabled)
        {
            var ft = BitConverter.GetBytes(utcNow.ToFileTimeUtc());
            Array.Copy(ft, 0, data, 4, 8);
        }
        return data;
    }

    /// <summary>Sets StartupApproved state, returning a backup of the previous value.</summary>
    public BackupItem SetEnabled(string entryId, bool enabled)
    {
        var target = ParseApprovedTarget(entryId) ?? throw new ArgumentException($"Entry '{entryId}' is not controlled by StartupApproved.");
        var path = $@"{ApprovedRoot}\{target.ApprovedSub}";
        var backup = RegistryBackup.Capture(_reg, target.Hive, path, target.ValueName, $"Startup approval for '{target.ValueName}'");
        _reg.SetValue(target.Hive, path, target.ValueName, new RegValue(RegKind.Binary, BuildApprovedValue(enabled, DateTime.UtcNow)));
        return backup;
    }
}

public static class CommandLine
{
    /// <summary>Extracts the executable path from a command line such as "\"C:\x\a.exe\" --min".</summary>
    public static string? ExtractExecutable(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var c = command.Trim();
        if (c.StartsWith('"'))
        {
            var end = c.IndexOf('"', 1);
            return end > 1 ? c[1..end] : null;
        }
        var lower = c.ToLowerInvariant();
        foreach (var ext in new[] { ".exe", ".com", ".bat", ".cmd", ".lnk", ".vbs", ".ps1" })
        {
            var idx = lower.IndexOf(ext, StringComparison.Ordinal);
            if (idx > 0) return c[..(idx + ext.Length)];
        }
        var space = c.IndexOf(' ');
        return space > 0 ? c[..space] : c;
    }
}

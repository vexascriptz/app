using System.Globalization;
using System.Text.RegularExpressions;
using SoftReset.Core.Platform;

namespace SoftReset.Core.Apps;

/// <summary>
/// Reads traditional desktop programs from the three Uninstall registry locations
/// that "Apps &amp; features" uses, filtering out updates, patches, and hidden system components.
/// </summary>
public sealed class UninstallRegistryReader
{
    public const string UninstallPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
    public const string UninstallPathWow64 = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

    private static readonly Regex MsiGuid = new(@"^\{[0-9A-Fa-f\-]{36}\}$", RegexOptions.Compiled);
    private readonly IRegistryAccess _reg;

    public UninstallRegistryReader(IRegistryAccess reg) => _reg = reg;

    public List<InstalledApp> Read(bool includeSystemComponents = false)
    {
        var apps = new List<InstalledApp>();
        ReadRoot(apps, RegHive.LocalMachine, UninstallPath, machineWide: true, includeSystemComponents);
        ReadRoot(apps, RegHive.LocalMachine, UninstallPathWow64, machineWide: true, includeSystemComponents);
        ReadRoot(apps, RegHive.CurrentUser, UninstallPath, machineWide: false, includeSystemComponents);

        // Same product registered in both views: keep the richer record.
        return apps
            .GroupBy(a => (a.Name.ToLowerInvariant(), a.Version.ToLowerInvariant()))
            .Select(g => g.OrderByDescending(a => a.EstimatedSizeBytes ?? 0).ThenByDescending(a => a.UninstallCommand?.Length ?? 0).First())
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void ReadRoot(List<InstalledApp> apps, RegHive hive, string root, bool machineWide, bool includeSystem)
    {
        IReadOnlyList<string> keys;
        try { keys = _reg.GetSubKeyNames(hive, root); }
        catch { return; }

        foreach (var key in keys)
        {
            var path = $@"{root}\{key}";
            string? S(string n) => _reg.GetValue(hive, path, n)?.AsString();
            int? I(string n) => _reg.GetValue(hive, path, n)?.AsInt();

            var name = S("DisplayName");
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (!includeSystem && I("SystemComponent") == 1) continue;
            if (!string.IsNullOrEmpty(S("ParentKeyName"))) continue; // updates attached to a parent product
            var releaseType = S("ReleaseType") ?? "";
            if (releaseType.Contains("Update", StringComparison.OrdinalIgnoreCase) ||
                releaseType.Contains("Hotfix", StringComparison.OrdinalIgnoreCase) ||
                releaseType.Contains("Security", StringComparison.OrdinalIgnoreCase)) continue;

            var isMsi = I("WindowsInstaller") == 1 && MsiGuid.IsMatch(key);
            var sizeKb = I("EstimatedSize");
            apps.Add(new InstalledApp
            {
                Id = $"desktop:{(machineWide ? "m" : "u")}:{key}",
                Name = name.Trim(),
                Publisher = S("Publisher")?.Trim() ?? "",
                Version = S("DisplayVersion")?.Trim() ?? "",
                Kind = I("SystemComponent") == 1 ? AppKind.SystemComponent : AppKind.Desktop,
                InstallDate = ParseInstallDate(S("InstallDate")),
                InstallLocation = NullIfEmpty(S("InstallLocation")),
                EstimatedSizeBytes = sizeKb is > 0 ? sizeKb.Value * 1024L : null,
                UninstallCommand = NullIfEmpty(S("UninstallString")),
                QuietUninstallCommand = NullIfEmpty(S("QuietUninstallString")),
                ModifyCommand = NullIfEmpty(S("ModifyPath")),
                IsMsi = isMsi,
                MsiProductCode = isMsi ? key : null,
                MachineWide = machineWide,
                RegistryKey = $@"{(hive == RegHive.LocalMachine ? "HKLM" : "HKCU")}\{path}",
                NoRemove = I("NoRemove") == 1,
                NoRepair = I("NoRepair") == 1 || !isMsi,
                DisplayIcon = NullIfEmpty(S("DisplayIcon")),
            });
        }
    }

    internal static DateTimeOffset? ParseInstallDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (DateTime.TryParseExact(s.Trim(), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return new DateTimeOffset(d);
        return null;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

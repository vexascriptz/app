using SoftReset.Core.Models;
using SoftReset.Core.Platform;

namespace SoftReset.Core.Collection;

/// <summary>
/// Configuration that Windows stores in documented registry locations. Shared by the
/// Windows collector and the simulated platform so both are analysed identically.
/// </summary>
public static class RegistryReaders
{
    public static GamingConfig ReadGaming(IRegistryAccess reg)
    {
        int? I(RegHive h, string p, string n) { try { return reg.GetValue(h, p, n)?.AsInt(); } catch { return null; } }
        string? S(RegHive h, string p, string n) { try { return reg.GetValue(h, p, n)?.AsString(); } catch { return null; } }
        var hags = I(RegHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode");
        var dx = S(RegHive.CurrentUser, @"Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings");
        return new GamingConfig
        {
            GameModeEnabled = (I(RegHive.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled") ?? 1) != 0,
            GameDvrBackgroundRecording = I(RegHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "HistoricalCaptureEnabled") == 1,
            GameDvrEnabled = (I(RegHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled") ?? 1) != 0,
            HardwareAcceleratedGpuScheduling = hags is null ? null : hags == 2,
            HagsSupported = hags is null ? null : true,
            WindowedGameOptimizations = dx is null ? null : Optimization.Actions.WindowedOptimizationsAction.GetFlag(dx, "SwapEffectUpgradeEnable") == "1",
            VariableRefreshRateOptimization = dx is null ? null : Optimization.Actions.WindowedOptimizationsAction.GetFlag(dx, "VRROptimizeEnable") == "1",
        };
    }

    public static (bool? Enabled, int? ConsentAdmin) ReadUac(IRegistryAccess reg)
    {
        const string p = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
        try
        {
            var lua = reg.GetValue(RegHive.LocalMachine, p, "EnableLUA")?.AsInt();
            var consent = reg.GetValue(RegHive.LocalMachine, p, "ConsentPromptBehaviorAdmin")?.AsInt();
            return (lua is null ? null : lua != 0, consent);
        }
        catch { return (null, null); }
    }

    public static (bool Pending, List<string> Reasons) ReadRebootPending(IRegistryAccess reg)
    {
        var reasons = new List<string>();
        try
        {
            if (reg.KeyExists(RegHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending"))
                reasons.Add("Windows component servicing (updates) is waiting for a restart");
            if (reg.KeyExists(RegHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired"))
                reasons.Add("Windows Update requires a restart");
            var pfro = reg.GetValue(RegHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager", "PendingFileRenameOperations");
            if (pfro?.Data is string[] { Length: > 0 })
                reasons.Add("Files are scheduled to be replaced at the next restart (installer or driver)");
        }
        catch { /* best effort */ }
        return (reasons.Count > 0, reasons);
    }

    public static (bool Enabled, string? Server, string? AutoConfig) ReadProxy(IRegistryAccess reg)
    {
        const string p = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
        try
        {
            return (reg.GetValue(RegHive.CurrentUser, p, "ProxyEnable")?.AsInt() == 1,
                reg.GetValue(RegHive.CurrentUser, p, "ProxyServer")?.AsString(),
                reg.GetValue(RegHive.CurrentUser, p, "AutoConfigURL")?.AsString());
        }
        catch { return (false, null, null); }
    }

    public static (bool Managed, bool Paused, DateTimeOffset? PausedUntil) ReadUpdatePolicy(IRegistryAccess reg)
    {
        try
        {
            var managed = reg.KeyExists(RegHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate");
            var pauseEnd = reg.GetValue(RegHive.LocalMachine, @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings", "PauseUpdatesExpiryTime")?.AsString();
            DateTimeOffset? until = DateTimeOffset.TryParse(pauseEnd, out var d) ? d : null;
            return (managed, until is { } u && u > DateTimeOffset.Now, until);
        }
        catch { return (false, false, null); }
    }

    public static bool? ReadFastStartup(IRegistryAccess reg)
    {
        try { return reg.GetValue(RegHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled")?.AsInt() is { } v ? v != 0 : null; }
        catch { return null; }
    }

    public static OsInfo ReadOs(IRegistryAccess reg)
    {
        const string p = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        var os = new OsInfo();
        try
        {
            os.ProductName = reg.GetValue(RegHive.LocalMachine, p, "ProductName")?.AsString() ?? "";
            os.DisplayVersion = reg.GetValue(RegHive.LocalMachine, p, "DisplayVersion")?.AsString() ?? reg.GetValue(RegHive.LocalMachine, p, "ReleaseId")?.AsString() ?? "";
            os.Edition = reg.GetValue(RegHive.LocalMachine, p, "EditionID")?.AsString() ?? "";
            os.Build = int.TryParse(reg.GetValue(RegHive.LocalMachine, p, "CurrentBuildNumber")?.AsString(), out var b) ? b : 0;
            os.Ubr = reg.GetValue(RegHive.LocalMachine, p, "UBR")?.AsInt() ?? 0;
            if (reg.GetValue(RegHive.LocalMachine, p, "InstallDate")?.AsLong() is { } secs and > 0)
                os.InstallDate = DateTimeOffset.FromUnixTimeSeconds(secs).ToLocalTime();
        }
        catch { }
        return os;
    }

    /// <summary>Parses the hosts file and returns active entries that redirect notable domains.</summary>
    public static (int Count, List<string> Notable) AnalyzeHosts(IEnumerable<string> lines)
    {
        string[] notable =
        {
            "microsoft.com", "windowsupdate", "windows.com", "live.com", "office.com", "bing.com", "google.com", "gstatic.com",
            "malwarebytes", "kaspersky", "eset", "norton", "avast", "avg.com", "bitdefender", "mcafee", "virustotal", "sophos",
            "apple.com", "amazon.com", "paypal.com", "facebook.com", "nvidia.com", "amd.com", "steampowered.com",
        };
        var count = 0;
        var list = new List<string>();
        foreach (var raw in lines)
        {
            var line = raw.Split('#')[0].Trim();
            if (line.Length == 0) continue;
            var parts = line.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            if (parts[1] is "localhost" or "localhost.localdomain" or "broadcasthost") continue;
            count++;
            if (parts.Skip(1).Any(h => notable.Any(n => h.Contains(n, StringComparison.OrdinalIgnoreCase))))
                list.Add(line);
        }
        return (count, list);
    }
}

/// <summary>
/// Heuristic indicators that a process deserves a closer look. These never classify a
/// process as malware: they only explain why a technician might want to investigate.
/// </summary>
public static class ProcessHeuristics
{
    private static readonly string[] SystemNames =
    {
        "svchost.exe", "lsass.exe", "csrss.exe", "winlogon.exe", "services.exe", "smss.exe", "wininit.exe",
        "explorer.exe", "dwm.exe", "spoolsv.exe", "taskhostw.exe", "rundll32.exe", "conhost.exe", "dllhost.exe", "RuntimeBroker.exe",
    };

    public static List<string> Evaluate(string name, string? path, SignatureState signature, IKnownFolders folders)
    {
        var reasons = new List<string>();
        if (string.IsNullOrEmpty(path)) return reasons;
        var dir = Path.GetDirectoryName(path) ?? "";
        var system32 = folders.System32;
        var windows = folders.WindowsDir;

        var mimics = SystemNames.FirstOrDefault(s => s.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (mimics is not null && !dir.StartsWith(windows, StringComparison.OrdinalIgnoreCase))
            reasons.Add($"Has the name of a Windows system process ({mimics}) but runs from outside the Windows folder");

        var lookalike = SystemNames.FirstOrDefault(s => !s.Equals(name, StringComparison.OrdinalIgnoreCase) && Levenshtein(s, name.ToLowerInvariant()) == 1 && name.Length >= 7);
        if (lookalike is not null)
            reasons.Add($"Name is one character away from the Windows process '{lookalike}'");

        var unsigned = signature is SignatureState.Unsigned or SignatureState.InvalidOrUntrusted or SignatureState.Revoked;
        var inTemp = dir.StartsWith(folders.Temp, StringComparison.OrdinalIgnoreCase) || dir.Contains(@"\Temp\", StringComparison.OrdinalIgnoreCase);
        if (inTemp && unsigned) reasons.Add("Unsigned program running from a temporary folder");
        if (dir.StartsWith(folders.Downloads, StringComparison.OrdinalIgnoreCase) && unsigned)
            reasons.Add("Unsigned program running directly from the Downloads folder");
        var roamingRoot = string.Equals(dir.TrimEnd('\\', '/'), folders.AppDataRoaming.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)
                          || string.Equals(dir.TrimEnd('\\', '/'), folders.ProgramData.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        if (roamingRoot && unsigned)
            reasons.Add("Unsigned program stored directly in the root of AppData or ProgramData");
        if (signature == SignatureState.Revoked)
            reasons.Add("The digital signature has been revoked by the issuer");
        else if (signature == SignatureState.InvalidOrUntrusted && reasons.Count == 0)
            reasons.Add("The digital signature is invalid or the file was modified after signing");
        // A single weak signal on its own is not reported.
        return reasons;
    }

    /// <summary>
    /// Optimal string alignment distance (Levenshtein plus adjacent transpositions), so that
    /// typosquatted names such as "scvhost.exe" count as one edit away from "svchost.exe".
    /// </summary>
    public static int Levenshtein(string a, string b)
    {
        a = a.ToLowerInvariant();
        b = b.ToLowerInvariant();
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
        for (var j = 1; j <= b.Length; j++)
        {
            var cost = a[i - 1] == b[j - 1] ? 0 : 1;
            d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
        }
        return d[a.Length, b.Length];
    }
}

/// <summary>Computes per-process CPU usage from two snapshots of cumulative processor time.</summary>
public sealed class ProcessCpuSampler
{
    private Dictionary<int, (TimeSpan Cpu, string Name)> _last = new();
    private DateTime _lastAt;
    private readonly int _logicalProcessors;

    public ProcessCpuSampler(int? logicalProcessors = null) => _logicalProcessors = Math.Max(1, logicalProcessors ?? Environment.ProcessorCount);

    public Dictionary<int, double> Sample(IReadOnlyList<ProcessInfo> processes, DateTime? now = null)
    {
        var at = now ?? DateTime.UtcNow;
        var result = new Dictionary<int, double>();
        var elapsed = (at - _lastAt).TotalMilliseconds * _logicalProcessors;
        var current = new Dictionary<int, (TimeSpan, string)>();
        foreach (var p in processes)
        {
            if (p.TotalProcessorTime is not { } cpu) continue;
            current[p.Pid] = (cpu, p.Name);
            if (_last.TryGetValue(p.Pid, out var prev) && prev.Name == p.Name && elapsed > 0)
                result[p.Pid] = Math.Clamp((cpu - prev.Cpu).TotalMilliseconds / elapsed * 100.0, 0, 100);
        }
        _last = current;
        _lastAt = at;
        return result;
    }
}

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using SoftReset.Core.Platform;
using SoftReset.Core.Storage;
using SoftReset.Platform.Windows.Native;

namespace SoftReset.Platform.Windows;

/// <summary>
/// Launches Windows pages and tools. Every target is validated: URIs must use an allow-listed
/// scheme, tools must be plain executable names that exist in the Windows directories, and
/// applications must be existing .exe files in standard install locations.
/// </summary>
internal sealed partial class WindowsShellService : IShellService
{
    private static readonly string[] AllowedSchemes = { "ms-settings:", "windowsdefender:", "https:", "ms-availablenetworks:", "ms-actioncenter:" };

    [GeneratedRegex(@"^[A-Za-z0-9_.-]+\.(exe|msc|cpl)$")]
    private static partial Regex SafeFileName();

    [GeneratedRegex(@"^Microsoft\.[A-Za-z0-9.]+$")]
    private static partial Regex SafeCanonicalName();

    private readonly IKnownFolders _folders;
    public WindowsShellService(IKnownFolders folders) => _folders = folders;

    public bool IsAvailable(LaunchTarget target, out string reason)
    {
        reason = "";
        switch (target.Kind)
        {
            case LaunchKind.Uri:
                if (!AllowedSchemes.Any(s => target.Target.StartsWith(s, StringComparison.OrdinalIgnoreCase))) { reason = "URI scheme is not allowed."; return false; }
                return true;
            case LaunchKind.SystemTool:
            case LaunchKind.Msc:
            case LaunchKind.ControlPanelApplet:
                if (!SafeFileName().IsMatch(target.Target)) { reason = "Invalid tool name."; return false; }
                if (ResolveSystemFile(target.Target) is null) { reason = $"{target.Target} is not present on this edition of Windows."; return false; }
                return true;
            case LaunchKind.ControlPanelItem:
                if (!SafeCanonicalName().IsMatch(target.Target)) { reason = "Invalid Control Panel name."; return false; }
                return true;
            case LaunchKind.Folder:
                if (!Directory.Exists(target.Target)) { reason = "Folder not found."; return false; }
                return true;
            case LaunchKind.RevealFile:
                if (!File.Exists(target.Target) && !Directory.Exists(target.Target)) { reason = "File not found."; return false; }
                return true;
            case LaunchKind.InstalledApplication:
                if (!target.Target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(target.Target)) { reason = "Application not found."; return false; }
                return true;
            case LaunchKind.AppsFolder:
                if (target.Target.Any(c => c is '"' or '&' or '|' or ' ')) { reason = "Invalid application id."; return false; }
                return true;
            default:
                reason = "Unsupported launch type.";
                return false;
        }
    }

    private string? ResolveSystemFile(string name)
    {
        foreach (var dir in new[] { _folders.System32, _folders.WindowsDir, Path.Combine(_folders.System32, "wbem") })
        {
            var p = Path.Combine(dir, name);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    public LaunchResult Launch(LaunchTarget target)
    {
        if (!IsAvailable(target, out var reason)) return LaunchResult.Fail(reason);
        try
        {
            ProcessStartInfo psi = target.Kind switch
            {
                LaunchKind.Uri => new ProcessStartInfo(target.Target) { UseShellExecute = true },
                LaunchKind.SystemTool => new ProcessStartInfo(ResolveSystemFile(target.Target)!, target.Arguments ?? "") { UseShellExecute = true },
                LaunchKind.Msc => new ProcessStartInfo(ResolveSystemFile(target.Target)!) { UseShellExecute = true },
                LaunchKind.ControlPanelApplet => new ProcessStartInfo(Path.Combine(_folders.System32, "control.exe"), target.Target) { UseShellExecute = true },
                LaunchKind.ControlPanelItem => new ProcessStartInfo(Path.Combine(_folders.System32, "control.exe"), $"/name {target.Target} {target.Arguments ?? ""}".Trim()) { UseShellExecute = true },
                LaunchKind.Folder => new ProcessStartInfo(Path.Combine(_folders.WindowsDir, "explorer.exe"), $"\"{target.Target}\"") { UseShellExecute = true },
                LaunchKind.RevealFile => new ProcessStartInfo(Path.Combine(_folders.WindowsDir, "explorer.exe"), $"/select,\"{target.Target}\"") { UseShellExecute = true },
                LaunchKind.InstalledApplication => new ProcessStartInfo(target.Target) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(target.Target) ?? "" },
                LaunchKind.AppsFolder => new ProcessStartInfo(Path.Combine(_folders.WindowsDir, "explorer.exe"), $"shell:AppsFolder\\{target.Target}") { UseShellExecute = true },
                _ => throw new NotSupportedException(),
            };
            Process.Start(psi)?.Dispose();
            return LaunchResult.Ok();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return LaunchResult.Fail("Cancelled at the administrator prompt.");
        }
        catch (Exception ex)
        {
            return LaunchResult.Fail(ex.Message);
        }
    }
}

/// <summary>Runs allow-listed Windows command-line tools from System32 and captures their output.</summary>
internal sealed class WindowsCommandRunner : ICommandRunner
{
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "sfc.exe", "dism.exe", "chkdsk.exe", "ipconfig.exe", "netsh.exe", "powercfg.exe", "nvidia-smi.exe", "winget", "sc.exe", "w32tm.exe", "wsreset.exe",
    };

    static WindowsCommandRunner()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static string? Resolve(string tool)
    {
        if (tool.Equals("winget", StringComparison.OrdinalIgnoreCase))
        {
            var p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "winget.exe");
            return File.Exists(p) ? p : null;
        }
        if (tool.Equals("nvidia-smi.exe", StringComparison.OrdinalIgnoreCase))
        {
            var p = Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe");
            if (File.Exists(p)) return p;
            var legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe");
            return File.Exists(legacy) ? legacy : null;
        }
        // 32-bit processes would be redirected to SysWOW64; the console is 64-bit, so System32 is native.
        var path = Path.Combine(Environment.SystemDirectory, tool);
        return File.Exists(path) ? path : null;
    }

    public async Task<CommandResult> RunAsync(string tool, string arguments, TimeSpan timeout, CancellationToken ct)
    {
        if (!Allowed.Contains(tool)) return new CommandResult { Started = false, StartError = $"{tool} is not on the console's allow-list.", ExitCode = -1 };
        var exe = Resolve(tool);
        if (exe is null) return new CommandResult { Started = false, StartError = $"{tool} was not found on this PC.", ExitCode = -1 };

        var unicodeOutput = tool.Equals("sfc.exe", StringComparison.OrdinalIgnoreCase);
        var oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        var psi = new ProcessStartInfo(exe, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = unicodeOutput ? Encoding.Unicode : tool == "winget" ? Encoding.UTF8 : oem,
            StandardErrorEncoding = unicodeOutput ? Encoding.Unicode : tool == "winget" ? Encoding.UTF8 : oem,
        };
        using var p = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (stdout) stdout.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (stderr) stderr.AppendLine(e.Data); };
        try { p.Start(); }
        catch (Exception ex) { return new CommandResult { Started = false, StartError = ex.Message, ExitCode = -1 }; }
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        var timedOut = false;
        try { await p.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException)
        {
            timedOut = !ct.IsCancellationRequested;
            try { p.Kill(entireProcessTree: true); } catch { }
            if (ct.IsCancellationRequested) throw;
        }
        p.WaitForExit(2000);
        return new CommandResult
        {
            ExitCode = timedOut ? -1 : p.ExitCode,
            StdOut = stdout.ToString().Replace("\0", ""),
            StdErr = stderr.ToString().Replace("\0", ""),
            TimedOut = timedOut,
        };
    }
}

internal sealed class WindowsMaintenanceService : IMaintenanceService
{
    public bool? IsSystemRestoreEnabled()
    {
        var reg = new WindowsRegistry();
        const string key = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore";
        // Windows Server editions don't include System Restore at all (found on a real Windows Server 2025 run).
        if (!reg.KeyExists(RegHive.LocalMachine, key) || !SystemRestoreClassExists()) return false;
        var disabledByPolicy = reg.GetValue(RegHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore", "DisableSR")?.AsInt() == 1;
        if (disabledByPolicy) return false;
        var v = reg.GetValue(RegHive.LocalMachine, key, "RPSessionInterval")?.AsInt();
        return v switch { 1 => true, 0 => false, _ => null };
    }

    private static bool SystemRestoreClassExists()
    {
        try
        {
            using var cls = new System.Management.ManagementClass(new System.Management.ManagementScope(@"\\.\root\default"), new System.Management.ManagementPath("SystemRestore"), null);
            cls.Get();
            return true;
        }
        catch (System.Management.ManagementException ex) when (ex.ErrorCode is System.Management.ManagementStatus.NotFound or System.Management.ManagementStatus.InvalidClass or System.Management.ManagementStatus.InvalidNamespace)
        {
            return false;
        }
        catch { return true; } // unknown: let the creation attempt report the real error
    }

    public RestorePointResult CreateRestorePoint(string description)
    {
        var before = CountRestorePoints();
        try
        {
            using var cls = new System.Management.ManagementClass(new System.Management.ManagementScope(@"\\.\root\default"), new System.Management.ManagementPath("SystemRestore"), null);
            using var inParams = cls.GetMethodParameters("CreateRestorePoint");
            inParams["Description"] = description.Length > 64 ? description[..64] : description;
            inParams["RestorePointType"] = 12;   // MODIFY_SETTINGS
            inParams["EventType"] = 100;         // BEGIN_SYSTEM_CHANGE
            using var outParams = cls.InvokeMethod("CreateRestorePoint", inParams, null);
            var rc = Convert.ToUInt32(outParams["ReturnValue"]);
            if (rc != 0) return new RestorePointResult { Created = false, Message = rc == 1058 ? "System Restore is disabled (service or System Protection off)." : $"Windows could not create the restore point (code {rc})." };
        }
        catch (UnauthorizedAccessException) { throw; }
        catch (System.Management.ManagementException ex) when (ex.ErrorCode == System.Management.ManagementStatus.AccessDenied)
        {
            throw new UnauthorizedAccessException("Creating a restore point requires administrator rights.");
        }
        catch (System.Management.ManagementException ex) when (ex.ErrorCode is System.Management.ManagementStatus.NotFound or System.Management.ManagementStatus.InvalidClass or System.Management.ManagementStatus.InvalidNamespace)
        {
            return new RestorePointResult { Created = false, Message = "System Restore isn't available on this edition of Windows." };
        }
        var after = CountRestorePoints();
        if (before is not null && after is not null && after <= before)
            return new RestorePointResult { Skipped = true, Message = "Windows skipped creating a new restore point because one was created in the last 24 hours. The existing recent restore point can be used." };
        return new RestorePointResult { Created = true, Message = $"Restore point \"{description}\" created." };
    }

    private static int? CountRestorePoints()
    {
        var list = Wmi.Query(@"root\default", "SELECT SequenceNumber FROM SystemRestore", out var err, 20);
        return err is null ? list.Count : null;
    }

    public (long Bytes, long Items)? GetRecycleBinInfo()
    {
        var info = new Win32.SHQUERYRBINFO { cbSize = Marshal.SizeOf<Win32.SHQUERYRBINFO>() };
        return Win32.SHQueryRecycleBinW(null, ref info) == 0 ? (info.i64Size, info.i64NumItems) : null;
    }

    public bool EmptyRecycleBin(out string error)
    {
        var hr = Win32.SHEmptyRecycleBinW(IntPtr.Zero, null, Win32.SHERB_NOCONFIRMATION | Win32.SHERB_NOPROGRESSUI | Win32.SHERB_NOSOUND);
        // S_OK, or E_UNEXPECTED when the bin is already empty.
        error = hr is 0 or unchecked((int)0x8000FFFF) ? "" : $"SHEmptyRecycleBin failed (0x{hr:X8}).";
        return error.Length == 0;
    }

    public bool SendToRecycleBin(string path, out string error)
    {
        try
        {
            if (File.Exists(path))
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            else if (Directory.Exists(path))
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            else { error = "Not found."; return false; }
            error = "";
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    public void BroadcastSettingsChanged()
    {
        try
        {
            Win32.SendMessageTimeoutW(Win32.HWND_BROADCAST, Win32.WM_SETTINGCHANGE, IntPtr.Zero, "ImmersiveColorSet", Win32.SMTO_ABORTIFHUNG, 1000, out _);
            Win32.SendMessageTimeoutW(Win32.HWND_BROADCAST, Win32.WM_SETTINGCHANGE, IntPtr.Zero, null, Win32.SMTO_ABORTIFHUNG, 1000, out _);
            Win32.InternetSetOptionW(IntPtr.Zero, Win32.INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
            Win32.InternetSetOptionW(IntPtr.Zero, Win32.INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
        }
        catch { }
    }

    public bool RestartExplorer(out string error)
    {
        try
        {
            var session = Process.GetCurrentProcess().SessionId;
            foreach (var p in Process.GetProcessesByName("explorer").Where(p => p.SessionId == session))
            {
                using (p) { p.Kill(); p.WaitForExit(5000); }
            }
            Thread.Sleep(2500);
            if (!Process.GetProcessesByName("explorer").Any(p => p.SessionId == session))
                Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = true });
            error = "";
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    public bool? GetMouseAcceleration()
    {
        var v = new int[3];
        return Win32.SystemParametersInfoW(Win32.SPI_GETMOUSE, 0, v, 0) ? v[2] != 0 : null;
    }

    public bool SetMouseAcceleration(bool enabled, out string error)
    {
        var v = enabled ? new[] { 6, 10, 1 } : new[] { 0, 0, 0 };
        var ok = Win32.SystemParametersInfoW(Win32.SPI_SETMOUSE, 0, v, Win32.SPIF_UPDATEINIFILE | Win32.SPIF_SENDCHANGE);
        error = ok ? "" : $"SystemParametersInfo failed ({Marshal.GetLastWin32Error()}).";
        return ok;
    }

    private static readonly Dictionary<string, (uint Get, uint Set)> BoolSpi = new()
    {
        [UiEffects.ClientAreaAnimation] = (0x1042, 0x1043),
        [UiEffects.MenuAnimation] = (0x1002, 0x1003),
        [UiEffects.TooltipAnimation] = (0x1016, 0x1017),
        [UiEffects.ComboBoxAnimation] = (0x1004, 0x1005),
        [UiEffects.ListBoxSmoothScrolling] = (0x1006, 0x1007),
        [UiEffects.SelectionFade] = (0x1014, 0x1015),
        [UiEffects.CursorShadow] = (0x101A, 0x101B),
        [UiEffects.DropShadow] = (0x1024, 0x1025),
    };

    public bool? GetUiEffect(string name)
    {
        if (name == UiEffects.WindowAnimation)
        {
            var ai = new Win32.ANIMATIONINFO { cbSize = (uint)Marshal.SizeOf<Win32.ANIMATIONINFO>() };
            return Win32.SystemParametersInfoW(Win32.SPI_GETANIMATION, ai.cbSize, ref ai, 0) ? ai.iMinAnimate != 0 : null;
        }
        var spi = name == UiEffects.DragFullWindows ? Win32.SPI_GETDRAGFULLWINDOWS : BoolSpi.TryGetValue(name, out var c) ? c.Get : 0u;
        if (spi == 0) return null;
        var mem = Marshal.AllocHGlobal(4);
        try
        {
            Marshal.WriteInt32(mem, 0);
            return Win32.SystemParametersInfoW(spi, 0, mem, 0) ? Marshal.ReadInt32(mem) != 0 : null;
        }
        finally { Marshal.FreeHGlobal(mem); }
    }

    public bool SetUiEffect(string name, bool enabled, out string error)
    {
        const uint flags = Win32.SPIF_UPDATEINIFILE | Win32.SPIF_SENDCHANGE;
        bool ok;
        if (name == UiEffects.WindowAnimation)
        {
            var ai = new Win32.ANIMATIONINFO { cbSize = (uint)Marshal.SizeOf<Win32.ANIMATIONINFO>(), iMinAnimate = enabled ? 1 : 0 };
            ok = Win32.SystemParametersInfoW(Win32.SPI_SETANIMATION, ai.cbSize, ref ai, flags);
        }
        else if (name == UiEffects.DragFullWindows)
        {
            ok = Win32.SystemParametersInfoW(Win32.SPI_SETDRAGFULLWINDOWS, enabled ? 1u : 0u, IntPtr.Zero, flags);
        }
        else if (BoolSpi.TryGetValue(name, out var c))
        {
            ok = Win32.SystemParametersInfoW(c.Set, 0, new IntPtr(enabled ? 1 : 0), flags);
        }
        else { error = $"Unknown effect {name}"; return false; }
        error = ok ? "" : $"SystemParametersInfo failed ({Marshal.GetLastWin32Error()}).";
        return ok;
    }
}

/// <summary>Runs one approved action in a short-lived elevated copy of the console (UAC prompt).</summary>
internal sealed class WindowsElevationService : IElevationService
{
    public bool IsElevated
    {
        get
        {
            try
            {
                using var id = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }

    public bool CanElevate => Environment.ProcessPath is not null;

    public async Task<ElevatedResult> RunElevatedAsync(ElevatedRequest request, CancellationToken ct)
    {
        var exe = Environment.ProcessPath;
        if (exe is null) return new ElevatedResult { Launched = false, Message = "Cannot determine the console's executable path." };
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(Json.Serialize(request, indented: false)));
        var psi = new ProcessStartInfo(exe, $"--elevated-run {payload}") { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
        Process? p;
        try { p = Process.Start(psi); }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new ElevatedResult { Launched = false, UserCancelled = true, Message = "The administrator prompt was declined." };
        }
        catch (Exception ex) { return new ElevatedResult { Launched = false, Message = ex.Message }; }
        if (p is null) return new ElevatedResult { Launched = false, Message = "The elevated helper did not start." };
        using (p)
        {
            await p.WaitForExitAsync(ct);
            var json = File.Exists(request.ResultFile) ? await File.ReadAllTextAsync(request.ResultFile, ct) : null;
            return new ElevatedResult { Launched = true, ExitCode = p.ExitCode, ResultJson = json };
        }
    }

    public bool RelaunchElevated(out string error)
    {
        try
        {
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Unknown executable path.");
            Process.Start(new ProcessStartInfo(exe, "--relaunched-elevated") { UseShellExecute = true, Verb = "runas" })?.Dispose();
            error = "";
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { error = "The administrator prompt was declined."; return false; }
        catch (Exception ex) { error = ex.Message; return false; }
    }
}

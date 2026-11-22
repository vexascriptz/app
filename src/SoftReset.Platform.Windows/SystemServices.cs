using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using SoftReset.Core.Models;
using SoftReset.Core.Platform;
using SoftReset.Core.Safety;
using SoftReset.Platform.Windows.Native;

namespace SoftReset.Platform.Windows;

internal sealed class WindowsProcessService : IProcessService
{
    public IReadOnlyList<ProcessInfo> Enumerate()
    {
        var parents = ParentMap();
        var list = new List<ProcessInfo>();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                var info = new ProcessInfo { Pid = p.Id, Name = p.Id == 0 ? "Idle" : p.ProcessName };
                try
                {
                    info.SessionId = p.SessionId;
                    info.Threads = p.Threads.Count;
                    info.Handles = p.HandleCount;
                    info.WorkingSetBytes = p.WorkingSet64;
                    info.PrivateBytes = p.PrivateMemorySize64;
                }
                catch { /* exited or protected */ }
                if (parents.TryGetValue(p.Id, out var pe))
                {
                    info.ParentPid = pe.Parent;
                    info.Name = string.IsNullOrEmpty(pe.Exe) ? info.Name : pe.Exe;
                    if (info.Threads == 0) info.Threads = pe.Threads;
                }
                else if (!info.Name.Contains('.') && p.Id > 4) info.Name += ".exe";

                var h = Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, p.Id);
                if (h != IntPtr.Zero)
                {
                    try
                    {
                        info.Path = ImagePath(h);
                        info.PathAccessible = info.Path is not null;
                        if (Win32.GetProcessTimes(h, out var created, out _, out var kernel, out var user))
                        {
                            info.TotalProcessorTime = TimeSpan.FromTicks((long)(kernel.Value + user.Value));
                            if (created.Value > 0) info.StartTime = DateTimeOffset.FromFileTime((long)created.Value);
                        }
                    }
                    finally { Win32.CloseHandle(h); }
                }
                list.Add(info);
            }
        }
        return list;
    }

    private static unsafe string? ImagePath(IntPtr h)
    {
        var buf = stackalloc char[1024];
        uint size = 1024;
        return Win32.QueryFullProcessImageName(h, 0, buf, ref size) ? new string(buf, 0, (int)size) : null;
    }

    private static Dictionary<int, (int Parent, string Exe, int Threads)> ParentMap()
    {
        var map = new Dictionary<int, (int, string, int)>();
        var snap = Win32.CreateToolhelp32Snapshot(Win32.TH32CS_SNAPPROCESS, 0);
        if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return map;
        try
        {
            var e = new Win32.PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<Win32.PROCESSENTRY32W>() };
            if (!Win32.Process32FirstW(snap, ref e)) return map;
            do { map[(int)e.th32ProcessID] = ((int)e.th32ParentProcessID, e.szExeFile, (int)e.cntThreads); }
            while (Win32.Process32NextW(snap, ref e));
        }
        finally { Win32.CloseHandle(snap); }
        return map;
    }

    public FileDetails GetFileDetails(string path, bool checkSignature)
    {
        var d = new FileDetails();
        try
        {
            var v = FileVersionInfo.GetVersionInfo(path);
            d.Description = string.IsNullOrWhiteSpace(v.FileDescription) ? null : v.FileDescription.Trim();
            d.Company = string.IsNullOrWhiteSpace(v.CompanyName) ? null : v.CompanyName.Trim();
            d.Product = v.ProductName;
            d.FileVersion = v.FileVersion;
        }
        catch { }
        if (checkSignature)
        {
            var (state, signer, catalog) = SignatureVerifier.Verify(path);
            d.Signature = state;
            d.Signer = signer;
            d.CatalogSigned = catalog;
        }
        return d;
    }

    public IReadOnlyDictionary<int, IReadOnlyList<string>> GetServicesByPid()
    {
        var map = new Dictionary<int, List<string>>();
        foreach (var o in Wmi.Query("SELECT Name, ProcessId FROM Win32_Service WHERE ProcessId <> 0"))
        {
            var pid = o.I("ProcessId") ?? 0;
            if (!map.TryGetValue(pid, out var l)) map[pid] = l = new List<string>();
            l.Add(o.S("Name") ?? "");
        }
        return map.ToDictionary(k => k.Key, k => (IReadOnlyList<string>)k.Value);
    }

    public bool TryTerminate(int pid, out string error)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            var name = p.ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? p.ProcessName : p.ProcessName + ".exe";
            if (ProtectedComponents.IsCriticalProcess(name) || ProtectedComponents.IsCriticalProcess(p.ProcessName))
            {
                error = "Protected Windows process.";
                return false;
            }
            if (p.Id == Environment.ProcessId) { error = "Cannot end the console itself."; return false; }
            p.Kill();
            p.WaitForExit(5000);
            error = "";
            return true;
        }
        catch (ArgumentException) { error = "Process already exited."; return true; }
        catch (Exception ex) { error = ex.Message; return false; }
    }
}

internal sealed class WindowsServiceManager : IServiceManager
{
    public IReadOnlyList<ServiceInfo> GetServices()
    {
        var list = new List<ServiceInfo>();
        foreach (var o in Wmi.Query("SELECT Name, DisplayName, Description, State, StartMode, DelayedAutoStart, PathName, ProcessId, StartName, ExitCode FROM Win32_Service"))
            list.Add(Map(o));
        if (list.Count == 0)
        {
            foreach (var sc in ServiceController.GetServices())
            {
                using (sc)
                {
                    list.Add(new ServiceInfo
                    {
                        Name = sc.ServiceName, DisplayName = sc.DisplayName, State = sc.Status.ToString(),
                        StartMode = sc.StartType switch { ServiceStartMode.Automatic => "Auto", ServiceStartMode.Manual => "Manual", ServiceStartMode.Disabled => "Disabled", ServiceStartMode.Boot => "Boot", _ => "System" },
                    });
                }
            }
        }
        return list;
    }

    private static ServiceInfo Map(System.Management.ManagementBaseObject o)
    {
        var path = o.S("PathName");
        return new ServiceInfo
        {
            Name = o.S("Name") ?? "",
            DisplayName = o.S("DisplayName") ?? "",
            Description = o.S("Description") ?? "",
            State = o.S("State") ?? "",
            StartMode = o.S("StartMode") ?? "",
            DelayedAutoStart = o.B("DelayedAutoStart") ?? false,
            PathName = path,
            ProcessId = o.I("ProcessId") ?? 0,
            Account = o.S("StartName"),
            ExitCode = o.I("ExitCode"),
            IsMicrosoft = path is not null && path.Contains(@"\Windows\", StringComparison.OrdinalIgnoreCase),
        };
    }

    public ServiceInfo? Get(string name)
    {
        var safe = name.Replace("'", "''");
        var o = Wmi.Query($"SELECT Name, DisplayName, Description, State, StartMode, DelayedAutoStart, PathName, ProcessId, StartName, ExitCode FROM Win32_Service WHERE Name = '{safe}'").FirstOrDefault();
        return o is null ? null : Map(o);
    }

    public void SetStartMode(string name, string startMode, bool delayedAutoStart)
    {
        var start = startMode switch
        {
            "Auto" => Win32.SERVICE_AUTO_START,
            "Manual" => Win32.SERVICE_DEMAND_START,
            "Disabled" => Win32.SERVICE_DISABLED,
            _ => throw new ArgumentException($"Unsupported start mode {startMode}"),
        };
        var scm = Win32.OpenSCManagerW(null, null, Win32.SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero) throw new UnauthorizedAccessException($"Cannot open the Service Control Manager (error {Marshal.GetLastWin32Error()}).");
        try
        {
            var svc = Win32.OpenServiceW(scm, name, Win32.SERVICE_CHANGE_CONFIG | Win32.SERVICE_QUERY_CONFIG);
            if (svc == IntPtr.Zero)
            {
                var err = Marshal.GetLastWin32Error();
                if (err == 5) throw new UnauthorizedAccessException("Changing this service requires administrator rights.");
                throw new InvalidOperationException($"Cannot open service {name} (error {err}).");
            }
            try
            {
                if (!Win32.ChangeServiceConfigW(svc, Win32.SERVICE_NO_CHANGE, start, Win32.SERVICE_NO_CHANGE, null, null, IntPtr.Zero, null, null, null, null))
                    throw new InvalidOperationException($"ChangeServiceConfig failed (error {Marshal.GetLastWin32Error()}).");
                if (startMode == "Auto")
                {
                    var info = new Win32.SERVICE_DELAYED_AUTO_START_INFO { fDelayedAutostart = delayedAutoStart ? 1 : 0 };
                    Win32.ChangeServiceConfig2W(svc, Win32.SERVICE_CONFIG_DELAYED_AUTO_START_INFO, ref info);
                }
            }
            finally { Win32.CloseServiceHandle(svc); }
        }
        finally { Win32.CloseServiceHandle(scm); }
    }

    public void Start(string name, TimeSpan timeout)
    {
        using var sc = new ServiceController(name);
        if (sc.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending) return;
        sc.Start();
        sc.WaitForStatus(ServiceControllerStatus.Running, timeout);
    }

    public void Stop(string name, TimeSpan timeout)
    {
        using var sc = new ServiceController(name);
        if (sc.Status is ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending) return;
        sc.Stop();
        sc.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
    }
}

/// <summary>Task Scheduler through its documented COM API (Schedule.Service).</summary>
internal sealed class WindowsTaskService : IScheduledTaskService
{
    private const int TASK_TRIGGER_BOOT = 8, TASK_TRIGGER_LOGON = 9, TASK_ACTION_EXEC = 0, TASK_ENUM_HIDDEN = 1;

    public IReadOnlyList<ScheduledTaskInfo> GetTasks()
    {
        var list = new List<ScheduledTaskInfo>();
        var type = Type.GetTypeFromProgID("Schedule.Service");
        if (type is null) return list;
        dynamic service = Activator.CreateInstance(type)!;
        try
        {
            service.Connect();
            Walk(service.GetFolder("\\"), list, 0);
        }
        finally { Marshal.FinalReleaseComObject(service); }
        return list;
    }

    private static void Walk(dynamic folder, List<ScheduledTaskInfo> list, int depth)
    {
        if (depth > 12) return;
        try
        {
            foreach (dynamic t in folder.GetTasks(TASK_ENUM_HIDDEN))
            {
                try { list.Add(Map(t)); } catch { }
            }
        }
        catch { /* folder access denied */ }
        try
        {
            foreach (dynamic sub in folder.GetFolders(0)) Walk(sub, list, depth + 1);
        }
        catch { }
    }

    private static ScheduledTaskInfo Map(dynamic t)
    {
        var info = new ScheduledTaskInfo
        {
            Path = (string)t.Path,
            Name = (string)t.Name,
            Enabled = (bool)t.Enabled,
            State = ((int)t.State) switch { 1 => "Disabled", 2 => "Queued", 3 => "Ready", 4 => "Running", _ => "Unknown" },
            LastResult = $"0x{(int)t.LastTaskResult:X8}",
        };
        try { var lr = (DateTime)t.LastRunTime; if (lr.Year > 2000) info.LastRun = lr; } catch { }
        try { var nr = (DateTime)t.NextRunTime; if (nr.Year > 2000) info.NextRun = nr; } catch { }
        try
        {
            dynamic def = t.Definition;
            info.Author = (string?)def.RegistrationInfo.Author ?? "";
            var triggers = new List<string>();
            foreach (dynamic trig in def.Triggers)
            {
                int tt = trig.Type;
                if (tt == TASK_TRIGGER_LOGON) { info.HasLogonTrigger = true; triggers.Add("At log on"); }
                else if (tt == TASK_TRIGGER_BOOT) { info.HasBootTrigger = true; triggers.Add("At startup"); }
                else triggers.Add(tt switch { 1 => "One time", 2 => "Daily", 3 => "Weekly", 4 => "Monthly", 6 => "On idle", 0 => "On event", _ => $"Trigger {tt}" });
            }
            info.Triggers = string.Join(", ", triggers);
            foreach (dynamic a in def.Actions)
            {
                if ((int)a.Type == TASK_ACTION_EXEC) { info.Action = $"{(string)a.Path} {(string?)a.Arguments}".Trim(); break; }
            }
        }
        catch { }
        return info;
    }

    public void SetEnabled(string taskPath, bool enabled)
    {
        var type = Type.GetTypeFromProgID("Schedule.Service") ?? throw new InvalidOperationException("Task Scheduler is unavailable.");
        dynamic service = Activator.CreateInstance(type)!;
        try
        {
            service.Connect();
            var folderPath = taskPath[..(taskPath.LastIndexOf('\\') is var i && i > 0 ? i : 1)];
            var name = taskPath[(taskPath.LastIndexOf('\\') + 1)..];
            dynamic folder = service.GetFolder(folderPath.Length == 0 ? "\\" : folderPath);
            dynamic task = folder.GetTask(name);
            task.Enabled = enabled;
        }
        catch (COMException ex) when ((uint)ex.HResult == 0x80070005)
        {
            throw new UnauthorizedAccessException("Changing this task requires administrator rights.");
        }
        finally { Marshal.FinalReleaseComObject(service); }
    }
}

internal sealed class WindowsPowerService : IPowerService
{
    private readonly WindowsCommandRunner _commands;
    public WindowsPowerService(WindowsCommandRunner commands) => _commands = commands;

    public PowerStatus GetStatus()
    {
        var st = new PowerStatus();
        if (Win32.PowerGetActiveScheme(IntPtr.Zero, out var activePtr) == 0)
        {
            var active = Marshal.PtrToStructure<Guid>(activePtr);
            Win32.LocalFree(activePtr);
            st.ActiveSchemeGuid = active.ToString();
            st.ActiveSchemeName = FriendlyName(active);
        }
        for (uint i = 0; i < 64; i++)
        {
            uint size = 16;
            var buf = Marshal.AllocHGlobal(16);
            try
            {
                if (Win32.PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 16 /*ACCESS_SCHEME*/, i, buf, ref size) != 0) break;
                var g = Marshal.PtrToStructure<Guid>(buf);
                st.Schemes.Add(new PowerScheme { Guid = g.ToString(), Name = FriendlyName(g), Active = g.ToString() == st.ActiveSchemeGuid });
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        if (Win32.GetSystemPowerStatus(out var ps))
        {
            st.OnAcPower = ps.ACLineStatus switch { 1 => true, 0 => false, _ => null };
            st.HasBattery = ps.BatteryFlag == 255 ? null : (ps.BatteryFlag & 128) == 0;
            st.BatterySaverOn = ps.SystemStatusFlag == 1;
        }
        if (!string.IsNullOrEmpty(st.ActiveSchemeGuid))
        {
            st.ProcessorMinStateAc = GetSettingIndex(st.ActiveSchemeGuid, PowerSettingIds.SubProcessor, PowerSettingIds.ProcThrottleMin, true);
            st.ProcessorMaxStateAc = GetSettingIndex(st.ActiveSchemeGuid, PowerSettingIds.SubProcessor, PowerSettingIds.ProcThrottleMax, true);
            st.ProcessorMaxStateDc = GetSettingIndex(st.ActiveSchemeGuid, PowerSettingIds.SubProcessor, PowerSettingIds.ProcThrottleMax, false);
        }
        var caps = Marshal.AllocHGlobal(256);
        try
        {
            if (Win32.CallNtPowerInformation(Win32.SystemPowerCapabilities, IntPtr.Zero, 0, caps, 256) == 0)
            {
                st.HibernationEnabled = Marshal.ReadByte(caps, 8) != 0;  // HiberFilePresent
                st.ModernStandby = Marshal.ReadByte(caps, 20) != 0;      // AoAc
            }
        }
        finally { Marshal.FreeHGlobal(caps); }
        st.FastStartupEnabled = Core.Collection.RegistryReaders.ReadFastStartup(new WindowsRegistry()) is { } fs ? fs && st.HibernationEnabled != false : null;
        return st;
    }

    private static string FriendlyName(Guid scheme)
    {
        uint size = 0;
        Win32.PowerReadFriendlyName(IntPtr.Zero, scheme, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size);
        if (size == 0) return scheme.ToString();
        var buf = Marshal.AllocHGlobal((int)size);
        try
        {
            return Win32.PowerReadFriendlyName(IntPtr.Zero, scheme, IntPtr.Zero, IntPtr.Zero, buf, ref size) == 0
                ? Marshal.PtrToStringUni(buf) ?? scheme.ToString()
                : scheme.ToString();
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    public void SetActiveScheme(string guid)
    {
        var rc = Win32.PowerSetActiveScheme(IntPtr.Zero, Guid.Parse(guid));
        if (rc != 0) throw new InvalidOperationException($"PowerSetActiveScheme failed ({rc}).");
    }

    public string DuplicateScheme(string sourceGuid)
    {
        var rc = Win32.PowerDuplicateScheme(IntPtr.Zero, Guid.Parse(sourceGuid), out var dest);
        if (rc != 0) throw new InvalidOperationException($"This PC does not provide that power plan (PowerDuplicateScheme error {rc}).");
        var g = Marshal.PtrToStructure<Guid>(dest);
        Win32.LocalFree(dest);
        return g.ToString();
    }

    public void DeleteScheme(string guid)
    {
        var rc = Win32.PowerDeleteScheme(IntPtr.Zero, Guid.Parse(guid));
        if (rc != 0) throw new InvalidOperationException($"PowerDeleteScheme failed ({rc}).");
    }

    public int? GetSettingIndex(string schemeGuid, string subgroupGuid, string settingGuid, bool ac)
    {
        uint v;
        var rc = ac
            ? Win32.PowerReadACValueIndex(IntPtr.Zero, Guid.Parse(schemeGuid), Guid.Parse(subgroupGuid), Guid.Parse(settingGuid), out v)
            : Win32.PowerReadDCValueIndex(IntPtr.Zero, Guid.Parse(schemeGuid), Guid.Parse(subgroupGuid), Guid.Parse(settingGuid), out v);
        return rc == 0 ? (int)v : null;
    }

    public void SetSettingIndex(string schemeGuid, string subgroupGuid, string settingGuid, bool ac, int value)
    {
        var rc = ac
            ? Win32.PowerWriteACValueIndex(IntPtr.Zero, Guid.Parse(schemeGuid), Guid.Parse(subgroupGuid), Guid.Parse(settingGuid), (uint)value)
            : Win32.PowerWriteDCValueIndex(IntPtr.Zero, Guid.Parse(schemeGuid), Guid.Parse(subgroupGuid), Guid.Parse(settingGuid), (uint)value);
        if (rc == 5) throw new UnauthorizedAccessException("Writing this power setting requires administrator rights.");
        if (rc != 0) throw new InvalidOperationException($"Writing the power setting failed ({rc}).");
    }

    public void SetHibernation(bool enabled)
    {
        var r = _commands.RunAsync("powercfg.exe", enabled ? "/hibernate on" : "/hibernate off", TimeSpan.FromSeconds(60), CancellationToken.None).GetAwaiter().GetResult();
        if (!r.Success) throw new InvalidOperationException($"powercfg failed (exit {r.ExitCode}): {r.StdErr}{r.StdOut}".Trim());
    }
}

internal sealed class WindowsDisplayService : IDisplayService
{
    public IReadOnlyList<DisplayInfo> GetDisplays()
    {
        var result = new List<DisplayInfo>();
        var friendly = MonitorFriendlyNames();
        var dpi = MonitorDpi();
        for (var i = 0; i < 16; i++)
        {
            var adapter = new Win32.DISPLAY_DEVICE { cb = Marshal.SizeOf<Win32.DISPLAY_DEVICE>() };
            if (!Win32.EnumDisplayDevicesW(null, i, ref adapter, 0)) break;
            if ((adapter.StateFlags & Win32.DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0 || (adapter.StateFlags & Win32.DISPLAY_DEVICE_MIRRORING_DRIVER) != 0) continue;
            var current = NewDevMode();
            if (!Win32.EnumDisplaySettingsExW(adapter.DeviceName, Win32.ENUM_CURRENT_SETTINGS, ref current, 0)) continue;
            var monitor = new Win32.DISPLAY_DEVICE { cb = Marshal.SizeOf<Win32.DISPLAY_DEVICE>() };
            var monitorName = "";
            if (Win32.EnumDisplayDevicesW(adapter.DeviceName, 0, ref monitor, 0))
            {
                monitorName = monitor.DeviceString;
                var hwId = monitor.DeviceID.Split('\\').Skip(1).FirstOrDefault();
                if (hwId is not null && friendly.TryGetValue(hwId, out var nice)) monitorName = nice;
            }
            var info = new DisplayInfo
            {
                DeviceName = adapter.DeviceName,
                AdapterName = adapter.DeviceString,
                MonitorName = monitorName,
                Width = current.dmPelsWidth,
                Height = current.dmPelsHeight,
                RefreshHz = current.dmDisplayFrequency,
                BitsPerPixel = current.dmBitsPerPel,
                IsPrimary = (adapter.StateFlags & Win32.DISPLAY_DEVICE_PRIMARY_DEVICE) != 0,
                ScalePercent = dpi.TryGetValue(adapter.DeviceName, out var d) ? (int)Math.Round(d / 96.0 * 100) : null,
            };
            var modes = new HashSet<DisplayMode>();
            for (var m = 0; m < 4096; m++)
            {
                var dm = NewDevMode();
                if (!Win32.EnumDisplaySettingsExW(adapter.DeviceName, m, ref dm, 0)) break;
                if (dm.dmBitsPerPel < 24 || dm.dmDisplayFrequency <= 1) continue;
                modes.Add(new DisplayMode(dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency, dm.dmBitsPerPel));
            }
            info.SupportedModes = modes.OrderByDescending(x => x.Width * x.Height).ThenByDescending(x => x.RefreshHz).ToList();
            info.MaxRefreshHzAtCurrentResolution = info.SupportedModes.Where(x => x.Width == info.Width && x.Height == info.Height).Select(x => x.RefreshHz).DefaultIfEmpty(info.RefreshHz).Max();
            result.Add(info);
        }
        return result;
    }

    private static Win32.DEVMODE NewDevMode() => new() { dmSize = (short)Marshal.SizeOf<Win32.DEVMODE>(), dmDeviceName = "", dmFormName = "" };

    public DisplayChangeResult SetMode(string deviceName, DisplayMode mode, bool testOnly)
    {
        var dm = NewDevMode();
        if (!Win32.EnumDisplaySettingsExW(deviceName, Win32.ENUM_CURRENT_SETTINGS, ref dm, 0))
            return new DisplayChangeResult { Success = false, Message = "Display not found." };
        dm.dmPelsWidth = mode.Width;
        dm.dmPelsHeight = mode.Height;
        dm.dmDisplayFrequency = mode.RefreshHz;
        if (mode.BitsPerPixel > 0) dm.dmBitsPerPel = mode.BitsPerPixel;
        dm.dmFields = Win32.DM_PELSWIDTH | Win32.DM_PELSHEIGHT | Win32.DM_DISPLAYFREQUENCY | Win32.DM_BITSPERPEL;
        var rc = Win32.ChangeDisplaySettingsExW(deviceName, ref dm, IntPtr.Zero, testOnly ? Win32.CDS_TEST : Win32.CDS_UPDATEREGISTRY, IntPtr.Zero);
        return rc switch
        {
            Win32.DISP_CHANGE_SUCCESSFUL => new DisplayChangeResult { Success = true, Message = testOnly ? "The display driver accepted the mode." : "Mode applied." },
            Win32.DISP_CHANGE_RESTART => new DisplayChangeResult { Success = true, RestartRequired = true, Message = "A restart is required for the mode to take effect." },
            -2 => new DisplayChangeResult { Success = false, Message = "The display driver rejected this mode (DISP_CHANGE_BADMODE)." },
            -1 => new DisplayChangeResult { Success = false, Message = "The display driver failed to apply the mode (DISP_CHANGE_FAILED)." },
            _ => new DisplayChangeResult { Success = false, Message = $"ChangeDisplaySettingsEx returned {rc}." },
        };
    }

    private static Dictionary<string, string> MonitorFriendlyNames()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in Wmi.Query(@"root\wmi", "SELECT InstanceName, UserFriendlyName FROM WmiMonitorID", out _, 10))
        {
            try
            {
                var inst = o.S("InstanceName") ?? "";
                var hw = inst.Split('\\').Skip(1).FirstOrDefault();
                if (hw is null || o["UserFriendlyName"] is not ushort[] chars) continue;
                var name = new string(chars.TakeWhile(c => c != 0).Select(c => (char)c).ToArray()).Trim();
                if (name.Length > 0) map[hw] = name;
            }
            catch { }
        }
        return map;
    }

    private static Dictionary<string, uint> MonitorDpi()
    {
        var map = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        try
        {
            Win32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr mon, IntPtr hdc, ref Win32.RECT r, IntPtr data) =>
            {
                var mi = new Win32.MONITORINFOEX { cbSize = Marshal.SizeOf<Win32.MONITORINFOEX>(), szDevice = "" };
                if (Win32.GetMonitorInfoW(mon, ref mi) && Win32.GetDpiForMonitor(mon, 0, out var dx, out _) == 0) map[mi.szDevice] = dx;
                return true;
            }, IntPtr.Zero);
        }
        catch { }
        return map;
    }
}

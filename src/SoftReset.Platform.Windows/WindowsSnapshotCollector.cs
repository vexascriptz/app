using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using SoftReset.Core.Collection;
using SoftReset.Core.Diagnostics.Rules;
using SoftReset.Core.Models;
using SoftReset.Core.Optimization.Actions;
using SoftReset.Core.Platform;
using SoftReset.Core.Startup;
using SoftReset.Platform.Windows.Native;

namespace SoftReset.Platform.Windows;

/// <summary>
/// Collects a full <see cref="SystemSnapshot"/> from Windows. Read-only. Each area runs in
/// isolation; anything that cannot be read is recorded as a collection issue so diagnostics can
/// state what they could not check.
/// </summary>
internal sealed class WindowsSnapshotCollector : ISnapshotCollector
{
    private readonly WindowsPlatform _p;
    public WindowsSnapshotCollector(WindowsPlatform p) => _p = p;

    public Task<SystemSnapshot> CollectAsync(CollectionOptions options, IProgress<CollectionProgress>? progress, CancellationToken ct) =>
        Task.Run(() => Collect(options, progress, ct), ct);

    private SystemSnapshot Collect(CollectionOptions o, IProgress<CollectionProgress>? progress, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var s = new SystemSnapshot
        {
            CapturedAt = DateTimeOffset.Now,
            MachineName = Environment.MachineName,
            CollectedElevated = _p.IsElevated,
            PlatformName = "Windows",
        };
        void Step(string stage, int pct, Action action)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new CollectionProgress(stage, pct));
            try { action(); }
            catch (OperationCanceledException) { throw; }
            catch (UnauthorizedAccessException ex) { s.AddIssue(stage, ex.Message, requiresAdmin: true); }
            catch (Exception ex) { s.AddIssue(stage, $"{ex.GetType().Name}: {ex.Message}"); }
        }

        Step("System information", 2, () => CollectOs(s));
        Step("Hardware", 6, () => CollectHardware(s));
        List<ProcessInfo> procs = new();
        var sampler = new ProcessCpuSampler(s.Hardware.CpuLogicalProcessors > 0 ? s.Hardware.CpuLogicalProcessors : null);
        Step("Processes", 10, () => { procs = _p.Processes.Enumerate().ToList(); sampler.Sample(procs); });
        Step("Performance sample", 14, () => SamplePerformance(s, o, ct));
        Step("Processes", 30, () => CollectProcesses(s, o, sampler));
        Step("Startup programs", 36, () => CollectStartup(s, o));
        Step("Event logs", 44, () => CollectEvents(s, o));
        Step("Crash dumps", 56, () => CollectDumps(s));
        Step("Storage", 60, () => CollectStorage(s));
        Step("Disk health", 66, () => CollectDisks(s));
        Step("Drivers and devices", 72, () => CollectDevices(s));
        Step("Graphics", 76, () => { s.Gpus = _p.Gpu.GetGpus(true).ToList(); s.Displays = _p.Display.GetDisplays().ToList(); });
        Step("Power", 80, () => s.Power = _p.Power.GetStatus());
        Step("Network", 84, () => s.Network = _p.Network.GetStatus(o.IncludeConnectivityTests));
        Step("Security", 88, () => CollectSecurity(s));
        Step("Windows Update", 92, () => CollectUpdates(s));
        Step("Gaming settings", 95, () =>
        {
            s.Gaming = RegistryReaders.ReadGaming(_p.Registry);
            s.Gaming.OverlayProcessesRunning = procs.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(n => KnownOverlays.Map.ContainsKey(n)).Select(n => KnownOverlays.Map[n]).ToList();
        });
        Step("Services", 97, () => CollectServices(s));
        Step("Thermal sensors", 98, () => CollectThermal(s));
        Step("Cleanup estimate", 99, () => CollectCleanup(s));
        if (!_p.IsElevated)
            s.AddIssue("Privileges", "The scan ran without administrator rights. Boot timing, crash dumps, SSD wear/temperature, TPM and BitLocker status may be missing. Click 'Give admin rights' for a complete scan.", true);
        s.CollectionDuration = sw.Elapsed;
        progress?.Report(new CollectionProgress("Done", 100));
        return s;
    }

    // ------------------------------------------------------------------ OS & hardware

    private void CollectOs(SystemSnapshot s)
    {
        s.Os = RegistryReaders.ReadOs(_p.Registry);
        var os = Wmi.Query("SELECT Caption, OSArchitecture, LastBootUpTime, InstallDate FROM Win32_OperatingSystem").FirstOrDefault();
        if (os is not null)
        {
            s.Os.ProductName = (os.S("Caption") ?? s.Os.ProductName).Replace("Microsoft ", "");
            s.Os.Architecture = os.S("OSArchitecture") ?? "";
            s.Os.LastBootTime = os.D("LastBootUpTime");
            s.Os.InstallDate ??= os.D("InstallDate");
        }
        else s.Os.LastBootTime = DateTimeOffset.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);
    }

    private static readonly int[] PortableChassis = { 8, 9, 10, 11, 12, 14, 18, 21, 30, 31, 32 };

    private void CollectHardware(SystemSnapshot s)
    {
        var h = s.Hardware;
        var cs = Wmi.Query("SELECT Manufacturer, Model, TotalPhysicalMemory, PCSystemType FROM Win32_ComputerSystem").FirstOrDefault();
        if (cs is not null)
        {
            h.Manufacturer = cs.S("Manufacturer") ?? "";
            h.Model = cs.S("Model") ?? "";
            h.TotalMemoryBytes = cs.L("TotalPhysicalMemory") ?? 0;
            h.IsLaptop = cs.I("PCSystemType") == 2;
        }
        var cpu = Wmi.Query("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor").FirstOrDefault();
        if (cpu is not null)
        {
            h.CpuName = (cpu.S("Name") ?? "").Trim();
            h.CpuCores = cpu.I("NumberOfCores") ?? 0;
            h.CpuLogicalProcessors = cpu.I("NumberOfLogicalProcessors") ?? Environment.ProcessorCount;
            h.CpuMaxClockMHz = cpu.I("MaxClockSpeed") ?? 0;
        }
        foreach (var m in Wmi.Query("SELECT DeviceLocator, Capacity, Speed, ConfiguredClockSpeed, Manufacturer, PartNumber FROM Win32_PhysicalMemory"))
            h.MemoryModules.Add(new MemoryModule
            {
                Slot = m.S("DeviceLocator") ?? "", CapacityBytes = m.L("Capacity") ?? 0, SpeedMHz = m.I("Speed") ?? 0,
                ConfiguredSpeedMHz = m.I("ConfiguredClockSpeed") ?? 0, Manufacturer = (m.S("Manufacturer") ?? "").Trim(), PartNumber = (m.S("PartNumber") ?? "").Trim(),
            });
        var bios = Wmi.Query("SELECT SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS").FirstOrDefault();
        h.BiosVersion = bios?.S("SMBIOSBIOSVersion") ?? "";
        h.BiosDate = bios?.D("ReleaseDate");
        var board = Wmi.Query("SELECT Manufacturer, Product FROM Win32_BaseBoard").FirstOrDefault();
        h.Motherboard = $"{board?.S("Manufacturer")} {board?.S("Product")}".Trim();
        var enclosure = Wmi.Query("SELECT ChassisTypes FROM Win32_SystemEnclosure").FirstOrDefault();
        if (enclosure?["ChassisTypes"] is ushort[] types && types.Any(t => PortableChassis.Contains(t))) h.IsLaptop = true;
        if (Wmi.Query("SELECT EstimatedChargeRemaining FROM Win32_Battery").FirstOrDefault() is { } bat)
        {
            h.IsLaptop = true;
            h.Battery = new BatteryInfo { ChargePercent = bat.I("EstimatedChargeRemaining") };
            if (Win32.GetSystemPowerStatus(out var ps)) h.Battery.OnAcPower = ps.ACLineStatus == 1;
            h.Battery.DesignCapacityMWh = Wmi.Query(@"root\wmi", "SELECT DesignedCapacity FROM BatteryStaticData", out _, 10).FirstOrDefault()?.L("DesignedCapacity");
            h.Battery.FullChargeCapacityMWh = Wmi.Query(@"root\wmi", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity", out _, 10).FirstOrDefault()?.L("FullChargedCapacity");
            h.Battery.CycleCount = Wmi.Query(@"root\wmi", "SELECT CycleCount FROM BatteryCycleCount", out _, 10).FirstOrDefault()?.I("CycleCount");
        }
    }

    // ------------------------------------------------------------------ performance

    private void SamplePerformance(SystemSnapshot s, CollectionOptions o, CancellationToken ct)
    {
        var p = s.Performance;
        var duration = o.CpuSampleDuration;
        p.SampleSeconds = duration.TotalSeconds;
        if (duration > TimeSpan.Zero && Win32.GetSystemTimes(out var i0, out var k0, out var u0))
        {
            var steps = Math.Max(1, (int)(duration.TotalMilliseconds / 500));
            ulong pi = i0.Value, pk = k0.Value, pu = u0.Value, firstI = pi, firstK = pk, firstU = pu;
            for (var n = 0; n < steps; n++)
            {
                ct.WaitHandle.WaitOne(500);
                ct.ThrowIfCancellationRequested();
                if (!Win32.GetSystemTimes(out var i1, out var k1, out var u1)) break;
                var total = (k1.Value - pk) + (u1.Value - pu);
                var busy = total - (i1.Value - pi);
                if (total > 0) p.CpuSeries.Add(Math.Round(100.0 * busy / total, 1));
                pi = i1.Value; pk = k1.Value; pu = u1.Value;
            }
            var t = (pk - firstK) + (pu - firstU);
            p.CpuAveragePercent = t > 0 ? Math.Round(100.0 * (t - (pi - firstI)) / t, 1) : 0;
            p.CpuPeakPercent = p.CpuSeries.DefaultIfEmpty(0).Max();
        }
        var mem = new Win32.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<Win32.MEMORYSTATUSEX>() };
        if (Win32.GlobalMemoryStatusEx(ref mem))
        {
            p.MemoryTotalBytes = (long)mem.ullTotalPhys;
            p.MemoryAvailableBytes = (long)mem.ullAvailPhys;
            p.CommitLimitBytes = (long)mem.ullTotalPageFile;
            p.CommitTotalBytes = (long)(mem.ullTotalPageFile - mem.ullAvailPageFile);
        }
        // PROCESSOR_POWER_INFORMATION: Number, MaxMhz, CurrentMhz, MhzLimit, MaxIdleState, CurrentIdleState (6 × ULONG)
        var cpus = Math.Max(1, Environment.ProcessorCount);
        var size = cpus * 24;
        var buf = Marshal.AllocHGlobal(size);
        try
        {
            if (Win32.CallNtPowerInformation(Win32.ProcessorInformation, IntPtr.Zero, 0, buf, (uint)size) == 0)
            {
                double max = 0, limit = 0;
                for (var i = 0; i < cpus; i++)
                {
                    max += (uint)Marshal.ReadInt32(buf, i * 24 + 4);
                    limit += (uint)Marshal.ReadInt32(buf, i * 24 + 12);
                }
                if (max > 0) p.CpuFrequencyPercentOfMax = Math.Round(100 * limit / max, 0);
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private void CollectProcesses(SystemSnapshot s, CollectionOptions o, ProcessCpuSampler sampler)
    {
        var procs = _p.Processes.Enumerate();
        var cpu = sampler.Sample(procs);
        s.ProcessCount = procs.Count;
        s.ThreadCount = procs.Sum(x => x.Threads);
        s.HandleCount = procs.Sum(x => x.Handles);
        var grouped = procs.Where(x => x.Pid > 4).GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Select(g => new ProcessSample
        {
            Pid = g.First().Pid,
            Name = g.Key,
            Path = g.Select(x => x.Path).FirstOrDefault(x => x is not null),
            CpuPercent = Math.Round(g.Sum(x => cpu.GetValueOrDefault(x.Pid)), 1),
            PrivateBytes = g.Sum(x => x.PrivateBytes),
            WorkingSetBytes = g.Sum(x => x.WorkingSetBytes),
            SessionId = g.First().SessionId,
        }).ToList();
        var top = grouped.OrderByDescending(x => x.CpuPercent).Take(o.TopProcessCount)
            .Union(grouped.OrderByDescending(x => x.PrivateBytes).Take(o.TopProcessCount)).ToList();
        foreach (var t in top.Where(t => t.Path is not null))
        {
            var d = _p.Processes.GetFileDetails(t.Path!, o.IncludeSignatureChecks);
            t.Publisher = d.Company;
            t.Description = d.Description;
            t.Signature = d.Signature;
        }
        s.TopProcesses = top.OrderByDescending(x => x.PrivateBytes).ToList();

        if (o.IncludeSignatureChecks)
        {
            foreach (var proc in procs.Where(x => x.Path is not null && x.SessionId != 0).GroupBy(x => x.Path!, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
            {
                // Only consider locations where unwanted software typically lives; signed system files are cached.
                var path = proc.Path!;
                if (path.StartsWith(_p.Folders.WindowsDir, StringComparison.OrdinalIgnoreCase) && !SystemNameOutsideSystem32(proc.Name, path)) continue;
                if (path.StartsWith(_p.Folders.ProgramFiles, StringComparison.OrdinalIgnoreCase) && !LooksLikeSystemName(proc.Name)) continue;
                if (path.StartsWith(_p.Folders.ProgramFilesX86, StringComparison.OrdinalIgnoreCase) && !LooksLikeSystemName(proc.Name)) continue;
                var det = _p.Processes.GetFileDetails(path, true);
                var reasons = ProcessHeuristics.Evaluate(proc.Name, path, det.Signature, _p.Folders);
                if (reasons.Count > 0) s.ProcessIndicators.Add(new SuspiciousProcessIndicator { Pid = proc.Pid, Name = proc.Name, Path = path, Reasons = reasons, Signature = det.Signature });
            }
        }
    }

    private static bool LooksLikeSystemName(string name) =>
        new[] { "svchost.exe", "lsass.exe", "csrss.exe", "winlogon.exe", "services.exe", "smss.exe", "explorer.exe", "dwm.exe" }.Any(n => ProcessHeuristics.Levenshtein(n, name) <= 1);

    private bool SystemNameOutsideSystem32(string name, string path) =>
        LooksLikeSystemName(name) && !path.StartsWith(_p.Folders.System32, StringComparison.OrdinalIgnoreCase) && !path.StartsWith(_p.Folders.WindowsDir + "\\explorer.exe", StringComparison.OrdinalIgnoreCase)
        && !path.Contains("SysWOW64", StringComparison.OrdinalIgnoreCase) && !Path.GetDirectoryName(path)!.Equals(_p.Folders.WindowsDir, StringComparison.OrdinalIgnoreCase);

    // ------------------------------------------------------------------ startup

    private void CollectStartup(SystemSnapshot s, CollectionOptions o)
    {
        IReadOnlyList<ScheduledTaskInfo> tasks;
        try { tasks = _p.Tasks.GetTasks(); }
        catch (Exception ex) { tasks = Array.Empty<ScheduledTaskInfo>(); s.AddIssue("Scheduled tasks", ex.Message); }
        s.StartupEntries = new StartupInventory(_p.Registry, _p.Folders).Enumerate(tasks);
        foreach (var e in s.StartupEntries)
        {
            var exe = e.ExecutablePath is { } x ? Environment.ExpandEnvironmentVariables(x) : null;
            if (exe is null || !File.Exists(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
            var d = _p.Processes.GetFileDetails(exe, o.IncludeSignatureChecks);
            e.Publisher = d.Signer ?? d.Company;
            e.Signature = d.Signature;
            e.IsMicrosoft = d.Signature == SignatureState.SignedMicrosoftWindows || (d.Signer?.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase) ?? false);
        }
    }

    // ------------------------------------------------------------------ events

    private void CollectEvents(SystemSnapshot s, CollectionOptions o)
    {
        var ms = (long)TimeSpan.FromDays(o.EventLookbackDays).TotalMilliseconds;
        var t = $"TimeCreated[timediff(@SystemTime) <= {ms}]";
        var r = s.Reliability;
        r.LookbackDays = o.EventLookbackDays;
        (string Log, string Query, int Max)[] queries =
        {
            ("System", $"*[System[(EventID=41 or EventID=6008 or EventID=1001) and {t}]]", 200),
            ("System", $"*[System[Provider[@Name='Microsoft-Windows-WHEA-Logger'] and {t}]]", 200),
            ("System", $"*[System[Provider[@Name='Display'] and (EventID=4101) and {t}]]", 100),
            ("System", $"*[System[Provider[@Name='Service Control Manager'] and (EventID=7000 or EventID=7001 or EventID=7009 or EventID=7011 or EventID=7023 or EventID=7024 or EventID=7031 or EventID=7034) and {t}]]", 300),
            ("System", $"*[System[(Provider[@Name='disk'] or Provider[@Name='Ntfs'] or Provider[@Name='Microsoft-Windows-Ntfs'] or Provider[@Name='storahci'] or Provider[@Name='stornvme'] or Provider[@Name='storport'] or Provider[@Name='iaStorAC'] or Provider[@Name='iaStorVD']) and (Level=1 or Level=2 or Level=3) and {t}]]", 200),
            ("System", $"*[System[Provider[@Name='Microsoft-Windows-DNS-Client'] and (EventID=1014) and {t}]]", 300),
            ("System", $"*[System[Provider[@Name='Microsoft-Windows-Kernel-Processor-Power'] and (EventID=37) and {t}]]", 200),
            ("System", $"*[System[Provider[@Name='Microsoft-Windows-WindowsUpdateClient'] and (EventID=20) and {t}]]", 50),
            ("Application", $"*[System[(Provider[@Name='Application Error'] or Provider[@Name='Application Hang']) and (EventID=1000 or EventID=1002) and {t}]]", 400),
            ("Application", $"*[System[Provider[@Name='Windows Error Reporting'] and (EventID=1001) and {t}]]", 150),
        };
        foreach (var (log, q, max) in queries)
        {
            try { r.Events.AddRange(Read(log, q, max)); }
            catch (UnauthorizedAccessException)
            {
                if (log == "System") r.SystemLogReadable = false; else r.ApplicationLogReadable = false;
            }
            catch (EventLogException ex) { s.AddIssue($"Event log ({log})", ex.Message); }
        }
        r.Events.RemoveAll(e => e.Id == 1001 && e.Log == "Application" && !e.Message.Contains("LiveKernelEvent", StringComparison.OrdinalIgnoreCase));
        r.Events.RemoveAll(e => e.Id == 1001 && e.Log == "System" && !(e.Provider.Contains("WER-SystemErrorReporting", StringComparison.OrdinalIgnoreCase) || e.Provider.Equals("BugCheck", StringComparison.OrdinalIgnoreCase)));

        try
        {
            var boots = Read("Microsoft-Windows-Diagnostics-Performance/Operational", $"*[System[(EventID>=100 and EventID<=110) and {t}]]", 300).ToList();
            foreach (var b in boots.Where(e => e.Id == 100))
            {
                var rec = new BootPerformance
                {
                    Time = b.Time,
                    BootTimeMs = int.TryParse(b.Get("BootTime"), out var bt) ? bt : 0,
                    MainPathBootTimeMs = int.TryParse(b.Get("MainPathBootTime"), out var mp) ? mp : null,
                    PostBootTimeMs = int.TryParse(b.Get("BootPostBootTime"), out var pb) ? pb : null,
                };
                foreach (var d in boots.Where(e => e.Id is >= 101 and <= 110 && Math.Abs((e.Time - b.Time).TotalMinutes) < 10))
                {
                    var name = d.Get("FriendlyName") is { Length: > 0 } fn ? fn : d.Get("Name") ?? $"event {d.Id}";
                    var deg = int.TryParse(d.Get("DegradationTime"), out var dt) ? $" slowed startup by {dt / 1000.0:0.#} s" : " slowed startup";
                    rec.Degradations.Add(name + deg);
                }
                if (rec.BootTimeMs > 0) r.BootRecords.Add(rec);
            }
        }
        catch (UnauthorizedAccessException) { r.BootRecordsReadable = false; s.AddIssue("Boot performance", "Boot duration records require administrator rights.", true); }
        catch (EventLogNotFoundException) { r.BootRecordsReadable = false; }
        catch (EventLogException) { r.BootRecordsReadable = false; s.AddIssue("Boot performance", "Boot duration records require administrator rights.", true); }
    }

    private static readonly string[] AppErrorFields = { "AppName", "AppVersion", "AppTimeStamp", "ModuleName", "ModuleVersion", "ModuleTimeStamp", "ExceptionCode", "FaultOffset", "ProcessId" };

    private static IEnumerable<EventEntry> Read(string log, string query, int max)
    {
        var q = new EventLogQuery(log, PathType.LogName, query) { ReverseDirection = true };
        using var reader = new EventLogReader(q);
        var count = 0;
        for (var rec = reader.ReadEvent(); rec is not null && count < max; rec = reader.ReadEvent(), count++)
        {
            using (rec)
            {
                var e = new EventEntry
                {
                    Log = log.StartsWith("Microsoft-Windows-Diagnostics", StringComparison.Ordinal) ? "Diagnostics-Performance" : log,
                    Provider = rec.ProviderName ?? "",
                    Id = rec.Id,
                    Level = rec.Level switch { 1 => "Critical", 2 => "Error", 3 => "Warning", _ => "Information" },
                    Time = rec.TimeCreated is { } tc ? new DateTimeOffset(tc) : DateTimeOffset.MinValue,
                };
                try { e.Message = Truncate(rec.FormatDescription() ?? "", 700); } catch { }
                try
                {
                    var x = XDocument.Parse(rec.ToXml());
                    XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";
                    var data = x.Root?.Element(ns + "EventData")?.Elements(ns + "Data").ToList() ?? new();
                    for (var i = 0; i < data.Count; i++)
                    {
                        var name = data[i].Attribute("Name")?.Value;
                        if (string.IsNullOrEmpty(name))
                            name = e.Provider == "Application Error" && i < AppErrorFields.Length ? AppErrorFields[i]
                                 : e.Provider == "Application Hang" && i == 0 ? "AppName"
                                 : $"param{i + 1}";
                        e.Data.TryAdd(name, data[i].Value);
                    }
                }
                catch { }
                yield return e;
            }
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private void CollectDumps(SystemSnapshot s)
    {
        var win = _p.Folders.WindowsDir;
        var r = s.Reliability;
        try
        {
            var mini = Path.Combine(win, "Minidump");
            if (Directory.Exists(mini))
                foreach (var f in new DirectoryInfo(mini).GetFiles("*.dmp"))
                    if (f.CreationTime > DateTime.Now.AddDays(-90))
                        r.CrashDumps.Add(new DumpFileInfo { Path = f.FullName, SizeBytes = f.Length, Created = f.CreationTime, Kind = "Minidump" });
            var lkr = Path.Combine(win, "LiveKernelReports");
            if (Directory.Exists(lkr))
                foreach (var f in new DirectoryInfo(lkr).GetFiles("*.dmp", SearchOption.AllDirectories))
                    if (f.CreationTime > DateTime.Now.AddDays(-90))
                        r.CrashDumps.Add(new DumpFileInfo { Path = f.FullName, SizeBytes = f.Length, Created = f.CreationTime, Kind = "LiveKernelReport" });
            var full = Path.Combine(win, "MEMORY.DMP");
            if (File.Exists(full)) { var fi = new FileInfo(full); r.CrashDumps.Add(new DumpFileInfo { Path = full, SizeBytes = fi.Length, Created = fi.CreationTime, Kind = "Full" }); }
        }
        catch (UnauthorizedAccessException) { r.CrashDumpsReadable = false; s.AddIssue("Crash dumps", "The crash dump folders can only be listed with administrator rights.", true); }
    }

    // ------------------------------------------------------------------ storage

    private static void CollectStorage(SystemSnapshot s)
    {
        var sysRoot = Path.GetPathRoot(Environment.SystemDirectory);
        foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed))
        {
            try
            {
                if (!d.IsReady) continue;
                s.Drives.Add(new DriveStatus
                {
                    Name = d.Name, Label = d.VolumeLabel, FileSystem = d.DriveFormat, TotalBytes = d.TotalSize, FreeBytes = d.AvailableFreeSpace,
                    IsSystemDrive = string.Equals(d.Name, sysRoot, StringComparison.OrdinalIgnoreCase), DriveType = "Fixed",
                });
            }
            catch { }
        }
    }

    private void CollectDisks(SystemSnapshot s)
    {
        var disks = Wmi.Query(@"root\Microsoft\Windows\Storage", "SELECT ObjectId, FriendlyName, MediaType, BusType, Size, HealthStatus, OperationalStatus FROM MSFT_PhysicalDisk", out var err, 20);
        if (err is not null) s.AddIssue("Disk health", err);
        var counters = Wmi.Query(@"root\Microsoft\Windows\Storage", "SELECT ObjectId, Temperature, Wear, ReadErrorsUncorrected, PowerOnHours FROM MSFT_StorageReliabilityCounter", out var cerr, 20);
        foreach (var d in disks)
        {
            var info = new PhysicalDiskInfo
            {
                FriendlyName = d.S("FriendlyName") ?? "Disk",
                MediaType = d.I("MediaType") switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => "Unknown" },
                BusType = d.I("BusType") switch { 17 => "NVMe", 11 => "SATA", 7 => "USB", 8 => "RAID", 10 => "SAS", 3 => "ATA", _ => "Other" },
                SizeBytes = d.L("Size") ?? 0,
                HealthStatus = d.I("HealthStatus") switch { 0 => "Healthy", 1 => "Warning", 2 => "Unhealthy", _ => "Unknown" },
            };
            if (d["OperationalStatus"] is ushort[] ops)
                info.OperationalStatus = ops.Select(x => x switch { 2 => "OK", 3 => "Degraded", 5 => "Predictive Failure", 6 => "Error", 10 => "Stopped", 13 => "Lost Communication", 0xD00B => "Transient Error", _ => x.ToString() }).ToList();
            // Reliability counters are linked by matching device id fragments in ObjectId.
            var key = (d.S("ObjectId") ?? "").Split('{').LastOrDefault()?.TrimEnd('}', '"') ?? "";
            var c = counters.FirstOrDefault(x => key.Length > 4 && (x.S("ObjectId") ?? "").Contains(key, StringComparison.OrdinalIgnoreCase));
            if (c is not null)
            {
                info.ReliabilityCountersAvailable = true;
                info.TemperatureC = c.I("Temperature") is > 0 and < 120 and var tc ? tc : null;
                info.WearPercent = c.I("Wear") is >= 0 and <= 100 and var w ? w : null;
                info.ReadErrorsUncorrected = c.L("ReadErrorsUncorrected");
                info.PowerOnHours = c.L("PowerOnHours");
            }
            s.PhysicalDisks.Add(info);
        }
        if (s.PhysicalDisks.Count > 0 && !s.PhysicalDisks.Any(x => x.ReliabilityCountersAvailable))
            s.AddIssue("Disk reliability", cerr ?? (s.CollectedElevated
                ? "This disk or its driver does not report wear and temperature counters (common for virtual disks, some RAID and USB controllers)."
                : "SSD wear and temperature counters require administrator rights."), !s.CollectedElevated);
        foreach (var f in Wmi.Query(@"root\wmi", "SELECT InstanceName, PredictFailure FROM MSStorageDriver_FailurePredictStatus", out _, 10).Where(x => x.B("PredictFailure") == true))
        {
            var target = s.PhysicalDisks.FirstOrDefault();
            if (target is not null) target.PredictFailure = true;
        }
    }

    private static void CollectDevices(SystemSnapshot s)
    {
        foreach (var d in Wmi.Query("SELECT Name, PNPClass, PNPDeviceID, Manufacturer, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0"))
            s.DeviceProblems.Add(new DeviceProblem
            {
                Name = d.S("Name") ?? "Unknown device", DeviceClass = d.S("PNPClass") ?? "", PnpDeviceId = d.S("PNPDeviceID") ?? "",
                Manufacturer = d.S("Manufacturer") ?? "", ErrorCode = d.I("ConfigManagerErrorCode") ?? 0,
            });
    }

    // ------------------------------------------------------------------ security & updates

    internal static Dictionary<string, bool> FirewallProfiles(IRegistryAccess reg)
    {
        var map = new Dictionary<string, bool>();
        const string root = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy";
        foreach (var (key, label) in new[] { ("DomainProfile", "Domain"), ("StandardProfile", "Private"), ("PublicProfile", "Public") })
        {
            var v = reg.GetValue(RegHive.LocalMachine, $@"{root}\{key}", "EnableFirewall")?.AsInt();
            if (v is not null) map[label] = v != 0;
        }
        return map;
    }

    private void CollectSecurity(SystemSnapshot s)
    {
        var sec = s.Security;
        var mp = Wmi.Query(@"root\Microsoft\Windows\Defender", "SELECT AntivirusEnabled, RealTimeProtectionEnabled, AntivirusSignatureAge, IsTamperProtected FROM MSFT_MpComputerStatus", out var mpErr, 15).FirstOrDefault();
        if (mp is not null)
        {
            sec.DefenderAntivirusEnabled = mp.B("AntivirusEnabled");
            sec.DefenderRealTimeEnabled = mp.B("RealTimeProtectionEnabled");
            sec.DefenderSignatureAgeDays = mp.I("AntivirusSignatureAge");
            sec.DefenderTamperProtection = mp.B("IsTamperProtected");
        }
        else if (mpErr is not null) s.AddIssue("Microsoft Defender", mpErr);
        foreach (var av in Wmi.Query(@"root\SecurityCenter2", "SELECT displayName, productState FROM AntiVirusProduct", out _, 15))
        {
            var name = av.S("displayName") ?? "";
            if (name.Contains("Defender", StringComparison.OrdinalIgnoreCase) && name.Contains("Windows", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.Contains("Microsoft Defender", StringComparison.OrdinalIgnoreCase)) continue;
            // productState bits 12-15 == 0x1 means the product reports itself as enabled.
            var state = av.L("productState") ?? 0;
            var enabled = ((state >> 12) & 0xF) is 1 or 3;
            sec.ThirdPartyAntivirus.Add(enabled ? name : name + " (registered, reports disabled)");
        }
        sec.FirewallProfiles = FirewallProfiles(_p.Registry);
        var uac = RegistryReaders.ReadUac(_p.Registry);
        sec.UacEnabled = uac.Enabled;
        sec.UacConsentPromptBehaviorAdmin = uac.ConsentAdmin;
        sec.SecureBootEnabled = _p.Registry.GetValue(RegHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled")?.AsInt() is { } sb ? sb == 1 : null;
        sec.MemoryIntegrityEnabled = _p.Registry.GetValue(RegHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled")?.AsInt() == 1;
        var tpm = Wmi.Query(@"root\cimv2\Security\MicrosoftTpm", "SELECT IsEnabled_InitialValue FROM Win32_Tpm", out var tpmErr, 10).FirstOrDefault();
        sec.TpmPresent = tpm is not null ? true : tpmErr is "Access denied (requires administrator)." ? null : false;
        sec.SmartScreenEnabled = _p.Registry.GetValue(RegHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", "SmartScreenEnabled")?.AsString() is { } ss ? ss != "Off" : null;
    }

    internal static UpdateStatus ReadUpdateHistory()
    {
        var u = new UpdateStatus();
        var type = Type.GetTypeFromProgID("Microsoft.Update.Session");
        if (type is null) return u;
        dynamic session = Activator.CreateInstance(type)!;
        try
        {
            dynamic searcher = session.CreateUpdateSearcher();
            int total = searcher.GetTotalHistoryCount();
            if (total == 0) return u;
            dynamic history = searcher.QueryHistory(0, Math.Min(total, 60));
            foreach (dynamic h in history)
            {
                try
                {
                    int op = h.Operation;
                    if (op != 1) continue; // installations only
                    int rc = h.ResultCode;
                    var item = new UpdateHistoryItem
                    {
                        Title = (string)h.Title,
                        Date = new DateTimeOffset(DateTime.SpecifyKind((DateTime)h.Date, DateTimeKind.Utc)).ToLocalTime(),
                        Result = rc switch { 2 => "Succeeded", 3 => "Succeeded with errors", 4 => "Failed", 5 => "Aborted", 1 => "In progress", _ => "Not started" },
                        HResult = rc is 4 or 5 ? $"0x{(int)h.HResult:X8}" : null,
                    };
                    u.RecentHistory.Add(item);
                    if (rc is 2 or 3 && (u.LastSuccessfulInstall is null || item.Date > u.LastSuccessfulInstall)) u.LastSuccessfulInstall = item.Date;
                }
                catch { }
            }
        }
        finally { Marshal.FinalReleaseComObject(session); }
        return u;
    }

    private void CollectUpdates(SystemSnapshot s)
    {
        try { s.Updates = ReadUpdateHistory(); }
        catch (Exception ex) { s.AddIssue("Windows Update history", ex.Message); }
        var reboot = RegistryReaders.ReadRebootPending(_p.Registry);
        s.Updates.RebootPending = reboot.Pending;
        s.Updates.RebootPendingReasons = reboot.Reasons;
        var policy = RegistryReaders.ReadUpdatePolicy(_p.Registry);
        s.Updates.ManagedByPolicy = policy.Managed;
        s.Updates.UpdatesPausedByPolicyOrUser = policy.Paused;
        s.Updates.PausedUntil = policy.PausedUntil;
        var wu = _p.Services.Get("wuauserv");
        s.Updates.WindowsUpdateServiceStartMode = wu?.StartMode;
        s.Updates.WindowsUpdateServiceState = wu?.State;
    }

    private void CollectServices(SystemSnapshot s)
    {
        foreach (var svc in _p.Services.GetServices().Where(x => x.StartMode == "Auto" && x.State != "Running" && x.ExitCode is > 0 and not 1077))
            s.NonRunningAutoServices.Add(new ServiceSummary { Name = svc.Name, DisplayName = svc.DisplayName, StartMode = svc.StartMode, State = svc.State, PathName = svc.PathName, ExitCode = svc.ExitCode });
    }

    private static void CollectThermal(SystemSnapshot s)
    {
        foreach (var z in Wmi.Query(@"root\wmi", "SELECT InstanceName, CurrentTemperature FROM MSAcpi_ThermalZoneTemperature", out _, 10))
        {
            var deciKelvin = z.L("CurrentTemperature") ?? 0;
            var c = deciKelvin / 10.0 - 273.15;
            if (c is > 5 and < 125)
                s.Thermal.Readings.Add(new ThermalReading { Sensor = z.S("InstanceName") ?? "ACPI thermal zone", Celsius = Math.Round(c, 1), Source = "ACPI thermal zone (WMI)", Reliable = false });
        }
        foreach (var g in s.Gpus.Where(g => g.Telemetry?.TemperatureC is not null))
            s.Thermal.Readings.Add(new ThermalReading { Sensor = g.Name, Celsius = g.Telemetry!.TemperatureC!.Value, Source = g.Telemetry.Source, Reliable = true });
        s.Thermal.ThrottlingEvents = s.Reliability.Find("Kernel-Processor-Power", 37).Count();
        s.Thermal.CpuSensorReliable = false;
        s.Thermal.Notes = "Windows provides no documented, reliable CPU temperature interface for consumer hardware; ACPI thermal zones (if present) usually report motherboard zones and are shown for reference only.";
    }

    private void CollectCleanup(SystemSnapshot s)
    {
        var c = s.Cleanup;
        var win = _p.Folders.WindowsDir;
        long? Measure(string path) { try { return Directory.Exists(path) ? FileCleaner.Measure(path, TimeSpan.Zero).BytesFound : 0; } catch { return null; } }
        var user = FileCleaner.Measure(_p.Folders.Temp, TimeSpan.Zero);
        c.UserTempBytes = user.BytesFound;
        c.UserTempFiles = user.FilesFound;
        c.WindowsTempBytes = Measure(Path.Combine(win, "Temp"));
        c.WindowsUpdateCacheBytes = Measure(Path.Combine(win, "SoftwareDistribution", "Download"));
        if (_p.Maintenance.GetRecycleBinInfo() is { } rb) { c.RecycleBinBytes = rb.Bytes; c.RecycleBinItems = rb.Items; }
        var root = Path.GetPathRoot(win)!;
        try { var h = new FileInfo(Path.Combine(root, "hiberfil.sys")); if (h.Exists) c.HibernationFileBytes = h.Length; } catch { }
        try { var p = new FileInfo(Path.Combine(root, "pagefile.sys")); if (p.Exists) c.PageFileBytes = p.Length; } catch { }
        c.CrashDumpBytes = s.Reliability.CrashDumps.Sum(d => d.SizeBytes);
        s.Updates.SoftwareDistributionDownloadBytes = c.WindowsUpdateCacheBytes;
    }
}

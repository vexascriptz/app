using System.Runtime.InteropServices;
using System.Text;
using SoftReset.Core.Models;
using SoftReset.Core.Platform;

namespace SoftReset.Platform.Windows.Native;

/// <summary>
/// NVIDIA Management Library (nvml.dll, installed with the NVIDIA driver). Read-only telemetry
/// through NVIDIA's documented, supported API.
/// </summary>
internal static unsafe class Nvml
{
    [StructLayout(LayoutKind.Sequential)] private struct Utilization { public uint Gpu, Memory; }
    [StructLayout(LayoutKind.Sequential)] private struct Memory { public ulong Total, Free, Used; }

    private static IntPtr _lib;

    private static bool Load()
    {
        if (_lib != IntPtr.Zero) return true;
        var candidates = new[]
        {
            Path.Combine(Environment.SystemDirectory, "nvml.dll"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvml.dll"),
        };
        foreach (var c in candidates)
            if (File.Exists(c) && NativeLibrary.TryLoad(c, out _lib)) return true;
        return false;
    }

    private static T Fn<T>(string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_lib, name));

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NoArg();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetCount(out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetHandle(uint index, out IntPtr device);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetString(IntPtr device, byte* buffer, uint length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetSysString(byte* buffer, uint length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetTemp(IntPtr device, int sensor, out uint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetUInt(IntPtr device, out uint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetInt(IntPtr device, out int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetULong(IntPtr device, out ulong value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetClock(IntPtr device, int type, out uint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetUtil(IntPtr device, out Utilization value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetMem(IntPtr device, out Memory value);

    public static List<(string Name, string Driver, GpuTelemetry Telemetry)> Read(out string? problem)
    {
        problem = null;
        var list = new List<(string, string, GpuTelemetry)>();
        if (!Load()) { problem = "nvml.dll not found (NVIDIA driver not installed)."; return list; }
        var init = Fn<NoArg>("nvmlInit_v2");
        var shutdown = Fn<NoArg>("nvmlShutdown");
        var rc = init();
        if (rc != 0) { problem = $"nvmlInit failed ({rc})."; return list; }
        try
        {
            var buf = stackalloc byte[96];
            var driver = "";
            if (Fn<GetSysString>("nvmlSystemGetDriverVersion")(buf, 96) == 0) driver = Utf8(buf);
            Fn<GetCount>("nvmlDeviceGetCount_v2")(out var count);
            for (uint i = 0; i < count && i < 8; i++)
            {
                if (Fn<GetHandle>("nvmlDeviceGetHandleByIndex_v2")(i, out var dev) != 0) continue;
                var name = Fn<GetString>("nvmlDeviceGetName")(dev, buf, 96) == 0 ? Utf8(buf) : $"NVIDIA GPU {i}";
                var t = new GpuTelemetry { Source = "NVML" };
                if (Fn<GetTemp>("nvmlDeviceGetTemperature")(dev, 0, out var temp) == 0) t.TemperatureC = temp;
                if (Fn<GetUtil>("nvmlDeviceGetUtilizationRates")(dev, out var util) == 0) { t.UtilizationPercent = util.Gpu; t.MemoryUtilizationPercent = util.Memory; }
                if (Fn<GetMem>("nvmlDeviceGetMemoryInfo")(dev, out var mem) == 0) { t.MemoryTotalBytes = (long)mem.Total; t.MemoryUsedBytes = (long)mem.Used; }
                if (Fn<GetUInt>("nvmlDeviceGetPowerUsage")(dev, out var mw) == 0) t.PowerWatts = Math.Round(mw / 1000.0, 1);
                if (Fn<GetUInt>("nvmlDeviceGetEnforcedPowerLimit")(dev, out var lim) == 0) t.PowerLimitWatts = Math.Round(lim / 1000.0, 1);
                if (Fn<GetUInt>("nvmlDeviceGetFanSpeed")(dev, out var fan) == 0) t.FanPercent = (int)fan;
                if (Fn<GetClock>("nvmlDeviceGetClockInfo")(dev, 0, out var gclk) == 0) t.GraphicsClockMHz = (int)gclk;
                if (Fn<GetClock>("nvmlDeviceGetClockInfo")(dev, 2, out var mclk) == 0) t.MemoryClockMHz = (int)mclk;
                if (Fn<GetInt>("nvmlDeviceGetPerformanceState")(dev, out var ps) == 0 && ps is >= 0 and < 32) t.PerformanceState = $"P{ps}";
                if (Fn<GetUInt>("nvmlDeviceGetCurrPcieLinkGeneration")(dev, out var gen) == 0 && Fn<GetUInt>("nvmlDeviceGetCurrPcieLinkWidth")(dev, out var width) == 0)
                    t.PcieLink = $"PCIe Gen{gen} x{width}";
                if (NativeLibrary.TryGetExport(_lib, "nvmlDeviceGetCurrentClocksEventReasons", out var p1) || NativeLibrary.TryGetExport(_lib, "nvmlDeviceGetCurrentClocksThrottleReasons", out p1))
                {
                    if (Marshal.GetDelegateForFunctionPointer<GetULong>(p1)(dev, out var reasons) == 0) t.ThrottleReasons = DescribeReasons(reasons);
                }
                list.Add((name, driver, t));
            }
        }
        finally { shutdown(); }
        return list;
    }

    internal static List<string> DescribeReasons(ulong r)
    {
        var l = new List<string>();
        if ((r & 0x4) != 0) l.Add("Power limit (software power cap)");
        if ((r & 0x8) != 0) l.Add("Hardware slowdown (temperature or power brake)");
        if ((r & 0x20) != 0) l.Add("Thermal slowdown (software)");
        if ((r & 0x40) != 0) l.Add("Thermal slowdown (hardware)");
        if ((r & 0x80) != 0) l.Add("Power brake slowdown (external power)");
        return l;
    }

    private static string Utf8(byte* p)
    {
        var len = 0;
        while (len < 96 && p[len] != 0) len++;
        return Encoding.UTF8.GetString(p, len);
    }
}

/// <summary>
/// WDDM kernel-mode thunks (gdi32.dll). KMTQAITYPE_ADAPTERPERFDATA is the documented source of the
/// GPU temperature/fan/power values shown in Task Manager (WDDM 2.4+, any GPU vendor whose driver reports them).
/// </summary>
internal static unsafe class D3dkmt
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ADAPTERINFO { public uint hAdapter; public uint LuidLow; public int LuidHigh; public uint NumOfSources; public int bPrecisePresentRegionsPreferred; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ENUMADAPTERS2 { public uint NumAdapters; public IntPtr pAdapters; }

    [StructLayout(LayoutKind.Sequential)]
    private struct QUERYADAPTERINFO { public uint hAdapter; public int Type; public IntPtr pPrivateDriverData; public uint PrivateDriverDataSize; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ADAPTER_PERFDATA
    {
        public uint PhysicalAdapterIndex;
        public ulong MemoryFrequency, MaxMemoryFrequency, MaxMemoryFrequencyOC, MemoryBandwidth, PCIEBandwidth;
        public uint FanRPM, Power, Temperature;
        public byte PowerStateOverride;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CLOSEADAPTER { public uint hAdapter; }

    [DllImport("gdi32.dll")] private static extern int D3DKMTEnumAdapters2(ref ENUMADAPTERS2 data);
    [DllImport("gdi32.dll")] private static extern int D3DKMTQueryAdapterInfo(ref QUERYADAPTERINFO data);
    [DllImport("gdi32.dll")] private static extern int D3DKMTCloseAdapter(ref CLOSEADAPTER data);

    private const int KMTQAITYPE_ADAPTERPERFDATA = 62;

    public sealed record AdapterPerf(long Luid, double? TemperatureC, int? FanRpm, double? PowerPercent);

    public static List<AdapterPerf> Read()
    {
        var result = new List<AdapterPerf>();
        var e = new ENUMADAPTERS2();
        if (D3DKMTEnumAdapters2(ref e) != 0 || e.NumAdapters == 0 || e.NumAdapters > 32) return result;
        var size = Marshal.SizeOf<ADAPTERINFO>();
        e.pAdapters = Marshal.AllocHGlobal(size * (int)e.NumAdapters);
        try
        {
            if (D3DKMTEnumAdapters2(ref e) != 0) return result;
            for (var i = 0; i < e.NumAdapters; i++)
            {
                var info = Marshal.PtrToStructure<ADAPTERINFO>(e.pAdapters + i * size);
                var perf = new ADAPTER_PERFDATA();
                var q = new QUERYADAPTERINFO
                {
                    hAdapter = info.hAdapter,
                    Type = KMTQAITYPE_ADAPTERPERFDATA,
                    pPrivateDriverData = (IntPtr)(&perf),
                    PrivateDriverDataSize = (uint)Marshal.SizeOf<ADAPTER_PERFDATA>(),
                };
                var luid = ((long)info.LuidHigh << 32) | info.LuidLow;
                if (D3DKMTQueryAdapterInfo(ref q) == 0)
                {
                    result.Add(new AdapterPerf(luid,
                        perf.Temperature > 0 && perf.Temperature < 1500 ? perf.Temperature / 10.0 : null,
                        perf.FanRPM > 0 ? (int)perf.FanRPM : null,
                        perf.Power > 0 ? perf.Power / 10.0 : null));
                }
                var close = new CLOSEADAPTER { hAdapter = info.hAdapter };
                D3DKMTCloseAdapter(ref close);
            }
        }
        finally { Marshal.FreeHGlobal(e.pAdapters); }
        return result;
    }
}

/// <summary>
/// Reads NVIDIA driver profile settings (DRS) through NVAPI, NVIDIA's public SDK. Read-only.
/// Function IDs and structure layouts come from NVIDIA's MIT-licensed nvapi_interface.h / nvapi.h.
/// This runs in a separate probe process so a driver fault cannot take down the console.
/// </summary>
internal static unsafe class NvApiDrs
{
    private const uint Initialize = 0x0150E828, Unload = 0xD22BDD7E, GetDriverAndBranch = 0x2926AAAD;
    private const uint CreateSession = 0x0694D52E, DestroySession = 0xDAD9CFF8, LoadSettings = 0x375DBD6B;
    private const uint GetBaseProfile = 0xDA8466A0, GetCurrentGlobalProfile = 0x617BFF9F, GetSetting = 0x73BF8338;
    private const uint GetProfileInfo = 0x61CD6FD6, GetNumProfiles = 0x1DAE4FBC, EnumProfiles = 0xBC371EE0;
    private const uint EnumSettings = 0xAE3039DA, EnumApplications = 0x7FA2173A;

    private const int SettingSize = 8328;          // sizeof(NVDRS_SETTING_V1)
    private const uint SettingVer = SettingSize | (1u << 16);
    private const int ProfileSize = 4116;          // sizeof(NVDRS_PROFILE_V1)
    private const uint ProfileVer = ProfileSize | (1u << 16);
    private const int AppSize = 12296;             // sizeof(NVDRS_APPLICATION_V1)
    private const uint AppVer = AppSize | (1u << 16);
    private const int StatusSettingNotFound = -160;

    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr QueryInterface(uint id);

    private static IntPtr Q(uint id)
    {
        var p = QueryInterface(id);
        if (p == IntPtr.Zero) throw new EntryPointNotFoundException($"NVAPI function 0x{id:X8} not available");
        return p;
    }

    public static NvidiaDriverSettingsResult Read(IEnumerable<(uint Id, string Name)> settingsOfInterest, int maxProfiles = 60)
    {
        var result = new NvidiaDriverSettingsResult();
        if (!File.Exists(Path.Combine(Environment.SystemDirectory, "nvapi64.dll")))
        {
            result.Status = "NVAPI is not available (no NVIDIA driver installed).";
            return result;
        }
        var init = (delegate* unmanaged[Cdecl]<int>)Q(Initialize);
        var status = init();
        if (status != 0) { result.Status = $"NVAPI initialisation failed ({status})."; return result; }
        IntPtr session = IntPtr.Zero;
        try
        {
            var driverFn = (delegate* unmanaged[Cdecl]<uint*, byte*, int>)Q(GetDriverAndBranch);
            uint drv;
            var branch = stackalloc byte[64];
            if (driverFn(&drv, branch) == 0)
            {
                result.DriverVersion = $"{drv / 100}.{drv % 100:D2}";
                result.DriverBranch = Marshal.PtrToStringAnsi((IntPtr)branch);
            }

            var create = (delegate* unmanaged[Cdecl]<IntPtr*, int>)Q(CreateSession);
            IntPtr s;
            if (create(&s) != 0) { result.Status = "Could not open a driver settings session."; return result; }
            session = s;
            var load = (delegate* unmanaged[Cdecl]<IntPtr, int>)Q(LoadSettings);
            if (load(session) != 0) { result.Status = "Could not load driver settings."; return result; }

            var getGlobal = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr*, int>)Q(GetCurrentGlobalProfile);
            IntPtr global;
            if (getGlobal(session, &global) != 0)
            {
                var getBase = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr*, int>)Q(GetBaseProfile);
                if (getBase(session, &global) != 0) { result.Status = "No global profile."; return result; }
            }

            var setting = (byte*)NativeMemory.AllocZeroed(SettingSize);
            var profile = (byte*)NativeMemory.AllocZeroed(ProfileSize);
            try
            {
                var getProfileInfo = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, byte*, int>)Q(GetProfileInfo);
                *(uint*)profile = ProfileVer;
                if (getProfileInfo(session, global, profile) == 0)
                    result.GlobalProfileName = new string((char*)(profile + 4));

                var getSetting = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, byte*, int>)Q(GetSetting);
                foreach (var (id, name) in settingsOfInterest)
                {
                    NativeMemory.Clear(setting, SettingSize);
                    *(uint*)setting = SettingVer;
                    var rc = getSetting(session, global, id, setting);
                    if (rc == StatusSettingNotFound)
                    {
                        result.GlobalSettings.Add(new NvidiaDriverSetting { SettingId = id, Name = name, Value = null, Location = "Default" });
                        continue;
                    }
                    if (rc != 0) continue;
                    result.GlobalSettings.Add(Parse(setting, name));
                }

                // Customised per-application profiles (user-modified settings only).
                var numFn = (delegate* unmanaged[Cdecl]<IntPtr, uint*, int>)Q(GetNumProfiles);
                var enumProfiles = (delegate* unmanaged[Cdecl]<IntPtr, uint, IntPtr*, int>)Q(EnumProfiles);
                var enumSettings = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, uint*, byte*, int>)Q(EnumSettings);
                var enumApps = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, uint*, byte*, int>)Q(EnumApplications);
                uint num;
                if (numFn(session, &num) == 0)
                {
                    const int batch = 16;
                    var settingsBuf = (byte*)NativeMemory.AllocZeroed((nuint)(SettingSize * batch));
                    var appBuf = (byte*)NativeMemory.AllocZeroed(AppSize);
                    try
                    {
                        for (uint i = 0; i < num && result.CustomizedProfiles.Count < maxProfiles; i++)
                        {
                            IntPtr hp;
                            if (enumProfiles(session, i, &hp) != 0) break;
                            NativeMemory.Clear(profile, ProfileSize);
                            *(uint*)profile = ProfileVer;
                            if (getProfileInfo(session, hp, profile) != 0) continue;
                            var numSettings = *(uint*)(profile + 4112);
                            if (numSettings == 0) continue;
                            var summary = new NvidiaProfileSummary
                            {
                                Name = new string((char*)(profile + 4)),
                                IsPredefined = *(uint*)(profile + 4104) != 0,
                                ApplicationCount = (int)*(uint*)(profile + 4108),
                                SettingCount = (int)numSettings,
                            };
                            for (uint start = 0; start < numSettings; start += batch)
                            {
                                NativeMemory.Clear(settingsBuf, (nuint)(SettingSize * batch));
                                for (var k = 0; k < batch; k++) *(uint*)(settingsBuf + k * SettingSize) = SettingVer;
                                uint got = batch;
                                if (enumSettings(session, hp, start, &got, settingsBuf) != 0) break;
                                for (var k = 0; k < got; k++)
                                {
                                    var sp = settingsBuf + k * SettingSize;
                                    var location = *(uint*)(sp + 4108);
                                    var predefined = *(uint*)(sp + 4112);
                                    if (location == 0 && predefined == 0)
                                        summary.UserSettings.Add(Parse(sp, null));
                                }
                                if (got < batch) break;
                            }
                            summary.UserModifiedSettingCount = summary.UserSettings.Count;
                            if (summary.UserModifiedSettingCount == 0) continue;
                            for (uint a = 0; a < Math.Min(summary.ApplicationCount, 5); a++)
                            {
                                NativeMemory.Clear(appBuf, AppSize);
                                *(uint*)appBuf = AppVer;
                                uint one = 1;
                                if (enumApps(session, hp, a, &one, appBuf) != 0 || one == 0) break;
                                summary.Applications.Add(new string((char*)(appBuf + 8)));
                            }
                            result.CustomizedProfiles.Add(summary);
                        }
                    }
                    finally
                    {
                        NativeMemory.Free(settingsBuf);
                        NativeMemory.Free(appBuf);
                    }
                }
            }
            finally
            {
                NativeMemory.Free(setting);
                NativeMemory.Free(profile);
            }
            result.Available = true;
            result.Status = "Read from the NVIDIA driver profile store (NVAPI DRS, read-only).";
        }
        finally
        {
            if (session != IntPtr.Zero)
            {
                var destroy = (delegate* unmanaged[Cdecl]<IntPtr, int>)Q(DestroySession);
                destroy(session);
            }
            var unload = (delegate* unmanaged[Cdecl]<int>)Q(Unload);
            unload();
        }
        return result;
    }

    private static NvidiaDriverSetting Parse(byte* s, string? knownName)
    {
        var type = *(uint*)(s + 4104);
        var setting = new NvidiaDriverSetting
        {
            SettingId = *(uint*)(s + 4100),
            Name = knownName ?? new string((char*)(s + 4)),
            IsPredefined = *(uint*)(s + 4112) != 0,
            Location = *(uint*)(s + 4108) switch { 0 => "Current", 1 => "Global", 2 => "Base", _ => "Default" },
        };
        if (string.IsNullOrEmpty(setting.Name)) setting.Name = $"0x{setting.SettingId:X8}";
        switch (type)
        {
            case 0: setting.Value = *(uint*)(s + 4224); break;                   // DWORD
            case 3: setting.StringValue = new string((char*)(s + 4224)); break;  // WSTRING
            case 4: setting.Value = (uint)*(ulong*)(s + 4224); break;            // QWORD (low part)
        }
        return setting;
    }
}

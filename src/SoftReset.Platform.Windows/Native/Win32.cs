using System.Runtime.InteropServices;

namespace SoftReset.Platform.Windows.Native;

/// <summary>Documented Win32 APIs used by the console.</summary>
internal static partial class Win32
{
    // ------------------------------------------------------------------ kernel32
    [StructLayout(LayoutKind.Sequential)]
    internal struct FILETIME { public uint Low; public uint High; public readonly ulong Value => ((ulong)High << 32) | Low; }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

    [StructLayout(LayoutKind.Sequential)]
    internal struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(IntPtr handle);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool QueryFullProcessImageName(IntPtr process, uint flags, char* buffer, ref uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetProcessTimes(IntPtr process, out FILETIME creation, out FILETIME exit, out FILETIME kernel, out FILETIME user);

    [StructLayout(LayoutKind.Sequential)]
    internal struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public uint BatteryLifeTime, BatteryFullLifeTime;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    // Toolhelp snapshot for parent PIDs
    internal const uint TH32CS_SNAPPROCESS = 0x2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct PROCESSENTRY32W
    {
        public uint dwSize, cntUsage, th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID, cntThreads, th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool Process32FirstW(IntPtr snapshot, ref PROCESSENTRY32W entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool Process32NextW(IntPtr snapshot, ref PROCESSENTRY32W entry);

    // ------------------------------------------------------------------ powrprof
    [LibraryImport("powrprof.dll")]
    internal static partial uint PowerGetActiveScheme(IntPtr rootPowerKey, out IntPtr activePolicyGuid);

    [LibraryImport("powrprof.dll")]
    internal static partial uint PowerSetActiveScheme(IntPtr rootPowerKey, in Guid schemeGuid);

    [LibraryImport("powrprof.dll")]
    internal static partial uint PowerEnumerate(IntPtr rootPowerKey, IntPtr schemeGuid, IntPtr subGroup, uint accessFlags, uint index, IntPtr buffer, ref uint bufferSize);

    [LibraryImport("powrprof.dll")]
    internal static partial uint PowerReadFriendlyName(IntPtr rootPowerKey, in Guid schemeGuid, IntPtr subGroup, IntPtr setting, IntPtr buffer, ref uint bufferSize);

    [LibraryImport("powrprof.dll")]
    internal static partial uint PowerReadACValueIndex(IntPtr rootPowerKey, in Guid scheme, in Guid subGroup, in Guid setting, out uint value);

    [LibraryImport("powrprof.dll")]
    internal static partial uint PowerReadDCValueIndex(IntPtr rootPowerKey, in Guid scheme, in Guid subGroup, in Guid setting, out uint value);

    [LibraryImport("powrprof.dll")]
    internal static partial uint PowerWriteACValueIndex(IntPtr rootPowerKey, in Guid scheme, in Guid subGroup, in Guid setting, uint value);

    [LibraryImport("powrprof.dll")]
    internal static partial uint PowerWriteDCValueIndex(IntPtr rootPowerKey, in Guid scheme, in Guid subGroup, in Guid setting, uint value);

    [LibraryImport("powrprof.dll")]
    internal static partial uint PowerDuplicateScheme(IntPtr rootPowerKey, in Guid source, out IntPtr destination);

    [LibraryImport("powrprof.dll")]
    internal static partial uint PowerDeleteScheme(IntPtr rootPowerKey, in Guid scheme);

    [LibraryImport("powrprof.dll")]
    internal static partial uint CallNtPowerInformation(int level, IntPtr input, uint inputLength, IntPtr output, uint outputLength);

    internal const int SystemPowerCapabilities = 4;
    internal const int ProcessorInformation = 11;

    [LibraryImport("kernel32.dll")]
    internal static partial IntPtr LocalFree(IntPtr mem);

    // ------------------------------------------------------------------ user32 display
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    internal const int DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x1;
    internal const int DISPLAY_DEVICE_PRIMARY_DEVICE = 0x4;
    internal const int DISPLAY_DEVICE_MIRRORING_DRIVER = 0x8;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields;
        public int dmPositionX, dmPositionY;
        public int dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    internal const int ENUM_CURRENT_SETTINGS = -1;
    internal const int DM_BITSPERPEL = 0x40000, DM_PELSWIDTH = 0x80000, DM_PELSHEIGHT = 0x100000, DM_DISPLAYFREQUENCY = 0x400000;
    internal const int CDS_UPDATEREGISTRY = 0x1, CDS_TEST = 0x2;
    internal const int DISP_CHANGE_SUCCESSFUL = 0, DISP_CHANGE_RESTART = 1;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool EnumDisplayDevicesW(string? device, int devNum, ref DISPLAY_DEVICE displayDevice, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool EnumDisplaySettingsExW(string device, int modeNum, ref DEVMODE devMode, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int ChangeDisplaySettingsExW(string device, ref DEVMODE devMode, IntPtr hwnd, int flags, IntPtr param);

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor, rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    internal delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref RECT rect, IntPtr data);

    [DllImport("user32.dll")]
    internal static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool GetMonitorInfoW(IntPtr monitor, ref MONITORINFOEX info);

    [DllImport("shcore.dll")]
    internal static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    // ------------------------------------------------------------------ user32 settings
    internal const uint SPIF_UPDATEINIFILE = 0x1, SPIF_SENDCHANGE = 0x2;
    internal const uint SPI_GETMOUSE = 0x0003, SPI_SETMOUSE = 0x0004;
    internal const uint SPI_GETANIMATION = 0x0048, SPI_SETANIMATION = 0x0049;
    internal const uint SPI_GETDRAGFULLWINDOWS = 0x0026, SPI_SETDRAGFULLWINDOWS = 0x0025;

    [StructLayout(LayoutKind.Sequential)]
    internal struct ANIMATIONINFO { public uint cbSize; public int iMinAnimate; }

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SystemParametersInfoW(uint action, uint uiParam, IntPtr pvParam, uint winIni);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SystemParametersInfoW(uint action, uint uiParam, int[] pvParam, uint winIni);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SystemParametersInfoW(uint action, uint uiParam, ref ANIMATIONINFO pvParam, uint winIni);

    internal static readonly IntPtr HWND_BROADCAST = new(0xffff);
    internal const uint WM_SETTINGCHANGE = 0x001A, SMTO_ABORTIFHUNG = 0x0002;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr SendMessageTimeoutW(IntPtr hwnd, uint msg, IntPtr wParam, string? lParam, uint flags, uint timeout, out IntPtr result);

    // ------------------------------------------------------------------ shell32 / wininet
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern int SHQueryRecycleBinW(string? rootPath, ref SHQUERYRBINFO info);

    internal const uint SHERB_NOCONFIRMATION = 0x1, SHERB_NOPROGRESSUI = 0x2, SHERB_NOSOUND = 0x4;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern int SHEmptyRecycleBinW(IntPtr hwnd, string? rootPath, uint flags);

    [DllImport("shell32.dll")]
    internal static extern int SHGetKnownFolderPath(in Guid folderId, uint flags, IntPtr token, out IntPtr path);

    internal static readonly Guid FOLDERID_Downloads = new("374DE290-123F-4565-9164-39C4925E467B");

    [DllImport("wininet.dll", SetLastError = true)]
    internal static extern bool InternetSetOptionW(IntPtr hInternet, int option, IntPtr buffer, int length);

    internal const int INTERNET_OPTION_REFRESH = 37, INTERNET_OPTION_SETTINGS_CHANGED = 39;

    // ------------------------------------------------------------------ advapi32 services
    internal const uint SC_MANAGER_CONNECT = 0x0001;
    internal const uint SERVICE_CHANGE_CONFIG = 0x0002, SERVICE_QUERY_CONFIG = 0x0001;
    internal const uint SERVICE_NO_CHANGE = 0xFFFFFFFF;
    internal const uint SERVICE_AUTO_START = 2, SERVICE_DEMAND_START = 3, SERVICE_DISABLED = 4;
    internal const uint SERVICE_CONFIG_DELAYED_AUTO_START_INFO = 3;

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr OpenSCManagerW(string? machine, string? database, uint access);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr OpenServiceW(IntPtr scm, string name, uint access);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool ChangeServiceConfigW(IntPtr service, uint serviceType, uint startType, uint errorControl, string? binaryPath,
        string? loadOrderGroup, IntPtr tagId, string? dependencies, string? startName, string? password, string? displayName);

    [StructLayout(LayoutKind.Sequential)]
    internal struct SERVICE_DELAYED_AUTO_START_INFO { public int fDelayedAutostart; }

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool ChangeServiceConfig2W(IntPtr service, uint infoLevel, ref SERVICE_DELAYED_AUTO_START_INFO info);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool CloseServiceHandle(IntPtr handle);
}

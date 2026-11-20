namespace SoftReset.Core.Safety;

/// <summary>
/// Components the console refuses to terminate, disable, or uninstall. These lists are
/// intentionally conservative: being unable to change something is always safer than
/// breaking a customer's computer.
/// </summary>
public static class ProtectedComponents
{
    /// <summary>Processes whose termination crashes Windows, logs the user off, or removes security.</summary>
    public static readonly HashSet<string> CriticalProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "System Idle Process", "Registry", "Memory Compression", "Secure System",
        "smss.exe", "csrss.exe", "wininit.exe", "winlogon.exe", "services.exe", "lsass.exe", "LsaIso.exe",
        "svchost.exe", "dwm.exe", "fontdrvhost.exe", "sihost.exe", "MsMpEng.exe", "NisSrv.exe",
        "MpDefenderCoreService.exe", "SecurityHealthService.exe", "SgrmBroker.exe", "audiodg.exe",
        "spoolsv.exe", "WUDFHost.exe", "conhost.exe", "ctfmon.exe", "TrustedInstaller.exe",
        "TiWorker.exe", "MsSense.exe", "smartscreen.exe", "dllhost.exe", "WmiPrvSE.exe",
        "SoftResetConsole.exe",
    };

    /// <summary>Services the console never changes the startup type of, and never stops.</summary>
    public static readonly HashSet<string> CriticalServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "RpcSs", "RpcEptMapper", "DcomLaunch", "LSM", "SamSs", "EventLog", "PlugPlay", "Power",
        "ProfSvc", "gpsvc", "Winmgmt", "CryptSvc", "BFE", "mpssvc", "WinDefend", "WdNisSvc",
        "SecurityHealthService", "Sense", "wscsvc", "SgrmBroker", "BrokerInfrastructure",
        "CoreMessagingRegistrar", "SystemEventsBroker", "TimeBrokerSvc", "StateRepository",
        "UserManager", "Schedule", "AudioSrv", "AudioEndpointBuilder", "Dhcp", "Dnscache",
        "nsi", "NlaSvc", "netprofm", "LanmanWorkstation", "Themes", "ShellHWDetection",
        "TrustedInstaller", "msiserver", "AppXSvc", "ClipSVC", "wuauserv", "UsoSvc", "BITS",
        "WaaSMedicSvc", "DeviceInstall", "DsmSvc", "KeyIso", "VaultSvc", "Netlogon", "EFS",
        "BDESVC", "TokenBroker", "camsvc", "WpnService", "WpnUserService", "CDPSvc",
        "Appinfo", "seclogon", "SENS", "EventSystem", "FontCache", "Wcmsvc", "WlanSvc",
        "iphlpsvc", "W32Time", "VSS", "swprv", "SystemRestore", "srservice",
    };

    /// <summary>Applications whose removal breaks Windows features or other software.</summary>
    public static readonly string[] EssentialAppPatterns =
    {
        "Microsoft Edge WebView2 Runtime",
        "Microsoft Visual C++",
        "Microsoft .NET",
        "Microsoft Windows Desktop Runtime",
        "Microsoft ASP.NET Core",
        "Windows Software Development Kit",
        "Microsoft Update Health Tools",
        "Windows PC Health Check",
        "Microsoft.WindowsStore",
        "Microsoft.StorePurchaseApp",
        "Microsoft.DesktopAppInstaller",
        "Microsoft.SecHealthUI",
        "Microsoft.VCLibs",
        "Microsoft.UI.Xaml",
        "Microsoft.NET.Native",
        "Microsoft.WindowsAppRuntime",
        "Microsoft.Windows.ShellExperienceHost",
        "Microsoft.Windows.StartMenuExperienceHost",
        "MicrosoftWindows.Client",
        "Microsoft.AAD.BrokerPlugin",
        "Microsoft.AccountsControl",
        "Microsoft.LockApp",
        "Microsoft.Windows.CloudExperienceHost",
        "windows.immersivecontrolpanel",
        "Microsoft.Windows.SecureAssessmentBrowser",
        "Intel(R) Management Engine",
        "Intel(R) Chipset",
        "AMD Chipset",
        "Realtek High Definition Audio Driver",
        "NVIDIA Graphics Driver",
        "AMD Software",
        "Intel(R) Graphics",
    };

    public static bool IsCriticalProcess(string name) => CriticalProcesses.Contains(name);

    public static bool IsCriticalService(string name) => CriticalServices.Contains(name);

    public static bool IsEssentialApp(string nameOrPackage) =>
        EssentialAppPatterns.Any(p => nameOrPackage.Contains(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>Folders the file organizer will never scan for moves or touch.</summary>
    public static bool IsProtectedFolder(string fullPath, Platform.IKnownFolders folders)
    {
        var p = Normalize(fullPath);
        string[] blocked =
        {
            folders.WindowsDir, folders.ProgramFiles, folders.ProgramFilesX86, folders.ProgramData,
            folders.AppDataLocal, folders.AppDataRoaming,
        };
        foreach (var b in blocked.Where(b => !string.IsNullOrWhiteSpace(b)))
        {
            var nb = Normalize(b);
            if (p.Equals(nb, StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith(nb + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        // The profile root itself (C:\Users\Name) and drive roots are too broad to reorganise.
        if (!string.IsNullOrWhiteSpace(folders.UserProfile) && p.Equals(Normalize(folders.UserProfile), StringComparison.OrdinalIgnoreCase))
            return true;
        if (Path.GetPathRoot(p) is { } root && Normalize(root).Equals(p, StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}

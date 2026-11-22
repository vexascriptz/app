using SoftReset.Core.Collection;
using SoftReset.Core.Models;
using SoftReset.Core.Optimization.Actions;
using SoftReset.Core.Platform;
using SoftReset.Core.Startup;

namespace SoftReset.Platform.Simulated;

/// <summary>Seed data for the demo PC: a gaming desktop with typical, fixable problems.</summary>
internal static class Scenario
{
    public static readonly string[] AllowedSchemes = { "ms-settings:", "windowsdefender:", "https:", "ms-availablenetworks:", "ms-actioncenter:" };

    public const string WingetUpgradeOutput =
        "   - \r\n   \\ \r\nName                         Id                          Version        Available      Source\r\n" +
        "------------------------------------------------------------------------------------------------\r\n" +
        "Google Chrome                Google.Chrome               129.0.6668.90  131.0.6778.86  winget\r\n" +
        "7-Zip 23.01 (x64)            7zip.7zip                   23.01          24.08          winget\r\n" +
        "VLC media player             VideoLAN.VLC                3.0.18         3.0.21         winget\r\n" +
        "Zoom Workplace               Zoom.Zoom                   6.0.2.33403    6.2.11.51470   winget\r\n" +
        "4 upgrades available.\r\n";

    private static readonly DateTimeOffset Now = DateTimeOffset.Now;

    public static void Seed(SimulatedPlatform p)
    {
        var r = p.Registry;
        var f = p.Folders;
        var st = p.State;

        // ---- OS identity
        const string nt = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        r.Set(RegHive.LocalMachine, nt, "ProductName", "Windows 10 Home");
        r.Set(RegHive.LocalMachine, nt, "DisplayVersion", "23H2");
        r.Set(RegHive.LocalMachine, nt, "EditionID", "Core");
        r.Set(RegHive.LocalMachine, nt, "CurrentBuildNumber", "22631");
        r.Set(RegHive.LocalMachine, nt, "UBR", 4460);

        // ---- Startup entries (HKCU Run)
        void Run(RegHive hive, string path, string name, string cmd) => r.Set(hive, path, name, cmd);
        var local = f.AppDataLocal;
        var roaming = f.AppDataRoaming;
        Run(RegHive.CurrentUser, StartupInventory.RunPath, "Discord", $"\"{local}\\Discord\\Update.exe\" --processStart Discord.exe");
        Run(RegHive.CurrentUser, StartupInventory.RunPath, "Spotify", $"\"{roaming}\\Spotify\\Spotify.exe\" --autostart --minimized");
        Run(RegHive.CurrentUser, StartupInventory.RunPath, "Steam", "\"C:\\Program Files (x86)\\Steam\\steam.exe\" -silent");
        Run(RegHive.CurrentUser, StartupInventory.RunPath, "EpicGamesLauncher", "\"C:\\Program Files (x86)\\Epic Games\\Launcher\\Portal\\Binaries\\Win32\\EpicGamesLauncher.exe\" -silent");
        Run(RegHive.CurrentUser, StartupInventory.RunPath, "OneDrive", $"\"{local}\\Microsoft\\OneDrive\\OneDrive.exe\" /background");
        Run(RegHive.CurrentUser, StartupInventory.RunPath, "MicrosoftEdgeAutoLaunch_6A8B", "\"C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe\" --no-startup-window --win-session-start");
        Run(RegHive.CurrentUser, StartupInventory.RunPath, "CCleaner Smart Cleaning", "\"C:\\Program Files\\CCleaner\\CCleaner64.exe\" /MONITOR");
        Run(RegHive.CurrentUser, StartupInventory.RunPath, "Opera GX Browser Assistant", $"{local}\\Programs\\Opera GX\\assistant\\browser_assistant.exe");
        Run(RegHive.CurrentUser, StartupInventory.RunPath, "Adobe Creative Cloud", "\"C:\\Program Files\\Adobe\\Adobe Creative Cloud\\ACC\\Creative Cloud.exe\" --showwindow=false --onOSstartup=true");
        Run(RegHive.CurrentUser, StartupInventory.RunPath, "Overwolf", "\"C:\\Program Files (x86)\\Overwolf\\OverwolfLauncher.exe\" -launcher");
        // HKLM Run
        Run(RegHive.LocalMachine, StartupInventory.RunPath, "SecurityHealth", "%windir%\\system32\\SecurityHealthSystray.exe");
        Run(RegHive.LocalMachine, StartupInventory.RunPath, "RtkAudUService", "\"C:\\Windows\\System32\\DriverStore\\FileRepository\\realtekservice.inf_amd64\\RtkAudUService64.exe\" -background");
        Run(RegHive.LocalMachine, StartupInventory.RunPath, "Corsair iCUE5 Software", "\"C:\\Program Files\\Corsair\\Corsair iCUE5 Software\\iCUE Launcher.exe\" --autorun");
        Run(RegHive.LocalMachine, StartupInventory.RunPathWow64, "Razer Synapse", "\"C:\\Program Files (x86)\\Razer\\Synapse3\\WPFUI\\Framework\\Razer Synapse 3 Host\\Razer Synapse 3.exe\" /autostart");
        // A disabled entry (Task Manager style)
        r.SetValue(RegHive.CurrentUser, $@"{StartupInventory.ApprovedRoot}\Run", "OneDrive", new RegValue(RegKind.Binary, StartupInventory.BuildApprovedValue(false, DateTime.UtcNow.AddDays(-40))));
        File.WriteAllText(Path.Combine(f.StartupUser, "Send to OneNote.lnk"), "lnk");

        // ---- Settings
        r.Set(RegHive.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1);
        r.Set(RegHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 1);
        r.Set(RegHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "HistoricalCaptureEnabled", 1);
        r.Set(RegHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 1);
        r.Set(RegHive.CurrentUser, @"Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings", "VRROptimizeEnable=0;");
        r.Set(RegHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideFileExt", 1);
        r.Set(RegHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Hidden", 2);
        r.Set(RegHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAl", 1);
        r.Set(RegHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
        r.Set(RegHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0);
        r.Set(RegHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 1);
        r.Set(RegHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", "ProxyEnable", 0);
        r.Set(RegHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\PushNotifications", "ToastEnabled", 1);
        r.Set(RegHive.CurrentUser, @"Software\Microsoft\Clipboard", "EnableClipboardHistory", 0);
        r.Set(RegHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 1);
        r.Set(RegHive.CurrentUser, @"Control Panel\Mouse", "MouseSpeed", "1");
        r.Set(RegHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 1);
        r.Set(RegHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA", 1);
        r.Set(RegHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "ConsentPromptBehaviorAdmin", 5);
        r.Set(RegHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled", 1);
        r.Set(RegHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Terminal Server", "fDenyTSConnections", 1);
        r.Set(RegHive.LocalMachine, @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings", "ActiveHoursStart", 8);
        r.Set(RegHive.LocalMachine, @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings", "ActiveHoursEnd", 23);

        // ---- Installed desktop programs
        void App(string key, string name, string publisher, string version, int sizeMb, string uninstall, bool msi = false, bool machine = true, string? date = "20240312", bool systemComponent = false)
        {
            var root = machine ? Core.Apps.UninstallRegistryReader.UninstallPath : Core.Apps.UninstallRegistryReader.UninstallPath;
            var hive = machine ? RegHive.LocalMachine : RegHive.CurrentUser;
            var k = $@"{root}\{key}";
            r.Set(hive, k, "DisplayName", name);
            r.Set(hive, k, "Publisher", publisher);
            r.Set(hive, k, "DisplayVersion", version);
            r.Set(hive, k, "EstimatedSize", sizeMb * 1024);
            r.Set(hive, k, "UninstallString", uninstall);
            if (date is not null) r.Set(hive, k, "InstallDate", date);
            if (msi) r.Set(hive, k, "WindowsInstaller", 1);
            if (systemComponent) r.Set(hive, k, "SystemComponent", 1);
        }
        App("Discord", "Discord", "Discord Inc.", "1.0.9172", 412, $"\"{local}\\Discord\\Update.exe\" --uninstall", machine: false, date: "20240105");
        App("Spotify", "Spotify", "Spotify AB", "1.2.48.405", 380, $"\"{roaming}\\Spotify\\Spotify.exe\" /uninstall", machine: false);
        App("Steam", "Steam", "Valve Corporation", "2.10.91.91", 610, "\"C:\\Program Files (x86)\\Steam\\uninstall.exe\"");
        App("Google Chrome", "Google Chrome", "Google LLC", "129.0.6668.90", 520, "\"C:\\Program Files\\Google\\Chrome\\Application\\129.0.6668.90\\Installer\\setup.exe\" --uninstall --channel=stable --system-level");
        App("Microsoft Edge", "Microsoft Edge", "Microsoft Corporation", "131.0.2903.70", 750, "\"C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\131.0.2903.70\\Installer\\setup.exe\" --uninstall");
        App("Microsoft EdgeWebView", "Microsoft Edge WebView2 Runtime", "Microsoft Corporation", "131.0.2903.70", 690, "setup.exe --uninstall --msedgewebview");
        App("{9E1E5F64-5E3F-4C52-AB9A-1B1E3C0F2B10}", "7-Zip 23.01 (x64 edition)", "Igor Pavlov", "23.01.00.0", 6, "MsiExec.exe /I{9E1E5F64-5E3F-4C52-AB9A-1B1E3C0F2B10}", msi: true);
        App("{E4D6F1C0-3B6A-4B5E-9D8F-6A5C4B3A2910}", "Epic Games Launcher", "Epic Games, Inc.", "1.3.93.0", 330, "MsiExec.exe /X{E4D6F1C0-3B6A-4B5E-9D8F-6A5C4B3A2910}", msi: true);
        App("{B0169E83-757B-4C8C-9A79-4E2A7F9E3B11}", "NVIDIA Graphics Driver 536.23", "NVIDIA Corporation", "536.23", 1400, "\"C:\\Windows\\SysWOW64\\RunDll32.EXE\" \"C:\\Program Files\\NVIDIA Corporation\\Installer2\\InstallerCore\\NVI2.DLL\",UninstallPackage Display.Driver");
        App("{C3A1D4E2-1F5A-4A0B-8E7C-2D3F4B5A6C12}", "Microsoft Visual C++ 2015-2022 Redistributable (x64) - 14.40.33810", "Microsoft Corporation", "14.40.33810.0", 20, "\"C:\\ProgramData\\Package Cache\\{C3A1D4E2}\\VC_redist.x64.exe\" /uninstall");
        App("OBS Studio", "OBS Studio", "OBS Project", "30.2.3", 420, "\"C:\\Program Files\\obs-studio\\uninstall.exe\"");
        App("VLC media player", "VLC media player", "VideoLAN", "3.0.18", 160, "\"C:\\Program Files\\VideoLAN\\VLC\\uninstall.exe\"");
        App("ZoomUMX", "Zoom Workplace", "Zoom Video Communications, Inc.", "6.0.2.33403", 290, $"\"{roaming}\\Zoom\\uninstall\\Installer.exe\" /uninstall", machine: false);
        App("CCleaner", "CCleaner", "Piriform Software Ltd", "6.24", 110, "\"C:\\Program Files\\CCleaner\\uninst.exe\"");
        App("Overwolf", "Overwolf", "Overwolf Ltd.", "0.262.1.4", 380, "\"C:\\Program Files (x86)\\Overwolf\\OWUninstaller.exe\"");
        App("{Corsair-iCUE5}", "iCUE", "Corsair", "5.18.98", 900, "MsiExec.exe /X{Corsair-iCUE5}");
        App("Razer Synapse", "Razer Synapse", "Razer Inc.", "3.9.1031.101114", 640, "\"C:\\Program Files (x86)\\Razer\\Razer Services\\Razer Synapse\\Razer Synapse Uninstaller.exe\"");
        App("Adobe Creative Cloud", "Adobe Creative Cloud", "Adobe Inc.", "6.4.0.361", 1200, "\"C:\\Program Files (x86)\\Adobe\\Adobe Creative Cloud\\Utils\\Creative Cloud Uninstaller.exe\"");
        App("KB-update", "Security Update for Microsoft Windows (KB5034441)", "Microsoft Corporation", "1", 1, "x", systemComponent: true);

        st.StorePackages.AddRange(new[]
        {
            Store("Microsoft.WindowsCalculator_8wekyb3d8bbwe", "Windows Calculator", "11.2405.2.0", inbox: true),
            Store("Microsoft.Windows.Photos_8wekyb3d8bbwe", "Microsoft Photos", "2024.11050.3002.0", inbox: true),
            Store("Microsoft.WindowsTerminal_8wekyb3d8bbwe", "Windows Terminal", "1.21.2911.0", inbox: true),
            Store("Microsoft.WindowsStore_8wekyb3d8bbwe", "Microsoft Store", "22410.1401.5.0", inbox: true),
            Store("Microsoft.XboxGamingOverlay_8wekyb3d8bbwe", "Xbox Game Bar", "7.124.10241.0", inbox: true),
            Store("Microsoft.GamingApp_8wekyb3d8bbwe", "Xbox", "2410.1001.20.0", inbox: false),
            Store("Clipchamp.Clipchamp_yxz26nhyzhsrt", "Microsoft Clipchamp", "3.1.12020.0", inbox: true),
            Store("MSTeams_8wekyb3d8bbwe", "Microsoft Teams", "24295.605.3225.8804", inbox: false),
            Store("NVIDIACorp.NVIDIAControlPanel_56jybvy8sckqj", "NVIDIA Control Panel", "8.1.967.0", inbox: false),
            Store("SpotifyAB.SpotifyMusic_zpdnekdrzrea0", "Spotify Music", "1.248.405.0", inbox: false),
        });
        st.Features.AddRange(new[]
        {
            new OptionalFeatureInfo { Name = "Microsoft-Windows-Subsystem-Linux", Caption = "Windows Subsystem for Linux", State = "Enabled" },
            new OptionalFeatureInfo { Name = "VirtualMachinePlatform", Caption = "Virtual Machine Platform", State = "Enabled" },
            new OptionalFeatureInfo { Name = "NetFx3", Caption = ".NET Framework 3.5 (includes .NET 2.0 and 3.0)", State = "Disabled" },
            new OptionalFeatureInfo { Name = "Microsoft-Hyper-V-All", Caption = "Hyper-V", State = "Disabled" },
            new OptionalFeatureInfo { Name = "Containers-DisposableClientVM", Caption = "Windows Sandbox", State = "Disabled" },
            new OptionalFeatureInfo { Name = "SMB1Protocol", Caption = "SMB 1.0/CIFS File Sharing Support", State = "Disabled" },
            new OptionalFeatureInfo { Name = "TelnetClient", Caption = "Telnet Client", State = "Disabled" },
            new OptionalFeatureInfo { Name = "Printing-PrintToPDFServices-Features", Caption = "Microsoft Print to PDF", State = "Enabled" },
        });

        // ---- Power
        st.Schemes.Add(new PowerScheme { Guid = WellKnownPowerSchemes.Balanced, Name = "Balanced" });
        st.Schemes.Add(new PowerScheme { Guid = WellKnownPowerSchemes.PowerSaver, Name = "Power saver" });
        st.ActiveScheme = WellKnownPowerSchemes.PowerSaver;

        // ---- Displays
        var gaming = new DisplayInfo { DeviceName = @"\\.\DISPLAY1", MonitorName = "ASUS VG27AQ", AdapterName = "NVIDIA GeForce RTX 3070", Width = 2560, Height = 1440, RefreshHz = 60, BitsPerPixel = 32, IsPrimary = true, ScalePercent = 100 };
        foreach (var hz in new[] { 60, 75, 100, 120, 144, 165 }) gaming.SupportedModes.Add(new DisplayMode(2560, 1440, hz, 32));
        foreach (var hz in new[] { 60, 120, 144 }) gaming.SupportedModes.Add(new DisplayMode(1920, 1080, hz, 32));
        var second = new DisplayInfo { DeviceName = @"\\.\DISPLAY2", MonitorName = "DELL P2419H", AdapterName = "NVIDIA GeForce RTX 3070", Width = 1920, Height = 1080, RefreshHz = 60, BitsPerPixel = 32, ScalePercent = 100 };
        second.SupportedModes.Add(new DisplayMode(1920, 1080, 60, 32));
        second.SupportedModes.Add(new DisplayMode(1280, 720, 60, 32));
        st.Displays.Add(gaming);
        st.Displays.Add(second);

        foreach (var e in UiEffects.All) st.UiEffects[e] = true;

        // ---- Processes
        var pid = 4;
        void P(string name, string? path, long privMb, int threads = 12, int session = 1, string? company = null, string? desc = null, SignatureState sig = SignatureState.Signed, string? signer = null)
        {
            pid += 4 + (pid % 7);
            st.Processes.Add(new ProcessInfo
            {
                Pid = pid, Name = name, Path = path, SessionId = session, Threads = threads, Handles = threads * 30,
                PrivateBytes = privMb * 1024 * 1024, WorkingSetBytes = (long)(privMb * 1.3 * 1024 * 1024),
                TotalProcessorTime = TimeSpan.FromSeconds(pid % 97), StartTime = Now.AddHours(-3), PathAccessible = path is not null,
            });
            if (path is not null)
                st.FileDetails[path] = new FileDetails { Company = company, Description = desc ?? Path.GetFileNameWithoutExtension(name), Signature = sig, Signer = signer ?? company, FileVersion = "1.0" };
        }
        var sys = @"C:\Windows\System32\";
        P("System", null, 0, 180, 0); st.ProtectedPids.Add(pid);
        P("Registry", null, 12, 4, 0);
        P("smss.exe", sys + "smss.exe", 1, 2, 0, "Microsoft Corporation", "Windows Session Manager", SignatureState.SignedMicrosoftWindows, "Microsoft Windows");
        P("csrss.exe", null, 2, 12, 0);
        P("wininit.exe", sys + "wininit.exe", 1, 1, 0, "Microsoft Corporation", "Windows Start-Up Application", SignatureState.SignedMicrosoftWindows, "Microsoft Windows");
        P("services.exe", null, 6, 8, 0);
        P("lsass.exe", null, 9, 10, 0);
        for (var i = 0; i < 68; i++) P("svchost.exe", sys + "svchost.exe", 8 + i % 23, 6 + i % 9, i % 5 == 0 ? 1 : 0, "Microsoft Corporation", "Host Process for Windows Services", SignatureState.SignedMicrosoftWindows, "Microsoft Windows");
        P("dwm.exe", sys + "dwm.exe", 180, 20, 1, "Microsoft Corporation", "Desktop Window Manager", SignatureState.SignedMicrosoftWindows, "Microsoft Windows");
        P("explorer.exe", @"C:\Windows\explorer.exe", 160, 90, 1, "Microsoft Corporation", "Windows Explorer", SignatureState.SignedMicrosoftWindows, "Microsoft Windows");
        P("MsMpEng.exe", null, 260, 40, 0);
        P("SearchIndexer.exe", sys + "SearchIndexer.exe", 70, 20, 0, "Microsoft Corporation", "Microsoft Windows Search Indexer", SignatureState.SignedMicrosoftWindows, "Microsoft Windows");
        P("RuntimeBroker.exe", sys + "RuntimeBroker.exe", 18, 8, 1, "Microsoft Corporation", "Runtime Broker", SignatureState.SignedMicrosoftWindows, "Microsoft Windows");
        P("ctfmon.exe", sys + "ctfmon.exe", 10, 10, 1, "Microsoft Corporation", "CTF Loader", SignatureState.SignedMicrosoftWindows, "Microsoft Windows");
        P("SecurityHealthSystray.exe", sys + "SecurityHealthSystray.exe", 4, 4, 1, "Microsoft Corporation", "Windows Security notification icon", SignatureState.SignedMicrosoftWindows, "Microsoft Windows");
        P("GameBar.exe", @"C:\Program Files\WindowsApps\Microsoft.XboxGamingOverlay\GameBar.exe", 60, 30, 1, "Microsoft Corporation", "Xbox Game Bar", SignatureState.Signed, "Microsoft Corporation");
        P("NVDisplay.Container.exe", @"C:\Windows\System32\DriverStore\FileRepository\nv_dispi.inf_amd64\Display.NvContainer\NVDisplay.Container.exe", 40, 30, 0, "NVIDIA Corporation", "NVIDIA Container", SignatureState.Signed, "NVIDIA Corporation");
        P("NVIDIA Overlay.exe", @"C:\Program Files\NVIDIA Corporation\NVIDIA app\CEF\NVIDIA Overlay.exe", 120, 40, 1, "NVIDIA Corporation", "NVIDIA Overlay", SignatureState.Signed, "NVIDIA Corporation");
        P("Discord.exe", $@"{local}\Discord\app-1.0.9172\Discord.exe", 420, 60, 1, "Discord Inc.", "Discord", SignatureState.Signed, "Discord Inc.");
        P("Discord.exe", $@"{local}\Discord\app-1.0.9172\Discord.exe", 260, 30, 1, "Discord Inc.", "Discord", SignatureState.Signed, "Discord Inc.");
        P("Spotify.exe", $@"{roaming}\Spotify\Spotify.exe", 310, 50, 1, "Spotify Ltd", "Spotify", SignatureState.Signed, "Spotify AB");
        P("steam.exe", @"C:\Program Files (x86)\Steam\steam.exe", 140, 70, 1, "Valve Corporation", "Steam", SignatureState.Signed, "Valve Corp.");
        P("steamwebhelper.exe", @"C:\Program Files (x86)\Steam\bin\cef\cef.win7x64\steamwebhelper.exe", 380, 40, 1, "Valve Corporation", "Steam Client WebHelper", SignatureState.Signed, "Valve Corp.");
        P("EpicGamesLauncher.exe", @"C:\Program Files (x86)\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe", 210, 60, 1, "Epic Games, Inc.", "EpicGamesLauncher", SignatureState.Signed, "Epic Games Inc.");
        for (var i = 0; i < 24; i++) P("chrome.exe", @"C:\Program Files\Google\Chrome\Application\chrome.exe", 90 + i * 17 % 260, 14, 1, "Google LLC", "Google Chrome", SignatureState.Signed, "Google LLC");
        for (var i = 0; i < 9; i++) P("msedgewebview2.exe", @"C:\Program Files (x86)\Microsoft\EdgeWebView\Application\131.0.2903.70\msedgewebview2.exe", 40 + i * 9, 12, 1, "Microsoft Corporation", "Microsoft Edge WebView2", SignatureState.Signed, "Microsoft Corporation");
        P("OverwolfBrowser.exe", @"C:\Program Files (x86)\Overwolf\0.262.1.4\OverwolfBrowser.exe", 230, 30, 1, "Overwolf Ltd.", "Overwolf Browser", SignatureState.Signed, "Overwolf Ltd");
        P("Overwolf.exe", @"C:\Program Files (x86)\Overwolf\0.262.1.4\Overwolf.exe", 150, 40, 1, "Overwolf Ltd.", "Overwolf", SignatureState.Signed, "Overwolf Ltd");
        P("iCUE.exe", @"C:\Program Files\Corsair\Corsair iCUE5 Software\iCUE.exe", 330, 70, 1, "Corsair", "iCUE", SignatureState.Signed, "Corsair Memory, Inc.");
        P("RazerCentralService.exe", @"C:\Program Files (x86)\Razer\Razer Services\Razer Central\RazerCentralService.exe", 90, 30, 0, "Razer Inc.", "Razer Central Service", SignatureState.Signed, "Razer USA Ltd.");
        P("Creative Cloud.exe", @"C:\Program Files\Adobe\Adobe Creative Cloud\ACC\Creative Cloud.exe", 250, 50, 1, "Adobe Inc.", "Creative Cloud", SignatureState.Signed, "Adobe Inc.");
        P("CCleaner64.exe", @"C:\Program Files\CCleaner\CCleaner64.exe", 60, 20, 1, "Piriform Software Ltd", "CCleaner", SignatureState.Signed, "Piriform Software Ltd");
        P("RtkAudUService64.exe", @"C:\Windows\System32\DriverStore\FileRepository\realtekservice.inf_amd64\RtkAudUService64.exe", 6, 6, 1, "Realtek Semiconductor", "Realtek HD Audio Universal Service", SignatureState.Signed, "Realtek Semiconductor Corp.");
        P("syshelper.exe", $@"{roaming}\syshelper.exe", 24, 6, 1, null, null, SignatureState.Unsigned);
        for (var i = 0; i < 30; i++) P("conhost.exe", sys + "conhost.exe", 6, 3, 1, "Microsoft Corporation", "Console Window Host", SignatureState.SignedMicrosoftWindows, "Microsoft Windows");

        // ---- Services
        void S(string name, string display, string mode, string state, bool ms = true, string? path = null, int pidLink = 0) =>
            st.Services.Add(new ServiceInfo { Name = name, DisplayName = display, StartMode = mode, State = state, IsMicrosoft = ms, PathName = path, ProcessId = pidLink, Description = display });
        S("wuauserv", "Windows Update", "Manual", "Running");
        S("bits", "Background Intelligent Transfer Service", "Manual", "Stopped");
        S("WSearch", "Windows Search", "Auto", "Running");
        S("Spooler", "Print Spooler", "Auto", "Running");
        S("SysMain", "SysMain", "Auto", "Running");
        S("DiagTrack", "Connected User Experiences and Telemetry", "Auto", "Running");
        S("WinDefend", "Microsoft Defender Antivirus Service", "Auto", "Running");
        S("XblAuthManager", "Xbox Live Auth Manager", "Manual", "Stopped");
        S("NVDisplay.ContainerLocalSystem", "NVIDIA Display Container LS", "Auto", "Running", false);
        S("Razer Synapse Service", "Razer Synapse Service", "Auto", "Running", false);
        S("CorsairService", "Corsair Service", "Auto", "Running", false);
        S("CCleanerPerformanceOptimizerService", "CCleaner Performance Optimizer Service", "Auto", "Running", false);
        S("edgeupdate", "Microsoft Edge Update Service (edgeupdate)", "Auto", "Stopped", true);
        S("gupdate", "Google Update Service (gupdate)", "Auto", "Stopped", false);
        S("EpicOnlineServices", "Epic Online Services", "Manual", "Stopped", false);
        S("Steam Client Service", "Steam Client Service", "Manual", "Stopped", false);

        // ---- Scheduled tasks
        void T(string path, bool enabled, bool logon, string action, string author = "")
            => st.Tasks.Add(new ScheduledTaskInfo { Path = path, Name = path.Split('\\').Last(), Enabled = enabled, State = enabled ? "Ready" : "Disabled", HasLogonTrigger = logon, Action = action, Author = author, Triggers = logon ? "At log on" : "Daily", LastResult = "0x0" });
        T(@"\Opera GX scheduled assistant Autoupdate 1712345678", true, true, $"{local}\\Programs\\Opera GX\\launcher.exe --scheduledautoupdate", "Opera");
        T(@"\CCleaner Update", true, true, "\"C:\\Program Files\\CCleaner\\CCUpdate.exe\"", "Piriform");
        T(@"\Overwolf Updater Task", true, true, "\"C:\\Program Files (x86)\\Overwolf\\OverwolfUpdater.exe\" -runpendingupdates", "Overwolf");
        T(@"\MicrosoftEdgeUpdateTaskMachineUA", true, false, "\"C:\\Program Files (x86)\\Microsoft\\EdgeUpdate\\MicrosoftEdgeUpdate.exe\" /ua", "Microsoft Corporation");
        T(@"\Microsoft\Windows\Defrag\ScheduledDefrag", true, false, "%windir%\\system32\\defrag.exe -c -h -o -$", "Microsoft Corporation");

        // ---- Events (last 30 days)
        void E(string log, string provider, int id, int daysAgo, string message, Dictionary<string, string>? data = null, string level = "Error")
            => st.Events.Add(new EventEntry { Log = log, Provider = provider, Id = id, Time = Now.AddDays(-daysAgo).AddHours(-daysAgo % 5), Message = message, Level = level, Data = data ?? new() });
        E("System", "Microsoft-Windows-Kernel-Power", 41, 3, "The system has rebooted without cleanly shutting down first.", new() { ["BugcheckCode"] = "0" }, "Critical");
        E("System", "Microsoft-Windows-Kernel-Power", 41, 17, "The system has rebooted without cleanly shutting down first.", new() { ["BugcheckCode"] = "0" }, "Critical");
        E("System", "EventLog", 6008, 3, "The previous system shutdown was unexpected.");
        E("System", "EventLog", 6008, 17, "The previous system shutdown was unexpected.");
        foreach (var d in new[] { 2, 3, 9, 17 })
            E("System", "Display", 4101, d, "Display driver nvlddmkm stopped responding and has successfully recovered.", level: "Warning");
        foreach (var d in new[] { 1, 2, 3, 6, 9, 12 })
            E("Application", "Application Error", 1000, d, "Faulting application name: FortniteClient-Win64-Shipping.exe, version: ++Fortnite+Release-32.10, Faulting module name: nvwgf2umx.dll",
                new() { ["AppName"] = "FortniteClient-Win64-Shipping.exe", ["ModuleName"] = "nvwgf2umx.dll", ["ExceptionCode"] = "c0000005" });
        E("System", "Microsoft-Windows-WindowsUpdateClient", 20, 5, "Installation Failure: Windows failed to install the following update with error 0x800F0922: 2024-01 Security Update for Windows (KB5034441).");
        E("System", "Microsoft-Windows-WindowsUpdateClient", 20, 12, "Installation Failure: Windows failed to install the following update with error 0x800F0922: 2024-01 Security Update for Windows (KB5034441).");
        foreach (var d in new[] { 1, 4, 8, 11, 15 })
            E("System", "Service Control Manager", 7031, d, "The Razer Synapse Service service terminated unexpectedly.", new() { ["param1"] = "Razer Synapse Service" });

        // ---- Sandbox files: temp folders with real (small) files so cleanup actions truly run
        SeedFiles(f.Temp, "tmp", 36, 64 * 1024, oldDays: 10);
        SeedFiles(Path.Combine(f.Temp, "ChromeInstaller"), "bin", 6, 256 * 1024, oldDays: 20);
        SeedFiles(Path.Combine(f.WindowsDir, "Temp"), "log", 12, 128 * 1024, oldDays: 30);
        SeedFiles(Path.Combine(f.WindowsDir, "SoftwareDistribution", "Download"), "cab", 8, 512 * 1024, oldDays: 6);
        SeedFiles(Path.Combine(f.AppDataRoaming, "discord", "Cache", "Cache_Data"), "dat", 20, 96 * 1024, oldDays: 2);
        SeedFiles(Path.Combine(f.AppDataRoaming, "discord", "Code Cache", "js"), "js", 10, 48 * 1024, oldDays: 2);
        SeedFiles(Path.Combine(f.AppDataRoaming, "discord", "GPUCache"), "bin", 4, 32 * 1024, oldDays: 2);
        SeedDownloads(f.Downloads);
    }

    private static InstalledApp Store(string family, string name, string version, bool inbox) => new()
    {
        Id = "store:" + family,
        Name = name,
        Publisher = family.StartsWith("Microsoft", StringComparison.Ordinal) || family.StartsWith("MSTeams", StringComparison.Ordinal) || family.StartsWith("Clipchamp", StringComparison.Ordinal) ? "Microsoft Corporation" : family.Split('.')[0],
        Version = version,
        Kind = AppKind.Store,
        PackageFamilyName = family,
        PackageFullName = family.Replace("_", $"_{version}_x64__"),
        IsInboxWindowsApp = inbox,
        InstallLocation = $@"C:\Program Files\WindowsApps\{family}",
        EstimatedSizeBytes = 40L * 1024 * 1024 + family.Length * 1_000_000L,
    };

    private static void SeedFiles(string dir, string ext, int count, int size, int oldDays)
    {
        Directory.CreateDirectory(dir);
        var data = new byte[size];
        new Random(count * size).NextBytes(data);
        for (var i = 0; i < count; i++)
        {
            var p = Path.Combine(dir, $"file{i:D3}.{ext}");
            File.WriteAllBytes(p, data.AsSpan(0, size - i * 7 % 1024).ToArray());
            var when = DateTime.UtcNow.AddDays(-oldDays - i % 5);
            File.SetLastWriteTimeUtc(p, when);
            File.SetCreationTimeUtc(p, when);
        }
        Directory.SetCreationTimeUtc(dir, DateTime.UtcNow.AddDays(-oldDays - 5));
    }

    private static void SeedDownloads(string dir)
    {
        var rnd = new Random(42);
        byte[] Bytes(int n) { var b = new byte[n]; rnd.NextBytes(b); return b; }
        var dupContent = Bytes(180_000);
        var files = new (string Name, byte[] Data, int AgeDays)[]
        {
            ("ChromeSetup.exe", Bytes(150_000), 200), ("DiscordSetup.exe", Bytes(120_000), 300), ("vlc-3.0.18-win64.exe", Bytes(140_000), 400),
            ("Invoice_March.pdf", dupContent, 90), ("Invoice_March (1).pdf", dupContent, 89), ("Invoice_March (2).pdf", dupContent, 30),
            ("holiday-photo-01.jpg", Bytes(90_000), 60), ("holiday-photo-02.jpg", Bytes(91_000), 60), ("holiday-photo-03.jpg", Bytes(92_000), 60),
            ("budget-2024.xlsx", Bytes(40_000), 500), ("resume_final_v3.docx", Bytes(35_000), 700), ("presentation.pptx", Bytes(70_000), 120),
            ("drivers-backup.zip", Bytes(160_000), 900), ("Windows11_InstallationAssistant.exe", Bytes(110_000), 380), ("song.mp3", Bytes(85_000), 50),
            ("clip.mp4", Bytes(190_000), 45), ("notes.txt", Bytes(4_000), 10),
        };
        foreach (var (name, data, age) in files)
        {
            var p = Path.Combine(dir, name);
            File.WriteAllBytes(p, data);
            File.SetLastWriteTimeUtc(p, DateTime.UtcNow.AddDays(-age));
            File.SetCreationTimeUtc(p, DateTime.UtcNow.AddDays(-age));
        }
    }

    public static double CpuRate(string name) => name switch
    {
        "MsMpEng.exe" => 3.5,
        "chrome.exe" => 0.9,
        "dwm.exe" => 2.5,
        "Discord.exe" => 1.8,
        "iCUE.exe" => 4.0,
        "OverwolfBrowser.exe" => 2.2,
        "steamwebhelper.exe" => 1.2,
        "svchost.exe" => 0.08,
        _ => 0.1,
    };

    public static List<GpuInfo> Gpus(bool telemetry) => new()
    {
        new GpuInfo
        {
            Name = "NVIDIA GeForce RTX 3070", Vendor = GpuVendor.Nvidia, DriverVersion = "31.0.15.3623", VendorDriverVersion = "536.23",
            DriverDate = new DateTimeOffset(2023, 6, 7, 0, 0, 0, TimeSpan.Zero), DedicatedMemoryBytes = 8L * 1024 * 1024 * 1024,
            PnpDeviceId = @"PCI\VEN_10DE&DEV_2484&SUBSYS_146710DE", HasAttachedDisplay = true,
            Telemetry = telemetry ? new GpuTelemetry
            {
                Source = "NVML (demo)", TemperatureC = 51, UtilizationPercent = 6, MemoryUsedBytes = 1_420L * 1024 * 1024, MemoryTotalBytes = 8L * 1024 * 1024 * 1024,
                PowerWatts = 34.2, PowerLimitWatts = 220, FanPercent = 30, GraphicsClockMHz = 345, MemoryClockMHz = 405, PerformanceState = "P8", PcieLink = "PCIe Gen4 x16",
            } : null,
        },
    };

    public static NvidiaDriverSettingsResult NvidiaSettings() => new()
    {
        Available = true,
        Status = "Read from the NVIDIA driver profile store (demo data).",
        DriverVersion = "536.23",
        DriverBranch = "r535_00",
        GlobalProfileName = "Base Profile",
        GlobalSettings =
        {
            new() { SettingId = 0x007BA09E, Name = "Maximum pre-rendered frames", Value = 1, Location = "Global" },
            new() { SettingId = 0x1057EB71, Name = "Power management mode", Value = 5, IsPredefined = true, Location = "Global" },
            new() { SettingId = 0x00A879CF, Name = "Vertical Sync", Value = 0x60925292, IsPredefined = true, Location = "Global" },
            new() { SettingId = 0x10835002, Name = "Frame Rate Limiter", Value = 0, IsPredefined = true, Location = "Global" },
            new() { SettingId = 0x00CE2691, Name = "Texture filtering - Quality", Value = 0, IsPredefined = true, Location = "Global" },
            new() { SettingId = 0x00198FFF, Name = "Shader Cache", Value = 1, IsPredefined = true, Location = "Global" },
            new() { SettingId = 0x00AC8497, Name = "Shader disk cache maximum size", Value = 10240, Location = "Global" },
            new() { SettingId = 0x1194F158, Name = "VRR mode", Value = 1, IsPredefined = true, Location = "Global" },
            new() { SettingId = 0x101E61A9, Name = "Anisotropic filtering setting", Value = 1, IsPredefined = true, Location = "Global" },
            new() { SettingId = 0x107EFC5B, Name = "Antialiasing - Mode", Value = 0, IsPredefined = true, Location = "Global" },
            new() { SettingId = 0x1074C972, Name = "Toggle FXAA on or off", Value = 0, IsPredefined = true, Location = "Global" },
            new() { SettingId = 0x20C1221E, Name = "Threaded optimization", Value = 0, IsPredefined = true, Location = "Global" },
        },
        CustomizedProfiles =
        {
            new() { Name = "Fortnite", ApplicationCount = 3, SettingCount = 41, UserModifiedSettingCount = 2, Applications = { "fortniteclient-win64-shipping.exe" },
                UserSettings = { new() { SettingId = 0x10835002, Name = "Frame Rate Limiter", Value = 141, Location = "Current" }, new() { SettingId = 0x1057EB71, Name = "Power management mode", Value = 1, Location = "Current" } } },
        },
    };

    public static SystemSnapshot BuildSnapshot(SimulatedPlatform p, CollectionOptions options)
    {
        var st = p.State;
        var reg = p.Registry;
        var s = new SystemSnapshot
        {
            CapturedAt = DateTimeOffset.Now,
            MachineName = "DESKTOP-DEMO01",
            CollectedElevated = p.IsElevated,
            PlatformName = p.Name,
            Os = RegistryReaders.ReadOs(reg),
        };
        s.Os.Architecture = "x64";
        s.Os.LastBootTime = Now.AddDays(-9).AddHours(-3);
        s.Os.InstallDate = Now.AddYears(-2);
        s.Os.ProductName = "Windows 11 Home";
        s.Hardware = new HardwareInfo
        {
            Manufacturer = "ASUS", Model = "System Product Name (custom build)", Motherboard = "ASUS ROG STRIX B550-F GAMING",
            CpuName = "AMD Ryzen 7 5800X 8-Core Processor", CpuCores = 8, CpuLogicalProcessors = 16, CpuMaxClockMHz = 3801,
            TotalMemoryBytes = 16L * 1024 * 1024 * 1024, BiosVersion = "3611", BiosDate = new DateTimeOffset(2023, 3, 1, 0, 0, 0, TimeSpan.Zero),
            MemoryModules =
            {
                new MemoryModule { Slot = "DIMM_A2", CapacityBytes = 8L * 1024 * 1024 * 1024, SpeedMHz = 3200, ConfiguredSpeedMHz = 2133, Manufacturer = "Corsair", PartNumber = "CMK16GX4M2E3200C16" },
                new MemoryModule { Slot = "DIMM_B2", CapacityBytes = 8L * 1024 * 1024 * 1024, SpeedMHz = 3200, ConfiguredSpeedMHz = 2133, Manufacturer = "Corsair", PartNumber = "CMK16GX4M2E3200C16" },
            },
        };

        // Processes and CPU
        var procs = p.Processes.Enumerate();
        var sampler = new ProcessCpuSampler(16);
        sampler.Sample(procs, DateTime.UtcNow.AddSeconds(-5));
        var cpu = sampler.Sample(p.Processes.Enumerate(), DateTime.UtcNow);
        s.ProcessCount = procs.Count;
        s.ThreadCount = procs.Sum(x => x.Threads);
        s.HandleCount = procs.Sum(x => x.Handles);
        var privTotal = procs.Sum(x => x.PrivateBytes);
        s.Performance = new PerformanceSample
        {
            SampleSeconds = options.CpuSampleDuration.TotalSeconds,
            CpuAveragePercent = Math.Round(cpu.Values.Sum() + 4, 1),
            CpuPeakPercent = Math.Round(cpu.Values.Sum() + 19, 1),
            CpuSeries = Enumerable.Range(0, 10).Select(i => Math.Round(cpu.Values.Sum() + 4 + Math.Sin(i) * 3, 1)).ToList(),
            MemoryTotalBytes = s.Hardware.TotalMemoryBytes,
            MemoryAvailableBytes = Math.Max(512L * 1024 * 1024, s.Hardware.TotalMemoryBytes - privTotal - 2_200_000_000),
            CommitTotalBytes = privTotal + 3_000_000_000,
            CommitLimitBytes = s.Hardware.TotalMemoryBytes + 4_000_000_000,
            CpuFrequencyPercentOfMax = 98,
        };
        s.TopProcesses = procs.GroupBy(x => x.Name).Select(g => new ProcessSample
        {
            Pid = g.First().Pid,
            Name = g.Key,
            Path = g.First().Path,
            CpuPercent = Math.Round(g.Sum(x => cpu.GetValueOrDefault(x.Pid)), 1),
            PrivateBytes = g.Sum(x => x.PrivateBytes),
            WorkingSetBytes = g.Sum(x => x.WorkingSetBytes),
            Publisher = g.First().Path is { } path && st.FileDetails.TryGetValue(path, out var fd) ? fd.Company : null,
        }).OrderByDescending(x => x.PrivateBytes).Take(options.TopProcessCount).ToList();

        // Startup (shared inventory code)
        s.StartupEntries = new StartupInventory(reg, p.Folders, p.FileExists).Enumerate(st.Tasks);
        foreach (var e in s.StartupEntries)
        {
            e.IsMicrosoft = e.Name is "SecurityHealth" or "OneDrive" || e.Name.StartsWith("MicrosoftEdge", StringComparison.Ordinal) || e.Name.Contains("OneNote", StringComparison.Ordinal);
            e.Publisher = e.Name switch
            {
                "Discord" => "Discord Inc.", "Spotify" => "Spotify AB", "Steam" => "Valve Corp.", "EpicGamesLauncher" => "Epic Games Inc.",
                "SecurityHealth" or "OneDrive" or "MicrosoftEdgeAutoLaunch_6A8B" => "Microsoft Corporation", "CCleaner Smart Cleaning" => "Piriform Software Ltd",
                "Adobe Creative Cloud" => "Adobe Inc.", "Overwolf" => "Overwolf Ltd", "Razer Synapse" => "Razer USA Ltd.", "Corsair iCUE5 Software" => "Corsair Memory, Inc.",
                "RtkAudUService" => "Realtek Semiconductor Corp.", _ => null,
            };
        }
        var opera = s.StartupEntries.FirstOrDefault(e => e.Name.StartsWith("Opera GX Browser", StringComparison.Ordinal));
        if (opera is not null) opera.FileExists = false;

        // Reliability
        s.Reliability = new ReliabilityData
        {
            LookbackDays = options.EventLookbackDays,
            Events = st.Events.ToList(),
            BootRecordsReadable = p.IsElevated,
            CrashDumpsReadable = p.IsElevated,
        };
        if (p.IsElevated)
        {
            s.Reliability.BootRecords.Add(new BootPerformance { Time = Now.AddDays(-9), BootTimeMs = 74_300, MainPathBootTimeMs = 31_000, PostBootTimeMs = 43_300, Degradations = { "Razer Synapse 3 (RazerCentralService) slowed startup by 6.2 s", "iCUE.exe slowed startup by 4.8 s" } });
            s.Reliability.BootRecords.Add(new BootPerformance { Time = Now.AddDays(-17), BootTimeMs = 69_800, MainPathBootTimeMs = 30_100, PostBootTimeMs = 39_700 });
        }
        else
        {
            s.AddIssue("Boot performance", "Boot duration records require administrator rights to read.", true);
            s.AddIssue("Crash dumps", @"C:\Windows\Minidump can only be listed with administrator rights.", true);
        }

        s.DeviceProblems.Add(new DeviceProblem { Name = "Bluetooth Peripheral Device", DeviceClass = "Other", PnpDeviceId = @"BTHENUM\{0000110E-0000-1000-8000-00805F9B34FB}", ErrorCode = 28 });
        s.Gpus = Gpus(true);
        s.Displays = p.Display.GetDisplays().ToList();
        s.Power = p.Power.GetStatus();
        s.Network = p.Network.GetStatus(options.IncludeConnectivityTests);
        var proxy = RegistryReaders.ReadProxy(reg);
        s.Network.ProxyEnabled = proxy.Enabled;
        s.Network.ProxyServer = proxy.Server;

        var uac = RegistryReaders.ReadUac(reg);
        s.Security = new SecurityStatus
        {
            DefenderAntivirusEnabled = true, DefenderRealTimeEnabled = true, DefenderSignatureAgeDays = 1, DefenderTamperProtection = true,
            FirewallProfiles = { ["Domain"] = true, ["Private"] = true, ["Public"] = true },
            UacEnabled = uac.Enabled, UacConsentPromptBehaviorAdmin = uac.ConsentAdmin, SecureBootEnabled = true, TpmPresent = true, MemoryIntegrityEnabled = false,
        };
        var reboot = RegistryReaders.ReadRebootPending(reg);
        var svcWu = p.Services.Get("wuauserv");
        s.Updates = new UpdateStatus
        {
            RebootPending = reboot.Pending, RebootPendingReasons = reboot.Reasons,
            LastSuccessfulInstall = Now.AddDays(-20),
            WindowsUpdateServiceStartMode = svcWu?.StartMode, WindowsUpdateServiceState = svcWu?.State,
            RecentHistory =
            {
                new UpdateHistoryItem { Title = "2024-01 Security Update for Windows (KB5034441)", Date = Now.AddDays(-5), Result = "Failed", HResult = "0x800F0922" },
                new UpdateHistoryItem { Title = "2024-01 Security Update for Windows (KB5034441)", Date = Now.AddDays(-12), Result = "Failed", HResult = "0x800F0922" },
                new UpdateHistoryItem { Title = "Security Intelligence Update for Microsoft Defender Antivirus", Date = Now.AddDays(-1), Result = "Succeeded" },
                new UpdateHistoryItem { Title = "2024-10 Cumulative Update for Windows 11 Version 23H2", Date = Now.AddDays(-20), Result = "Succeeded" },
            },
        };
        s.Gaming = RegistryReaders.ReadGaming(reg);
        s.Gaming.OverlayProcessesRunning = procs.Select(x => x.Name).Distinct()
            .Where(n => Core.Diagnostics.Rules.KnownOverlays.Map.ContainsKey(n)).Select(n => Core.Diagnostics.Rules.KnownOverlays.Map[n]).ToList();

        // Storage
        var tempBytes = FileCleaner.Measure(p.Folders.Temp, TimeSpan.Zero).BytesFound;
        var winTemp = FileCleaner.Measure(Path.Combine(p.Folders.WindowsDir, "Temp"), TimeSpan.Zero).BytesFound;
        var wuCache = FileCleaner.Measure(Path.Combine(p.Folders.WindowsDir, "SoftwareDistribution", "Download"), TimeSpan.Zero).BytesFound;
        s.Cleanup = new CleanupCandidates
        {
            UserTempBytes = tempBytes, WindowsTempBytes = winTemp, RecycleBinBytes = st.RecycleBinBytes, RecycleBinItems = st.RecycleBinItems,
            WindowsUpdateCacheBytes = wuCache, HibernationFileBytes = st.HibernationEnabled ? 6_800_000_000 : 0, PageFileBytes = 4_000_000_000,
        };
        s.Drives.Add(new DriveStatus { Name = @"C:\", Label = "Windows", FileSystem = "NTFS", TotalBytes = st.SystemDriveTotal, FreeBytes = st.SystemDriveBaseFree + st.BytesFreedInSandbox + (st.HibernationEnabled ? 0 : 6_800_000_000), IsSystemDrive = true });
        s.Drives.Add(new DriveStatus { Name = @"D:\", Label = "Games", FileSystem = "NTFS", TotalBytes = 2_000_250_000_000, FreeBytes = 812_000_000_000 });
        s.PhysicalDisks.Add(new PhysicalDiskInfo { FriendlyName = "Samsung SSD 970 EVO Plus 500GB", MediaType = "SSD", BusType = "NVMe", SizeBytes = 500_107_862_016, HealthStatus = "Healthy", OperationalStatus = { "OK" }, TemperatureC = p.IsElevated ? 47 : null, WearPercent = p.IsElevated ? 11 : null, ReliabilityCountersAvailable = p.IsElevated });
        s.PhysicalDisks.Add(new PhysicalDiskInfo { FriendlyName = "ST2000DM008-2FR102", MediaType = "HDD", BusType = "SATA", SizeBytes = 2_000_398_934_016, HealthStatus = "Healthy", OperationalStatus = { "OK" }, ReliabilityCountersAvailable = p.IsElevated });
        if (!p.IsElevated) s.AddIssue("Disk reliability", "SSD wear and temperature counters require administrator rights.", true);

        // Suspicious process indicators (shared heuristics)
        foreach (var proc in procs.Where(x => x.Path is not null).GroupBy(x => x.Path!).Select(g => g.First()))
        {
            var det = p.Processes.GetFileDetails(proc.Path!, true);
            var reasons = ProcessHeuristics.Evaluate(proc.Name, proc.Path, det.Signature, p.Folders);
            if (reasons.Count > 0)
                s.ProcessIndicators.Add(new SuspiciousProcessIndicator { Pid = proc.Pid, Name = proc.Name, Path = proc.Path, Reasons = reasons, Signature = det.Signature });
        }

        s.Thermal = new ThermalData { CpuSensorReliable = false, Notes = "No documented CPU temperature source is available on this PC." };
        return s;
    }
}

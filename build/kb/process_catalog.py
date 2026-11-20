#!/usr/bin/env python3
"""Generates src/SoftReset.Core/Knowledge/Data/processes.json — explanations of common processes.

Expected paths use environment variables and '*' wildcards. A process found outside its expected
location is flagged for review — never classified as malware by name alone.
"""
import json
import os

P = []
SYS32 = r"%SystemRoot%\System32"
WIN = r"%SystemRoot%"
DRIVERSTORE = r"%SystemRoot%\System32\DriverStore\FileRepository\*"
WINAPPS = r"%ProgramFiles%\WindowsApps\*"


def p(name, display, desc, publisher, category, paths=(), critical=False, windows=False, functionality="", trouble=(),
      high_cpu=None, high_mem=None, startup=None, safe_to_end=False, aliases=()):
    e = dict(name=name, aliases=list(aliases), displayName=display, description=desc, publisher=publisher, category=category,
             expectedPaths=list(paths), critical=critical, windowsComponent=windows, functionality=functionality or desc,
             troubleshooting=list(trouble), safeToEnd=safe_to_end)
    if high_cpu: e["highCpu"] = high_cpu
    if high_mem: e["highMemory"] = high_mem
    if startup: e["startup"] = startup
    P.append(e)


MS = "Microsoft Corporation"
CORE = "Core Windows process. Ending it crashes Windows, signs the user out, or is blocked by Windows."

# ---------------------------------------------------------------- Windows core
p("System", "System (NT kernel)", "The Windows kernel and drivers.", MS, "windows-core", critical=True, windows=True,
  functionality="Hosts kernel threads, drivers, and hardware interrupts.",
  trouble=["High CPU in 'System' usually means a driver problem: use Resource Monitor or Windows Performance Recorder to find the driver.",
           "Update chipset, storage, network, and GPU drivers.", "Check for failing disks (I/O retries show up as System CPU)."],
  high_cpu="Driver activity: network, storage, or GPU drivers busy or misbehaving; also hardware interrupts from failing devices.")
p("Registry", "Registry", "Holds registry hives in memory.", MS, "windows-core", critical=True, windows=True, trouble=["Normal; memory use reflects registry size."])
p("Memory Compression", "Memory Compression", "Stores compressed memory pages to avoid paging to disk.", MS, "windows-core", critical=True, windows=True,
  high_mem="Normal when RAM is full — it is compressing other apps' memory. Add RAM or close apps if constantly high.",
  trouble=["High memory here is a symptom of memory pressure, not a problem in itself."])
p("Secure System", "Secure System", "Virtualization-based security (VBS) process.", MS, "windows-core", critical=True, windows=True)
p("Idle", "System Idle Process", "Represents idle CPU time.", MS, "windows-core", critical=True, windows=True, aliases=["System Idle Process"],
  functionality="Not a real program — high values mean the CPU is free.")
p("smss.exe", "Session Manager", "Starts user sessions at boot.", MS, "windows-core", [SYS32], True, True, trouble=[CORE])
p("csrss.exe", "Client Server Runtime", "Handles console windows and parts of the Win32 subsystem.", MS, "windows-core", [SYS32], True, True,
  trouble=[CORE, "A csrss.exe outside System32 is a strong indicator of malware masquerading as Windows."])
p("wininit.exe", "Windows Start-Up Application", "Starts services.exe and lsass.exe at boot.", MS, "windows-core", [SYS32], True, True, trouble=[CORE])
p("winlogon.exe", "Windows Logon", "Handles sign-in, sign-out, and Ctrl+Alt+Del.", MS, "windows-core", [SYS32], True, True, trouble=[CORE])
p("services.exe", "Service Control Manager", "Starts and stops Windows services.", MS, "windows-core", [SYS32], True, True, trouble=[CORE])
p("lsass.exe", "Local Security Authority", "Validates sign-ins and enforces security policy.", MS, "windows-core", [SYS32], True, True,
  trouble=[CORE, "High CPU can indicate many authentication requests (e.g. a misbehaving app or network scan)."])
p("LsaIso.exe", "Credential Guard (LSA isolated)", "Isolated LSA process used by Credential Guard.", MS, "windows-core", [SYS32], True, True)
p("svchost.exe", "Service Host", "Hosts Windows services. There are always many instances.", MS, "windows-service", [SYS32, r"%SystemRoot%\SysWOW64"], True, True,
  functionality="Each instance runs one or more services; the Services column shows which.",
  trouble=["Find which service is busy in the 'Services' column and look that service up.",
           "Windows Update (wuauserv), Defender, and Delivery Optimization are common temporary CPU users.",
           "svchost.exe anywhere other than System32/SysWOW64 should be investigated."],
  high_cpu="One of its hosted services is busy — commonly Windows Update, Delivery Optimization, or Windows Search.")
p("dwm.exe", "Desktop Window Manager", "Composes the desktop and window effects using the GPU.", MS, "windows-shell", [SYS32], True, True,
  trouble=["High GPU/CPU use often follows GPU driver problems or very high refresh/multiple monitors.", "Update the GPU driver."],
  high_mem="Can grow with many monitors/high resolutions; a GPU driver leak can make it grow continuously — update the driver.")
p("fontdrvhost.exe", "Usermode Font Driver Host", "Renders fonts in a sandbox.", MS, "windows-core", [SYS32], True, True)
p("sihost.exe", "Shell Infrastructure Host", "Supports the Start menu, Action Center, and notifications.", MS, "windows-shell", [SYS32], True, True)
p("explorer.exe", "Windows Explorer", "Taskbar, desktop, and File Explorer windows.", MS, "windows-shell", [WIN],
  windows=True, safe_to_end=False,
  trouble=["If the taskbar freezes, restart Explorer from the console's Desktop actions.",
           "High CPU usually comes from shell extensions (cloud sync overlays, archive tools) or a folder with thousands of thumbnails."],
  high_cpu="Shell extensions, thumbnail generation, or a stuck network drive.")
p("taskhostw.exe", "Host Process for Windows Tasks", "Runs DLL-based scheduled and startup tasks.", MS, "windows-core", [SYS32], windows=True)
p("RuntimeBroker.exe", "Runtime Broker", "Manages permissions for Store apps.", MS, "windows-core", [SYS32], windows=True,
  trouble=["High usage is tied to a Store app (often Photos or Widgets) — close the related app."],
  high_cpu="A Store app is busy (Photos indexing, Widgets, etc.).")
p("conhost.exe", "Console Window Host", "Hosts command-line windows.", MS, "windows-core", [SYS32], True, True)
p("dllhost.exe", "COM Surrogate", "Runs COM components such as thumbnail and media handlers.", MS, "windows-core", [SYS32, r"%SystemRoot%\SysWOW64"], True, True,
  high_cpu="Usually thumbnail/codec handlers processing a large media folder.")
p("ctfmon.exe", "CTF Loader", "Text input services (keyboard layouts, IME, handwriting).", MS, "windows-core", [SYS32], True, True)
p("audiodg.exe", "Windows Audio Device Graph Isolation", "Processes audio effects in isolation.", MS, "windows-service", [SYS32], True, True,
  trouble=["High CPU usually comes from audio enhancements or vendor audio effects — disable enhancements for the device."],
  high_cpu="Audio enhancements or a buggy audio driver effect.")
p("spoolsv.exe", "Print Spooler", "Manages print jobs.", MS, "windows-service", [SYS32], True, True,
  trouble=["High CPU or crashes are usually printer driver problems — restart the Print Spooler service and update/remove the printer driver."],
  high_cpu="A stuck print job or bad printer driver.")
p("WmiPrvSE.exe", "WMI Provider Host", "Answers system information queries (WMI).", MS, "windows-service", [SYS32 + r"\wbem"], True, True,
  trouble=["Constant high CPU means an app is querying WMI heavily — vendor utilities (RGB, hardware monitors) are common causes."],
  high_cpu="Another app is polling system information (RGB/hardware monitoring tools, scripts).")
p("WUDFHost.exe", "Windows Driver Foundation host", "Hosts user-mode drivers (sensors, fingerprint readers, MTP phones).", MS, "windows-core", [SYS32], True, True)
p("SearchIndexer.exe", "Microsoft Windows Search Indexer", "Indexes files for fast search.", MS, "windows-service", [SYS32], windows=True,
  trouble=["High usage after copying many files is normal and temporary.", "If constant, rebuild the index from Indexing Options."],
  high_cpu="Indexing new or changed files; constant use suggests a corrupt index — rebuild it.")
p("SearchHost.exe", "Search (Windows 11)", "The Start/taskbar search interface.", MS, "windows-shell", [WINAPPS, r"%SystemRoot%\SystemApps\*"], windows=True)
p("SearchApp.exe", "Search (Windows 10)", "The taskbar search interface.", MS, "windows-shell", [r"%SystemRoot%\SystemApps\*"], windows=True)
p("SearchProtocolHost.exe", "Search Protocol Host", "Reads files for the indexer.", MS, "windows-service", [SYS32], windows=True, safe_to_end=True)
p("SearchFilterHost.exe", "Search Filter Host", "Extracts text from files for indexing.", MS, "windows-service", [SYS32], windows=True, safe_to_end=True)
p("StartMenuExperienceHost.exe", "Start", "The Start menu.", MS, "windows-shell", [r"%SystemRoot%\SystemApps\*"], windows=True,
  trouble=["If Start won't open, ending this process restarts it."])
p("ShellExperienceHost.exe", "Windows Shell Experience Host", "Notification area flyouts and system UI.", MS, "windows-shell", [r"%SystemRoot%\SystemApps\*"], windows=True)
p("TextInputHost.exe", "Text Input Application", "Touch keyboard, emoji panel, and IME UI.", MS, "windows-shell", [r"%SystemRoot%\SystemApps\*"], windows=True)
p("ApplicationFrameHost.exe", "Application Frame Host", "Window frames for Store apps.", MS, "windows-shell", [SYS32], windows=True)
p("SystemSettings.exe", "Settings", "The Windows Settings app.", MS, "windows-shell", [r"%SystemRoot%\ImmersiveControlPanel"], windows=True, safe_to_end=True)
p("LockApp.exe", "Lock screen", "The lock screen.", MS, "windows-shell", [r"%SystemRoot%\SystemApps\*"], windows=True)
p("backgroundTaskHost.exe", "Background Task Host", "Runs background tasks for Store apps.", MS, "windows-core", [SYS32], windows=True, safe_to_end=True)
p("smartscreen.exe", "Windows Defender SmartScreen", "Checks downloaded files and apps against reputation data.", MS, "security", [SYS32], True, True)
p("TiWorker.exe", "Windows Modules Installer Worker", "Installs Windows updates and components.", MS, "updater", [r"%SystemRoot%\WinSxS\*"], True, True,
  trouble=["High CPU/disk is normal while updates install — let it finish and restart when asked."],
  high_cpu="Installing or cleaning up updates. Normal for minutes to an hour after Patch Tuesday.")
p("TrustedInstaller.exe", "Windows Modules Installer", "Servicing stack for updates.", MS, "updater", [r"%SystemRoot%\servicing"], True, True)
p("MoUsoCoreWorker.exe", "Windows Update orchestrator worker", "Scans for and coordinates Windows updates.", MS, "updater", [SYS32], windows=True,
  high_cpu="Scanning for updates; temporary.", trouble=["Often blocks sleep while checking for updates — check Windows Update status."])
p("usocoreworker.exe", "Update Session Orchestrator worker", "Windows Update coordination.", MS, "updater", [SYS32], windows=True)
p("CompatTelRunner.exe", "Microsoft Compatibility Telemetry", "Runs compatibility appraisal for upgrades.", MS, "windows-service", [SYS32], windows=True, safe_to_end=True,
  high_cpu="Periodic compatibility scans; usually finishes within an hour.",
  trouble=["Temporary high disk/CPU after updates; leave it to finish. The scheduled task can be reviewed in Settings Explorer → Scheduled tasks."])
p("sppsvc.exe", "Software Protection", "Windows licensing/activation service.", MS, "windows-service", [SYS32], True, True)
p("SgrmBroker.exe", "System Guard Runtime Monitor", "Platform integrity monitoring.", MS, "security", [SYS32], True, True)
p("dasHost.exe", "Device Association Framework", "Pairs wireless and network devices.", MS, "windows-service", [SYS32], windows=True)
p("WidgetService.exe", "Widgets service", "Background service for Widgets.", MS, "windows-shell", [WINAPPS], windows=True, safe_to_end=True)
p("Widgets.exe", "Widgets", "News and weather widgets board.", MS, "windows-shell", [WINAPPS], windows=True, safe_to_end=True,
  startup="Runs while the Widgets button is enabled. Turn the button off in Taskbar settings to stop it.")
p("PhoneExperienceHost.exe", "Phone Link", "Connects Android/iPhone to the PC.", MS, "app", [WINAPPS], windows=True, safe_to_end=True,
  startup="Starts at sign-in if Phone Link is set up; can be turned off inside Phone Link settings.", aliases=["YourPhone.exe"])
p("GameBar.exe", "Xbox Game Bar", "Game overlay and capture.", MS, "app", [WINAPPS], windows=True, safe_to_end=True)
p("GameBarPresenceWriter.exe", "Game Bar Presence Writer", "Reports the running game to Xbox services.", MS, "app", [SYS32], windows=True, safe_to_end=True)
p("msedge.exe", "Microsoft Edge", "Microsoft's web browser.", MS, "browser", [r"%ProgramFiles(x86)%\Microsoft\Edge\Application", r"%ProgramFiles%\Microsoft\Edge\Application"],
  safe_to_end=True, high_mem="Each tab and extension uses memory; use Edge's own task manager (Shift+Esc) to find heavy tabs.",
  startup="'Startup boost' keeps Edge running after sign-in; can be turned off in edge://settings/system.")
p("msedgewebview2.exe", "Microsoft Edge WebView2", "Embedded browser engine used by many apps (Widgets, Teams, Outlook, games launchers).", MS, "windows-core",
  [r"%ProgramFiles(x86)%\Microsoft\EdgeWebView\Application\*", r"%ProgramFiles%\Microsoft\EdgeWebView\Application\*"], windows=True,
  trouble=["Memory belongs to the app that embeds it — check the parent process.", "Never uninstall the WebView2 Runtime; many apps need it."])
p("OneDrive.exe", "Microsoft OneDrive", "Cloud file sync.", MS, "cloud-sync", [r"%LOCALAPPDATA%\Microsoft\OneDrive", r"%ProgramFiles%\Microsoft OneDrive"], safe_to_end=True,
  high_cpu="Syncing many files or stuck on a file — check the OneDrive icon for sync errors.",
  startup="Needed at startup for continuous backup of Desktop/Documents. Disable only if the customer doesn't use OneDrive.")
p("SecurityHealthSystray.exe", "Windows Security notification icon", "Tray icon for Windows Security.", MS, "security", [SYS32], windows=True,
  startup="Keep enabled — it surfaces security warnings.")
p("SecurityHealthService.exe", "Windows Security Service", "Windows Security center.", MS, "security", [SYS32], True, True)
p("MsMpEng.exe", "Microsoft Defender Antivirus", "Antivirus scanning engine.", MS, "security", [r"%ProgramData%\Microsoft\Windows Defender\Platform\*", r"%ProgramFiles%\Windows Defender"], True, True,
  trouble=["High CPU during scheduled or first scans is normal.", "Constant high CPU can come from scanning a folder that changes constantly (e.g. a game's shader cache, dev build folders) — review with the customer before adding exclusions."],
  high_cpu="Scanning (scheduled scan, new downloads, or many changed files).")
p("NisSrv.exe", "Microsoft Network Realtime Inspection", "Network intrusion protection for Defender.", MS, "security", [r"%ProgramData%\Microsoft\Windows Defender\Platform\*"], True, True)
p("MpDefenderCoreService.exe", "Defender Core Service", "Defender platform core service.", MS, "security", [r"%ProgramData%\Microsoft\Windows Defender\Platform\*"], True, True)
p("MpCmdRun.exe", "Defender command-line utility", "Runs Defender maintenance tasks.", MS, "security", [r"%ProgramData%\Microsoft\Windows Defender\Platform\*", r"%ProgramFiles%\Windows Defender"], windows=True)
p("MRT.exe", "Malicious Software Removal Tool", "Monthly malware removal scan.", MS, "security", [SYS32], windows=True, high_cpu="Monthly scan; finishes on its own.")
p("wsappx", "AppX Deployment Service", "Installs and updates Store apps.", MS, "windows-service", windows=True, high_cpu="Store app updates; temporary.")
p("TabTip.exe", "Touch Keyboard and Handwriting", "Touch keyboard.", MS, "windows-core", [r"%CommonProgramFiles%\microsoft shared\ink"], windows=True)
p("Copilot.exe", "Microsoft Copilot", "Copilot app.", MS, "app", [WINAPPS], safe_to_end=True)

# ---------------------------------------------------------------- Graphics/driver utilities
NV = "NVIDIA Corporation"
p("NVDisplay.Container.exe", "NVIDIA Display Container", "Hosts NVIDIA Control Panel and display driver plugins.", NV, "driver",
  [DRIVERSTORE, r"%ProgramFiles%\NVIDIA Corporation\Display.NvContainer"], windows=False,
  trouble=["Required for NVIDIA Control Panel. If it uses high CPU, update or clean-install the driver."])
p("nvcontainer.exe", "NVIDIA Container", "Hosts NVIDIA App/GeForce Experience services.", NV, "vendor-utility", [r"%ProgramFiles%\NVIDIA Corporation\NvContainer"], safe_to_end=True,
  high_cpu="Overlay/recording features or driver update checks.")
p("NVIDIA app.exe", "NVIDIA App", "Driver updates, game settings, and overlay.", NV, "vendor-utility", [r"%ProgramFiles%\NVIDIA Corporation\NVIDIA app\*"], safe_to_end=True,
  startup="Not needed at startup unless the customer uses the overlay/recording; drivers still work without it.")
p("NVIDIA Overlay.exe", "NVIDIA Overlay", "In-game overlay, recording, and Instant Replay.", NV, "vendor-utility", [r"%ProgramFiles%\NVIDIA Corporation\NVIDIA app\*", r"%ProgramFiles%\NVIDIA Corporation\NVIDIA GeForce Experience"], safe_to_end=True,
  trouble=["Instant Replay continuously records — turn it off if not used."])
p("nvsphelper64.exe", "NVIDIA ShadowPlay Helper", "Recording/hotkey helper for the NVIDIA overlay.", NV, "vendor-utility", [r"%ProgramFiles%\NVIDIA Corporation\*"], safe_to_end=True)
p("NVIDIA Web Helper.exe", "NVIDIA Web Helper", "GeForce Experience web component.", NV, "vendor-utility", [r"%ProgramFiles(x86)%\NVIDIA Corporation\NvNode"], safe_to_end=True)
p("nvidia-smi.exe", "NVIDIA System Management Interface", "Command-line GPU monitoring tool.", NV, "driver", [SYS32, DRIVERSTORE], safe_to_end=True)
AMD = "Advanced Micro Devices, Inc."
p("RadeonSoftware.exe", "AMD Software: Adrenalin Edition", "AMD driver control panel and overlay.", AMD, "vendor-utility", [r"%ProgramFiles%\AMD\CNext\CNext"], safe_to_end=True,
  startup="Needed for the AMD overlay/hotkeys and driver update notifications; graphics work without it.")
p("AMDRSServ.exe", "AMD Radeon Settings service", "Background service for AMD Software features (ReLive, overlay).", AMD, "vendor-utility", [r"%ProgramFiles%\AMD\CNext\CNext"], safe_to_end=True)
p("atieclxx.exe", "AMD External Events Client", "Handles display hotkeys and events for the AMD driver.", AMD, "driver", [SYS32, DRIVERSTORE])
p("atiesrxx.exe", "AMD External Events Service", "AMD display event service.", AMD, "driver", [SYS32, DRIVERSTORE])
p("amdfendrsr.exe", "AMD Crash Defender", "Driver crash recovery service.", AMD, "driver", [SYS32, DRIVERSTORE])
p("igfxEM.exe", "Intel Graphics Event Monitor", "Intel graphics hotkeys and display events.", "Intel Corporation", "driver", [SYS32, DRIVERSTORE])
p("IntelGraphicsSoftware.exe", "Intel Graphics Software", "Intel Arc/Iris Xe control panel.", "Intel Corporation", "vendor-utility", [r"%ProgramFiles%\Intel\Intel Graphics Software"], safe_to_end=True)
p("RtkAudUService64.exe", "Realtek HD Audio Universal Service", "Realtek audio jack detection and audio console support.", "Realtek Semiconductor", "driver", [SYS32, DRIVERSTORE],
  startup="Supports jack detection and the Realtek Audio Console — leave enabled.")
p("WavesSvc64.exe", "Waves MaxxAudio Service", "Laptop audio enhancement service.", "Waves Audio Ltd.", "driver", [DRIVERSTORE],
  high_cpu="Known to use CPU on some laptops; updating the audio driver from the laptop maker helps.")

# ---------------------------------------------------------------- Peripherals & RGB
p("lghub.exe", "Logitech G HUB", "Logitech gaming peripheral software.", "Logitech", "peripheral", [r"%ProgramFiles%\LGHUB"], safe_to_end=True,
  startup="Needed at startup only for on-the-fly profile switching and lighting; onboard-memory profiles work without it.", aliases=["lghub_agent.exe", "lghub_updater.exe"])
p("Razer Synapse 3.exe", "Razer Synapse", "Razer peripheral configuration.", "Razer Inc.", "peripheral", [r"%ProgramFiles(x86)%\Razer\*"], safe_to_end=True,
  startup="Needed at startup for lighting/macros not stored on the device.", aliases=["RazerCentralService.exe", "Razer Synapse Service.exe", "Razer Synapse Service Process.exe"],
  high_cpu="Known to use CPU/memory; update Synapse or reinstall if constant.")
p("iCUE.exe", "Corsair iCUE", "Corsair RGB, fan, and peripheral control.", "Corsair", "peripheral", [r"%ProgramFiles%\Corsair\*"], safe_to_end=True,
  startup="Controls fan curves on some systems — confirm before disabling at startup.", high_cpu="Lighting effects and hardware polling; reduce effect complexity or update iCUE.",
  aliases=["Corsair.Service.exe", "iCUE Launcher.exe"])
p("SteelSeriesGG.exe", "SteelSeries GG", "SteelSeries peripheral software.", "SteelSeries ApS", "peripheral", [r"%ProgramFiles%\SteelSeries\GG"], safe_to_end=True, aliases=["SteelSeriesEngine.exe"])
p("MSIAfterburner.exe", "MSI Afterburner", "GPU overclocking and monitoring.", "MSI", "vendor-utility", [r"%ProgramFiles(x86)%\MSI Afterburner"], safe_to_end=True,
  trouble=["If the PC is unstable, reset Afterburner to defaults — GPU overclocks are a common cause of crashes."])
p("RTSS.exe", "RivaTuner Statistics Server", "On-screen display and frame limiter used with Afterburner.", "Unwinder", "vendor-utility", [r"%ProgramFiles(x86)%\RivaTuner Statistics Server"], safe_to_end=True,
  trouble=["Can conflict with other overlays and some anti-cheat systems."])
p("ArmouryCrate.exe", "ASUS Armoury Crate", "ASUS RGB, fan, and system control.", "ASUSTeK COMPUTER INC.", "vendor-utility", [WINAPPS, r"%ProgramFiles%\ASUS\*"], safe_to_end=True,
  aliases=["ArmouryCrate.Service.exe", "ArmouryCrateControlInterface.exe", "AsusCertService.exe", "LightingService.exe"],
  high_cpu="Lighting services are known to be heavy; update or use the official uninstall tool if not needed.")

# ---------------------------------------------------------------- Game launchers & anti-cheat
p("steam.exe", "Steam", "Valve's game store and launcher.", "Valve Corporation", "game-launcher", [r"%ProgramFiles(x86)%\Steam", r"%ProgramFiles%\Steam"], safe_to_end=True,
  startup="Not needed at startup unless the customer wants downloads/updates in the background.")
p("steamwebhelper.exe", "Steam Client WebHelper", "Steam's built-in browser (store, library UI).", "Valve Corporation", "game-launcher", [r"%ProgramFiles(x86)%\Steam\bin\cef\*"], safe_to_end=True,
  high_mem="The Steam UI; disable GPU acceleration in Steam's Interface settings on low-end PCs.")
p("GameOverlayUI.exe", "Steam Overlay", "In-game Steam overlay.", "Valve Corporation", "game-launcher", [r"%ProgramFiles(x86)%\Steam"], safe_to_end=True)
p("EpicGamesLauncher.exe", "Epic Games Launcher", "Epic's game store and launcher.", "Epic Games, Inc.", "game-launcher", [r"%ProgramFiles(x86)%\Epic Games\Launcher\*"], safe_to_end=True,
  startup="Not needed at startup.", high_cpu="Known to use CPU while idle; close it after launching games.", aliases=["EpicWebHelper.exe"])
p("Battle.net.exe", "Battle.net", "Blizzard launcher.", "Blizzard Entertainment", "game-launcher", [r"%ProgramFiles(x86)%\Battle.net"], safe_to_end=True, startup="Not needed at startup.")
p("EADesktop.exe", "EA app", "Electronic Arts launcher.", "Electronic Arts", "game-launcher", [r"%ProgramFiles%\Electronic Arts\EA Desktop\*"], safe_to_end=True, startup="Not needed at startup.",
  aliases=["EABackgroundService.exe"])
p("upc.exe", "Ubisoft Connect", "Ubisoft launcher.", "Ubisoft", "game-launcher", [r"%ProgramFiles(x86)%\Ubisoft\Ubisoft Game Launcher"], safe_to_end=True, startup="Not needed at startup.",
  aliases=["UbisoftConnect.exe"])
p("RiotClientServices.exe", "Riot Client", "Launcher for Valorant and League of Legends.", "Riot Games, Inc.", "game-launcher", [r"C:\Riot Games\Riot Client"], safe_to_end=True)
p("vgc.exe", "Riot Vanguard", "Kernel-level anti-cheat for Valorant/League.", "Riot Games, Inc.", "anti-cheat", [r"%ProgramFiles%\Riot Vanguard"],
  trouble=["Requires TPM 2.0 and Secure Boot on Windows 11.", "Uninstall via Installed apps if the customer no longer plays Riot games."],
  aliases=["vgtray.exe"])
p("EasyAntiCheat.exe", "Easy Anti-Cheat", "Anti-cheat service used by many games.", "Epic Games, Inc.", "anti-cheat", [r"%ProgramFiles(x86)%\EasyAntiCheat", r"%ProgramFiles(x86)%\EasyAntiCheat_EOS"],
  trouble=["Game won't launch: repair EAC from the game's install folder (EasyAntiCheat_Setup.exe)."], aliases=["EasyAntiCheat_EOS.exe"])
p("BEService.exe", "BattlEye Service", "Anti-cheat service used by some games.", "BattlEye Innovations", "anti-cheat", [r"%ProgramFiles(x86)%\Common Files\BattlEye"])
p("FACEIT.exe", "FACEIT", "Competitive platform client with anti-cheat.", "FACEIT", "anti-cheat", [r"%ProgramFiles%\FACEIT AC", r"%LOCALAPPDATA%\FACEIT"], safe_to_end=True)
p("Overwolf.exe", "Overwolf", "In-game overlay apps platform.", "Overwolf Ltd.", "app", [r"%ProgramFiles(x86)%\Overwolf\*"], safe_to_end=True,
  startup="Not needed at startup; also bundled by CurseForge.", high_mem="Each Overwolf app is a browser process; uninstall unused apps inside Overwolf.",
  aliases=["OverwolfBrowser.exe", "OverwolfHelper.exe", "OverwolfLauncher.exe"])

# ---------------------------------------------------------------- Communication & media
p("Discord.exe", "Discord", "Voice, video, and text chat.", "Discord Inc.", "communication", [r"%LOCALAPPDATA%\Discord\*"], safe_to_end=True,
  startup="Optional at startup — users who want instant notifications keep it; it can start minimised.",
  high_cpu="Hardware acceleration issues or streaming; try toggling hardware acceleration in Discord settings.",
  high_mem="Electron app; several processes are normal.", trouble=["Black screen/glitches: toggle Hardware Acceleration, then clear the cache."])
p("Spotify.exe", "Spotify", "Music streaming.", "Spotify AB", "communication", [r"%APPDATA%\Spotify", WINAPPS], safe_to_end=True,
  startup="Not needed at startup; turn off in Spotify → Settings → Startup and window behaviour.")
p("Teams.exe", "Microsoft Teams (classic)", "Chat and meetings.", MS, "communication", [r"%LOCALAPPDATA%\Microsoft\Teams\*"], safe_to_end=True, aliases=["ms-teams.exe"],
  startup="Workplace users usually keep it at startup.", high_mem="Large meetings and many chats use significant memory.")
p("Zoom.exe", "Zoom Workplace", "Video meetings.", "Zoom Video Communications, Inc.", "communication", [r"%APPDATA%\Zoom\bin", r"%ProgramFiles%\Zoom\bin"], safe_to_end=True,
  startup="Not needed at startup unless the customer joins many meetings.")
p("slack.exe", "Slack", "Team chat.", "Slack Technologies", "communication", [r"%LOCALAPPDATA%\slack\*", WINAPPS], safe_to_end=True)
p("WhatsApp.exe", "WhatsApp", "Messaging.", "WhatsApp LLC", "communication", [WINAPPS], safe_to_end=True)
p("Telegram.exe", "Telegram", "Messaging.", "Telegram FZ-LLC", "communication", [r"%APPDATA%\Telegram Desktop"], safe_to_end=True)
p("obs64.exe", "OBS Studio", "Streaming and recording.", "OBS Project", "creative", [r"%ProgramFiles%\obs-studio\bin\64bit"], safe_to_end=True,
  high_cpu="Software (x264) encoding; switch to the GPU encoder (NVENC/AMF/QSV) in Output settings.")
p("vlc.exe", "VLC media player", "Media player.", "VideoLAN", "app", [r"%ProgramFiles%\VideoLAN\VLC"], safe_to_end=True)

# ---------------------------------------------------------------- Browsers
p("chrome.exe", "Google Chrome", "Web browser.", "Google LLC", "browser", [r"%ProgramFiles%\Google\Chrome\Application", r"%LOCALAPPDATA%\Google\Chrome\Application"], safe_to_end=True,
  high_mem="Each tab/extension is a process; use Chrome's Task Manager (Shift+Esc) to find heavy tabs and extensions.",
  high_cpu="A busy tab or extension; check Chrome's Task Manager.",
  startup="'Continue running background apps when Chrome is closed' keeps it in memory; can be turned off in chrome://settings/system.")
p("firefox.exe", "Mozilla Firefox", "Web browser.", "Mozilla Corporation", "browser", [r"%ProgramFiles%\Mozilla Firefox"], safe_to_end=True,
  high_mem="Use about:processes to find heavy tabs.")
p("brave.exe", "Brave", "Web browser.", "Brave Software, Inc.", "browser", [r"%ProgramFiles%\BraveSoftware\Brave-Browser\Application", r"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\Application"], safe_to_end=True)
p("opera.exe", "Opera / Opera GX", "Web browser.", "Opera Norway AS", "browser", [r"%LOCALAPPDATA%\Programs\Opera*"], safe_to_end=True,
  startup="Opera installs a 'browser assistant' startup entry and auto-update tasks that can be disabled.", aliases=["browser_assistant.exe"])

# ---------------------------------------------------------------- Cloud, office, creative
p("Dropbox.exe", "Dropbox", "Cloud file sync.", "Dropbox, Inc.", "cloud-sync", [r"%ProgramFiles(x86)%\Dropbox\Client", r"%ProgramFiles%\Dropbox\Client"], safe_to_end=True,
  startup="Needed at startup for continuous sync.")
p("GoogleDriveFS.exe", "Google Drive for desktop", "Cloud file sync.", "Google LLC", "cloud-sync", [r"%ProgramFiles%\Google\Drive File Stream\*"], safe_to_end=True)
p("OfficeClickToRun.exe", "Microsoft Office Click-to-Run", "Office updates and streaming installation service.", MS, "updater", [r"%ProgramFiles%\Common Files\microsoft shared\ClickToRun"], windows=False,
  trouble=["Office repair (Quick/Online) is started from Installed apps → Microsoft 365 → Modify."])
p("OUTLOOK.EXE", "Microsoft Outlook (classic)", "Email client.", MS, "app", [r"%ProgramFiles%\Microsoft Office\root\Office16", r"%ProgramFiles(x86)%\Microsoft Office\root\Office16"], safe_to_end=True,
  high_cpu="Large mailboxes indexing or a bad add-in; start with 'outlook.exe /safe' to test add-ins.")
p("olk.exe", "Outlook (new)", "Email client (new Outlook).", MS, "app", [WINAPPS], safe_to_end=True)
p("WINWORD.EXE", "Microsoft Word", "Word processor.", MS, "app", [r"%ProgramFiles%\Microsoft Office\root\Office16"], safe_to_end=True)
p("EXCEL.EXE", "Microsoft Excel", "Spreadsheets.", MS, "app", [r"%ProgramFiles%\Microsoft Office\root\Office16"], safe_to_end=True)
p("Creative Cloud.exe", "Adobe Creative Cloud", "Adobe app manager.", "Adobe Inc.", "creative", [r"%ProgramFiles%\Adobe\Adobe Creative Cloud\ACC", r"%ProgramFiles(x86)%\Adobe\Adobe Creative Cloud\ACC"], safe_to_end=True,
  startup="Not needed at startup unless the customer syncs Adobe fonts/files continuously.",
  aliases=["CCXProcess.exe", "CCLibrary.exe", "AdobeIPCBroker.exe", "Adobe Desktop Service.exe", "CoreSync.exe"])
p("AdobeARM.exe", "Adobe Acrobat Update Service", "Updates Acrobat/Reader.", "Adobe Inc.", "updater", [r"%ProgramFiles(x86)%\Common Files\Adobe\ARM\*"], safe_to_end=True)
p("Acrobat.exe", "Adobe Acrobat", "PDF reader/editor.", "Adobe Inc.", "app", [r"%ProgramFiles%\Adobe\Acrobat DC\Acrobat"], safe_to_end=True, aliases=["AcroRd32.exe"])

# ---------------------------------------------------------------- Updaters & utilities
p("GoogleUpdate.exe", "Google Update", "Keeps Google software updated.", "Google LLC", "updater", [r"%ProgramFiles(x86)%\Google\Update", r"%LOCALAPPDATA%\Google\Update"], safe_to_end=True,
  aliases=["updater.exe"], startup="Runs via scheduled tasks; leave enabled so Chrome stays patched.")
p("MicrosoftEdgeUpdate.exe", "Microsoft Edge Update", "Keeps Edge and WebView2 updated.", MS, "updater", [r"%ProgramFiles(x86)%\Microsoft\EdgeUpdate"], safe_to_end=True,
  startup="Leave enabled — WebView2 security updates depend on it.")
p("jusched.exe", "Java Update Scheduler", "Checks for Java updates.", "Oracle Corporation", "updater", [r"%ProgramFiles(x86)%\Common Files\Java\Java Update"], safe_to_end=True,
  startup="Old Java runtimes are a security risk — uninstall Java if no app needs it.")
p("CCleaner64.exe", "CCleaner", "Third-party cleaning tool.", "Piriform Software Ltd", "app", [r"%ProgramFiles%\CCleaner"], safe_to_end=True,
  startup="'Smart Cleaning' monitoring is not needed at startup. Avoid its registry cleaner on customer PCs.", aliases=["CCleaner.exe"])
p("7zFM.exe", "7-Zip File Manager", "Archive tool.", "Igor Pavlov", "app", [r"%ProgramFiles%\7-Zip"], safe_to_end=True)
p("PowerToys.exe", "Microsoft PowerToys", "Productivity utilities.", MS, "app", [r"%ProgramFiles%\PowerToys", r"%LOCALAPPDATA%\PowerToys"], safe_to_end=True)
p("Everything.exe", "Everything (voidtools)", "Instant file name search.", "voidtools", "app", [r"%ProgramFiles%\Everything"], safe_to_end=True)
p("wallpaper64.exe", "Wallpaper Engine", "Animated wallpapers.", "Wallpaper Engine Team", "app", [r"%ProgramFiles(x86)%\Steam\steamapps\common\wallpaper_engine"], safe_to_end=True,
  high_cpu="Animated wallpapers use GPU/CPU; set it to pause while games run.", aliases=["wallpaper32.exe"])

# ---------------------------------------------------------------- Security suites
p("MBAMService.exe", "Malwarebytes Service", "Malwarebytes protection service.", "Malwarebytes", "security", [r"%ProgramFiles%\Malwarebytes\Anti-Malware"],
  trouble=["Running alongside another real-time antivirus can slow the PC; keep one real-time product."], aliases=["mbamtray.exe", "Malwarebytes.exe"])
p("AvastSvc.exe", "Avast Antivirus", "Avast antivirus service.", "Avast Software", "security", [r"%ProgramFiles%\Avast Software\Avast"],
  trouble=["Use the official avastclear tool for complete removal."], aliases=["AvastUI.exe"])
p("AVGSvc.exe", "AVG Antivirus", "AVG antivirus service.", "AVG Technologies", "security", [r"%ProgramFiles%\AVG\Antivirus"], aliases=["AVGUI.exe"])
p("mcshield.exe", "McAfee", "McAfee antivirus engine.", "McAfee, LLC", "security", [r"%ProgramFiles%\McAfee\*", r"%ProgramFiles%\Common Files\McAfee\*"],
  trouble=["Often pre-installed as a trial; remove with the McAfee Consumer Product Removal tool if the customer doesn't subscribe."], aliases=["mfemms.exe", "McUICnt.exe", "mcapexe.exe"])
p("NortonSecurity.exe", "Norton", "Norton security suite.", "Gen Digital Inc.", "security", [r"%ProgramFiles%\Norton*\*"],
  trouble=["Remove with the Norton Remove and Reinstall tool."], aliases=["Norton.exe", "NortonUI.exe"])
p("bdagent.exe", "Bitdefender", "Bitdefender agent.", "Bitdefender", "security", [r"%ProgramFiles%\Bitdefender\*"], aliases=["bdservicehost.exe"])
p("ekrn.exe", "ESET", "ESET antivirus kernel service.", "ESET", "security", [r"%ProgramFiles%\ESET\*"], aliases=["egui.exe"])

out = os.path.join(os.path.dirname(__file__), "..", "..", "src", "SoftReset.Core", "Knowledge", "Data", "processes.json")
names = [x["name"].lower() for x in P] + [a.lower() for x in P for a in x["aliases"]]
d = {n for n in names if names.count(n) > 1}
if d: raise SystemExit(f"Duplicate process names: {d}")
with open(out, "w", encoding="utf-8") as f:
    json.dump(P, f, indent=1, ensure_ascii=False)
    f.write("\n")
print(f"Wrote {len(P)} processes ({len(names)} names incl. aliases)")

#!/usr/bin/env python3
"""Generates src/SoftReset.Core/Knowledge/Data/apps.json — guides for common Windows applications.

Cache locations listed here are the ONLY folders the 'Clear application cache' action may empty.
Only disposable caches are listed (never profiles, settings, saved logins, or user files).
"""
import json
import os

A = []


def app(key, name, match, category, purpose, settings=(), startup="", startup_names=(), processes=(), problems=(), repair=(),
        reset=(), uninstall=(), links=(), caches=(), packages=(), essential=False, essential_reason=None):
    e = dict(key=key, name=name, match=list(match), matchPackages=list(packages), category=category, purpose=purpose,
             relevantSettings=list(settings), startupBehavior=startup, startupEntryNames=list(startup_names), processNames=list(processes),
             commonProblems=[dict(problem=p, fixes=list(f)) for p, f in problems], repair=list(repair), reset=list(reset),
             safeUninstall=list(uninstall), links=[dict(label=l, url=u) for l, u in links],
             caches=[dict(path=p, description=d) for p, d in caches], essential=essential)
    if essential_reason: e["essentialReason"] = essential_reason
    A.append(e)


STD_UNINSTALL = ["Close the application first.", "Uninstall from Settings → Apps → Installed apps (or from its page in the Application Library), using the vendor's own uninstaller.",
                 "Restart if the uninstaller asks.", "Leftover user data (if any) stays in AppData — remove only with the customer's agreement."]

# ------------------------------------------------------------------ Communication
app("discord", "Discord", [r"^Discord$"], "Communication",
    "Voice, video, and text chat widely used by gamers and communities.",
    ["Hardware acceleration (Settings → Advanced)", "Overlay (Settings → Game Overlay)", "Open Discord on startup (Settings → Windows Settings)", "Voice input/output devices (Settings → Voice & Video)"],
    "Starts at sign-in by default (Run key 'Discord'), minimised to the tray.", ["Discord"], ["Discord.exe"],
    [("Black or grey screen / UI glitches", ["Toggle Settings → Advanced → Hardware Acceleration and restart Discord.", "Clear the Discord cache (console action).", "Update the GPU driver."]),
     ("Microphone not working", ["Windows Settings → Privacy & security → Microphone: allow desktop apps.", "Discord → Voice & Video: choose the correct input device and run Mic Test.", "Turn off 'Exclusive mode' for the mic in the Sound control panel if another app grabs it."]),
     ("Stuck on 'Checking for updates' / update loop", ["Close Discord fully (tray icon → Quit).", "Clear the cache.", "Reinstall from discord.com — settings are stored in the account."]),
     ("Game audio quietens during calls", ["Windows Sound → Communications tab → 'Do nothing' (Settings Explorer → Sound → Communications activity)."]),
     ("Stutter while gaming", ["Disable the in-game overlay for that game.", "Turn off hardware acceleration if the GPU is low-end."])],
    ["Clear the cache, then restart Discord.", "Reinstall: uninstall, then download the installer from discord.com."],
    ["Clearing the cache keeps login and settings.", "A full reset (sign-out + removing %APPDATA%\\discord) should only be done with the customer present."],
    STD_UNINSTALL + ["Discord installs per user in %LOCALAPPDATA%\\Discord — no admin needed."],
    [("Discord Support", "https://support.discord.com"), ("Discord download", "https://discord.com/download")],
    [(r"%APPDATA%\discord\Cache", "Web cache"), (r"%APPDATA%\discord\Code Cache", "Compiled script cache"), (r"%APPDATA%\discord\GPUCache", "GPU shader cache")])

app("teams", "Microsoft Teams", [r"^Microsoft Teams", r"^Teams Machine-Wide Installer$"], "Communication",
    "Chat, meetings, and collaboration for work and school.",
    ["Auto-start (Teams → Settings → General)", "Devices (Settings → Devices)", "GPU acceleration"],
    "Starts at sign-in by default.", ["com.squirrel.Teams.Teams", "MSTeams"], ["ms-teams.exe", "Teams.exe"],
    [("Camera or mic not detected", ["Allow camera/microphone for desktop apps in Windows privacy settings.", "Select the device in Teams → Settings → Devices."]),
     ("Teams very slow or won't load", ["Quit Teams fully and restart.", "Reset the new Teams app from Installed apps → Microsoft Teams → Advanced options → Reset (signs out)."]),
     ("Two Teams installed (classic and new)", ["Classic Teams is retired; uninstall 'Microsoft Teams classic' and the 'Teams Machine-Wide Installer'."])],
    ["Installed apps → Microsoft Teams → Advanced options → Repair."], ["Advanced options → Reset (requires signing in again)."],
    STD_UNINSTALL, [("Teams help", "https://support.microsoft.com/teams")], packages=["MSTeams_"])

app("zoom", "Zoom Workplace", [r"^Zoom"], "Communication", "Video meetings and webinars.",
    ["Start Zoom when I start Windows (Settings → General)", "Audio/Video devices", "Hardware acceleration (Video → Advanced)"],
    "Can start at sign-in (optional).", ["ZoomUMX"], ["Zoom.exe"],
    [("Camera black in Zoom", ["Close other apps using the camera.", "Allow camera access for desktop apps.", "Zoom → Video → Advanced: toggle hardware acceleration."]),
     ("Echo or bad audio", ["Use a headset.", "Zoom → Audio: enable 'Suppress background noise' and choose the correct speaker."])],
    ["Reinstall the latest version from zoom.us/download."], [], STD_UNINSTALL, [("Zoom Support", "https://support.zoom.com")])

app("slack", "Slack", [r"^Slack"], "Communication", "Team messaging.",
    ["Launch at login (Preferences → Advanced)", "Hardware acceleration"], "Optional at sign-in.", ["com.squirrel.slack.slack"], ["slack.exe"],
    [("Blank window", ["Help → Troubleshooting → Clear Cache and Restart."])], ["Reinstall from slack.com."], [], STD_UNINSTALL,
    [("Slack Help Center", "https://slack.com/help")],
    [(r"%APPDATA%\Slack\Cache", "Web cache"), (r"%APPDATA%\Slack\Code Cache", "Script cache"), (r"%APPDATA%\Slack\GPUCache", "GPU cache")])

app("whatsapp", "WhatsApp", [r"^WhatsApp"], "Communication", "Messaging linked to the customer's phone.",
    ["Start at login (Settings → General)"], "Optional at sign-in.", [], ["WhatsApp.exe"],
    [("Not syncing", ["Re-link the device from the phone: Linked devices."])], ["Installed apps → WhatsApp → Advanced options → Repair."], ["Advanced options → Reset (unlinks the PC)."],
    STD_UNINSTALL, [("WhatsApp Help", "https://faq.whatsapp.com")], packages=["5319275A.WhatsAppDesktop"])

app("telegram", "Telegram Desktop", [r"^Telegram"], "Communication", "Messaging.", [], "Optional at sign-in.", ["Telegram Desktop"], ["Telegram.exe"],
    [], ["Reinstall from desktop.telegram.org."], [], STD_UNINSTALL, [("Telegram FAQ", "https://telegram.org/faq")])

# ------------------------------------------------------------------ Media
app("spotify", "Spotify", [r"^Spotify"], "Media", "Music and podcast streaming.",
    ["Open Spotify automatically after you log into the computer (Settings → Startup and window behaviour)", "Hardware acceleration (Settings → Compatibility)", "Offline storage location"],
    "Starts at sign-in by default (minimised).", ["Spotify"], ["Spotify.exe"],
    [("Black window / UI not loading", ["Turn off hardware acceleration in Settings → Compatibility.", "Clear the cache (console action)."]),
     ("Downloads use lots of disk", ["Settings → Storage: change offline storage location or clear cache."]),
     ("No sound", ["Check the output device in Spotify (Connect icon) and the Windows Volume mixer."])],
    ["Clear cache, then restart.", "Reinstall from spotify.com/download (desktop) or the Microsoft Store."], ["Sign out and back in (Settings → Log out)."],
    STD_UNINSTALL, [("Spotify Support", "https://support.spotify.com")],
    [(r"%LOCALAPPDATA%\Spotify\Storage", "Streaming cache (rebuilds automatically)"), (r"%LOCALAPPDATA%\Spotify\Data", "Downloaded song cache data")],
    packages=["SpotifyAB.SpotifyMusic"])

app("vlc", "VLC media player", [r"^VLC media player"], "Media", "Plays almost any audio/video format without extra codecs.",
    ["Hardware-accelerated decoding (Tools → Preferences → Input/Codecs)"], "Does not start with Windows.", [], ["vlc.exe"],
    [("Video stutters", ["Tools → Preferences → Input/Codecs → Hardware-accelerated decoding: Automatic."]),
     ("Outdated version", ["Update from Help → Check for updates or via winget."])],
    ["Tools → Preferences → Reset Preferences."], ["Reset Preferences restores defaults."], STD_UNINSTALL, [("VLC Support", "https://www.videolan.org/support/")])

app("obs", "OBS Studio", [r"^OBS Studio"], "Creative", "Free streaming and recording software.",
    ["Output → Encoder (use NVENC/AMF/QSV)", "Video → Base/Output resolution and FPS", "Run as administrator for game capture priority"],
    "Does not start with Windows.", [], ["obs64.exe"],
    [("Encoding overloaded / lag", ["Switch to the hardware encoder (NVENC, AMD AMF, Intel QSV).", "Lower output resolution or FPS.", "Cap the game's frame rate so the GPU has headroom."]),
     ("Black screen in Display Capture on laptops", ["Set OBS to the same GPU as the display in Settings → Display → Graphics."])],
    ["Help → Check file integrity (Windows installer)."], ["Profile/Scene collection reset from the menus."], STD_UNINSTALL, [("OBS Help", "https://obsproject.com/help")])

app("itunes", "Apple Music / iTunes / Apple Devices", [r"^iTunes$", r"^Apple Music", r"^Apple Devices", r"^Apple Mobile Device Support"], "Media",
    "Music library and iPhone/iPad sync.", [], "Apple helper services start with Windows.", [], ["iTunes.exe", "AppleMobileDeviceProcess.exe"],
    [("iPhone not detected", ["Unlock the phone and tap Trust.", "Repair Apple Mobile Device Support.", "Try another cable/port."])],
    ["Repair from Installed apps (Advanced options) for Store versions."], [], STD_UNINSTALL, [("Apple Support", "https://support.apple.com")])

# ------------------------------------------------------------------ Gaming
app("steam", "Steam", [r"^Steam$"], "Gaming", "Valve's game store, launcher, and community.",
    ["Run Steam when my computer starts (Settings → Interface)", "Shader pre-caching (Settings → Downloads)", "In-game overlay (Settings → In Game)", "GPU accelerated rendering in web views (Settings → Interface)"],
    "Starts at sign-in by default (Run key 'Steam').", ["Steam"], ["steam.exe", "steamwebhelper.exe"],
    [("Store/library pages blank", ["Settings → Downloads → Clear Download Cache.", "Clear the web browser cache (console action).", "Disable GPU accelerated rendering in web views."]),
     ("Game won't launch or crashes", ["Library → right-click game → Properties → Installed Files → Verify integrity of game files.", "Update GPU drivers.", "Disable overlays (Steam, Discord, NVIDIA) to test."]),
     ("Slow downloads", ["Settings → Downloads → change download region; clear download cache."]),
     ("High memory use from steamwebhelper", ["Disable GPU accelerated rendering and animated avatars in Settings → Interface."])],
    ["Verify game files from each game's Properties.", "Reinstall Steam into the same folder to keep games (do not delete steamapps)."],
    ["Settings → Downloads → Clear Download Cache (requires signing in again)."],
    ["Uninstalling Steam deletes installed games inside its folder — move 'steamapps' first if the customer wants to keep them.", "Use Installed apps → Steam → Uninstall."],
    [("Steam Support", "https://help.steampowered.com")],
    [(r"%LOCALAPPDATA%\Steam\htmlcache", "Web view cache")])

app("epic", "Epic Games Launcher", [r"^Epic Games Launcher$"], "Gaming", "Epic Games Store launcher (Fortnite, free games).",
    ["Run when my computer starts (Settings)", "Minimize to system tray", "Throttle downloads"], "Starts at sign-in by default.", ["EpicGamesLauncher"],
    ["EpicGamesLauncher.exe", "EpicWebHelper.exe"],
    [("Launcher stuck loading", ["Clear the web cache (console action).", "Run the launcher as administrator once."]),
     ("High CPU while idle", ["Close the launcher after starting games; disable 'Run when my computer starts'."]),
     ("Game crashes (e.g. Fortnite)", ["Library → ⋯ → Manage → Verify files.", "Update GPU drivers; remove GPU overclocks."])],
    ["Verify game files from the Library.", "Repair the launcher from Installed apps (MSI) → Modify/Repair."], [], STD_UNINSTALL + ["Uninstalling the launcher also removes installed Epic games."],
    [("Epic Games Help", "https://www.epicgames.com/help")],
    [(r"%LOCALAPPDATA%\EpicGamesLauncher\Saved\webcache", "Launcher web cache")])

app("battlenet", "Battle.net", [r"^Battle\.net$"], "Gaming", "Blizzard games launcher.", ["When I start my computer, launch Battle.net"], "Optional at sign-in.", ["Battle.net"], ["Battle.net.exe"],
    [("Stuck updating", ["Close Battle.net and delete the cache folder (console action).", "Run Scan and Repair from the game's options."])],
    ["Game options → Scan and Repair."], [], STD_UNINSTALL, [("Blizzard Support", "https://us.battle.net/support")],
    [(r"%ProgramData%\Blizzard Entertainment\Battle.net\Cache", "Launcher cache")])

app("ea", "EA app", [r"^EA app$", r"^EA Desktop"], "Gaming", "Electronic Arts games launcher.", ["Launch on startup"], "Optional at sign-in.", [], ["EADesktop.exe"],
    [("App not loading", ["Use Help → App recovery → Clear cache inside the EA app."])], ["Game → Repair."], [], STD_UNINSTALL, [("EA Help", "https://help.ea.com")])

app("ubisoft", "Ubisoft Connect", [r"^Ubisoft Connect"], "Gaming", "Ubisoft games launcher.", [], "Optional at sign-in.", [], ["upc.exe", "UbisoftConnect.exe"],
    [("Game won't launch", ["Verify files from the game's Properties."])], ["Verify files."], [], STD_UNINSTALL, [("Ubisoft Help", "https://www.ubisoft.com/help")])

app("xbox", "Xbox app (PC Game Pass)", [r"^Xbox$"], "Gaming", "Microsoft's PC games app for Game Pass.", ["Gaming Services must be installed and running"], "Does not start at sign-in.", [], ["XboxPcApp.exe"],
    [("Games won't install or launch", ["Update the Xbox app and Gaming Services from the Microsoft Store.", "Repair Gaming Services from Installed apps.", "Make sure Windows Update is working."])],
    ["Installed apps → Xbox → Advanced options → Repair."], ["Advanced options → Reset."], STD_UNINSTALL, [("Xbox Support", "https://support.xbox.com")],
    packages=["Microsoft.GamingApp_"])

app("riot", "Riot Client and Vanguard", [r"^Riot Vanguard", r"^Riot Client", r"^VALORANT", r"^League of Legends"], "Gaming",
    "Launcher for Valorant/League; Vanguard is a kernel-level anti-cheat that starts with Windows.",
    ["Secure Boot and TPM 2.0 (required on Windows 11)", "Vanguard tray icon"], "Vanguard (vgc service) starts at boot.", ["Riot Vanguard"], ["RiotClientServices.exe", "vgc.exe"],
    [("VAN 9003 / VAN 9001 errors", ["Enable Secure Boot and TPM 2.0 in firmware (Settings Explorer → Security)."]),
     ("Game crashes with other tools running", ["Close overlays and RGB/monitoring tools; Vanguard blocks some drivers."])],
    ["Reinstall Vanguard from the Riot Client."], [], ["Uninstall 'Riot Vanguard' from Installed apps if the customer no longer plays Riot games."],
    [("Valorant Support", "https://support-valorant.riotgames.com")])

app("minecraft", "Minecraft Launcher", [r"^Minecraft Launcher"], "Gaming", "Launcher for Minecraft Java and Bedrock.", ["Allocated memory (Java edition installations)"], "Does not start at sign-in.", [], ["Minecraft.exe", "javaw.exe"],
    [("Low FPS in Java edition", ["Make sure the game uses the dedicated GPU (Graphics preference per app).", "Use the default memory allocation unless running large modpacks."])],
    ["Installed apps → Minecraft Launcher → Repair."], [], STD_UNINSTALL, [("Minecraft Help", "https://help.minecraft.net")], packages=["Microsoft.4297127D64EC6_"])

app("roblox", "Roblox", [r"^Roblox"], "Gaming", "Online game platform.", ["Graphics mode (in-game settings)"], "Does not start at sign-in.", [], ["RobloxPlayerBeta.exe"],
    [("Crashes or won't start", ["Clear the Roblox temporary cache (console action).", "Reinstall from roblox.com."])],
    ["Reinstall from roblox.com."], [], STD_UNINSTALL, [("Roblox Help", "https://en.help.roblox.com")],
    [(r"%TEMP%\Roblox", "Temporary game cache")])

app("overwolf", "Overwolf / CurseForge", [r"^Overwolf", r"^CurseForge"], "Gaming", "Platform for in-game overlay apps; also used by CurseForge.", [], "Starts at sign-in by default.", ["Overwolf"],
    ["Overwolf.exe", "OverwolfBrowser.exe"],
    [("High memory and stutter", ["Uninstall unused Overwolf apps.", "Disable Overwolf's overlay for competitive games."])],
    ["Reinstall from overwolf.com."], [], STD_UNINSTALL, [("Overwolf Support", "https://support.overwolf.com")])

app("faceit", "FACEIT", [r"^FACEIT"], "Gaming", "Competitive platform with its own anti-cheat driver.", [], "Anti-cheat runs only while FACEIT is open.", [], ["FACEIT.exe"],
    [("Anti-cheat errors", ["Disable unsigned drivers/tools flagged by FACEIT; update Windows."])], [], [], STD_UNINSTALL, [("FACEIT Support", "https://support.faceit.com")])

# ------------------------------------------------------------------ Browsers
BROWSER_PROBLEMS = [
    ("Slow browsing / high memory", ["Open the browser's task manager (Shift+Esc) and close heavy tabs.", "Disable unused extensions.", "Clear the cache (console action) with the browser closed."]),
    ("Pop-ups, redirected searches, or unknown extensions", ["Remove unknown extensions.", "Reset settings from the browser's Reset settings page.", "Check for a manual proxy (Settings Explorer → Network → Proxy).", "Run a Microsoft Defender scan."]),
    ("Videos stutter / black video", ["Toggle 'Use graphics acceleration when available'.", "Update the GPU driver."]),
]
app("chrome", "Google Chrome", [r"^Google Chrome$"], "Browser", "Google's web browser.",
    ["Continue running background apps when Chrome is closed (chrome://settings/system)", "Graphics acceleration", "Memory Saver (chrome://settings/performance)"],
    "Not in startup by default; background apps can keep it running.", [], ["chrome.exe"], BROWSER_PROBLEMS,
    ["Reset settings: chrome://settings/reset.", "Reinstall from google.com/chrome."], ["chrome://settings/reset restores defaults without deleting bookmarks or passwords."],
    STD_UNINSTALL + ["Bookmarks/passwords are kept if the customer signs in to sync before uninstalling."],
    [("Chrome Help", "https://support.google.com/chrome")],
    [(r"%LOCALAPPDATA%\Google\Chrome\User Data\Default\Cache", "Web cache"), (r"%LOCALAPPDATA%\Google\Chrome\User Data\Default\Code Cache", "Script cache"),
     (r"%LOCALAPPDATA%\Google\Chrome\User Data\Default\GPUCache", "GPU cache"), (r"%LOCALAPPDATA%\Google\Chrome\User Data\ShaderCache", "Shader cache")])

app("edge", "Microsoft Edge", [r"^Microsoft Edge$"], "Browser", "Microsoft's web browser, built into Windows.",
    ["Startup boost (edge://settings/system)", "Continue running background extensions and apps", "Efficiency mode"],
    "Startup boost keeps Edge pre-loaded at sign-in.", ["MicrosoftEdgeAutoLaunch"], ["msedge.exe"], BROWSER_PROBLEMS,
    ["Installed apps → Microsoft Edge → Modify → Repair (downloads a fresh copy, keeps data)."], ["edge://settings/reset."],
    ["Edge is part of Windows and cannot be uninstalled in most regions. Do not attempt to force-remove it — it breaks Windows features and WebView2-based apps."],
    [("Edge support", "https://support.microsoft.com/microsoft-edge")],
    [(r"%LOCALAPPDATA%\Microsoft\Edge\User Data\Default\Cache", "Web cache"), (r"%LOCALAPPDATA%\Microsoft\Edge\User Data\Default\Code Cache", "Script cache"),
     (r"%LOCALAPPDATA%\Microsoft\Edge\User Data\Default\GPUCache", "GPU cache")],
    essential=True, essential_reason="Microsoft Edge is a Windows component; removing it can break Windows features.")

app("firefox", "Mozilla Firefox", [r"^Mozilla Firefox"], "Browser", "Mozilla's web browser.", ["about:preferences → Performance", "about:processes"],
    "Not in startup.", [], ["firefox.exe"], BROWSER_PROBLEMS,
    ["Help → More troubleshooting information → Refresh Firefox (keeps bookmarks/passwords)."], ["Refresh Firefox."], STD_UNINSTALL,
    [("Firefox Support", "https://support.mozilla.org")])

app("brave", "Brave", [r"^Brave"], "Browser", "Privacy-focused Chromium browser.", [], "Not in startup.", [], ["brave.exe"], BROWSER_PROBLEMS,
    ["Reset settings at brave://settings/reset."], [], STD_UNINSTALL, [("Brave Help", "https://support.brave.com")])

app("opera", "Opera / Opera GX", [r"^Opera"], "Browser", "Chromium-based browsers; Opera GX targets gamers.", ["GX Control (RAM/CPU limiters)", "Browser assistant startup entry"],
    "Installs a 'browser assistant' startup item and auto-update tasks.", ["Opera GX Browser Assistant", "Opera Browser Assistant"], ["opera.exe"], BROWSER_PROBLEMS,
    ["Reinstall from opera.com."], [], STD_UNINSTALL, [("Opera Help", "https://help.opera.com")])

# ------------------------------------------------------------------ Productivity & cloud
app("office", "Microsoft 365 / Office", [r"^Microsoft 365", r"^Microsoft Office", r"^Office 16 Click-to-Run"], "Productivity", "Word, Excel, PowerPoint, Outlook.",
    ["File → Account → Update Options", "Add-ins (File → Options → Add-ins)"], "Click-to-Run service runs in the background.", [], ["WINWORD.EXE", "EXCEL.EXE", "OUTLOOK.EXE", "OfficeClickToRun.exe"],
    [("Office apps crash or won't open", ["Installed apps → Microsoft 365 → Modify → Quick Repair, then Online Repair if needed.", "Start the app in safe mode (hold Ctrl) to test add-ins."]),
     ("Outlook slow or not syncing", ["Start 'outlook.exe /safe' to test add-ins.", "Repair the profile from Control Panel → Mail."]),
     ("Activation errors", ["Sign in with the account that owns the licence (File → Account)."])],
    ["Quick Repair (offline) → Online Repair (full reinstall)."], [], ["Uninstall from Installed apps; for stubborn installs use Microsoft's Office uninstall support tool."],
    [("Office support", "https://support.microsoft.com/office")])

app("onedrive", "Microsoft OneDrive", [r"^Microsoft OneDrive"], "Cloud storage", "Cloud backup and sync for files, built into Windows.",
    ["Files On-Demand", "Backup of Desktop/Documents/Pictures", "Start OneDrive automatically when I sign in"], "Starts at sign-in.", ["OneDrive"], ["OneDrive.exe"],
    [("Sync stuck", ["Click the OneDrive icon to see errors; pause and resume sync.", "Reset OneDrive: run 'onedrive.exe /reset' (re-syncs everything, keeps files)."]),
     ("Files missing after setup", ["Check the OneDrive folder — Desktop/Documents may have moved into OneDrive by Folder Backup."]),
     ("Disk full", ["Enable Files On-Demand and 'Free up space' on large folders."])],
    ["Reset OneDrive (onedrive.exe /reset)."], ["onedrive.exe /reset re-syncs without deleting cloud files."],
    ["Before uninstalling, turn off Folder Backup so Desktop/Documents return to the PC; confirm cloud-only files are downloaded."],
    [("OneDrive help", "https://support.microsoft.com/onedrive")])

app("dropbox", "Dropbox", [r"^Dropbox"], "Cloud storage", "Cloud file sync.", ["Online-only files", "Start Dropbox on system startup"], "Starts at sign-in.", ["Dropbox"], ["Dropbox.exe"],
    [("High CPU while syncing", ["Wait for initial sync; exclude large folders with selective sync."])], ["Reinstall from dropbox.com."], [],
    STD_UNINSTALL + ["Confirm online-only files are not needed locally before uninstalling."], [("Dropbox Help", "https://help.dropbox.com")])

app("googledrive", "Google Drive for desktop", [r"^Google Drive"], "Cloud storage", "Google Drive sync.", ["Streaming vs mirroring"], "Starts at sign-in.", [], ["GoogleDriveFS.exe"],
    [("Drive letter missing", ["Restart Google Drive; check its settings for the drive letter."])], ["Reinstall from google.com/drive/download."], [], STD_UNINSTALL,
    [("Google Drive Help", "https://support.google.com/drive")])

app("adobecc", "Adobe Creative Cloud", [r"^Adobe Creative Cloud"], "Creative", "Manages Adobe apps, fonts, and cloud files.", ["Launch Creative Cloud at login", "File sync"],
    "Starts at sign-in by default with several helper processes.", ["Adobe Creative Cloud"], ["Creative Cloud.exe", "CCXProcess.exe"],
    [("Many background processes", ["Disable 'Launch at login' if the customer rarely uses Adobe apps."]),
     ("App updates fail", ["Sign out and in; use Adobe's Creative Cloud Cleaner Tool as a last resort."])],
    ["Reinstall Creative Cloud desktop."], [], ["Use Adobe's Creative Cloud uninstaller; uninstall individual Adobe apps first."],
    [("Adobe Creative Cloud help", "https://helpx.adobe.com/creative-cloud.html")])

app("acrobat", "Adobe Acrobat / Reader", [r"^Adobe Acrobat"], "Productivity", "PDF viewer/editor.", ["Default PDF app", "Protected Mode"], "Update service runs in the background.", [], ["Acrobat.exe", "AcroRd32.exe"],
    [("PDFs open in the wrong app", ["Set the default app for .pdf in Settings → Default apps."])], ["Help → Repair installation."], [], STD_UNINSTALL,
    [("Acrobat help", "https://helpx.adobe.com/acrobat.html")])

app("7zip", "7-Zip", [r"^7-Zip"], "Utilities", "Free archive tool (zip, 7z, rar extraction).", [], "Does not start with Windows.", [], ["7zFM.exe"],
    [("Outdated version", ["Update via winget or 7-zip.org — old versions had security issues."])], [], [], STD_UNINSTALL, [("7-Zip", "https://www.7-zip.org")])

app("winrar", "WinRAR", [r"^WinRAR"], "Utilities", "Archive tool.", [], "Does not start with Windows.", [], ["WinRAR.exe"],
    [("Outdated version", ["Update — older versions had serious vulnerabilities."])], [], [], STD_UNINSTALL, [("WinRAR", "https://www.win-rar.com")])

app("powertoys", "Microsoft PowerToys", [r"^PowerToys"], "Utilities", "Productivity utilities from Microsoft.", ["Run at startup"], "Optional at sign-in.", [], ["PowerToys.exe"],
    [], ["Reinstall from the Microsoft Store or GitHub releases."], [], STD_UNINSTALL, [("PowerToys docs", "https://learn.microsoft.com/windows/powertoys/")])

app("terminal", "Windows Terminal", [r"^Windows Terminal"], "Utilities", "Modern terminal for PowerShell/Command Prompt.", [], "Does not start with Windows.", [], ["WindowsTerminal.exe"],
    [], ["Installed apps → Windows Terminal → Advanced options → Repair."], ["Advanced options → Reset."], STD_UNINSTALL,
    [("Windows Terminal docs", "https://learn.microsoft.com/windows/terminal/")], packages=["Microsoft.WindowsTerminal_"])

app("ccleaner", "CCleaner", [r"^CCleaner"], "Utilities", "Third-party cleaning tool.", ["Smart Cleaning (Settings)", "Performance Optimizer"],
    "Installs a startup entry, a service, and update tasks.", ["CCleaner Smart Cleaning", "CCleaner Monitoring"], ["CCleaner64.exe"],
    [("Background monitoring and pop-ups", ["Turn off Smart Cleaning and Performance Optimizer, or uninstall — Windows Storage Sense covers temp cleanup."]),
     ("Registry cleaner side effects", ["Avoid registry cleaners on customer PCs; they provide no measurable benefit and can break apps."])],
    [], [], STD_UNINSTALL, [("CCleaner Support", "https://support.ccleaner.com")])

# ------------------------------------------------------------------ Hardware vendor software
app("nvidia", "NVIDIA App / GeForce Experience / Control Panel", [r"^NVIDIA app", r"^NVIDIA GeForce Experience", r"^NVIDIA Graphics Driver", r"^NVIDIA Control Panel"], "Drivers",
    "NVIDIA driver, control panel, and optional app for updates/overlay.",
    ["See the console's Graphics → NVIDIA section for driver settings", "Overlay and Instant Replay (NVIDIA App)", "Shader cache size (NVIDIA Control Panel)"],
    "Driver services start with Windows; the NVIDIA App overlay is optional.", [], ["NVIDIA app.exe", "NVIDIA Overlay.exe", "nvcontainer.exe"],
    [("Stutter or graphical corruption after a driver update", ["Clear the NVIDIA shader cache (console action) — games rebuild it.", "Clean-install the driver (NVIDIA App → Drivers → Reinstall → Clean install)."]),
     ("'Display driver stopped responding'", ["Clean-install the latest driver; remove GPU overclocks; check temperatures."]),
     ("NVIDIA Control Panel missing", ["Install 'NVIDIA Control Panel' from the Microsoft Store (DCH drivers)."])],
    ["Reinstall the driver with 'Clean install'."], ["NVIDIA Control Panel → Manage 3D settings → Restore."],
    ["Do not uninstall the NVIDIA Graphics Driver without a replacement ready — the display falls back to a basic driver.", "The NVIDIA App itself can be uninstalled safely; drivers keep working."],
    [("NVIDIA drivers", "https://www.nvidia.com/en-us/drivers/"), ("NVIDIA App", "https://www.nvidia.com/en-us/software/nvidia-app/")],
    [(r"%LOCALAPPDATA%\NVIDIA\DXCache", "DirectX shader cache"), (r"%LOCALAPPDATA%\NVIDIA\GLCache", "OpenGL/Vulkan shader cache")], packages=["NVIDIACorp.NVIDIAControlPanel"])

app("amd", "AMD Software: Adrenalin Edition", [r"^AMD Software", r"^AMD Radeon Software", r"^AMD Chipset Software"], "Drivers",
    "AMD graphics driver, control panel, and overlay.",
    ["Radeon Anti-Lag, Chill, Boost, Image Sharpening (see Graphics → AMD)", "Instant Replay", "Factory reset option during install"],
    "Driver services start with Windows.", [], ["RadeonSoftware.exe", "AMDRSServ.exe"],
    [("Driver timeout errors", ["Clean install with the 'Factory Reset' option or AMD Cleanup Utility.", "Disable tuning/undervolts in Adrenalin's Performance tab."]),
     ("Stutter after driver update", ["Clear the AMD shader cache (console action or Adrenalin → Graphics → Reset Shader Cache)."])],
    ["Reinstall with Factory Reset."], ["Adrenalin → Settings → System → Factory reset."], ["Use AMD Cleanup Utility only before reinstalling a driver."],
    [("AMD Support", "https://www.amd.com/en/support")],
    [(r"%LOCALAPPDATA%\AMD\DxCache", "DirectX shader cache"), (r"%LOCALAPPDATA%\AMD\DxcCache", "DXC shader cache"), (r"%LOCALAPPDATA%\AMD\VkCache", "Vulkan shader cache")])

app("intelgfx", "Intel Graphics Software / Arc Control", [r"^Intel\(R\) Graphics", r"^Intel Graphics Software", r"^Intel\(R\) Arc", r"^Intel Driver"], "Drivers",
    "Intel graphics driver and control software.", ["Graphics settings per game", "Driver updates (Intel Driver & Support Assistant)"], "Driver services start with Windows.", [], ["IntelGraphicsSoftware.exe", "igfxEM.exe"],
    [("Laptop display issues after updating", ["Use the laptop manufacturer's driver if the generic Intel driver causes problems."])],
    ["Reinstall the driver."], [], ["Do not remove the graphics driver without a replacement."],
    [("Intel graphics support", "https://www.intel.com/content/www/us/en/support/products/80939/graphics.html")])

app("razer", "Razer Synapse", [r"^Razer Synapse"], "Peripherals", "Razer device configuration (lighting, macros, DPI).", ["Start on boot"], "Starts at boot with services.", ["Razer Synapse"],
    ["Razer Synapse 3.exe", "RazerCentralService.exe"],
    [("Service keeps crashing", ["Update Synapse; reinstall using Razer's clean reinstall guide."]), ("Slow boot", ["Synapse services delay startup; keep only if lighting/macros are needed."])],
    ["Repair from the Razer installer."], [], STD_UNINSTALL, [("Razer Support", "https://mysupport.razer.com")])

app("ghub", "Logitech G HUB", [r"^Logitech G HUB"], "Peripherals", "Logitech gaming peripheral software.", ["Start at login"], "Starts at sign-in.", ["LGHUB"], ["lghub.exe"],
    [("Stuck on loading", ["Restart the LGHUB services or reinstall."])], ["Reinstall."], [], STD_UNINSTALL, [("Logitech Support", "https://support.logi.com")])

app("icue", "Corsair iCUE", [r"^iCUE", r"^Corsair iCUE"], "Peripherals", "Corsair RGB, fans, and peripherals.", ["Start on system startup", "Fan curves"], "Starts at sign-in.", ["Corsair iCUE5 Software"],
    ["iCUE.exe"], [("High CPU", ["Reduce lighting effects; update iCUE."]), ("Fans loud after disabling", ["iCUE may control fan curves — keep it at startup if it manages fans."])],
    ["Repair from Installed apps."], [], STD_UNINSTALL, [("Corsair Help", "https://help.corsair.com")])

app("afterburner", "MSI Afterburner", [r"^MSI Afterburner"], "Utilities", "GPU overclocking and monitoring.", ["Start with Windows", "Apply overclocking at system startup"], "Optional at sign-in.", [],
    ["MSIAfterburner.exe"], [("Crashes in games", ["Reset to defaults (the ↺ button) — overclocks are a common cause."])], [], [], STD_UNINSTALL,
    [("MSI Afterburner", "https://www.msi.com/Landing/afterburner/graphics-cards")])

# ------------------------------------------------------------------ Security
app("malwarebytes", "Malwarebytes", [r"^Malwarebytes"], "Security", "Anti-malware scanner (free on-demand, paid real-time).", ["Real-time protection", "Register with Windows Security Center"],
    "Starts with Windows.", [], ["MBAMService.exe", "mbamtray.exe"],
    [("Conflicts with another antivirus", ["Keep only one real-time product; use Malwarebytes free as an on-demand scanner."])],
    ["Use the Malwarebytes Support Tool to repair."], [], ["Use Malwarebytes Support Tool → Advanced → Clean for complete removal."], [("Malwarebytes Support", "https://support.malwarebytes.com")])

app("mcafee", "McAfee", [r"^McAfee"], "Security", "Antivirus suite, often pre-installed as a trial.", [], "Starts with Windows.", [], ["mcshield.exe"],
    [("Expired trial nagging", ["Uninstall and rely on Microsoft Defender, which turns back on automatically."])], [], [], ["Uninstall via Installed apps, then McAfee's Consumer Product Removal tool if remnants remain."],
    [("McAfee Support", "https://www.mcafee.com/support")])

app("norton", "Norton", [r"^Norton"], "Security", "Antivirus suite.", [], "Starts with Windows.", [], ["NortonSecurity.exe"],
    [("Expired subscription", ["Uninstall with Norton Remove and Reinstall tool → Advanced options → Remove only."])], [], [], ["Use the Norton Remove and Reinstall tool."],
    [("Norton Support", "https://support.norton.com")])

app("avast", "Avast / AVG", [r"^Avast", r"^AVG"], "Security", "Antivirus suites (same company).", [], "Starts with Windows.", [], ["AvastSvc.exe", "AVGSvc.exe"],
    [("Bundled browser/cleaner add-ons", ["Remove optional components the customer doesn't need."])], [], [], ["Use avastclear / AVG Clear in Safe Mode for complete removal."],
    [("Avast Support", "https://support.avast.com")])

# ------------------------------------------------------------------ Essential runtimes & Windows components
app("vcredist", "Microsoft Visual C++ Redistributables", [r"^Microsoft Visual C\+\+"], "Runtime", "Shared runtime libraries required by many games and applications.",
    [], "Not a running program.", [], [],
    [("'MSVCP140.dll / VCRUNTIME140.dll is missing'", ["Install the latest supported Visual C++ Redistributable (x64 and x86) from Microsoft."])],
    ["Installed apps → the redistributable → Modify → Repair."], [], ["Do NOT uninstall — many programs depend on specific versions, and duplicates of different years are normal."],
    [("Latest supported Visual C++ downloads", "https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist")],
    essential=True, essential_reason="Required by many applications; multiple versions side by side are normal.")

app("dotnet", ".NET runtimes", [r"^Microsoft \.NET", r"^Microsoft Windows Desktop Runtime", r"^Microsoft ASP\.NET Core"], "Runtime", "Runtime for .NET applications.",
    [], "Not a running program.", [], [], [("App asks for .NET", ["Install the exact runtime version the app requests from dotnet.microsoft.com."])],
    ["Reinstall the runtime version."], [], ["Only remove versions confirmed unused; apps break silently otherwise."],
    [(".NET downloads", "https://dotnet.microsoft.com/download")], essential=True, essential_reason="Applications depend on specific runtime versions.")

app("webview2", "Microsoft Edge WebView2 Runtime", [r"^Microsoft Edge WebView2"], "Runtime", "Embedded browser engine used by Widgets, Teams, Outlook, and many launchers.",
    [], "Runs inside the apps that use it.", [], ["msedgewebview2.exe"], [("Apps show blank windows", ["Repair WebView2 from Installed apps → Modify."])],
    ["Installed apps → WebView2 Runtime → Modify → Repair."], [], ["Do NOT uninstall — Windows and many apps depend on it."],
    [("WebView2", "https://developer.microsoft.com/microsoft-edge/webview2/")], essential=True, essential_reason="Windows features and many apps depend on it.")

app("store", "Microsoft Store", [r"^Microsoft Store$"], "Windows component", "Installs and updates Store apps.", [], "Not a startup app.", [], ["WinStore.App.exe"],
    [("Store won't open or download", ["Run wsreset.exe (clears the Store cache).", "Installed apps → Microsoft Store → Advanced options → Repair/Reset.", "Check date/time and Windows Update."])],
    ["Advanced options → Repair."], ["wsreset.exe or Advanced options → Reset."], ["Do NOT remove the Microsoft Store — app updates and many features depend on it."],
    [("Store troubleshooting", "https://support.microsoft.com/account-billing/troubleshoot-problems-with-installing-games-and-apps-from-microsoft-store-ea36b8ad-7e5f-4dc3-ba8f-35f7d44ae7d6")],
    packages=["Microsoft.WindowsStore_"], essential=True, essential_reason="Required for Store app updates and some Windows features.")

app("java", "Java Runtime (Oracle/OpenJDK)", [r"^Java( \d+)? Update", r"^Java\(TM\)", r"^Eclipse Temurin", r"^Microsoft Build of OpenJDK"], "Runtime", "Runtime for Java applications (e.g. Minecraft mods, some business apps).",
    [], "Java Update Scheduler may start with Windows.", [], ["jusched.exe", "javaw.exe"], [("Outdated Java", ["Remove versions no app needs; old versions are a security risk."])],
    ["Reinstall the required version."], [], ["Confirm no application requires the version before removing it."], [("Java help", "https://www.java.com/en/download/help/")])

out = os.path.join(os.path.dirname(__file__), "..", "..", "src", "SoftReset.Core", "Knowledge", "Data", "apps.json")
keys = [a["key"] for a in A]
if len(keys) != len(set(keys)): raise SystemExit("Duplicate app keys")
with open(out, "w", encoding="utf-8") as f:
    json.dump(A, f, indent=1, ensure_ascii=False)
    f.write("\n")
print(f"Wrote {len(A)} application guides")

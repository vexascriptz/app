#!/usr/bin/env python3
"""
Generates src/SoftReset.Core/Knowledge/Data/settings.json — the Windows Settings Explorer catalog.

Every entry carries the eleven fields the console shows: name, description, how the current
value is read, available values, recommendations by use case, performance implications,
risks/compatibility, admin requirement, restart requirement, rollback instructions, and
working controls. Controls only use documented mechanisms: ms-settings: URIs, built-in
tools, Control Panel canonical names, and registry values that the corresponding Windows
setting itself writes. Settings that Windows does not expose programmatically are marked
read type "none" and only open the relevant Windows page.

Run:  python3 build/kb/settings_catalog.py
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
from settings_part2 import more_entries  # noqa: E402

E = []  # all entries

HKCU, HKLM, HKU = "HKCU", "HKLM", "HKU"
ADV = r"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"
CDM = r"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager"
PERSONALIZE = r"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"
WU_UX = r"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings"
CONSENT = r"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore"

DEFAULT_ROLLBACK = ("Every change made from the console is backed up first. Use Change History → Roll back, "
                    "or choose the previous value with the control below.")


def S(id, name, cat, desc, read, values, recs, perf, risks, admin=False, restart="none", rollback=None,
      controls=(), minBuild=0, maxBuild=None, editions=None, tags=(), ref=None, sub=None):
    if rollback is None:
        has_write = any(c["type"] in ("registry-set", "registry-delete", "action") for c in controls)
        rollback = DEFAULT_ROLLBACK if has_write else (
            "Nothing is changed by the console. Changes made on the opened Windows page are reverted on that same page.")
    e = {
        "id": id, "name": name, "category": cat, "description": desc, "read": read, "values": list(values),
        "recommendations": [dict(useCase=u, value=v, why=w) for (u, v, w) in recs],
        "performance": perf, "risks": risks, "adminRequired": admin, "restart": restart, "rollback": rollback,
        "controls": list(controls), "minBuild": minBuild, "tags": list(tags),
    }
    if maxBuild: e["maxBuild"] = maxBuild
    if editions: e["editions"] = editions
    if ref: e["reference"] = ref
    if sub: e["subcategory"] = sub
    E.append(e)


def reg(hive, path, value, map=None, missing=None, policy=None):
    r = {"type": "registry", "hive": hive, "path": path, "value": value}
    if map: r["map"] = map
    if missing: r["missing"] = missing
    if policy: r["policyOverrides"] = policy
    return r


def probe(p): return {"type": "probe", "probe": p}
def svc(name): return {"type": "service", "service": name}
def task(path): return {"type": "task", "path": path}
def none(reason=None): return {"type": "none", "reason": reason} if reason else {"type": "none"}


def uri(label, target): return {"type": "open", "label": label, "launch": "Uri", "target": target}
def tool(label, exe, args=None):
    c = {"type": "open", "label": label, "launch": "SystemTool", "target": exe}
    if args: c["arguments"] = args
    return c
def cpl(label, name, page=None):
    c = {"type": "open", "label": label, "launch": "ControlPanelItem", "target": name}
    if page: c["arguments"] = page
    return c
def applet(label, file): return {"type": "open", "label": label, "launch": "ControlPanelApplet", "target": file}
def msc(label, file): return {"type": "open", "label": label, "launch": "Msc", "target": file}
def regset(label, kind, data, result): return {"type": "registry-set", "label": label, "kind": kind, "data": str(data), "resultLabel": result}
def regdel(label, result): return {"type": "registry-delete", "label": label, "resultLabel": result}
def act(label, action, params=None):
    c = {"type": "action", "label": label, "actionId": action}
    if params: c["parameters"] = params
    return c


ONOFF = {"1": "On", "0": "Off"}
OFFON_INV = {"0": "On", "1": "Off"}

# =========================================================================== System
S("system.computer-name", "Computer name", "System",
  "The name that identifies this PC on the network and in the customer's Microsoft account device list.",
  probe("system.computer-name"), ["Any name up to 15 characters (letters, numbers, hyphens)"],
  [("Home", "Something recognisable, e.g. JORDAN-DESKTOP", "Makes the PC easy to find on the network and in account device lists."),
   ("Business", "Follow the organisation's naming scheme", "Domain/MDM tooling often depends on names.")],
  "None.", "Renaming breaks saved network shares that point to the old name and requires a restart. Domain-joined PCs must be renamed by their administrator.",
  admin=True, restart="restart", controls=[uri("Open About (Rename this PC)", "ms-settings:about"), tool("Open System Properties → Computer Name", "SystemPropertiesComputerName.exe")],
  tags=["rename", "hostname", "about"])

S("system.edition", "Windows edition and activation", "System",
  "The Windows edition (Home, Pro, …), version, and activation state.",
  probe("system.edition"), ["Home", "Pro", "Education", "Enterprise", "Pro for Workstations"],
  [("All", "Genuine, activated Windows", "Unactivated Windows shows watermarks and limits personalisation.")],
  "None.", "Changing edition requires a valid product key. Never use unofficial activators — they are a common malware source.",
  controls=[uri("Open Activation", "ms-settings:activation"), uri("Open About", "ms-settings:about")], tags=["activation", "license", "version"])

S("system.uptime", "Time since last full restart", "System",
  "How long Windows has been running since its last full boot. With Fast Startup on, 'Shut down' does not reset this.",
  probe("system.uptime"), ["Duration"],
  [("All", "Restart at least weekly", "Clears memory leaks and completes update installation.")],
  "Very long uptimes allow leaks and driver issues to accumulate.", "None.",
  controls=[tool("Open Task Manager (Performance → CPU shows uptime)", "taskmgr.exe")], tags=["restart", "boot"])

S("system.clipboard-history", "Clipboard history", "System",
  "Keeps a history of copied items (Win+V) instead of only the last one.",
  reg(HKCU, r"Software\Microsoft\Clipboard", "EnableClipboardHistory", ONOFF, "Off (default)",
      policy=[reg(HKLM, r"SOFTWARE\Policies\Microsoft\Windows\System", "AllowClipboardHistory", {"0": "Disabled by policy", "1": "Allowed by policy"})]),
  ["On", "Off"],
  [("Office", "On", "Speeds up copy/paste-heavy work."), ("Shared/public PC", "Off", "Copied passwords and personal data stay in history.")],
  "Negligible.", "Copied sensitive text (passwords, card numbers) stays in the history until cleared or restart.",
  controls=[regset("Turn on", "DWord", 1, "On"), regset("Turn off", "DWord", 0, "Off"), uri("Open Clipboard settings", "ms-settings:clipboard")],
  minBuild=17763, tags=["win+v", "copy", "paste"])

S("system.remote-desktop", "Remote Desktop (incoming connections)", "System",
  "Allows other computers to connect to this PC with Remote Desktop. Only Pro, Education, and Enterprise editions can host sessions.",
  reg(HKLM, r"SYSTEM\CurrentControlSet\Control\Terminal Server", "fDenyTSConnections", {"1": "Off", "0": "On"}, "Off"),
  ["On", "Off"],
  [("Home users", "Off", "Remote Desktop exposed to the internet is a major ransomware entry point."),
   ("Business with VPN", "On only if required", "Use with Network Level Authentication and a VPN.")],
  "None.", "Security-sensitive. The console only reports this value and opens the Windows page; it never enables remote access.",
  editions=["Professional", "Enterprise", "Education", "ProfessionalWorkstation"],
  controls=[uri("Open Remote Desktop settings", "ms-settings:remotedesktop"), tool("Open System Properties → Remote", "SystemPropertiesRemote.exe")],
  tags=["rdp", "remote", "security"])

S("system.date-time", "Date, time, and time synchronisation", "System",
  "Time zone and automatic internet time synchronisation (Windows Time service).",
  svc("W32Time"), ["Set time automatically: On/Off", "Time zone"],
  [("All", "Set time automatically: On", "Wrong clocks break HTTPS sites, sign-ins, and updates.")],
  "None.", "An incorrect clock causes certificate errors and failed Microsoft account sign-ins.",
  controls=[uri("Open Date & time", "ms-settings:dateandtime"), applet("Open classic Date and Time", "timedate.cpl")], tags=["clock", "ntp", "timezone"])

S("system.developer-mode", "Developer Mode", "System",
  "Allows installing unsigned app packages (sideloading) and enables developer features.",
  reg(HKLM, r"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock", "AllowDevelopmentWithoutDevLicense", ONOFF, "Off"),
  ["On", "Off"], [("Most customers", "Off", "Reduces the ability to install untrusted app packages.")],
  "None.", "Leaving it on lowers protection against untrusted app packages.",
  admin=True, controls=[uri("Open For developers", "ms-settings:developers")], tags=["sideload"])

S("system.troubleshooters", "Built-in troubleshooters", "System",
  "Microsoft's automated troubleshooters (network, audio, printer, Windows Update). On current Windows 11 builds these open in the Get Help app.",
  none(), ["Not a setting"], [("All", "Run the relevant troubleshooter early", "They fix common configuration problems and log what they changed.")],
  "None.", "None — troubleshooters prompt before changing anything.",
  controls=[uri("Open Troubleshoot", "ms-settings:troubleshoot")], tags=["fix", "get help"])

S("system.recovery", "Recovery options (Reset this PC, advanced startup)", "System",
  "Reset this PC, go back to the previous Windows version, and restart into Windows Recovery Environment.",
  none(), ["Not a setting"],
  [("Last resort", "Reset this PC → Keep my files", "Reinstalls Windows while keeping personal files; apps must be reinstalled.")],
  "None.", "Reset this PC removes installed applications. Always back up first.",
  controls=[uri("Open Recovery", "ms-settings:recovery"), tool("Create a recovery drive", "RecoveryDrive.exe")], tags=["reset", "reinstall", "winre"])

# =========================================================================== Display
S("display.refresh-rate", "Refresh rate", "Display",
  "How many times per second the monitor redraws the image. Windows frequently defaults high-refresh monitors to 60 Hz.",
  probe("display.refresh"), ["Rates the monitor reports (e.g. 60, 120, 144, 165, 240 Hz)"],
  [("Gaming", "Highest supported rate", "Smoother motion and lower input latency."),
   ("Office", "Highest supported rate", "Smoother scrolling and cursor movement."),
   ("Laptop on battery", "60 Hz or dynamic refresh", "Lower refresh saves battery.")],
  "Higher refresh uses slightly more GPU power; no downside on desktops.",
  "If the cable/port cannot carry the bandwidth the screen may go blank — the console reverts automatically without confirmation.",
  controls=[act("Set highest refresh rate", "display.refresh-rate"), uri("Open Advanced display", "ms-settings:display-advanced")],
  tags=["hz", "fps", "monitor", "144hz"])

S("display.resolution", "Display resolution", "Display",
  "Number of pixels on the screen. The recommended value is the panel's native resolution.",
  probe("display.resolution"), ["Resolutions reported by the display driver"],
  [("All", "Native (marked Recommended)", "Non-native resolutions look blurry.")],
  "Lower resolutions reduce GPU load in games only; use in-game resolution settings instead.",
  "A wrong resolution can make text blurry. Windows reverts automatically after 15 seconds if not confirmed.",
  controls=[uri("Open Display settings", "ms-settings:display")], tags=["pixels", "blurry"])

S("display.scaling", "Scale (text and app size)", "Display",
  "Makes text, apps, and other items larger on high-DPI displays.",
  probe("display.scale"), ["100%", "125%", "150%", "175%", "200%", "Custom"],
  [("Laptops 14\"–16\" at 1080p", "125%–150% (Recommended value)", "Readable without wasting space."),
   ("Accessibility", "Higher scale or Text size", "Larger text for low-vision users.")],
  "None.", "Some older apps appear blurry at non-100% scaling; use the app's compatibility settings (High DPI override).",
  restart="signout", controls=[uri("Open Display settings", "ms-settings:display")], tags=["dpi", "zoom", "size"])

S("display.night-light", "Night light", "Display",
  "Reduces blue light in the evening. Stored in an undocumented binary format, so the console does not read it.",
  none("Night light state is stored in an undocumented binary blob."), ["On", "Off", "Scheduled"],
  [("Evening use", "Scheduled", "Warmer colours in the evening; colour-critical work should turn it off.")],
  "None.", "Tints colours — turn off for photo/video editing.",
  controls=[uri("Open Night light", "ms-settings:nightlight")], tags=["blue light"])

S("display.multiple-displays", "Multiple displays", "Display",
  "Arrangement, primary display, and duplicate/extend mode (Win+P).",
  probe("display.count"), ["Extend", "Duplicate", "Second screen only", "PC screen only"],
  [("Desktop", "Extend, with the main monitor as primary", "Taskbar and new windows appear on the primary display.")],
  "Each additional high-refresh display adds a little GPU idle load.",
  "Mismatched refresh rates between monitors can cause stutter in some games when windowed.",
  controls=[uri("Open Display settings", "ms-settings:display"), tool("Open Project (Win+P)", "DisplaySwitch.exe")], tags=["monitor", "dual"])

S("display.hdr", "HDR", "Display",
  "High dynamic range for HDR-capable monitors.",
  none(), ["On", "Off"],
  [("HDR monitor with games/videos", "On", "Brighter highlights and wider colour."),
   ("Entry-level 'HDR400' monitors", "Often Off", "Washed-out desktop colours are common on low-end HDR panels.")],
  "None.", "SDR content can look dull with HDR on — adjust 'SDR content brightness'.",
  controls=[uri("Open Display settings", "ms-settings:display")], tags=["hdr10"])

S("display.cleartype", "ClearType text smoothing", "Display",
  "Sub-pixel font smoothing that makes text sharper on LCD screens.",
  reg(HKCU, r"Control Panel\Desktop", "FontSmoothing", {"2": "On", "0": "Off"}, "On"),
  ["On", "Off"], [("All", "On, tuned with the ClearType Text Tuner", "Improves readability.")],
  "None.", "Turning font smoothing off makes text jagged.",
  controls=[tool("Open ClearType Text Tuner", "cttune.exe")], tags=["font", "blurry text"])

S("display.color-management", "Colour profiles", "Display",
  "ICC colour profiles assigned to each display.",
  none(), ["Profiles installed for each monitor"],
  [("Photo/video work", "Calibrated profile from a colorimeter", "Accurate colours.")],
  "None.", "A wrong profile gives tinted colours.",
  controls=[tool("Open Colour Management", "colorcpl.exe")], tags=["icc", "calibration"])

# =========================================================================== Graphics
S("graphics.hags", "Hardware-accelerated GPU scheduling", "Graphics",
  "Lets the GPU schedule its own work and manage video memory. Required for DLSS Frame Generation on NVIDIA RTX 40/50.",
  reg(HKLM, r"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", {"2": "On", "1": "Off"}, "Not set (driver/Windows default)"),
  ["On", "Off"],
  [("RTX 40/50 with frame generation", "On", "Required by the feature."),
   ("Older games/capture tools with stutter", "Off", "Some titles behave better without it."),
   ("Everyone else", "Measure", "Results vary by game and driver.")],
  "Small latency/CPU-overhead differences; can be positive or negative per game.",
  "Only works if the GPU and driver support it. Needs a restart.",
  admin=True, restart="restart",
  controls=[act("Change (backed up)", "gaming.hags", {"state": "On"}), uri("Open Graphics settings", "ms-settings:display-advancedgraphics")],
  minBuild=19041, tags=["hags", "gpu", "frame generation", "latency"])

S("graphics.windowed-optimizations", "Optimizations for windowed games", "Graphics",
  "Uses the modern flip presentation model for DirectX 10/11 games in windowed and borderless modes.",
  reg(HKCU, r"Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings", {"*": "{value}"}, "Default (Off)"),
  ["On", "Off"],
  [("Gaming (borderless windowed)", "On", "Lower latency; enables Auto HDR and VRR for windowed games."),
   ("Older game with glitches", "Off", "A few titles misbehave.")],
  "Lower latency in borderless games.", "Rare visual glitches in old titles.",
  controls=[act("Change (backed up)", "gaming.windowed-optimizations", {"state": "On"}), uri("Open Graphics settings", "ms-settings:display-advancedgraphics")],
  minBuild=22621, tags=["borderless", "flip model", "latency"])

S("graphics.per-app-gpu", "Graphics preference per app", "Graphics",
  "Chooses the GPU (power saving / high performance) for each application.",
  none("Stored per application; see the per-app list in the action."), ["Let Windows decide", "Power saving", "High performance"],
  [("Laptop with dedicated GPU, game runs slowly", "High performance for the game", "Ensures the dedicated GPU is used."),
   ("Battery life", "Power saving for browsers/office apps", "Keeps the dedicated GPU idle.")],
  "Can multiply FPS on hybrid laptops when a game was on the integrated GPU.", "Higher power use for that app.",
  controls=[act("Set per-app preference", "gaming.gpu-preference"), uri("Open Graphics settings", "ms-settings:display-advancedgraphics")],
  tags=["optimus", "integrated", "dedicated", "laptop"])

S("graphics.vrr-optimization", "Variable refresh rate (Windows optimisation)", "Graphics",
  "Lets Windows apply VRR (G-SYNC Compatible/FreeSync) to DirectX 11 games that don't support it natively.",
  reg(HKCU, r"Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings", {"*": "{value}"}, "Default"),
  ["On", "Off"], [("VRR monitor", "On", "Smoother frame pacing.")],
  "Smoother frame pacing on VRR monitors.", "Rare flicker in menus on some monitors.",
  controls=[uri("Open Graphics settings", "ms-settings:display-advancedgraphics")], minBuild=19041, tags=["freesync", "gsync"])

S("graphics.driver", "Graphics driver version", "Graphics",
  "The installed GPU driver. See the Graphics section for vendor-specific details.",
  probe("gpu.driver"), ["Vendor driver versions"],
  [("Gaming", "Latest stable vendor driver", "Game fixes and optimisations."),
   ("Laptops with switchable graphics", "Manufacturer driver if vendor driver causes problems", "OEM customisations.")],
  "Newer drivers often fix stutter and crashes.", "Driver updates occasionally introduce regressions; note the previous version.",
  controls=[msc("Open Device Manager", "devmgmt.msc"), tool("Open DirectX Diagnostic Tool", "dxdiag.exe")], tags=["nvidia", "amd", "intel", "driver"])

# =========================================================================== Sound
S("sound.devices", "Output and input devices", "Sound",
  "Which speakers/headphones and microphone Windows uses by default.",
  probe("audio.devices"), ["Installed audio endpoints"],
  [("Headset users", "Headset set as default and default communications device", "Prevents sound coming from the wrong device.")],
  "None.", "Apps like Discord/Zoom have their own device selection that can override Windows.",
  controls=[uri("Open Sound settings", "ms-settings:sound"), applet("Open classic Sound panel", "mmsys.cpl")], tags=["speaker", "headphones", "microphone", "audio"])

S("sound.app-volume", "Volume mixer (per-app volume and device)", "Sound",
  "Per-application volume and output device.",
  none(), ["Per-app volume 0–100", "Per-app device"],
  [("Streamers/gamers", "Route chat and game to different devices if needed", "Independent control.")],
  "None.", "A muted app here looks like 'no sound' in that app.",
  controls=[uri("Open Volume mixer", "ms-settings:apps-volume")], tags=["mixer", "muted"])

S("sound.ducking", "Communications activity (auto volume reduction)", "Sound",
  "When Windows detects a call, it lowers other sounds. Many gamers find game audio dropping during Discord calls.",
  reg(HKCU, r"Software\Microsoft\Multimedia\Audio", "UserDuckingPreference",
      {"0": "Mute all other sounds", "1": "Reduce other sounds by 80%", "2": "Reduce other sounds by 50%", "3": "Do nothing"}, "Reduce other sounds by 80% (default)"),
  ["Mute all other sounds", "Reduce by 80% (default)", "Reduce by 50%", "Do nothing"],
  [("Gaming with voice chat", "Do nothing", "Game audio stays at full volume during calls."), ("Office calls", "Reduce by 80%", "Music quietens during calls.")],
  "None.", "None.",
  controls=[regset("Do nothing", "DWord", 3, "Do nothing"), regset("Reduce by 80% (default)", "DWord", 1, "Reduce other sounds by 80%"), applet("Open Sound → Communications", "mmsys.cpl")],
  tags=["discord", "volume drops", "ducking"])

S("sound.enhancements", "Audio enhancements and spatial sound", "Sound",
  "Driver enhancements (loudness, virtual surround) and Windows Sonic/Dolby Atmos spatial sound.",
  none(), ["On/Off per device"],
  [("Audio problems (crackling, no sound in one app)", "Turn enhancements off", "Driver enhancement bugs are a common cause.")],
  "Enhancements add small processing latency.", "Spatial sound can make stereo music sound processed.",
  controls=[uri("Open Sound settings", "ms-settings:sound"), applet("Open classic Sound panel", "mmsys.cpl")], tags=["crackling", "spatial", "sonic"])

# =========================================================================== Notifications
S("notifications.toasts", "Notifications", "Notifications",
  "Whether apps and Windows can show notification banners.",
  reg(HKCU, r"Software\Microsoft\Windows\CurrentVersion\PushNotifications", "ToastEnabled", ONOFF, "On (default)"),
  ["On", "Off"], [("Most users", "On, with noisy apps turned off individually", "Keeps important alerts (security, backup)."),
                 ("Presentations/streaming", "Use Do not disturb", "Temporary silence without losing alerts.")],
  "None.", "Turning all notifications off hides security and backup warnings too.",
  controls=[regset("Turn on", "DWord", 1, "On"), regset("Turn off", "DWord", 0, "Off"), uri("Open Notifications", "ms-settings:notifications")],
  tags=["toast", "banner", "do not disturb"])

S("notifications.lock-screen", "Notifications on the lock screen", "Notifications",
  "Shows notification content while the PC is locked.",
  reg(HKCU, r"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings", "NOC_GLOBAL_SETTING_ALLOW_TOASTS_ABOVE_LOCK", ONOFF, "On (default)"),
  ["On", "Off"], [("Shared offices", "Off", "Prevents message previews being visible on a locked PC.")],
  "None.", "Message previews can expose private information.",
  controls=[regset("Turn off", "DWord", 0, "Off"), regset("Turn on", "DWord", 1, "On"), uri("Open Notifications", "ms-settings:notifications")], tags=["privacy", "lock"])

S("notifications.tips", "Tips and suggestions notifications", "Notifications",
  "'Get tips and suggestions when using Windows' notifications.",
  reg(HKCU, CDM, "SubscribedContent-338389Enabled", ONOFF, "On (default)"),
  ["On", "Off"], [("Experienced users", "Off", "Fewer interruptions."), ("New users", "On", "Helpful hints.")],
  "None.", "None.",
  controls=[regset("Turn off", "DWord", 0, "Off"), regset("Turn on", "DWord", 1, "On"), uri("Open Notifications", "ms-settings:notifications")], tags=["ads", "suggestions"])

S("notifications.welcome-experience", "Windows welcome experience after updates", "Notifications",
  "Full-screen 'what's new' pages after feature updates.",
  reg(HKCU, CDM, "SubscribedContent-310093Enabled", ONOFF, "On (default)"),
  ["On", "Off"], [("Most users", "Off", "Avoids full-screen interruptions after updates.")],
  "None.", "None.",
  controls=[regset("Turn off", "DWord", 0, "Off"), regset("Turn on", "DWord", 1, "On"), uri("Open Notifications", "ms-settings:notifications")], tags=["oobe"])

S("notifications.finish-setup", "Suggest ways to finish setting up", "Notifications",
  "The 'Let's finish setting up your device' screen that promotes Microsoft services.",
  reg(HKCU, r"Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement", "ScoobeSystemSettingEnabled", ONOFF, "On (default)"),
  ["On", "Off"], [("Most users", "Off", "Prevents repeated setup prompts.")],
  "None.", "None.",
  controls=[regset("Turn off", "DWord", 0, "Off"), regset("Turn on", "DWord", 1, "On")], tags=["scoobe", "nag"])

# =========================================================================== Power
S("power.plan", "Power plan", "Power",
  "The active power scheme controlling CPU performance states, sleep, and device power saving.",
  probe("power.active-scheme"), ["Balanced (default)", "Power saver", "High performance", "Ultimate Performance", "OEM plans"],
  [("Most PCs", "Balanced", "Modern CPUs boost fully under Balanced."),
   ("Older desktop, CPU-bound games", "High performance", "Keeps clocks high; measure the gain."),
   ("Laptop on battery", "Balanced with battery saver", "Battery life.")],
  "Power saver noticeably slows the PC; High performance adds power and heat for small gains on modern CPUs.",
  "OEM utilities may switch plans automatically. Modern Standby PCs only expose Balanced (use the power mode slider).",
  controls=[act("Change power plan (backed up)", "power.plan"), cpl("Open Power Options", "Microsoft.PowerOptions"), uri("Open Power & battery", "ms-settings:powersleep")],
  tags=["performance", "battery", "balanced", "high performance"])

S("power.fast-startup", "Fast Startup", "Power",
  "Shut down hibernates the kernel so the next boot is faster.",
  reg(HKLM, r"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", ONOFF, "On"),
  ["On", "Off"],
  [("PCs with driver glitches that persist after shutdown", "Off", "Every shutdown becomes a clean start."),
   ("Dual-boot / Wake-on-LAN users", "Off", "Prevents locked NTFS volumes and WoL failures."),
   ("Slow HDD-based PCs", "On", "Faster cold boot.")],
  "Off adds a few seconds to cold boots.", "With it on, uptime never resets on shutdown — restarts are needed to apply some updates/drivers.",
  admin=True, controls=[act("Change (backed up)", "power.fast-startup", {"state": "Off"}), cpl("Open 'Choose what the power buttons do'", "Microsoft.PowerOptions", "/page pageGlobalSettings")],
  tags=["hiberboot", "shutdown", "boot"])

S("power.hibernation", "Hibernation", "Power",
  "Saves memory to hiberfil.sys and powers off completely. Fast Startup depends on it.",
  probe("power.hibernation"), ["On", "Off"],
  [("Laptops", "On", "Protects work if the battery runs out during sleep."),
   ("Small SSD nearly full", "Off", "Frees roughly 40% of RAM size on disk.")],
  "None while running.", "Off disables Fast Startup and the Hibernate option.",
  admin=True, controls=[act("Change (backed up)", "power.hibernation")], tags=["hiberfil", "disk space"])

S("power.usb-selective-suspend", "USB selective suspend", "Power",
  "Allows Windows to power down idle USB devices.",
  probe("power.usb-selective-suspend"), ["Enabled (default)", "Disabled"],
  [("USB devices disconnect or lag (headsets, controllers, audio interfaces)", "Disabled (plugged in)", "Standard fix."),
   ("Laptop on battery", "Enabled", "Saves power.")],
  "No performance effect.", "Slightly higher idle power use.",
  controls=[act("Change (backed up)", "power.usb-selective-suspend", {"state": "Disabled"}), applet("Open Advanced power settings", "powercfg.cpl")],
  tags=["usb", "disconnect"])

S("power.processor-max", "Maximum processor state", "Power",
  "Caps CPU speed as a percentage of maximum in the active plan.",
  probe("power.processor-max"), ["5%–100%"],
  [("All (plugged in)", "100%", "Lower values cap performance."), ("Hot laptop as a temporary measure", "99% disables turbo boost", "Cooler but slower; fix cooling instead.")],
  "Values below 100% directly reduce CPU performance.", "Raising it on a laptop with poor cooling increases heat.",
  controls=[act("Restore to 100% (backed up)", "power.processor-max-state"), applet("Open Advanced power settings", "powercfg.cpl")], tags=["cpu", "throttle", "turbo"])

S("power.processor-min", "Minimum processor state", "Power",
  "The lowest CPU performance level the plan allows at idle.",
  probe("power.processor-min"), ["0%–100%"],
  [("All", "5% (Balanced default)", "Lets the CPU idle down; boost is unaffected."),
   ("Latency-sensitive audio work", "Higher values", "Can reduce latency spikes at the cost of power.")],
  "100% keeps the CPU at high clocks constantly — little benefit on modern CPUs.", "Higher idle power and heat.",
  controls=[applet("Open Advanced power settings", "powercfg.cpl")], tags=["cpu", "idle"])

S("power.sleep", "Screen and sleep timeouts", "Power",
  "When the screen turns off and the PC sleeps.",
  probe("power.sleep-ac"), ["Minutes or Never"],
  [("Desktops", "Screen 10–15 min, sleep 30 min+", "Saves energy."), ("Downloads/servers", "Sleep: Never while plugged in", "Prevents interruptions.")],
  "None.", "Sleep can interrupt long downloads or remote access.",
  controls=[uri("Open Power & sleep", "ms-settings:powersleep")], tags=["timeout", "screen off"])

S("power.battery-saver", "Battery saver / Energy saver", "Power",
  "Limits background activity and brightness on battery (Energy saver on newer Windows 11 builds).",
  none(), ["On", "Off", "Automatic at %"],
  [("Laptops", "Automatic at 20%", "Extends remaining battery.")],
  "Reduces performance and background sync while on.", "Background sync (mail, OneDrive) pauses.",
  controls=[uri("Open Battery saver", "ms-settings:batterysaver")], tags=["laptop", "energy saver"])

S("power.buttons-lid", "Power button and lid actions", "Power",
  "What pressing the power button or closing the lid does.",
  none(), ["Do nothing", "Sleep", "Hibernate", "Shut down", "Turn off the display"],
  [("Laptops used docked", "Lid: Do nothing (plugged in)", "Use external monitors with the lid closed.")],
  "None.", "A lid set to 'Do nothing' on battery can overheat in a bag.",
  controls=[cpl("Open 'Choose what the power buttons do'", "Microsoft.PowerOptions", "/page pageGlobalSettings")], tags=["lid", "power button"])

# =========================================================================== Storage
S("storage.free-space", "Storage usage", "Storage",
  "Free space on each drive and what is using it.",
  probe("storage.free"), ["Free space"],
  [("All", "Keep 15%+ free on the Windows drive", "Updates, page file, and temp files need room.")],
  "Below ~10% free, updates fail and performance degrades.", "None.",
  controls=[uri("Open Storage", "ms-settings:storagesense"), act("Clear temporary files", "storage.user-temp")], tags=["disk space", "full"])

S("storage.storage-sense", "Storage Sense", "Storage",
  "Automatically deletes temporary files and old Recycle Bin/Downloads contents on a schedule.",
  reg(HKCU, r"Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy", "01", ONOFF, "Off (default)",
      policy=[reg(HKLM, r"SOFTWARE\Policies\Microsoft\Windows\StorageSense", "AllowStorageSenseGlobal", {"0": "Disabled by policy", "1": "Enabled by policy"})]),
  ["On", "Off"],
  [("Small SSDs", "On, Downloads cleanup Never", "Keeps temp files in check without deleting downloads."),
   ("Users who keep files in Downloads", "Make sure 'Delete files in Downloads' is Never", "Prevents unexpected deletion.")],
  "Keeps free space healthy.", "Can delete files in Downloads and the Recycle Bin if configured to.",
  controls=[regset("Turn on", "DWord", 1, "On"), regset("Turn off", "DWord", 0, "Off"), uri("Configure Storage Sense", "ms-settings:storagepolicies")], tags=["cleanup", "automatic"])

S("storage.save-locations", "Where new content is saved", "Storage",
  "Default drive for new apps, documents, music, and pictures.",
  none(), ["Any NTFS drive"], [("PCs with a small SSD and a big HDD", "Save media to the HDD", "Saves SSD space.")],
  "Apps installed to an HDD load slower.", "Removable drives as save locations break when unplugged.",
  controls=[uri("Open Save locations", "ms-settings:savelocations")], tags=["default drive"])

S("storage.disk-cleanup", "Disk Cleanup", "Storage",
  "The classic cleanup tool, including 'Clean up system files' for old update packages.",
  none(), ["Not a setting"], [("Low space", "Run with 'Clean up system files'", "Removes previous Windows installations and update leftovers.")],
  "Frees space.", "Removing 'Previous Windows installation(s)' prevents rolling back a feature update.",
  controls=[tool("Open Disk Cleanup", "cleanmgr.exe"), act("Clear Windows Update cache", "storage.update-cache")], tags=["cleanmgr"])

S("storage.optimize-drives", "Optimise drives (defrag/TRIM)", "Storage",
  "Scheduled defragmentation for HDDs and TRIM for SSDs.",
  task(r"\Microsoft\Windows\Defrag\ScheduledDefrag"), ["Weekly (default)", "Off"],
  [("All", "On (weekly)", "HDDs stay defragmented; SSDs receive TRIM, which maintains write performance.")],
  "Keeps HDD performance and SSD write speed healthy.", "None — Windows never defragments SSDs.",
  controls=[tool("Open Optimise Drives", "dfrgui.exe"), act("Enable the scheduled task", "tasks.enabled", {"task": r"\Microsoft\Windows\Defrag\ScheduledDefrag", "enabled": "true"})],
  tags=["defrag", "trim", "ssd"])

S("storage.disks", "Disks and volumes", "Storage",
  "Partitions, drive letters, and disk health.",
  none(), ["Not a setting"], [("New drive not visible", "Initialise and format it in Disk Management", "New disks ship unformatted.")],
  "None.", "Disk Management can delete partitions — be careful.",
  controls=[msc("Open Disk Management", "diskmgmt.msc"), uri("Open Disks & volumes", "ms-settings:disksandvolumes")], tags=["partition", "drive letter"])

S("storage.indexing", "Search indexing", "Storage",
  "Windows Search indexes files for fast search in Start, File Explorer, and Outlook.",
  svc("WSearch"), ["Classic (libraries/desktop)", "Enhanced (whole PC)"],
  [("Most users", "Classic", "Indexes common locations with minimal overhead."),
   ("High CPU from SearchIndexer after big file copies", "Wait or pause indexing", "Indexing settles once caught up.")],
  "Indexing uses CPU/disk briefly after large changes.", "Disabling the service breaks fast search in Start, Outlook, and File Explorer.",
  controls=[uri("Open Searching Windows", "ms-settings:cortana-windowssearch"), cpl("Open Indexing Options", "Microsoft.IndexingOptions")], tags=["search", "searchindexer"])

# =========================================================================== Multitasking
S("multitasking.snap", "Snap windows", "Multitasking",
  "Drag windows to screen edges to arrange them; Snap layouts on Windows 11.",
  reg(HKCU, r"Control Panel\Desktop", "WindowArrangementActive", {"1": "On", "0": "Off"}, "On (default)"),
  ["On", "Off"], [("Most users", "On", "Fast window arrangement."), ("Users who keep snapping by accident", "Off", "Prevents unwanted resizing.")],
  "None.", "None.", restart="signout",
  controls=[regset("Turn on", "String", 1, "On"), regset("Turn off", "String", 0, "Off"), uri("Open Multitasking", "ms-settings:multitasking")], tags=["snap layouts"])

S("multitasking.alt-tab", "Alt+Tab shows browser tabs", "Multitasking",
  "Whether Alt+Tab includes Microsoft Edge tabs.",
  reg(HKCU, ADV, "MultiTaskingAltTabFilter", {"3": "Open windows only", "0": "Windows and all tabs", "1": "Windows and 5 most recent tabs", "2": "Windows and 3 most recent tabs"}, "Default (depends on version)"),
  ["Open windows only", "Windows and 3/5/all tabs"], [("Most users", "Open windows only", "Classic, predictable Alt+Tab.")],
  "None.", "None.",
  controls=[regset("Open windows only", "DWord", 3, "Open windows only"), regset("Windows and 5 recent tabs", "DWord", 1, "Windows and 5 most recent tabs"), uri("Open Multitasking", "ms-settings:multitasking")],
  minBuild=19042, tags=["edge tabs"])

S("multitasking.shake", "Title bar window shake", "Multitasking",
  "Shaking a window minimises all others.",
  reg(HKCU, ADV, "DisallowShaking", {"1": "Off", "0": "On"}, "Default"),
  ["On", "Off"], [("Most users", "Off", "Prevents windows minimising by accident.")],
  "None.", "None.",
  controls=[regset("Turn off", "DWord", 1, "Off"), regset("Turn on", "DWord", 0, "On"), uri("Open Multitasking", "ms-settings:multitasking")], tags=["aero shake"])

S("multitasking.virtual-desktops", "Virtual desktops", "Multitasking",
  "Multiple desktops (Win+Ctrl+D) and how their windows appear in the taskbar/Alt+Tab.",
  none(), ["Per-desktop or all-desktops windows"], [("Power users", "Use desktops per task", "Less clutter.")],
  "None.", "Windows 'missing' from the taskbar may be on another desktop.",
  controls=[uri("Open Multitasking", "ms-settings:multitasking")], tags=["task view"])

# =========================================================================== Bluetooth
S("bluetooth.radio", "Bluetooth adapter", "Bluetooth",
  "Whether a Bluetooth radio is present and working.",
  probe("bluetooth.radio"), ["Present/On", "Present/Off", "Not present"],
  [("Desktops without built-in Bluetooth", "Use a Bluetooth 5.x USB adapter", "Cheap adapters with generic drivers cause dropouts.")],
  "None.", "Bluetooth audio on 2.4 GHz can be disrupted by Wi-Fi and USB 3 interference.",
  controls=[uri("Open Bluetooth & devices", "ms-settings:bluetooth"), msc("Open Device Manager", "devmgmt.msc")], tags=["wireless"])

S("bluetooth.support-service", "Bluetooth Support Service", "Bluetooth",
  "Service required for discovering and pairing Bluetooth devices.",
  svc("bthserv"), ["Manual (default, trigger start)", "Automatic", "Disabled"],
  [("All", "Manual (default)", "Starts when needed.")],
  "None.", "Disabled breaks pairing and connections.",
  controls=[msc("Open Services", "services.msc"), act("Restore to Manual (backed up)", "services.start-mode", {"service": "bthserv", "startMode": "Manual"})], tags=["service"])

S("bluetooth.swift-pair", "Swift Pair notifications", "Bluetooth",
  "Shows a notification to pair supported Bluetooth devices when they're nearby.",
  reg(HKCU, r"Software\Microsoft\Windows\CurrentVersion\Bluetooth", "QuickPair", ONOFF, "On (default)"),
  ["On", "Off"], [("Offices with many devices", "Off", "Avoids constant pairing prompts.")],
  "None.", "None.",
  controls=[regset("Turn off", "DWord", 0, "Off"), regset("Turn on", "DWord", 1, "On")], tags=["pairing"])

S("bluetooth.absolute-volume", "Bluetooth absolute volume", "Bluetooth",
  "Synchronises Windows volume with the headphones' volume. Disabling it fixes very quiet or uncontrollable volume on some headsets.",
  reg(HKLM, r"SYSTEM\ControlSet001\Control\Bluetooth\Audio\AVRCP\CT", "DisableAbsoluteVolume", {"1": "Disabled", "0": "Enabled"}, "Enabled (default)"),
  ["Enabled", "Disabled"], [("Headphones too quiet / volume jumps", "Disabled", "Separates Windows and headset volume."), ("Everyone else", "Enabled", "Single volume control.")],
  "None.", "Two volume controls (Windows and headset) instead of one.",
  admin=True, restart="restart",
  controls=[regset("Disable absolute volume", "DWord", 1, "Disabled"), regset("Enable (default)", "DWord", 0, "Enabled")], tags=["headphones", "quiet", "airpods"])

S("bluetooth.devices", "Paired devices", "Bluetooth",
  "Paired keyboards, mice, headphones, and controllers.",
  none(), ["Per device"], [("Device misbehaving", "Remove and re-pair it", "Clears stale pairing keys.")],
  "None.", "Removing a device requires pairing mode on the device to reconnect.",
  controls=[uri("Open Bluetooth & devices", "ms-settings:bluetooth"), uri("Open Devices", "ms-settings:connecteddevices")], tags=["pair"])

# =========================================================================== Network
S("network.status", "Network status and adapters", "Network",
  "Connected adapters, link speed, and IP configuration.",
  probe("network.adapters"), ["Per adapter"],
  [("Gaming/streaming PCs", "Wired Ethernet at 1 Gbps or faster", "Lowest latency and most stable.")],
  "Wi-Fi adds latency and jitter compared to Ethernet.", "None.",
  controls=[uri("Open Network status", "ms-settings:network-status"), applet("Open Network Connections", "ncpa.cpl")], tags=["ethernet", "ip", "adapter"])

S("network.dns", "DNS servers", "Network",
  "The servers that translate website names to addresses.",
  probe("network.dns"), ["Automatic (router/ISP)", "Manual"],
  [("Most homes", "Automatic", "Router or ISP DNS."), ("Unreliable ISP DNS", "A reputable public resolver, set on the router", "Fixes slow or failing lookups for the whole home.")],
  "Slow DNS delays the start of every page load.", "Manually set DNS can break captive portals and work VPNs.",
  controls=[applet("Open Network Connections", "ncpa.cpl"), act("Flush DNS cache", "network.flush-dns"), uri("Open Network status", "ms-settings:network-status")], tags=["dns", "resolver"])

S("network.proxy", "Proxy server", "Network",
  "Manual proxy for web traffic.",
  reg(HKCU, r"Software\Microsoft\Windows\CurrentVersion\Internet Settings", "ProxyEnable", ONOFF, "Off"),
  ["Off", "On (address:port)", "Automatic configuration script"],
  [("Home PCs", "Off", "Stale proxies cause 'no internet' in browsers."), ("Workplace PCs", "As required by IT", "Corporate networks may require a proxy.")],
  "Unnecessary proxies slow every connection.", "Turning off a required corporate proxy breaks browsing at work.",
  controls=[act("Change (backed up)", "network.disable-proxy", {"state": "Off"}), uri("Open Proxy settings", "ms-settings:network-proxy")], tags=["proxy", "no internet"])

S("network.wifi", "Wi-Fi connection", "Network",
  "Current Wi-Fi network, signal strength, and band.",
  probe("network.wifi"), ["Per network"],
  [("Gaming/video calls", "5 GHz or 6 GHz, strong signal", "Less interference than 2.4 GHz.")],
  "Weak signal causes lag spikes and disconnects.", "Windows 11 24H2+ requires Location permission for apps to read Wi-Fi details.",
  controls=[uri("Open Wi-Fi settings", "ms-settings:network-wifi"), uri("Show available networks", "ms-availablenetworks:")], tags=["wireless", "signal"])

S("network.metered", "Metered connection", "Network",
  "Marks a connection as metered so Windows limits background downloads.",
  none("Stored per network profile; open the connection's properties."), ["On", "Off"],
  [("Mobile hotspot/limited data", "On", "Prevents large update downloads."), ("Home broadband", "Off", "Metered blocks automatic updates and OneDrive sync.")],
  "None.", "Updates and cloud sync pause on metered connections.",
  controls=[uri("Open Ethernet settings", "ms-settings:network-ethernet"), uri("Open Wi-Fi settings", "ms-settings:network-wifi")], tags=["data limit"])

S("network.sharing", "Network discovery and file sharing", "Network",
  "Whether the PC can see/be seen by other devices on the local network.",
  none(), ["On/Off per network profile (Private/Public)"],
  [("Home network (Private)", "Discovery on if sharing is needed", "Lets PCs find printers and shares."), ("Public Wi-Fi", "Off", "Invisible to other devices.")],
  "None.", "Setting a public network to Private exposes shares to strangers.",
  controls=[cpl("Open Advanced sharing settings", "Microsoft.NetworkAndSharingCenter", "/page Advanced"), uri("Open Network status", "ms-settings:network-status")], tags=["smb", "share"])

S("network.reset", "Network reset", "Network",
  "Removes and reinstalls network adapters and resets networking components.",
  none(), ["Not a setting"],
  [("Persistent connectivity problems after other fixes", "Winsock/TCP-IP reset first, full Network reset as a last step", "Least disruptive first.")],
  "None.", "Removes VPN adapters and static IP settings; requires restart.",
  controls=[act("Reset Winsock and TCP/IP", "network.reset-stack"), uri("Open Advanced network settings", "ms-settings:network-advancedsettings")], tags=["winsock", "fix internet"])

E.extend(more_entries(globals()))

out = os.path.join(os.path.dirname(__file__), "..", "..", "src", "SoftReset.Core", "Knowledge", "Data", "settings.json")
ids = [e["id"] for e in E]
dupes = {i for i in ids if ids.count(i) > 1}
if dupes:
    raise SystemExit(f"Duplicate ids: {dupes}")
with open(out, "w", encoding="utf-8") as f:
    json.dump(E, f, indent=1, ensure_ascii=False)
    f.write("\n")
print(f"Wrote {len(E)} settings to {os.path.normpath(out)}")

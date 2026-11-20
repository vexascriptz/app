"""Second half of the settings catalog (see settings_catalog.py)."""


def more_entries(g):
    S, reg, probe, svc, task, none = g["S"], g["reg"], g["probe"], g["svc"], g["task"], g["none"]
    uri, tool, cpl, applet, msc, regset, regdel, act = g["uri"], g["tool"], g["cpl"], g["applet"], g["msc"], g["regset"], g["regdel"], g["act"]
    HKCU, HKLM, HKU, ADV, CDM, PERSONALIZE, WU_UX, CONSENT = g["HKCU"], g["HKLM"], g["HKU"], g["ADV"], g["CDM"], g["PERSONALIZE"], g["WU_UX"], g["CONSENT"]
    ONOFF = g["ONOFF"]
    PRO = ["Professional", "Enterprise", "Education", "ProfessionalWorkstation", "ServerRdsh"]

    def onoff_controls(on=1, off=0, kind="DWord"):
        return [regset("Turn on", kind, on, "On"), regset("Turn off", kind, off, "Off")]

    # ======================================================================= Personalization
    S("personalization.color-mode", "Colour mode (light/dark)", "Personalization",
      "Light or dark appearance for Windows and apps.",
      reg(HKCU, PERSONALIZE, "AppsUseLightTheme", {"1": "Light", "0": "Dark"}, "Light (default)"),
      ["Light", "Dark", "Custom"], [("Preference", "Customer's choice", "Purely visual."), ("Dim rooms/OLED", "Dark", "Less glare.")],
      "None.", "Some older apps ignore dark mode.",
      controls=[act("Change (backed up)", "desktop.dark-mode", {"state": "Dark"}), uri("Open Colours", "ms-settings:colors")], tags=["dark mode", "theme"])

    S("personalization.transparency", "Transparency effects", "Personalization",
      "Acrylic/mica translucency on the taskbar, Start, and windows.",
      reg(HKCU, PERSONALIZE, "EnableTransparency", ONOFF, "On (default)"),
      ["On", "Off"], [("Low-end/integrated GPU", "Off", "Slightly less GPU work."), ("Most PCs", "On", "No measurable cost.")],
      "Tiny GPU cost.", "None.",
      controls=[act("Change (backed up)", "desktop.transparency", {"state": "Off"}), uri("Open Colours", "ms-settings:colors")], tags=["acrylic", "mica"])

    S("personalization.accent-taskbar", "Accent colour on Start and taskbar", "Personalization",
      "Applies the accent colour to Start and the taskbar.",
      reg(HKCU, PERSONALIZE, "ColorPrevalence", ONOFF, "Off (default)"),
      ["On", "Off"], [("Preference", "Customer's choice", "Purely visual.")], "None.", "None.",
      controls=onoff_controls() + [uri("Open Colours", "ms-settings:colors")], tags=["color"])

    S("personalization.background", "Desktop background", "Personalization",
      "Picture, solid colour, slideshow, or Windows spotlight.",
      none(), ["Picture", "Solid colour", "Slideshow", "Windows spotlight"],
      [("Low-end PCs", "Picture or solid colour", "Slideshows use a little background activity.")],
      "Negligible.", "Windows spotlight shows Microsoft promotions on the desktop.",
      controls=[uri("Open Background", "ms-settings:personalization-background")], tags=["wallpaper"])

    S("personalization.lockscreen-tips", "Fun facts and tips on the lock screen", "Personalization",
      "Shows tips and promotions on the lock screen.",
      reg(HKCU, CDM, "SubscribedContent-338387Enabled", ONOFF, "On (default)"),
      ["On", "Off"], [("Most users", "Off", "Removes promotional text from the lock screen.")], "None.", "None.",
      controls=[regset("Turn off", "DWord", 0, "Off"), regset("Turn on", "DWord", 1, "On"), uri("Open Lock screen", "ms-settings:lockscreen")], tags=["spotlight", "ads"])

    S("personalization.start-suggestions-win10", "Start menu suggestions (Windows 10)", "Personalization",
      "'Show suggestions occasionally in Start' — app promotions in the Start menu.",
      reg(HKCU, CDM, "SubscribedContent-338388Enabled", ONOFF, "On (default)"),
      ["On", "Off"], [("Most users", "Off", "No app advertisements in Start.")], "None.", "None.",
      controls=[regset("Turn off", "DWord", 0, "Off"), regset("Turn on", "DWord", 1, "On"), uri("Open Start settings", "ms-settings:personalization-start")],
      maxBuild=19045, tags=["ads", "start"])

    S("personalization.start-recommendations", "Start recommendations for tips and new apps (Windows 11)", "Personalization",
      "Recommendations for tips, shortcuts, and new apps in the Start Recommended section.",
      reg(HKCU, ADV, "Start_IrisRecommendations", ONOFF, "On (default)"),
      ["On", "Off"], [("Most users", "Off", "Cleaner Start menu.")], "None.", "None.",
      controls=[regset("Turn off", "DWord", 0, "Off"), regset("Turn on", "DWord", 1, "On"), uri("Open Start settings", "ms-settings:personalization-start")],
      minBuild=22621, tags=["start", "recommended"])

    S("personalization.recent-items", "Show recently opened items", "Personalization",
      "Recent files in Start, Jump Lists, and File Explorer.",
      reg(HKCU, ADV, "Start_TrackDocs", ONOFF, "On (default)"),
      ["On", "Off"], [("Shared PCs", "Off", "Others can't see recently opened files."), ("Personal PCs", "On", "Quick access to recent work.")],
      "None.", "Off also empties Jump Lists.",
      controls=[regset("Turn off", "DWord", 0, "Off"), regset("Turn on", "DWord", 1, "On"), uri("Open Start settings", "ms-settings:personalization-start")], tags=["privacy", "jump lists"])

    S("personalization.themes", "Themes and fonts", "Personalization",
      "Theme packs, installed fonts, and font management.",
      none(), ["Not a setting"], [("Font problems", "Reinstall or remove the font from Fonts settings", "Corrupt fonts cause rendering issues.")],
      "None.", "Deleting system fonts can break apps.",
      controls=[uri("Open Themes", "ms-settings:themes"), uri("Open Fonts", "ms-settings:fonts")], tags=["font"])

    # ======================================================================= Applications
    S("apps.installed", "Installed apps", "Applications",
      "All installed desktop and Store apps. The console's Application Library shows details and guides for each.",
      none(), ["Not a setting"], [("Slow PCs", "Remove unused software via its own uninstaller", "Fewer background services and updaters.")],
      "Unused apps often add startup items and background services.", "Never remove runtimes (Visual C++, .NET, WebView2) that other apps depend on.",
      controls=[uri("Open Installed apps", "ms-settings:appsfeatures"), applet("Open Programs and Features", "appwiz.cpl")], tags=["uninstall", "programs"])

    S("apps.default-apps", "Default apps", "Applications",
      "Which app opens each file type and link (browser, PDF reader, mail).",
      none(), ["Per file type / protocol"], [("All", "The customer's preferred browser and PDF reader", "Avoids surprises after updates.")],
      "None.", "Windows updates occasionally reset defaults.",
      controls=[uri("Open Default apps", "ms-settings:defaultapps")], tags=["browser", "pdf", "file association"])

    S("apps.optional-features", "Optional features and Windows features", "Applications",
      "Features such as .NET 3.5, Hyper-V, WSL, and Windows Sandbox.",
      none(), ["Enabled/Disabled per feature"],
      [("Gaming PCs", "Only enable virtualisation features when needed", "Hyper-V and Memory Integrity can lower performance in some games."),
       ("Old apps needing .NET 3.5", "Enable .NET Framework 3.5", "Required by some legacy software.")],
      "Virtualisation features (Hyper-V, VBS) can cost a few percent in games.", "Disabling features other software depends on breaks it (e.g. WSL, Docker, Android apps).",
      controls=[uri("Open Optional features", "ms-settings:optionalfeatures"), tool("Open Windows Features", "optionalfeatures.exe")], tags=["hyper-v", "wsl", ".net 3.5"])

    S("apps.install-source", "Choose where to get apps", "Applications",
      "Restricts installing desktop apps to the Microsoft Store or warns before installing others.",
      reg(HKLM, r"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", "AicEnabled",
          {"Anywhere": "Anywhere", "PreferStore": "Anywhere, but let me know if there's a comparable app in the Store", "StoreOnly": "The Microsoft Store only", "Recommendations": "Anywhere, but warn me"}, "Anywhere (default)"),
      ["Anywhere", "Anywhere, but warn", "Store only"],
      [("Elderly or vulnerable users", "Anywhere, but warn me", "An extra prompt before installing downloaded programs."), ("Most users", "Anywhere", "No friction.")],
      "None.", "Store only blocks most normal software installers.",
      admin=True, controls=[uri("Open Advanced app settings", "ms-settings:appsfeatures")], tags=["install", "store only"])

    S("apps.background-apps-win10", "Let apps run in the background (Windows 10)", "Applications",
      "Global switch for Store apps running in the background on Windows 10.",
      reg(HKCU, r"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", {"1": "Off", "0": "On"}, "On (default)",
          policy=[reg(HKLM, r"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsRunInBackground", {"0": "User decides (policy)", "1": "Force allow (policy)", "2": "Force deny (policy)"})]),
      ["On", "Off"], [("Low-end Windows 10 PCs", "Off, then allow mail/calendar if needed", "Less background activity."), ("Most users", "On", "Notifications from Store apps keep working.")],
      "Small battery/CPU savings on low-end PCs.", "Store apps stop updating live tiles and notifications.",
      controls=[regset("Turn off", "DWord", 1, "Off"), regset("Turn on", "DWord", 0, "On"), uri("Open Background apps", "ms-settings:privacy-backgroundapps")],
      maxBuild=19045, tags=["background"])

    S("apps.apps-for-websites", "Apps for websites", "Applications",
      "Lets installed apps open specific website links instead of the browser.",
      none(), ["On/Off per app"], [("All", "On for apps the customer uses", "Links open in the right app.")], "None.", "None.",
      controls=[uri("Open Apps for websites", "ms-settings:appsforwebsites")], tags=["links"])

    # ======================================================================= Startup
    S("startup.apps", "Startup apps (this user)", "Startup",
      "Apps that start automatically when this user signs in.",
      none("Shown in detail in the startup action."), ["Enabled/Disabled per app"],
      [("All", "Only what the customer needs at sign-in (security, cloud sync, peripheral drivers)", "Faster sign-in, less background memory.")],
      "Each startup app delays the usable desktop and uses memory.", "Disabled apps (chat, sync, RGB) don't start automatically until re-enabled.",
      controls=[act("Review startup apps", "startup.user-items"), uri("Open Startup apps", "ms-settings:startupapps"), tool("Open Task Manager → Startup", "taskmgr.exe", "/0 /startup")],
      tags=["boot", "autostart", "run"])

    S("startup.machine-apps", "Startup apps (all users)", "Startup",
      "Apps registered to start for every user (HKLM Run keys and the all-users Startup folder).",
      none(), ["Enabled/Disabled per app"], [("All", "Keep security and hardware driver helpers; disable bundled promos", "Faster sign-in.")],
      "Same as per-user startup apps.", "Requires administrator approval.",
      admin=True, controls=[act("Review machine startup apps", "startup.machine-items")], tags=["boot", "hklm"])

    S("startup.logon-tasks", "Third-party sign-in tasks", "Startup",
      "Scheduled tasks from non-Microsoft software that run at sign-in or boot (not shown in Task Manager's Startup tab).",
      none(), ["Enabled/Disabled per task"], [("All", "Disable unnecessary updaters and launchers", "Fewer background processes.")],
      "Each task may launch a background process at sign-in.", "Disabling updaters stops automatic updates for that app.",
      admin=True, controls=[act("Review sign-in tasks", "startup.logon-tasks"), msc("Open Task Scheduler", "taskschd.msc")], tags=["scheduled task", "updater"])

    S("startup.restart-apps", "Restart apps after signing in", "Startup",
      "Automatically reopens apps that were open when Windows restarted.",
      reg(HKCU, r"Software\Microsoft\Windows NT\CurrentVersion\Winlogon", "RestartApps", ONOFF, "Off (default)"),
      ["On", "Off"], [("Customers confused by apps reopening by themselves", "Off", "Predictable sign-in."), ("Power users", "On", "Resume work quickly.")],
      "Reopening many apps slows the first minute after sign-in.", "None.",
      controls=[regset("Turn off", "DWord", 0, "Off"), regset("Turn on", "DWord", 1, "On"), uri("Open Sign-in options", "ms-settings:signinoptions")], tags=["reopen", "sign in"])

    # ======================================================================= Privacy
    S("privacy.advertising-id", "Advertising ID", "Privacy",
      "Lets apps show personalised ads using a per-user advertising identifier.",
      reg(HKCU, r"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", ONOFF, "On (default)",
          policy=[reg(HKLM, r"SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo", "DisabledByGroupPolicy", {"1": "Disabled by policy"})]),
      ["On", "Off"], [("Privacy-conscious customers", "Off", "Apps can't correlate activity for ads.")], "None.", "None.",
      controls=[regset("Turn off", "DWord", 0, "Off"), regset("Turn on", "DWord", 1, "On"), uri("Open General privacy", "ms-settings:privacy-general")], tags=["ads", "tracking"])

    S("privacy.tailored-experiences", "Tailored experiences", "Privacy",
      "Lets Microsoft use diagnostic data for personalised tips and offers.",
      reg(HKCU, r"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", ONOFF, "Default"),
      ["On", "Off"], [("Privacy-conscious customers", "Off", "Fewer personalised offers.")], "None.", "None.",
      controls=[regset("Turn off", "DWord", 0, "Off"), regset("Turn on", "DWord", 1, "On"), uri("Open Diagnostics & feedback", "ms-settings:privacy-feedback")], tags=["diagnostics"])

    S("privacy.diagnostic-data", "Diagnostic data level", "Privacy",
      "Required vs optional diagnostic data sent to Microsoft. Organisations often set this by policy.",
      reg(HKLM, r"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection", "AllowTelemetry", {"0": "Security (Enterprise only)", "1": "Required", "3": "Optional"}, "Default",
          policy=[reg(HKLM, r"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", {"0": "Security (policy)", "1": "Required (policy)", "2": "Enhanced (policy)", "3": "Optional (policy)"})]),
      ["Required", "Optional"], [("Privacy-conscious customers", "Required", "Minimum data; Windows Update still works.")],
      "None — disabling telemetry services does not measurably speed up Windows.", "Blocking telemetry with hosts files or by disabling services can break updates.",
      admin=True, controls=[uri("Open Diagnostics & feedback", "ms-settings:privacy-feedback")], tags=["telemetry"])

    S("privacy.feedback-frequency", "Feedback frequency", "Privacy",
      "How often Windows asks for feedback.",
      reg(HKCU, r"Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", {"0": "Never"}, "Automatically (default)"),
      ["Automatically", "Never"], [("Most users", "Never", "No feedback pop-ups.")], "None.", "None.",
      controls=[regset("Never ask", "DWord", 0, "Never"), regdel("Automatically (default)", "Automatically"), uri("Open Diagnostics & feedback", "ms-settings:privacy-feedback")], tags=["survey"])

    S("privacy.activity-history", "Activity history", "Privacy",
      "Stores a history of activities (apps, files, websites) on the device.",
      reg(HKLM, r"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", {"0": "Disabled by policy", "1": "Enabled by policy"}, "User-controlled"),
      ["On", "Off"], [("Shared PCs", "Off", "Others can't browse activity history.")], "None.", "None.",
      controls=[uri("Open Activity history", "ms-settings:privacy-activityhistory")], tags=["timeline"])

    S("privacy.location", "Location services", "Privacy",
      "Device-wide location access for apps. Required for Wi-Fi details in desktop apps on Windows 11 24H2+.",
      reg(HKLM, rf"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location", "Value", {"Allow": "On", "Deny": "Off"}, "Default"),
      ["On", "Off"], [("Laptops", "On, with per-app control", "Maps, weather, and Find my device need it."), ("Desktops", "Customer's choice", "Little benefit on desktops.")],
      "None.", "Off disables Find my device and location-aware time zone.",
      admin=True, controls=[uri("Open Location", "ms-settings:privacy-location")], tags=["gps", "find my device"])

    S("privacy.camera", "Camera access", "Privacy",
      "Whether apps can use the camera. A common cause of 'camera not found' in Zoom/Teams/Discord.",
      reg(HKCU, rf"{CONSENT}\webcam", "Value", {"Allow": "Allowed", "Deny": "Blocked"}, "Default (allowed)"),
      ["Allowed", "Blocked"], [("Video calls not working", "Allow camera access and desktop app access", "Desktop apps need 'Let desktop apps access your camera'.")],
      "None.", "Allowing access lets any desktop app use the camera.",
      controls=[uri("Open Camera privacy", "ms-settings:privacy-webcam")], tags=["webcam", "zoom", "teams"])

    S("privacy.microphone", "Microphone access", "Privacy",
      "Whether apps can use the microphone. A common cause of 'my mic doesn't work in Discord'.",
      reg(HKCU, rf"{CONSENT}\microphone", "Value", {"Allow": "Allowed", "Deny": "Blocked"}, "Default (allowed)"),
      ["Allowed", "Blocked"], [("Mic not working in an app", "Allow access and 'Let desktop apps access your microphone'", "Both switches must be on for desktop apps.")],
      "None.", "Allowing access lets any desktop app use the microphone.",
      controls=[uri("Open Microphone privacy", "ms-settings:privacy-microphone")], tags=["mic", "discord", "voice"])

    S("privacy.online-speech", "Online speech recognition", "Privacy",
      "Sends voice to Microsoft's cloud for dictation (Win+H) and voice access.",
      reg(HKCU, r"Software\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy", "HasAccepted", ONOFF, "Off"),
      ["On", "Off"], [("Dictation users", "On", "Much better accuracy."), ("Privacy-conscious", "Off", "Voice stays local.")], "None.", "Off degrades dictation.",
      controls=[uri("Open Speech privacy", "ms-settings:privacy-speech")], tags=["dictation", "voice"])

    S("privacy.search-history", "Device search history", "Privacy",
      "Uses local search history to improve search results.",
      reg(HKCU, r"Software\Microsoft\Windows\CurrentVersion\SearchSettings", "IsDeviceSearchHistoryEnabled", ONOFF, "On (default)"),
      ["On", "Off"], [("Shared PCs", "Off", "Previous searches aren't suggested to others.")], "None.", "None.",
      controls=[regset("Turn off", "DWord", 0, "Off"), regset("Turn on", "DWord", 1, "On"), uri("Open Search permissions", "ms-settings:search-permissions")], tags=["search"])

    S("privacy.app-permissions", "App permissions (contacts, files, notifications…)", "Privacy",
      "Per-capability app permissions.",
      none(), ["Per capability"], [("All", "Review permissions for unknown apps", "Limits data access.")], "None.", "Blocking a permission an app needs breaks that feature.",
      controls=[uri("Open Privacy & security", "ms-settings:privacy"), uri("Open App diagnostics permission", "ms-settings:privacy-appdiagnostics")], tags=["permissions"])

    # ======================================================================= Security (read-only by design)
    SEC_NOTE = "The console only reports security settings and opens Windows Security; it never weakens protection."
    S("security.defender-realtime", "Microsoft Defender real-time protection", "Security",
      "Scans files when they are opened or downloaded.",
      probe("security.defender-realtime"), ["On", "Off"], [("All (without another antivirus)", "On", "Primary malware protection.")],
      "Scanning adds a little overhead to file access; exclusions should only be used for trusted folders.", SEC_NOTE,
      controls=[uri("Open Virus & threat protection", "windowsdefender://threat")], tags=["antivirus", "defender"])

    S("security.defender-signatures", "Antivirus definitions", "Security",
      "Age of Defender's security intelligence updates.",
      probe("security.defender-signatures"), ["Updated within a day"], [("All", "Current (under 2 days)", "New threats are recognised.")],
      "None.", SEC_NOTE, controls=[uri("Open Virus & threat protection", "windowsdefender://threat")], tags=["definitions", "update"])

    S("security.tamper-protection", "Tamper Protection", "Security",
      "Prevents apps from changing Defender settings.",
      reg(HKLM, r"SOFTWARE\Microsoft\Windows Defender\Features", "TamperProtection", {"5": "On", "4": "Off", "1": "Off", "0": "Off"}, "Unknown"),
      ["On", "Off"], [("All", "On", "Stops malware from turning Defender off.")], "None.", SEC_NOTE,
      controls=[uri("Open Virus & threat protection settings", "windowsdefender://threatsettings")], tags=["defender"])

    S("security.firewall", "Windows Firewall", "Security",
      "Firewall state for Domain, Private, and Public networks.",
      probe("security.firewall"), ["On", "Off per profile"], [("All", "On for all profiles", "Blocks unsolicited inbound connections.")],
      "None.", SEC_NOTE, controls=[uri("Open Firewall & network protection", "windowsdefender://network"), msc("Open Advanced Firewall", "wf.msc")], tags=["firewall"])

    S("security.uac", "User Account Control", "Security",
      "Prompts before apps make system changes.",
      reg(HKLM, r"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "ConsentPromptBehaviorAdmin",
          {"0": "Never notify", "1": "Prompt for credentials on secure desktop", "2": "Always notify", "3": "Prompt for credentials", "4": "Prompt for consent", "5": "Notify only when apps try to make changes (default)"}, "Default"),
      ["Always notify", "Notify (default)", "Notify without dimming", "Never notify"], [("All", "Default or Always notify", "Prevents silent system changes.")],
      "None.", SEC_NOTE + " Never-notify lets malware elevate silently.",
      controls=[tool("Open UAC settings", "UserAccountControlSettings.exe")], tags=["uac", "admin prompt"])

    S("security.secure-boot", "Secure Boot", "Security",
      "Firmware feature that only allows trusted boot loaders. Required by Windows 11 and some anti-cheat systems (Valorant, Battlefield 6).",
      probe("security.secure-boot"), ["On", "Off", "Unsupported (legacy BIOS)"], [("All", "On", "Blocks bootkits; required by some games.")],
      "None.", "Changed in UEFI firmware setup, not in Windows. Switching an MBR install to UEFI requires MBR2GPT first.",
      controls=[tool("Open System Information", "msinfo32.exe")], tags=["uefi", "anti-cheat", "vanguard"])

    S("security.tpm", "TPM (Trusted Platform Module)", "Security",
      "Security chip used by BitLocker, Windows Hello, and Windows 11 requirements.",
      probe("security.tpm"), ["TPM 2.0 ready", "TPM 1.2", "Not present / disabled in firmware"], [("All", "TPM 2.0 enabled (fTPM/PTT in firmware)", "Required for Windows 11.")],
      "None.", "Clearing the TPM can lock BitLocker drives — back up recovery keys first.",
      controls=[msc("Open TPM Management", "tpm.msc"), uri("Open Device security", "windowsdefender://devicesecurity")], tags=["tpm", "windows 11"])

    S("security.memory-integrity", "Memory integrity (Core isolation)", "Security",
      "Hypervisor-protected code integrity: blocks malicious drivers.",
      probe("security.memory-integrity"), ["On", "Off"],
      [("Most users", "On", "Strong protection against kernel malware."), ("Competitive gamers on older CPUs", "Discuss trade-off", "Can cost a few percent FPS on some systems.")],
      "Small overhead on some older CPUs and games.", SEC_NOTE + " Incompatible old drivers prevent enabling it.",
      controls=[uri("Open Core isolation", "windowsdefender://coreisolation")], tags=["hvci", "vbs", "core isolation"])

    S("security.smartscreen", "SmartScreen for apps and files", "Security",
      "Warns before running unrecognised downloaded apps.",
      reg(HKLM, r"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", "SmartScreenEnabled", {"Warn": "Warn", "RequireAdmin": "Require admin approval", "Off": "Off"}, "Default (Warn)"),
      ["Warn", "Block", "Off"], [("All", "Warn", "Stops many fake installers.")], "None.", SEC_NOTE,
      controls=[uri("Open App & browser control", "windowsdefender://appbrowser")], tags=["smartscreen"])

    S("security.bitlocker", "Drive encryption (BitLocker / Device encryption)", "Security",
      "Encrypts the system drive so data is unreadable if the PC is stolen.",
      probe("security.bitlocker"), ["On", "Off", "Suspended"], [("Laptops", "On, recovery key saved to the Microsoft account", "Protects data if stolen.")],
      "Negligible on modern CPUs with AES acceleration.", "Without the recovery key, data is lost after firmware/TPM changes. Confirm the key location before BIOS updates.",
      controls=[uri("Open Device encryption", "ms-settings:deviceencryption"), cpl("Open BitLocker (Pro)", "Microsoft.BitLockerDriveEncryption")], tags=["encryption", "recovery key"])

    S("security.controlled-folder-access", "Controlled folder access (ransomware protection)", "Security",
      "Blocks unknown apps from changing files in protected folders.",
      reg(HKLM, r"SOFTWARE\Microsoft\Windows Defender\Windows Defender Exploit Guard\Controlled Folder Access", "EnableControlledFolderAccess", {"0": "Off", "1": "On", "2": "Audit mode"}, "Off (default)"),
      ["On", "Off", "Audit"], [("Customers storing irreplaceable files locally", "On, with OneDrive backup", "Defence against ransomware.")],
      "None.", "Legitimate apps (games saving to Documents) may be blocked until allowed.",
      controls=[uri("Open Virus & threat protection", "windowsdefender://threat")], tags=["ransomware"])

    S("security.lsa-protection", "Local Security Authority protection", "Security",
      "Runs LSA as a protected process to prevent credential theft.",
      reg(HKLM, r"SYSTEM\CurrentControlSet\Control\Lsa", "RunAsPPL", {"1": "On", "2": "On (without UEFI lock)", "0": "Off"}, "Off/Default"),
      ["On", "Off"], [("All", "On", "Blocks credential-dumping tools.")], "None.", SEC_NOTE,
      controls=[uri("Open Core isolation", "windowsdefender://coreisolation")], minBuild=22621, tags=["credential guard"])

    # ======================================================================= Windows Update
    S("update.status", "Windows Update status", "Windows Update",
      "When updates last installed successfully.",
      probe("update.last-install"), ["Date of last successful install"], [("All", "Updated within the last month", "Security fixes.")],
      "Pending updates can consume bandwidth and CPU in the background.", "None.",
      controls=[uri("Open Windows Update", "ms-settings:windowsupdate"), uri("Check for updates", "ms-settings:windowsupdate-action"), uri("Update history", "ms-settings:windowsupdate-history")], tags=["patch"])

    S("update.reboot-pending", "Restart required", "Windows Update",
      "Whether updates are waiting for a restart.",
      probe("update.reboot-pending"), ["Yes", "No"], [("All", "Restart promptly", "Completes installation.")], "Pending updates may slow shutdown/startup.", "None.",
      controls=[uri("Open Restart options", "ms-settings:windowsupdate-restartoptions")], tags=["restart"])

    S("update.active-hours", "Active hours", "Windows Update",
      "Hours when Windows won't restart automatically for updates.",
      reg(HKLM, WU_UX, "ActiveHoursStart", {"*": "Starts at {value}:00"}, "Automatic"),
      ["Automatic", "Manual range up to 18 hours"], [("Workers/gamers", "Cover the hours the PC is used", "Avoids surprise restarts.")], "None.", "None.",
      controls=[uri("Open Active hours", "ms-settings:windowsupdate-activehours")], tags=["restart"])

    S("update.pause", "Pause updates", "Windows Update",
      "Temporarily pauses update installation.",
      reg(HKLM, WU_UX, "PauseUpdatesExpiryTime", {"*": "Paused until {value}"}, "Not paused"),
      ["Not paused", "Paused 1–5 weeks"], [("All", "Not paused (except briefly around important deadlines)", "Paused updates leave security holes.")], "None.", "None.",
      controls=[uri("Open Windows Update", "ms-settings:windowsupdate")], tags=["defer"])

    S("update.other-products", "Updates for other Microsoft products", "Windows Update",
      "Updates Office, drivers, and other Microsoft software through Windows Update.",
      reg(HKLM, WU_UX, "AllowMUUpdateService", ONOFF, "Off"),
      ["On", "Off"], [("PCs with Microsoft Office", "On", "Keeps Office patched.")], "None.", "None.",
      controls=[uri("Open Advanced options", "ms-settings:windowsupdate-options")], tags=["office"])

    S("update.get-latest", "Get the latest updates as soon as they're available", "Windows Update",
      "Receives non-security feature updates earlier.",
      reg(HKLM, WU_UX, "IsContinuousInnovationOptedIn", ONOFF, "Off (default)"),
      ["On", "Off"], [("Most customers", "Off", "Wait for features to mature.")], "None.", "Early features can have bugs.",
      controls=[uri("Open Windows Update", "ms-settings:windowsupdate")], minBuild=22621, tags=["features"])

    S("update.metered", "Download updates over metered connections", "Windows Update",
      "Allows update downloads even on metered connections.",
      reg(HKLM, WU_UX, "AllowAutoWindowsUpdateDownloadOverMeteredNetwork", ONOFF, "Off (default)"),
      ["On", "Off"], [("Mobile hotspot users", "Off", "Protects data allowance.")], "None.", "None.",
      controls=[uri("Open Advanced options", "ms-settings:windowsupdate-options")], tags=["data"])

    S("update.delivery-optimization", "Delivery Optimization", "Windows Update",
      "Shares update downloads with other PCs on the local network (and optionally the internet).",
      reg(HKLM, r"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", {"0": "HTTP only (policy)", "1": "LAN (policy)", "2": "Group (policy)", "3": "Internet (policy)", "99": "Simple (policy)", "100": "Bypass (policy)"}, "User-controlled (Settings)"),
      ["Off", "PCs on my local network", "PCs on my local network and the internet"], [("Homes with slow internet and several PCs", "Local network", "Downloads updates once."), ("Metered/limited upload", "Off", "No uploads to other PCs.")],
      "Can use upload bandwidth.", "None.",
      controls=[uri("Open Delivery Optimization", "ms-settings:delivery-optimization")], tags=["bandwidth"])

    S("update.optional-updates", "Optional updates (drivers)", "Windows Update",
      "Optional driver and feature updates offered by Windows Update.",
      none(), ["Per update"], [("Device with missing drivers", "Check optional driver updates", "Often contains the missing driver.")],
      "None.", "Optional GPU drivers from Windows Update are often older than the vendor's.",
      controls=[uri("Open Optional updates", "ms-settings:windowsupdate-optionalupdates")], tags=["drivers"])

    S("update.service", "Windows Update service", "Windows Update",
      "The service that downloads and installs updates. Tweak tools sometimes disable it.",
      svc("wuauserv"), ["Manual (default, trigger start)", "Disabled"], [("All", "Manual", "Windows default; starts when needed.")],
      "None.", "Disabled stops all security updates.",
      admin=True, controls=[act("Restore to Manual (backed up)", "services.start-mode", {"service": "wuauserv", "startMode": "Manual"}), msc("Open Services", "services.msc")], tags=["wuauserv"])

    # ======================================================================= Accessibility
    S("accessibility.text-size", "Text size", "Accessibility",
      "Enlarges text everywhere without changing overall scaling.",
      reg(HKCU, r"Software\Microsoft\Accessibility", "TextScaleFactor", {"*": "{value}%"}, "100%"),
      ["100%–225%"], [("Low vision / elderly customers", "125%–150%", "Easier reading.")], "None.", "Very large text can truncate labels in older apps.",
      controls=[uri("Open Text size", "ms-settings:easeofaccess-display")], tags=["font size", "elderly"])

    S("accessibility.animations", "Animation effects", "Accessibility",
      "Window and control animations.",
      probe("visual.animations"), ["On", "Off"], [("Motion-sensitive users", "Off", "Less motion."), ("Low-end PCs", "Off", "Feels snappier.")],
      "Off makes the interface feel faster on slow PCs.", "None.",
      controls=[act("Change visual effects (backed up)", "display.visual-effects"), uri("Open Visual effects", "ms-settings:easeofaccess-display")], tags=["motion"])

    S("accessibility.sticky-keys-shortcut", "Sticky Keys shortcut (press Shift 5 times)", "Accessibility",
      "The Shift×5 shortcut that turns on Sticky Keys — a classic annoyance in games.",
      reg(HKCU, r"Control Panel\Accessibility\StickyKeys", "Flags", {"510": "Shortcut on (default)", "506": "Shortcut off", "511": "Sticky Keys on"}, "Default"),
      ["Shortcut on", "Shortcut off"], [("Gamers", "Shortcut off", "No pop-up when pressing Shift repeatedly."), ("Users who rely on Sticky Keys", "Leave on", "Accessibility need.")],
      "None.", "Users who need Sticky Keys must enable it in Settings.", restart="signout",
      controls=[regset("Turn shortcut off", "String", 506, "Shortcut off"), regset("Restore default", "String", 510, "Shortcut on (default)"), uri("Open Keyboard accessibility", "ms-settings:easeofaccess-keyboard")], tags=["shift", "gaming"])

    S("accessibility.filter-keys-shortcut", "Filter Keys shortcut (hold right Shift 8 s)", "Accessibility",
      "Holding right Shift turns on Filter Keys, which ignores brief key presses — often mistaken for a broken keyboard.",
      reg(HKCU, r"Control Panel\Accessibility\Keyboard Response", "Flags", {"126": "Shortcut on (default)", "122": "Shortcut off", "127": "Filter Keys on"}, "Default"),
      ["Shortcut on", "Shortcut off"], [("Gamers", "Shortcut off", "Prevents accidental activation."), ("Keyboard 'not working'", "Check Filter Keys is off", "Common cause.")],
      "None.", "None.", restart="signout",
      controls=[regset("Turn shortcut off", "String", 122, "Shortcut off"), regset("Restore default", "String", 126, "Shortcut on (default)"), uri("Open Keyboard accessibility", "ms-settings:easeofaccess-keyboard")], tags=["keyboard", "gaming"])

    S("accessibility.toggle-keys-shortcut", "Toggle Keys shortcut (hold Num Lock 5 s)", "Accessibility",
      "Plays a tone when Caps/Num/Scroll Lock is pressed.",
      reg(HKCU, r"Control Panel\Accessibility\ToggleKeys", "Flags", {"62": "Shortcut on (default)", "58": "Shortcut off", "63": "Toggle Keys on"}, "Default"),
      ["Shortcut on", "Shortcut off"], [("Most users", "Default", "Rarely triggered.")], "None.", "None.", restart="signout",
      controls=[regset("Turn shortcut off", "String", 58, "Shortcut off"), regset("Restore default", "String", 62, "Shortcut on (default)")], tags=["keyboard"])

    S("accessibility.cursor-size", "Mouse pointer size", "Accessibility",
      "Size of the mouse pointer.",
      reg(HKCU, r"Software\Microsoft\Accessibility", "CursorSize", {"*": "Size {value}"}, "Size 1 (default)"),
      ["1–15"], [("Low vision / large screens", "3–5", "Easier to find the pointer.")], "None.", "None.",
      controls=[uri("Open Mouse pointer", "ms-settings:easeofaccess-mousepointer")], tags=["cursor"])

    S("accessibility.color-filters", "Colour filters", "Accessibility",
      "Greyscale, inverted, and colour-blindness filters (Win+Ctrl+C).",
      reg(HKCU, r"Software\Microsoft\ColorFiltering", "Active", ONOFF, "Off"),
      ["On", "Off"], [("Screen suddenly grey", "Turn off (shortcut Win+Ctrl+C)", "Common accidental activation.")], "None.", "None.",
      controls=[uri("Open Colour filters", "ms-settings:easeofaccess-colorfilter")], tags=["greyscale", "black and white"])

    S("accessibility.mono-audio", "Mono audio", "Accessibility",
      "Combines left and right channels into one.",
      reg(HKCU, r"Software\Microsoft\Multimedia\Audio", "AccessibilityMonoMixState", ONOFF, "Off"),
      ["On", "Off"], [("Hearing in one ear", "On", "Hear both channels."), ("Missing stereo sound", "Off", "Check it isn't on accidentally.")], "None.", "None.",
      controls=[uri("Open Audio accessibility", "ms-settings:easeofaccess-audio")], tags=["audio"])

    S("accessibility.scrollbars", "Always show scrollbars", "Accessibility",
      "Shows scrollbars permanently instead of hiding them.",
      reg(HKCU, r"Control Panel\Accessibility", "DynamicScrollbars", {"0": "Always show", "1": "Auto-hide (default)"}, "Auto-hide (default)"),
      ["Always show", "Auto-hide"], [("Elderly / touchpad users", "Always show", "Easier scrolling.")], "None.", "None.",
      controls=[regset("Always show", "DWord", 0, "Always show"), regset("Auto-hide (default)", "DWord", 1, "Auto-hide (default)"), uri("Open Visual effects", "ms-settings:easeofaccess-display")], tags=["scrollbar"])

    S("accessibility.tools", "Narrator, Magnifier, and High contrast", "Accessibility",
      "Screen reader, magnifier, and contrast themes.",
      none(), ["On/Off"], [("Accidental activation", "Ctrl+Win+Enter toggles Narrator; Win+Esc closes Magnifier", "Common support calls.")], "None.", "None.",
      controls=[uri("Open Narrator", "ms-settings:easeofaccess-narrator"), uri("Open Magnifier", "ms-settings:easeofaccess-magnifier"), uri("Open Contrast themes", "ms-settings:easeofaccess-highcontrast")], tags=["screen reader"])

    # ======================================================================= Gaming
    S("gaming.game-mode", "Game Mode", "Gaming",
      "Prioritises games and suppresses Windows Update driver installs/restart notifications during play.",
      reg(HKCU, r"Software\Microsoft\GameBar", "AutoGameModeEnabled", ONOFF, "On (default)"),
      ["On", "Off"], [("Gaming", "On", "Fewer interruptions.")], "Small or no FPS change; mainly prevents interruptions.", "None.",
      controls=[act("Change (backed up)", "gaming.game-mode", {"state": "On"}), uri("Open Game Mode", "ms-settings:gaming-gamemode")], tags=["fps", "gaming"])

    S("gaming.background-recording", "Background recording (Record what happened)", "Gaming",
      "Continuously records gameplay for instant clips.",
      reg(HKCU, r"Software\Microsoft\Windows\CurrentVersion\GameDVR", "HistoricalCaptureEnabled", ONOFF, "Off (default)"),
      ["On", "Off"], [("Low/mid-range PCs", "Off", "Frees encoder, memory, and disk writes."), ("Clip makers", "On", "Instant replays.")],
      "Measurable cost on low-end systems; minimal on modern GPUs.", "None.",
      controls=[act("Change (backed up)", "gaming.background-recording", {"state": "Off"}), uri("Open Captures", "ms-settings:gaming-gamedvr")], tags=["game dvr", "recording"])

    S("gaming.captures", "Game Bar captures", "Gaming",
      "Allows Game Bar to record clips and screenshots.",
      reg(HKCU, r"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", ONOFF, "On (default)"),
      ["On", "Off"], [("Using another recorder (OBS, ShadowPlay)", "Off", "Avoids duplicate capture tools.")], "None when not recording.", "None.",
      controls=[uri("Open Captures", "ms-settings:gaming-gamedvr")], tags=["xbox game bar"])

    S("gaming.game-bar-controller", "Open Game Bar with controller Xbox button", "Gaming",
      "Pressing the Xbox button on a controller opens Game Bar.",
      reg(HKCU, r"Software\Microsoft\GameBar", "UseNexusForGameBarEnabled", ONOFF, "On (default)"),
      ["On", "Off"], [("Steam Big Picture users", "Off", "Xbox button opens Steam instead.")], "None.", "None.",
      controls=[regset("Turn off", "DWord", 0, "Off"), regset("Turn on", "DWord", 1, "On"), uri("Open Game Bar settings", "ms-settings:gaming-gamebar")], tags=["controller"])

    S("gaming.mouse-acceleration", "Enhance pointer precision (mouse acceleration)", "Gaming",
      "Pointer moves further with faster mouse movements.",
      probe("input.mouse-acceleration"), ["On (default)", "Off"], [("FPS gamers", "Off", "Consistent aim."), ("Office users", "On", "Comfortable with low-DPI mice.")],
      "None.", "None.",
      controls=[act("Change (backed up)", "input.mouse-acceleration", {"state": "Off"}), applet("Open Mouse properties", "main.cpl")], tags=["mouse", "aim", "fps"])

    S("gaming.controllers", "Game controllers", "Gaming",
      "Connected controllers and calibration.",
      none(), ["Per controller"], [("Controller not detected", "Check joy.cpl and Bluetooth/USB connection", "Verifies Windows sees it.")], "None.", "None.",
      controls=[applet("Open Game Controllers", "joy.cpl")], tags=["gamepad", "xbox controller"])

    S("gaming.xbox-services", "Xbox Live services", "Gaming",
      "Services used by Xbox app/Game Pass sign-in and multiplayer.",
      svc("XblAuthManager"), ["Manual (default)"], [("Game Pass/Xbox app users", "Manual (default)", "Required for sign-in.")],
      "None (they start only when needed).", "Disabling breaks Xbox app and Game Pass games.",
      controls=[act("Restore to Manual (backed up)", "services.start-mode", {"service": "XblAuthManager", "startMode": "Manual"}), msc("Open Services", "services.msc")], tags=["game pass"])

    # ======================================================================= Accounts
    S("accounts.your-info", "Your account", "Accounts",
      "Signed-in account and whether this session is elevated.",
      probe("accounts.current"), ["Microsoft account", "Local account", "Work/school"], [("Home users", "Microsoft account", "Enables OneDrive backup and BitLocker key backup.")],
      "None.", "Switching account type changes where settings/files sync.",
      controls=[uri("Open Your info", "ms-settings:yourinfo")], tags=["microsoft account", "local account"])

    S("accounts.sign-in-options", "Sign-in options (PIN, Windows Hello, password)", "Accounts",
      "Ways to sign in. The console never collects or records passwords.",
      none(), ["PIN", "Face", "Fingerprint", "Password", "Security key"], [("All", "Windows Hello PIN", "Faster and safer than passwords.")], "None.", "None.",
      controls=[uri("Open Sign-in options", "ms-settings:signinoptions")], tags=["pin", "hello", "password"])

    S("accounts.other-users", "Other users and family", "Accounts",
      "Additional local and family accounts.",
      none(), ["Per account"], [("Shared PCs", "Separate standard accounts for each person", "Isolation and safety.")], "None.", "Deleting an account deletes its files.",
      controls=[uri("Open Other users", "ms-settings:otherusers"), tool("Open User Accounts (netplwiz)", "netplwiz.exe"), msc("Open Local Users and Groups", "lusrmgr.msc")], tags=["family", "users"])

    S("accounts.email-accounts", "Email and accounts used by apps", "Accounts",
      "Accounts used by Mail, Calendar, and other apps.",
      none(), ["Per account"], [("All", "Remove accounts the customer no longer uses", "Fewer sign-in prompts.")], "None.", "None.",
      controls=[uri("Open Email & accounts", "ms-settings:emailandaccounts")], tags=["outlook"])

    S("accounts.work-school", "Access work or school", "Accounts",
      "Work/school accounts and device management (MDM) enrolment.",
      none(), ["Per account"], [("Personal PCs", "Only intentionally connected accounts", "MDM can control settings.")], "None.", "Disconnecting can remove work data and access.",
      controls=[uri("Open Access work or school", "ms-settings:workplace")], tags=["mdm", "intune"])

    S("accounts.backup", "Windows Backup and sync", "Accounts",
      "Backs up folders, settings, and apps to the Microsoft account.",
      none(), ["On/Off per item"], [("All", "Back up Desktop/Documents/Pictures to OneDrive", "Protects against disk failure.")], "Initial sync uses bandwidth.", "OneDrive storage limits apply.",
      controls=[uri("Open Windows Backup", "ms-settings:backup"), uri("Open Sync settings (Windows 10)", "ms-settings:sync")], tags=["onedrive", "backup"])

    S("accounts.credential-manager", "Credential Manager", "Accounts",
      "Stored Windows and web credentials. The console does not read stored credentials.",
      none(), ["Not a setting"], [("Repeated sign-in prompts", "Remove the stale credential", "Clears outdated passwords for shares/Office.")], "None.", "Removing credentials requires re-entering them.",
      controls=[cpl("Open Credential Manager", "Microsoft.CredentialManager")], tags=["password"])

    # ======================================================================= Devices
    S("devices.device-manager", "Device Manager", "Devices",
      "Hardware devices and their drivers. Problem devices are reported by the Diagnostics scan.",
      none(), ["Not a setting"], [("All", "No devices with warning icons", "Each warning is a non-working device.")], "None.", "Uninstalling the wrong device driver can remove input/display until restart.",
      controls=[msc("Open Device Manager", "devmgmt.msc")], tags=["drivers", "hardware"])

    S("devices.mouse-speed", "Mouse pointer speed", "Devices",
      "Pointer sensitivity (1–20, default 10).",
      reg(HKCU, r"Control Panel\Mouse", "MouseSensitivity", {"*": "{value} of 20"}, "10 (default)"),
      ["1–20"], [("Gamers", "10 (1:1), adjust DPI on the mouse instead", "Avoids skipped pixels.")], "None.", "None.",
      controls=[uri("Open Mouse settings", "ms-settings:mousetouchpad"), applet("Open Mouse properties", "main.cpl")], tags=["sensitivity", "dpi"])

    S("devices.touchpad", "Touchpad", "Devices",
      "Gestures, sensitivity, and tap-to-click.",
      none(), ["Per setting"], [("Laptop users", "Precision touchpad drivers from the manufacturer", "Best gesture support.")], "None.", "None.",
      controls=[uri("Open Touchpad", "ms-settings:devices-touchpad")], tags=["laptop", "gestures"])

    S("devices.autoplay", "AutoPlay", "Devices",
      "What happens when USB drives, phones, or discs are connected.",
      reg(HKCU, r"Software\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers", "DisableAutoplay", {"0": "On", "1": "Off"}, "On (default)"),
      ["On", "Off"], [("Security-conscious", "Off or 'Ask me every time'", "Prevents automatic actions on inserted media.")], "None.", "None.",
      controls=[regset("Turn off", "DWord", 1, "Off"), regset("Turn on", "DWord", 0, "On"), uri("Open AutoPlay", "ms-settings:autoplay")], tags=["usb"])

    S("devices.usb-notifications", "USB connection error notifications", "Devices",
      "Notifies about USB connection issues (e.g. insufficient power).",
      reg(HKCU, r"Software\Microsoft\Shell\USB", "NotifyOnUsbErrors", ONOFF, "On (default)"),
      ["On", "Off"], [("All", "On", "Surfaces failing hubs and cables.")], "None.", "None.",
      controls=[uri("Open USB settings", "ms-settings:usb")], tags=["usb"])

    S("devices.driver-search", "Automatic driver downloads from Windows Update", "Devices",
      "Whether Windows Update automatically installs drivers and device apps.",
      reg(HKLM, r"SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching", "SearchOrderConfig", {"1": "Yes (default)", "0": "No"}, "Yes (default)"),
      ["Yes", "No"], [("Most users", "Yes", "Missing drivers install automatically."), ("Windows Update keeps replacing a working GPU driver", "No (temporarily)", "Stops downgrades; re-enable later.")],
      "None.", "With 'No', new devices may not get drivers automatically.",
      admin=True, controls=[regset("Allow (default)", "DWord", 1, "Yes (default)"), regset("Don't download drivers", "DWord", 0, "No"), tool("Open Device Installation Settings", "SystemPropertiesHardware.exe")], tags=["driver", "windows update"])

    S("devices.typing", "Typing and keyboard", "Devices",
      "Autocorrect, text suggestions, and keyboard layouts.",
      none(), ["Per setting"], [("Wrong characters typed", "Check the keyboard layout/language", "Common cause of '@' and '\"' swapped.")], "None.", "None.",
      controls=[uri("Open Typing", "ms-settings:typing"), uri("Open Language & region", "ms-settings:regionlanguage")], tags=["keyboard layout"])

    # ======================================================================= Printers
    S("printers.list", "Printers and scanners", "Printers",
      "Installed printers and the default printer.",
      probe("printers.list"), ["Installed printers"], [("All", "Remove old printers no longer used", "Cleaner print dialogs.")], "None.", "Removing a printer removes its queue.",
      controls=[uri("Open Printers & scanners", "ms-settings:printers"), cpl("Open Devices and Printers", "Microsoft.DevicesAndPrinters")], tags=["print", "scanner"])

    S("printers.default-management", "Let Windows manage my default printer", "Printers",
      "Windows sets the default to the last printer used at each location.",
      reg(HKCU, r"Software\Microsoft\Windows NT\CurrentVersion\Windows", "LegacyDefaultPrinterMode", {"0": "On (Windows manages)", "1": "Off (fixed default)"}, "On (default)"),
      ["On", "Off"], [("Customers whose default printer keeps changing", "Off", "The chosen default stays fixed.")], "None.", "None.",
      controls=[regset("Turn off (fixed default)", "DWord", 1, "Off (fixed default)"), regset("Turn on", "DWord", 0, "On (Windows manages)"), uri("Open Printers & scanners", "ms-settings:printers")], tags=["default printer"])

    S("printers.spooler", "Print Spooler service", "Printers",
      "Service that manages print jobs.",
      svc("Spooler"), ["Automatic (default)", "Disabled"], [("Printing problems", "Restart the service", "Clears stuck queues.")], "None.", "Disabled prevents all printing.",
      controls=[act("Restore to Automatic (backed up)", "services.start-mode", {"service": "Spooler", "startMode": "Auto"}), msc("Open Services", "services.msc")], tags=["stuck print job"])

    S("printers.print-management", "Print Management console", "Printers",
      "Advanced printer, driver, and queue management (Pro editions).",
      none(), ["Not a setting"], [("Driver conflicts", "Remove old printer drivers here", "Fixes many printing errors.")], "None.", "Removing driver packages affects all printers using them.",
      editions=["Professional", "Enterprise", "Education", "ProfessionalWorkstation"], controls=[msc("Open Print Management", "printmanagement.msc")], tags=["drivers"])

    # ======================================================================= Taskbar
    S("taskbar.alignment", "Taskbar alignment", "Taskbar",
      "Centre (default) or left-aligned Start button and icons on Windows 11.",
      reg(HKCU, ADV, "TaskbarAl", {"1": "Centre", "0": "Left"}, "Centre (default)"),
      ["Centre", "Left"], [("Users coming from Windows 10", "Left", "Familiar layout.")], "None.", "None.",
      controls=[act("Change (backed up)", "desktop.taskbar-alignment", {"state": "Left"}), uri("Open Taskbar settings", "ms-settings:taskbar")], minBuild=22000, tags=["start button"])

    S("taskbar.search", "Taskbar search", "Taskbar",
      "Search box, icon, or hidden.",
      reg(HKCU, r"Software\Microsoft\Windows\CurrentVersion\Search", "SearchboxTaskbarMode", {"0": "Hidden", "1": "Search icon", "2": "Search box", "3": "Search icon and label"}, "Default"),
      ["Hidden", "Icon", "Box", "Icon and label"], [("Elderly users", "Search box", "Discoverable."), ("Small screens", "Icon", "Saves space.")], "None.", "None.",
      controls=[regset("Search icon", "DWord", 1, "Search icon"), regset("Search box", "DWord", 2, "Search box"), regset("Hidden", "DWord", 0, "Hidden"), uri("Open Taskbar settings", "ms-settings:taskbar")], tags=["search box"])

    S("taskbar.task-view", "Task view button", "Taskbar",
      "Shows the Task view (virtual desktops) button.",
      reg(HKCU, ADV, "ShowTaskViewButton", ONOFF, "On (default)"),
      ["On", "Off"], [("Most users", "Customer's choice", "Win+Tab still works.")], "None.", "None.",
      controls=[regset("Show", "DWord", 1, "On"), regset("Hide", "DWord", 0, "Off"), uri("Open Taskbar settings", "ms-settings:taskbar")], tags=["virtual desktops"])

    S("taskbar.widgets", "Widgets button", "Taskbar",
      "News and weather widgets. Recent Windows builds protect this value from programmatic changes, so it can only be changed in Settings.",
      reg(HKCU, ADV, "TaskbarDa", ONOFF, "On (default)"),
      ["On", "Off"], [("Distraction-free", "Off", "Removes news feed.")], "Widgets keep a background process running when enabled.", "None.",
      controls=[uri("Open Taskbar settings", "ms-settings:taskbar")], minBuild=22000, tags=["news", "weather"])

    S("taskbar.combine", "Combine taskbar buttons", "Taskbar",
      "Whether windows of the same app are grouped into one button.",
      reg(HKCU, ADV, "TaskbarGlomLevel", {"0": "Always (default)", "1": "When taskbar is full", "2": "Never"}, "Always (default)"),
      ["Always", "When full", "Never"], [("Users who prefer labels", "When taskbar is full", "Shows window titles.")], "None.", "None.",
      controls=[regset("Always", "DWord", 0, "Always"), regset("When full", "DWord", 1, "When taskbar is full"), regset("Never", "DWord", 2, "Never"), uri("Open Taskbar settings", "ms-settings:taskbar")], tags=["labels", "group"])

    S("taskbar.end-task", "End task in taskbar right-click menu", "Taskbar",
      "Adds 'End task' to taskbar app menus (Settings → System → For developers).",
      reg(HKCU, ADV + r"\TaskbarDeveloperSettings", "TaskbarEndTask", ONOFF, "Off (default)"),
      ["On", "Off"], [("Technicians/power users", "On", "Close frozen apps without Task Manager.")], "None.", "Unsaved work is lost when ending a task.",
      controls=[regset("Turn on", "DWord", 1, "On"), regset("Turn off", "DWord", 0, "Off"), uri("Open For developers", "ms-settings:developers")], minBuild=22631, tags=["frozen app"])

    S("taskbar.small-icons-win10", "Small taskbar buttons (Windows 10)", "Taskbar",
      "Smaller taskbar icons.",
      reg(HKCU, ADV, "TaskbarSmallIcons", ONOFF, "Off (default)"),
      ["On", "Off"], [("Small screens", "On", "More room.")], "None.", "None.",
      controls=[regset("Turn on", "DWord", 1, "On"), regset("Turn off", "DWord", 0, "Off")], maxBuild=19045, tags=["icons"])

    S("taskbar.badges", "Taskbar badges", "Taskbar",
      "Unread counters on taskbar icons.",
      reg(HKCU, ADV, "TaskbarBadges", ONOFF, "On (default)"),
      ["On", "Off"], [("Most users", "On", "Visible unread counts.")], "None.", "None.",
      controls=[regset("Turn on", "DWord", 1, "On"), regset("Turn off", "DWord", 0, "Off")], tags=["notifications"])

    S("taskbar.auto-hide", "Automatically hide the taskbar", "Taskbar",
      "Hides the taskbar until the pointer touches the edge. Stored in an undocumented binary value.",
      none("Stored in an undocumented binary value (StuckRects3)."), ["On", "Off"],
      [("Small laptop screens", "Customer's choice", "More vertical space.")], "None.", "Fullscreen apps sometimes keep the taskbar visible regardless.",
      controls=[uri("Open Taskbar settings", "ms-settings:taskbar")], tags=["hide"])

    # ======================================================================= File Explorer
    S("explorer.extensions", "File name extensions", "File Explorer",
      "Shows .exe/.pdf/.docx extensions.",
      reg(HKCU, ADV, "HideFileExt", {"0": "Shown", "1": "Hidden"}, "Hidden (default)"),
      ["Shown", "Hidden"], [("All", "Shown", "Exposes fake 'invoice.pdf.exe' attachments.")], "None.", "Renaming can accidentally change extensions.",
      controls=[act("Change (backed up)", "desktop.file-extensions", {"state": "Show"}), cpl("Open Folder Options", "Microsoft.FolderOptions")], tags=["extension", "security"])

    S("explorer.hidden-files", "Hidden files and folders", "File Explorer",
      "Shows items marked hidden, such as AppData.",
      reg(HKCU, ADV, "Hidden", {"1": "Shown", "2": "Hidden"}, "Hidden (default)"),
      ["Shown", "Hidden"], [("Troubleshooting", "Shown temporarily", "Access app data folders."), ("Customers", "Hidden", "Avoids accidental changes.")], "None.", "None.",
      controls=[act("Change (backed up)", "desktop.hidden-files", {"state": "Show"}), cpl("Open Folder Options", "Microsoft.FolderOptions")], tags=["appdata"])

    S("explorer.protected-os-files", "Protected operating system files", "File Explorer",
      "Shows files Windows marks as protected system files.",
      reg(HKCU, ADV, "ShowSuperHidden", {"1": "Shown", "0": "Hidden"}, "Hidden (default)"),
      ["Shown", "Hidden"], [("All", "Hidden", "Prevents accidental deletion of system files.")], "None.", "Showing them risks accidental deletion of desktop.ini and boot files.",
      controls=[regset("Hide (recommended)", "DWord", 0, "Hidden"), regset("Show", "DWord", 1, "Shown")], tags=["system files"])

    S("explorer.launch-to", "Open File Explorer to", "File Explorer",
      "Home/Quick access or This PC.",
      reg(HKCU, ADV, "LaunchTo", {"1": "This PC", "2": "Home / Quick access", "3": "Downloads"}, "Home (default)"),
      ["This PC", "Home", "Downloads"], [("Technicians/users who manage drives", "This PC", "Shows drives and free space.")], "None.", "None.",
      controls=[act("Change (backed up)", "desktop.explorer-this-pc", {"state": "This PC"}), cpl("Open Folder Options", "Microsoft.FolderOptions")], tags=["home", "quick access"])

    S("explorer.recent-files", "Show recently used files in Quick access/Home", "File Explorer",
      "Recent files list in File Explorer.",
      reg(HKCU, r"Software\Microsoft\Windows\CurrentVersion\Explorer", "ShowRecent", ONOFF, "On (default)"),
      ["On", "Off"], [("Shared PCs", "Off", "Privacy.")], "None.", "None.",
      controls=[regset("Turn off", "DWord", 0, "Off"), regset("Turn on", "DWord", 1, "On")], tags=["privacy"])

    S("explorer.frequent-folders", "Show frequently used folders", "File Explorer",
      "Frequent folders in Quick access/Home.",
      reg(HKCU, r"Software\Microsoft\Windows\CurrentVersion\Explorer", "ShowFrequent", ONOFF, "On (default)"),
      ["On", "Off"], [("Shared PCs", "Off", "Privacy.")], "None.", "None.",
      controls=[regset("Turn off", "DWord", 0, "Off"), regset("Turn on", "DWord", 1, "On")], tags=["quick access"])

    S("explorer.full-path", "Display the full path in the title bar", "File Explorer",
      "Shows the complete folder path in the window title.",
      reg(HKCU, r"Software\Microsoft\Windows\CurrentVersion\Explorer\CabinetState", "FullPath", ONOFF, "Off (default)"),
      ["On", "Off"], [("Technicians", "On", "Know exactly where you are.")], "None.", "None.",
      controls=[regset("Turn on", "DWord", 1, "On"), regset("Turn off", "DWord", 0, "Off")], tags=["path"])

    S("explorer.sync-provider-notifications", "Sync provider notifications (OneDrive promotions)", "File Explorer",
      "OneDrive/Microsoft 365 notifications inside File Explorer.",
      reg(HKCU, ADV, "ShowSyncProviderNotifications", ONOFF, "On (default)"),
      ["On", "Off"], [("Most users", "Off", "Removes in-Explorer promotions.")], "None.", "None.",
      controls=[regset("Turn off", "DWord", 0, "Off"), regset("Turn on", "DWord", 1, "On")], tags=["ads", "onedrive"])

    S("explorer.compact-view", "Compact view", "File Explorer",
      "Reduces spacing between items (Windows 11).",
      reg(HKCU, ADV, "UseCompactMode", ONOFF, "Off (default)"),
      ["On", "Off"], [("Mouse/keyboard users", "On", "More items visible."), ("Touch screens", "Off", "Larger targets.")], "None.", "None.",
      controls=[regset("Turn on", "DWord", 1, "On"), regset("Turn off", "DWord", 0, "Off")], minBuild=22000, tags=["spacing"])

    S("explorer.checkboxes", "Item check boxes", "File Explorer",
      "Check boxes for selecting files.",
      reg(HKCU, ADV, "AutoCheckSelect", ONOFF, "Off"),
      ["On", "Off"], [("Touchpad/elderly users", "On", "Easier multi-select.")], "None.", "None.",
      controls=[regset("Turn on", "DWord", 1, "On"), regset("Turn off", "DWord", 0, "Off")], tags=["select"])

    S("explorer.separate-process", "Launch folder windows in a separate process", "File Explorer",
      "Isolates folder windows from the taskbar/desktop process.",
      reg(HKCU, ADV, "SeparateProcess", ONOFF, "Off (default)"),
      ["On", "Off"], [("Explorer crashes take down the taskbar", "On", "Folder crashes no longer restart the taskbar.")],
      "Slightly more memory.", "None.",
      controls=[regset("Turn on", "DWord", 1, "On"), regset("Turn off", "DWord", 0, "Off")], restart="explorer", tags=["stability", "crash"])

    S("explorer.thumbnails", "Always show icons, never thumbnails", "File Explorer",
      "Disables thumbnail previews.",
      reg(HKCU, ADV, "IconsOnly", {"1": "Icons only", "0": "Thumbnails (default)"}, "Thumbnails (default)"),
      ["Icons only", "Thumbnails"], [("Very slow PCs/network folders", "Icons only", "Faster browsing."), ("Most users", "Thumbnails", "Visual previews.")],
      "Icons only speeds up folders full of images/videos on slow PCs.", "None.",
      controls=[regset("Icons only", "DWord", 1, "Icons only"), regset("Thumbnails", "DWord", 0, "Thumbnails (default)")], tags=["preview"])

    # ======================================================================= Advanced system properties
    S("advanced.performance-options", "Performance options (visual effects)", "Advanced system properties",
      "Windows animations, shadows, and visual effects.",
      probe("visual.animations"), ["Let Windows choose", "Best appearance", "Best performance", "Custom"],
      [("Low-end PCs", "Best performance (keep 'Smooth edges of screen fonts')", "Snappier feel."), ("Modern PCs", "Let Windows choose", "No real benefit to disabling.")],
      "Noticeable only on low-end hardware.", "None.",
      controls=[act("Change visual effects (backed up)", "display.visual-effects"), tool("Open Performance Options", "SystemPropertiesPerformance.exe")], tags=["animations", "effects"])

    S("advanced.virtual-memory", "Virtual memory (page file)", "Advanced system properties",
      "Disk space used to extend RAM.",
      probe("system.pagefile"), ["System managed (default)", "Custom size", "No paging file"],
      [("All", "System managed", "Disabling the page file causes crashes when memory runs out and prevents crash dumps.")],
      "A page file on an SSD performs best.", "'No paging file' causes out-of-memory crashes and prevents BSOD dumps.",
      admin=True, restart="restart", controls=[tool("Open Performance Options (Advanced → Virtual memory)", "SystemPropertiesPerformance.exe")], tags=["pagefile", "swap"])

    S("advanced.system-protection", "System Protection (System Restore)", "Advanced system properties",
      "Restore points for the system drive.",
      probe("system.restore"), ["On (with disk space limit)", "Off"], [("All", "On for C:, 5–10% space", "Undo bad driver/update changes.")],
      "Uses some disk space.", "Turning off deletes existing restore points.",
      admin=True, controls=[act("Create a restore point", "maintenance.restore-point"), tool("Open System Protection", "SystemPropertiesProtection.exe")], tags=["restore point"])

    S("advanced.auto-restart", "Automatically restart after a system failure", "Advanced system properties",
      "Whether Windows restarts immediately after a blue screen. Turning it off keeps the stop code on screen so the customer can photograph it.",
      reg(HKLM, r"SYSTEM\CurrentControlSet\Control\CrashControl", "AutoReboot", ONOFF, "On (default)"),
      ["On", "Off"], [("PCs with unexplained restarts", "Off while diagnosing", "Shows the stop code instead of rebooting."), ("Unattended PCs", "On", "Recovers automatically.")],
      "None.", "With Off, the PC stays on the blue screen until restarted manually.",
      admin=True, controls=[regset("Turn off (show stop codes)", "DWord", 0, "Off"), regset("Turn on (default)", "DWord", 1, "On"), tool("Open Startup and Recovery", "SystemPropertiesAdvanced.exe")], tags=["bsod", "blue screen"])

    S("advanced.crash-dump", "Crash dump type", "Advanced system properties",
      "What Windows saves when it crashes.",
      reg(HKLM, r"SYSTEM\CurrentControlSet\Control\CrashControl", "CrashDumpEnabled", {"0": "None", "1": "Complete memory dump", "2": "Kernel memory dump", "3": "Small memory dump (minidump)", "7": "Automatic memory dump (default)"}, "Automatic (default)"),
      ["None", "Small", "Kernel", "Complete", "Automatic", "Active"], [("All", "Automatic or Small", "Keeps minidumps for diagnosis.")],
      "None.", "'None' leaves no evidence after blue screens.",
      admin=True, controls=[regset("Automatic (default)", "DWord", 7, "Automatic memory dump"), regset("Small memory dump", "DWord", 3, "Small memory dump"), tool("Open Startup and Recovery", "SystemPropertiesAdvanced.exe")], tags=["minidump", "bsod"])

    S("advanced.environment-variables", "Environment variables", "Advanced system properties",
      "PATH and other environment variables.",
      none(), ["Per variable"], [("Developers", "Edit carefully", "Broken PATH entries break command-line tools.")], "None.", "Deleting system PATH entries breaks Windows tools.",
      controls=[tool("Open Environment Variables", "rundll32.exe", "sysdm.cpl,EditEnvironmentVariables")], tags=["path"])

    S("advanced.remote-assistance", "Remote Assistance", "Advanced system properties",
      "Allows inviting someone to help remotely. Tech-support scammers often ask victims to enable remote tools.",
      reg(HKLM, r"SYSTEM\CurrentControlSet\Control\Remote Assistance", "fAllowToGetHelp", ONOFF, "Default"),
      ["On", "Off"], [("Customers who don't use it", "Off", "Removes an avenue for scam 'support' sessions.")], "None.", "The console only reports this; change it on the Remote tab.",
      controls=[tool("Open System Properties → Remote", "SystemPropertiesRemote.exe")], tags=["scam", "remote"])

    S("advanced.dep", "Data Execution Prevention", "Advanced system properties",
      "Memory protection that blocks code execution from data pages.",
      none(), ["Essential programs only (default)", "All programs"], [("All", "Default", "Compatible and protective.")], "None.", "Forcing DEP for all programs can break very old software.",
      controls=[tool("Open DEP settings", "SystemPropertiesDataExecutionPrevention.exe")], tags=["security"])

    # ======================================================================= Services
    def service_entry(id, display, name, desc, default_mode, recs, perf, risks, restore_mode, critical=False):
        controls = [msc("Open Services", "services.msc")]
        controls.insert(0, act(f"Restore to {restore_mode} (backed up)", "services.start-mode", {"service": name, "startMode": restore_mode}))
        S(id, display, "Services", desc, svc(name), [f"{default_mode} (Windows default)", "Automatic", "Manual", "Disabled"],
          recs, perf, risks + (" The console never disables this core service." if critical else ""), admin=True, controls=controls, tags=["service", name.lower()])

    service_entry("services.windows-search", "Windows Search (WSearch)", "WSearch", "Indexes files for fast searching.", "Automatic (Delayed)",
                  [("All", "Automatic (Delayed)", "Disabling breaks Start, Outlook, and File Explorer search.")], "Brief CPU/disk use after big file changes.", "Disabling makes searches very slow or broken.", "AutoDelayed")
    service_entry("services.sysmain", "SysMain (formerly Superfetch)", "SysMain", "Preloads frequently used apps into spare memory.", "Automatic",
                  [("SSD PCs", "Automatic (default)", "Benefit is small but disabling rarely helps."), ("HDD PC with constant 100% disk", "Diagnose first", "Usually a symptom of an HDD/low RAM, not SysMain itself.")],
                  "Uses idle memory; frees it instantly when needed.", "Disabling does not measurably speed up modern PCs.", "Auto")
    service_entry("services.diagtrack", "Connected User Experiences and Telemetry (DiagTrack)", "DiagTrack", "Sends diagnostic data according to the diagnostic data level.", "Automatic",
                  [("All", "Automatic (use the Diagnostic data setting to limit data)", "Disabling the service gives no measurable speed-up and is unsupported.")], "Negligible.", "Disabling may break Windows Insider and some troubleshooting features.", "Auto")
    service_entry("services.time", "Windows Time (W32Time)", "W32Time", "Keeps the clock synchronised.", "Manual (trigger start)",
                  [("All", "Manual (default)", "Starts automatically for synchronisation.")], "None.", "Clock drift breaks HTTPS and sign-ins.", "Manual", critical=True)
    service_entry("services.bits", "Background Intelligent Transfer Service (BITS)", "bits", "Downloads updates in the background.", "Manual",
                  [("All", "Manual (default)", "Required by Windows Update and Store.")], "Throttles itself to idle bandwidth.", "Disabled breaks Windows Update.", "Manual", critical=True)
    service_entry("services.remote-registry", "Remote Registry", "RemoteRegistry", "Allows remote users to modify the registry.", "Disabled",
                  [("All home PCs", "Disabled (default)", "Reduces attack surface.")], "None.", "Enabled on a home PC is unusual and worth investigating.", "Disabled")
    service_entry("services.wlan", "WLAN AutoConfig", "WlanSvc", "Manages Wi-Fi connections.", "Automatic",
                  [("All with Wi-Fi", "Automatic", "Required for Wi-Fi.")], "None.", "Disabled removes Wi-Fi entirely.", "Auto", critical=True)
    S("services.audio", "Windows Audio (Audiosrv)", "Services", "Core audio service.", svc("Audiosrv"), ["Automatic (default)"],
      [("No sound at all", "Check it is running", "Restarting it fixes some audio failures.")], "None.", "Core service — the console never changes it.",
      controls=[msc("Open Services", "services.msc")], tags=["service", "audio"])
    S("services.defender", "Microsoft Defender Antivirus Service", "Services", "Antivirus engine service (MsMpEng.exe).", svc("WinDefend"), ["Automatic"],
      [("All", "Running (unless another antivirus replaced it)", "Protection.")], "Scans use CPU briefly, mostly when idle.", "Protected by Windows; cannot and should not be disabled.",
      controls=[uri("Open Windows Security", "windowsdefender:"), msc("Open Services", "services.msc")], tags=["service", "msmpeng"])

    # ======================================================================= Scheduled tasks
    S("tasks.library", "Task Scheduler library", "Scheduled tasks",
      "All scheduled tasks. Third-party sign-in tasks can be reviewed in Startup.",
      none(), ["Not a setting"], [("All", "Review non-Microsoft tasks with unknown authors", "Malware and adware often persist as tasks.")],
      "Frequent tasks can cause periodic CPU spikes.", "Do not delete Microsoft tasks.",
      controls=[msc("Open Task Scheduler", "taskschd.msc"), act("Review third-party sign-in tasks", "startup.logon-tasks")], tags=["persistence"])

    def task_entry(id, name, path, desc, recs, perf, risks):
        S(id, name, "Scheduled tasks", desc, task(path), ["Enabled (default)", "Disabled"], recs, perf, risks, admin=True,
          controls=[act("Enable (Windows default)", "tasks.enabled", {"task": path, "enabled": "true"}), msc("Open Task Scheduler", "taskschd.msc")], tags=["task"])

    task_entry("tasks.defrag", "Scheduled drive optimisation", r"\Microsoft\Windows\Defrag\ScheduledDefrag", "Weekly defrag (HDD) and TRIM (SSD).",
               [("All", "Enabled", "Maintains drive performance.")], "Runs during idle time.", "Disabling lets SSD write performance degrade and HDDs fragment.")
    task_entry("tasks.silent-cleanup", "Disk Cleanup silent cleanup", r"\Microsoft\Windows\DiskCleanup\SilentCleanup", "Runs Disk Cleanup automatically when space is low.",
               [("All", "Enabled", "Automatic space recovery.")], "Runs when space is low.", "None.")
    task_entry("tasks.compat-appraiser", "Microsoft Compatibility Appraiser", r"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser",
               "Checks app compatibility for upgrades (runs CompatTelRunner.exe, which can use high CPU/disk for a while).",
               [("All", "Enabled", "Needed for upgrade compatibility checks; the CPU use is temporary.")], "Periodic CPU/disk bursts.", "Disabling may delay feature update offers.")
    task_entry("tasks.update-scan", "Windows Update scheduled scan", r"\Microsoft\Windows\UpdateOrchestrator\Schedule Scan", "Regular checks for Windows updates.",
               [("All", "Enabled", "Keeps updates flowing.")], "Brief network/CPU use.", "Disabled delays security updates.")
    task_entry("tasks.system-restore", "System Restore scheduled checkpoint", r"\Microsoft\Windows\SystemRestore\SR", "Creates periodic restore points.",
               [("All", "Enabled", "Regular safety points.")], "Brief disk activity.", "Fewer restore points to recover from.")

    # ======================================================================= Registry-backed preferences
    S("registry.menu-show-delay", "Menu show delay", "Registry-backed preferences",
      "Delay before cascading menus open (milliseconds).",
      reg(HKCU, r"Control Panel\Desktop", "MenuShowDelay", {"*": "{value} ms"}, "400 ms (default)"),
      ["0–4000 ms (default 400)"], [("Power users", "200 ms", "Menus feel more responsive."), ("Users who open menus accidentally", "400 ms (default)", "Less accidental opening.")],
      "Perceived responsiveness only.", "Very low values make menus open accidentally.", restart="signout",
      controls=[regset("Set 200 ms", "String", 200, "200 ms"), regset("Restore 400 ms (default)", "String", 400, "400 ms (default)")], tags=["menu", "responsiveness"])

    S("registry.long-paths", "Win32 long path support", "Registry-backed preferences",
      "Allows paths longer than 260 characters for apps that support them (documented by Microsoft).",
      reg(HKLM, r"SYSTEM\CurrentControlSet\Control\FileSystem", "LongPathsEnabled", ONOFF, "Off (default)"),
      ["On", "Off"], [("Developers / deep folder structures", "On", "Avoids 'path too long' errors in supporting apps.")],
      "None.", "Apps that don't support long paths still fail; File Explorer itself has limits.",
      admin=True, restart="restart", controls=[regset("Turn on", "DWord", 1, "On"), regset("Turn off", "DWord", 0, "Off")], tags=["path too long"])

    S("registry.verbose-status", "Verbose startup/shutdown messages", "Registry-backed preferences",
      "Shows detailed status (e.g. 'Stopping service X') during startup, shutdown, sign-in, and sign-out. Documented Group Policy setting; useful for diagnosing slow shutdowns.",
      reg(HKLM, r"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "VerboseStatus", ONOFF, "Off (default)"),
      ["On", "Off"], [("Diagnosing slow startup/shutdown", "On temporarily", "Reveals what Windows is waiting for.")], "None.", "None.",
      admin=True, controls=[regset("Turn on", "DWord", 1, "On"), regset("Turn off", "DWord", 0, "Off")], tags=["slow shutdown"])

    S("registry.numlock-boot", "Num Lock at sign-in screen", "Registry-backed preferences",
      "Whether Num Lock is on at the sign-in screen (affects PIN entry on the number pad).",
      reg(HKU, r".DEFAULT\Control Panel\Keyboard", "InitialKeyboardIndicators", {"2": "On", "0": "Off", "2147483648": "Off (default)", "2147483650": "On"}, "Default"),
      ["On", "Off"], [("Customers using number-pad PINs", "On", "PIN entry works immediately.")], "None.", "Fast Startup can override the state; firmware settings also affect it.",
      admin=True, restart="restart", controls=[regset("Turn on", "String", 2, "On"), regset("Turn off", "String", 0, "Off")], tags=["numlock", "pin"])

    S("registry.auto-end-tasks", "Automatically end tasks at shutdown", "Registry-backed preferences",
      "Closes hung apps at shutdown without asking. Reported only: enabling it risks losing unsaved work.",
      reg(HKCU, r"Control Panel\Desktop", "AutoEndTasks", {"1": "On", "0": "Off (default)"}, "Off (default)"),
      ["On", "Off"], [("All", "Off", "Users get the chance to save work.")], "Faster shutdown when apps hang.", "Unsaved work is discarded.",
      controls=[uri("Open Power & sleep", "ms-settings:powersleep")], tags=["shutdown"])

    # ======================================================================= Group Policy
    GP_NOTE = "Group Policy values are reported, not changed: policies are usually set deliberately by an organisation, or by a tweak tool that should be reviewed."
    S("gp.editor", "Local Group Policy Editor", "Group Policy",
      "Edits local policies (Pro, Education, Enterprise only).",
      none(), ["Not a setting"], [("Pro editions", "Use for documented policies only", "Policies override Settings.")], "None.", GP_NOTE,
      editions=PRO, controls=[msc("Open Group Policy Editor", "gpedit.msc"), msc("Open Resultant Set of Policy", "rsop.msc")], tags=["gpedit"])

    S("gp.windows-update-disabled", "Automatic Updates disabled by policy", "Group Policy",
      "NoAutoUpdate policy — often set by tweak tools to stop updates.",
      reg(HKLM, r"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate", {"1": "Automatic updates disabled by policy", "0": "Enabled by policy"}, "Not configured (good)"),
      ["Not configured", "Enabled", "Disabled"], [("Home PCs", "Not configured", "Normal update behaviour.")], "None.", GP_NOTE,
      controls=[uri("Open Windows Update", "ms-settings:windowsupdate"), msc("Open Group Policy Editor", "gpedit.msc")], tags=["policy", "updates"])

    S("gp.defender-disabled", "Defender disabled by policy", "Group Policy",
      "DisableAntiSpyware policy. Ignored by current Defender versions but its presence indicates a tweak tool or malware tampered with security.",
      reg(HKLM, r"SOFTWARE\Policies\Microsoft\Windows Defender", "DisableAntiSpyware", {"1": "Set (investigate)", "0": "Not disabling"}, "Not configured (good)"),
      ["Not configured", "Set"], [("All", "Not configured", "Defender works normally.")], "None.", GP_NOTE,
      controls=[uri("Open Windows Security", "windowsdefender:")], tags=["policy", "tamper"])

    S("gp.task-manager-disabled", "Task Manager disabled by policy", "Group Policy",
      "DisableTaskMgr — commonly set by malware to stop users from ending it.",
      reg(HKCU, r"Software\Microsoft\Windows\CurrentVersion\Policies\System", "DisableTaskMgr", {"1": "Task Manager blocked (investigate)", "0": "Allowed"}, "Not configured (good)"),
      ["Not configured", "Blocked"], [("All", "Not configured", "Task Manager available.")], "None.", GP_NOTE + " If set unexpectedly, scan for malware.",
      controls=[uri("Open Windows Security", "windowsdefender://threat")], tags=["malware", "policy"])

    S("gp.registry-tools-disabled", "Registry Editor disabled by policy", "Group Policy",
      "DisableRegistryTools — sometimes set by malware.",
      reg(HKCU, r"Software\Microsoft\Windows\CurrentVersion\Policies\System", "DisableRegistryTools", {"1": "Blocked (investigate)", "2": "Blocked (investigate)", "0": "Allowed"}, "Not configured (good)"),
      ["Not configured", "Blocked"], [("All", "Not configured", "Normal.")], "None.", GP_NOTE,
      controls=[uri("Open Windows Security", "windowsdefender://threat")], tags=["malware", "regedit"])

    S("gp.cmd-disabled", "Command Prompt disabled by policy", "Group Policy",
      "DisableCMD policy.",
      reg(HKCU, r"Software\Policies\Microsoft\Windows\System", "DisableCMD", {"1": "Blocked", "2": "Blocked (scripts allowed)", "0": "Allowed"}, "Not configured (good)"),
      ["Not configured", "Blocked"], [("Home PCs", "Not configured", "Normal.")], "None.", GP_NOTE,
      controls=[msc("Open Group Policy Editor", "gpedit.msc")], tags=["policy"])

    S("gp.search-web-results", "Web results in Start search disabled by policy", "Group Policy",
      "DisableSearchBoxSuggestions — removes Bing web results from Start search.",
      reg(HKCU, r"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", {"1": "Web results disabled", "0": "Allowed"}, "Not configured (web results shown)"),
      ["Not configured", "Disabled"], [("Customers who find web results confusing", "Enabled by IT/technician via Group Policy", "Local-only search.")], "Slightly faster search results.", GP_NOTE,
      controls=[msc("Open Group Policy Editor", "gpedit.msc")], tags=["bing", "search"])

    S("gp.telemetry-policy", "Diagnostic data policy", "Group Policy",
      "AllowTelemetry policy value.",
      reg(HKLM, r"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", {"0": "Security", "1": "Required", "2": "Enhanced", "3": "Optional"}, "Not configured"),
      ["Not configured", "0–3"], [("Home PCs", "Not configured (use Settings)", "Avoids policy conflicts.")], "None.", GP_NOTE,
      controls=[uri("Open Diagnostics & feedback", "ms-settings:privacy-feedback")], tags=["telemetry", "policy"])

    # ======================================================================= Legacy Control Panel
    def legacy(id, name, desc, control, use, tags):
        S(id, name, "Legacy Control Panel", desc, none("Classic Control Panel applet — opens the tool."), ["Opens the classic applet"],
          [("Technicians", use, "Some options exist only in the classic applet.")], "None.", "Changes made in the applet take effect as described there.",
          controls=[control], tags=tags)

    legacy("cpl.programs", "Programs and Features", "Classic uninstall list including repair options for MSI products.", applet("Open Programs and Features", "appwiz.cpl"), "Repair/uninstall desktop programs", ["appwiz"])
    legacy("cpl.network-connections", "Network Connections", "Adapter properties, IPv4/IPv6, DNS, and enable/disable adapters.", applet("Open Network Connections", "ncpa.cpl"), "Adapter properties and manual DNS", ["ncpa"])
    legacy("cpl.sound", "Sound", "Playback/recording devices, formats, enhancements, and communications ducking.", applet("Open Sound", "mmsys.cpl"), "Default formats and enhancements", ["mmsys"])
    legacy("cpl.mouse", "Mouse properties", "Buttons, pointers, pointer precision, and wheel settings.", applet("Open Mouse properties", "main.cpl"), "Pointer precision and button swap", ["mouse"])
    legacy("cpl.power-options", "Power Options", "Power plans and advanced power settings.", applet("Open Power Options", "powercfg.cpl"), "Advanced plan settings", ["power"])
    legacy("cpl.system-properties", "System Properties", "Computer name, hardware, advanced, protection, and remote tabs.", applet("Open System Properties", "sysdm.cpl"), "Performance, page file, startup and recovery", ["sysdm"])
    legacy("cpl.internet-options", "Internet Options", "Legacy proxy, security zones, and TLS settings still used by many desktop apps.", applet("Open Internet Options", "inetcpl.cpl"), "Proxy/LAN settings and resetting IE settings", ["proxy", "tls"])
    legacy("cpl.date-time", "Date and Time", "Classic time settings and internet time server.", applet("Open Date and Time", "timedate.cpl"), "Internet time server sync", ["clock"])
    legacy("cpl.region", "Region", "Formats and administrative language settings (non-Unicode programs).", applet("Open Region", "intl.cpl"), "Non-Unicode program language for old apps", ["locale"])
    legacy("cpl.game-controllers", "Game Controllers", "Connected joysticks/gamepads and calibration.", applet("Open Game Controllers", "joy.cpl"), "Controller testing", ["gamepad"])
    legacy("cpl.firewall", "Windows Defender Firewall", "Firewall profiles and allowed apps.", applet("Open Firewall", "firewall.cpl"), "Allowed apps list", ["firewall"])
    legacy("cpl.user-accounts", "User Accounts", "Account type changes and credential links.", cpl("Open User Accounts", "Microsoft.UserAccounts"), "Change account type", ["accounts"])
    legacy("cpl.devices-printers", "Devices and Printers", "Classic devices view with printer properties and troubleshooting.", cpl("Open Devices and Printers", "Microsoft.DevicesAndPrinters"), "Printer properties and queues", ["printers"])
    legacy("cpl.indexing", "Indexing Options", "Indexed locations, rebuilding the index.", cpl("Open Indexing Options", "Microsoft.IndexingOptions"), "Rebuild a broken search index", ["search"])
    legacy("cpl.security-maintenance", "Security and Maintenance", "Reliability history link and maintenance messages.", applet("Open Security and Maintenance", "wscui.cpl"), "Reliability history and problem reports", ["maintenance"])
    legacy("cpl.recovery", "Recovery (System Restore)", "Open System Restore and recovery tools.", cpl("Open Recovery", "Microsoft.Recovery"), "Launch System Restore", ["system restore"])
    legacy("cpl.file-history", "File History", "Classic backup to an external drive.", cpl("Open File History", "Microsoft.FileHistory"), "Configure external-drive backups", ["backup"])
    legacy("cpl.folder-options", "File Explorer Options", "View options, privacy, and search for File Explorer.", cpl("Open File Explorer Options", "Microsoft.FolderOptions"), "View settings not exposed in Settings", ["folder options"])

    # ======================================================================= Administrative tools
    def admin_tool(id, name, desc, control, use, admin=False, editions=None, tags=()):
        S(id, name, "Administrative tools", desc, none("Administrative tool — opens the console."), ["Not a setting"],
          [("Technicians", use, "Primary diagnostic tool for this area.")], "None.", "Administrative consoles can make significant changes — use with care.",
          admin=admin, editions=editions, controls=[control], tags=list(tags))

    admin_tool("admin.computer-management", "Computer Management", "Event Viewer, services, disks, devices, and local users in one console.", msc("Open Computer Management", "compmgmt.msc"), "One-stop admin console", admin=True, tags=["compmgmt"])
    admin_tool("admin.event-viewer", "Event Viewer", "Windows event logs (the console's diagnostics read these too).", msc("Open Event Viewer", "eventvwr.msc"), "Inspect raw events behind findings", tags=["logs"])
    admin_tool("admin.services", "Services", "All services and their startup types.", msc("Open Services", "services.msc"), "Start/stop/restart services", admin=True, tags=["services"])
    admin_tool("admin.task-scheduler", "Task Scheduler", "Scheduled tasks.", msc("Open Task Scheduler", "taskschd.msc"), "Review persistence and updaters", tags=["tasks"])
    admin_tool("admin.disk-management", "Disk Management", "Partitions and drive letters.", msc("Open Disk Management", "diskmgmt.msc"), "Initialise/format/assign letters", admin=True, tags=["partition"])
    admin_tool("admin.performance-monitor", "Performance Monitor", "Live counters and data collector sets.", msc("Open Performance Monitor", "perfmon.msc"), "Long-running performance captures", tags=["perfmon"])
    admin_tool("admin.resource-monitor", "Resource Monitor", "Live per-process CPU, memory, disk, and network activity.", tool("Open Resource Monitor", "resmon.exe"), "Find which process is hitting the disk or network", tags=["disk 100%"])
    admin_tool("admin.reliability-monitor", "Reliability Monitor", "Timeline of crashes, installs, and failures.", tool("Open Reliability Monitor", "perfmon.exe", "/rel"), "Correlate problems with installs and updates", tags=["crash history"])
    admin_tool("admin.system-information", "System Information", "Detailed hardware/firmware inventory including Secure Boot and BIOS mode.", tool("Open System Information", "msinfo32.exe"), "Check BIOS mode, Secure Boot, and components", tags=["msinfo32", "bios"])
    admin_tool("admin.system-configuration", "System Configuration", "Boot options (safe mode, diagnostic startup).", tool("Open System Configuration", "msconfig.exe"), "Safe/diagnostic boot for clean-boot troubleshooting", admin=True, tags=["safe mode", "clean boot"])
    admin_tool("admin.registry-editor", "Registry Editor", "Direct registry editing.", tool("Open Registry Editor", "regedit.exe"), "Inspect values referenced in this catalog", admin=True, tags=["regedit"])
    admin_tool("admin.memory-diagnostic", "Windows Memory Diagnostic", "Tests RAM at the next restart.", tool("Open Windows Memory Diagnostic", "mdsched.exe"), "Suspected RAM faults (BSODs, corruption)", admin=True, tags=["ram test"])
    admin_tool("admin.dxdiag", "DirectX Diagnostic Tool", "Graphics, sound, and DirectX information; saves a report.", tool("Open DirectX Diagnostic Tool", "dxdiag.exe"), "Collect GPU/driver details", tags=["directx", "gpu"])
    admin_tool("admin.firewall-advanced", "Windows Defender Firewall with Advanced Security", "Inbound/outbound rules.", msc("Open Advanced Firewall", "wf.msc"), "Inspect rules blocking apps", admin=True, tags=["firewall rules"])
    admin_tool("admin.local-security-policy", "Local Security Policy", "Security policies (Pro editions).", msc("Open Local Security Policy", "secpol.msc"), "Audit/security policies", admin=True, editions=PRO, tags=["secpol"])
    admin_tool("admin.local-users", "Local Users and Groups", "Local accounts and group membership (Pro editions).", msc("Open Local Users and Groups", "lusrmgr.msc"), "Fix admin group membership", admin=True, editions=PRO, tags=["users"])
    admin_tool("admin.windows-tools", "Windows Tools", "Folder of all administrative tools.", cpl("Open Windows Tools", "Microsoft.AdministrativeTools"), "Browse all admin tools", tags=["tools"])
    admin_tool("admin.task-manager", "Task Manager", "Processes, performance, startup apps, and services.", tool("Open Task Manager", "taskmgr.exe"), "Quick live view", tags=["taskmgr"])
    admin_tool("admin.msrt", "Malicious Software Removal Tool", "Microsoft's on-demand removal tool for prevalent malware (delivered monthly by Windows Update).", tool("Open MSRT", "mrt.exe"), "Second-opinion scan", admin=True, tags=["malware"])
    admin_tool("admin.recovery-drive", "Recovery Drive Creator", "Creates a USB recovery drive.", tool("Open Recovery Drive", "RecoveryDrive.exe"), "Prepare for future repairs", admin=True, tags=["usb recovery"])

    return []

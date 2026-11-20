#!/usr/bin/env python3
"""Generates src/SoftReset.Core/Knowledge/Data/gpu.json — GPU vendor settings explained.

NVIDIA setting IDs and value enumerations are taken from NVIDIA's public NVAPI header
NvApiDriverSettings.h (github.com/NVIDIA/nvapi, MIT licence). The console only READS these
through the documented NVAPI DRS interface; it never writes driver settings. Changes are made by
opening the NVIDIA Control Panel / NVIDIA App with instructions.
"""
import json
import os

G = []


def s(id, vendor, cat, name, desc, effects, tradeoffs, recs, where, drs=None, values=None, default=None, fmt=None, notes=None, open_target=None):
    e = dict(id=id, vendor=vendor, category=cat, name=name, description=desc, effects=effects, tradeoffs=tradeoffs,
             recommendations=[dict(useCase=u, value=v, why=w) for u, v, w in recs], whereToChange=where,
             readability="Read from the driver (NVAPI DRS)" if drs else "Not readable through a documented interface — open the vendor control panel")
    if drs: e["drsSettingId"] = drs
    if values: e["valueNames"] = values
    if default: e["defaultValue"] = default
    if fmt: e["valueFormat"] = fmt
    if notes: e["notes"] = notes
    if open_target: e["openTarget"] = open_target
    G.append(e)


NV = "NVIDIA"
NVCP3D = "NVIDIA Control Panel → 3D Settings → Manage 3D settings (Global or Program Settings), or NVIDIA App → Graphics."

# ------------------------------------------------------------------ NVIDIA: latency & sync
s("nv.low-latency", NV, "Low latency", "Low Latency Mode",
  "Limits how many frames the CPU queues ahead of the GPU (formerly 'Maximum pre-rendered frames').",
  "On (1 frame) reduces input latency when the GPU is the bottleneck. Ultra additionally schedules frames just in time.",
  "Can slightly reduce average FPS or increase stutter in CPU-bound games. Games with NVIDIA Reflex ignore it — use Reflex in-game instead.",
  [("Competitive games without Reflex", "On or Ultra", "Lower input latency when GPU-bound."), ("Games with Reflex", "Off (use Reflex)", "Reflex is more effective and overrides this."), ("Single-player/cinematic", "Off", "Smoothest frame pacing.")],
  NVCP3D, drs="0x007BA09E", values={"0": "Off (application-controlled)", "1": "On (1 frame) — Ultra also reads as 1"}, default="0", fmt="frames",
  notes="Ultra uses an additional internal driver flag that the public NVAPI headers don't document, so On and Ultra both read as '1 frame'.")

s("nv.vsync", NV, "Vertical synchronization", "Vertical sync",
  "Synchronises frame presentation to the display refresh to prevent tearing.",
  "On removes tearing; Fast Sync allows uncapped rendering without tearing (needs FPS well above refresh).",
  "Adds input latency when FPS reaches the refresh rate. With G-SYNC, use V-Sync On in NVCP + Off in-game + a frame cap ~3 FPS below refresh.",
  [("G-SYNC/VRR monitor", "On (with a frame cap below refresh)", "Tear-free with minimal latency inside the VRR range."), ("Competitive, fixed-refresh monitor", "Off", "Lowest latency, accepts tearing."), ("General", "Use the 3D application setting", "Let each game decide.")],
  NVCP3D, drs="0x00A879CF",
  values={"0x60925292": "Use the 3D application setting", "0x08416747": "Off", "0x47814940": "On", "0x32610244": "1/2 refresh rate", "0x71271021": "1/3 refresh rate", "0x13245256": "1/4 refresh rate", "0x18888888": "Fast"},
  default="0x60925292")

s("nv.max-frame-rate", NV, "Frame-rate limits", "Max Frame Rate",
  "Driver-level frame-rate cap.",
  "Caps FPS to reduce power, heat, and fan noise; keeps G-SYNC active when capped below refresh.",
  "In-game limiters and Reflex usually have lower latency than the driver limiter.",
  [("G-SYNC monitor", "~3 FPS below refresh (e.g. 141 for 144 Hz)", "Stays inside the VRR range."), ("Laptops/quiet PCs", "60–90 FPS for light games", "Less heat and noise."), ("Competitive", "Off (use in-game cap/Reflex)", "Lowest latency.")],
  NVCP3D, drs="0x10835002", values={"0": "Off"}, default="0", fmt="fps")

s("nv.background-fps", NV, "Frame-rate limits", "Background Application Max Frame Rate",
  "Caps FPS for games running in the background (alt-tabbed).",
  "Saves power and heat when a game is minimised.", "None for the foreground game.",
  [("Most users", "20–30 FPS", "Prevents a background game from heating the PC.")], NVCP3D)

s("nv.vrr-mode", NV, "G-SYNC", "G-SYNC application mode",
  "Which applications may use variable refresh (G-SYNC / G-SYNC Compatible).",
  "Fullscreen and windowed allows VRR in borderless games.",
  "Windowed mode can cause flicker or slowdowns in some desktop apps on certain monitors.",
  [("Gaming with borderless windows", "Fullscreen and windowed", "VRR in most games."), ("Flicker in apps", "Fullscreen only", "Restricts VRR to exclusive fullscreen.")],
  "NVIDIA Control Panel → Display → Set up G-SYNC (enable per display and choose fullscreen/windowed).",
  drs="0x1194F158", values={"0": "Disabled", "1": "Fullscreen only", "2": "Fullscreen and windowed"}, default="1",
  notes="G-SYNC must also be enabled per monitor in 'Set up G-SYNC', which is not exposed through the DRS profile settings.")

s("nv.monitor-technology", NV, "G-SYNC", "Monitor technology",
  "Per-application override of variable refresh (G-SYNC vs fixed refresh).",
  "Forcing fixed refresh disables VRR for that application.", "Fixed refresh reintroduces tearing/stutter in games below refresh.",
  [("Most games", "G-SYNC (Allow)", "Smooth frame delivery.")], NVCP3D, drs="0x10A879CF",
  values={"0": "G-SYNC / VRR allowed", "1": "Force off", "2": "Disallow", "3": "ULMB", "4": "Fixed refresh"}, default="0")

s("nv.power-management", NV, "Power management", "Power management mode",
  "How aggressively the GPU drops clocks when lightly loaded.",
  "'Prefer maximum performance' keeps clocks high, avoiding brief clock ramp-up stutters in some games.",
  "Higher idle power and heat; on multi-monitor desktops the GPU may never idle down. Laptops lose battery life.",
  [("Most users", "Normal / Optimal power (default)", "Clocks ramp up instantly under load anyway."), ("Specific game with clock-related stutter", "Prefer maximum performance (that game's profile only)", "Avoid applying globally.")],
  NVCP3D, drs="0x1057EB71",
  values={"0": "Adaptive", "1": "Prefer maximum performance", "2": "Driver controlled", "3": "Prefer consistent performance", "4": "Prefer minimum power", "5": "Normal (Optimal power)"}, default="5")

# ------------------------------------------------------------------ NVIDIA: image quality
s("nv.texture-quality", NV, "Texture filtering", "Texture filtering - Quality",
  "Overall texture filtering quality/performance trade-off.",
  "Higher performance settings apply filtering optimisations.", "Performance gains are negligible on modern GPUs; High performance can cause shimmering.",
  [("All modern GPUs", "Quality or High quality", "No meaningful FPS gain from lower settings.")], NVCP3D, drs="0x00CE2691",
  values={"0xFFFFFFF6": "High quality", "0": "Quality", "0x0000000A": "Performance", "0x00000014": "High performance"}, default="0")

s("nv.aniso-level", NV, "Texture filtering", "Anisotropic filtering",
  "Sharpens textures viewed at angles.", "16x looks noticeably sharper on floors/roads.", "Very small performance cost on modern GPUs.",
  [("Most games", "Application-controlled, or 16x override", "Cheap quality improvement.")], NVCP3D, drs="0x101E61A9",
  values={"0": "Off (point)", "1": "Off (application-controlled)", "2": "2x", "4": "4x", "8": "8x", "16": "16x"}, default="1")

s("nv.aniso-mode", NV, "Texture filtering", "Anisotropic filtering mode",
  "Whether the driver overrides the game's anisotropic filtering.", "Override forces the level above.", "Can conflict with games that already use AF.",
  [("Most users", "Application-controlled", "Avoids double filtering.")], NVCP3D, drs="0x10D2BB16", values={"0": "Application-controlled", "1": "User-defined (override)", "2": "Conditional"}, default="0")

s("nv.neg-lod-bias", NV, "Texture filtering", "Texture filtering - Negative LOD bias",
  "Allows or clamps negative level-of-detail bias.", "Clamp reduces shimmering when anisotropic filtering is forced.", "None significant.",
  [("Forced AF", "Clamp", "Less shimmer.")], NVCP3D, drs="0x0019BB68", values={"0": "Allow", "1": "Clamp"}, default="0")

s("nv.trilinear-opt", NV, "Texture filtering", "Texture filtering - Trilinear optimization",
  "Uses bilinear filtering where differences are invisible.", "Tiny performance gain.", "Ignored when Quality is High quality.",
  [("All", "Default", "No measurable impact.")], NVCP3D, drs="0x002ECAF2", values={"0": "On", "1": "Off"}, default="0")

s("nv.aniso-sample-opt", NV, "Texture filtering", "Texture filtering - Anisotropic sample optimization",
  "Reduces anisotropic samples for performance.", "Tiny gain.", "Can cause shimmering.",
  [("All", "Off", "Quality over negligible gains.")], NVCP3D, drs="0x0084CD70", values={"0": "Off", "1": "On"}, default="0")

s("nv.aa-mode", NV, "Antialiasing", "Antialiasing - Mode",
  "Whether the driver overrides or enhances in-game antialiasing.", "Driver AA only works in older DirectX 9/OpenGL titles.",
  "Overriding can break modern deferred-rendering games; use in-game AA/DLSS instead.",
  [("Modern games", "Application-controlled", "Use in-game TAA/DLAA/DLSS.")], NVCP3D, drs="0x107EFC5B",
  values={"0": "Application-controlled", "1": "Override any application setting", "2": "Enhance the application setting"}, default="0")

s("nv.fxaa", NV, "Antialiasing", "Antialiasing - FXAA",
  "Post-process antialiasing applied by the driver.", "Smooths edges cheaply.", "Blurs text and UI; stacks with in-game AA.",
  [("Most users", "Off", "Prefer in-game AA.")], NVCP3D, drs="0x1074C972", values={"0": "Off", "1": "On"}, default="0")

s("nv.image-scaling", NV, "Display scaling", "Image Scaling (NVIDIA Image Scaling)",
  "Driver-level upscaling and sharpening for games that lack DLSS.",
  "Render below native resolution and upscale for higher FPS.", "Softer image than DLSS; also changes desktop resolution list when enabled.",
  [("Games without DLSS on lower-end GPUs", "On with 85% render scale", "Cheap FPS boost."), ("Games with DLSS", "Off", "DLSS is better.")],
  "NVIDIA Control Panel → Manage 3D settings → Image Scaling, or NVIDIA App → Graphics.", drs="0x10444444", values={"0": "Off", "1": "On"}, default="0")

# ------------------------------------------------------------------ NVIDIA: shader cache & OpenGL
s("nv.shader-cache", NV, "Shader caches", "Shader Cache",
  "Stores compiled shaders on disk so games don't recompile them each launch.",
  "Reduces stutter and load times after the first run.", "Corrupt caches can cause stutter/glitches — clear them after driver updates if problems appear.",
  [("All", "On", "Less compilation stutter.")], NVCP3D, drs="0x00198FFF", values={"0": "Off", "1": "On"}, default="1")

s("nv.shader-cache-size", NV, "Shader caches", "Shader Cache Size",
  "Maximum disk space for the shader cache.", "Larger caches avoid evicting shaders for big game libraries.", "Uses disk space.",
  [("Gamers with large libraries", "10 GB or Unlimited on SSDs with free space", "Fewer recompiles."), ("Small SSD", "Driver default", "Saves space.")],
  NVCP3D, drs="0x00AC8497", values={"4294967295": "Unlimited", "0": "Disabled"}, fmt="mb",
  notes="Shown as stored by the driver (megabytes). The cache folders can be cleared with the NVIDIA app guide's 'Clear cache' action.")

s("nv.threaded-optimization", NV, "3D settings", "Threaded optimization",
  "Lets the OpenGL driver use multiple CPU threads.", "Can improve FPS in CPU-bound OpenGL games.", "Rarely causes instability in old OpenGL titles.",
  [("All", "Auto", "Driver decides per game.")], NVCP3D, drs="0x20C1221E", values={"0": "Auto", "1": "On", "2": "Off"}, default="0")

s("nv.triple-buffering", NV, "3D settings", "Triple buffering (OpenGL)",
  "Third frame buffer for OpenGL games with V-Sync.", "Smoother FPS with V-Sync on in OpenGL.", "Adds a frame of latency; only affects OpenGL.",
  [("All", "Off unless using V-Sync in OpenGL games", "Lower latency.")], NVCP3D, drs="0x20FDD1F9", values={"0": "Off", "1": "On"}, default="0")

s("nv.ansel", NV, "3D settings", "Ansel / Photo mode",
  "NVIDIA's in-game photo mode.", "Enables photo mode in supported games.", "None while unused.",
  [("All", "Default", "No performance cost unless used.")], NVCP3D, drs="0x1075D972", values={"0": "Off", "1": "On"}, default="1")

s("nv.preferred-gpu", NV, "3D settings", "Preferred graphics processor (laptops)",
  "On hybrid-graphics laptops, which GPU runs an application.",
  "High-performance NVIDIA processor runs games on the dedicated GPU.", "Higher power use.",
  [("Gaming laptops", "Per game: High-performance NVIDIA processor", "Ensures the dedicated GPU is used."), ("Battery", "Auto-select", "iGPU for light apps.")],
  NVCP3D + " Windows' own per-app setting (Settings → Display → Graphics) takes priority on recent Windows versions.",
  drs="0x10F9DC80", values={"0": "Integrated graphics", "1": "High-performance NVIDIA processor", "16": "Auto-select"}, default="16")

# ------------------------------------------------------------------ NVIDIA: display (not in DRS)
s("nv.color-range", NV, "Color settings", "Output dynamic range and colour format",
  "RGB Full vs Limited range, colour depth, and format per display.",
  "Full range shows true blacks on PC monitors; Limited range causes washed-out greys.",
  "TVs may expect Limited; mismatches crush blacks or wash them out.",
  [("PC monitors over HDMI", "Use NVIDIA colour settings: RGB, Full, highest bit depth", "Fixes washed-out colours."), ("TVs", "Match the TV's black level setting", "Correct black levels.")],
  "NVIDIA Control Panel → Display → Change resolution → Use NVIDIA colour settings.", open_target="nvcp")

s("nv.digital-vibrance", NV, "Color settings", "Digital vibrance",
  "Boosts colour saturation.", "More vivid colours (popular in competitive shooters).", "Inaccurate colours for photo/video work.",
  [("Competitive gamers", "Personal preference (~60–70%)", "Visibility."), ("Creative work", "50% (default)", "Accurate colour.")],
  "NVIDIA Control Panel → Display → Adjust desktop colour settings.", open_target="nvcp")

s("nv.refresh-rate", NV, "Refresh rates", "Refresh rate (NVIDIA)",
  "Refresh rate per display; mirrors the Windows setting.", "Higher refresh = smoother motion.", "Cable/port bandwidth limits.",
  [("All", "Highest supported", "See the console's Display findings.")], "NVIDIA Control Panel → Display → Change resolution, or Windows Advanced display settings.", open_target="nvcp")

s("nv.display-scaling", NV, "Display scaling", "Scaling mode (GPU vs display)",
  "Where non-native resolutions are scaled.", "GPU scaling gives consistent results and enables integer scaling.", "Display scaling may add less latency on some monitors.",
  [("Stretched resolutions in games", "GPU scaling, Full-screen", "Predictable."), ("Pixel-art/retro", "Integer scaling", "Sharp pixels.")],
  "NVIDIA Control Panel → Display → Adjust desktop size and position.", open_target="nvcp")

s("nv.dsr", NV, "Display scaling", "DSR / DLDSR factors",
  "Renders above native resolution and downsamples.", "Sharper image.", "Large performance cost.",
  [("Older games with spare GPU power", "DLDSR 1.78x", "Image quality.")], NVCP3D, open_target="nvcp")

s("nv.physx", NV, "PhysX", "PhysX processor",
  "Which processor runs GPU PhysX effects in supported (mostly older) games.", "Auto-select uses the GPU.", "Forcing CPU tanks FPS in PhysX titles.",
  [("All", "Auto-select", "Driver picks the GPU.")], "NVIDIA Control Panel → 3D Settings → Configure Surround, PhysX.", open_target="nvcp",
  notes="PhysX configuration uses a separate NVAPI interface that is not read by the console.")

s("nv.rtx-video", NV, "3D settings", "RTX Video Super Resolution / HDR",
  "AI upscaling and SDR-to-HDR for browser video on RTX GPUs.", "Sharper streamed video.", "Higher GPU power use during video playback.",
  [("RTX owners watching low-res video", "On (Quality 1–2)", "Visible improvement."), ("Laptops on battery", "Off", "Power.")],
  "NVIDIA Control Panel → Video → Adjust video image settings, or NVIDIA App → System → Video.", open_target="nvcp")

s("nv.reflex", NV, "Low latency", "NVIDIA Reflex (in-game)",
  "In-game latency reduction technology.", "Lowest system latency in supported games.", "Reflex 'On + Boost' raises power use slightly.",
  [("Supported competitive games", "On", "Better than driver Low Latency Mode.")], "Inside each supported game's video settings.")

# ------------------------------------------------------------------ AMD (Adrenalin)
AMD = "AMD"
ADR = "AMD Software: Adrenalin Edition → Gaming → Graphics (Global or per-game)."
s("amd.anti-lag", AMD, "Low latency", "Radeon Anti-Lag / Anti-Lag 2", "Reduces input latency by pacing CPU work.", "Lower latency when GPU-bound.", "Slight FPS cost in some games; Anti-Lag 2 must be integrated by the game.",
  [("Competitive games", "On", "Lower latency.")], ADR)
s("amd.chill", AMD, "Frame-rate limits", "Radeon Chill", "Dynamically lowers FPS when there's little movement.", "Large power/heat savings.", "Variable FPS can feel inconsistent; adds latency at low FPS.",
  [("Laptops / quiet PCs", "On with min/max near refresh", "Saves power."), ("Competitive", "Off", "Consistent FPS.")], ADR)
s("amd.frtc", AMD, "Frame-rate limits", "Frame Rate Target Control", "Driver frame-rate cap.", "Reduces power and heat.", "In-game caps usually have lower latency.",
  [("FreeSync monitor", "A few FPS below refresh", "Stays in VRR range.")], ADR)
s("amd.boost", AMD, "3D settings", "Radeon Boost", "Lowers resolution dynamically during fast motion.", "Higher FPS during motion.", "Blurrier image while moving.",
  [("Low-end GPUs", "On", "FPS gains in supported games."), ("High-end", "Off", "Image quality.")], ADR)
s("amd.ris", AMD, "3D settings", "Radeon Image Sharpening", "Contrast-adaptive sharpening.", "Crisper image, especially with upscaling.", "Over-sharpening adds halos.",
  [("TAA-blurry games", "On, 50–80%", "Restores detail.")], ADR)
s("amd.enhanced-sync", AMD, "Vertical synchronization", "Enhanced Sync", "Tear-free presentation without V-Sync latency at high FPS.", "Less tearing above refresh.", "Can cause stutter/black screens on some systems.",
  [("Problems with flicker/stutter", "Off", "Known to cause issues on some setups.")], ADR)
s("amd.vsync", AMD, "Vertical synchronization", "Wait for Vertical Refresh", "Driver V-Sync override.", "Removes tearing.", "Adds latency.",
  [("FreeSync users", "Off, unless application specifies", "Use FreeSync + frame cap.")], ADR)
s("amd.freesync", AMD, "G-SYNC", "AMD FreeSync", "Variable refresh rate.", "Smooth frame delivery without tearing.", "Some monitors flicker at low FPS.",
  [("FreeSync monitors", "On (also enable in the monitor's OSD)", "Smoothness.")], "AMD Software → Gaming → Display → AMD FreeSync.")
s("amd.afmf", AMD, "3D settings", "AMD Fluid Motion Frames", "Driver frame generation.", "Higher displayed FPS.", "Adds latency and artifacts; needs a high base FPS (60+).",
  [("Single-player games with 60+ FPS", "Optional", "Smoother motion.")], ADR)
s("amd.rsr", AMD, "Display scaling", "Radeon Super Resolution", "Driver upscaling for games without FSR.", "Higher FPS at lower render resolution.", "Softer image; requires exclusive fullscreen at lower resolution.",
  [("Low-end GPUs", "On", "FPS gains.")], ADR)
s("amd.texture-quality", AMD, "Texture filtering", "Texture Filtering Quality", "Texture filtering trade-off.", "Negligible performance difference.", "Performance setting can shimmer.",
  [("All", "Standard or High", "No real gain from lower.")], ADR)
s("amd.tessellation", AMD, "3D settings", "Tessellation Mode", "Override tessellation level.", "Limiting can boost FPS in tessellation-heavy older titles.", "Lower geometric detail.",
  [("All", "AMD optimized", "Sensible default.")], ADR)
s("amd.shader-cache", AMD, "Shader caches", "Shader Cache / Reset Shader Cache", "On-disk compiled shaders.", "Less stutter.", "Corruption after updates causes stutter — reset it.",
  [("Stutter after driver update", "Reset Shader Cache", "Rebuilds cleanly.")], "AMD Software → Gaming → Graphics → Advanced → Reset Shader Cache.")
s("amd.pixel-format", AMD, "Color settings", "Pixel Format and colour depth", "RGB Full vs YCbCr and bit depth.", "RGB 4:4:4 Full gives correct PC colours.", "TVs may need Limited range.",
  [("PC monitors", "RGB 4:4:4 Full RGB", "Fixes washed-out colours.")], "AMD Software → Gaming → Display → Pixel Format.")
s("amd.gpu-scaling", AMD, "Display scaling", "GPU Scaling / Integer Scaling", "Where non-native resolutions are scaled.", "Consistent scaling; integer for pixel art.", "Minor latency differences.",
  [("Stretched resolutions", "GPU Scaling: Full panel", "Predictable.")], "AMD Software → Gaming → Display.")
s("amd.sam", AMD, "3D settings", "Smart Access Memory (Resizable BAR)", "Lets the CPU access all VRAM at once.", "A few percent FPS in many games.", "Requires Above 4G Decoding and Resizable BAR in BIOS + UEFI/CSM off.",
  [("Supported Ryzen + Radeon systems", "On", "Free performance.")], "BIOS (Resizable BAR) and AMD Software → Performance → Tuning.")
s("amd.tuning", AMD, "Power management", "Performance tuning (undervolt/overclock)", "GPU clock/voltage/fan/power limit tuning.", "Can lower temperatures or raise performance.", "Unstable tuning causes driver timeouts and crashes.",
  [("Crashes/timeouts", "Reset tuning to default", "Common cause of instability.")], "AMD Software → Performance → Tuning.")

# ------------------------------------------------------------------ Intel
INTEL = "Intel"
IGS = "Intel Graphics Software (or Arc Control) → Graphics."
s("intel.low-latency", INTEL, "Low latency", "Intel XeLL / Low latency mode", "Reduces render queue latency.", "Lower input latency.", "Supported games only (XeLL is integrated per game).",
  [("Competitive games", "On where available", "Latency.")], IGS)
s("intel.frame-limiter", INTEL, "Frame-rate limits", "Frame Rate Limiter", "Driver FPS cap.", "Power/heat savings.", "Prefer in-game caps.",
  [("Laptops", "Cap to refresh rate", "Battery life.")], IGS)
s("intel.sync", INTEL, "Vertical synchronization", "Vertical Sync (Smooth Sync / Speed Sync)", "Sync modes; Smooth Sync dithers tear lines.", "Less visible tearing.", "Speed Sync adds variability.",
  [("Most users", "Application choice", "Game decides.")], IGS)
s("intel.adaptive-sync", INTEL, "G-SYNC", "Adaptive Sync", "Variable refresh rate on supported displays.", "Smooth frame delivery.", "Monitor must support VRR.",
  [("VRR monitors", "On", "Smoothness.")], IGS)
s("intel.panel-self-refresh", INTEL, "Power management", "Panel Self Refresh / Display Power Saving", "Laptop panel power-saving features.", "Better battery life.", "A common cause of laptop screen flicker.",
  [("Laptop screen flickers", "Turn Panel Self Refresh off", "Known flicker fix.")], "Intel Graphics Software → System → Power (laptop), or the laptop maker's utility.")
s("intel.endurance-gaming", INTEL, "Power management", "Endurance Gaming", "Caps FPS and power on battery.", "Longer gaming on battery.", "Lower FPS.",
  [("Laptop gaming on battery", "On", "Battery life.")], IGS)
s("intel.sharpening", INTEL, "3D settings", "Image sharpening", "Driver sharpening filter.", "Crisper image.", "Halo artifacts if too strong.", [("TAA-blurry games", "Light sharpening", "Detail.")], IGS)

out = os.path.join(os.path.dirname(__file__), "..", "..", "src", "SoftReset.Core", "Knowledge", "Data", "gpu.json")
with open(out, "w", encoding="utf-8") as f:
    json.dump(G, f, indent=1, ensure_ascii=False)
    f.write("\n")
print(f"Wrote {len(G)} GPU settings ({sum(1 for g in G if 'drsSettingId' in g)} readable via NVAPI DRS)")

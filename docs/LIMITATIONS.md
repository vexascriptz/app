# Known limitations and what still needs validation on Windows

This document is deliberately specific. "Verified" below means an automated test or a recorded run
exercised the behaviour; anything else is listed as unverified.

## How the console was verified

The console was developed in a Linux environment with **no access to a Windows 10/11 desktop**.
Verification therefore happened in three layers:

1. **Linux, simulated PC** — 136 core tests and 10 headless UI tests drive the real engine and the real
   application window against a simulated Windows PC. These prove the logic, the safety flow (preview →
   approval → backup → apply → verify → rollback), and that every page and button works. They do not
   prove that the Windows APIs behave as expected.
2. **Real Windows, GitHub Actions `windows-latest`** — the [Windows build and validation workflow](../.github/workflows/windows-validation.yml)
   builds the solution, runs all test suites including 16 Windows-only tests against the real OS
   (registry, WMI, event logs, signatures, services, Task Scheduler, power plans, settings catalogue,
   every action's inspection), publishes the single-file executable, and runs that executable's
   `--self-test` and `--capture-pages` modes. See [TESTING.md](TESTING.md) for the recorded results.
3. **Not yet done: a real customer-style Windows 10 and Windows 11 PC.** The CI runner is a Windows
   Server virtual machine without a GPU, a physical monitor, a battery, consumer Store apps, or a
   standard (non-admin) user with UAC prompts. The items below need checking on real hardware.

## Needs validation on a real Windows 10/11 PC

| Area | Why it is unverified | How to check |
|---|---|---|
| UAC elevation flow | CI runs as an elevated administrator, so the admin prompt when the app opens, and the `--elevated-run` path (and declining either) have only been tested in simulation. | Open the app normally: Windows should ask for admin rights; say Yes once and No once (the banner should appear). As a standard user, apply an admin action (e.g. *Hibernation*). |
| Temperature and health labels on real sensors | The runner has no GPU or NVMe sensors, so GPU/drive temperatures and their labels have only been checked on the demo PC. | Open Home on a gaming PC and compare the GPU and drive temperatures with Task Manager / the drive maker's tool. |
| NVIDIA NVAPI driver-settings read and NVML telemetry | The CI runner has no NVIDIA GPU. Structure sizes were checked against NVIDIA's headers, but the probe has not run against a real driver. | On an NVIDIA PC: *Graphics → NVIDIA → Read driver settings*. Compare a few values with the NVIDIA Control Panel. |
| AMD/Intel GPU detection and D3DKMT telemetry | No GPU on the runner. | Open *Graphics* on AMD and Intel machines. |
| Display refresh-rate change and auto-revert | The runner has no physical display with multiple modes. | On a high-refresh monitor running at 60 Hz: *Use 144 Hz…*, let the 15-second countdown expire, confirm it reverts; repeat and keep. |
| Consumer settings (Game Mode, HAGS, Game Bar, notifications, Store-only settings) | Windows Server lacks several consumer features; those settings show as not applicable there. | `--self-test` on Windows 10 22H2 and Windows 11 23H2/24H2, Home and Pro. Review `settings.json` in the output. |
| Store app inventory, repair/reset links, Store app launching | Store apps are largely absent on the Server image. | *Apps* page on a consumer PC. |
| Battery/laptop power settings, Modern Standby | No battery on the runner. | `--self-test` on a laptop. |
| SFC, DISM, CHKDSK scan, Windows Update reset, restore points | Long-running repair tools are inspected but not executed in CI (they take many minutes and change the runner). | Run each from *Speed up & fix* on a test PC. |
| File organizer on OneDrive-synced folders | Cloud placeholders are detected by attribute and skipped; not tested against a live OneDrive account. | Analyse a OneDrive-backed Desktop with "Files On-Demand" enabled. |
| High-DPI and multi-monitor layout | Captured at 100 % scaling only. | Run at 150 % and 200 % scaling. |

## Known limitations (by design or not yet built)

- **No CPU temperature.** Windows has no supported way to read the CPU's own temperature without installing a kernel driver, and this app doesn't install drivers. The check-up says "Not reported" instead of guessing; a motherboard sensor is shown (marked approximate) when Windows exposes one. GPU temperatures come from the graphics driver, drive temperatures from Windows' storage counters (admin rights needed).
- **Labels are general guidance.** Good/Warm/Hot thresholds follow common manufacturer guidance, not each exact model's limits. "Not signed" means only that — it is never treated as malware.

- **Not code-signed.** SmartScreen will warn on first run. Sign `SoftResetConsole.exe` with an
  Authenticode certificate (e.g. `signtool sign /fd SHA256 /a /tr http://timestamp.digicert.com /td SHA256 SoftResetConsole.exe`)
  to remove the warning.
- **NVIDIA/AMD/Intel driver settings are read-only.** Writing them would mean writing to the driver's
  profile database; the console opens the vendor's own software instead. AMD and Intel offer no public
  read interface, so their settings are explained but current values are not shown.
- **Sysinternals tools are not bundled** (Microsoft's licence forbids redistribution). Put
  `procexp64.exe`, `autoruns64.exe`, `tcpview64.exe` in `tools\sysinternals` next to the console to
  launch them from the *Running programs* page.
- **Settings Windows doesn't expose programmatically** (84 of 245 catalogue entries — for example
  Bluetooth pairing, printer management, account sign-in options) show a description and open the
  correct Windows page; they don't show a current value.
- **Group Policy:** the console reads policy values that override settings and reports them, but does
  not edit policies.
- **Rollback limits:** deleted temporary files, emptied Recycle Bin, cleared caches, component-store
  cleanup, and repair tools cannot be undone; they are labelled *Not reversible* before approval.
  Restore points depend on System Protection being enabled.
- **CPU/temperature sensors:** CPU temperature is only available where Windows exposes ACPI thermal
  zones; many desktops don't. GPU temperature needs NVML (NVIDIA) or is otherwise unavailable.
- **Health score** is indicative only (it is derived from the findings, not a benchmark).
- **English only.**
- **x64 only** is published; an ARM64 build is configured (`-r win-arm64`) but untested.

# Testing and results

## Test suites

| Suite | Tests | Runs on | What it covers |
|---|---|---|---|
| `tests/SoftReset.Core.Tests` | 181 | Linux and Windows | Auto optimize planning (power, graphics, startup, cleanup, settings; visuals reduced only on weak hardware; running it twice changes nothing); every diagnostic rule against fixtures; every optimization action's apply → verify → rollback against the simulated PC (including admin actions through the elevated helper, UAC decline, failure part-way, interrupted changes); temp cleanup keeps anything new to the folder; file organizer apply/undo/never-overwrite; knowledge-base integrity (all 245 settings have all fields, safe controls, valid read specs; processes; app guides; GPU settings); PDF structure; output parsers; sessions; storage. |
| `tests/SoftReset.App.Tests` | 40 | Linux and Windows | The real application window rendered headlessly and driven with mouse clicks and keyboard input: admin prompt at start-up (rules, banner, setting); Home check-up labels every reading and Fix turns a line Good; labels on Graphics, Running programs, Windows settings, Speed up & fix, Find problems; Auto optimize from Home (one permission prompt, Undo all), running it twice finds nothing, Graphics-only auto, every setting has Apply/Open and Apply works, every problem has Fix/Open and Fix all rescans; every page opens; approve → apply → verify → undo; declining approval; declined UAC; settings change undone from Undo changes; display change auto-revert; file organizer apply and undo; typed confirmation for data removal; protected processes. Also produces the screenshots in `docs/screenshots`. |
| `tests/SoftReset.Windows.Tests` | 18 | Windows only (skipped elsewhere) | Real registry, WMI, event logs, signatures, services, Task Scheduler, startup, installed apps, power, displays, network, GPU, live metrics; all 35 rules on real data; all 245 settings read; every action inspected and previewed on real Windows without changing anything. Four tests change the real machine and restore it (Explorer option, power plan, DNS flush, and a full Auto optimize → run again finds nothing → Undo all) — they run only with `SOFTRESET_ALLOW_SYSTEM_CHANGES=1`, in their own CI step, one test at a time. |
| `SoftResetConsole.exe --self-test` | 15 checks | Windows | The published executable's own read-only validation, including the PC check-up labels on real data (`checkup.json`). |
| `SoftResetConsole.exe --capture-pages` | every page | Windows | The published executable opens every page against real data and saves screenshots. |

Run locally:

```bash
dotnet test SoftReset.sln
```

## Recorded results

### Version 1.1.0 (Auto optimize) — real Windows, GitHub Actions run 13

[Run 44995986416](https://github.com/vexascriptz/app/actions/runs/44995986416) on `windows-latest`
(Windows Server 2025 Datacenter 24H2, build 26100, elevated, Hyper-V VM, no GPU, no battery):

- Core: **152 passed**. UI: **16 passed**. Real Windows: **18 passed, 0 skipped**.
- The real-Windows Auto optimize test built a plan from the actual machine, applied it (display
  changes left out — the VM has one fixed mode), confirmed nothing failed, ran Auto optimize again and
  got **0 changes**, then ran **Undo all** and checked the power plan and every recommended setting were
  back to their original values.
- `--self-test`: **all 14 checks passed** (health 75, same 4 findings as run 2 — correct for a CI VM).
- `--capture-pages`: every page opened and was captured (exit code 0).
- Linux (simulated PC): Core **152 passed**, UI **16 passed**, real-Windows suite **18 skipped**.

What earlier 1.1.0 runs found and what was fixed:

- *Restore point failed with "Not found"* on Windows Server, which has no System Restore. The app now
  checks System Restore is available and switched on before offering a restore point, and says so plainly
  if it isn't.
- *A UI test's files vanished mid-test* (runs 9–11). Cause: the real Auto optimize test cleaned `%TEMP%`
  at the same time, and the simulated PC's files (dated months back) lived there. That showed a real
  risk: an archive just unzipped into `%TEMP%` keeps its old file dates, so it could have been deleted
  while in use. Temp cleanup now also keeps anything **created** in the last day, and skips whole folders
  created in the last day. Test sandboxes moved out of `%TEMP%`; tests that change the real machine run in
  their own CI step.

### Version 1.0.1 (simplified interface, customer session removed)

- Linux: Core **136 passed**, UI **10 passed**, Windows-only **16 skipped** (not counted as passing).
- Windows: see the Release workflow run for 1.0.1 — it runs every suite and the exe's self-test before publishing.

### Linux development environment (simulated PC), version 1.0.0

- Core: **136 passed**, 0 failed.
- UI: **11 passed**, 0 failed.
- Windows-only: **16 skipped** (reported as skipped — not counted as passing).
- The actual application binary was also launched under a virtual X display in `--demo --capture-pages`
  mode; all 11 pages opened and rendered (exit code 0).

### Real Windows — GitHub Actions run 1

[Run 44774306587](https://github.com/vexascriptz/app/actions/runs/44774306587) on `windows-latest`
(Windows Server 2025 Datacenter 24H2, build 26100, elevated administrator, Hyper-V VM, no GPU):

- Build: succeeded.
- Core: **135 passed**. UI: **11 passed**. Windows-only: **16 passed, 0 skipped** (including the three
  tests that change real settings and roll them back: Explorer option, power plan, DNS flush).
- Publish: single-file `SoftResetConsole.exe` produced.
- `--self-test` of the published exe: **all 14 checks passed**. Recorded output:

```
[PASS] Create platform: Windows, build 26100, elevated=True
[PASS] Collect snapshot (7s): Windows Server 2025 Datacenter 24H2 (build 26100.33828); CPU AMD EPYC 9V74 80-Core Processor;
       RAM 16 GB; 151 processes; 5 startup entries; 5 events; 1 GPU(s); 1 display(s); 2 disk(s); 1 collection note(s) in 6.8s
[PASS] Diagnostics: 35 rules, 5 findings, health 66: [High] Real-time protection is off; [High] No active network connection
       with a gateway; [Medium] Unknown device: not working; [Medium] UAC is set to never notify; [Low] 1 service(s) repeatedly failed
[PASS] Settings catalog read (7.36s): 245 settings: 153 readable, 8 not applicable to this PC
[PASS] Process details and signatures: 152 processes, 148 with paths; signatures: SignedMicrosoftWindows=31, Unsigned=2, Signed=7;
       notepad.exe=SignedMicrosoftWindows; explorer.exe=SignedMicrosoftWindows (Microsoft Windows)
[PASS] Installed applications (7.86s): 99 apps (39 Store, 60 desktop), 11 with guides; 319 optional features
[PASS] Graphics: Microsoft Hyper-V Video [Microsoft] driver 10.0.26100.1150 temp n/a | NVIDIA DRS: NVAPI is not available
       (no NVIDIA driver installed). (0 settings) | NVCP: none
[PASS] Displays: \\.\DISPLAY1 HyperVMonitor 1024x768@64 (max 64) on Microsoft Hyper-V Video, 13 modes
[PASS] Power: High performance (3 plans), AC=True, max CPU 100%, hibernation=False, modern standby=False
[PASS] Services and tasks: 259 services; 210 scheduled tasks
[PASS] Network: 14 adapters (11 up), proxy=False, hosts entries=1
[PASS] Live metrics: CPU 2.3%, memory 26.9%
[PASS] Optimization actions: Inspect (read-only): 39 actions inspected without errors
```

- `--capture-pages` of the published exe: **all 11 pages opened and were captured** (exit code 0).

**What run 1 found, and what was fixed.** Reviewing the findings against the runner's actual state:

- *Real-time protection is off*, *UAC is set to never notify*, *Unknown device*, *service failures*:
  correct for a CI virtual machine.
- *No active network connection with a gateway*: **a false positive.** The runner's network card is a
  Hyper-V adapter, which the console classed as "virtual" and ignored. The same mistake would have
  affected real PCs whose connection runs through a Hyper-V external switch (`vEthernet`, common with
  WSL/Docker). The rule now accepts any connected adapter with a default gateway and is suppressed when
  a connectivity test confirms internet access; a regression test was added
  (`Network_RouteThroughVirtualAdapter_IsNotReportedAsDisconnected`).
- *Disk reliability* note said counters "require administrator rights" although the process was
  elevated. It now says the disk/driver doesn't report them when the scan is already elevated.

The results of the run that includes these fixes are recorded below.

### Real Windows — GitHub Actions run 2 (with the fixes)

[Run 44775300558](https://github.com/vexascriptz/app/actions/runs/44775300558), same runner image:

- Core: **136 passed** (including the new regression test). UI: **11 passed**. Windows-only: **16 passed, 0 skipped**.
- `--self-test`: **all 14 checks passed**. The network false positive is gone and the disk note is accurate:

```
[PASS] Collection issues (informational): Disk reliability: This disk or its driver does not report wear and temperature
       counters (common for virtual disks, some RAID and USB controllers).
[PASS] Diagnostics: 35 rules, 4 findings, health 75: [High] Real-time protection is off; [Medium] Unknown device: not working;
       [Medium] UAC is set to never notify; [Low] 1 service(s) repeatedly failed
```

- `--capture-pages`: **all 11 pages opened and were captured** (exit code 0).

The screenshots captured on Windows, the self-test output files (`snapshot.json`, `diagnostics.json`,
`settings.json`, `diagnostic-report.pdf`), and the `.trx` test results are in each run's
`windows-validation` artifact. Every later push re-runs the same workflow.

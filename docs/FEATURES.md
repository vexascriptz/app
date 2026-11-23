# Feature inventory and verification status

Legend for the **Verified by** column:

- **Core** — automated test in `tests/SoftReset.Core.Tests` (engine against the simulated PC or fixtures)
- **UI** — automated test in `tests/SoftReset.App.Tests` that drives the real application window with
  headless mouse/keyboard input against the simulated PC
- **Win** — automated test in `tests/SoftReset.Windows.Tests` against the real Windows APIs on the
  GitHub Actions `windows-latest` runner
- **Self-test** — the published `SoftResetConsole.exe --self-test` run on that runner
- **Capture** — the published `SoftResetConsole.exe --capture-pages` run on that runner
- **Not yet** — implemented, but not exercised on real Windows hardware (see [LIMITATIONS.md](LIMITATIONS.md))

## Application shell

| Feature | Verified by |
|---|---|
| Single-file portable executable, runtime bundled, no installer | Publish step in CI; launched by Self-test and Capture |
| Asks for admin rights as soon as it opens (UAC); saying No opens it normally with a "Give admin rights" banner; never asks twice; can be turned off in About; `--no-admin` skips it | UI (`AdminStartupTests`: decision rules, argument quoting, banner, setting); real UAC prompt: **Not yet** (CI is already admin) |
| Without admin, per-action UAC elevation through `--elevated-run` | Core (`AdminAction_RunsThroughElevatedHelper…`, `UacDeclined_MakesNoChange…`), UI (`Declined_UAC_prompt…`); real UAC prompt: **Not yet** |
| Plain labels next to every reading (Good / Warm / Hot / Low / Problem / Unknown) with a one-line meaning: Home check-up (temperatures, drives, startup apps, power plan, monitors, driver age, antivirus, firewall, updates, last restart, internet, Wi-Fi, battery), live CPU/memory, Graphics, Running programs, Windows settings, Speed up & fix, Find problems | Core (`RatingsTests`), UI (`CheckupTests`, `LabelsTests`) |
| Portable data folder next to the exe, fallback to `%LOCALAPPDATA%` | Core (`AppPaths_PortableWhenWritable_FallbackOtherwise`) |
| Interrupted changes detected at next start | Core (`InterruptedRecords_AreRecoveredAtStartup`, `Journal_SkipsCorruptRecords`) |
| All 10 pages open and render a real view | UI (`Every_navigation_item_opens_its_page`, `Every_page_renders_with_data`), Capture |
| Delete all saved data with typed confirmation | UI (`Removing_all_data_requires_typing_DELETE`) |
| Demo mode with a simulated customer PC | UI (all), launched under a virtual display |

## Find problems

| Feature | Verified by |
|---|---|
| Snapshot collection from real Windows (OS, hardware, performance, drives, processes, startup, events, devices, GPUs, displays, power, network, security, updates, gaming config, cleanup sizes) | Win (`Snapshot_collection_reads_real_hardware_and_os_data`), Self-test |
| 35 rules with evidence, causes, confidence, uncertainties, recommendations | Core (30+ rule fixture tests), Win (`Diagnostic_rules_run_on_real_data_without_errors`), Self-test |
| A failing rule is reported, not fatal; findings without evidence are rejected | Core |
| Suspicious-process findings never claim malware | Core (`SuspiciousProcessFinding_NeverClaimsMalware`), UI |
| Indicative health score | Core |
| Diagnostic PDF export | Core, Self-test (writes `diagnostic-report.pdf`) |

## Running programs

| Feature | Verified by |
|---|---|
| Process list with CPU, memory, publisher, signature, service/startup association | UI, Win (`Processes_services_and_tasks_enumerate`) |
| Authenticode verification (embedded and catalog) | Win (`Windows_binaries_verify_as_microsoft_signed`, `Unsigned_files_are_reported_unsigned_not_trusted`), Self-test |
| 139-process knowledge base | Core (`Processes_AreUniqueAndComplete`, `Processes_CriticalOnesMatchProtectedList`) |
| Investigation indicators (look-alike names, unsigned in temp/AppData roots) — no malware verdicts | Core (`ProcessHeuristics_FlagsMimicsAndLookalikes_NotNormalApps`), UI |
| Critical processes cannot be ended | UI (`Protected_processes_cannot_be_ended…`), Core (`EndProcesses_SkipsProtectedProcesses`) |
| Sysinternals launch when present, official page otherwise | Manual code path; **Not yet** on Windows |

## Windows settings

| Feature | Verified by |
|---|---|
| 245 settings, 30 categories, all 11 fields per entry | Core (`Settings_CoverEveryRequiredCategory`, `Settings_HaveAllElevenFields`) |
| Availability by Windows build and edition | Core, Win (`Every_catalogued_setting_reads_without_errors`) |
| Current values from registry/service/task/API probes, policy overrides noted | Win, Self-test (writes `settings.json`) |
| 100 writable settings via backed-up, reversible changes | Core (`SettingRegistrySet_ValidatesCatalogAndRollsBack`), UI (`Settings_explorer_write_is_rolled_back_from_change_history`) |
| Security settings are never write controls; HKLM writes require admin | Core |
| Open-the-right-page controls for settings Windows doesn't expose | Core (`Settings_ControlsAreSafeAndResolvable`) |

## Graphics

| Feature | Verified by |
|---|---|
| GPU list, driver version/date, vendor software detection | Win (`Power_display_network_and_gpu_read`), Self-test |
| NVML telemetry (NVIDIA) and D3DKMT data | Simulated in UI; real GPU: **Not yet** |
| NVAPI driver-settings read (out of process), 21 DRS settings, profiles | Core (`GpuSettings_DrsIdsParseAndValuesDescribe`), UI (sim); real driver: **Not yet** |
| 54 explained driver settings (NVIDIA/AMD/Intel) | Core |
| Display modes and refresh-rate change with 15 s keep-or-revert | Core (`RefreshRate_ChangesAndRestoresMode`, `RefreshRate_UnsupportedModeFailsWithoutChange`), UI (`Refresh_rate_change_reverts…`); real monitor: **Not yet** |
| HAGS and per-app GPU preference (Windows settings) | Core |

## Apps

| Feature | Verified by |
|---|---|
| Desktop programs (Uninstall keys), Store apps, optional features | Core (`UninstallReader_FiltersAndDetectsMsi`), Win (`Startup_inventory_and_installed_apps_read`), Self-test |
| 53 app guides matched to installed apps | Core (`AppGuides_AreCompleteAndMatchInstalledApps`) |
| Cache clearing only in documented cache folders | Core (`ClearAppCache_EmptiesDocumentedFoldersOnly`, `AppGuide_CachesNeverPointAtProfilesOrSettings`) |
| Uninstall only via the app's own uninstaller; essential components protected | UI page; code path verified in sim; real uninstaller launch: **Not yet** |
| winget upgrade of selected apps | Core (`Winget_ListsAndUpgradesSelected`, parser tests); real winget: **Not yet** |

## Speed up & fix (40 fixes, plus 3 used by Windows settings)

| Category | Actions | Verified by |
|---|---|---|
| Startup | Startup apps (this user / all users), third-party sign-in tasks | Core (`StartupItems_DisableThenRollback`, `StartupFolderEntry_CanBeDisabled`, `LogonTasks_DisableAndRollback`) |
| Storage cleanup | User temp, Windows temp, Recycle Bin, Update cache, component cleanup | Core (`UserTempCleanup_DeletesOnlyOldFiles`, `FileCleaner_DoesNotFollowSymlinks`) |
| Background activity | End selected processes, service start type, scheduled tasks | Core (`ServiceStartMode_RefusesToDisableCriticalService`, `ServiceStartMode_ThirdPartyServiceRoundTrip`, `TaskEnabled_ProtectsWindowsTasks`) |
| Power | Power plan, max processor state, USB selective suspend, hibernation, Fast Startup | Core, UI (`Approved_change_is_applied_verified_and_can_be_undone`), **Win** (`Power_plan_change_is_verified_and_rolled_back_on_real_windows`) |
| Gaming | Game Mode, background recording, HAGS, windowed optimizations, GPU preference | Core (`WindowedOptimizations_EditsOnlyItsKey`) |
| Display | Refresh rate, visual effects, mouse acceleration | Core (`VisualEffects_PerformanceThenRestore`), UI |
| Windows maintenance | Restore point, SFC, DISM, CHKDSK scan, Windows Update reset | Core (`ResetWindowsUpdate_RenamesAndRollbackRestores`); real runs: **Not yet** |
| App maintenance | Clear app cache, winget upgrade | Core |
| Network | Flush DNS, renew DHCP, reset Winsock/TCP-IP, disable manual proxy | Core, **Win** (`Flushing_the_dns_cache_runs_the_real_command`) |
| Desktop | File extensions, hidden files, taskbar alignment, dark mode, transparency, Explorer start folder, restart Explorer | Core, **Win** (`Explorer_option_is_applied_verified_and_rolled_back_on_real_windows`) |
| All | Every action inspects and previews without changing anything on real Windows | **Win** (`Every_optimization_action_inspects_without_changing_anything`), Self-test |
| All | Honest metadata (benefit, risk, reversibility, measurement) | Core (`ActionCatalog_EveryActionHasHonestMetadata`) |
| All | Failure part-way keeps backups; partial rollback works | Core (`FailureMidway_KeepsBackups_AndPartialRollbackWorks`) |

## Organize files

| Feature | Verified by |
|---|---|
| Folder analysis by category, largest files, suggestions | Core, UI |
| Byte-for-byte duplicates (full hash for large files) | Core (`Duplicates_FoundByContentNotName`, `Duplicates_LargeFilesUseFullHash`) |
| Preview, approval, apply, undo — never deletes or overwrites | Core (`Execute_ThenUndo_RestoresEveryFile`, `Execute_NeverOverwrites`, `Undo_RefusesToOverwriteNewFileAtOriginalLocation`), UI (`File_organizer_moves_only_after_approval…`) |
| Skips recent files, shortcuts, cloud placeholders; refuses system folders | Core |

## Undo changes and safety

| Feature | Verified by |
|---|---|
| Write-ahead change journal with typed backups | Core |
| Registry backup restores value and absence exactly, all value kinds | Core (`RegistryBackup_RoundTripsAllKinds`), **Win** (`Registry_backup_restores_previous_value_and_previous_absence`, `Registry_round_trips_every_value_kind…`) |
| Undo from the Undo changes page | UI |
| Approval required; declining changes nothing | UI (`Declining_approval_changes_nothing`) |

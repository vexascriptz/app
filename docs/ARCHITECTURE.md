# Architecture

## Technology choice

The specification preferred C#/.NET WPF "unless a better approach" was identified. The console uses
**C# on .NET 10 (LTS) with Avalonia 11** instead of WPF. The application is still a native Windows
desktop program (a single PE32+ GUI executable using Win32, WMI, the registry, WinVerifyTrust,
Task Scheduler COM, powrprof, NVML/NVAPI and so on), not a web view. Avalonia was chosen because:

- the complete UI — not only the view models — can be driven by automated tests with a headless
  renderer, including real mouse clicks and keyboard input, and rendered to screenshots. That made it
  possible to verify every page and every approval/rollback flow without a Windows machine, and to
  keep doing so in CI;
- it publishes to a single self-contained executable with the runtime bundled, which is what a USB
  toolkit needs;
- it has first-class compiled bindings (binding errors are build errors).

## Solution layout

| Project | Target | Purpose |
|---|---|---|
| `src/SoftReset.Core` | net10.0 | Everything that is not Windows-specific: snapshot model, platform abstractions, diagnostic engine and rules, optimization framework and actions, change journal and backups, knowledge bases (embedded JSON), settings reader, file organizer, PDF writer and reports, parsers for `powercfg`/`netsh`/`nvidia-smi`/`winget` output. |
| `src/SoftReset.Platform.Windows` | net10.0-windows | The real implementation of the abstractions: registry, WMI/CIM, event logs, performance data, processes and signatures, services, Task Scheduler, power (powrprof), displays (EnumDisplaySettingsEx/ChangeDisplaySettingsEx), GPU (NVML, NVAPI DRS, D3DKMT), network, installed apps (Uninstall keys, WinRT PackageManager), shell launching, elevation. |
| `src/SoftReset.Platform.Simulated` | net10.0 | A complete simulated PC (in-memory registry, processes, services, events, displays, GPUs, files in a sandbox folder) seeded with a realistic gaming-desktop scenario. Used by `--demo`, by all UI tests, and by the core integration tests. |
| `src/SoftReset.App` | net10.0-windows | The Avalonia application: shell, pages, dialogs, view models, the action runner, self-test, and page capture. |
| `tests/SoftReset.Core.Tests` | net10.0 | Unit and integration tests for the engine. |
| `tests/SoftReset.App.Tests` | net10.0-windows | Headless UI tests of the real window. |
| `tests/SoftReset.Windows.Tests` | net10.0-windows | Tests against the real Windows APIs (skipped elsewhere). |
| `build/kb/*.py`, `build/icon/*.py` | — | Generators for the knowledge-base JSON files and the icon. |

## Key flows

**Diagnosis.** `ISnapshotCollector.CollectAsync` gathers a `SystemSnapshot` (each area is isolated: a
failure is recorded as a collection issue, not a crash). `DiagnosticEngine.Analyze` runs every rule;
each finding carries evidence (value + source), likely causes, a confidence level with its reason,
uncertainties, and recommendations that may reference an optimization action. Rule exceptions are
captured as rule errors rather than aborting the scan.

**Changing something.** All changes go through `ActionRunner` (UI) → `ActionExecutor` (core):

1. `InspectAsync` — read-only; returns current state, options, items, or "not applicable".
2. `PreviewAsync` — read-only; returns every target with its from → to values, warnings, admin/restart
   needs, and reversibility.
3. Approval dialog — the technician confirms the customer approves.
4. A `ChangeRecord` is written to the journal *before* anything changes.
5. If the action needs administrator rights and the console isn't elevated, the same executable is
   started through UAC with `--elevated-run`, performs only that request, and writes the result to the
   journal.
6. `ApplyAsync` captures backups (each persisted immediately) and applies.
7. `VerifyAsync` re-reads the system and confirms the result.
8. `RollbackAsync` restores the captured backups in reverse order.

Backups are typed (`registry-value`, `power-active-scheme`, `power-setting`, `hibernation`,
`service-start`, `service-running`, `scheduled-task`, `display-mode`, `ui-effect`,
`mouse-acceleration`, `folder-rename`, `note`).

**NVIDIA driver settings.** The NVAPI driver-settings database is read in a separate process
(`--probe nvapi-drs`) so that a driver problem cannot crash the console. Only documented, public NVAPI
read calls are used, with structure layouts from NVIDIA's MIT-licensed headers. Nothing is written.

## Data layout

```
softreset-data/
  .softreset-data       marker
  settings.json
  reports/              saved PDF reports
  scans/                diagnostic scans
  journal/              one JSON file per change record
  backups/              file-based backups
  file-operations/      file organizer logs (used for undo)
  logs/
```

## Building

```bash
dotnet build SoftReset.sln
```

```bash
dotnet test SoftReset.sln
```

```bash
dotnet publish src/SoftReset.App/SoftReset.App.csproj -c Release -r win-x64 -o dist/SoftResetConsole
```

Regenerate the knowledge bases after editing a generator:

```bash
python3 build/kb/settings_catalog.py
```

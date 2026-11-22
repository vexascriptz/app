Portable Windows 10/11 technician console for PC service work: diagnose with real measurements, explain findings, get the customer's approval for each fix, apply it with a backup, measure the result, and produce a PDF report. No installer, account, subscription, internet connection, background service, or startup entry.

## Download

| File | What it is |
|---|---|
| `SoftResetConsole-VERSION-win-x64.zip` | **Recommended.** `SoftResetConsole.exe` plus `START-HERE.txt`. |
| `SoftResetConsole.exe` | The program on its own (about 56 MB, .NET runtime bundled). |
| `SHA256SUMS.txt` | Checksums to verify the downloads. |

**To run:** unzip anywhere (USB drive, Desktop, `C:\Tools`) and double-click `SoftResetConsole.exe`. It starts with standard rights; Windows asks for administrator approval (UAC) only for the specific changes that need it. Records are saved in a `softreset-data` folder next to the exe.

**SmartScreen:** the exe is not code-signed, so Windows may say the publisher is unknown. Click **More info → Run anyway**.

**Try it safely:** `SoftResetConsole.exe --demo` runs against a built-in simulated PC and changes nothing real.

## What's included

- **Customer session** — 10 guided steps from concerns to a before/after PDF report, with recorded customer approval.
- **Diagnostics** — read-only scan; 35 checks, each finding with evidence, confidence, and recommendations.
- **Background processes** — publisher, signature, CPU/memory, startup association, knowledge of 139 common processes. Never labels anything as malware.
- **Settings explorer** — 245 Windows settings in 30 categories; 100 can be changed and undone in place.
- **Graphics & NVIDIA** — GPUs, displays, live telemetry, 54 explained driver settings (NVIDIA values read through NVAPI, read-only).
- **Applications** — installed programs, Store apps, features; 53 detailed app guides; uninstall only via the app's own uninstaller.
- **Optimization** — 40 actions; every one previewed, approved, backed up, verified, and undoable where possible.
- **File organizer** — duplicates and reorganisation with preview and undo; never deletes.
- **Change history** — every change with its backup and one-click rollback.

## Validation status

This build was compiled, tested, and self-tested on a GitHub Actions Windows runner (Windows Server 2025) before being published: all core, UI, and Windows-only test suites passed, and the published exe's `--self-test` passed. It has **not yet been validated on a consumer Windows 10/11 PC** with a GPU, a high-refresh monitor, a battery, or a standard user account with UAC prompts. See [Known limitations](https://github.com/REPOSITORY/blob/COMMIT/docs/LIMITATIONS.md) for what to check.

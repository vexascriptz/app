A portable Windows 10/11 tool for fixing PCs. No install, no account, no internet needed.

## Download

- **`SoftResetConsole-VERSION-win-x64.zip`** — recommended. Unzip anywhere and double-click `SoftResetConsole.exe`.
- `SoftResetConsole.exe` — the program on its own.
- `SHA256SUMS.txt` — checksums.

If Windows says "unknown publisher", click **More info → Run anyway** (the app isn't code-signed yet). To try it without touching the PC, run `SoftResetConsole.exe --demo`.

## New in 1.2

- **Asks for admin rights when it opens**, so scans see everything (like drive temperatures) and fixes don't each need their own prompt. Say No and it still works — there's a "Give admin rights" button.
- **Good / Warm / Problem labels on everything.** Home has a PC check-up: temperatures, drives, startup apps, power plan, monitors, drivers, antivirus, firewall, updates and more — each with a coloured label, one line saying what it means, and a Fix or Open button when something needs a look.

## Auto optimize

One click on Home: picks every safe speed-up this PC needs, applies them with one Windows permission prompt, and has Undo all. Keeps the PC looking the same unless the hardware is too weak.

## What's in it

- **Find problems** — scans the PC and lists what's wrong. Changes nothing.
- **Speed up & fix** — pick a fix, see what changes, apply. Most can be undone.
- **Running programs** — what's running and who made it.
- **Windows settings** — search any setting and change it.
- **Graphics** — graphics card, monitors, driver settings.
- **Apps** — fixes, repair and uninstall for installed programs.
- **Organize files** — sort a folder, find duplicates. Nothing is deleted.
- **Undo changes** — undo anything the app changed.

## Tested

Built and tested on a Windows machine (GitHub Actions, Windows Server 2025) before release. Not yet tried on a home Windows 10/11 PC with a graphics card or laptop battery — see [known limitations](https://github.com/REPOSITORY/blob/COMMIT/docs/LIMITATIONS.md).

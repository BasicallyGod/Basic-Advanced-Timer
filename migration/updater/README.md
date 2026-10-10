# One-click migration test builds

Copy this folder somewhere writable and separate from your production installation (for example, Documents\Basic Timer Testing). Keep it in the same place between updates. Windows PowerShell 5.1 is included in Windows; the application artifact is self-contained for Windows x64 and needs no separate .NET runtime.

One-time setup:

1. Install [GitHub CLI](https://cli.github.com/) and open a fresh terminal so `gh` is on PATH.
2. Run `gh auth login --hostname github.com` and sign in with access to the private `BasicallyGod/Basic-Advanced-Timer` repository. The account/token must be able to read repository contents and Actions artifacts. No token is stored by this script.
3. Double-click **Update and Launch.cmd**. A shortcut to that file also works; use Windows' Send to > Desktop (create shortcut) if desired.

The launcher bypasses execution policy for this single PowerShell process; it does not change your saved policy. Organization-enforced restrictions may still apply.

**Launch Current.cmd** launches the downloaded build offline without GitHub CLI. **Rollback and Launch.cmd** switches to the previous downloaded build, then launches it. Rollback toggles the two build records; running Update afterward selects the latest successful build again. Errors stay visible in the launcher window.

## Verified pipeline and storage

Inspected `migration` at commit `356a9ca27cad38f13cbf8f68bbc61beaf6e90bd9` on 10 October 2026:

- Workflow: `.github/workflows/avalonia-preview.yml`, **Avalonia migration preview**, triggered by migration pushes, PRs, or manual dispatch. The updater accepts successful completed push/manual runs on `migration`, excludes PR merge builds, and paginates past expired/missing artifacts. It does not select the separate PowerShell checks or PNG preview artifacts.
- Portable artifact: `BasicsAdvancedTimer-Windows-preview`. Windows publishes `Timer.Desktop.csproj` in Release, win-x64, self-contained, multiple files, to `artifacts/windows-preview/`. Uploading this directory places its contents at the artifact root; `gh run download` with one named artifact extracts directly into the requested directory ([CLI documentation](https://cli.github.com/manual/gh_run_download)). The executable is `BasicsAdvancedTimer.Preview.exe`, alongside managed assemblies, runtime/native libraries, README and `Start-Demo.cmd`.
- Latest inspected portable artifact: run `38052164486`, commit above, uploaded `2026-10-10T12:32:16Z`. The artifact had not expired at inspection. Runtime selection queries GitHub each time; this ID is not hard-coded.
- `App.axaml.cs` normally uses `migration-data` beside the executable, or accepts `--data-dir`. `--demo` is nonpersistent. This updater always passes `--data-dir` pointing to its fixed `test-install/data` directory, with quoted paths. It never imports production data automatically.
- `TimerStore.cs` persists `basics-advanced-timer.settings.json`, `Timesheets/sessions.csv`, notes, entry groups, credit positions, edit history, recovery/checkpoint state, away records, export files and backups. It uses `.migration.lock`, a write-ahead `migration-journal.json`, temporary writes and `.bak` files. Those files remain outside the version directories.
- `AutoStart.cs` uses the shared HKCU Run value `BasicsAdvancedTimer`. The new `--test-build` argument prevents the app from offering or accessing this startup setting. A workflow-generated `test-build-support.json` marks builds supporting this isolation. Older artifacts without the marker are deliberately rejected; the first usable build must include this change on `migration`.

## Update safety and rollback

Downloads are extracted into a new `test-install/staging-*` folder. The updater requires the executable, application/storage/core assemblies, runtime configuration, Windows runtime and test-mode marker; it checks the EXE's MZ signature and rejects reparse points. Validation is a structural check, not a cryptographic signature or proof that a build is bug-free.

After validation, the updater asks you to exit any running test timer through the tray menu, and checks both executable paths and the data folder lock. It never kills your application. A separate updater lock prevents concurrent updater invocations.

Before an update or rollback, the updater snapshots test data into `test-install/data-backups`. Version directories are retained under `test-install/versions`; only `state.json` is atomically replaced. Run ID, attempt, commit SHA, artifact ID and upload date are recorded. Matching installed run/commit/artifact IDs skip downloading and launch the current version. Branch, full commit, run and local build date appear in the console.

A download, validation or promotion failure leaves the current build record intact. If the network/authentication fails, use **Launch Current.cmd**. Interrupted staging directories are retained for diagnosis; the script does not automatically delete versions or data.

Rollback switches executable versions and keeps the current test data. It does **not** reverse a future application's data-schema migration. Pre-update snapshots are available for manual recovery: exit the timer, copy the affected data folder aside, then restore a chosen snapshot into `test-install/data`. Never copy data into a running timer. To test real records, copy them into the isolated test data folder while all relevant timers are closed; do not point the test app at your production data.

## Validation

Run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File Test-Updater.ps1` for offline filesystem regression tests. Downloads, GitHub requests and launches are mocked; tests cover first installation, duplicate download avoidance, partial downloads, incompatible artifacts, persistent data, rollback, offline launch, updater/data locks and state path rejection. The Windows workflow runs these tests before publishing, then smoke-tests the actual native executable with `--test-build`.

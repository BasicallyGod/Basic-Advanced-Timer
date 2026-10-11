# Development builds and updater

The Windows updater is for the **experimental** and **alpha** channels only. A Store release on `main` is installed and updated through Microsoft Store, not this updater.

## Choose a channel

1. Install [GitHub CLI](https://cli.github.com/) and run `gh auth login` once.
2. Keep a writable copy of this `migration/updater` folder on your computer.
3. Double-click **Timer Updater.cmd** and choose **experimental** for normal testing or **alpha** for unstable prototypes.
4. Choose **Update and launch**, **Launch current** or **Roll back and launch**. The command-line shortcuts default to `experimental`.

The picker opens without GitHub CLI, authentication or an internet connection. GitHub CLI is required only for **Update and launch**. A console stays alongside the picker; the selected action opens its own console for progress and prompts. Close the picker when finished.

To update an existing updater, download the `experimental` source ZIP from GitHub (**Code > Download ZIP** with `experimental` selected), extract it first, then replace the `.cmd` and `.ps1` files in your existing updater folder with the files from `migration/updater`. Keep your existing `test-install` folder in place; it holds your builds and data. Run the launcher from that same writable folder, not from inside the ZIP.

If startup fails, the console displays the error and stays open. Script errors also produce `updater-startup-error.log` beside the launcher where writable, and a message box where Windows Forms is available. If workplace policy blocks PowerShell, the console should show that policy error; ask IT to permit the updater. For a diagnostic check from a terminal in the updater folder:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File .\Timer-Updater-Window.ps1 -SmokeTest
```

This briefly shows the real picker, selects both channels and closes automatically without downloading builds or changing timer data. `Update and Launch.cmd` is the direct console shortcut and does not open the channel picker.

Every channel uses a separately installed executable, database, settings, backup history, version history and update lock:

- Experimental: `test-install/channels/experimental/{data,versions,data-backups}`.
- Alpha: `test-install/channels/alpha/{data,versions,data-backups}`.

Data is **not copied or automatically migrated between channels**. If you used an older updater, its `test-install/data`, `versions` and `state.json` remain untouched as legacy data. Back them up; migrate manually only with all timers closed. Never use a production Microsoft Store data folder for experiments.

Each successful push to `experimental` generates `BasicsAdvancedTimer-Windows-experimental`, and a push to `alpha` generates `BasicsAdvancedTimer-Windows-alpha`. The updater chooses the artifact corresponding to the selected branch, refusing pull-request merge artifacts or failed runs. The app receives `--test-build --data-dir` so it cannot open the Store database.

Updates stage new files, validate them, and save data snapshots before switching. **Rollback restores only the executable version, not database format.** If an alpha version changes the database format, restore its matching backup manually after closing the app.

The Windows workflow runs `Test-Updater.ps1` and `Test-UpdaterWindow.ps1` as offline regression tests, alongside automated desktop tests and native smoke tests. The window test runs the actual CMD launcher on Windows PowerShell 5.1 without `gh` in PATH and checks worker dispatch from a folder containing spaces, an apostrophe and an ampersand.

For release promotion and the main Microsoft Store workflow, see [../../BRANCHES.md](../../BRANCHES.md).

# Development builds and updater

The Windows updater is for the **experimental** and **alpha** channels only. A Store release on `main` is installed and updated through Microsoft Store, not this updater.

## Choose a channel

1. Install [GitHub CLI](https://cli.github.com/) and run `gh auth login` once.
2. Keep a writable copy of this `migration/updater` folder on your computer.
3. Double-click **Timer Updater.cmd** and choose **experimental** for normal testing or **alpha** for unstable prototypes.
4. Choose **Update and launch**, **Launch current** or **Roll back and launch**. The command-line shortcuts default to `experimental`.

Every channel uses a separately installed executable, database, settings, backup history, version history and update lock:

- Experimental: `test-install/channels/experimental/{data,versions,data-backups}`.
- Alpha: `test-install/channels/alpha/{data,versions,data-backups}`.

Data is **not copied or automatically migrated between channels**. If you used an older updater, its `test-install/data`, `versions` and `state.json` remain untouched as legacy data. Back them up; migrate manually only with all timers closed. Never use a production Microsoft Store data folder for experiments.

Each successful push to `experimental` generates `BasicsAdvancedTimer-Windows-experimental`, and a push to `alpha` generates `BasicsAdvancedTimer-Windows-alpha`. The updater chooses the artifact corresponding to the selected branch, refusing pull-request merge artifacts or failed runs. The app receives `--test-build --data-dir` so it cannot open the Store database.

Updates stage new files, validate them, and save data snapshots before switching. **Rollback restores only the executable version, not database format.** If an alpha version changes the database format, restore its matching backup manually after closing the app.

The Windows workflow runs `Test-Updater.ps1` as an offline regression test, alongside automated desktop tests and native smoke tests.

For release promotion and the main Microsoft Store workflow, see [../../BRANCHES.md](../../BRANCHES.md).

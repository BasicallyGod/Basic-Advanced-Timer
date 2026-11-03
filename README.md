# Basic’s Advanced Timer

An always-on-top time tracker for client accounts and weekly timesheets. It runs as a small pill that stays out of the way, and opens into a dashboard with a daily timeline, the week's hours and exports. It is written in C# with Avalonia on .NET 8 and is built and tested for Windows. The code is cross-platform, but Linux and macOS are not tested for now. The packaged Windows build needs no installer, administrator access or separately installed runtime.

The earlier PowerShell application (v2.32) has been retired. Its source remains available in Git history. See [Moving from the PowerShell version](#moving-from-the-powershell-version) before switching.

## Getting started

1. Open the latest successful run of the [Development builds workflow](https://github.com/BasicallyGod/Basic-Advanced-Timer/actions/workflows/avalonia-preview.yml) and download **BasicsAdvancedTimer-Windows-experimental** (or the alpha artifact if intentionally testing alpha).
2. Extract the whole ZIP into its own folder and run `BasicsAdvancedTimer.Preview.exe`. Keep its DLLs together.
3. For updates, download **Timer-Branch-Launcher-Windows** from the workflow run and open **Timer Branch Launcher.exe**. Choose a preview branch and click **Download and launch**. Each branch keeps its own data. See the [launcher guide](app/preview-launcher/README.md).

Run `Start-Demo.cmd` to try the app with sample accounts and history, without touching real data. Data is saved to a `migration-data` folder beside the executable, or to a folder you choose with `--data-dir`.

## What it does

- Start, stop and switch between client accounts, with Break and Lunch, from the pill or the dashboard.
- Set working hours by weekday, days off and one-off starts. Start day and Finish day work throughout the day, and the week overview measures your time against the schedule.
- Track budgets by week, month or quarter, with a ring on each account.
- Review each day on a scrollable, zoomable timeline. Drag blocks to move or resize them, add, merge and delete time, and keep notes and planned meetings with reminders. Every time edit can be undone, and the history survives a restart.
- Review the week as a chart or table of time by day, with each account's budget and pace. Export a summary and detailed CSV with decimal hours that add up exactly.
- Follow Windows light or dark by default, or choose Light or Dark. Choose decimal hours or hours and minutes, and the size of the pill.
- Start with Windows, with an app icon on the taskbar and a notification area icon. The taskbar button can be switched off to run from the tray alone. The window has no close button, so it cannot be closed by accident.

## Data

Time records are kept in `timer.db` (SQLite), with a small `basics-advanced-timer.settings.json` beside it. The database is opened exclusively, so two copies cannot write to one folder, and it is backed up daily. To move computers or make a backup, copy the data folder while the app is closed. None of these data files belongs in Git.

## Moving from the PowerShell version

The new app deliberately does not import the old CSV timesheets. If it finds `Timesheets/sessions.csv` in its data folder it says so, leaves the files alone and starts with an empty database. Keep your old `Timesheets` folder as an archive, or recover the old app from Git history if you need to run it again. It refuses to use a folder that still holds the old script, so the two never share data.

## Release channels

The repository uses `main` (Microsoft Store stable), `experimental` (everyday testing) and `alpha` (unstable prototypes). Development builds use separate portable artifacts and isolated preview data. See [BRANCHES.md](BRANCHES.md) and the [launcher guide](app/preview-launcher/README.md).

The Store build comes from `main` as an unsigned MSIX, and the Store signs and distributes it. Installs and updates for it go through the Microsoft Store, not the updater. See the [MSIX packaging guide](app/store-packaging/README.md) for Microsoft Partner Center instructions.

## Building

```sh
dotnet run --project app/src/Timer.Desktop/Timer.Desktop.csproj
dotnet test app/tests/Timer.Desktop.Tests/Timer.Desktop.Tests.csproj --configuration Release
```

The application, tests, packaging and updater live under `app/`. The [documentation index](docs/README.md) links to the current guides, design notes and historical archive. See [CHANGELOG.md](CHANGELOG.md) for build history.

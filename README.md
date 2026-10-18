# Basic’s Advanced Timer

An always-on-top time tracker for client accounts and weekly timesheets. It runs as a small pill that stays out of the way, and opens into a dashboard with a daily timeline, the week's hours and exports. It is written in C# with Avalonia on .NET 8 and is built and tested for Windows. The code is cross-platform, but Linux and macOS are not tested for now. The packaged Windows build needs no installer, administrator access or separately installed runtime.

The earlier PowerShell application (v2.32) has been retired. Its source remains available in Git history. See [Moving from the PowerShell version](#moving-from-the-powershell-version) before switching.

## Getting started

1. Open the latest successful run of the [Development builds workflow](https://github.com/BasicallyGod/Basic-Advanced-Timer/actions/workflows/avalonia-preview.yml) and download **BasicsAdvancedTimer-Windows-experimental** (or the alpha artifact if intentionally testing alpha).
2. Extract the whole ZIP into its own folder and run `BasicsAdvancedTimer.Preview.exe`. Keep its DLLs together.
3. To update, copy [migration/updater](migration/updater/README.md) somewhere writable and run `Timer Updater.cmd`. Choose the experimental or alpha channel, then install the newest successful build and roll back if needed. Each channel keeps its own data.

Run `Start-Demo.cmd` to try the app with sample accounts and history, without touching real data. Data is saved to a `migration-data` folder beside the executable, or to a folder you choose with `--data-dir`.

## What it does

- Start, stop and switch between client accounts, with Break and Lunch, from the pill or the dashboard.
- Set working hours by weekday, days off and one-off starts. Start day and Finish day work throughout the day, and time beyond the usual finish is shown as overtime.
- Track budgets by week, month or quarter, with a ring on each account.
- Review each day on a scrollable, zoomable timeline. Drag blocks to move or resize them, add, merge, group and delete time, tidy small gaps, add or remove minutes for the day, and keep notes. Every edit can be undone, and the history survives a restart.
- Review the week as an overview, an hours table and a list of entries. Export a summary and detailed CSV with decimal hours that add up exactly.
- Follow Windows light or dark by default, choose Light or Dark, or make your own theme in the theme editor. Choose decimal hours or hours and minutes, and the size of the interface.
- Start with Windows, with an app icon on the taskbar and a notification area icon. The taskbar button can be switched off to run from the tray alone. The window has no close button, so it cannot be closed by accident.

## Data

Time records are kept in `timer.db` (SQLite), imported Feed reports and source state in a separate `feed.db`, with a small `basics-advanced-timer.settings.json` beside it. The database is opened exclusively, so two copies cannot write to one folder, and it is backed up daily. To move computers or make a backup, copy the data folder while the app is closed. None of these data files belongs in Git. See [Feed sources and scheduled delivery](FEED.md) for folder, paste and RSS setup.

## Moving from the PowerShell version

The new app deliberately does not import the old CSV timesheets. If it finds `Timesheets/sessions.csv` in its data folder it says so, leaves the files alone and starts with an empty database. Keep your old `Timesheets` folder as an archive, or recover the old app from Git history if you need to run it again. It refuses to use a folder that still holds the old script, so the two never share data.

## Release channels

The repository uses `main` (Microsoft Store stable), `experimental` (everyday testing) and `alpha` (unstable prototypes). Development builds use separate portable artifacts and isolated updater data. See [BRANCHES.md](BRANCHES.md) and [updater guide](migration/updater/README.md).

The Store build comes from `main` as an unsigned MSIX, and the Store signs and distributes it. Installs and updates for it go through the Microsoft Store, not the updater. See the [MSIX packaging guide](migration/packaging/README.md) for Microsoft Partner Center instructions.

## Building

```sh
dotnet run --project migration/src/Timer.Desktop/Timer.Desktop.csproj
dotnet test migration/tests/Timer.Desktop.Tests/Timer.Desktop.Tests.csproj --configuration Release
```

The code lives under `migration/` for now, so existing updater shortcuts keep working. See [migration/README.md](migration/README.md) for the user guide and architecture, [CLAUDE.md](CLAUDE.md) for engineering notes and [CHANGELOG.md](CHANGELOG.md) for history.

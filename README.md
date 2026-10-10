# Basic’s Advanced Timer

An always-on-top time tracker for client accounts and weekly timesheets. It runs as a small pill that stays out of the way, and opens into a dashboard with a daily timeline, the week's hours and exports. It is written in C# with Avalonia on .NET 8 and runs on Windows, Linux and macOS. The packaged Windows build needs no installer, administrator access or separately installed runtime.

The earlier PowerShell application (v2.32) has been retired. Its source remains available in Git history. See [Moving from the PowerShell version](#moving-from-the-powershell-version) before switching.

## Getting started

1. Open the latest successful run of the [Avalonia preview workflow](https://github.com/BasicallyGod/Basic-Advanced-Timer/actions/workflows/avalonia-preview.yml) and download **BasicsAdvancedTimer-Windows-preview**.
2. Extract the whole ZIP into its own folder and run `BasicsAdvancedTimer.Preview.exe`. Keep its DLLs together.
3. For a one-click update and rollback routine, see [migration/updater](migration/updater/README.md).

Run `Start-Demo.cmd` to try the app with sample accounts and history, without touching real data. Data is saved to a `migration-data` folder beside the executable, or to a folder you choose with `--data-dir`.

## What it does

- Start, stop and switch between client accounts, with Break and Lunch, from the pill or the dashboard.
- Set working hours by weekday, days off and one-off starts. Start day and Finish day work throughout the day, and time beyond the usual finish is shown as overtime.
- Track budgets by week, month or quarter, with a ring on each account.
- Review each day on a scrollable, zoomable timeline. Drag blocks to move or resize them, add, merge, group and delete time, tidy small gaps, add or remove minutes for the day, and keep notes. Every edit can be undone, and the history survives a restart.
- Review the week as an overview, an hours table and a list of entries. Export a summary and detailed CSV with decimal hours that add up exactly.
- Choose from eleven themes (Hawthorn by default), or make your own in the theme editor. Choose decimal hours or hours and minutes, and the size of the interface.
- Start with Windows, with a notification area icon. The window has no close button, so it cannot be closed by accident.

## Data

Everything is kept in `timer.db` (SQLite) with a small `basics-advanced-timer.settings.json` beside it. The database is opened exclusively, so two copies cannot write to one folder, and it is backed up daily. To move computers or make a backup, copy the data folder while the app is closed. Neither file belongs in Git.

## Moving from the PowerShell version

The new app does not import the old CSV timesheets yet. If it finds `Timesheets/sessions.csv` in its data folder it says so, leaves the files alone and starts with an empty database. Keep your old `Timesheets` folder as an archive, or recover the old app from Git history if you need to run it again. It refuses to use a folder that still holds the old script, so the two never share data.

## Building

```sh
dotnet run --project migration/src/Timer.Desktop/Timer.Desktop.csproj
dotnet test migration/tests/Timer.Desktop.Tests/Timer.Desktop.Tests.csproj --configuration Release
```

The code lives under `migration/` for now, so existing updater shortcuts keep working. See [migration/README.md](migration/README.md) for the user guide and architecture, [CLAUDE.md](CLAUDE.md) for engineering notes and [CHANGELOG.md](CHANGELOG.md) for history.

# Basic’s Advanced Timer: user guide and architecture

This folder holds the whole application. The name `migration` is historical and is kept so the updater and workflow paths keep working.

## Running it

Download **BasicsAdvancedTimer-Windows-experimental** from the latest successful **Development builds** workflow run, extract the whole ZIP into its own folder and run `BasicsAdvancedTimer.Preview.exe`. The package includes the .NET runtime and needs no administrator rights. Data goes to **migration-data** beside the executable, or to the folder given with `--data-dir`. `Start-Demo.cmd` (or Settings, Open sample-data demo) runs a simulated 14:15 day with seven accounts and does not save anything real.

The right-click menu is short on purpose. It has Decimal hours, the running build (for example `Version 0.1.77 (aa17cda)`: the workflow run number and the first seven characters of the commit, or development build for local builds), Test overtime in the sample-data demo, and Close timer. Everything else is on the settings page, opened with the gear at the foot of the dashboard's icon rail.

## The two windows

- **Pill.** Timer, account and today's credit, and pause/resume. Click the account to show the list, click the timer to open the dashboard. Hold and drag anywhere along its header to move it. The expanded pill ends in a wide Start day (green) or Finish day (red) button. A thin line shows progress through the working day, turning red in overtime and amber on a break, and the pill glows amber, red or green for a break, overtime or recording.
- **Dashboard.** A slim rail of icons down the left chooses the Timeline, Week overview, Hours table or Entries view, and holds the minimise and settings buttons. The left column has a Day elapsed card (the day clock, the active account, allocated and unallocated totals), then the client accounts and the Break and day buttons. Each account shows its colour, how much of its budget is used as a bar with a tick for how far through the week, month or quarter it is, a red percentage once it is over, today's time and a play or pause circle. The right column is titled with the view, with the date arrows and a Today button, an action row (Add time, Undo, Merge, zoom, export) and the workspace. It stays above other windows. Drag any bare part of the header rows to move it.

The big clock shows elapsed working-day time from the configured or manual start. It keeps running when accounts are paused or switched, and excludes Lunch, breaks and finished-day intervals. Time beyond the usual finish is overtime, shown in red. A day off counts only the time actually tracked. Scheduled starts take effect on their own. Start day allows early starts and resumes a finished day, and starting an account starts the day too.

## Day to day

- **Accounts.** Settings, Accounts and budgets adds, hides, reorders and recolours accounts and edits week, month or quarter budgets (decimals such as 7.5 are fine, blank means none). Drag a row by its grip to reorder it. Each account has one of sixteen palette colours, or any `#RRGGBB`.
- **Break.** Stops time going to every account and wears the amber break colour. It reads Start break and Finish break. Ending a break goes back to the account that was running.
- **Working hours.** The Working hours section of the settings page edits each weekday's start, end and day-off status. Overnight shifts are not supported yet.
- **Timeline.** The hours run down a spine with a tick for each hour, and for each half and quarter hour as you zoom in. Each block has a dot on the spine where it starts, with its name, time range and credited time. On today a line and a chip in the accent colour mark the current time. Scroll, or hold Ctrl and use the wheel, or use the bar under the timeline (fit the day, zoom out, a slider, zoom in) to zoom (60 to 640 pixels an hour). Drag a block to move it, or its top or bottom edge to resize it. Movement snaps to five minutes and to neighbours. Click blocks to select them and Merge to join blocks for one account and day, filling blank gaps with credited time. Double-click a block to edit it, or empty space to add one. In the editor, moving a block's end later into the next block asks first, then moves the blocks in the way later (as little as needed, stopping at the first gap) as one undoable edit. Right-click for edit, merge and delete, or in empty space for Add time and Tidy day.
- **Tidy day.** Previews small gaps (up to five minutes by default), lets you give each to the previous or next account, rechecks everything on apply and adds credited time as one undoable edit.
- **Undo.** Reverses the latest time or note edit, up to 50 steps, kept across restarts.
- **Export.** Writes `Detail.csv` and `Summary.csv` to a new folder under `Timesheets/Exports` in the data folder. Decimal hours are apportioned so daily, weekly and detail figures add up exactly. Only saved entries are included, so pause first to include the running interval.
- **Themes.** Settings, Appearance and behaviour has a theme choice. System is the default and follows Windows light or dark. Light and Dark are the two built-in themes (orange accent, tinted timeline blocks with a coloured edge). Edit themes opens the editor, where built-in themes can be duplicated and a copy renamed and recoloured, previewed live (themes that tint their blocks also get two sliders for how much account colour the fill takes and how strong the edge is). The editor asks before throwing away unsaved edits. Themes saved under an older name (Time Deck, Hawthorn and the rest) open as Light or Dark, whichever they were closest to. In dark themes the week chart and its legend use each account's own colour, so slices stay easy to see.
- **Taskbar, tray and start-up.** The timer has an app icon and a taskbar button, so it can be pinned and launched like any app. Settings, Show in taskbar switches the button off to run from the notification area alone, and left-clicking the tray icon then shows or hides the timer. Neither window has a close button, and Alt+F4 hides the timer to the tray while tracking carries on. The tray icon is green while recording. Start with Windows adds a per-user sign-in entry and needs no administrator rights. On Windows 11 dialogs have a title bar in the theme's colours and the app icon.

## Data and safety

Records live in `timer.db` (SQLite, write-ahead log), opened exclusively so two copies cannot share a folder, with daily backups. The settings file `basics-advanced-timer.settings.json` is replaced atomically with a `.bak` copy and preserves unknown sections. The running session checkpoints every 15 seconds, stops and saves on a normal close and, after a crash, credits time only up to the last checkpoint. A failed save keeps pending time and cancels closing. The app refuses a folder that holds the old PowerShell script, and deliberately never imports the old CSV timesheets.

## Microsoft Store package

The MSIX build and signing instructions are in [packaging/README.md](packaging/README.md). Release promotion rules and the three channels (`main`, `experimental`, `alpha`) are in [../BRANCHES.md](../BRANCHES.md). Portable data stays beside the exe, while packaged app data lives in per-user storage. The Store build hides the sample-data demo and rejects `--demo`. A locally built development MSIX is self-signed for testing only and is not a published Store build.

## Architecture

- `Timer.Core` is platform-independent C#: tracking engine, schedules, day calculations, credited sessions, recovery and the theme model and library.
- `Timer.Storage` is the SQLite database, settings, undo history, credit review, ledger editing and week export.
- `Timer.Desktop` is the Avalonia UI. `TimerModel` adapts the engine and store to the views. Styles and the Light/Dark/theme dictionaries come from `ThemeEngine`, which registers each theme as an Avalonia theme variant at start-up.

The build uses .NET 8 and pinned Avalonia 11.3.16.

```sh
dotnet run --project migration/src/Timer.Desktop/Timer.Desktop.csproj
dotnet test migration/tests/Timer.Desktop.Tests/Timer.Desktop.Tests.csproj --configuration Release
dotnet publish migration/src/Timer.Desktop/Timer.Desktop.csproj --configuration Release --runtime win-x64 --self-contained true -o artifacts/windows-preview
```

Tests use disposable synthetic data only. They cover the engine (early starts, pauses, lunch, finish and resume, overtime, days off, midnight splitting), storage (database, backups, undo, fault injection), the production windows and styles in a headless Avalonia, and the theme engine (contrast, derived tints, editor flow). The workflow runs them on Windows, Linux and macOS, then publishes the Windows build.

# Avalonia migration build

The `migration` branch starts from experimental v2.32. Its original PowerShell launcher still runs the existing application. This directory contains the separate Avalonia replacement under development.

## Try the Windows build

Download **BasicsAdvancedTimer-Windows-preview** from the latest successful **Avalonia migration preview** GitHub Actions run. Extract the whole ZIP into its own folder and run `BasicsAdvancedTimer.Preview.exe`. Keep its DLLs and native libraries together. The package includes the .NET runtime and runs with ordinary user privileges, without an installer or separately installed SDK/runtime.

This stage now records real sessions. By default it creates **migration-data** beside the executable. Accounts and totals start empty; the earlier illustrative timesheet is no longer seeded into the app. Three initial account names are provided; change visibility, add accounts and configure budgets through Settings → Accounts and budgets.

The main clock shows elapsed working-day time, from the configured or early/manual start. It continues when accounts are paused or switched, and excludes Lunch and finished-day intervals. It continues counting beyond the scheduled finish, with overtime shown separately in red. Account rows count only their own credited time. Usual scheduled starts still take effect automatically, matching the PowerShell application. Start day allows early starts and resumes finished days; starting an account starts/resumes the day too.

Theme options are under **Settings > Appearance**, and also available by right-clicking either window. Choose System, Light or Dark. Hours format is in Settings and the dashboard tools row. Not tracking is grey; overtime is independent and red. Budget rings use the configured week/month/quarter period.

The pill and fixed dashboard share one tracking model. Drag anywhere across the pill header, or drag the dashboard's date header, to move the window. Timeline and Week now use recorded sessions and credited totals. Sessions crossing midnight are split; overlapping timeline entries use separate lanes. Timeline scroll is retained during periodic updates.

## Test copied existing data

Close the old application, then copy its settings, `Timesheets` directory and recovery/away files into a **separate data-only folder**, keeping the originals. Launch the new executable with:

```powershell
.\BasicsAdvancedTimer.Preview.exe --data-dir "C:\Users\YourName\Documents\Timer migration test"
```

The app refuses the old application folder when its script or launcher is present. A folder lock prevents two new builds using the same data folder. Do not run the old app against the migration copy simultaneously; the old process does not participate in the new lock.

The storage adapter reads the existing schema-1 JSON sections, account visibility, budget periods, weekday shifts, days off, manual starts, CSV credited rows and away intervals. It preserves unknown settings sections/root properties and extra CSV columns. Notes now load and save in their existing CSV format. Editing a source interval prunes obsolete group references. Credit positions and grouping metadata now load in the existing formats. Legacy merged credit contributes to totals; placing that credit draws intervals without adding ledger rows. Legacy Undo files and exports remain in place.

Sessions, manual starts, away intervals, preferences and crash recovery are saved together through an atomic write-ahead journal. Interrupted saves replay before loading. Files receive `.bak` copies. Active sessions checkpoint every 15 seconds and stop/save on a normal close. Crash recovery credits only up to the last successful checkpoint, not the time the app was closed. A save failure keeps pending time in memory, shows an error, retries, and prevents ordinary closing until saving succeeds. Invalid settings/CSV stop startup rather than resetting data.

## Current scope

Connected: day/account/lunch tracking, early starts, finish/resume, overtime, copied settings/CSV loading, new session saving, crash recovery, account visibility and budget editing, budget period totals, theme and format persistence, recorded Timeline/Week display, entry editing and notes, gap additions, deletion, persistent Undo, credit positioning/grouping, reviewed Tidy day and reconciled weekly CSV exports.

Still to migrate: account renaming/reordering, tray integration and auto-start. Stopped weekly decimal values use the same reconciled hundredths as export; running totals are provisional. This remains a migration build for testing, rather than the complete replacement for everyday timesheets.

## Architecture and checks

- `Timer.Core` is platform-independent C# for schedules, day calculations, credited sessions and recovery.
- `Timer.Storage` adapts existing files, locking, backups and recoverable saves.
- `Timer.Desktop` supplies Avalonia views, shared theme resources and the application model. It has no duplicate timekeeping rules.

The build uses .NET 8 and pinned Avalonia 11.3.16. SDK/NuGet dependencies are needed to develop it, not to run the packaged Windows build.

```sh
dotnet run --project migration/src/Timer.Desktop/Timer.Desktop.csproj
dotnet test migration/tests/Timer.Desktop.Tests/Timer.Desktop.Tests.csproj --configuration Release
dotnet publish migration/src/Timer.Desktop/Timer.Desktop.csproj --configuration Release --runtime win-x64 --self-contained true -o artifacts/windows-preview
```

Tests use disposable synthetic data only. Core checks exercise early starts, account pauses, lunch, finish/resume, overtime, days off, midnight splitting and credited adjustments. Storage checks cover copied legacy formats, unknown-field preservation, locking, checkpoint recovery, journal replay and malformed-file handling. UI checks use production styles/fonts/windows, drawn row positions, shared formats and view switching. CI checks Windows, Linux and macOS, uploads rendered previews, and starts the published Windows executable with an isolated temporary store. Native DPI transitions, tray behaviour and the intended work-machine launch still need desktop checks.

### UI review and sample data

The compact pill follows the original layout: timer on the left, account and today’s credit in the middle, pause/resume on the right. Click the account area to show or hide its account list. Click the timer to open the dashboard, and use Back to pill to return. Hold LMB and move at least five display-independent pixels anywhere across the pill header, including its timer, account or pause control, to drag it without activating that control. Release without dragging to click. The dashboard moves by dragging its header text or empty header space. Header buttons remain clickable. The main clock turns green while tracking and grey while idle; overtime has its own red label. Pause/resume retains the selected account and does not stop the day clock.

Run **Start-Demo.cmd** from the Windows preview folder, or choose **Settings → Open sample-data demo**. Demo mode has populated accounts, budgets and recorded intervals. It uses a simulated 14:15 clock on today's date, advances in real time, and never opens or saves personal data. Choose **Settings → Force overtime** inside the demo to set today's shift end to 13:00. Restart the demo to reset it. CLI: `--demo` (cannot be combined with `--data-dir`).

**Settings → Working hours** edits each weekday's working status, start and end. These changes persist in normal mode. The optional “Also reset today” checkbox removes today's exceptional start/shift so the new schedule applies; it does not change account records or finished-day breaks. Overnight shifts remain unsupported at this stage. Copied-data imports still require `--data-dir` as described above; normal launch starts with your migration-data folder, without synthetic records.

**Settings → Accounts and budgets** adds accounts, hides/shows existing accounts and edits their week/month/quarter budgets. Budget hours accept decimals such as 7.5; blank means no budget. Hiding the active account first stops and saves its current session. Hidden accounts remain in Timeline and Week history, and showing one again restores its tracking row and credited totals. Existing account names cannot yet be renamed or deleted because their historical CSV references must be migrated together. Lunch stays a built-in account.

### Editing recorded time

Double-click or right-click a completed timeline block to open its entry editor. Change account, start/end time or note; delete is available for normal positioned entries. Notes can span lines and contain commas/quotes. Week now includes an individual entries list beneath the totals, including merged credit and adjustments (notes only). Running intervals are read-only until tracking stops.

Drag a completed timeline block to move it, or its top/bottom eight-pixel edge to resize it. Movement snaps to five minutes. Overlaps and intervals shorter than three seconds are rejected. Future entries are allowed and credited immediately. Existing imported overlaps can keep their notes, but editing their times must resolve any conflict. Click/right-click free timeline space or choose **Add time** to add new credited time in a gap. This increases the ledger total; it does not position old merged credit. Start/end accept HH:mm or HH:mm:ss; 24:00 is accepted as a day's end. Cross-midnight manual intervals must be entered separately for each day.

**Undo** reverses the most recent time/note edit, addition or deletion. The snapshot persists across restart and keeps sessions recorded after that edit. Sessions, notes, credit positions, group metadata and edit history are journalled together; a failed save retains the in-memory change for retry. CSV extra columns survive edits and Undo. A changed source interval loses its old grouping reference, restored by Undo. Ambiguous legacy note keys (duplicate account/date/start) are protected from individual editing. The new Undo history uses migration-edit-state.json; old PowerShell Undo files remain untouched.

### Credit review, Tidy day and exports

The dashboard **Tools** menu contains **Position existing credit**, **Group entries**, **Tidy day** and **Export week**.

Position existing credit selects an account's remaining merged credit for the selected day, a start time, minutes and a position note. It preserves credited sessions and totals. Positioned credit has an outlined timeline block: double-click or right-click to edit or **Unplace** it. Unplacing returns the minutes to the available pool. Positions use the compatible `Timesheets/credit-positions.csv` format, including older files without a Note column. Future placements are allowed; overlaps and crossing midnight are rejected. The timeline caption shows remaining unplaced credit.

Group entries lists the selected week and selects at least two source entries for one account/day. Groups appear as one Week entry, without implying that the gaps between them were worked. Click that entry to inspect/edit its parts or ungroup it. Adjoining groups show one combined timeline card labelled with their summed credited duration. Imported groups with gaps show separate linked sections, preserving the gaps. Right-click it to ungroup, or use the visible Ungroup button beside its Week entry. Tools → Group entries also lists existing groups for inspection/ungrouping. Grouping and ungrouping do not rewrite the ledger or change totals. Editing a grouped source prunes its obsolete reference and any remaining singleton group; Undo restores it.

Tidy day previews gaps up to five minutes by default (adjustable 1–60). Choose the previous or next account for each gap, or leave it unfilled. Lunch, away intervals, future gaps, ambiguous boundaries and running entries are excluded. Applying rechecks the complete preview before adding credited time; a changed preview adds nothing. One Undo reverses the selected batch. Fractional boundaries are rounded inward so a fill cannot overlap adjacent entries.

Export week creates **Detail.csv** and **Summary.csv** together in a new folder under `migration-data/Timesheets/Exports` (or the selected copied data folder). Existing exports are retained. The dialog shows the saved folder path. Demo/test exports use a temporary folder. Exports include saved entries only: pause first to include the current active interval. Detail includes positions, unplaced credit, groups, adjustments and notes, excluding Lunch to match the working total. Summary includes hidden/historical accounts and a total excluding Lunch. Integer hundredths are reconciled across entries, account/day cells and grand totals, including negative adjustments. Positions and groups never duplicate credited time in exports.

The dashboard now has the same transparent rounded window surface as the pill. Scrollbars use an eight-pixel track and a slim draggable thumb, without arrow buttons.

### Select and merge blocks

**Ctrl+left-click** completed Timeline blocks or Week entries to toggle selection. Selected blocks have a visible outline. Choose **Merge** in the dashboard toolbar or **right-click → Merge selected**. Ctrl selection does not move/resize a block or open its editor. Press Escape to clear selection; changing dates clears it too. Clicking empty timeline space clears selection and opens Add time.

Merging requires blocks for the same account and day, with only blank gaps between them. Blank gaps are added as credited entries for that account and included in the combined total; the selection hint shows the added amount before merging. Overlaps, intervening entries, positioned credit and running time prevent a merge. Gap fills use whole-second starts and the ledger’s 0.01-minute precision, rounded inward to avoid overlap. Existing groups may be selected with other blocks to extend the group. Original source rows remain intact; one Undo removes the added gap time and restores prior grouping. Ungroup retains the newly credited gap entries. Older nonconsecutive groups render as separate linked timeline sections, without filling their gaps visually.

**Single-click empty timeline space** to add a block. The entry editor opens at the clicked time, snapped to five minutes, with a proposed 30-minute duration capped at the next occupied interval or midnight. Change the account, end and note, then Save. Clicking occupied blocks retains their normal edit/drag/selection behaviour. Only one entry editor opens at a time.

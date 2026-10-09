# Avalonia migration build

The `migration` branch starts from experimental v2.32. Its original PowerShell launcher still runs the existing application. This directory contains the separate Avalonia replacement under development.

## Try the Windows build

Download **BasicsAdvancedTimer-Windows-preview** from the latest successful **Avalonia migration preview** GitHub Actions run. Extract the whole ZIP into its own folder and run `BasicsAdvancedTimer.Preview.exe`. Keep its DLLs and native libraries together. The package includes the .NET runtime and runs with ordinary user privileges, without an installer or separately installed SDK/runtime.

This stage now records real sessions. By default it creates **migration-data** beside the executable. Accounts and totals start empty; the earlier illustrative timesheet is no longer seeded into the app. Three initial account names are provided until account editing is migrated.

The main clock shows elapsed working-day time, from the configured or early/manual start. It continues when accounts are paused or switched, and excludes Lunch and finished-day intervals. It continues counting beyond the scheduled finish, with overtime shown separately in red. Account rows count only their own credited time. Usual scheduled starts still take effect automatically, matching the PowerShell application. Start day allows early starts and resumes finished days; starting an account starts/resumes the day too.

Theme options are under **Settings > Appearance**, and also available by right-clicking either window. Choose System, Light or Dark. Hours format is in Settings and the dashboard tools row. Not tracking is grey; overtime is independent and red. Budget rings use the configured week/month/quarter period.

The pill and fixed dashboard share one tracking model. Drag the pill's account text or the dashboard's date header to move the window. Timeline and Week now use recorded sessions and credited totals. Sessions crossing midnight are split; overlapping timeline entries use separate lanes. Timeline scroll is retained during periodic updates.

## Test copied existing data

Close the old application, then copy its settings, `Timesheets` directory and recovery/away files into a **separate data-only folder**, keeping the originals. Launch the new executable with:

```powershell
.\BasicsAdvancedTimer.Preview.exe --data-dir "C:\Users\YourName\Documents\Timer migration test"
```

The app refuses the old application folder when its script or launcher is present. A folder lock prevents two new builds using the same data folder. Do not run the old app against the migration copy simultaneously; the old process does not participate in the new lock.

The storage adapter reads the existing schema-1 JSON sections, account visibility, budget periods, weekday shifts, days off, manual starts, CSV credited rows and away intervals. It preserves unknown settings sections/root properties and extra CSV columns. Notes, groups, credit positions, Undo files and exports remain in place and are not rewritten. Legacy merged credit contributes to totals but is not turned into invented timeline blocks. Position/group/note rendering is a later stage.

Sessions, manual starts, away intervals, preferences and crash recovery are saved together through an atomic write-ahead journal. Interrupted saves replay before loading. Files receive `.bak` copies. Active sessions checkpoint every 15 seconds and stop/save on a normal close. Crash recovery credits only up to the last successful checkpoint, not the time the app was closed. A save failure keeps pending time in memory, shows an error, retries, and prevents ordinary closing until saving succeeds. Invalid settings/CSV stop startup rather than resetting data.

## Current scope

Connected: day/account/lunch tracking, early starts, finish/resume, overtime, copied settings/CSV loading, new session saving, crash recovery, budget period totals, theme and format persistence, and recorded Timeline/Week display.

Still to migrate: account/budget/working-hours editors, positioned credit and grouping presentation, timeline dragging and gap filling, Tidy day, notes, Undo, export and exact two-decimal reconciliation, tray integration and auto-start. Weekly decimal values currently format credited durations directly; they are not the reconciled export values. This remains a migration build for testing, rather than the complete replacement for everyday timesheets.

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

Click the pill’s account area to show or hide its compact account list. Click its timer to open the dashboard, and use Back to pill to return. Drag the pill using the clear strip above or below these two click targets, or its side margins. The dashboard moves by dragging its header text or empty header space. Header buttons remain clickable. The main clock turns green while tracking and grey while idle; overtime has its own red label.

Run **Start-Demo.cmd** from the Windows preview folder, or choose **Settings → Open sample-data demo**. Demo mode has populated accounts, budgets and recorded intervals. It uses a simulated 14:15 clock on today's date, advances in real time, and never opens or saves personal data. Choose **Settings → Force overtime** inside the demo to set today's shift end to 13:00. Restart the demo to reset it. CLI: `--demo` (cannot be combined with `--data-dir`).

**Settings → Working hours** edits each weekday's working status, start and end. These changes persist in normal mode. The optional “Also reset today” checkbox removes today's exceptional start/shift so the new schedule applies; it does not change account records or finished-day breaks. Overnight shifts remain unsupported at this stage. Copied-data imports still require `--data-dir` as described above; normal launch starts with your migration-data folder, without synthetic records.

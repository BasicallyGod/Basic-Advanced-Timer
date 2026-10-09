# Basic’s Advanced Timer

**Development status:** `main` contains the stable v2.22 release. The `experimental` branch contains v2.25 with settings consolidation, branding changes and all-day Start day / Finish day controls. Its automated tests have passed, but migration against a real Windows installation has not yet been verified. Keep everyday timesheets on the stable release until that check is complete.

Basic’s Advanced Timer is a portable, always-on-top Windows time tracker for client accounts and weekly timesheets. The current application is **v2.25**. It runs from a folder using Windows PowerShell 5.1 and WinForms, with compiled C# rendering built into the script. No installer, administrator access, extra PowerShell modules or separate .NET runtime are required.

## Getting started

1. Download the repository ZIP and extract it to a folder you can keep, such as `Documents\Basic’s Advanced Timer`.
2. Double-click `StartAccountTimer2.vbs`. It starts `AccountTimer2.ps1` without a visible PowerShell console.
3. To start automatically when signing in, open `shell:startup` using Win + R and add a shortcut to `StartAccountTimer2.vbs`.

Keep `StartAccountTimer2.vbs` and `AccountTimer2.ps1` together. The launcher uses its own folder to find the script, so you can move the application as one folder. Close the timer before updating the script or moving its data. Run only one copy against a given data folder.

**Requirements:** Windows 10 or 11 with the built-in Windows PowerShell 5.1.

## What it does

- Start, stop and switch between client accounts, with a dedicated Lunch timer, in the full window or compact pill.
- Set usual working hours, different weekday shifts, days off and day-specific overrides. Start day and Finish day are available throughout the day, including before the usual start and after the usual finish. Starting early counts from the exact button press for that date without changing your usual hours. Finish day pauses the day and unallocated counters; Start day resumes them without counting the break. Choose a client account to allocate the time. Starting an account early also starts the day. The same controls are available from the right-click menu in compact view.
- Track account budgets, record notes, and correct time through manual entries and round-up or round-down adjustments.
- The week and timeline drawer uses larger text, spacious rows and a wider totals column. Navigation has its own row. A taller drawer leaves the desktop visible beneath the shorter timer.
- Review the week in the side drawer or separate weekly window. Scroll and zoom the daily timeline, drag session boundaries, and inspect short sessions and overlapping work.
- Choose between **positioning existing credited time** and **adding new time** when filling a timeline gap. The first action does not increase account totals.
- Group sessions in the weekly view without deleting the original recordings, and ungroup them to edit individual entries.
- Choose themes, account-list transparency, interface size, and decimal-hours or hours-and-minutes display. Preference changes are saved in one portable JSON configuration file. Preferences are stored in a single JSON file.
- Export a weekly account/day summary and detailed time-entry CSV with reconciled two-decimal hours.

## Repository files

| Path | Purpose |
| --- | --- |
| `AccountTimer2.ps1` | Current Basic’s Advanced Timer application, including its UI, tracking, editing and exports |
| `StartAccountTimer2.vbs` | Relative-path launcher without a console window; filename retained for existing shortcuts |
| `tests/timeline-projection.ps1` | Synthetic timeline and credit-position regression checks |
| `tests/decimal-reconciliation.ps1` | Synthetic accounting and CSV-export reconciliation checks |
| `tests/renderer-layout.ps1` | Compiles and renders the UI, checking drawer controls and transparent corners |
| `tests/day-controls.ps1` | Synthetic early-start, finish, resume and overtime checks |
| `tests/settings-migration.ps1` | Synthetic settings migration, persistence and branding checks |
| `.github/workflows/powershell-checks.yml` | Runs all five test scripts in Windows PowerShell on GitHub Actions |
| `CLAUDE.md` | Engineering notes, architecture and pitfalls for contributors |
| `CHANGELOG.md` | Feature and maintenance history |
| `.gitignore`, `.gitattributes` | Exclude local data by default and standardise line endings |

Older `AccountTimer.ps1` and `BillableTimer.ps1` implementations and their launchers have been removed from the current file tree because the current application does not call them. Their source is still available through earlier commits in Git history. You do not need them to run v2.23.

## Local data and portability

The application keeps its user preferences in **`basics-advanced-timer.settings.json`** beside the script. It replaces 13 separate legacy preference files covering accounts, budgets, working days and hours, interface size, window positions, theme selection, appearance and view state.

An existing installation is migrated automatically on first launch, and your original preference files are kept as a backup. See [Upgrading an existing installation](#upgrading-an-existing-installation-v223) below before replacing the script.

Your work records, crash recovery and editable theme catalogue remain separate from the JSON preferences. These local files **are not in the repository** and are excluded from ordinary Git tracking by `.gitignore`. They may contain sensitive client names and work notes.

| Local path | Purpose |
| --- | --- |
| `Timesheets/sessions.csv` | Authoritative recorded and credited account time, including adjustments |
| `Timesheets/notes.csv` | Notes linked to session records |
| `Timesheets/entry-groups.csv` | Non-destructive group membership for weekly entries |
| `Timesheets/credit-positions.csv` | Clock positions assigned to previously merged credit, without adding hours |
| `Timesheets/*.undo.csv` | One-level undo snapshots for the corresponding files |
| `basics-advanced-timer.settings.json` | Portable application preferences (accounts, budgets, working hours, selected theme and UI layout) |
| `basics-advanced-timer.settings.json.bak` | Last settings version before an update |
| `Timesheets/legacy-settings-backup/` | Archived original preference files from the first migration |
| `account-timer-state.txt` | Live session state and crash recovery; deliberately separate |
| `account-timer-away.txt` | Workday finish/away intervals; deliberately separate |
| `account-timer-themes.txt` | Editable colour theme definitions; deliberately separate |
| `Timesheets/Week commencing YYYY-MM-DD.csv` | Weekly summary by account and day |
| `Timesheets/Week commencing YYYY-MM-DD entries.csv` | Detailed time entries and reconciled `Hours` column |
| `account-timer-error.log` | Diagnostic logging and rendering performance output |

To move to another computer or make a complete backup, copy the **entire application folder**, including `Timesheets`, the new `.json` settings file and the remaining `.txt` state and theme files. Keep the two newer metadata CSVs with `sessions.csv`; they affect how historical merged time and manual groups appear. On a fresh download, the timer creates local configuration and time files as they are needed.

## Upgrading an existing installation (v2.23)

Close the timer and **copy your entire existing application folder to a backup location** before replacing `AccountTimer2.ps1`. Keep the `StartAccountTimer2.vbs` filename: shortcuts created for earlier versions continue working, although the application now displays **Basic’s Advanced Timer** in Windows.

On the first run, v2.23 imports the 13 existing preference files (`account-timer-accounts.txt`, `account-timer-settings.txt`, `account-timer-days.txt`, `account-timer-budgets.txt`, and appearance/view preference files) into `basics-advanced-timer.settings.json`. It verifies the new settings file and then moves the original preference files to `Timesheets/legacy-settings-backup/`; **it does not delete them**. Keep that archive until you have checked your settings. Your actual time records are not migrated or changed.

Editable `account-timer-themes.txt`, `account-timer-state.txt`, `account-timer-away.txt`, error logs and all timesheet CSVs remain separate and are not migrated. The JSON settings writer uses a temporary file and a `.bak` copy of the previous version to reduce the risk of losing preferences. If the JSON becomes unreadable, startup stops with a message rather than silently resetting your settings.

**Important:** Once the migration is complete, older versions of the script do not read the new JSON settings file. To roll back to an old script, restore the saved application folder or copy the original preference files back from the legacy backup. Always test upgrades against a separate copy of your live data.

The GitHub repository keeps its existing URL and the PowerShell/VBS filenames for compatibility; the on-screen application and documentation use the new brand.

## Recorded time versus credited time

`sessions.csv` determines credited hours. A manually added entry increases credited time, while a rounded or adjusted row changes the credited total without necessarily describing a continuous clock interval. Older destructive `merged` records retain credited hours but may not retain the original individual timestamps.

Positioning some of that older merged credit adds a location in `credit-positions.csv`, not another credited session. The timeline shows the positioned portion and reports the remaining credit as unplaced. New groups in `entry-groups.csv` retain their original source intervals.

## Timesheet rounding

The application retains precise source durations. The exported `Hours` column distributes differences in 0.01-hour increments across entries and account/day figures so the detailed entries, weekly summary and grand total add up exactly. One hundredth of an hour equals 36 seconds. The `Duration` column and hours-and-minutes labels are rounded to whole minutes for readability, so adding those labels may not reproduce a two-decimal total.

A running timer is provisional until its session is saved. Completed weekly decimal views use the export's reconciled values; the active timer can temporarily differ from the persisted export.

## Running the tests

From a PowerShell prompt in the repository folder:

```powershell
powershell.exe -NoProfile -File .\tests\timeline-projection.ps1
powershell.exe -NoProfile -File .\tests\decimal-reconciliation.ps1
powershell.exe -NoProfile -File .\tests\settings-migration.ps1
powershell.exe -NoProfile -File .\tests\day-controls.ps1
```

The tests use synthetic data and do not load your personal timesheets. GitHub Actions runs all four checks on Windows for pull requests and changes to `main` and `experimental`.

See `CHANGELOG.md` for the feature history and `CLAUDE.md` for developer implementation notes.

## Application naming

The interface, notification-area tooltip, windows and dialogs use **Basic’s Advanced Timer**. The original launcher and script filenames remain `StartAccountTimer2.vbs` and `AccountTimer2.ps1` so existing shortcuts and portable installations continue working. The repository URL has also been left unchanged to preserve existing links.

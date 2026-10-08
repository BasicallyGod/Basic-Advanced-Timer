# Account Timer

Account Timer is a portable, always-on-top Windows time tracker for client accounts and weekly timesheets. The current application is **v2.22**. It runs from a folder using Windows PowerShell 5.1 and WinForms, with compiled C# rendering built into the script. No installer, administrator access, extra PowerShell modules or separate .NET runtime are required.

## Getting started

1. Download the repository ZIP and extract it to a folder you can keep, such as `Documents\Account Timer`.
2. Double-click `StartAccountTimer2.vbs`. It starts `AccountTimer2.ps1` without a visible PowerShell console.
3. To start automatically when signing in, open `shell:startup` using Win + R and add a shortcut to `StartAccountTimer2.vbs`.

Keep `StartAccountTimer2.vbs` and `AccountTimer2.ps1` together. The launcher uses its own folder to find the script, so you can move the application as one folder. Close the timer before updating the script or moving its data. Run only one copy against a given data folder.

**Requirements:** Windows 10 or 11 with the built-in Windows PowerShell 5.1.

## What it does

- Start, stop and switch between client accounts, with a dedicated Lunch timer, in the full window or compact pill.
- Set usual working hours, different weekday shifts, days off and day-specific overrides. The Finish day control stops the working-day and unallocated counters until work resumes.
- Track account budgets, record notes, and correct time through manual entries and round-up or round-down adjustments.
- Review the week in the side drawer or separate weekly window. Scroll and zoom the daily timeline, drag session boundaries, and inspect short sessions and overlapping work.
- Choose between **positioning existing credited time** and **adding new time** when filling a timeline gap. The first action does not increase account totals.
- Group sessions in the weekly view without deleting the original recordings, and ungroup them to edit individual entries.
- Choose themes, account-list transparency, interface size, and decimal-hours or hours-and-minutes display.
- Export a weekly account/day summary and detailed time-entry CSV with reconciled two-decimal hours.

## Repository files

| Path | Purpose |
| --- | --- |
| `AccountTimer2.ps1` | Current application, including its UI, tracking, editing and exports |
| `StartAccountTimer2.vbs` | Relative-path launcher without a console window |
| `tests/timeline-projection.ps1` | Synthetic timeline and credit-position regression checks |
| `tests/decimal-reconciliation.ps1` | Synthetic accounting and CSV-export reconciliation checks |
| `.github/workflows/powershell-checks.yml` | Runs both test scripts in Windows PowerShell on GitHub Actions |
| `CLAUDE.md` | Engineering notes, architecture and pitfalls for contributors |
| `CHANGELOG.md` | Feature and maintenance history |
| `.gitignore`, `.gitattributes` | Exclude local data by default and standardise line endings |

Older `AccountTimer.ps1` and `BillableTimer.ps1` implementations and their launchers have been removed from the current file tree because the current application does not call them. Their source is still available through earlier commits in Git history. You do not need them to run v2.22.

## Local data and portability

The application creates its files beside the script. These **are not in the repository** and are excluded from ordinary Git tracking by `.gitignore`. They can include sensitive account names and work notes. Avoid committing or sharing them.

| Local path | Purpose |
| --- | --- |
| `Timesheets/sessions.csv` | Authoritative recorded and credited account time, including adjustments |
| `Timesheets/notes.csv` | Notes linked to session records |
| `Timesheets/entry-groups.csv` | Non-destructive group membership for weekly entries |
| `Timesheets/credit-positions.csv` | Clock positions assigned to previously merged credit, without adding hours |
| `Timesheets/*.undo.csv` | One-level undo snapshots for the corresponding files |
| `Timesheets/Week commencing YYYY-MM-DD.csv` | Weekly summary by account and day |
| `Timesheets/Week commencing YYYY-MM-DD entries.csv` | Detailed time entries and reconciled `Hours` column |
| `account-timer-*.txt` | Accounts, themes, budgets, working hours, day overrides, window positions and other preferences |
| `account-timer-error.log` | Diagnostic logging and rendering performance output |

To move to another computer or make a complete backup, copy the **entire application folder**, including `Timesheets` and all `account-timer-*.txt` files. Keep the two newer metadata CSVs with `sessions.csv`; they affect how historical merged time and manual groups appear. On a fresh download, the timer creates local configuration and time files as they are needed.

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
```

The tests use synthetic data and do not load your personal timesheets. GitHub Actions also runs both checks on Windows for pull requests and changes to `main`.

See `CHANGELOG.md` for the feature history and `CLAUDE.md` for developer implementation notes.

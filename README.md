# Account timer

A small always-on-top timer for Windows that tracks time against client accounts and prepares it for timesheets. It runs as a PowerShell script, so it needs no installation and no admin rights.

AccountTimer2.ps1 is the current version. The two earlier scripts are kept as working fallbacks. See [CHANGELOG.md](CHANGELOG.md) for the full history.

## What it does

- Shows the working day so far, counting from your start time, with overtime after your end time
- One-click tracking per account, plus Lunch, with only one running at a time
- A compact pill view and a full view, with a week drawer and a separate weekly view
- Budgets per account, notes on longer stretches of time, rounding to 15 minutes and manual edits with Undo
- Separate unallocated figures for the working day and overtime
- Themes, see-through account list, decimal or hours and minutes display
- Weekly summary and time entry files in CSV, ready for timesheets or Excel

## Requirements

- Windows 10 or 11
- Windows PowerShell 5.1 (built into Windows)

## Getting started

1. Put the files in a folder you'll keep, for example Documents\Account timer.
2. Double-click StartAccountTimer2.vbs to start the timer.
3. To start it automatically, press Win + R, type shell:startup, and put a shortcut to StartAccountTimer2.vbs in the folder that opens.

Only run one of the timers at a time, as they share the same time log and settings.

| File | Purpose |
| --- | --- |
| AccountTimer2.ps1 | The current timer |
| StartAccountTimer2.vbs | Starts the current timer without a console window |
| AccountTimer.ps1, StartAccountTimer.vbs | The earlier account timer |
| BillableTimer.ps1, StartTimer.vbs | The original billable-hours timer |

## Your data stays local

The timer creates these alongside the scripts as you use it. They are listed in .gitignore, so your time logs, account names and settings are never uploaded with the code.

| File | Contents |
| --- | --- |
| Timesheets\sessions.csv | Every stretch of time tracked |
| Timesheets\notes.csv | Notes on stretches of time |
| Timesheets\Week commencing (date).csv | Weekly summary by account and day |
| Timesheets\Week commencing (date) entries.csv | Each stretch of time for the week |
| account-timer-*.txt | Accounts, budgets, themes, settings and window positions |
| account-timer-error.log | Problems and the drawing speed report |

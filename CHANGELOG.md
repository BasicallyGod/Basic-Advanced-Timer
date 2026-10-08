# Changelog

Basic’s Advanced Timer grew from a single billable-hours counter into a full timesheet tracker. Only `AccountTimer2.ps1` remains in the current working tree; the earlier implementations and their launchers are available in Git history.

Version numbers were assigned after the fact, one per round of changes, all built in October 2026.

| Script | What it is | Versions |
| --- | --- | --- |
| AccountTimer2.ps1 | Current app and only supported launcher target | 2.0 to 2.23 |
| AccountTimer.ps1 | Historical predecessor, removed from current tree | 1.0 to 1.7 |
| BillableTimer.ps1 | Historical predecessor, removed from current tree | 0.1 to 0.5 |

## Repository maintenance after v2.22

- Removed the unused historical scripts `AccountTimer.ps1` and `BillableTimer.ps1`, together with `StartAccountTimer.vbs` and `StartTimer.vbs`. The current timer and its launcher never load these files.
- Kept the existing filenames `AccountTimer2.ps1` and `StartAccountTimer2.vbs` so existing shortcuts and portable installations continue to work.
- Refreshed `README.md`, `CLAUDE.md` and the application header to describe the current timeline, non-destructive groups, credited-time positions and decimal exports.
- Kept `tests/`, Windows CI, `.gitignore` and `.gitattributes`. They are development and data-safety infrastructure, not dead files.
- This was repository housekeeping only. **The application remains v2.22**, and historical sources remain accessible in Git history.

## Version history

| Version | Script | Type | Change |
| --- | --- | --- | --- |
| 2.23 | AccountTimer2 | Improved | Consolidate 13 preference files into one portable versioned JSON settings store; preserve the original text files in a migration backup |
| 2.23 | AccountTimer2 | Improved | Use atomic settings updates with the previous JSON version retained as a backup; reject unreadable or unsupported settings instead of silently resetting |
| 2.23 | AccountTimer2 | Improved | Rename the Windows application, dialogs, tray text and theme header to Basic’s Advanced Timer, while retaining script and launcher filenames for compatibility |
| 2.23 | AccountTimer2 | Improved | Keep session records, theme definitions, away intervals and crash recovery separate from preferences; add synthetic Windows migration tests |
| 2.23 | AccountTimer2 | Improved | Rebranded visible windows, dialogs, notification icon and Windows application identity as Basic’s Advanced Timer while keeping script and launcher filenames compatible |
| 2.23 | AccountTimer2 | Improved | Consolidated 13 legacy preference text files into a portable JSON settings store with automatic migration, atomic writes, prior-version backups and archived originals |
| 2.23 | AccountTimer2 | Improved | Retained sessions, undo records, crash-recovery state, away periods and editable theme catalogue in separate dedicated files |
| 2.23 | AccountTimer2 | New | Added synthetic PowerShell settings migration and branding regression coverage to Windows CI |
| 2.22 | AccountTimer2 | Fixed | Two-decimal weekly summary and detailed entry exports now reconcile exactly, including rounding, grouping, adjustments and positioned credit |
| 2.22 | AccountTimer2 | Improved | Assign deterministic integer 0.01h increments across account/day and detailed rows rather than rounding each independently |
| 2.22 | AccountTimer2 | Improved | Completed weekly views follow the exported decimal allocation; live active time remains provisional |
| 2.22 | AccountTimer2 | Fixed | Reject exporting detailed totals if any credited source session is missing from the projected entries; handle Lunch separately |
| 2.21 | AccountTimer2 | Fixed | Fractional-second session boundaries no longer cause valid timeline gaps to fail allocation validation; safely snap inward after verifying the unrounded selection |
| 2.21 | AccountTimer2 | New | A timeline gap offers positioning existing merged credit without added hours, or adding new credited time |
| 2.21 | AccountTimer2 | New | Position metadata projects original merged credit into the timeline and weekly entries without modifying the credited source rows |
| 2.21 | AccountTimer2 | Improved | Manual weekly grouping preserves every original session and note rather than creating a destructive synthetic merged row |
| 2.21 | AccountTimer2 | Improved | Group and credit-position metadata are portable CSV files with undo snapshots; positioned time can be adjusted in both editing views |
| 2.21 | AccountTimer2 | Fixed | Removed clipped short-switch zoom badges; short intervals remain drawn at their recorded positions and can be inspected by zooming in |
| 2.21 | AccountTimer2 | Fixed | Clearing today's account time now includes corresponding group/position metadata in the undoable edit |
| 2.20 | AccountTimer2 | Fixed | Legacy merged credits are no longer drawn as imaginary continuous sessions; their total appears separately as unplaced time in the timeline totals |
| 2.20 | AccountTimer2 | Improved | Added and edited entries keep their chosen time positions but have a dashed outline, while rounding stays out of the clock timeline |
| 2.20 | AccountTimer2 | Fixed | Recorded stretches shorter than a minute are retained in timeline gap detection and weekly entries; automatic weekly grouping now requires adjacent real sessions |
| 2.20 | AccountTimer2 | Improved | The weekly entries export identifies merged, added and edited rows, leaves unknown merged clock ranges blank, and warns before a destructive merge |
| 2.20 | AccountTimer2 | Fixed | Timeline clock and duration labels use the same truncate-to-minute convention as the tooltips |
| 2.19 | AccountTimer2 | Fixed | Timeline card text uses the selected theme where legible, with light or dark text chosen for contrast on each account colour |
| 2.19 | AccountTimer2 | Improved | Timeline text uses greyscale GDI+ antialiasing at different sizes while the rest of the drawer keeps its Windows text rendering |
| 2.18 | AccountTimer2 | Fixed | Stretches that overlap, such as merged or hand-edited entries, now sit side by side on the timeline instead of hiding each other, and dragging an edge only stops at the neighbour in its own lane |
| 2.18 | AccountTimer2 | Fixed | An entry edited to run past midnight is drawn to the end of the day instead of going missing from the timeline |
| 2.18 | AccountTimer2 | Improved | Pointing at a merged, edited or added entry on the timeline says how it was made, and that merged times are approximate |
| 2.18 | AccountTimer2 | New | A Size setting under Appearance, from Smaller to Largest, that scales the whole timer and drawer, is remembered, and makes menus larger too |
| 2.18 | AccountTimer2 | Improved | Larger text on the timeline and the drawer's buttons, and taller account totals rows |
| 2.17 | AccountTimer2 | Improved | The timeline is now a vertical day view like a calendar, with hours down the left and each stretch of time as a card showing its name, times and length, so short stretches are readable and easy to drag |
| 2.17 | AccountTimer2 | New | Scroll the timeline with the mouse wheel and zoom with Ctrl and the wheel or the plus and minus buttons. It opens with now in view, and a Now button jumps back to it |
| 2.17 | AccountTimer2 | Improved | The timeline has an hour of room either side of the day for adding time, the account totals sit in a column beside it, and the drawer is at least 420 tall |
| 2.16 | AccountTimer2 | Improved | Unallocated time, in the day and in overtime, is shown as h:mm (for example UNALLOCATED OT 0:45), whatever the hours setting is |
| 2.16 | AccountTimer2 | Improved | The Finish day button now sits under the Lunch row and only appears from 30 minutes before the end of the day, staying as Carry on once the day is finished |
| 2.15 | AccountTimer2 | New | Each working day can have its own start and finish times, for shift patterns, set under Working hours, Hours and days, with a Same every day button |
| 2.15 | AccountTimer2 | New | A Finish day button appears in the overtime section, and Finish for the day is in the menu, to say the day is over. Unallocated time and overtime stop counting and the red not tracking state stays off, until an account is started or Carry on is pressed |
| 2.15 | AccountTimer2 | Improved | The drawer opens on the Timeline tab, which is now the first tab, with the Week tab second |
| 2.14 | AccountTimer2 | New | Drag the start or finish edge of a block on the timeline to change its time, snapping to 5 minutes (hold Shift for 1 minute) and stopping at its neighbours, and at now for today |
| 2.14 | AccountTimer2 | New | Where two blocks touch, one drag moves the change-over between them, and a note stays with its block |
| 2.14 | AccountTimer2 | New | Click a gap on the timeline to fill it with an account, or drag across part of the gap to fill just that stretch |
| 2.14 | AccountTimer2 | New | An Undo button on the timeline, and Escape cancels a drag in progress |
| 2.13 | AccountTimer2 | New | Timeline tab in the drawer, showing a day along a strip with a block for each stretch of time, Lunch as a hatched gap, working hours lightly shaded and a marker for now |
| 2.13 | AccountTimer2 | New | Point at a block on the timeline to see the account, start and finish, length and note, and use Previous and Next to move between days |
| 2.13 | AccountTimer2 | Improved | The drawer remembers whether it was showing the week or the timeline, and the menu now says Show drawer and Hide drawer |
| 2.12 | AccountTimer2 | Fixed | Tooltips no longer hidden behind the timer when it brings itself to the front |
| 2.12 | AccountTimer2 | New | Usual working hours and days, week start day, and a today-only box for different hours or a day off |
| 2.12 | AccountTimer2 | New | Days off show DAY OFF and never count as unallocated, overtime or not tracking |
| 2.12 | AccountTimer2 | New | Unallocated time split into working day and overtime, with both shown on hover |
| 2.12 | AccountTimer2 | New | Overtime while tracking gets its own purple colours, separate from the red not tracking state |
| 2.12 | AccountTimer2 | New | Hours shown as decimal (1.25) or hours and minutes (1:15), switchable from the menu or the week drawer |
| 2.11 | AccountTimer2 | Improved | Budgets in the week drawer show the period once in the heading instead of on every row |
| 2.11 | AccountTimer2 | Fixed | Week drawer text drawn by Windows, crisp and without ClearType colour fringing |
| 2.11 | AccountTimer2 | Improved | HOURS, MINUTES and SECONDS labels removed, making the full view 12 pixels shorter |
| 2.11 | AccountTimer2 | Improved | Clearer corner buttons, moved in from the edges |
| 2.11 | AccountTimer2 | Improved | Tooltips in the theme's colours instead of black on beige |
| 2.11 | AccountTimer2 | Fixed | Weekly view text no longer thickens when scrolling |
| 2.10 | AccountTimer2 | Improved | Full view drawing moved to compiled C#, with automatic fallback to the old drawing |
| 2.10 | AccountTimer2 | New | The timer draws its own soft shadow and rim, following whatever shape is showing |
| 2.10 | AccountTimer2 | Fixed | Blur no longer shows as a frosted border, and stays on with the week drawer open |
| 2.9 | AccountTimer2 | New | Drawing speed report under Appearance, timing each redraw by view |
| 2.9 | AccountTimer2 | Improved | Week drawer drawn once and reused, cutting redraw time from 74 ms to 47 ms |
| 2.8 | AccountTimer2 | New | Week drawer beside the full timer, with hours by account and day, budgets, week navigation and copying |
| 2.8 | AccountTimer2 | Improved | Drawer button moved to the top left so it can't be confused with the Lunch row |
| 2.8 | AccountTimer2 | Improved | Dragging the full view handed to Windows for smooth movement |
| 2.8 | AccountTimer2 | Fixed | Grey corner beside the timer when the drawer was open |
| 2.7 | AccountTimer2 | New | Its own icon in the theme's colours, its own taskbar identity and a themed weekly view title bar |
| 2.7 | AccountTimer2 | Fixed | Weekly view buttons and table headings drawn smoothly, and chart text drawn by Windows |
| 2.6 | AccountTimer2 | New | Time entries can be merged, deleted, moved to another account, re-timed or added, with Undo |
| 2.5 | AccountTimer2 | New | Pill shadow, rim, breathing glow when not tracking and a live dot while recording |
| 2.5 | AccountTimer2 | Improved | Right-click menu regrouped into Appearance and Open file submenus |
| 2.4 | AccountTimer2 | New | Time entries tab showing each stretch of time by day, with consecutive stretches merged |
| 2.4 | AccountTimer2 | New | Weekly entries file listing every stretch of time with its note |
| 2.3 | AccountTimer2 | Fixed | Budget bars and the day progress bar stuck at empty or full, caused by a rounding quirk |
| 2.3 | AccountTimer2 | Fixed | Weekly view failing to open, caused by a clashing variable name |
| 2.2 | AccountTimer2 | New | Budgets per account, weekly, monthly or quarterly, shown as a bar with figures on hover |
| 2.2 | AccountTimer2 | New | Optional note prompt after 15 minutes or more on an account |
| 2.2 | AccountTimer2 | New | Weekly view with a chart, hours table, budgets and notes |
| 2.1 | AccountTimer2 | New | Pill expands into its own account list instead of a Windows menu |
| 2.1 | AccountTimer2 | Fixed | Pill corners showing the backdrop and jagged pill edges |
| 2.1 | AccountTimer2 | Fixed | Right-clicking the pill's list picked the wrong account |
| 2.0 | AccountTimer2 | New | Compact pill view, switchable with the full view, with each view's position remembered |
| 2.0 | AccountTimer2 | New | Red not tracking state whenever nothing is running during the working day |
| 2.0 | AccountTimer2 | New | Refuses to start a second copy, so time is never tracked twice |
| 1.7 | AccountTimer | New | See-through account list with sharpened text and optional blur |
| 1.7 | AccountTimer | Fixed | Red halo on see-through text, and a laggy snapshot blur replaced |
| 1.7 | AccountTimer | Improved | Account names use the full width unless the rounding buttons are showing |
| 1.6 | AccountTimer | New | Fourteen themes in an editable themes file, with reload from the menu |
| 1.5 | AccountTimer | New | Round today's time up or down to 15 minutes, and set today's time by hand, for accounts and Lunch |
| 1.4 | AccountTimer | New | Overtime mode after the end of the day, with its own counter and red colours |
| 1.3 | AccountTimer | Improved | Lunch set apart from the accounts with a gap and a darker band |
| 1.2 | AccountTimer | New | Drag handles to reorder accounts |
| 1.1 | AccountTimer | New | Add, hide and bring back accounts, up to 12 showing at once |
| 1.0 | AccountTimer | New | Account slots with play and pause, one running at a time, logged to sessions.csv with a weekly summary file |
| 1.0 | AccountTimer | New | Running time pauses when the laptop sleeps, and is recovered after a crash |
| 0.5 | BillableTimer | Fixed | Soft text on monitors with different scaling, and oversized menus on the 1080p screen |
| 0.4 | BillableTimer | Improved | Redesigned as a compact panel with bold digits, circular buttons and green and orange states |
| 0.4 | BillableTimer | New | WORKING status and an always-visible lunch total |
| 0.3 | BillableTimer | New | Tray icon, error log, and recovery when the timer is hidden or off screen |
| 0.2 | BillableTimer | New | Lunch button that pauses the count, remembered across restarts |
| 0.1 | BillableTimer | New | Always-on-top timer counting from 08:30 to 17:30, resetting at midnight, with no install needed |

## Possible future work

- [ ] Investigate Windows-drawn text for the main timer's solid areas, matching the drawer's text rendering. This is an idea for evaluation, not a scheduled release.

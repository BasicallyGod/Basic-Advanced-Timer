# Changelog

## Avalonia migration — 9 October 2026

The entries below follow the migration milestones, newest first. They describe the separate Avalonia preview on `migration`; the original PowerShell application remains v2.32. Migration builds have not yet been assigned release version numbers.

## Migration click selection and deliberate nudging

- Replaced Ctrl+click with normal click selection in Timeline and Week. Dragging does not select blocks; right-click offers editing/inspection and Merge selected, alongside the toolbar Merge action.
- Kept forgiving neighbour snapping for small overshoots. Moving more than 24 display-independent pixels past a neighbour deliberately pushes it, rippling through additional blocks as needed.
- Preview nudged source blocks during the drag, preserve their durations and notes, and save the complete move as one journalled edit with persistent Undo.
- Reject pushes that would cross midnight or collide with positioned credit, running time or unresolved imported overlaps, without partially applying the move.

## Migration drag insertion and multi-step Undo

- Dragged blocks insert between crossed neighbours, moving those entries into the space left behind instead of jumping past the entire run.
- Small overshoots still nestle; resize edges remain clamped. Neighbour movements preview before release, preserve durations and notes, and commit as one undoable action.
- Undo now retains up to 50 edits across restarts, imports the previous single-step history, and preserves subsequently recorded sessions. The Undo button shows the available step count.

## Migration leapfrogging, selection and timeline zoom

- Ordinary clicks toggle block selection; Merge remains available in the toolbar and right-click menu.
- Small drag overshoots nestle against neighbours. Larger whole-block drags leapfrog occupied intervals without shifting neighbouring entries; resizing retains boundary clamping.
- Ctrl+mousewheel zooms the timeline between 60 and 640 pixels per hour, keeping the time under the pointer anchored. Ordinary wheel scrolling remains available.
- Timeline scale survives view refreshes; changing date resets the scroll position to the first entry.

## Migration drag snapping and merge-menu fixes

- Fixed a selected block disappearing when its right-click menu opened. Pointer capture loss now restores a block's geometry only when a left-button gesture is actually in progress.
- Made moving and resizing snap to neighbouring blocks' boundaries, including times such as 09:53 that fall between five-minute marks. Five-minute increments remain the default away from neighbours.
- Made overshooting stop at the first neighbouring block instead of producing an overlap error or jumping past it. This applies to whole-block moves and both resize edges.
- Kept real overlap validation and Undo, and accounted for fractional legacy end times when saving snapped entries.
- Extended native input regression checks for menu capture loss, cancelled drags, exact-boundary snapping, substantial overshoots and Undo. All 49 tests pass on Windows, Linux and macOS; packaged Windows normal/demo launch checks also pass.

## Migration block selection, gap merges and click-to-add

- Added Ctrl+left-click selection in Timeline and Week, visible selection outlines, a toolbar Merge button and right-click → Merge selected.
- Kept selection separate from moving/resizing and opening editors. Escape and date changes clear selection.
- Initially restricted new merges to adjoining blocks and rendered older groups with gaps as separate linked sections, avoiding a continuous block over uncredited time.
- Revised merging to allow blank gaps between blocks for the same account/day. Those gaps now become “Added by merge” credited entries and count towards the combined total. The selection hint previews the amount added.
- Continue to reject merges across intervening entries, positioned credit, running time or overlaps. Existing groups can be extended when all their source parts are selected.
- Preserve original source rows. One Undo removes the added gap time and restores prior grouping; Ungroup keeps the newly credited gap entries.
- Added single-click entry creation on empty timeline space, with a five-minute-snapped start and a proposed 30-minute duration capped at the next occupied interval or midnight. Only one entry editor opens at a time.

## Migration grouped-block presentation and dashboard polish

- Made compatible grouped intervals appear as a combined timeline block and Week entry, with access to their original parts.
- Added right-click Ungroup, a visible Week Ungroup action and existing-group access through Tools → Group entries.
- Allowed future manual entries and credit placements; their time is credited immediately.
- Replaced oversized scrollbars and arrow buttons with an eight-pixel track and slim draggable thumb.
- Restored the dashboard's transparent rounded window surface to match the pill.

## Migration credit review and weekly exports (repository development)

- Restored non-destructive positioning of merged credit, with position notes, editing, unplacing and compatible metadata import.
- Added same-day/account source grouping, grouped Week entries and ungrouping without rewriting credited sessions.
- Added reviewed Tidy day gap fills, excluding lunch/away/running intervals, with stale-preview rejection and one batch Undo.
- Reconciled integer hundredths across detailed entries, account/day summary cells and weekly totals, including negative adjustments and separate Lunch totals.
- Saved export pairs to new folders and journalled position metadata with the ledger, notes, groups and persistent Undo.
- Added synthetic accounting/storage checks and native UI input/rendering checks; sample data now includes merged credit and a short gap.

## Migration timeline editing, notes and Undo (repository development)

- Restored entry time/account/note editing and deletion, plus five-minute snapped timeline move/resize gestures and new credited time in free gaps.
- Added individual entries beneath Week totals, including notes-only access for merged legacy credit and adjustments.
- Preserved CSV extra columns and moved notes with edited source keys; pruned obsolete group references without changing positioned merged credit.
- Added a persistent last-edit Undo snapshot that retains later recorded sessions, saved together with sessions, notes and group metadata through journal replay.
- Retained exact credited durations when only notes change, including fractional-minute short segments.


## Migration pill layout, dragging, settings and demo

- Restored the familiar compact pill: timer on the left, account and today's credit in the middle, pause/resume on the right.
- Clicking the timer opens the dashboard; clicking the account toggles the small account list. Back to pill returns from the dashboard.
- Separated clicks from dragging with a five-pixel movement threshold, so dragging over timer, account and pause controls does not activate them. Added dashboard header dragging.
- Made tracking and idle states visibly distinct, with a green tracking clock, grey idle clock and independent red overtime indication.
- Corrected timeline startup scrolling and retained its position during updates.
- Added an isolated, populated sample-data demo and a Force overtime setting for checking both themes and tracking states without saving personal data.
- Added weekday working-hour/shift settings, account visibility/addition and budget editing, with persisted preferences in normal mode.

## Migration tracking and storage (repository development)

- Connected a platform-independent C# tracking core, separating the day clock from account session clocks.
- Ported scheduled/early starts, lunch, finish/resume, overtime, midnight splitting and credited adjustments.
- Added compatible copied-data loading and persistent sessions/settings/away/recovery with locking, atomic backups and journal replay.
- Replaced illustrative Timeline/Week data with recorded entries and credited totals; preserved unplaced legacy credit.
- Moved light/dark/system appearance into Settings and right-click menus.
- Added synthetic core/storage regression cases. At this stage, editing, metadata presentation, exact export reconciliation and tray integration remained to be migrated; the subsequent milestones above restore the first three.

## Migration foundation (repository development)

- Created `migration` from experimental v2.32; kept the existing application and launcher intact.
- Added an isolated Avalonia desktop preview with pill/dashboard windows, shared System/Light/Dark themes, vector budget indicators and illustrative Timeline/Week views.
- Made Not tracking grey and overtime red, with independent recording/overtime indicators.
- Added production-style headless input/rendering checks, cross-platform CI and self-contained Windows preview packaging.
- Documented staged core/storage/editing migration and the preview's synthetic, non-persistent data boundary. No production tracking behaviour or file format is changed.

Basic’s Advanced Timer grew from a single billable-hours counter into a full timesheet tracker. The original Windows application remains in `AccountTimer2.ps1`, alongside the separate Avalonia work under `migration/`. Earlier PowerShell implementations and their launchers are available in Git history.

The PowerShell version numbers below were assigned after the fact, one per round of changes, all built in October 2026.

| Script | What it is | Versions |
| --- | --- | --- |
| AccountTimer2.ps1 | Current app and only supported launcher target | 2.0 to 2.32 |
| AccountTimer.ps1 | Historical predecessor, removed from current tree | 1.0 to 1.7 |
| BillableTimer.ps1 | Historical predecessor, removed from current tree | 0.1 to 0.5 |

## v2.32

- Made dashboard hover, active and drag highlights span the full account column.
- Removed the top Pause action and its pointer handlers; account play/pause controls remain the tracking controls.
- Integrated Lunch into the account list with the same row height and spacing.
- Increased the dashboard timer from 28px to 40px while preserving the live status and day action.
- Extended renderer checks with full-width active/hover assertions and a hover preview.

## v2.31

- Separated dashboard drag grips from budget rings, removed the duplicate idle status, and centred the pill account details vertically.
- Increased the initial Timeline zoom to 192 pixels per hour so short entries are easier to read and edit.
- Applied the selected hours/minutes or decimal format to allocated/unallocated totals and both parts of budget usage.
- Removed the extra Lunch separator and replaced the skewed cutlery icon with a shared upright glyph.
- Made DPI updates use the same monitor-fit calculation as dashboard layout, and deferred drawing until a view switch is complete.
- Added regression coverage for fractional budget formats, default timeline zoom, grip spacing and a fitted view transition without intermediate frames.

## v2.30

- Refactored the dashboard into shared layout and typography services, separate chrome, live strip, account list, sidebar summary and Week components, alongside the existing Timeline component.
- Centralised dashboard row bounds, scroll limits, button rectangles and timeline height. Drawing and pointer hit tests use the same geometry.
- Fixed startup using 30px rendered rows with 48px hit targets. Renderer creation and every layout refresh now configure the dashboard from the shared layout.
- Unified dashboard text on grayscale GDI+ antialiasing, including header, tabs, timeline labels and week cells. The working pill rendering remains unchanged.
- Renderer tests now use the actual startup factory and font assignments, and exercise pointer positions above, within and below rows before/after scrolling at 80%, 100%, 125%, 150% and 200% scaling, with rendered previews for each view.

## v2.29

- Matched the approved dashboard concept: pale Sage surfaces, a distinct live timer strip, two-line account rows with budget details, and allocated/unallocated totals in the sidebar.
- Placed Timeline/Week tabs beside date navigation, with actions on one tools row. Removed the duplicate timeline legend so cards use the full workspace; account and week lists scroll within the fixed window.
- Added softer timeline cards with account-coloured edges and matching pill colours, a seconds clock and larger account spacing.
- Disabled blur and hid obsolete transparency controls. Existing blur preferences cannot re-enable the backdrop effect.
- Kept the date header, live dot, budgets, day controls, recording, editing and exports.

## v2.28

- Made the pill the default small tracking view and replaced the expanding full view with a fixed 960 by 640 dashboard, scaled to fit the screen.
- Added today's date, current account, live recording dot, Pause, Start/Finish day and Back to pill in a stable dashboard header.
- Kept account controls on the left and Timeline or Week on the right, with consistent action styling and no content-driven resizing.
- Added budget utilisation rings, usage tooltips, Dashboard and Start/Finish day to the expanded pill. Rings honour each account's configured budget period.
- Extended Windows rendering checks to cover maximum account capacity, both dashboard tabs, fixed sizing, navigation hit targets and actual pill rendering.

## v2.27

- Added Tidy day in the timeline drawer, with a preview of small gaps and per-gap choices of the preceding account, following account or Leave gap.
- Uses a five-minute default threshold, adjustable from 1 to 30 minutes. Excludes lunch, away intervals, running neighbours and unbounded gaps.
- Applies selected fills as one undoable batch of new credited entries without moving original timestamps, and rejects stale or invalid previews before adding any rows.
- Added synthetic checks for sub-minute and fractional-second gaps, account choices, exclusions, stale previews and credited totals.

## v2.26

- Restored Windows blur with an open drawer by extending the normal timer backdrop to match the drawer height.
- Kept the day button at the bottom of the extended panel with matching hit testing. Closing the drawer restores the compact normal height.
- Updated renderer checks to verify the filled backdrop and footer alignment.

## v2.25

- Removed the filled blank corner below the timer when the drawer is taller, with correct shapes on either side.
- Enlarged drawer text to 14px and timeline text to 15px, increased row spacing and widened the drawer and totals column.
- Moved navigation onto a separate row, enlarged button targets, and increased text and rows in the separate weekly window.
- Added Windows renderer compilation and drawing checks for transparent corners and button bounds.

## v2.24

- Start day and Finish day are available throughout the day in the full window and right-click menu, including before scheduled hours and during overtime.
- Early starts count from the exact press time and persist for that date only. Starting a client account early also starts the day.
- Finishing stops the active account and freezes unallocated time; restarting excludes the break and retains work already completed.
- Added synthetic regression checks for early starts, persistence, finishing and resuming, days off and overtime.

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
| 2.23 | AccountTimer2 | Improved | Keep session records, theme definitions, away intervals and crash recovery separate from preferences |
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

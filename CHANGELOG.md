# Changelog

## Check your version

The Avalonia preview on `migration` shows `Version 0.1.<build> (<commit>)` above Close timer in the right-click menu. The build number is the preview workflow run number; the commit identifies the exact source. Local builds show development build. The original PowerShell application remains v2.32.

The milestones below are newest first. Commit links mark when each change landed; they are not release version numbers. Use the [migration commit history](https://github.com/BasicallyGod/Basic-Advanced-Timer/commits/migration/) for individual changes and [preview builds](https://github.com/BasicallyGod/Basic-Advanced-Timer/actions/workflows/avalonia-preview.yml) to match a packaged version to its commit.

## Avalonia migration

### 10 October 2026

| Milestone | Commit | What changed |
| --- | --- | --- |
| Tab bar and compact account rows | pending | Dashboard tabs are plain words on a hairline with a green bar under the chosen one, closer to the mock-up, and the day arrows are lighter. Dashboard account rows are 54 pixels instead of 68, so about two more accounts show before scrolling. The pill's rows are unchanged. |
| Slimmer rows and a tidier action row | pending | Pill and dashboard account rows are 52 pixels with a smaller ring and play button. Zoom and fit moved from a floating bar into the action row beside Tools, shown only on the Timeline tab. A disabled button keeps its outline, dimmed, instead of a solid grey fill. The "Recorded time · Click blocks to select" line is gone and only appears to report a selection. Icons now redraw when the theme changes. |
| Tools menu retired | pending | Group entries is gone from the menus (Merge still groups, and clicking a group still opens it to inspect or ungroup). Tidy day is offered when you right-click empty timeline space. Export week is an icon button in the action row on the week tabs. Zoom stays on the Timeline tab. |
| Overview, headings and clock polish | pending | The week tabs are now Timeline, Week view, Overview and Entries. Export week and Copy hours table are icons on the Overview tab's table heading. Hours and minutes and Budgets are proper headings with a rule between them. The menu item is now Add time. The clock is bolder in the pill and the dashboard. Today allocated and Unallocated sit in small type under the dashboard clock, and the pill shows a small unallocated figure under its clock while the day runs. |
| Dashboard rebuilt around the day | pending | Following the mock-up, the left column now holds the date, clock, tracking state, Allocated and Unallocated figures, the accounts, then Take a break and Finish day (with a flag icon) at the foot. The right column has settings and minimise in its top corner, then the tabs and the review. The day button moved out of the header. |
| Day button, clock and footer polish | pending | Break and Finish day use the same type. Start day is green and Finish day red, with the flag in the same colour, and the flag waves and the cup steams under the pointer. Messages now appear only under the review, so they never change the left column's height. Allocated and Unallocated figures are smaller, and the clock is full-strength text when nothing is running, in the pill and the dashboard. |
| Review tweaks | [090d9c4](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/090d9c4b76b6e7645b8a1f07152999c7fb4359d2) | Wider gap between dot and name in Overview. Selection shows by border only. The bottom bar appears only for a message. Today allocated and Unallocated are aligned in two columns. Settings is a gear and Back to pill a minimise bar. The idle pill glow is gone. |
| Short-block and colour visibility | [4e086c8](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/4e086c83e3d3b642f2bcdc0a6e1639b6ee6849da) | Short blocks remain selectable and keep labels inside their bounds. Very dark colours are brightened when drawn without changing saved colours; outlines and idle pill glow removed. |
| Day-clock corrections | [7a6bb2d](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/7a6bb2d11694b2c567b21c493c6906a85bc73eb9) | Changing working hours clears unnecessary manual starts while preserving genuine early starts. Days off count only tracked work, with no unallocated time or overtime. |
| Account colours, database step 9 | [2233e76](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/2233e7656a1afc8cdccb6933ebeae086de226005) | Sixteen palette colours and custom #RRGGBB colours, saved per account. Timeline text adapts for contrast. |
| Legacy credit removal, database step 8 | [3cbe9d5](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/3cbe9d5a8d5553f9e8970d5aca9b32c07d8a096d) | Removed unpositioned merged credit, credit placement and old Undo imports. Every non-adjustment entry has a clock position. Grouping and gap-filling Merge remain. |
| Export validation, database step 7 | [9d6c692](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/9d6c692716195b38dac73c6c36b4790385f651fe) | Weekly exports check saved database totals before writing. Decimal detail, daily and weekly totals still reconcile exactly. |
| Database file release | [01a7af6](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/01a7af675c392f2c633ee32b4a894d988a864bd6) | Closing releases timer.db promptly on Windows so its folder can be copied or deleted. |
| Account identity, database step 6 | [7ce1b1e](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/7ce1b1ef544afcda0f7238012535056fb054abee) | Renames apply to all history after a backup and clear Undo. Existing names cannot be combined; add an account to start afresh. |
| Typed breaks, database step 5 | [13d977a](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/13d977a0b9331b5f32271cb4421199e126a30210) | Breaks are separate entries with a saved return account. Break is reserved; Lunch is available as an ordinary account name. |
| Database Undo, database step 4 | [ec26cff](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/ec26cff11d5e92f54205396fb3f8fdf939f3fe27) | History moves to edits tables and saves incrementally, retaining the 50-step/2 MB limits. Previous test-build history is not carried over. |
| SQLite storage, database step 3 | [5b3a313](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/5b3a313ab0a06ad2c50c6b67a40538786d948f9c) | Accounts, entries and tracking state move to timer.db with transactional saves. New databases start empty; old Timesheets files are left untouched and are no longer imported. Preferences remain JSON. |
| Database foundation, database step 2 | [a8c4204](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/a8c4204ec8adb50c953e3845db62a7edd9732b75) | Added schema validation, exclusive access, format upgrades and fourteen daily backups. Not yet connected to the app at this step. |
| Visible build identity | [795fc7c](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/795fc7c2a62bc7d5d567a1d002d28addbb28b920) | Published previews show the workflow build number and source commit in the menu. |
| SQLite dependency, database step 1 | [aa17cda](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/aa17cdaf4ceb5540116abd71ca9124e24a6ad330) | Added the library and a packaged startup check before changing storage. |
| Test-build updater | [dfa8f63](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/dfa8f6361c19903d331154c12bb57498cd0ae268) | Added an isolated update, launch and rollback helper for Windows preview testing. |
| Stable account colours and timeline controls | [356a9ca](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/356a9ca27cad38f13cbf8f68bbc61beaf6e90bd9) | Colours survive reordering and renaming; timeline zoom/fit controls and animated account reordering added. |
| Adjust time today | [3637ecf](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/3637ecfc460e586b3e341df60674ed8982602352) | Signed daily adjustments with notes, included in totals and exports, with editable/deletable timeline chips. |
| Dashboard and weekly review | [7988bdb](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/7988bdb82d8c79c7e3a8f56fecd1a100da824475) | Timeline, Overview, Hours table and Entries share one tab row. Budget meters, compact pill controls and a larger demo added. Past-day overtime stops at the last activity. |

### 9 October 2026

| Milestone | Commit | What changed |
| --- | --- | --- |
| Weekly reporting and breaks | [d0851eb](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/d0851eb84b077fc1a8320fdf4f8d39bda6a37bfe) | Added weekly totals, budgets, copyable hours and Move to account. A separate Break button resumes the previous account; break and overtime tint the pill. |
| Windows integration | [e49eb5e](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/e49eb5ecffeebfc2363e91c8dfbf6f67573ceabe) | Added tray controls and optional start with Windows. Hiding keeps tracking; Close timer saves and exits. |
| Account management | [723d125](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/723d12509f6f30acc7a9245a5636457f158729a1) | Added renaming and drag reordering. The original choice to rename history or start afresh is replaced by database step 6. |
| Timeline editing | [07d16f0](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/07d16f0948caa2286b881346b2f08c4455a3620e) | Double-click empty space to add time; Delete/Backspace removes selections. Short-block resizing, day limits and recording-block protection improved. |
| Compact persistent Undo | [a271c89](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/a271c892ad70ffd76b1cda5383f6bd54148f8471) | Undo stores small changes, survives restarts and refuses to overwrite outside edits. History is capped at 50 steps and about 2 MB. |
| Selection, moves and zoom | [5275696](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/52756969ffaec7988a2335f36d5e11ec97f35815) | Click to select, batch Merge/Undo, pointer-anchored zoom and drag insertion between neighbours. Merge credits blank gaps; Ungroup keeps that added time. |
| Review and exports | [e4604f3](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/e4604f31f8c3d0400b0535112eb370d9fabccc0d) | Restored grouping, reviewed Tidy day and reconciled weekly exports. Legacy credit placement introduced here is removed by database step 8. |
| Editing and notes | [9363835](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/9363835f92195fb5394bc590840491d0b216553f) | Restored entry editing/deletion, timeline move/resize, gap additions, notes and persistent Undo. |
| Pill, settings and demo | [378046f](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/378046fe1dd02251c93299cd497d8f6b5cb066bb) | Restored compact tracking controls, dashboard navigation, working hours, budgets and an isolated demo. |
| Real tracking and storage | [a3cb0dd](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/a3cb0dd8f361925327d37f4c74e9f493e749d280) | Connected the C# tracking core, early starts, finish/resume, overtime and checkpoint recovery. Copied CSV storage introduced here is replaced by database step 3. |
| Avalonia foundation | [7d7c2ed](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/7d7c2ed4c81b5c5e5553ec504f95dbf4d1d16792) | Separate pill/dashboard preview with shared themes, cross-platform checks and a portable Windows package, starting from PowerShell v2.32. |

## PowerShell version history

These versions were assigned retrospectively to rounds of changes in October 2026. `AccountTimer2.ps1` covers v2.0 to v2.32; earlier scripts are preserved in Git history.

| Version | Main change |
| --- | --- |
| 2.32 | Full-width account highlights, integrated Lunch row and larger dashboard clock. |
| 2.31 | Clearer spacing and hours formats, timeline zoom and monitor-fit transitions. |
| 2.30 | Shared dashboard layout and typography; drawing and hit targets agree. |
| 2.29 | Sage dashboard redesign with live strip, budget details and scrolling lists. |
| 2.28 | Compact pill becomes the default; fixed dashboard and budget rings added. |
| 2.27 | Reviewed Tidy day fills small gaps as one undoable edit. |
| 2.26 | Restored drawer blur/backdrop and aligned day controls. |
| 2.25 | Larger drawer text, spacing and navigation targets. |
| 2.24 | Start/Finish day throughout the day; exact early starts and finish/resume accounting. |
| 2.23 | Basic's Advanced Timer branding and one portable JSON settings file with safe migration/backups. |
| 2.22 | Decimal weekly summaries and detailed exports reconcile exactly. |
| 2.21 | Non-destructive groups, existing-credit placement and fractional-gap fixes. |
| 2.20 | Separated recorded timeline positions from legacy credited totals; retained short sessions. |
| 2.19 | Theme-aware timeline text and improved antialiasing. |
| 2.18 | Overlapping timeline lanes and a persisted interface Size setting. |
| 2.17 | Vertical, scrollable and zoomable day timeline. |
| 2.16 | Unallocated time shown as h:mm; Finish day moved under Lunch. |
| 2.15 | Weekday shift patterns and Finish/Carry on controls. |
| 2.14 | Timeline edge editing, gap filling and Undo. |
| 2.13 | First timeline view, day navigation and entry tooltips. |
| 2.12 | Working hours/days, day overrides, overtime and decimal/h:mm formats. |
| 2.11 | Clearer drawer text, controls and tooltips. |
| 2.10 | Compiled C# full-view renderer, shadows and blur fixes. |
| 2.9 | Drawing-speed report and cached week rendering. |
| 2.8 | Week drawer with daily hours, budgets and copying. |
| 2.7 | Application icon/identity and themed weekly view. |
| 2.6 | Time-entry editing, merging, deletion, reassignment and Undo. |
| 2.5 | Pill shadow, glow and live recording indicator. |
| 2.4 | Detailed time-entry view and weekly entry exports. |
| 2.3 | Fixed budget/progress bars and weekly-view startup. |
| 2.2 | Account budgets, note prompts and weekly reporting. |
| 2.1 | Expandable pill account list and input/rendering fixes. |
| 2.0 | Compact pill, remembered positions, idle warning and single-instance protection. |
| 1.7 | Transparent account list, sharper text and blur fixes. |
| 1.6 | Editable themes. |
| 1.5 | Manual totals and quarter-hour rounding. |
| 1.4 | Overtime counter and colours. |
| 1.3 | Separate Lunch row. |
| 1.2 | Account drag reordering. |
| 1.1 | Add/hide/restore accounts. |
| 1.0 | Account tracking, CSV summaries, sleep handling and crash recovery. |
| 0.5 | Monitor scaling and menu fixes. |
| 0.4 | Compact panel redesign and visible lunch total. |
| 0.3 | Tray icon, error log and window recovery. |
| 0.2 | Persisted Lunch pause. |
| 0.1 | Original always-on-top working-day counter. |

Repository maintenance after v2.22 removed obsolete scripts and refreshed documentation without changing the runtime version. Full earlier notes remain in [Git history](https://github.com/BasicallyGod/Basic-Advanced-Timer/commits/migration/CHANGELOG.md).

## Keeping this useful

Record each meaningful behaviour, accounting, compatibility or storage change once, newest first, with a version or dated commit link and a short description. Group minor visual fixes together. Keep implementation details in engineering notes and test results in commits or pull requests. Keep plans in the migration documentation.

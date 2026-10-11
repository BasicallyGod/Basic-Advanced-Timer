# Changelog

## Check your version

The app shows `Version 0.1.<build> (<commit>)` above Close timer in the right-click menu, followed by the channel name for experimental and alpha builds. The build number is the workflow run number and the commit identifies the exact source. Local builds show development build.

The milestones below are newest first. Commit links mark when each change landed; they are not release version numbers. Use the [commit history](https://github.com/BasicallyGod/Basic-Advanced-Timer/commits/main/) for individual changes and the [build workflow runs](https://github.com/BasicallyGod/Basic-Advanced-Timer/actions/workflows/avalonia-preview.yml) to match a packaged version to its commit.

## Avalonia migration

### 11 October 2026

| Milestone | Commit | What changed |
| --- | --- | --- |
| Tab names and account tooltips | pending | The dashboard tabs are now Timeline, Week overview, Hours table and Entries. The budget tooltip on account rows is gone, since the row already shows it. |
| App icon, taskbar button and themed dialogs | [f3f6883](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/f3f688310ac475ac65c686c2bbca20a710af9ad3) | The timer has an app icon (window, taskbar, executable) and a taskbar button, which Settings can switch off to run from the tray alone. On Windows 11 dialog title bars take the theme's colours and follow theme changes. |
| Adjustments on the timeline | [9f97959](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/9f979597edfef31efd1b02b9419523298fb7c810) | Adjust time today now draws a striped, muted block in the account's colour, as tall as the minutes it adds or removes. It can be dragged to move where it sits without changing any total, double-clicked to edit the note and right-clicked to delete. The chips above the timeline are gone. |
| System is the default theme again | [032fd87](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/032fd871b2f74b3ac36167d55e5b5181f3a2fd02) | New installs follow Windows light or dark. Light and Dark now lead the theme list, ahead of the colourful themes. |
| Play button hover | [cf36493](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/cf36493969e1b9c867a515fa613ccbc1dfde6d7a) | Account and pill play and pause buttons ease into an accent-coloured glow under the pointer, so it is clear they do something. |
| Stable, experimental and alpha channels | [2e6b81a](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/2e6b81a69eaa133807009134504325877de706da) | `main` is now the Microsoft Store release, with `experimental` for everyday testing and `alpha` for risky prototypes. Each channel builds its own portable ZIP, and the old `migration` branch is retired. The Store build is an unsigned MSIX built only from `main`, with a Windows startup task for Start with Windows, per-user data and no sample-data demo. See BRANCHES.md. |
| Updater channels | [49c1014](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/49c1014531784b5044ffcb245df88187d663252a) | Timer Updater.cmd now offers the experimental and alpha channels. Each keeps its own data, builds, backups and startup entry under `test-install/channels`, so an alpha database never meets the experimental build. The picker opens without GitHub CLI and has a window test. |

### 10 October 2026

| Milestone | Commit | What changed |
| --- | --- | --- |
| Start with Windows through the updater | [5d0fa98](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/5d0fa9894b4c3af1ddb2c817c48de6c0765f6afb) | The Start with Windows setting is now offered when the timer was started by the updater. It signs in through the updater's launch mode instead of a fixed build folder, so it keeps working after updates. Builds run directly from a ZIP are unchanged. |
| Branch picker updater (since replaced by channels) | [651eac2](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/651eac2aef5f767776befbf6390185b610373147) | A small window, Timer Updater.cmd, lists the repository's branches and updates and launches the newest successful build from the one you pick, with Launch current and Roll back beside it. The older cmd files take builds from main. The build workflow also runs on main. |
| Avalonia app becomes the application | [2ad1c1c](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/2ad1c1cbefd7181863ab2a19c8ffab3f384bed55) | The PowerShell application, its launcher, its six test scripts and their workflow are removed from the tree and remain in Git history. The README, engineering notes and user guide now describe the Avalonia app. The old CSV timesheets are deliberately not imported. |
| Theme editor prompt and menu fixes | [c80fb25](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/c80fb25d6bf839f24a7af5ca2cdf90a02bf127a3) | The theme editor now asks before closing, switching theme or duplicating with unsaved edits, offering Save, Discard or Keep editing. The default theme is listed above System in the Appearance menu. Starting a window drag, or pressing on the pill, now closes any open right-click menu instead of leaving it open. |
| New built-in themes and a new default | [f75071c](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/f75071c8f6cc262bc5565d0d1ec6887f6bd21a44) | Built-in themes now follow the mock-ups. A deep green and lime theme is the default for new installs, followed by Light, Dark, Sage, Graphite, Paper, Midnight, Sandstone and Aubergine, plus the two high contrast themes. Ocean becomes Midnight and Sunset becomes Sandstone on existing installs. Themes gain Accent, Accent text and Clock tokens, used for the tab bar, the Add time button and the idle clock. Block colours stay as chosen per account. Installs already set to System stay on System. |
| Tab bar and compact account rows | [25e865a](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/25e865a3505b1dfc13d4ec17dbfce6a605ead23d) | Dashboard tabs are plain words on a hairline with a green bar under the chosen one, closer to the mock-up, and the day arrows are lighter. Dashboard account rows are 54 pixels instead of 68, so about two more accounts show before scrolling. The pill's rows are unchanged. |
| Slimmer rows and a tidier action row | [fcbeef2](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/fcbeef21352853c989ffc1931452b9b4267f2fcb) | Pill and dashboard account rows are 52 pixels with a smaller ring and play button. Zoom and fit moved from a floating bar into the action row beside Tools, shown only on the Timeline tab. A disabled button keeps its outline, dimmed, instead of a solid grey fill. The "Recorded time · Click blocks to select" line is gone and only appears to report a selection. Icons now redraw when the theme changes. |
| Tools menu retired | [2581497](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/2581497c493bd5e18c34e627a5aae19d2e8d3378) | Group entries is gone from the menus (Merge still groups, and clicking a group still opens it to inspect or ungroup). Tidy day is offered when you right-click empty timeline space. Export week is an icon button in the action row on the week tabs. Zoom stays on the Timeline tab. |
| Overview, headings and clock polish | [aaf37c9](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/aaf37c9be971eec53fce370f2ce984634e02291f) | The week tabs are now Timeline, Week view, Overview and Entries. Export week and Copy hours table are icons on the Overview tab's table heading. Hours and minutes and Budgets are proper headings with a rule between them. The menu item is now Add time. The clock is bolder in the pill and the dashboard. Today allocated and Unallocated sit in small type under the dashboard clock, and the pill shows a small unallocated figure under its clock while the day runs. |
| Dashboard rebuilt around the day | [5725c49](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/5725c4948f7aa50f678f2b683081073fe0854a8d) | Following the mock-up, the left column now holds the date, clock, tracking state, Allocated and Unallocated figures, the accounts, then Take a break and Finish day (with a flag icon) at the foot. The right column has settings and minimise in its top corner, then the tabs and the review. The day button moved out of the header. |
| Overtime clock, always on top, break wording | [46cc29f](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/46cc29f3040f26cb373b0833ad97afd3533e1356) | The pill's clock stays the overtime red even while an account records (a break still shows amber). The dashboard stays in front of other windows, like the pill. The break button reads Start break and Finish break, to match Finish day. |
| Break and overtime colours, side-by-side buttons, dragging | [bfba76a](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/bfba76a9ed01afdc802da4a053e103b0b7df13cc) | The dashboard clock turns amber on a break, with the status line and dot amber too, and red in overtime. Take a break and Finish day sit side by side, with a quiet outline at rest that takes their own colour on hover. The pill glows in the theme's live colour while an account records. The dashboard can be dragged from the whole identity block, the accounts heading, and the tab and action rows, not just thin strips. |
| Theme editor, theme engine part 2 | [03fbdfb](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/03fbdfb41d3270050753eb0f30739ce1cda0c0b3) | Settings has a Themes window: pick a theme, duplicate a built-in one, then rename it and change any colour with sliders or a hex box, seeing each change in the app as it is made. Nothing is kept until Save, and closing puts the saved look back. Each theme can show account colours as chosen or softened to suit it, and the window warns when text would be hard to read. Delete returns to System. |
| Theme engine, part 1 | [a65e46b](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/a65e46bd1891d5b960f0c578e351952c6e9548a1) | Colours now come from theme definitions, not hand-written dictionaries. Built-in themes are System, Light, Dark, High contrast light and dark, Ocean, Sunset and Graphite. The pill's break and overtime tints are worked out from each theme's own accents. A user's own themes are stored in the settings file, and a theme that no longer exists falls back to System. The theme editor and block colour handling come next. |
| Quieter day buttons and layout | [7b539bc](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/7b539bc2783ac80de2b8cf0d4a9a253bc9e5111c) | Take a break and Finish day show no outline until the pointer is over them, with their icons always coloured. Settings and minimise moved to the top right of the left column, giving the tabs more height, and the Unallocated figure sits closer to Allocated. |
| Day button, clock and footer polish | [b24d324](https://github.com/BasicallyGod/Basic-Advanced-Timer/commit/b24d3241ab894060c59b85635693dda1fb89ab91) | Break and Finish day use the same type. Start day is green and Finish day red, with the flag in the same colour, and the flag waves and the cup steams under the pointer. Messages now appear only under the review, so they never change the left column's height. Allocated and Unallocated figures are smaller, and the clock is full-strength text when nothing is running, in the pill and the dashboard. |
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

This application has been retired and removed from the tree. These versions were assigned retrospectively to rounds of changes in October 2026. `AccountTimer2.ps1` covers v2.0 to v2.32; earlier scripts are preserved in Git history.

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

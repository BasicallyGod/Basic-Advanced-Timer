# Engineering notes

Implementation reference for contributors and coding assistants working on Account Timer. It documents the current architecture, earlier engineering pitfalls and the behaviour that should be preserved when making changes. The application has evolved through PowerShell and C# rendering iterations.

## What this is

An always-on-top Windows timer for tracking time against client accounts and preparing timesheets. It is written in Windows PowerShell 5.1 with WinForms, plus C# compiled at start-up with Add-Type. No installs or admin rights are allowed, so no modules, packages or external tools.

| File | Role |
| --- | --- |
| `AccountTimer2.ps1` | The only current application script. Includes WinForms UI and compiled C# rendering. |
| `StartAccountTimer2.vbs` | Portable relative-path launcher without a console window. |
| `tests/timeline-projection.ps1` | Synthetic regression tests for session projections, grouping and gap editing. |
| `tests/decimal-reconciliation.ps1` | Synthetic tests for decimal-hour accounting and CSV exports. |
| `.github/workflows/powershell-checks.yml` | Runs both regression suites under Windows PowerShell 5.1. |
| `README.md`, `CHANGELOG.md` | User guide and chronological development record. |

There are no legacy timer executables or launchers in the current tree. Older `AccountTimer.ps1` and `BillableTimer.ps1` versions are preserved in Git history. The current application does not import or launch them. Its single-instance check also recognises an older AccountTimer process in case one is still installed elsewhere and could use the same local data. Do not relax this safeguard without checking for concurrent CSV writes.

## v2.21: credited totals and positioned timeline intervals

- `Timesheets/sessions.csv` remains the **only source of account credited totals**, including original merged rows. Positioning credit never changes the source row or adds hours.
- `Timesheets/credit-positions.csv` keeps `Id,Date,Account,Start,Minutes,Note` for manually located portions of a merged legacy credit. The timeline draws dashed positioned intervals and the weekly entries view projects those portions with their own times, subtracting them from the displayed unplaced residual of the merged source. Notes for a newly positioned credit live in the position metadata so they cannot overwrite a source session note with the same start time. The source CSV remains unchanged.
- `Timesheets/entry-groups.csv` keeps `GroupId,Date,Account,Start,End,Minutes` for original session references. A manual weekly Group command preserves all original source intervals. The UI groups only; the timekeeping ledger does not. A manual group can be Ungrouped from the weekly context menu without changing the sources; edit times/notes on its individual constituent entries after ungrouping rather than silently rewriting the entire group.
- Metadata files have matching .undo.csv snapshots and are managed via `Invoke-LogChange`. Editing a source row can invalidate its old group membership. CSV row keys remain imperfect; a future database should introduce permanent IDs.
- The gap menu distinguishes **Position existing credited time** (no added hours) from **Add new credited time** (increases totals). It rechecks the gap before committing. Short rapid switches remain individual coloured intervals at all zoom levels, without overlay badges; hover and hit testing use their original positions.
- Run the synthetic regression fixture with `powershell -NoProfile -File tests\timeline-projection.ps1` on Windows PowerShell 5.1.

## Timeline and timesheet reconciliation

The CSV stores recorded intervals, manually added/edited intervals, adjustments and legacy merged credit in the same row format. `Convert-SessionRow` derives their `Origin` without modifying saved files. The timeline shows recorded and explicitly positioned manual intervals, but does not invent continuous spans for older, historically destructive `merged` records. Daily totals and exports still include those credits. `Get-WeekEntries` retains short sessions and separates positioned credit from the remaining unplaced portion, without crediting both. `Get-EntryBlocks` groups automatically only when recorded intervals really are adjacent. **Current manual grouping is non-destructive**, with membership stored in `entry-groups.csv` and reversible using Ungroup. Only old merged rows lack their original constituent positions. See `tests/timeline-projection.ps1` for the source-level regression checks.

## How AccountTimer2.ps1 is put together

- **Tracking and files** are plain PowerShell. Time is logged to Timesheets\sessions.csv (Date, Account, Start, End, Minutes), one row per stretch. The End column also marks special rows with "rounded up", "rounded down", "adjusted", "merged", "edited" or "added". Notes are in Timesheets\notes.csv, keyed by date, account and start time.
- **Settings** live in account-timer-*.txt files beside the script (accounts, budgets, themes, view, positions, effects, working hours, day overrides and more). Working hours are a default start and end plus optional per-weekday shifts ($script:dayHours, written as shift.mon=09:00,17:00 lines), and Get-DayPlan is the one place that works out a day's hours, so use it rather than reading $StartTime and $EndTime. Themes are in account-timer-themes.txt, editable by the user.
- **The window** is a layered window (WS_EX_LAYERED). Each frame is drawn into a 32-bit image and pushed with UpdateLayeredWindow, which allows see-through areas, rounded shapes and shadows. WinForms child controls cannot be shown on a layered window, so everything on the timer is drawn by hand. Text input uses separate small windows (note prompt, dialogs).
- **The full view** (timer rows plus the week drawer) is drawn by the C# class Win.FullRenderer. PowerShell fills its fields each frame (Sync-RendererState) and calls Render. If the C# fails to compile or throws, the script falls back to the PowerShell drawing functions (Draw-Full, Draw-Drawer) and logs why.
- **The drawer** has two views, switched by the Timeline and Week tabs under its title (Timeline is first and the default). The week table is described above. The timeline (Win.FullRenderer.DrawTimeline) draws one day as a vertical calendar column, with hours down the left, a card per stretch of time, Lunch hatched, working hours lightly shaded, a line for now and the account totals in a column on the right. PowerShell owns the scroll position ($script:tlScrollH, the hour at the top) and zoom ($script:tlPx, units per hour) and hands them to the renderer each frame; the wheel scrolls, Ctrl and the wheel zooms, and the timeline needs at least $TimelineMinH of drawer height. Legacy merged rows contain only credited sums and have no trustworthy clock span. Get-DayTimeline keeps these off the timeline and reports UnplacedSeconds in the totals legend. Gap filling offers two explicit actions, positioning existing legacy credit without changing totals or adding new credited time, with refreshed gap and credit validation before saving. Added and edited rows have an explicit clock position but are marked with a dashed card outline. The timeline counts even sub-minute recorded sessions when finding gaps, matching the ledger. Overlapping positioned stretches are given lanes by Set-TimelineLanes and drawn side by side, and an edge only stops at the neighbour in its own lane (Get-LaneNeighbour). Overlaps of a few seconds count as touching, because rounding in the log produces them. The drawer queues text after drawing its shapes and only queues timeline labels fully inside the viewport (`QueueIn`); timeline labels use their separate antialiased GDI+ path rather than the standard Windows text queue. PowerShell prepares the day with Get-DayTimeline and Get-DayLegend, and Build-TimelineModel hands it to C#, with times as hours since midnight. Timeline labels use a separate GDI+ antialiasing queue on the solid drawer layer, and CardTextColour chooses a contrasting text colour based on the theme. The renderer reports where it drew the cards column and viewport (TlX0, TlW, TlY, TlH) so PowerShell can turn a pointer position into a time (Get-TimelineTimeAt), and PowerShell does all the hit testing (Get-TimelineHit) and pushes the hover and drag state back each frame in Sync-RendererState. The timeline exists only in the C# drawing, so if that has to be switched off the drawer goes back to the week table. Whether the drawer is open and which view it shows are kept as two lines in account-timer-drawer.txt.
- **Editing on the timeline** goes through the same Invoke-LogChange as the weekly view, so it is saved with an Undo copy. Dragging an edge (Get-TimelineDrag does the snapping and limits as a pure function) shows a preview by rebuilding the drawer model with $script:tlPreview applied, and only writes the log on mouse up (Save-TimelineEdit). Two editable blocks within a minute of each other share one 'join' edge. What is running now has no log row yet, so it can't be dragged. Gaps (`Get-TimelineGaps`) are untracked stretches, capped at now for today. Filling a gap offers either a source-backed credit position in `credit-positions.csv` (no additional hours) or an `added` session row (additional credited time). The regular 15 second refresh is skipped while a drag is in progress.
- **Finishing the day** is kept as away intervals in account-timer-away.txt ($script:awayIntervals, one start and end per line, with the end blank while still finished). Update-View and Get-WeekData treat away time like lunch, so billable time, overtime and unallocated time all stop counting, and the red not tracking state is skipped (view.DayDone). Starting any account closes the open interval (Start-Slot), and one left open from an earlier day is closed at midnight when loaded. The Finish day button sits under the Lunch row, drawn by C# (DayBtnW and DayBtnH) and hit-tested in PowerShell (Get-DayBtnRect). It shows from $FinishLeadMinutes before the end of the day, and for as long as the day is finished (as Carry on), and while it shows the timer is $FinishAreaH taller ($script:finishH, which Resize-Form adds to BaseH). The right-click menu offers the same toggle once the day has started. Unallocated figures use Format-Clock, which is always h:mm.
- **The pill** (compact view) is still drawn in PowerShell, as it is fast enough.
- **The weekly view** is a normal WinForms window with DataGridView tables, owner-drawn headers and buttons.
- **Size** is $Scale, set from the menu (Appearance, Size) and kept in account-timer-size.txt, and Zoom is Scale times the screen's DPI over 96. Set-UiScale changes it while running, keeping the timer where it is. Don't name a parameter $scale, as it hides $Scale from everything it calls.
- **Drawing speed** can be checked from the menu (Appearance, Drawing speed). Under 30 ms per redraw feels instant, over 50 ms feels sluggish.

## Pitfalls already hit (please don't reintroduce them)

- **Windows PowerShell compiles C# 5.** No string interpolation ($""), ?., expression-bodied members (=>), nameof, out var, tuples or pattern matching. Newer compilers accept these, so test compiles elsewhere can pass while Windows fails.
- **PowerShell variable names ignore capitals.** $C is the shortcut for System.Drawing.Color, so a local $c silently replaces it. The same applies to $GB (en-GB culture), $Inv, $px, $RF, $PF and $tip. Check new short variable names against these.
- **PowerShell scope is dynamic.** A function called from another can see the caller's local variables, which is how the $c clash broke things inside nested calls.
- **[math]::Min(1, 0.4) returns 0.** With a whole number first, PowerShell picks the whole-number overload. Always write 1.0 or 0.0 when the other value can be a fraction.
- **Avoid .GetNewClosure() in event handlers that call script functions or use $script: variables.** It cuts the handler off from the script's scope.
- **Single-quoted strings don't expand `t.** Use double quotes when a tab is intended.
- **Windows' blur fills the whole window rectangle** and ignores the window's region. That is why the shadow margin is dropped while the blur is showing.
- **ClearType needs a solid background.** Over see-through areas, text is drawn as a shape mask and combined in C#. On saturated theme colours, ClearType's coloured edges look bad, so the week drawer uses Windows text drawing with plain-shade smoothing (ANTIALIASED_QUALITY).
- **The process is per-monitor DPI aware.** Sizes are in base units multiplied by $Zoom. Menus and dialogs are scaled by hand.
- **Keep-Visible** reasserts always-on-top every two seconds, but skips while the mouse is over the timer or a tooltip is showing, so tooltips aren't buried.

## Checking changes

Visual behaviour must be validated on Windows. The following checks catch common mistakes, and the two synthetic suites already run in GitHub Actions on Windows:

1. Parse `AccountTimer2.ps1` and the test scripts with `System.Management.Automation.Language.Parser.ParseFile`. Run `tests/timeline-projection.ps1` and `tests/decimal-reconciliation.ps1` in Windows PowerShell 5.1.
2. Confirm the embedded C# blocks compile under Windows PowerShell 5.1 using `Add-Type`; the synthetic tests do not themselves exercise the interactive layered window.
3. Unit-test non-visual functions (calculations, file reading and writing, formatting) by extracting them from the script with the PowerShell parser and running them against sample data.
4. Search new code for variable names that clash with the script-level ones above.

5. Off Windows, PowerShell 7 can be had with `dotnet tool install --tool-path <dir> PowerShell --version 7.4.6` (after installing dotnet-sdk-8.0), and `mcs -langversion:5` from the mono-mcs package enforces the C# 5 limit on the extracted C# blocks. With libgdiplus, a small C# harness can render the drawer to a PNG, with the Windows text drawing swapped for DrawString, which is enough to check layout and hover areas.

The user tests the visuals on Windows and reports back with screenshots.

## Conventions

- Maintain `AccountTimer2.ps1` as the sole current application. Avoid adding new dependencies on removed legacy scripts. Historical versions are recoverable from Git history.
- Record functional changes in `CHANGELOG.md` newest first, with version, script and a short description. Record repository-only maintenance separately rather than inventing a new runtime version.
- Code comments in plain English explaining why, not just what.
- Interface text and documentation follow the user's writing preferences: British spelling, no em dashes, no colons mid-sentence, no arrows or unusual characters, plain and human wording. Refer to "client accounts", not "retained accounts".
- Never commit private `Timesheets` contents, local `account-timer-*.txt` settings or logs. `.gitignore` excludes these by default. Test with synthetic fixtures only.

## Potential future improvement

- Consider Windows-drawn text for the timer's solid areas (status line, unallocated figure, overtime counter), matching the drawer. This remains a proposal, not an implemented feature.


### Fractional-second timeline gaps
CSV session durations use hundredths of a minute (0.6-second increments), which can leave an effective end time such as 10:03:14.2 even though the source End field says 10:03:14. Never round the selected gap before containment validation. `Resolve-TimelineGapFill` first checks the unrounded selection against the current gap, then moves the start forward and the end backward to whole seconds for the portable CSV representation. This avoids false "gap has changed" errors while still rejecting occupied gaps. Regression fixture: `tests/timeline-projection.ps1`.


## Decimal timesheet reconciliation (v2.22)

`Get-WeekReconciliation` produces authoritative **persisted** decimal-hour exports without modifying `sessions.csv`. It checks that the projected detailed entries equal the raw credited seconds for every account/day. Hundredths of an hour (36 seconds each) are assigned by deterministic largest-remainder apportionment: first across account/day totals to match the precisely rounded weekly grand total, and then to each detailed entry within its bucket. This works with negative adjustments, historical merged credit, and positions placed from it. All final figures are integer hundredths, so daily sums, weekly account sums, and detailed Hours rows are exactly additive.

`Write-WeekSummary` supplies its reconciliation result to `Write-WeekEntries`, preventing two independent rounding passes. Stopped/completed weekly views in decimal mode use the same quantized account/day figures. While a timer runs, it is provisional until saved, so the live UI and persisted exports differ temporarily. In h:mm mode, whole-minute display may not add to hundredths of hours, as 0.01h equals 36 seconds; use exported Hours for exact timesheet arithmetic.

The reconciliation algorithm can move 0.01h between individual account/day displayed cells when necessary to preserve the precisely rounded weekly grand total. It does not change source durations. Tests: `tests/decimal-reconciliation.ps1`, exercised on Windows PowerShell 5.1.

## Repository maintenance after v2.22

The working tree deliberately contains only the current app, its launcher, documentation, configuration for Git, and two synthetic tests plus their Windows CI workflow. Previous complete timers and their VBS launchers were removed from the current tree, not rewritten out of history. Keep `StartAccountTimer2.vbs` alongside `AccountTimer2.ps1`; it resolves the application path relative to itself. Do not remove `tests/` or `.github/workflows/`, even though they are not called by the interactive app; they protect the accounting and timeline behaviour. The generated `Timesheets/` folder, undo snapshots, and text settings are real runtime dependencies and must remain intact when copying an existing installation, even though they are not versioned.

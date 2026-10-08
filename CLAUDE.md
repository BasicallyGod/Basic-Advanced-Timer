# Notes for Claude

Context for working on this repository. The timer was built in a long chat with Claude in October 2026. This file carries over what that chat learnt, so read it before making changes.

## What this is

An always-on-top Windows timer for tracking time against client accounts and preparing timesheets. It is written in Windows PowerShell 5.1 with WinForms, plus C# compiled at start-up with Add-Type. No installs or admin rights are allowed, so no modules, packages or external tools.

| File | Role |
| --- | --- |
| AccountTimer2.ps1 | The current timer. Almost all work happens here. |
| AccountTimer.ps1 | Earlier version, kept as a fallback. Shares the same data files. |
| BillableTimer.ps1 | The original single timer, kept as a fallback. |
| Start*.vbs | Launch each script without a console window. |
| CHANGELOG.md | Full history. Update it with every change. |

Only one timer runs at a time. AccountTimer2.ps1 refuses to start if another copy is running.

## How AccountTimer2.ps1 is put together

- **Tracking and files** are plain PowerShell. Time is logged to Timesheets\sessions.csv (Date, Account, Start, End, Minutes), one row per stretch. The End column also marks special rows with "rounded up", "rounded down", "adjusted", "merged", "edited" or "added". Notes are in Timesheets\notes.csv, keyed by date, account and start time.
- **Settings** live in account-timer-*.txt files beside the script (accounts, budgets, themes, view, positions, effects, working hours, day overrides and more). Working hours are a default start and end plus optional per-weekday shifts ($script:dayHours, written as shift.mon=09:00,17:00 lines), and Get-DayPlan is the one place that works out a day's hours, so use it rather than reading $StartTime and $EndTime. Themes are in account-timer-themes.txt, editable by the user.
- **The window** is a layered window (WS_EX_LAYERED). Each frame is drawn into a 32-bit image and pushed with UpdateLayeredWindow, which allows see-through areas, rounded shapes and shadows. WinForms child controls cannot be shown on a layered window, so everything on the timer is drawn by hand. Text input uses separate small windows (note prompt, dialogs).
- **The full view** (timer rows plus the week drawer) is drawn by the C# class Win.FullRenderer. PowerShell fills its fields each frame (Sync-RendererState) and calls Render. If the C# fails to compile or throws, the script falls back to the PowerShell drawing functions (Draw-Full, Draw-Drawer) and logs why.
- **The drawer** has two views, switched by the Timeline and Week tabs under its title (Timeline is first and the default). The week table is described above. The timeline (Win.FullRenderer.DrawTimeline) draws one day as a strip, with a block per stretch of time, Lunch hatched, working hours lightly shaded and a marker for now. PowerShell prepares the day with Get-DayTimeline and Get-DayLegend, and Build-TimelineModel hands it to C#, with times as hours since midnight. The renderer reports where it drew the strip (TlX0, TlW, TlY, TlH) so PowerShell can turn a pointer position into a time, and PowerShell does all the hit testing (Get-TimelineHit) and pushes the hover and drag state back each frame in Sync-RendererState. The timeline exists only in the C# drawing, so if that has to be switched off the drawer goes back to the week table. Whether the drawer is open and which view it shows are kept as two lines in account-timer-drawer.txt.
- **Editing on the timeline** goes through the same Invoke-LogChange as the weekly view, so it is saved with an Undo copy. Dragging an edge (Get-TimelineDrag does the snapping and limits as a pure function) shows a preview by rebuilding the drawer model with $script:tlPreview applied, and only writes the log on mouse up (Save-TimelineEdit). Two editable blocks within a minute of each other share one 'join' edge. What is running now has no log row yet, so it can't be dragged. Gaps (Get-TimelineGaps) are the untracked stretches, capped at now for today, and filling one adds an 'added' row. The regular 15 second refresh is skipped while a drag is in progress.
- **Finishing the day** is kept as away intervals in account-timer-away.txt ($script:awayIntervals, one start and end per line, with the end blank while still finished). Update-View and Get-WeekData treat away time like lunch, so billable time, overtime and unallocated time all stop counting, and the red not tracking state is skipped (view.DayDone). Starting any account closes the open interval (Start-Slot), and one left open from an earlier day is closed at midnight when loaded. The Finish day button in the overtime section is drawn by C# (DayBtn fields) and hit-tested in PowerShell ($DayBtn); the right-click menu offers the same toggle once the day has started.
- **The pill** (compact view) is still drawn in PowerShell, as it is fast enough.
- **The weekly view** is a normal WinForms window with DataGridView tables, owner-drawn headers and buttons.
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

Nothing can be run visually off Windows, but these catch most mistakes:

1. Syntax check every script with System.Management.Automation.Language.Parser.ParseFile.
2. Compile each embedded C# block with Add-Type.
3. Unit-test non-visual functions (calculations, file reading and writing, formatting) by extracting them from the script with the PowerShell parser and running them against sample data.
4. Search new code for variable names that clash with the script-level ones above.

5. Off Windows, PowerShell 7 can be had with `dotnet tool install --tool-path <dir> PowerShell --version 7.4.6` (after installing dotnet-sdk-8.0), and `mcs -langversion:5` from the mono-mcs package enforces the C# 5 limit on the extracted C# blocks. With libgdiplus, a small C# harness can render the drawer to a PNG, with the Windows text drawing swapped for DrawString, which is enough to check layout and hover areas.

The user tests the visuals on Windows and reports back with screenshots.

## Conventions

- Keep the old scripts working as fallbacks. AccountTimer2.ps1 is where new features go.
- Every change gets a CHANGELOG.md row (version, script, type of New, Improved or Fixed, and a one-line description), newest first.
- Code comments in plain English explaining why, not just what.
- Interface text and documentation follow the user's writing preferences: British spelling, no em dashes, no colons mid-sentence, no arrows or unusual characters, plain and human wording. Refer to "client accounts", not "retained accounts".
- Personal data (Timesheets folder, settings .txt files, logs) must never be committed. .gitignore covers them.

## Planned next

- Windows-drawn text for the timer's solid areas (status line, unallocated figure, overtime counter), matching the week drawer.

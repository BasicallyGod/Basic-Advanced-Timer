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
- **Settings** live in account-timer-*.txt files beside the script (accounts, budgets, themes, view, positions, effects, working hours, day overrides and more). Themes are in account-timer-themes.txt, editable by the user.
- **The window** is a layered window (WS_EX_LAYERED). Each frame is drawn into a 32-bit image and pushed with UpdateLayeredWindow, which allows see-through areas, rounded shapes and shadows. WinForms child controls cannot be shown on a layered window, so everything on the timer is drawn by hand. Text input uses separate small windows (note prompt, dialogs).
- **The full view** (timer rows plus the week drawer) is drawn by the C# class Win.FullRenderer. PowerShell fills its fields each frame (Sync-RendererState) and calls Render. If the C# fails to compile or throws, the script falls back to the PowerShell drawing functions (Draw-Full, Draw-Drawer) and logs why.
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

The user tests the visuals on Windows and reports back with screenshots.

## Conventions

- Keep the old scripts working as fallbacks. AccountTimer2.ps1 is where new features go.
- Every change gets a CHANGELOG.md row (version, script, type of New, Improved or Fixed, and a one-line description), newest first.
- Code comments in plain English explaining why, not just what.
- Interface text and documentation follow the user's writing preferences: British spelling, no em dashes, no colons mid-sentence, no arrows or unusual characters, plain and human wording. Refer to "client accounts", not "retained accounts".
- Personal data (Timesheets folder, settings .txt files, logs) must never be committed. .gitignore covers them.

## Planned next

- A timeline drawer showing each stretch of today along a strip, with Lunch as a gap and a marker for now. Interactive editing (dragging edges, filling gaps) could follow. Build its drawing in C# from the start.
- Windows-drawn text for the timer's solid areas (status line, unallocated figure, overtime counter), matching the week drawer.

# Basic’s Advanced Timer: engineering notes

Notes for contributors and coding assistants. The application is a C# Avalonia desktop app on .NET 8, always on top, for tracking time against client accounts and preparing timesheets. It must run without installs or administrator rights. The earlier PowerShell/WinForms app (v2.32) was removed from the tree and lives in Git history.

## Layout

| Path | Role |
| --- | --- |
| `migration/src/Timer.Core` | Platform-independent tracking engine (`TrackingEngine`), schedules, day maths and the theme model (`Themes.cs`). |
| `migration/src/Timer.Storage` | `TimerDatabase` (SQLite), `TimerStore`, undo, credit review, ledger editing, week export. |
| `migration/src/Timer.Desktop` | Avalonia views, `TimerModel` (UI adapter), `ThemeEngine`, styles in `App.axaml`. |
| `migration/tests/Timer.Desktop.Tests` | xUnit tests, including headless Avalonia tests of production windows. |
| `migration/updater` | Standalone Windows test-build updater with rollback. |
| `migration/packaging` | MSIX packaging for the Microsoft Store (manifest template, `build-msix.ps1`, placeholder icons). Packaged builds keep data under `%LOCALAPPDATA%` via `PackageEnvironment`; portable builds keep it beside the exe. Store builds compile for `net8.0-windows10.0.19041.0` (`StorePackage=true`, `STORE_PACKAGE` defined) so the Start with Windows menu item can use the WinRT startup task; everyday builds stay `net8.0`. |
| `.github/workflows/microsoft-store-msix.yml` | Store release workflow for `main`. Pull requests compile the Store target and run tests only; a push to `main` builds the unsigned `BasicsAdvancedTimer-MSIX-Store` package. |
| `.github/workflows/avalonia-preview.yml` | Development builds for `experimental` and `alpha`: updater tests, then app tests on Windows, Linux and macOS, then a channel-named portable Windows ZIP. |
| `BRANCHES.md` | The three release channels and how changes are promoted. |
| `README.md`, `migration/README.md`, `CHANGELOG.md` | Overview, user guide and architecture, history. |

The `migration` folder name is historical and kept so updater and workflow paths still work. Renaming it is a separate change.

## Branches and releases

`main` is the Store release, `experimental` is everyday testing and `alpha` is for risky prototypes. Work on `experimental` or a `feature/...` branch from it, promote alpha to experimental and experimental to main by pull request, and never push straight to `main`. Builds show their channel beside the version, and only non-Store builds offer the sample-data demo. The updater serves experimental and alpha only, each with its own data, versions and backups. See `BRANCHES.md`.

## Behaviour to preserve

- **Credited totals.** Entries hold credited durations. Totals come from credited seconds, never from clock spans. Positioning credit never adds hours; "Add time" does.
- **Day clock.** The main clock is working plus overtime time, excluding Lunch, breaks and away. Stopping or switching an account does not reset it. Scheduled starts are automatic. A day off counts only tracked time. `SetShifts` drops manual starts the new hours make unnecessary. `ResumeAfterBreak` is stored with the running row. `BreakAccount.Name = "Break"` is reserved.
- **Exact decimal hours.** Exports apportion hundredths of an hour by largest remainder, first across account/day totals and then within each bucket, so every sum is exact. Running totals are provisional until saved.
- **Gaps.** Selected gaps are validated against the unrounded current gap before being moved to whole seconds, because durations use hundredths of a minute. Tidy day rechecks the whole selection before adding anything, as one undo step.
- **Undo.** Edits are stored as splices plus hashes in `edits` and `edit_changes`, so history stays small and survives restarts.

## Storage

`TimerDatabase` holds accounts, entries, groups, away, day_starts, running, edits, edit_changes and meta. It takes an exclusive lock, uses WAL and `user_version`, and makes daily backups. Dispose every `SqliteCommand` or Windows keeps the file locked, which fails tests on CI. `TimerStore` rewrites `entries` when a snapshot changes. Notes, groups and `accountsSeeded` are blobs in `meta`. The settings JSON preserves unknown sections and is swapped in atomically with a `.bak`. Sessions checkpoint every 15 seconds and recovery stops at the last checkpoint. `TimerStore` refuses a folder holding the old `AccountTimer2.ps1` or launcher, and the old CSV files are deliberately never imported. Never auto-open another app's data folder. Never commit `migration-data`, `timer.db`, settings JSON or logs (`.gitignore` excludes them).

## Themes

Each theme is a `ThemeDefinition` (name, Light or Dark base, colour tokens, block style) and an Avalonia `ThemeVariant`. `ThemeTokens.Names` lists the tokens (Surface, Panel, Ink, Muted, Line, Hover, Selected, Live, Idle, Overtime, BudgetWarning, Break, Finish, Accent, AccentInk, Clock, ShadowColor). `ThemeEngine.Register` writes brushes into `Application.Resources.ThemeDictionaries` in place so open windows repaint. Built-ins register in `App.Initialize`, user themes in the `TimerModel` constructor and on save. The pill's break and overtime tints are derived in HSL from the accent and the theme's own lightness. The default is System (`ThemeLibrary.Default`), which follows Windows light or dark. Light and Dark lead the built-in list, then the colourful themes, Sage, Graphite, Paper, Midnight, Sandstone, Aubergine and two high contrast themes. `ThemeLibrary.Renamed` maps old names (Ocean, Sunset). Only a missing theme setting defaults to `ThemeLibrary.Default`; a saved choice is kept. Tests require Ink on Surface and Panel at 7:1, Muted on Surface at 3.5:1 and AccentInk on Accent at 4.5:1 for every built-in and derived variant. Built-ins are locked in the editor; the editor asks before discarding unsaved edits. Timeline blocks are drawn by the theme's block rules (`ThemeDerivation.Block`, via `TimerModel.BlockLook`). The style is solid (the account colour), softened, or tinted (`BlockTint` is how much account colour the fill takes and `BlockEdge` how far the left edge moves towards the text colour), and every block has a strip down its left side. Time Deck is the first built-in with tinted blocks. Clocks and big numbers use Bricolage Grotesque (SIL Open Font License, `Assets/Fonts`), bundled as three instances cut from the variable font, each its own family (`ClockFont`, `FiguresFont`, `ClockLightFont`, built in `App.Initialize` and used through the `clock`, `figures` and `clockLight` classes).

## UI conventions and pitfalls

- Every window calls `WindowChrome.Attach(this)`, which gives it the app icon and, on Windows 11, a title bar in the theme's colours that follows theme changes and previews (`ThemeEngine.Applied`). Older Windows keeps its normal title bar.
- There is no Adjust time feature. Databases made while it existed are cleaned when they open: `TimerStore` leaves `adjustment` rows out, keeps a `before-removing-adjustments` backup, drops their notes and clears the undo history (`AdjustmentsRemoved` drives a notice). The schema's `adjustment` kind is left in place, since SQLite cannot drop a CHECK value cheaply.
- The taskbar button is on by default and `TaskbarButton` can be switched off only once the tray exists (`TrayAvailable`).
- Use the helpers in `Controls.cs` (`Ui.Grid`, `Ui.Put`, `Ui.Button`, `Ui.IconButton`, `Ui.Text`). Style through classes in `App.axaml` and `DynamicResource` theme tokens rather than hard-coded colours.
- `Ui.Visible` lifts very dark account colours so dots and blocks stay visible on dark themes; `Ui.InkOn` picks text for a block.
- Drag handlers mark presses as handled, so they must call `Ui.CloseMenus` first or an open right-click menu stays open.
- Pill and dashboard are separate windows sharing one model. Do not resize a visible window to change views. The dashboard is `Topmost`.
- Headless tests: `DispatcherTimer` and animations do not tick, so test state not frames. `TextBox.TextChanged` does not fire, so listen to `PropertyChanged` on `TextBox.TextProperty`.
- Fault-injection helper `StorageFaults` (in `EditingTests.cs`) uses triggers to make saves fail.
- Keep the C# for `Timer.Core` and `Timer.Storage` free of UI types.

## Checking changes

Run `dotnet test migration/tests/Timer.Desktop.Tests -c Release`. CI runs it on Windows, Linux and macOS and publishes a self-contained Windows build; check the run before saying tests pass. The user tests visuals on Windows and reports back with screenshots.

## Conventions

- Record changes in `CHANGELOG.md`, newest first, each row linked to its commit.
- Comments in plain English explaining why.
- Interface text and documentation use British spelling, no em dashes, no mid-sentence colons, no arrows or unusual characters, plain human wording. Say "client accounts".
- Test with synthetic data only.

## Not done yet

Overnight shifts, theme import and export, per-theme block colour palettes, and the pill's break and overtime tint during theme preview.

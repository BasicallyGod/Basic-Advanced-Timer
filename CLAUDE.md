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
| `.github/workflows/microsoft-store-msix.yml` | Builds a self-signed development MSIX on pushes. The unsigned Store candidate is built only when the workflow is run by hand with the Store candidate box ticked. |
| `.github/workflows/avalonia-preview.yml` | Tests on Windows, Linux and macOS, then publishes the Windows build. |
| `README.md`, `migration/README.md`, `CHANGELOG.md` | Overview, user guide and architecture, history. |

The `migration` folder name is historical and kept so updater and workflow paths still work. Renaming it is a separate change.

## Behaviour to preserve

- **Credited totals.** Entries hold credited durations. Totals come from credited seconds, never from clock spans. Positioning credit never adds hours; "Add time" does. Merged and adjustment rows are not recorded timeline intervals.
- **Day clock.** The main clock is working plus overtime time, excluding Lunch, breaks and away. Stopping or switching an account does not reset it. Scheduled starts are automatic. A day off counts only tracked time. `SetShifts` drops manual starts the new hours make unnecessary. `ResumeAfterBreak` is stored with the running row. `BreakAccount.Name = "Break"` is reserved.
- **Exact decimal hours.** Exports apportion hundredths of an hour by largest remainder, first across account/day totals and then within each bucket, so every sum is exact. Running totals are provisional until saved.
- **Gaps.** Selected gaps are validated against the unrounded current gap before being moved to whole seconds, because durations use hundredths of a minute. Tidy day rechecks the whole selection before adding anything, as one undo step.
- **Undo.** Edits are stored as splices plus hashes in `edits` and `edit_changes`, so history stays small and survives restarts.

## Storage

`TimerDatabase` holds accounts, entries, groups, away, day_starts, running, edits, edit_changes and meta. It takes an exclusive lock, uses WAL and `user_version`, and makes daily backups. Dispose every `SqliteCommand` or Windows keeps the file locked, which fails tests on CI. `TimerStore` rewrites `entries` when a snapshot changes. Notes, groups and `accountsSeeded` are blobs in `meta`. The settings JSON preserves unknown sections and is swapped in atomically with a `.bak`. Sessions checkpoint every 15 seconds and recovery stops at the last checkpoint. `TimerStore` refuses a folder holding the old `AccountTimer2.ps1` or launcher, and the old CSV files are deliberately never imported. Never auto-open another app's data folder. Never commit `migration-data`, `timer.db`, settings JSON or logs (`.gitignore` excludes them).

## Themes

Each theme is a `ThemeDefinition` (name, Light or Dark base, colour tokens, block style) and an Avalonia `ThemeVariant`. `ThemeTokens.Names` lists the tokens (Surface, Panel, Ink, Muted, Line, Hover, Selected, Live, Idle, Overtime, BudgetWarning, Break, Finish, Accent, AccentInk, Clock, ShadowColor). `ThemeEngine.Register` writes brushes into `Application.Resources.ThemeDictionaries` in place so open windows repaint. Built-ins register in `App.Initialize`, user themes in the `TimerModel` constructor and on save. The pill's break and overtime tints are derived in HSL from the accent and the theme's own lightness. The first built-in is the default (`ThemeLibrary.Default`), then Light, Dark, Sage, Graphite, Paper, Midnight, Sandstone, Aubergine and two high contrast themes. `ThemeLibrary.Renamed` maps old names (Ocean, Sunset). A saved "System" choice stays System; only a missing choice defaults to `ThemeLibrary.Default`. Tests require Ink on Surface and Panel at 7:1, Muted on Surface at 3.5:1 and AccentInk on Accent at 4.5:1 for every built-in and derived variant. Built-ins are locked in the editor; the editor asks before discarding unsaved edits. Block colours stay as the account's own colour unless a theme's block style softens them.

## UI conventions and pitfalls

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

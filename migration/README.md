# Avalonia migration preview

The `migration` branch starts from experimental v2.32. The PowerShell application remains runnable through its existing launcher. This directory contains the first, separate Avalonia desktop shell.

## Try the Windows preview

Open the latest successful **Avalonia migration preview** run in GitHub Actions and download **BasicsAdvancedTimer-Windows-preview**. Extract the complete ZIP into its own folder, then double-click `BasicsAdvancedTimer.Preview.exe`. Keep all neighbouring DLLs and native libraries with it. The package includes the .NET runtime, requests ordinary user privileges and does not require an installer or a separately installed SDK/runtime. Workplace application policies may still block an unfamiliar executable; launching on the intended Windows machine is a migration checkpoint.

The preview starts as an always-on-top pill. Expand it to select a sample account, open Dashboard, start/finish the demonstration day or choose System, Light or Dark appearance. Drag the account text to move the pill, or the dashboard's date header to move the dashboard. Close from the expanded pill or dashboard.

The dashboard has a 960 by 640 logical-pixel layout, fitted to the current screen before it opens. It is a separate window from the pill, so switching views does not resize a visible window. Per-account controls include budget rings. Lunch has the same row layout. There is one recording indicator and a separate overtime indicator. Not tracking is grey; overtime is red in both themes. Use **Preview overtime** to inspect the combination, and the hours-format button to check totals and budgets.

**This is a UI preview, not a replacement time tracker.** All accounts, day blocks and weekly totals are illustrative. Account buttons run an in-memory demonstration timer; nothing is saved. It never opens the existing CSV files, preferences or recovery state. Relaunching resets the demonstration. Timeline editing, budgets/settings editing, export, Undo, recovery, tray integration and authoritative working-hours logic are not connected yet. Timeline blocks remain illustrative when the demonstration timer runs.

## Development

The preview uses .NET 8 and pinned Avalonia 11.3.16 packages. The SDK and NuGet packages are development/build dependencies only; user-facing Windows builds are self-contained.

```sh
dotnet run --project migration/src/Timer.Desktop/Timer.Desktop.csproj
dotnet test migration/tests/Timer.Desktop.Tests/Timer.Desktop.Tests.csproj --configuration Release
dotnet publish migration/src/Timer.Desktop/Timer.Desktop.csproj --configuration Release --runtime win-x64 --self-contained true -o artifacts/windows-preview
```

Headless checks load the production app styles, bundled Inter font and actual window components. They check drawn row positions with pointer input before and after scrolling, independent recording/overtime states, shared formats and view switching. They render both themes and tabs for inspection. CI runs on Windows, Linux and macOS, and also starts the published Windows executable with a native pill/dashboard switch check. This verifies the shell, not complete native platform compatibility. Native DPI transitions, window positioning, transparency and the intended work-machine launch need desktop checks.

## Migration stages

1. **Desktop shell.** Prove the pill/dashboard arrangement, themes, native layout/input and portable Windows launch. Current stage.
2. **Tracking core.** Port working hours, early starts, away intervals, sessions, overtime and budgets to a platform-independent C# project. Translate the established synthetic regression cases before connecting the views.
3. **Compatible storage.** Implement and fixture-test existing CSV, metadata, JSON sections, atomic saving, single-instance protection and recovery. Exercise copied fixture data; preserve credited versus recorded time and decimal reconciliation. Do not silently replace production files.
4. **Review and editing.** Connect Timeline/Week, lane-aware drags, grouping, credit positioning, Tidy day, notes, export and batch Undo to the tested core.
5. **Platform integration and adoption.** Implement tray/lifecycle behaviour, screen positioning, packaging and optional auto-start separately per OS. Verify the Windows replacement first, then test the Mac/Linux desktop builds before retiring the old launcher.

The migration replaces the PowerShell/WinForms presentation architecture. It preserves the approved pill/dashboard design and the established timekeeping rules. No database redesign is part of this first stage.

## Theme architecture

`App.axaml` owns shared component styles and two theme dictionaries. Surface, panel, ink, muted text, separators, hover, selection, recording, idle, overtime and budget-warning colours have named meanings. System delegates to the operating system's preference; Light/Dark override it. Account colours are shared across variants, with dark text on the light account cards.

Themes do not determine recording or overtime state. Both indicators can be shown together. The old editable theme catalogue remains used by the existing PowerShell app; the new shell neither reads nor rewrites it. Mapping existing theme preferences is a later, explicit settings migration.

# Redesign: build stages

The plan agreed with the owner for turning the mock (`docs/redesign/mock/`) into the app. `DESIGN-BRIEF.md` holds the decisions. Each stage is one `feature/redesign-...` branch and one pull request into `experimental`, with its CHANGELOG row and CLAUDE.md changes. The app stays usable after every stage. Nothing is merged without the owner saying so, and the owner tests on Windows after each drop.

Defaults agreed: the Light and Dark changes are done inside the app (no shared-package release, `SharedTimerVersion` stays 0.5.0); the timeline keeps its scrolling and zoom (the mock draws a fixed 08:00 to 18:00); Ctrl+C and Ctrl+V on blocks carry over unchanged.

| Stage | What | Size | State |
|---|---|---|---|
| 0 | Groundwork: this folder, a manual-run ZIP on any branch, CLAUDE.md pointer | S | done (PR 120) |
| 1 | The vertical pill (72 by 262), status capsules, mug, click the time, peek and accounts panel, one-item right-click menu. The Full, Compact and Minimal layouts and the pill-style setting go | M | in review |
| 2 | Dashboard shell: 1100 by 720 window, full-height rail, header with date picker, bell and gear, account pop-out, Finish day and Start day, neutral Light palette, slim scrollbars. The old timeline sits inside it for now | M | in review, revised after the first Windows test: accounts as a drawer on the pill and the rail, amber break circle, pill and rail drawn at one size (see the Round 2 board) (no bell yet, that is Stage 6; no recorded-day dots or Today footer in the date picker yet; the Breaks figure waits for Stage 4) |
| 3 | Settings page with Export, Getting started restyled. Hours table, Entries, the theme editor and the move-later setting go | M | |
| 4 | The new horizontal timeline, view only: gradient blocks, stacking lanes, meeting row, now marker, day totals strip | L | |
| 5 | Timeline interaction: select and shift-click, drag, the Selected block card, Merge and Unmerge, Undo, Add time, floating messages, automatic Continue earlier block. Tidy day and the old Group and Edit dialogs go | L | |
| 6 | Notes: a `NotesModel` on the shared notes model, the Notes card, meetings and repeats, reminder pop-up, bell flyout, due capsule, then the Notes page | L | |
| 7 | Week overview: totals strip, schedule line, Chart or Table, pace, day markers | M | |
| 8 | Polish: the pill app icon, tray glyph and Store tiles, animation audit, contrast tests for the new Light, accessibility, dead code, docs, repeat-tests run | M | |

Drops for the owner to test: Drop 1 is stages 1 and 2, Drop 2 is stages 3 to 5, Drop 3 is stages 6 to 8.

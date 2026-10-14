# Navigation: audit and proposal

Status: agreed in outline. Overview folds into Timesheet, and the desk becomes the default home in alpha. Settings > Desk now provides an optional switch back to the classic dashboard for comparison and testing; this choice is remembered. Steps 1 and 2 of the order of work are done, and most of step 3: the desk is home and the legacy left column no longer shows. The older dashboard layout code is still in the tree (tests reach it by turning the desk off) and is the remaining thing to delete. It follows the consolidated feedback on the desk and the floating timer, and keeps the three layers apart: the pill for the moment, the desk as the main workspace, dedicated views for detailed work.

## What there is today

| Control | Opens | Where it lives | Desk tile that overlaps it |
| --- | --- | --- | --- |
| Pill button (`BackToPill`) | The floating timer | Rail or top bar, first | None (this is the way out) |
| Desk (`DeskTab`, preview) | The desk | Rail or bar, shown only with the preview switch on | n/a |
| Timeline (`TimelineTab`) | Day timeline with all editing, plus the left column (working time card, accounts, break and Finish day) | Rail or bar | Timeline, Day strip, Time tracker (partly) |
| Week overview (`WeekOverviewTab`) | Week totals, a bar chart, and each account's week and budget | Rail or bar | This week (chart), Day, Accounts (budget bars) |
| Hours table (`WeekHoursTab`) | The timesheet grid, Copy table and Export | Rail or bar | None |
| Entries (`WeekEntriesTab`) | Every entry of the week by day: edit, group, ungroup, merge, delete, export | Rail or bar | None |
| Settings gear | Settings dialog | Rail or bar foot | None |
| Close timer | Quits | Rail or bar foot | None |
| Tile hover buttons | Open (goes to a page: tracker, strip and timeline go to Timeline; week and day go to Week overview) and open in place | Each tile | n/a |

With the desk on, the rail becomes a bar and the legacy left column is hidden. With it off, the legacy dashboard is the whole app. So today there are two homes, chosen by a preview switch.

## What only the legacy dashboard does

These must exist on the desk, or somewhere reachable from it, before the legacy home can go:

1. **Start day and Finish day.** On the left column and the pill's open list. The desk has no way to start or finish the day.
2. **Continue earlier block.** Already available on the desk: it lives on the message bar, which floats over every page including the desk. (An earlier draft of this audit said otherwise.)
3. **Reordering accounts** (drag grip) and the budget bar with its pace tick in a narrow list. The Accounts tile shows budget bars when big but cannot reorder.
4. **The day card** (working time, allocated, unallocated). The Day and Time tracker tiles cover the figures.
5. **Hidden-account handling, Accounts and budgets.** Now in Settings, so not lost.

## Proposal

**Principle: the desk is home. Detail lives one step away, in the same top bar.**

1. **Make the desk the default home** once items 1 to 3 above exist on it. Until then the preview switch stays.
2. **Retire the legacy left column and the Timeline page's split layout**, not the Timeline. The Timeline page becomes the full-width timeline with its editing bar, which is what the Timeline tile already opens in place.
3. **Top bar, left to right:** Pill, Desk, Timeline, Timesheet, then the date arrows, then Settings and Close. Four places and two utilities, nothing else.
4. **Fold the three week views into one Timesheet view** with a segmented control inside the page (Overview, Hours, Entries), rather than three top-level buttons. Overview is the least independent: its stats, chart and budgets are all desk tiles (This week, Day, Accounts). Keep it as the first section of Timesheet so nothing is lost, and let it shrink as the tiles take over its job. Hours and Entries stay in full: they are the timesheet and export work.
5. **Icons:** Desk (grid of tiles), Timeline (rows on a spine, as now), Timesheet (table, as the Hours icon now), Pill (the miniature pill, as now). The Week and Entries icons go, since they become sections.
6. **Opening a tile.** Two behaviours, always the same: the expand button opens the tile in place on the desk (a bigger version of itself, Escape returns); the open-page button goes to the dedicated view that holds its detail (Timeline, Timesheet). Tiles with no dedicated view (post-its, to-do, notes, clock, pomodoro) have only expand.
7. **Context carried across.** One selected date (`SelectedDate`) drives the desk, Timeline and Timesheet already; keep that. Add a selected account that Timeline and Timesheet honour when you arrive from an Accounts tile (a filter chip you can clear).
8. **Pill to Desk.** The pill's menu already has Open Desk. Add a visible way on the pill itself: its existing "open dashboard" press on the clock opens the desk once it is the home.
9. **No duplicated logic.** Views read the same model (`TimerModel`), and every button calls the same method. Moving a button never copies its code.

## Order of work if this is agreed

1. Add Start/Finish day and account reordering to the desk. Done: the Day tile has the button, and the Accounts list has its grips.
2. Make Timesheet one view with three sections, keeping every control and button name. Done: one `TimesheetTab` button, with Overview, Hours and Entries as tabs on the page.
3. Make the desk the default home and remove the legacy left column. Done in product: always on, opens on it, no switch, column hidden everywhere. To do: delete the older layout code and move the tests that still use it.
4. Date and account carried between views.

## Questions for decision

- Is it right that Overview folds into Timesheet rather than staying a separate button?
- Should the desk be on for everyone by default in the next alpha, or only after the three missing controls land?
- Account filter on arrival: a chip you can clear, or just a highlight?

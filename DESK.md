# The desk: plan

A project to turn the dashboard into a resizable desk of small tiles that you move, open and put away. This file is the working plan. Update it as decisions change.

## What it is

- The dashboard window can be resized in both directions, down to a minimum of about 640 by 480. Below that the pill is the next step down. Content is trimmed and rearranged to fit as the window changes size.
- The home view is a desk of small tiles that tile to fill the window. Tiles resize only when it is useful (a taller timeline, more room for notes).
- Every tile opens in place, as its full page, or both, depending on the tile. Small tiles are look and open only, with a control only where it is essential.
- A drawer (a side panel) holds tiles that are put away. Dragging a tile into the drawer hides it and dragging one out shows it. Each tile also has a hide button.
- The icon rail is replaced by a slim top bar (back to the pill, date arrows, settings, close).
- Tiles squeeze into smaller forms of themselves as the window narrows, and rearrange to use the space (for example a clock becomes just its face, the timer just the time, accounts become dots with times, a post-it becomes one line).
- Mock-ups live in the design canvas shared in the session (rows A, B and C). The chosen direction is B and C.

## Decisions so far

- Lively is the default style and Calm is a setting. Calm is also used whenever Windows is set to reduce animations.
- One soft hue per tile type. Tile hues are decoration and carry no meaning. They stay clear of the colours that do (green for tracking, amber for a break, red for overtime or a spent budget, blue for selection and the main action).
- Motion only when the user acts: hover, pick up, drag, drop, open, put back, drawer. Bouncy and large in Lively, short fades in Calm.
- A one-time first-run hint shows how to move tiles and use the drawer.
- To-do items link to accounts with a #Account tag that takes the account's colour. Links store the account, not the typed text, so renaming an account keeps them.
- Reminders and dated notes can appear in Coming up and, later, on the timeline.
- Trinkets for the first version are post-its, a to-do list and an analogue clock. A pomodoro timer follows. It is independent of time tracking at first.
- Durations stay HH:MM everywhere.

## Phases

1. Resizable window. The dashboard resizes between about 640 by 480 and full screen, remembers its size and position, and the existing pages reflow: below about 900 wide the account sidebar folds into a button, the toolbar compacts and the week cards stack. Content width is capped on very large windows, the timeline keeps its zoom, and the toolbar never overflows. Ships on its own.
2. The tile board (resizing, open in place, the Timeline tile and the top bar are in; Notes by day, Coming up and the day strip are in too, with data format 3 for notes; the trinkets, one post-it, to-do and analogue clock, are in; the drawer and Lively/Calm motion are in too; started: layout maths, drag to reorder, three tiles and the preview switch are in; spring motion, a saved arrangement per size and resizing tiles are still to do). A custom tile layout with drag and drop, a saved arrangement per window size, spring motion, and the first tiles (Working time, Accounts, Day strip) with open in place and full page. Behind a preview switch in settings, with the current pages still available.
3. The rest. The drawer, Notes by day, Reminders and deadlines, the remaining tiles, and the data changes they need (a schema upgrade with the install over an older database test before it reaches the Store build).
4. Polish (hues for the light and dark themes are done; the three studies still borrow from their base). Hues per theme, the Lively and Calm switch, the first-run hint, the trinkets (post-its, to-do, clock, pomodoro).

## Rules for this project

- Work on `feature/desk` and branches from it. Phase 1 is promoted to `alpha` on its own.
- The current dashboard stays working until the desk has matched it.
- Test state, not frames. Animations do not tick in the headless tests, so the feel needs hands-on testing in alpha.
- Do not change the colour roles. Tile hues are decoration only.

## Open questions

- Dated notes show on the timeline as small markers; they could later be edited or moved from there.
- Which tiles show at the minimum size by default (suggested: Working time, Accounts, Day strip).
- Tile forms are roomy, normal and tiny. A fully fluid form is a possible later step for particular tiles.
- Whether dropping a tile onto an occupied spot swaps or pushes.
- How the full views are reached once the rail is gone besides expanding a tile.
- What the pomodoro does at the end of a work block (suggested: a quiet nudge only).

See NAVIGATION.md for the audit of the navigation and the proposed structure.

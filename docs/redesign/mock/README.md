# The mock

These are the Design canvas boards the redesign was drawn and agreed on (`*.dc.html`, with `canvas.json` for their layout). They open in the Design canvas at https://claude.ai/artifact/5rsmaa9K1jvdpW9vwvJmUQ (private to the owner), not in a browser, and they are kept here so the design survives if the canvas is lost. They are not part of the build.

Main is the Timeline page, Pill the pill states, Week the Week overview, Settings the Settings page, Accounts and AccountsDialog the account pop-out and Accounts and budgets, Setup Getting started, Icon the application icon, EntryDialog Add time, Messages the messages and errors, Notes the Notes page and Lights the status-light options (the third was chosen). PillSoft is the pill as built (with the account list sliding out from behind it), Pill the earlier round 3 states and Refine the round 2 options that were decided between. Round7 holds the tooltip card and the rail icons (with their hover motion) as agreed and now built. PillMerge explores putting the play or pause tile and the Accounts button on one background (a proposal, nothing built). PillHorizontal draws a horizontal pill with the account's state dot, full name and a drop-down list (three arrangements; a proposal, nothing built).

Themes holds five theme proposals (Slate, Midnight, Sage, Mono and a Hawthorn-inspired one), each with its own account colours, drawn on the Timeline and Week layouts and on the pill in every state. They are not built yet.

Store holds a draft Microsoft Store listing: every Partner Center field written out, five draft 1920 by 1080 screenshots (real renders with demo data, to be retaken on Windows) and the art tiles in `store/`.

AccountsAlongside draws three ways to keep the account list and the pages in view together (a docked panel, a slim strip that opens into it, and a recording chip in the header that hangs the list beneath it) and a table of how Timeline, Notes and Week react to the room they have. A proposal, nothing built.

AccountsDock is option A from that board, chosen, drawn as a live prototype of the docked account panel opening and closing (one eased progress value drives the panel, the rail's wash and each page's reflow), with frames of the opening, what moves in what order, and the timings. A proposal, nothing built.

ReminderTimes draws four ways to lay out the reminder picker's four quick times (09:00, 12:00, 15:00, 17:00), which sit in a 2 by 2 grid wider than the hour and minute boxes: the same grid in narrower chips (A), one row under the month (B), one column the width of the steppers (C, recommended) and a Quick menu (D). Drawings only, nothing built yet.

WeekExtras draws five other ways to show a day's meetings and open notes under its name on the Week overview chart (two quiet chips, words, a dot for each, a small agenda, and a mark with the detail on hover), beside the current icons with a number. Drawings only, nothing built yet.

Icons draws every icon the app has, taken from the drawing code (`IconGlyph` and the controls that draw their own symbols), on the 18 unit grid they are built on, with each one's ink size, how far it sits from the grid's centre, where it is used and which are never drawn, plus a width and height chart and a list of consistency points. The preview test `WriteIconSheet` renders the real icons for comparison. On the canvas the boards are laid out in a grid of four columns under four section titles (the app as built, the pill with its icons and messages, options we chose between, and drafts), each with its note above it.

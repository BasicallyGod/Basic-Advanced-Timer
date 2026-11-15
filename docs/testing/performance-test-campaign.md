# Alpha performance test campaign — outline

Run when convenient after the implementation batches. Record the candidate's full version/commit, Windows display scaling, monitor arrangement, theme, and whether data is demo or an Alpha test folder. Build 619 is the last user-confirmed animation baseline; build 621 introduced startup preparation and still needs native evaluation.

Use the demo for visual stress and a separate Alpha test folder for persistence checks. Keep continuous frame timing off for the initial comparisons; use it separately if investigating a remaining problem. Passive reports measure UI callbacks and operations, not frames presented by the GPU.

## First pass: launch and everyday use

| Priority | Scenario | Steps | Expected result / evidence |
|---|---|---|---|
| P1 | Cold launch, immediate dashboard | Fully quit, launch, open the dashboard immediately; repeat three times and copy each report. | Pill becomes usable promptly; first opening remains visually smooth. Compare storage, pill/dashboard construction, first-request preparation and callback times separately. |
| P1 | Prepared dashboard | Fully quit, launch, wait 2–3 seconds, then open. | First opening stays smooth; compare preparation cost with the immediate-open case. |
| P1 | Setup and cold menu requests | In a fresh test folder check first-run setup; separately open Accounts or Notes from the pill before ordinary dashboard use. | Correct view opens once; no missing request, duplicate dialog or unexpected return to setup. |
| P1 | Normal tracking | Start, switch accounts, pause, resume and take a break. Leave the dashboard hidden for several minutes. | Clocks and allocation remain correct; reminders and checkpoint saves continue while visual work is paused. |
| P1 | Persistence | In the Alpha test folder track known intervals, wait through a checkpoint, quit and reopen. | Accounts, entries, preferences and running-session recovery behave as expected. Keep this separate from demo-only visual tests. |
| P1 | Account identity | Add an account, drag it before existing rows, rename existing accounts and save; reopen the app. | Order and colours persist; each account keeps its own time, notes and grouped blocks. |
| P2 | Diagnostic controls | Start/stop frame timing several times; separately pause routine refresh while tracking and waiting for a reminder. | Fresh frame sessions have one callback chain. Checkpoints and reminder maintenance continue during refresh pause; important errors/alerts still appear. |

## Second pass: transitions and hidden views

| Priority | Scenario | Steps | Expected result / evidence |
|---|---|---|---|
| P1 | Repeated transitions | Expand/collapse 10 times on Timeline, then on Notes, Week and Settings. Include a few quick reversals. Copy a report after each group. | No enlarged frame, clipping jump, flicker, stranded half-grown window or unexpected pill reappearance. Note perceived smoothness as well as timings. |
| P1 | Draft and list catch-up | Type an unsubmitted note; switch pages, collapse/hide, wait past a reminder, then return. | Draft/search/filter state survives; lists, counts, overdue colours and timeline time catch up immediately. |
| P1 | Hide/minimise/restore | Hide to tray or minimise on each page, including during a transition; return after several minutes. | Latest state appears once; no frozen controls or delayed resurrection after hiding. |
| P1 | Quit and relaunch | Quit from pill and dashboard, both before and after using the dashboard. | Process exits cleanly; no hidden window or timer brings the app back. |
| P2 | Reduced motion | Test with Windows animations disabled, then enabled. | Disabled motion reaches the final state directly; normal animation resumes when enabled. |
| P2 | Display scales and themes | Try Light/Dark and available 100%, 125%, 150%, 200% display scales; move between monitors if available. | Pill, snapshot and live dashboard remain aligned, including first and last frames. No lost or off-screen window. |

## Longer check

Run 30–60 minutes of mixed use, with at least 50 transitions and a prolonged hidden/minimised period. Observe CPU activity and memory trends after warm-up rather than comparing a single memory reading. Expect memory to settle rather than grow continually with repeated page changes. An overnight run can cover reminder/date rollover without altering the system clock.

If a stall remains, copy the passive report after reproducing it. Handoff timings distinguish snapshot release, pill show, dashboard hide, activation and deferred refresh. A short recording is useful for a transient visual error; include the page and whether it occurs on opening or closing.

## Result log

| Build / commit | Scenario | Immediate or prepared | Display scale / theme | Result | Report / recording | Follow-up |
|---|---|---|---|---|---|---|
| | | | | | | |

Prioritise data correctness, lost windows and draft loss above cosmetic differences. Before further optimisation, separate reproducible rendering problems from isolated scheduling delays using the reports and visual observations together.

## Follow-up: dashboard native bounds

Alpha 628 feedback passed the functional scenarios but reported one dashboard opening at the correct content scale inside an undersized window. Most repeated transitions had approximately 16–17 ms callback cadence; two recent expansions had approximately 150 ms of dispatcher lateness without a matching save or workspace rebuild. Those stalls remain unexplained and are separate from window sizing.

After the bounds guard change, repeat immediate and prepared opening plus collapse/reopen near the work-area edges. If multiple monitors are available, include different scaling/work-area sizes and move the pill between them before expanding. The dashboard should use its intended size, limited to the source monitor's work area, without cropping at an old window boundary. Reduced-motion opening should reach the same bounds.

Copy report includes expected and actual native dimensions in pixels and the result of any corrective resize. If cropping recurs, copy that report before collapsing, and capture a screenshot showing the outer window edge. Automated checks simulate stale native bounds and late logical-size notifications; they do not reproduce native Windows monitor/DPI event ordering.
## UI follow-up after Alpha 639

- Settings uses the original gear toggle and Settings heading again, without a Back button. Reopening starts at Appearance. Account management remains under Settings / Hours and accounts; there is no Manage button or time/budget explanation beside the sidebar heading.
- Notes uses the original account-filter chips, Clear action and empty messages. The result-summary row and Reset all filters action are gone. Search, account filtering and note views remain independent; drafts and queries should survive switching pages.
- In Light/Dark, focus and resize ordinary, short/narrow, multiline and borderless text fields. There should be no blue focus boundary, no geometry jump, and no inset shadow outside the field. The Timeline card's inline note editor should rely on its enclosing form rather than drawing a second shadow inside it.
- Check the Timeline's left/right edge shading: narrower and lighter, without changing scrolling, selection or block editing. With nothing selected, Details should contain just a short prompt.
- Keyboard ownership, ordinary removal Undo/storage retries, visible budget periods and direct week copy/export from Alpha 639 are retained. Spot-check those workflows alongside expansion/collapse. Intermittent dispatcher stalls remain a separate investigation.

## E-ink display style (Windows checks)

The look is built and tested headless (pictures are written into `TIMER_PREVIEW_DIR` by `DisplayInkTests.PicturesOfTheWeekAndTimelineInBothLooks`), but none of it has been looked at on real Windows. Check, with Settings, Look, Display style set to E-ink and the Effect at 60, 0 and 100:

- **Speckle and dot matrix at 100, 125, 150 and 200 per cent display scale, Light and Dark.** Letters should keep their shape (the speckle is a tiled mask over normal type, not a bitmap of the letters; if it blurs, fall back to per-string bitmaps as `docs/design/eink-display-plan.md` describes). The account name is GNU Unifont's 8 by 16 dots at 1.69 pixels a dot, bold: check no dot lands half a pixel off and the six cells read at the pill's size.
- **Windows high contrast and the contrast tests in practice:** ink on paper, quiet words, the green, amber and red of the status colours and the keys' outlines.
- **The pill:** the clock's 30 pixel type and the two digit alert count never change width; the pill is a little wider in E-ink than in Phosphor (type is wider than segments), and the window follows when the look is changed in Settings while the pill is showing. The play and break keys are 34 by 16 in E-ink (the plan's 44 by 20 was not built; look at whether they are big enough to press). The compact pill now keeps both keys in both looks: check its width and that Reset position and a screen change still size it correctly.
- **The tall pill:** Budgets are gone, the bat is Phosphor-only, and a saved `pilllower=budgets` or (in E-ink) `pilllower=bat` shows the day strip. Press the lower display through its cycle in both looks; in E-ink the change is two steps, and with Windows' animations off it is instant.
- **The timeline and Week page:** blocks with textures, plated words, the recording block's inset green ring, the selection ring, reminder tags and the ruler readout; the Week instrument's accounts folding, scrolling by rows with the wheel, tracing an account, and Week and Timesheet keys. Compare the frame monitor (Settings, About, Development) on the Timeline and Week pages in E-ink against Phosphor: grain over every block should cost little, but measure it.
- **Phosphor Dark's bright CRT account colours** and the pale Week display in Phosphor Light.
- Reduced motion: the ticker, the name scroller and the lower display's change stand still or jump.
- **Seasonal decorations** on the wider E-ink pill and the new compact pill.

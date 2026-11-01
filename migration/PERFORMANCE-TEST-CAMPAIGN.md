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

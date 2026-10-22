# Compact sidebar experiments

In Settings → Appearance and behaviour → Compact accounts, choose **Vertical gauges**, **Heatmap strips**, or **Integrated capsules**. **Current · names and bars** remains the default. The choice is saved locally and can be reversed at any time. These options affect the Notes account column only; the navigation rail, pill and expanded dashboard retain their existing behavior. Experimental sidebars stack hours over minutes at the top, with icon-only pause/resume, break and start/finish-day actions fixed along the bottom. Tooltips and accessible labels describe each action and follow its current state. The original sidebar keeps its horizontal clock and labelled day action.

| Design | Account column | Row height | Budget display |
|---|---:|---:|---|
| Current | 176px | 60px | Name, today’s time and horizontal bar |
| Vertical gauges | 112px | 42px | Bottom-up fill beside abbreviation and today’s time; tick shows progress through the budget period |
| Heatmap strips | 112px | 42px | Seven cells fill from bottom to top, with partial cells retaining the exact utilisation |
| Integrated capsules | 112px | 42px | Abbreviation inside a coloured capsule that fills from below; today’s time alongside |

The three alternatives use 36% less column width and 30% less row height than the current implementation, with 28px clock digits and a 32px-wide icon button for each bottom action. Account rows remain 42px click targets and scroll independently. The reference image’s approximate 120px width differs from the current app’s 176px implementation.

Account order and colours come from the existing model. Abbreviations use the existing collision-safe label helper (including numbered names); full names remain in tooltips and accessible names. A budget’s fill uses the same configured daily, weekly, monthly or quarterly window as the expanded dashboard. Today’s recorded time remains a separate figure. A dash means no budget, while an empty indicator means a budget with no usage. A red cap and `!` indicate usage over 100%, and the tooltip retains the actual percentage. Recording uses a live dot and tinted row; the selected account retains an outline when paused.

Tooltips show the full account name, today’s recorded time, budget usage/limit, reporting period, percentage and recording/over-budget state. Scheduling a note does not contribute to these figures.

`CompactPrototypeTests` validates budget windows, saved choices, tracking, active/paused states, ordering, no-budget/zero-budget/over-budget states, scrolling with many accounts, minute rollover without losing tracking seconds, and icon control placement/state labels. With `TIMER_PREVIEW_DIR` set, it also renders each alternative and the current design from identical data in Light and Dark themes.

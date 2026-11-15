# Display and data presentation

Package E of the Windows UI/UX cohesion work, following the shared record editors in Package D.

## Account identity and wording

`AccountPresentation` and `AccountCatalogue` supply the real display name, source key, colour, configured/hidden/historical state and optional E-ink pattern. The source key remains the existing account association. Display normalisation, segment fallbacks and chip captions never become account identifiers or get written back to names.

Capture a catalogue once for the relevant refresh. Ordinary assignment controls do not allocate patterns or calculate budget totals. Recording lists can provide their already-filtered catalogue; a known hidden account can still be described from configuration. Record editors retain the full review catalogue so assigned hidden and historical accounts remain available. Renames follow the existing draft, texture and ledger remapping rules.

Visible assignment chips and suggestions use the real spaced name, including acronyms. `#` remains a typing shortcut. It does not change how the account name is displayed. Budget descriptions use the model's existing formatting, usage and period rules. When a caller has already calculated usage, pass it into the shared description rather than rereading the ledger.

Sidebar drawing consumes the totals supplied by `AccountList`'s refresh. Its paint path does not calculate ledger or budget totals or allocate patterns. This preserves the earlier per-refresh caching work while keeping the full budget value and period available in the row's accessible name.

## Keep the material boundary

Navigation and editing remain ordinary type. The pill, sidebar, timeline and Week instrument continue to use the established Phosphor and E-ink renderers. Geometry, transport keys, BREAK treatment, honeycomb, bloom and paper erosion are retained.

Application legends can use the existing sentence-case conversion in E-ink. Account names, meeting names and user-authored ticker content explicitly preserve their case. Measurement and drawing receive the same case and weight policy. Bold block labels are measured with the bold typeface they actually draw, and ellipsis fitting includes the ellipsis width. This avoids clipping uppercase names or turning acronyms into ordinary title words.

Phosphor's Week percentage legend uses `pct`, since its segment alphabet does not contain a percent glyph. E-ink keeps `%`. The underlying percentage, status thresholds and budget period are unchanged.

## Name scroller and previews

`SegmentName.SetAccount` is the common account-to-display entry point. Unchanged names and unchanged widths preserve scroll position. A width/look/effect or actual name change updates the relevant render inputs. Static short names do not require an animation timer.

Phosphor keeps its existing whole-cell loop. E-ink keeps its initial 1.4-second reading pause, then loops continuously with the existing gap. Crossing a loop boundary preserves the fractional overshoot and does not add another pause. Reduced motion presents a stable portion; returning to motion can wake the control on its next owner refresh. Hidden and detached controls retain the existing stepped-control timer ownership.

Settings pill pictures use the same name conversion and glyphs with `Animate = false`. They remain deterministic render-only illustrations with no tracking model. Preview group dimensions and the approved pill composition are unchanged.

## Dense timeline items

`DisplayItem` adds keyboard and automation behaviour to the existing Border targets. Its style key is deliberately Border, preserving the existing backgrounds, hit regions, hover material and pointer gestures. Pointer activation still uses the established drag/resize/select code. Focusing by pointer does not scroll the timeline to a new position.

Tab can reach even a minimum-width block or stack. Enter/Space selects it; Shift retains the existing multiple-block selection meaning. Shift+F10 or the context-menu key opens an item's existing commands. A named automation item exposes the complete account/meeting name, time, duration and selected state even when the drawn label is omitted. Stack members retain their own named keyboard targets in Details. The recording item remains nonselectable.

A separate dashed focus adorner distinguishes keyboard focus from persistent selection. Keyboard activation restores focus to the rebuilt target after the model refresh. Minimum block width, placement, overlap, magnet, pointer-gesture deferral and undo boundaries are unchanged.

## Week and Timesheet

The instrument retains its existing geometry and rendering. Its overlay controls are updated in place, preserving focus when a trace changes or layout repeats. The trace, account scroll window and quiet-account expansion are session presentation state owned by the model; switching view mode or rebuilding the instrument does not silently lose them. Account renaming relinks the trace.

Account and block trace controls expose their pressed state. A Clear trace action is available while tracing, and returns keyboard focus to the account row when possible. Opening a day remains a separate action. Week/Timesheet keys expose their current state and restore keyboard focus after switching modes.

Timesheet account cells use `WeekTable`'s reconciled displayed/copy values, keyed by the actual account association and calendar date. The automation readout includes account, day and actual value, including zero cells, and is read-only. Activating a cell opens that day. Only the visible account window has cell controls, keeping the automation tree bounded. Budgets continue to use their own reference period; a monthly or quarterly budget is not presented as the week's account total.

Working-day measures and sums of overlapping credited entries can differ. Existing overlap indicators and underlying totals remain authoritative. This package does not redefine working time, allocation, export reconciliation or storage precision.

## Keyboard routes

| Context | Action |
| --- | --- |
| Pill, including Compact | Alt+A opens or closes account choice |
| Timeline item or stack | Tab to reach; Enter/Space to select; Shift for block multi-selection |
| Timeline item with commands | Shift+F10 or context-menu key |
| Details stack | Tab to a member; Enter/Space to select |
| Week account or block | Enter/Space toggles the trace |
| Timesheet value | Named read-only value; Enter/Space opens its day |
| Tall lower display | Enter/Space cycles; context menu chooses a mode directly |

## Verification boundary

Headless coverage checks real names and associations, hidden/historical accounts, pattern and trace remapping, case/weight measurement, loop continuity, dense-item keyboard/context actions, trace focus, reconciled Timesheet values, zero cells, Compact account access and Tall mode values. Render checks cover Light/Dark with Phosphor/E-ink. Existing pointer gesture, draft and decimal reconciliation tests remain release gates.

Native scale, motion, focus, recovery and packaged Store acceptance remain Package F. No schema, package pin, recording policy or export format changed here.

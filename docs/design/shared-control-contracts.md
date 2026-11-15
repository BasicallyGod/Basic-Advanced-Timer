# Shared control contracts

Package B of the Windows UI/UX cohesion review, following the first corrections in PR #258.

## Menus and choices

Native Avalonia ComboBoxes remain in use. ChoiceStrip provides labels and account swatches but does not intercept their keys. Native arrow navigation, type-ahead, Enter, Escape, focus return and popup placement remain framework responsibilities.

Dropdown and context-menu surfaces use Panel, Ink, Line and Hover, matching application picker cards. Menu radius is 16, item radius 10, outer padding 6 and item padding 10 by 7. These values describe menu chrome, not the dimensions of every application control. Menus cap their height at 360 logical pixels and scroll. Existing calendar/reminder shadow clearance is retained; no new out-of-bounds popup shadow is added.

The Notes account filter is explicitly a multi-selection control. Every row exposes checked state and toggles across its whole usable area. Account rows scroll within 280 logical pixels; the summary and relevant Clear action stay outside that scroll area. Toggling reconstructs the current row content and returns focus to its replacement. Clear returns focus to the first account. PickerFocus owns initial focus, Tab containment, Escape dismissal and focus return.

ChoiceButton supplies the existing button material with a native radio role. Model-owned selected classes are authoritative, so a refused action does not leave a misleading checked state. Arrow keys wrap through available sibling choices; Home/End choose their endpoints. One choice per group is a Tab stop. Automation Select invokes the same action as pointer/keyboard activation rather than changing only a visual property. Settings tiles, settings navigation and shared segmented/chip choices use this contract.

CompletionButton supplies the existing round completion presentation with a checkbox role and native toggle behaviour. Notes retain their existing Toggle command; completion is not reintroduced into Details. Importance retains its existing ToggleButton implementation.

Field and icon actions use human-readable accessible names. Internal identifiers such as EntryAccount are not spoken as dropdown labels. A DialogButton retains its text name when its content becomes an icon/text panel.

## Duration entry

| Field | Bare numeric value | Bounds | Blank |
| --- | --- | --- | --- |
| Block length | Minutes, including the existing fractional-minute shortcut | Positive, at most 24 hours | Invalid |
| Account budget | Hours, including previously accepted fractional budgets | Positive finite hours; no new 24-hour limit | No budget |
| Daily break allowance | Minutes | Zero to four hours | Invalid |

Both HH:mm and decimal-hour preferences control field presentation. Explicit input remains independent of that preference:

- `01:30` means 90 minutes.
- `1.5h`, `90m` and `1h 30m` mean 90 minutes.
- `1.30` in a budget means 1.3 hours. It is not guessed to mean 1 hour 30 minutes.
- A bare `45` remains 45 minutes in a block and 45 hours in a budget.
- The current culture's decimal separator is accepted. An invariant decimal point is also accepted for pasted values. Thousands separators are not inferred.
- Clock fields remain clock fields, including the existing midnight `24:00` end boundary. They are not interpreted as durations.

DurationBox shows a short h/min cue for bare numeric text and exposes the policy in accessible help text. Explicit colon/unit text does not acquire a misleading bare-number suffix. Blank budget fields say none.

Presentation is not storage precision. An unchanged field returns the exact value supplied when it was populated. Consequently opening a fractional-hour budget in HH:mm mode, or a short precise duration in decimal mode, and saving unchanged does not round the value. A format preference change reformats a pristine field, preserves its original value, and leaves unfinished edited text intact. Model subscriptions are attached only while the field is in the visual tree.

Explicit edits parse to a new value. Invalid text stays available for correction. Add time refuses an invalid length instead of saving its previous, now stale, end time. A valid length updates the end as before; fractional seconds survive that conversion and clock text round trips. The ledger's established credited-duration precision and overlap validation remain authoritative.

## Validation

Automated coverage includes culture-sensitive input, unit compatibility, invalid/non-finite values, budget bounds, unchanged fractional budgets, live format changes, fractional-second clock round trips, stale-end refusal, native dropdown keyboard ownership, radio navigation and automation Select, completion keyboard/automation toggles, and multi-select filter focus/clear/Escape behaviour.

The control gallery uses the same production duration fields. Native form/menu comparison is required in both display styles. Narrator speech and user comprehension are separate acceptance checks; automation patterns alone do not establish either.

This package does not change Details draft ownership, note identity remapping, recurrence generation, queued storage writes, storage schemas or Core/Storage package versions. Those protections remain in place for the later shared-draft and editor packages.

# Overnight meetings and Details drafts

Repeating meetings retain a draft until Save or Discard. While a draft is pending, selecting another item, clearing selection or navigating to another day is refused with a short explanation. The existing Save and Discard buttons resolve it. Page changes and hiding the dashboard retain the existing Details instance and draft. Invalid text is retained for correction; validation never discards other pending fields. Each committed start change preserves the duration of the most recent valid draft, including when the end falls on the next date.

The Details save-state line distinguishes editing, queued storage writes and durable saves. A refused write remains in Notes' retry queue and says Not saved · retrying. Recovery refreshes that status without a second user edit. Recurrence failures also prevent a misleading Saved status.

Workdays and calendar dates serve different purposes. Copy compares configured workday identities, so blocks on either side of midnight can be copied as one shift. A meeting request preserves the actual calendar date at the pointer, including when rounding carries into the following date.

Meetings remain one item when they span midnight, with a maximum duration of 24 hours. The calendar date is the start date; the end is an offset from it. An earlier end clock means the following date, and rows explicitly show (+1 day). Both add forms, both editors, recurrence, timeline dragging and stacked-meeting resizing preserve that offset. The Notes page's day grouping/filtering follows the configured workday.

Core/Storage 0.10.0 introduces data format 7 to retain overnight ends for occurrences and templates. The upgrade makes a backup and changes no existing note values. Older builds refuse format 7; returning to an older build requires restoring the pre-upgrade backup rather than opening the upgraded data.

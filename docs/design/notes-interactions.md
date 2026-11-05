# Notes interaction refinements

The entire note or meeting row in the timeline's Notes card selects the item, including its padding and chips. Existing text buttons retain keyboard activation. Completion ticks, important stars, links and delete buttons keep their own actions without selecting or toggling the row a second time.

The note Details header places the important star immediately before Delete. The body contains the words, account and reminder. Completion stays on the Notes card's row tick and on the Notes page.

Importance is controlled by the star independently of typed text. Editors do not show storage flags, and typing `!!` creates literal punctuation. Existing starred notes keep their importance. New literal text is escaped when necessary inside the existing note text storage format; reminders, account edits, retries and reopening preserve it without a schema change.

The Notes page's add-form host allows the kind picker's rounded shadow to extend beyond the single-line Note form, just as it does for Meeting. Settings shows its seven navigation sections immediately, without a search field.

Review Notes and Meeting add forms, note Details and Settings in Light and Dark. Check pointer clicks in row padding, row actions, star state while an add draft is stashed, existing starred notes and literal punctuation across persistence.

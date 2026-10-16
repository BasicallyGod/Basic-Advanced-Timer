# Feed: a wire of client updates read from files

Agreed in conversation on 16 October 2026. The shared reader is `Feed` in `Timer.Core` 0.2.0, and the Feed tab, its Settings section and the prompt are built in this app. **Not yet built:** the account strip (unread items for the account being tracked), so for now the feed is only on its own tab.

## What it is

A **Feed** tab on the dashboard that shows a continuous stream of short updates about clients, newest first, like a printout from a news wire. The updates are written by something else (a scheduled ChatGPT or Claude task, a script, or by hand) into Markdown files in a folder. The app only reads them. There is no network access, no account and no API key, so client details stay on the computer and in whatever cloud folder the person already syncs.

## Principles

- **Read only.** The app never writes to, renames or deletes anything in the feed folder, so a file can be rewritten at any moment without conflicts. Read and done marks live in the app's own settings.
- **Plain text.** Anything that can write Markdown can feed it. No special tool is needed.
- **Same words as Notes.** An entry uses the Notes shorthand already in the app (`#Tag` links an account, `!` marks urgency, `@day` and a time are understood), so a feed item can become a day note without rewriting it.
- **Untrusted input.** The files are treated as untrusted text (see Safety).

## The folder

Settings, Feed, has a folder path. Every `.md` and `.txt` file in it (not in subfolders) is a **source**; the file name, without the extension, is the source label. A person might have one file per scheduled task or one per client. The app watches the folder for changes and also has a Refresh button, and shows when the newest file was last written ("Updated 3 hours ago").

## Entry format

An entry starts at a heading line (`#` or `##`, and so on) and runs to the next heading or the end of the file.

```
## ! Acme: supplier delay confirmed
Date: 2026-10-16 08:30
ID: acme-delay-1016
#AccountOne
The shipment slipped to Tuesday. Worth a call before the 2pm meeting.
```

- **Headline:** the heading text. A leading `!` marks it urgent (as a leading `!` makes a note a deadline) and is dropped from the shown headline.
- **Date:** an optional `Date:` line (`2026-10-16`, with an optional time). Without it the entry takes the file's last-written time, and entries without a date in one file are ordered by their position (earlier in the file is newer, matching a file that is written newest first).
- **ID:** an optional `ID:` line. When present it identifies the entry. When absent the app uses a fingerprint of the date, headline and text.
- **Tags:** `#AccountName` anywhere in the entry. A tag links an entry to an account only when it names exactly one account the app knows (the same rule as the to-do tag); other tags stay as plain words.
- **Body:** everything else, shown as plain text with line breaks and simple Markdown (bold, italic, bullet lists). Never HTML.

A file with no headings counts as one entry (headline: the file name).

## Merging

All sources are read and the entries merged into one stream, newest first. Because a scheduled task may rewrite its whole file each run, entries with the same ID, or the same fingerprint, from any source count as one and show once. An entry whose wording changes without an ID shows as a new entry; the optional `ID:` line is how an author keeps an entry the same through edits.

## States

Both are stored in the app's settings (the `feed` JSON section), keyed by ID or fingerprint:

- **Read.** Set automatically when an entry is expanded (this is on by default, with a switch to turn it off), by hand with a toggle, or for everything above a chosen item with Mark everything above as read.
- **Done.** For entries that need action. Separate from read. Done entries are hidden unless the Done filter is on, and Done can be undone.

The record for an entry is dropped from settings once the entry is gone from the folder and past retention, so the settings stay small.

## Display

A Feed tab on the dashboard's icon rail, with a badge showing the unread count.

- One scrolling stream with day dividers (Today, Yesterday, then the date).
- Each item shows its time, a source chip, account chips, the headline and the first two lines of the body. Clicking expands it to the full text (and marks it read).
- Unread items are normal weight; read items are dimmed.
- Filter chips along the top: Unread, an account, a source, and Done.
- Add to notes on an item makes a day note from it on today (or the day it names) with its tags, using the existing note code.
- **Account strip:** while an account is being tracked, a slim strip on the dashboard shows up to two unread items tagged with that account, so the briefing is in view when the person switches client.

## Retention and size

- Entries older than **30 days** are hidden, by their date. A setting changes this to **90 days**. Nothing is deleted from the folder, because the app never writes to it.
- Settings, Feed, shows a note on the folder: its size, the number of files, how many entries are showing, and how many are hidden by retention ("2.4 MB in 18 files. 412 entries showing, 120 hidden as older than 30 days."). A line is shown when the folder is large (over 50 MB) suggesting old files be cleared by hand.
- Parsing is done on a background thread and only re-done for files whose timestamp or size has changed, so a large folder does not slow the app.

## Prompt helper

A **Copy prompt** button in Settings, Feed, puts a ready-made prompt on the clipboard for ChatGPT or Claude scheduled tasks. It describes the format above, lists the person's actual account tags (for example `#AccountOne`, `#Acme`), asks for newest-first entries with a `Date:` line, and asks for a stable `ID:` on each so edits do not make them look new. The tags in the prompt come from the person's accounts at the time it is copied.

## Safety

- Files are limited in size (1 MB each, and a folder limit on the number of files read; anything over is skipped with a note in Settings).
- Text is shown as text. No HTML is rendered and no scripts run.
- Links are shown as plain words and are not opened automatically. Opening one asks first.
- A tag only links to an account the person already has. A feed file cannot create, rename or change an account, a budget or any time.
- The folder path is chosen by the person; the app does not search for folders and does not open another app's data folder.

## Where the code goes

- **`Basic-Timer-Core` (shared, 0.2.0):** the entry parser, the merge and duplicate rules, and the retention rule, as pure code with tests. Together with `NoteShorthand` and the shortcut parser, which move there from the desk app so both apps use one copy. No UI types.
- **This repository:** the folder watcher, the `feed` settings section (read/done marks and options), `FeedView` (the tab and the account strip), the Settings pane, and the prompt text.
- **The desk app** can add a Feed tile later with no new parsing work.

## Tests

Parser (headings, dates, IDs, tags, files without headings), merge and duplicate rules, retention at 30 and 90 days, the read and done states, the folder note, safety limits, and a visibility audit entry for the new tab in Light and Dark.

## Related work agreed at the same time

1. Port Quick Capture and Notes onto the dashboard as a tab.
2. Port the pill layouts (Full, Compact, Minimal) and their settings picker.

Both come from the desk app, and both depend on moving `NoteShorthand` into the shared repository first.

## Build order

1. Shared 0.2.0: shorthand and shortcut parsers, then the feed parser.
2. Dashboard: Notes tab and Quick Capture.
3. Dashboard: Feed tab, Settings pane, prompt helper.
4. Pill layouts and settings picker.

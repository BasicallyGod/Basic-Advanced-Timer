# Feed sources and delivery

The Feed imports reports into `feed.db` beside `timer.db`. External content, source configuration and read/done marks are separate from time records. Include `feed.db` when backing up or moving the data folder, with the app closed. Existing automatic time-ledger backups do not include the Feed archive.

## Add a source

In Settings → Feed, select **Add source**, name it, choose a type and optionally assign a default account. Tags within an entry override the default account. Edit changes future imports; already imported entries retain their account association.

- **Local or synchronised folder:** select the local folder containing `.md` or `.txt` reports. OneDrive, Google Drive and similar services work through their local sync client. Make the folder available offline. The app reads files directly in that folder, not subfolders, and never modifies them.
- **Paste / import report:** paste ordinary text or Markdown into the source editor and save. Use **Import / edit** to add later reports to that source. Headings split a report into entries; the original report is retained.
- **RSS / Atom news feed:** enter the publisher's HTTP or HTTPS feed URL. This is independent of assistant reports. No API key is required. HTML is displayed as text rather than executed; external images are not loaded.

Sources show their last successful check and any import errors. Pause stops automatic imports; Resume restarts them. Remove keeps imported reports and their read/done states. Refresh checks active sources. The original folder setting remains for existing installations, with existing entry IDs and marks preserved.

Folder notifications are debounced, with a one-minute polling fallback. RSS is checked every minute. Imports run while the Timer is open and catch up on startup. Reports remain readable when the source disappears or the computer is offline. Age settings hide older entries without deleting them.

Files are read twice, 400 ms apart, and accepted only if their contents agree. Locked, unavailable, changing, empty and oversized files are retried on a later check. Each report/feed is limited to 1 MB; each folder check considers its newest 200 files. RSS/Atom reads at most 500 entries per response. HTTP requests and downloads have timeouts, and XML DTDs/external entities are prohibited. These bounds prevent a single source from consuming unlimited resources; very large source collections should be split or archived outside the watched folder.

## Report format and identity

Ordinary text works without a template. Optional headings, dates and IDs improve structure:

```markdown
## Supplier update
Date: 2026-10-18 09:30
ID: supplier-shipment-123
#AccountOne
The revised delivery date is Tuesday. [Original announcement](https://example.com/news).
```

Use the same explicit ID when revising an item. IDs are scoped to a source, preventing unrelated assistants' `ID: 1` items from colliding. Repeated identical content within that source deduplicates. Without IDs, content fingerprints identify repeated entries; an unambiguous unchanged heading/date within the same file also survives body edits. Changing the heading, date and body without a stable ID can create a new entry. Reports from independent sources remain independent even if their text matches.

The Feed retains original report versions for reference. A source update cannot erase an earlier imported entry. Safe HTTP/HTTPS links appear as explicit clickable destinations in the detail pane; file links, scripts and embedded HTML never execute. Adding an entry to Notes and the account update strip use the same account resolution as the Feed.

## Scheduled assistant delivery: verification gate

Documentation checked on 18 October 2026:

- [ChatGPT scheduled tasks](https://learn.chatgpt.com/docs/automations) documents scheduled work with connected tools and skills. Desktop tasks that need local files require the computer and app to remain running. Web tasks do not have a persistent local folder.
- [Claude recurring tasks](https://support.claude.com/en/articles/13854387-schedule-recurring-tasks-in-claude-cowork) documents scheduled sessions with connected tools. Cloud scheduling and local-file scheduling have different execution requirements, including differences for older schedules.

These capabilities do **not** establish that a particular user's existing task can write a cloud file or call a custom MCP operation without an approval prompt. No provider-account unattended delivery test was performed in this change. There is no cloud relay, direct MCP submission endpoint or built-in paid AI scheduling in this release.

For either provider, first run a small report-writing task against a folder or file-writing connector that the account actually supports. Then configure a scheduled run with the same destination and verify all of the following:

1. A new Markdown/text file reaches the selected local/synchronised folder without manual copying or an approval prompt.
2. Its entries arrive in the Timer with their date, source, links and account.
3. A second scheduled run arrives without intervention; refreshing does not duplicate the first report or reset its read/done marks.
4. Closing/reopening the Timer catches up, and temporarily disconnecting the source leaves imported reports readable.

A short instruction for a supported destination is: “Save each report as a new `.md` file in [destination], named `feed-[task]-[date-time].md`. Keep links and publication dates; use stable IDs for items you revise.” A sync folder by itself does not give an assistant permission or tools to write there. If unattended file creation is unavailable, use paste import until the provider supports it.

Direct delivery remains gated on two successful unattended calls to a submission-only proof of concept, including authentication and retry/deduplication evidence. Only then should a relay be designed with revocable per-user credentials, HTTPS retrieval and retention controls. A closed desktop app cannot itself retrieve reports; a future relay would hold them for the next connection. Optional API-based monitoring and spending controls remain future work.

## Implementation and validation

`FeedImporters` adapts folder/text/RSS inputs into `FeedReport` batches. `FeedArchive` commits reports, entries and source status into a separate SQLite database. `TimerModel` applies background results on the UI thread, discarding results from an obsolete source configuration. Existing Feed presentation, Notes actions, account filtering and dashboard strip consume the same `FeedEntry` model.

Automated tests use synthetic files and XML: persistence and edits, original-report history, duplicate IDs, offline folders, file limits/locks, RSS/Atom dates and links, and prohibited XML/link content. They do not establish that OneDrive, Google Drive, ChatGPT or Claude can complete an unattended account-specific workflow.

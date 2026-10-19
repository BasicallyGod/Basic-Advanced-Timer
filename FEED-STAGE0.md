# Stage 0: does a scheduled assistant task write a report unattended?

One question decides whether the Feed can rely on ChatGPT and Claude scheduled tasks: **can a scheduled run save a file into a folder the Timer reads, with nobody clicking approve?** Run this once per provider and destination, and record the result in the sheet at the bottom. Use synthetic or harmless topics only.

Nothing here needs a code change. The Timer already reads `.md` and `.txt` files from a folder source (Settings → Feed → Add source → Local or synchronised folder).

## Before you start

1. Pick a destination folder that syncs to your computer (OneDrive, Google Drive, Dropbox) or a plain local folder. Make it available offline.
2. In the Timer, add it as a folder source, name it after the test (for example "Stage 0 Claude"), and give it a default account.
3. Note what the provider can reach. The documented behaviour differs between desktop and cloud runs, so the first thing to learn is which of these your account offers:
   - a scheduled task that can write to a local folder (the app and computer must stay on),
   - a scheduled task with a cloud-storage connector (Drive, OneDrive) that can create files,
   - neither (then this provider only works through paste import).

## The task to schedule

Paste one of these as the scheduled task's instructions. Replace only `DESTINATION` with the folder or location the task can write to (for example "my OneDrive folder Timer-Feed"). The timestamp in the file name stops a run overwriting the one before. The date and time in the title let you tell the runs apart.

**Claude**

```text
Write a short test report titled "Stage 0 test Claude" with today's date and the current time in the title.

Include two items. Give each its own heading, a "Date:" line with today's date, an "ID:" line that includes the date and time of this run (stage0-claude-YYYYMMDDHHMM-1 and stage0-claude-YYYYMMDDHHMM-2), one sentence of text, and one link to https://example.com.

Save the report as a new .md file named feed-stage0-claude-YYYY-MM-DD-HHMM.md (today's date and the current time) in DESTINATION. Never overwrite an earlier file. When it is saved, reply with only the file name. If you cannot save the file, say so and do not ask me anything.
```

**ChatGPT**

```text
Write a short test report titled "Stage 0 test ChatGPT" with today's date and the current time in the title.

Include two items. Give each its own heading, a "Date:" line with today's date, an "ID:" line that includes the date and time of this run (stage0-chatgpt-YYYYMMDDHHMM-1 and stage0-chatgpt-YYYYMMDDHHMM-2), one sentence of text, and one link to https://example.com.

Save the report as a new .md file named feed-stage0-chatgpt-YYYY-MM-DD-HHMM.md (today's date and the current time) in DESTINATION. Never overwrite an earlier file. When it is saved, reply with only the file name. If you cannot save the file, say so and do not ask me anything.
```

Schedule it to run a few minutes from now, then again at least an hour later, so run two happens with nobody at the keyboard. Do not open the provider's app during a run.

The IDs carry the run's date and time on purpose. The Feed treats a repeated ID within a source as the same item revised, so a second run that reuses an ID updates the earlier entry instead of adding a new one, which would make check 3 fail for the wrong reason.

The last sentence of each prompt matters: a run that cannot save should report that plainly instead of waiting for you. Copy that message into the results sheet.

## Checks

Tick each one only if it happened **without you clicking approve or copying anything**.

1. A new file reached the folder on its own.
2. Both entries appear in the Feed with the right date, source name, link and default account.
3. The second scheduled run arrived on its own, added two new entries and left run one's read/done marks alone. Pressing Refresh does not duplicate anything.
4. Close the Timer, let a run happen, reopen it: the new entries appear. Then disconnect or pause the folder: imported reports stay readable.

A run that asks for approval, or only works while you are watching, counts as a fail for that check. Write down the exact wording of any prompt it showed.

## Results sheet

| Provider | Destination | Scheduled run type (desktop, cloud) | Check 1 | Check 2 | Check 3 | Check 4 | Approval prompts or errors | Notes |
|---|---|---|---|---|---|---|---|---|
| Claude | | | | | | | | |
| ChatGPT | | | | | | | | |

## What the results decide

- **Checks 1 to 4 pass:** write the short setup guide for that provider and destination. Direct delivery (Stage 4) can then be prototyped, because unattended runs are proven to work for it.
- **Check 1 fails everywhere:** do not build a relay. Keep paste import and the folder source, and revisit when the provider supports unattended file creation or tool calls.
- **Checks pass only on desktop with the computer on:** document that limit in the guide. Stage 4 is then only worth building if a hosted tool call (check below) also works.

## Optional second test: unattended tool calls (for Stage 4)

Only if the file test shows file creation is unreliable. Schedule a task that calls one harmless connected tool or custom connector (for example, one that adds a line to a test document) and see whether the run completes with no approval. A relay would need exactly this, so a pass here is the evidence Stage 4 requires: two unattended calls, with authentication and retry behaviour noted.

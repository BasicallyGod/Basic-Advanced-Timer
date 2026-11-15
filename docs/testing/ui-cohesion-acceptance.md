# UI cohesion — native acceptance (Package F)

## Candidate and evidence

The baseline is Experimental **0.1.824**, merge `866404db65b9e2175cc71354ea87ea791b7f34bb` (PR 262). Windows workflow [43750162229](https://github.com/BasicallyGod/Basic-Advanced-Timer/actions/runs/43750162229) passed 960 tests, with no failures or skips, and its published native startup/view-switch and isolated demo checks. Artifact 14176112751 was downloaded for this pass. About confirms `0.1.824 (866404d) — Experimental`.

The machine runs Windows 11 Home, build 26200, on a 3840 × 2560 display. Original Windows scaling was 125%. The pass selected 100%, 150% and 200% through Windows Settings, then visually confirmed restoration to 125%. The principal dashboard was approximately 1280 × 800 logical pixels. These checks do not represent a small 1080p monitor at 200% or mixed-monitor DPI behaviour.

Core/Storage remain 0.10.1, Avalonia 11.3.16, .NET 8. The baseline published main DLL SHA256 is `A4BF9FBAFC62DF6CE108AAE0BAAAF9439BE858DF6F33F8E254135BD86547D634`.

All data is disposable: a new explicit `--data-dir` for setup/recovery and a separate stress fixture. `--test-build` leaves startup registration alone. No real timer data or Store installation was changed. Fixture triggers and direct SQL belong only to verification tooling, never application code or a migration.

## Native outcomes

| Area | Observed scope | Outcome |
| --- | --- | --- |
| Setup | Escape, name validation/correction, all four steps, Enter progression, current transport illustration, reopening | Works; final action's automation name was stale, addressed below |
| Ordinary note | Composer Enter; lower padded row hit area; Details dirty text; open same draft inline on Notes; explicit Save | Draft retained and exact saved text confirmed after shutdown |
| Write recovery | Fixture-only failed insert; negative ID; newer text while queued; corrected Save; automatic retry | Editor and focus retained, ID remapped from -1 to stored ID 2, status became Saved; database confirmed text after shutdown |
| Overnight meeting | 23:30–00:15, native creation at 200% | Row displays +1 day; database stores end offset `1.00:15` |
| Picker focus | Pointer-open reminder; initial strip focus; Tab; Escape; meeting calendar at 200% | Tab stays inside reminder, Escape returns to invoking bell/day button; time semantics gap addressed below |
| Materials | Light/Dark × Phosphor/E-ink, native Settings and sidebar; Light E-ink Details/composer/calendar; Phosphor dense timeline and reports | Material distinctions retained; no major clipping in inspected settled views |
| Pill | Full at original scale; Compact E-ink at 125/100%; Tall E-ink at 150/200%; Tall Phosphor at 125% | Keys and lower readout contained; Compact Alt+A opens account choice and Escape dismisses it |
| Tall direct choices | Lower-display right-click, E-ink mode menu, choose Week | Strip/Next/Week/Both present, Bat excluded; read-only Week value exposed |
| Transport | Phosphor start, break, resume, pause in disposable fixture | Action names track state; recording green, break amber; Tab gives visible focus and Enter opens dashboard |
| Dense timeline | 24 overlapping one-minute blocks, four per stack; reverse Tab route and Enter | Named list items reached, selected stack opens Details, focus restored to stack target |
| Week | 23 accounts including hidden/historical-style and long legacy identity; trace selection, Timesheet switch, Clear trace | Focus retained; trace carries across mode; Clear returns focus to account; overlap flags visible for deliberately overlapping data |
| Timesheet | Native account/date/value cells, including zero cells and Sunday allocation | Context and read-only values exposed; zero drawing uses dashes while semantic value is 00:00 |
| Large Notes | 20,303 records, 20,000 completed; All/Done filters and native wheel scrolling | Completed history shows about 11–12 realised row controls in captured trees, rather than thousands |
| Virtualised draft | Edit completed row, scroll it out, verify editor absent, scroll back, Save | Exact draft returns; saved text and completed state confirmed in database after shutdown |
| Persistence | Graceful fresh-fixture close/reopen; later abrupt stress-fixture termination with no running recording | Saved notes/meeting/settings retained; SQLite integrity check reports ok |

The stress fixture also contained 5,025 ledger entries and 23 accounts before transport checks. History and overlaps were deliberately synthetic; their totals are not a realistic working week. A full current-schema fixture was seeded while the app was closed. An initial seed with assumed account IDs failed its transaction and was corrected to use actual account identities before observations began.

Live DPI changes sometimes invalidated computer-use coordinate mappings. Rehydrating the returned window and checking a fresh screenshot resolved those tool errors. An indexed lower-display click initially opened the parent menu; a positional check opened the intended mode menu. That was not recorded as an application defect.

## Confirmed findings and corrections

### F-01: spoken action names could lag behind visible captions

The final setup action visibly said **Open timer**, but Windows UI Automation still named it **Next**. Frame timing similarly retained **Start frame timing** after activation. `Ui.DialogButton` set a permanent explicit name when constructed.

Dialog buttons now update their generated automation name when their text caption changes. Replacing text with an icon/text panel retains the last meaningful name. A caller-supplied descriptive name takes precedence. Regression coverage walks the actual setup to its last step and verifies these boundaries.

### F-02: reminder time was usable with keys but missing from automation

The original focus defect is fixed: native pointer-open visibly focuses the time strip, Tab moves within the popup, and Escape closes it and returns Windows focus to the bell. However, the time strip had no automation peer and was absent from the native tree, even though its day buttons were present. Keyboard usability alone did not resolve its semantic exposure.

The existing strip now exposes an editable `HH:mm` value and a commit action. Its name supplies the date and time; Enter/Invoke commits. Changing the automation value previews the pin, matching the keyboard's explicit commit. The selected working day determines the date, including overnight day starts. Invalid, past and disabled edits are refused, and commit revalidates the current clock. Exact minutes, including 23:59, remain representable. The existing pointer and keyboard five-minute movements are unchanged.

Tests exercise midnight and 18:00 day starts, exact minutes, invalid input, disabled state and a pin that becomes past before commit. Actual Narrator speech still needs listening verification.

### F-03: an unopened queued creation lacked a persistent row badge

A failed creation showed an error toast, but its row gained **Retrying** only after opening an editor created a shared draft. That could make the pending write unclear once the toast disappeared.

Row badges now also read the note model's persistence state and observe note changes. This does not acquire a draft or rebuild the Notes list merely to display a badge. Dirty edits retain **Draft** precedence. Tests cover unopened note and meeting insert failures, an unopened existing note update failure, and clearing the badge after successful retry.

These changes do not alter storage schema, recording policy, shared package pins, display geometry or the approved material renderers.

## Performance evidence and limits

A passive report from the stress fixture measured approximately 975 ms to pill opened and 4,011 ms to dashboard-prepared callback. Startup storage load was 31.4 ms; model construction 38.3 ms. Account refresh averaged 3.9 ms across 73 calls, worst 14.0 ms. Save averaged 52.2 ms across eight calls, worst 206.2 ms. These numbers are a single-machine sample, not thresholds or a before/after benchmark.

The sampled expansion recorded a 32.8 ms worst native callback gap and 3.0 ms worst dispatcher probe lateness. Animation update itself peaked at 0.5 ms. Preparation cost 109.1 ms, including a 49.6 ms dashboard capture; total transition including preparation was 466.5 ms. This sample did not reproduce the previously reported 80–110 ms delay cluster. It does not establish that intermittent stalls are fixed.

Continuous timing is a separate diagnostic pass. Screenshot capture, UI Automation and concurrent local test/build work can disturb its measurements; they must be disclosed when interpreting that report. Neither the native callback monitor nor passive operation timings measures frames presented by the GPU.

## Remaining acceptance and release gates

- Reduced motion: Windows Animation effects was originally On. Automatic approval review refused the Off toggle pending explicit permission. It remains unchanged until permission and the check are completed.
- Windows text-size variation: explicit temporary-change permission requested; no changed-size pass claimed yet.
- The scale pass is sampled native coverage, not every theme × pill × page × scale permutation or a cold launch at every scale. Smaller physical displays and mixed-monitor movement remain separate checks.
- Native minimise/tray return, Store-installed startup registration, export destination workflow and update/upgrade handling are not established by this pass. Existing automated lifecycle/export contracts and CI native smoke results are supporting evidence only.
- Actual Narrator speech needs listening verification. Windows UI Automation observations are not a passed speech test.
- A first-time participant must still assess idle/recording/break comprehension and saved/unsaved understanding. The implementer's own successful navigation is not an independent comprehension sample.
- Promotion to Main additionally requires the installed MSIX release gate in `BRANCHES.md`: installation/update, persistence, tray/startup and export. No Main promotion or Store acceptance is claimed here.

Final candidate test and native recheck results are recorded after the finding fixes are validated.

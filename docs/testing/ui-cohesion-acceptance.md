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
| Portable export | Native Export CSV files on the stress fixture, 9–15 November | Both files written; 23 account totals and all 161 account/day cells reconcile with detail hours, including zeros |

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

### F-04: Windows larger-text preference — corrected before merge

With explicit permission, Windows Text size was changed from 100% to exactly 150% and applied. Windows Settings and other desktop text enlarged. Ordinary Timer Settings labels retained their original size. A cold launch of the fixed candidate into first-run setup also retained the original text sizes. Therefore this is **not** a passed larger-text accessibility check, even though the inspected layouts did not clip. Text size was restored to 100% afterwards.

This is separate from DPI scaling. `TextSizing` now reads the Windows per-user text preference at startup and observes `WM_SETTINGCHANGE`, with activation as a fallback. Registry access is read-only; there is no polling, new package, preference write or application zoom. The supported Windows range is 100–225%. Other platforms retain the existing typography.

The shared typography policy scales each control's own design font once. Inherited and template-bound fonts follow their owner. Default-sized explicit text is registered on load as well as on property changes. Text-bearing fixed fields/buttons gain width and line height; icon-only keys retain their geometry. Registrations are weak, dead entries are pruned, and layout callbacks subscribe only while attached. Copying a label's font for linked note text uses its unscaled design size. Live adaptation updates existing controls rather than recreating pages, draft leases or focused editors.

The pill, sidebar instrument face, timeline instruments and Settings display illustration explicitly retain their existing geometry/material rendering. This policy does not claim that every custom-drawn chart label becomes larger. The ordinary workspace typography, dialogs, menus, inputs and calendar labels follow the preference.

Layouts now reserve space for the larger text: Settings navigation widens; theme/option groups reflow; settings labels and account summaries wrap; Day and time stacks its groups and shift fields; account-editor columns and dialog cards expand within the window; onboarding scrolls within a bounded card; Notes filters wrap with the account filter below; header navigation/date/search and timeline totals use separate rows; editor options/footer wrap; calendar and reminder cards expand. At 100%, the existing header height/gear position remains stable.

Automated checks cover inherited/template fonts, cold creation, later font changes, restoration, focused unfinished drafts and selection, detached layout subscriptions, fixed instrument geometry, all Settings sections, account-column separation, onboarding bounds, calendar cards and header-key position at 100/150/225%. Look renders cover Light/Dark × Phosphor/E-ink at 150/225%. These are layout/render checks, not a claim of a complete native accessibility matrix.

The native follow-up applied exactly 150% through Windows Settings and independently confirmed the registry value. The already-open Settings page visibly enlarged and reflowed; a cold launch picked up the preference; Phosphor and E-ink instrument styling remained intact. The cold dashboard header, timeline totals and Details editor were inspected. An unsaved Details draft remained intact and visibly Draft through restoration to 100%; Cancel returned the original saved note. Windows Text size was restored and observed at 100%, and the disposable app/Settings windows were closed. Native 225%, smaller physical monitors, mixed-monitor transitions and actual Narrator speech remain outside this sample.

## Performance evidence and limits

A passive report from the stress fixture measured approximately 975 ms to pill opened and 4,011 ms to dashboard-prepared callback. Startup storage load was 31.4 ms; model construction 38.3 ms. Account refresh averaged 3.9 ms across 73 calls, worst 14.0 ms. Save averaged 52.2 ms across eight calls, worst 206.2 ms. These numbers are a single-machine sample, not thresholds or a before/after benchmark.

The sampled expansion recorded a 32.8 ms worst native callback gap and 3.0 ms worst dispatcher probe lateness. Animation update itself peaked at 0.5 ms. Preparation cost 109.1 ms, including a 49.6 ms dashboard capture; total transition including preparation was 466.5 ms. This sample did not reproduce the previously reported 80–110 ms delay cluster. It does not establish that intermittent stalls are fixed.

Continuous timing was a separate diagnostic pass. Its retained Notes sample reported 75 callback fps, p95 30.4 ms and worst 75.4 ms; Timeline reported 48 callback fps, p95 31.4 ms and worst 84.3 ms. Local builds/tests and computer-use captures overlapped that collection. Those disturbances prevent treating it as an isolated performance baseline or attributing the peaks to application rendering. Timing was stopped afterwards. Neither the native callback monitor nor passive operation timings measures frames presented by the GPU.

## Remaining acceptance and release gates

- Reduced motion: after explicit permission, Windows Animation effects was set Off. A read-only call to the same client-area animation API used by the app confirmed disabled animations. Live and cold pill observations showed stationary account text; dashboard/picker navigation remained usable. This is sampled behaviour, not a presented-frame measurement of every effect. Original On restoration is confirmed in the final verification record.
- Windows text-size variation: F-04's follow-up now adapts ordinary typography and reflows layouts. Native live/cold 150% and automated 150/225% checks are described above; this is sampled coverage.
- The scale pass is sampled native coverage, not every theme × pill × page × scale permutation or a cold launch at every scale. Smaller physical displays and mixed-monitor movement remain separate checks.
- Native minimise/tray return, Store-installed startup registration and update/upgrade handling are not established by this pass. Portable export was exercised above; that does not establish the installed Store workflow. Existing automated lifecycle/export contracts and CI native smoke results are supporting evidence only.
- Actual Narrator speech needs listening verification. Windows UI Automation observations are not a passed speech test.
- A first-time participant must still assess idle/recording/break comprehension and saved/unsaved understanding. The implementer's own successful navigation is not an independent comprehension sample.
- Promotion to Main additionally requires the installed MSIX release gate in `BRANCHES.md`: installation/update, persistence, tray/startup and export. No Main promotion or Store acceptance is claimed here.

## Earlier fixed-candidate validation (0.1.826)

The correction code is commit `670d3edaeb312c80d9d299d15086f73cc09ebedf`, PR 263. Feature workflow [43755367982](https://github.com/BasicallyGod/Basic-Advanced-Timer/actions/runs/43755367982) published **0.1.826**, artifact 14177374025. It passed all 967 tests, with zero failures or skips, and both native smoke modes. The separate PR workflow passed its shell checks and packaged launcher checks. Locally, all 967 tests passed before the final lookup-reuse adjustment; 97 affected tests passed after it.

The downloaded fixed executable was checked natively:

- Setup's final action exposes Open timer, matching its visible caption.
- The reminder strip appears as an editable time value with date context in Windows UI Automation. SetValue to 22:17 previews that exact minute; Enter commits it and returns to the composer. Creating the note stores `at_time = 22:17` in the fixture database.
- An inserted note rejected by the fixture trigger shows Retrying before any editor is opened, while Details remains empty. The persistent badge is separate from the failure toast. Quit refuses to close while the note remains queued, preserving it for correction.
- A cold launch under Animation effects Off remains usable. F-04 was confirmed on this fixed executable's cold setup, not inferred solely from the baseline.
- The corrected failed creation subsequently retried successfully, the badge cleared, and normal Quit succeeded. A database read confirmed both the exact-minute reminder and corrected note; integrity check returned ok. Fixture rejection triggers were removed while the app was closed.

All temporary Windows changes were restored and observed: display scale 125%, Text size 100%, Animation effects On. A read-only client-area animation API check also returned enabled after restoration. The disposable timer processes and the Settings window opened for this session were closed. The downloaded fixed main DLL SHA256 is `97066F12AA38083148F91C901BBA7565D9F4B0EF4D4F8964A37A5BF9A085C52C`.

Documentation-only commits initially recorded these outcomes without changing that correction code. The later F-04 typography implementation extends the same PR before merge; the native larger-text sample above used a local Release build. Its additional render and regression evidence is retained beside the original pass.

The complete local evidence, diagnostic reports, fixture SQL and native notes are in the project review directory `reviews/ui-cohesion-f/`; they are not application data or shipped fixtures.

## Larger-text regression validation

The expanded local suite passed **996 tests, zero failures or skips**. After the final account-filter caption/popup-width adjustment, all 30 affected typography/header tests passed again. This adds 29 typography/layout cases to the earlier 967-test candidate. Thirty-five local preview images cover the materials and expanded workspace/settings/calendar layouts. The native sample used explicit fixture data and left Windows Text size at its original 100%; display scale and animation preferences were not changed during this follow-up.

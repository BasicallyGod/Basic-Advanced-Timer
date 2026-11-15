# Shared record draft foundation

Package C of the UI/UX cohesion work. This provides model/controller infrastructure for existing notes, meetings and individual time blocks. Package D adopts it in the editors together; this package does not change the current visible save-on-blur/explicit-series workflows.

## Ownership and lifetime

`TimerModel.Drafts` lazily creates a `DraftWorkspace`. A view acquires a `DraftLease` for a note/meeting ID or an existing unmerged ledger row. Two views acquiring the same record receive the same `RecordDraft`, identified by a UI-only GUID. Disposing the lease releases the view's ownership; the workspace retains no controls, windows, popup owners or timers.

Dirty drafts and accepted pending writes survive release, navigation, filtering, hiding and selection changes. No arbitrary cache cap evicts work. Clean released records are removed. Once the final pending write is confirmed, a released clean entry is removed too. Cancel resets only the unsubmitted values to the latest accepted snapshot; it does not remove or undo a submitted retry.

This is session state. No new crash-recovery file, database table or storage schema is introduced. The workspace follows the model's single-threaded mutation contract.

## Values, validation and conflict state

`RecordValues` is immutable. Clock text is kept verbatim while editing, including invalid intermediate input. The draft has a baseline accepted snapshot, current values, local revision, submitted revision, field errors and field conflicts. `AllLater` is command scope; changing scope alone does not mutate a meeting or create a record change to save.

Two independent axes describe the result:

- `Phase`: Clean, Draft, Invalid, Conflict, Submitting or Missing.
- `PendingPersistence`: accepted model data still awaits storage confirmation.

A draft can therefore contain newer unsaved typing while its previous accepted revision retries. An acknowledgement clears persistence state, not that newer typing. The accepted command snapshot is captured before model notifications; a reentrant change cannot silently become the baseline of newer text. Duplicate submission while a command is running returns Busy.

Refresh uses a field-by-field comparison against the baseline. Untouched fields follow the model, dirty fields remain local, and a field changed differently on both sides retains its original, local and current values. `Resolve` explicitly keeps the local value or adopts the current value. A recurrence/template change also conflicts with an outstanding “all later” draft, so the command does not silently apply against another recurrence context.

Validation operates on the combined record and returns field-associated errors. It checks required note/meeting words, account availability, editable fields, current reminder time and bounded clock spans. A reminder newly chosen in the future is checked again on Save. Existing overdue reminders may remain while another field changes. Overnight and full-day meetings and fractional-second block boundaries retain existing precision. Archived recurrence rules are preserved; unsupported all-later changes are refused.

Missing/deleted records retain dirty text and refuse Save. They are never recreated implicitly or reassigned to a neighbour. This is deliberate protection for the later editor's missing-record resolution UI.

## Commands and persistence receipts

`Save` validates, applies one existing model operation and reports NoChanges, Saved, PendingPersistence, Invalid, Conflict, Missing, Refused or Busy. Opening an editor, changing values, releasing a lease or changing selection does not submit it. An unchanged record does not create another note revision or ledger undo entry.

The existing NotesModel write queue and TimerModel ledger checkpoint remain authoritative. A successful validation/model return is not itself a storage acknowledgement. Compound meeting receipts include affected later occurrences, deleted occurrences and recurrence-template writes. A visible occurrence reaching storage cannot incorrectly mark its failed later updates as saved. Conversely, an unrelated failed note does not mark a successful plain-note command as unsaved.

Ledger commands distinguish a failed pre-edit checkpoint from a model edit accepted before the final write failed. The former retains the unsubmitted draft; the latter records the accepted revision as pending. The workspace does not introduce a second retry queue.

Ledger persistence has its own `LedgerUnsaved` flag. A startup-setting error does not mark a ledger record unsaved, and clearing that setting's error cannot acknowledge a failed ledger write. The global save warning retains a fallback while ledger persistence remains pending; only a successful ledger checkpoint clears that flag.

`SaveAll` first validates every dirty record. If any is invalid, missing or conflicted, none of the group is submitted. Refused and queued records remain owned. This is the foundation for the grouped quit decision in Package D; the actual quit UI is not installed here.

## Identity and account changes

Temporary note IDs update the draft's storage ID while retaining its GUID, text and submitted revision. Existing note-ID alias resolution remains authoritative. Known ledger edit/drag/undo replacements remap tracked references deliberately. Mapping requires an actual replacement at the known slot with unchanged row count; an existing neighbour or structural deletion is never accepted as the old record. An exact restored row can also be recovered when uniquely identifiable.

Account edits defer draft/model refresh until the related account, note-tag and ledger changes have completed. Renames relink both accepted assignments and unsubmitted assignment intent. Draft words are not rewritten. Deleted assignment targets remain available as invalid text for correction instead of being replaced with another account. Ledger account renames use the existing row replacement machinery.

Unknown or ambiguous structural replacement remains Missing, with dirty data retained. Do not add a heuristic that guesses another record from a similar name or time.

## Package D handoff

Adopt the workspace in block Details, note Details, meeting Details and inline Notes editing as a coherent candidate. Editors should acquire/release leases, update immutable values, show both draft and persistence state, and invoke the shared command on explicit Save/Ctrl+Enter. Navigation must release the view while retaining work. Cancel and closing a picker have different boundaries.

The visible migration still needs shared Save/Cancel footers, first-error focus, conflict/missing-record resolution, list draft indicators, the grouped quit decision and independent Note/Meeting composer drafts. Existing add-line commands remain one-step Add flows. This foundation does not prematurely add any of those controls or change editor semantics in only one surface.

## Verification

Model tests cover shared leases and cleanup, navigation without submission, whole-record save/no-op behaviour, field merging/conflicts, exact validation text retention, stale reminder validation, batch preflight, reentrant commands, temporary IDs, queued acknowledgements, Cancel with pending writes, unrelated failures, later update/removal receipts, recurrence conflict resolution, block drag/undo/redo replacement, deleted-record safety, fractional/full-day spans, pre/post-edit write failures, account relinking and retention of 200 outstanding drafts.

The full desktop suite and native startup checks remain release gates. There is no new editor UI to visually approve in this package; native interaction acceptance follows when Package D adopts the model.

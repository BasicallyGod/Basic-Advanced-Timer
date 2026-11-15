using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class DraftWorkspaceTests
{
    private static readonly DateTime Day = new(2026, 11, 15);
    private static TimerModel Model(TimerStore? store = null, Func<DateTime>? clock = null)
    {
        var engine = new TrackingEngine(clock: clock ?? (() => Day.AddHours(14)));
        var settings = new StoreSettings(new() { new("Account one"), new("Account two") }, false, "Dark", DayOfWeek.Monday, SetupDone: true);
        if (store is not null) { store.Load(engine); store.Save(engine, settings); }
        return new(engine, settings, store);
    }
    private static void Stored(Action<TimerStore, TimerModel> check)
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-draft-" + Guid.NewGuid());
        try { using var store = new TimerStore(folder); check(store, Model(store)); }
        finally { Directory.Delete(folder, true); }
    }
    private static long Note(TimerModel model, string words = "Original")
    { model.Notes.AddNote(words, "Account one", null); return model.Notes.All.Last().Id; }
    private static SessionRow Block(TimerModel model, int hour = 9)
    { Assert.True(model.AddEntry("Account one", Day.AddHours(hour), Day.AddHours(hour + 1), "Original")); return model.Engine.Sessions.Last(); }

    [Fact]
    public void LeasesShareIdentityAndOnlyOutstandingWorkSurvivesNavigation()
    {
        var model = Model(); var id = Note(model);
        var first = model.Drafts.AcquireNote(id); var second = model.Drafts.AcquireNote(id);
        Assert.Same(first.Draft, second.Draft); first.Dispose(); Assert.Equal(1, model.Drafts.TrackedCount);
        second.Dispose(); Assert.Equal(0, model.Drafts.TrackedCount);
        var edited = model.Drafts.AcquireNote(id); var draft = edited.Draft;
        draft.Update(draft.Values with { Words = "Unsubmitted" }); edited.Dispose();
        model.GoToDate(Day.AddDays(-1)); model.Tick(); model.SelectNote(id); model.ClearSelection();
        using var reopened = model.Drafts.AcquireNote(id);
        Assert.Same(draft, reopened.Draft); Assert.Equal("Unsubmitted", draft.Values.Words);
        Assert.Equal("Original", model.Notes.Words(model.Notes.Find(id)!));
        model.Drafts.Cancel(draft); reopened.Dispose(); Assert.Equal(0, model.Drafts.TrackedCount);
    }

    [Fact]
    public void ACombinedNoteCommandAppliesOnlyOnSaveAndNoChangesDoNotResubmit()
    {
        var model = Model(); var id = Note(model); using var lease = model.Drafts.AcquireNote(id); var d = lease.Draft;
        var reminder = Day.AddDays(1).AddHours(9);
        d.Update(d.Values with { Words = "Edited", Account = "Account two", Reminder = reminder, Important = true });
        Assert.False(NotesModel.IsImportant(model.Notes.Find(id)!)); Assert.Null(NotesModel.ReminderOf(model.Notes.Find(id)!));
        Assert.Equal(DraftSaveOutcome.Saved, model.Drafts.Save(d).Outcome);
        var saved = model.Notes.Find(id)!;
        Assert.Equal("Edited", model.Notes.Words(saved)); Assert.Equal("Account two", model.Notes.AccountOf(saved));
        Assert.Equal(reminder, NotesModel.ReminderOf(saved)); Assert.True(NotesModel.IsImportant(saved)); Assert.False(d.IsDirty);
        var revision = model.Notes.Revision;
        Assert.Equal(DraftSaveOutcome.NoChanges, model.Drafts.Save(d).Outcome); Assert.Equal(revision, model.Notes.Revision);
    }

    [Fact]
    public void RefreshMergesUntouchedFieldsAndPreservesBothSidesOfAConflict()
    {
        var model = Model(); var id = Note(model); using var lease = model.Drafts.AcquireNote(id); var d = lease.Draft;
        d.Update(d.Values with { Words = "My words" });
        model.Notes.EditNote(id, "Original", "Account two", Day.AddDays(1));
        Assert.Equal("My words", d.Values.Words); Assert.Equal("Account two", d.Values.Account); Assert.Empty(d.Conflicts);
        model.Notes.EditNote(id, "Other words", "Account two", Day.AddDays(1));
        var conflict = Assert.Single(d.Conflicts); Assert.Equal(DraftField.Words, conflict.Field);
        Assert.Equal("Original", conflict.Original); Assert.Equal("My words", conflict.Yours); Assert.Equal("Other words", conflict.Current);
        Assert.Equal(DraftSaveOutcome.Conflict, model.Drafts.Save(d).Outcome);
        d.Resolve(DraftField.Words, keepMine: true); Assert.Equal(DraftSaveOutcome.Saved, model.Drafts.Save(d).Outcome);
        Assert.Equal("My words", model.Notes.Words(model.Notes.Find(id)!));
        d.Update(d.Values with { Words = "Another draft" }); model.Notes.EditNote(id, "Newest", "Account two", Day.AddDays(1));
        d.Resolve(DraftField.Words, keepMine: false); Assert.Equal("Newest", d.Values.Words); Assert.False(d.IsDirty);
    }

    [Fact]
    public void ValidationRetainsRawTextAndRechecksTheClockAtSubmission()
    {
        var now = Day.AddHours(14); var model = Model(clock: () => now);
        model.Notes.AddMeeting(Day, TimeSpan.FromHours(22), TimeSpan.FromHours(26), "Night", null, RepeatChoice.None);
        using var meeting = model.Drafts.AcquireNote(model.Notes.All.Single().Id); var d = meeting.Draft;
        d.Update(d.Values with { Start = "22:", Words = "Unsubmitted" });
        var refused = model.Drafts.Save(d); Assert.Equal(DraftSaveOutcome.Invalid, refused.Outcome);
        Assert.Contains(refused.Errors, e => e.Field == DraftField.Start); Assert.Equal("22:", d.Values.Start);
        d.Update(d.Values with { Start = "22:00:00" }); Assert.Equal(DraftSaveOutcome.Saved, model.Drafts.Save(d).Outcome);
        Assert.Equal(TimeSpan.FromHours(26), model.Notes.All.Single().End);
        var id = Note(model); using var note = model.Drafts.AcquireNote(id); var n = note.Draft;
        n.Update(n.Values with { Reminder = now.AddMinutes(1) }); now = now.AddMinutes(2);
        Assert.Contains(model.Drafts.Save(n).Errors, e => e.Field == DraftField.Reminder);
        Assert.Equal(Day.AddHours(14).AddMinutes(1), n.Values.Reminder);
    }

    [Fact]
    public void GroupSaveValidatesEveryDraftBeforeApplyingAny()
    {
        var model = Model(); var one = Note(model, "One"); var two = Note(model, "Two");
        using var a = model.Drafts.AcquireNote(one); using var b = model.Drafts.AcquireNote(two);
        a.Draft.Update(a.Draft.Values with { Words = "Changed one" }); b.Draft.Update(b.Draft.Values with { Words = " " });
        var result = model.Drafts.SaveAll(); Assert.Contains(result, r => r.Result.Outcome == DraftSaveOutcome.Invalid);
        Assert.Equal("One", model.Notes.Words(model.Notes.Find(one)!)); Assert.True(a.Draft.IsDirty);
        b.Draft.Update(b.Draft.Values with { Words = "Changed two" });
        Assert.All(model.Drafts.SaveAll(), r => Assert.Equal(DraftSaveOutcome.Saved, r.Result.Outcome));
    }

    [Fact]
    public void ReentrantSaveIsBusyAndAnOlderSubmissionCannotEraseNewTyping()
    {
        var model = Model(); var id = Note(model); using var lease = model.Drafts.AcquireNote(id); var d = lease.Draft;
        d.Update(d.Values with { Words = "Submitted" }); var submitted = d.Revision;
        model.Notes.Changed += () =>
        {
            if (model.Notes.Words(model.Notes.Find(id)!) != "Submitted") return;
            d.Update(d.Values with { Words = "Typed during save" });
            Assert.Equal(DraftSaveOutcome.Busy, model.Drafts.Save(d).Outcome);
        };
        Assert.Equal(DraftSaveOutcome.Saved, model.Drafts.Save(d).Outcome);
        Assert.Equal(submitted, d.SubmittedRevision); Assert.True(d.Revision > submitted);
        Assert.Equal("Submitted", d.Accepted.Words); Assert.Equal("Typed during save", d.Values.Words); Assert.True(d.IsDirty);
    }

    [Fact]
    public void TemporaryIdRecoveryKeepsTheSameUnsubmittedDraft()
    {
        Stored((store, model) =>
        {
            store.Database.Execute("CREATE TRIGGER fail_insert BEFORE INSERT ON day_notes BEGIN SELECT RAISE(ABORT, 'test'); END");
            var id = Note(model); Assert.True(id < 0);
            using var lease = model.Drafts.AcquireNote(id); var d = lease.Draft; var identity = d.Identity;
            d.Update(d.Values with { Words = "Still a draft" }); Assert.True(d.PendingPersistence);
            store.Database.Execute("DROP TRIGGER fail_insert"); model.Notes.RetryUnsaved();
            Assert.True(d.NoteId > 0); Assert.Equal(identity, d.Identity); Assert.False(d.PendingPersistence); Assert.True(d.IsDirty);
            using var savedIdLease = model.Drafts.AcquireNote(d.NoteId!.Value); Assert.Same(d, savedIdLease.Draft);
            Assert.Equal("Original", store.LoadDayNotes().Single().Text.Replace(" #Accountone", ""));
            Assert.Equal(DraftSaveOutcome.Saved, model.Drafts.Save(d).Outcome);
            Assert.Equal("Still a draft", model.Notes.Words(model.Notes.Find(d.NoteId.Value)!));
        });
    }

    [Fact]
    public void RetryAcknowledgementClearsOnlyPersistenceNotTheNewerDraft()
    {
        Stored((store, model) =>
        {
            var id = Note(model); using var lease = model.Drafts.AcquireNote(id); var d = lease.Draft;
            store.Database.Execute("CREATE TRIGGER fail_update BEFORE UPDATE ON day_notes BEGIN SELECT RAISE(ABORT, 'test'); END");
            d.Update(d.Values with { Words = "Queued" }); var revision = d.Revision;
            Assert.Equal(DraftSaveOutcome.PendingPersistence, model.Drafts.Save(d).Outcome); Assert.False(d.IsDirty);
            d.Update(d.Values with { Words = "Newer draft" });
            store.Database.Execute("DROP TRIGGER fail_update"); model.Notes.RetryUnsaved();
            Assert.False(d.PendingPersistence); Assert.True(d.IsDirty); Assert.Equal(revision, d.SubmittedRevision);
            Assert.Equal("Queued", d.Accepted.Words); Assert.Equal("Newer draft", d.Values.Words);
            Assert.Equal(DraftSaveOutcome.Saved, model.Drafts.Save(d).Outcome);
        });
    }

    [Fact]
    public void CancelCannotDiscardAnAlreadySubmittedRetry()
    {
        Stored((store, model) =>
        {
            var id = Note(model); var lease = model.Drafts.AcquireNote(id); var d = lease.Draft;
            store.Database.Execute("CREATE TRIGGER fail_update BEFORE UPDATE ON day_notes BEGIN SELECT RAISE(ABORT, 'test'); END");
            d.Update(d.Values with { Words = "Queued" }); model.Drafts.Save(d);
            d.Update(d.Values with { Words = "Discard this part" }); model.Drafts.Cancel(d); lease.Dispose();
            Assert.Equal("Queued", d.Values.Words); Assert.True(d.PendingPersistence); Assert.False(d.IsDirty); Assert.Equal(1, model.Drafts.TrackedCount);
            store.Database.Execute("DROP TRIGGER fail_update"); model.Notes.RetryUnsaved();
            Assert.Equal(0, model.Drafts.TrackedCount); Assert.Contains("Queued", store.LoadDayNotes().Single().Text);
        });
    }

    [Fact]
    public void AnUnrelatedFailedNoteDoesNotMakeASuccessfulCommandLookUnsaved()
    {
        Stored((store, model) =>
        {
            var bad = Note(model, "Bad"); var good = Note(model, "Good");
            store.Database.Execute($"CREATE TRIGGER fail_one BEFORE UPDATE ON day_notes WHEN OLD.id = {bad} BEGIN SELECT RAISE(ABORT, 'test'); END");
            model.Notes.EditNote(bad, "Queued elsewhere", null, null); Assert.True(model.Notes.Unsaved);
            using var lease = model.Drafts.AcquireNote(good); var d = lease.Draft; d.Update(d.Values with { Words = "Saved here" });
            Assert.Equal(DraftSaveOutcome.Saved, model.Drafts.Save(d).Outcome); Assert.False(d.PendingPersistence);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompoundMeetingReceiptIncludesLaterWritesAndRemovals(bool removeLater)
    {
        Stored((store, model) =>
        {
            model.Notes.AddMeeting(Day.AddDays(-1), TimeSpan.FromHours(9), TimeSpan.FromHours(10), "Original", null, RepeatChoice.Day);
            var selected = model.Notes.MeetingsOn(Day).Single();
            using var lease = model.Drafts.AcquireNote(selected.Id); var d = lease.Draft; d.AllLater = true;
            var operation = removeLater ? "DELETE" : "UPDATE";
            store.Database.Execute($"CREATE TRIGGER fail_later BEFORE {operation} ON day_notes WHEN OLD.id <> {selected.Id} BEGIN SELECT RAISE(ABORT, 'test'); END");
            d.Update(d.Values with { Words = "Updated", Repeat = removeLater ? RepeatChoice.None : RepeatChoice.Day });
            Assert.Equal(DraftSaveOutcome.PendingPersistence, model.Drafts.Save(d).Outcome); Assert.True(d.PendingPersistence);
            Assert.Equal("Original", model.Notes.Words(model.Notes.MeetingsOn(Day.AddDays(-1)).Single()));
            store.Database.Execute("DROP TRIGGER fail_later"); model.Notes.RetryUnsaved(); Assert.False(d.PendingPersistence);
        });
    }

    [Fact]
    public void ExternalRecurrenceChangesRequireAnExplicitResolutionForAllLater()
    {
        var model = Model(); model.Notes.AddMeeting(Day, TimeSpan.FromHours(9), TimeSpan.FromHours(10), "Original", null, RepeatChoice.Day);
        var note = model.Notes.MeetingsOn(Day).Single(); using var lease = model.Drafts.AcquireNote(note.Id); var d = lease.Draft;
        d.AllLater = true; d.Update(d.Values with { Words = "My future change" });
        model.Notes.EditMeeting(note.Id, "Original", null, Day, note.At!.Value, note.End!.Value, RepeatChoice.Week, true);
        Assert.Contains(d.Conflicts, c => c.Field == DraftField.Repeat); Assert.Equal(DraftSaveOutcome.Conflict, model.Drafts.Save(d).Outcome);
        d.Resolve(DraftField.Repeat, keepMine: false); Assert.Equal(DraftSaveOutcome.Saved, model.Drafts.Save(d).Outcome);
        Assert.Equal(RepeatChoice.Week, model.Notes.RepeatOf(model.Notes.Find(note.Id)!));
    }

    [Fact]
    public void ABlockDraftFollowsItsReplacedRowAndSavesOneCombinedEdit()
    {
        var model = Model(); var row = Block(model); using var lease = model.Drafts.AcquireBlock(row); var d = lease.Draft;
        d.Update(d.Values with { Words = "Draft note" });
        Assert.True(model.MoveEntry(row, Day.AddHours(10), Day.AddHours(11)));
        var current = model.Engine.Sessions.Single(); Assert.NotSame(row, current);
        Assert.Equal("10:00:00", d.Values.Start); Assert.Equal("Draft note", d.Values.Words); Assert.Empty(d.Conflicts);
        using var second = model.Drafts.AcquireBlock(current); Assert.Same(d, second.Draft);
        d.Update(d.Values with { Account = "Account two", End = "11:30" });
        Assert.Equal(DraftSaveOutcome.Saved, model.Drafts.Save(d).Outcome);
        current = model.Engine.Sessions.Single(); Assert.Equal("Account two", current.Account); Assert.Equal(90, current.Duration.TotalMinutes);
        Assert.Equal("Draft note", model.Editor.Note(current));
        var undo = model.Editor.UndoState; Assert.Equal(DraftSaveOutcome.NoChanges, model.Drafts.Save(d).Outcome); Assert.Same(undo, model.Editor.UndoState);
        Assert.True(model.UndoEdit()); Assert.Equal("Original", model.Editor.Note(model.Engine.Sessions.Single()));
    }

    [Fact]
    public void DeletedBlocksNeverRetargetADraftToTheNeighbourAtTheSameIndex()
    {
        var model = Model(); var row = Block(model); var other = Block(model, 11);
        var lease = model.Drafts.AcquireBlock(row); var d = lease.Draft; d.Update(d.Values with { Words = "Retain me" });
        model.SelectBlock(new[] { row }, false); Assert.True(model.DeleteSelection());
        Assert.Equal(DraftSaveOutcome.Missing, model.Drafts.Save(d).Outcome); Assert.Equal("Retain me", d.Values.Words);
        Assert.Same(other, model.Engine.Sessions.Single()); Assert.Equal("Original", model.Editor.Note(other));
        model.Drafts.Cancel(d); lease.Dispose(); Assert.Equal(0, model.Drafts.TrackedCount);
    }

    [Fact]
    public void ClosedStorageRefusesSubmissionAndRetainsTheDraft()
    {
        var model = Model(); using var lease = model.Drafts.AcquireNote(Note(model)); var d = lease.Draft;
        d.Update(d.Values with { Words = "Keep me" }); model.MarkClosed();
        Assert.Equal(DraftSaveOutcome.Refused, model.Drafts.Save(d).Outcome); Assert.True(d.IsDirty);
    }

    [Fact]
    public void ReentrantExternalChangeConflictsWithNewTypingRatherThanBecomingItsBaseSilently()
    {
        var model = Model(); var id = Note(model); using var lease = model.Drafts.AcquireNote(id); var d = lease.Draft;
        d.Update(d.Values with { Words = "Submitted" }); var entered = false;
        model.Notes.Changed += () =>
        {
            if (entered) return; entered = true;
            d.Update(d.Values with { Words = "New typing" });
            model.Notes.EditNote(id, "External change", "Account one", null);
        };
        model.Drafts.Save(d);
        Assert.Equal("New typing", d.Values.Words); Assert.Equal("External change", d.Accepted.Words);
        Assert.Contains(d.Conflicts, c => c.Field == DraftField.Words && Equals(c.Original, "Submitted"));
    }

    [Fact]
    public void BlockDraftSurvivesDragUndoAndRedoWithTheSameIdentity()
    {
        var model = Model(); var row = Block(model); using var lease = model.Drafts.AcquireBlock(row); var d = lease.Draft;
        d.Update(d.Values with { Words = "Unsubmitted" });
        Assert.True(model.DragEntry(row, Day.AddHours(10), Day.AddHours(11), false));
        Assert.False(d.Missing); Assert.Equal("10:00:00", d.Values.Start);
        Assert.True(model.UndoEdit()); Assert.False(d.Missing); Assert.Equal("09:00:00", d.Values.Start);
        Assert.True(model.RedoEdit()); Assert.False(d.Missing); Assert.Equal("10:00:00", d.Values.Start);
        using var again = model.Drafts.AcquireBlock(model.Engine.Sessions.Single()); Assert.Same(d, again.Draft);
        Assert.Equal("Unsubmitted", d.Values.Words);
    }

    [Fact]
    public void FullDayMeetingAndFractionalBlockTimesSurviveAnUnchangedTimeSave()
    {
        var model = Model(); model.Notes.AddMeeting(Day, TimeSpan.FromHours(9), TimeSpan.FromHours(33), "Full day", null, RepeatChoice.None);
        using var meeting = model.Drafts.AcquireNote(model.Notes.All.Single().Id); var m = meeting.Draft;
        m.Update(m.Values with { Words = "New name" }); Assert.Equal(DraftSaveOutcome.Saved, model.Drafts.Save(m).Outcome);
        Assert.Equal(TimeSpan.FromHours(33), model.Notes.All.Single().End);
        var start = Day.AddHours(10); var end = start.AddMinutes(7.51);
        Assert.True(model.AddEntry("Account one", start, end, "Original"));
        using var block = model.Drafts.AcquireBlock(model.Engine.Sessions.Single()); var b = block.Draft;
        b.Update(b.Values with { Words = "New block note" }); Assert.Equal(DraftSaveOutcome.Saved, model.Drafts.Save(b).Outcome);
        Assert.Equal(7.51, model.Engine.Sessions.Single().Duration.TotalMinutes, 6);
    }

    [Fact]
    public void ABlockWriteFailureKeepsAnAcceptedRevisionSeparateFromTheNextDraft()
    {
        Stored((store, model) =>
        {
            var row = Block(model); using var lease = model.Drafts.AcquireBlock(row); var d = lease.Draft;
            store.Database.Execute("CREATE TRIGGER fail_changed BEFORE INSERT ON entries WHEN NEW.seconds = 5400 BEGIN SELECT RAISE(ABORT, 'test'); END");
            d.Update(d.Values with { Words = "Queued block", End = "10:30" });
            Assert.Equal(DraftSaveOutcome.PendingPersistence, model.Drafts.Save(d).Outcome);
            Assert.True(d.PendingPersistence); Assert.False(d.IsDirty);
            model.SetAutoStart(false); // This clears its own error; it is not a ledger acknowledgement.
            Assert.True(d.PendingPersistence); Assert.NotNull(model.SaveError);
            d.Update(d.Values with { Words = "New block draft" });
            store.Database.Execute("DROP TRIGGER fail_changed"); Assert.True(model.Save());
            Assert.False(d.PendingPersistence); Assert.True(d.IsDirty); Assert.Equal("New block draft", d.Values.Words);
            Assert.Equal("Queued block", d.Accepted.Words);
        });
    }

    [Fact]
    public void APreflightStorageFailureNeverClaimsTheNewBlockDraftWasAccepted()
    {
        Stored((store, model) =>
        {
            var row = Block(model); using var lease = model.Drafts.AcquireBlock(row); var d = lease.Draft;
            // A pending recorded row makes the pre-edit checkpoint require an INSERT.
            model.Engine.Sessions.Add(new SessionRow(Day.ToString("yyyy-MM-dd"), "Account one", "12:00:00", "added", "10"));
            StorageFaults.Block(store); d.Update(d.Values with { Words = "Not submitted" });
            Assert.Equal(DraftSaveOutcome.Refused, model.Drafts.Save(d).Outcome);
            Assert.True(d.IsDirty); Assert.Null(d.SubmittedRevision); Assert.Equal("Original", model.Editor.Note(row));
        });
    }

    [Fact]
    public void DeletedNotesRetainTheirDraftAndNeverGetRecreatedBySavingIt()
    {
        var model = Model(); var id = Note(model); using var lease = model.Drafts.AcquireNote(id); var d = lease.Draft;
        d.Update(d.Values with { Words = "Retained" }); model.Notes.Remove(id);
        Assert.Equal(DraftSaveOutcome.Missing, model.Drafts.Save(d).Outcome); Assert.Empty(model.Notes.All);
        Assert.Equal("Retained", d.Values.Words);
    }

    [Fact]
    public void KeepMineOnAChangedRecurrencePreservesTheDraftsActualChoice()
    {
        var model = Model(); model.Notes.AddMeeting(Day, TimeSpan.FromHours(9), TimeSpan.FromHours(10), "Original", null, RepeatChoice.Day);
        var note = model.Notes.MeetingsOn(Day).Single(); using var lease = model.Drafts.AcquireNote(note.Id); var d = lease.Draft;
        d.AllLater = true; d.Update(d.Values with { Words = "Future edit" });
        model.Notes.EditMeeting(note.Id, "Original", null, Day, note.At!.Value, note.End!.Value, RepeatChoice.Week, true);
        Assert.Equal(RepeatChoice.Day, d.Values.Repeat); Assert.Equal(RepeatChoice.Week, d.Accepted.Repeat);
        d.Resolve(DraftField.Repeat, keepMine: true); Assert.Equal(DraftSaveOutcome.Saved, model.Drafts.Save(d).Outcome);
        Assert.Equal(RepeatChoice.Day, model.Notes.RepeatOf(model.Notes.Find(note.Id)!));
    }

    [Fact]
    public void OutstandingDraftsAreNotEvictedAndCleanReleasedRecordsAreNotCached()
    {
        var model = Model(); var ids = Enumerable.Range(0, 200).Select(i => Note(model, "Note " + i)).ToArray();
        foreach (var id in ids)
        {
            using var lease = model.Drafts.AcquireNote(id);
            lease.Draft.Update(lease.Draft.Values with { Words = "Retained " + id });
        }
        model.Tick(); Assert.Equal(200, model.Drafts.TrackedCount); Assert.Equal(200, model.Drafts.Outstanding.Count);
        foreach (var id in ids)
        {
            using var lease = model.Drafts.AcquireNote(id);
            Assert.Equal("Retained " + id, lease.Draft.Values.Words); model.Drafts.Cancel(lease.Draft);
        }
        Assert.Equal(0, model.Drafts.TrackedCount);
    }

    [Fact]
    public void AccountRenameRelinksAcceptedAndUnsubmittedAssignmentsWithoutAnIntermediateConflict()
    {
        var model = Model(); var id = Note(model); using var note = model.Drafts.AcquireNote(id); var n = note.Draft;
        n.Update(n.Values with { Words = "Unsubmitted words" });
        var row = Block(model); using var block = model.Drafts.AcquireBlock(row); var b = block.Draft;
        b.Update(b.Values with { Words = "Block draft", Account = "Account two" });
        model.SetAccounts(new[] { new AccountSettings("Renamed one"), new AccountSettings("Renamed two") },
            new Dictionary<string, string> { ["Account one"] = "Renamed one", ["Account two"] = "Renamed two" });
        Assert.Equal("Renamed one", n.Accepted.Account); Assert.Equal("Renamed one", n.Values.Account); Assert.Empty(n.Conflicts);
        Assert.Equal("Renamed one", b.Accepted.Account); Assert.Equal("Renamed two", b.Values.Account); Assert.Empty(b.Conflicts); Assert.False(b.Missing);
        Assert.Equal("Unsubmitted words", n.Values.Words); Assert.Equal("Block draft", b.Values.Words);
        Assert.Equal(DraftSaveOutcome.Saved, model.Drafts.Save(n).Outcome); Assert.Equal(DraftSaveOutcome.Saved, model.Drafts.Save(b).Outcome);
        Assert.Equal("Renamed two", model.Engine.Sessions.Single().Account);
    }

    [Fact]
    public void DeletedAccountAssignmentsAreRetainedForCorrectionInsteadOfSavingToAnotherAccount()
    {
        var model = Model(); using var lease = model.Drafts.AcquireNote(Note(model)); var d = lease.Draft;
        d.Update(d.Values with { Account = "Account two" });
        model.SetAccounts(new[] { new AccountSettings("Account one") }, deleted: new[] { "Account two" });
        Assert.Equal("Account two", d.Values.Account);
        Assert.Contains(model.Drafts.Save(d).Errors, e => e.Field == DraftField.Account);
        Assert.Equal("Account one", model.Notes.AccountOf(model.Notes.All.Single()));
    }

    private sealed class RefusedStartup : IAutoStart
    {
        public bool Available => true;
        public bool IsEnabled => false;
        public void Set(bool enabled) => throw new IOException("Synthetic startup failure");
    }
    [Fact]
    public void AStartupSettingErrorIsNotMistakenForAnUnconfirmedLedgerWrite()
    {
        var model = Model(); var row = Block(model); model.AutoStart = new RefusedStartup(); model.SetAutoStart(true);
        Assert.NotNull(model.LastError); using var lease = model.Drafts.AcquireBlock(row);
        Assert.False(lease.Draft.PendingPersistence);
    }

    [Fact]
    public void RecoveryCannotMistakeAnotherCommandsUndoChangeForAnAcceptedDraft()
    {
        Stored((store, model) =>
        {
            var row = Block(model); using var lease = model.Drafts.AcquireBlock(row); var d = lease.Draft;
            d.Update(d.Values with { Words = "Retain this draft" });
            model.Engine.Sessions.Add(new SessionRow(Day.ToString("yyyy-MM-dd"), "Account one", "12:00:00", "added", "10"));
            StorageFaults.Block(store); Assert.False(model.Save()); StorageFaults.Unblock(store);
            var entered = false;
            model.PropertyChanged += (_, _) =>
            {
                if (entered || model.LastError is not null) return; entered = true;
                model.SelectBlock(new[] { row }, false); Assert.True(model.DeleteSelection());
            };
            Assert.Equal(DraftSaveOutcome.Refused, model.Drafts.Save(d).Outcome);
            Assert.True(d.IsDirty); Assert.Equal("Retain this draft", d.Values.Words); Assert.Null(d.SubmittedRevision);
            Assert.DoesNotContain(model.Engine.Sessions, r => ReferenceEquals(r, row));
        });
    }
}

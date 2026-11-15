using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class ExplicitRecordEditorTests
{
    private static TimerModel Model() { var model = TimerModel.CreateDemo(() => new DateTime(2026, 10, 14, 14, 15, 0)); model.CompleteSetup(); return model; }
    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Settle(Window w) { for (var i = 0; i < 3; i++) { Dispatcher.UIThread.RunJobs(); using var frame = w.CaptureRenderedFrame(); } }
    private static void Click(Control root, string name) => Find<Button>(root, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public void ABlockDraftSurvivesNavigationAndOnlySaveWritesIt()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show();
        try
        {
            var rows = model.Engine.Sessions.Where(r => r.Duration > TimeSpan.Zero && r.Day == model.SelectedDate).Take(2).ToArray();
            var before = model.Editor.Note(rows[0]); model.SelectBlock(new[] { rows[0] }, false); Settle(window);
            var box = Find<TextBox>(window, "SelectedNote"); box.Focus(); box.Text = "Line one\nLine two";
            Find<TextBox>(window, "SelectedStart").Focus();
            Assert.Equal(before, model.Editor.Note(rows[0]));
            model.SelectBlock(new[] { rows[1] }, false); Settle(window);
            model.SelectBlock(new[] { rows[0] }, false); Settle(window);
            Assert.Equal("Line one\nLine two", Find<TextBox>(window, "SelectedNote").Text);
            Click(window, "DetailsSave"); Settle(window);
            Assert.Equal("Line one\nLine two", model.SelectedBlocks.Single().Note);
            Assert.False(model.Drafts.HasUnsubmittedChanges);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DetailsAndInlineEditorShareOneDraftAndCancelRestoresBoth()
    {
        var model = Model(); model.Notes.AddNote("Original", null, null); var id = model.Notes.All.Last().Id;
        using var lease = model.Drafts.AcquireNote(id);
        var details = new RecordEditor(model, model.Drafts.AcquireNote(id));
        var inline = new RecordEditor(model, model.Drafts.AcquireNote(id), inline: true);
        var panel = new StackPanel(); panel.Children.Add(details); panel.Children.Add(inline);
        var window = new Window { Width = 800, Height = 800, Content = panel }; window.Show(); Settle(window);
        try
        {
            Find<TextBox>(details, "DetailsNoteWords").Text = "Shared draft";
            Assert.Equal("Shared draft", Find<TextBox>(inline, "PageNoteField").Text);
            Assert.Equal("Original", model.Notes.Words(model.Notes.Find(id)!));
            Click(inline, "PageCancel");
            Assert.Equal("Original", Find<TextBox>(details, "DetailsNoteWords").Text);
            Assert.False(lease.Draft.IsDirty);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void InvalidLengthSurvivesNavigationAndSaveFocusesItsField()
    {
        var model = Model(); var row = model.Engine.Sessions.First(r => r.Duration > TimeSpan.Zero);
        using var held = model.Drafts.AcquireBlock(row);
        var editor = new RecordEditor(model, model.Drafts.AcquireBlock(row));
        var window = new Window { Width = 650, Height = 500, Content = editor }; window.Show(); Settle(window);
        try
        {
            var length = Find<TextBox>(window, "SelectedLength"); length.Focus(); length.Text = "nonsense";
            Click(window, "DetailsSave");
            Assert.Equal(DraftField.Length, Assert.Single(held.Draft.Errors).Field);
            Assert.True(length.IsFocused); Assert.Equal("nonsense", length.Text);
            window.Content = null; Settle(window); window.Content = editor; Settle(window);
            Assert.Equal("nonsense", Find<TextBox>(window, "SelectedLength").Text);
            Click(window, "DetailsDiscard"); Assert.False(held.Draft.IsDirty);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ACleanEditorCanDetachAndReattachAfterItsWorkspaceEntryWasPruned()
    {
        var model = Model(); model.Notes.AddNote("Reattach", null, null); var id = model.Notes.All.Last().Id;
        var editor = new RecordEditor(model, model.Drafts.AcquireNote(id));
        var window = new Window { Width = 650, Height = 500, Content = editor }; window.Show(); Settle(window);
        try
        {
            window.Content = null; Settle(window); Assert.Equal(0, model.Drafts.TrackedCount);
            window.Content = editor; Settle(window);
            Find<TextBox>(editor, "DetailsNoteWords").Text = "After reattach"; Click(editor, "DetailsSave");
            Assert.Equal("After reattach", model.Notes.Words(model.Notes.Find(id)!));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SavingWordsAfterAnExternalMeetingMoveDoesNotMoveItsEndAgain()
    {
        var model = Model(); model.Notes.AddMeeting(model.SelectedDate, TimeSpan.FromHours(9), TimeSpan.FromHours(10), "Moved meeting", null, RepeatChoice.None);
        var id = model.Notes.All.Last().Id; var editor = new RecordEditor(model, model.Drafts.AcquireNote(id));
        var window = new Window { Width = 650, Height = 500, Content = editor }; window.Show(); Settle(window);
        try
        {
            Assert.Null(model.Notes.MoveMeeting(id, TimeSpan.FromHours(9.5), TimeSpan.FromHours(10.5)));
            Find<TextBox>(editor, "DetailsMeetingName").Text = "Updated after drag"; Click(editor, "DetailsSave");
            Assert.Equal(TimeSpan.FromHours(9.5), model.Notes.Find(id)!.At); Assert.Equal(TimeSpan.FromHours(10.5), model.Notes.Find(id)!.End);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void BothAddContextsKeepIndependentNoteAndMeetingDrafts()
    {
        var model = Model(); var card = new NotesCard(model); var page = new NotesPage(model);
        var panel = new StackPanel(); panel.Children.Add(card); panel.Children.Add(page);
        var window = new Window { Width = 1050, Height = 850, Content = panel }; window.Show(); Settle(window);
        try
        {
            Find<TextBox>(card, "NoteField").Text = "Timeline note";
            model.SetNotesCardShowsMeetings(true); Settle(window); Find<TextBox>(card, "NoteField").Text = "Timeline meeting";
            model.SetNotesCardShowsMeetings(false); Settle(window); Assert.Equal("Timeline note", Find<TextBox>(card, "NoteField").Text);
            Find<TextBox>(page, "PageNoteField").Text = "Page note"; page.SetKind(true); Find<TextBox>(page, "PageNoteField").Text = "Page meeting";
            page.SetKind(false); Assert.Equal("Page note", Find<TextBox>(page, "PageNoteField").Text);
            Assert.Equal(4, model.CreationDrafts.Outstanding.Count);
            model.CreationDrafts.DiscardAll(); Assert.Empty(model.CreationDrafts.Outstanding);
            Assert.Equal("", Find<TextBox>(page, "PageNoteField").Text); Assert.Equal("", Find<TextBox>(card, "NoteField").Text);
            Find<TextBox>(page, "PageNoteField").Text = "Hidden add draft";
            panel.Children.Remove(page); model.CreationDrafts.DiscardAll(); panel.Children.Add(page); Settle(window);
            Assert.Equal("", Find<TextBox>(page, "PageNoteField").Text); Assert.Empty(model.CreationDrafts.Outstanding);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void GroupedSaveValidatesEveryRecordBeforeWritingAndIncludesAddForms()
    {
        var model = Model(); model.Notes.AddNote("Original", null, null); var id = model.Notes.All.Last().Id;
        using var lease = model.Drafts.AcquireNote(id); lease.Draft.Update(lease.Draft.Values with { Words = "Edited" });
        var creation = model.CreationDrafts.Get("notes", true); model.CreationDrafts.Update("notes", true, creation with { Words = "Meeting", Start = "invalid" });
        var dialog = new DraftReviewDialog(model, (_, _) => { }); var host = new DialogHost(); var window = new Window { Width = 800, Height = 650, Content = host }; window.Show(); host.Show(dialog); Settle(window);
        try
        {
            Click(dialog, "SaveAllDrafts"); Assert.Equal("Original", model.Notes.Words(model.Notes.Find(id)!));
            model.CreationDrafts.Update("notes", true, creation with { Words = "Meeting" }); Click(dialog, "SaveAllDrafts");
            Assert.Equal("Edited", model.Notes.Words(model.Notes.Find(id)!)); Assert.Empty(model.CreationDrafts.Outstanding);
            Assert.Contains(model.Notes.All, n => model.Notes.Words(n) == "Meeting");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ControlEnterSavesAndEscapeFirstDismissesAccountSuggestions()
    {
        var model = Model(); model.Notes.AddNote("Keyboard", null, null); var id = model.Notes.All.Last().Id;
        var editor = new RecordEditor(model, model.Drafts.AcquireNote(id));
        var window = new Window { Width = 650, Height = 500, Content = editor }; window.Show(); Settle(window);
        try
        {
            var words = Find<TextBox>(editor, "DetailsNoteWords"); words.Focus(); words.Text = "Draft #";
            window.KeyPress(Key.Escape, Avalonia.Input.RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.Equal("Draft #", words.Text); Assert.True(editor.Draft.IsDirty);
            window.KeyPress(Key.Escape, Avalonia.Input.RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.Equal("Keyboard", words.Text); Assert.False(editor.Draft.IsDirty);
            words.Text = "First line";
            window.KeyPress(Key.Enter, Avalonia.Input.RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.Contains("\n", words.Text); Assert.Equal("Keyboard", model.Notes.Words(model.Notes.Find(id)!));
            words.Text = "First line\nSecond line";
            window.KeyPress(Key.Enter, Avalonia.Input.RawInputModifiers.Control, PhysicalKey.Enter, null); Settle(window);
            Assert.Equal("First line\nSecond line", model.Notes.Words(model.Notes.Find(id)!));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void GroupedQuitCanKeepOrDiscardDraftsWithoutSavingThem()
    {
        var model = Model(); model.Notes.AddNote("Before quit", null, null); var id = model.Notes.All.Last().Id;
        using var lease = model.Drafts.AcquireNote(id); lease.Draft.Update(lease.Draft.Values with { Words = "Unfinished" });
        var host = new DialogHost(); var window = new Window { Width = 850, Height = 650, Content = host }; window.Show(); var quits = 0;
        try
        {
            var review = new DraftReviewDialog(model, (_, _) => { }, () => quits++); host.Show(review); Settle(window);
            Click(review, "KeepEditingDrafts"); Assert.Equal(0, quits); Assert.True(lease.Draft.IsDirty);
            review = new DraftReviewDialog(model, (_, _) => { }, () => quits++); host.Show(review); Settle(window);
            Click(review, "DiscardAllDrafts"); Assert.Equal(1, quits); Assert.False(lease.Draft.IsDirty);
            Assert.Equal("Before quit", model.Notes.Words(model.Notes.Find(id)!));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void EditorActionsRemainReachableInBothThemes(string theme)
    {
        var model = Model(); model.SetTheme(theme); var window = new DashboardWindow(model); window.Show();
        try
        {
            var row = model.Engine.Sessions.First(r => r.Duration > TimeSpan.Zero && r.Day == model.SelectedDate); model.SelectBlock(new[] { row }, false); Settle(window);
            var card = Find<SelectedBlockCard>(window, "SelectedBlockCard");
            foreach (var name in new[] { "DetailsSave", "DetailsDiscard" })
            {
                var button = Find<Button>(card, name); var point = button.TranslatePoint(default, card)!.Value;
                Assert.InRange(point.Y, 0, card.Bounds.Height - button.Bounds.Height); Assert.InRange(point.X, 0, card.Bounds.Width - button.Bounds.Width);
            }
            var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR");
            if (folder is not null) { Directory.CreateDirectory(folder); using var shot = window.CaptureRenderedFrame(); shot?.Save(Path.Combine(folder, "explicit-editor-" + theme + ".png")); }
        }
        finally { window.Close(); model.SetTheme("System"); }
    }
}

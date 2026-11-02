using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class UiEfficiencyTests
{
    private static DateTime Now => new(2026, 11, 2, 14, 0, 0);
    private static TimerModel Model()
    {
        var model = new TimerModel(new TrackingEngine(clock: () => Now)); model.CompleteSetup(); return model;
    }
    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Settle(Window window)
    {
        for (var i = 0; i < 3; i++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); }
    }
    private static void Press(Window window, string name)
    {
        Find<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(window);
    }

    [AvaloniaFact]
    public void NotesAndDialogsCannotNavigateTheDateOrPasteHiddenTimelineBlocks()
    {
        var model = Model();
        model.Engine.Sessions.Add(new("2026-11-02", "Account one", "09:00:00", "added", "30"));
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            model.SelectBlock(model.Engine.Sessions.ToArray(), false); model.CopySelection();
            window.ShowNotes(); Settle(window); Find<Button>(window, "NotesTab").Focus();
            window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
            window.KeyPress(Key.V, RawInputModifiers.Control, PhysicalKey.V, null);
            Assert.Equal(Now.Date, model.SelectedDate); Assert.Single(model.Engine.Sessions);
            window.SelectTab(false); model.OpenGap(); Settle(window); Find<Button>(window, "CloseDialog").Focus();
            window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
            window.KeyPress(Key.V, RawInputModifiers.Control, PhysicalKey.V, null);
            Assert.Equal(Now.Date, model.SelectedDate); Assert.Single(model.Engine.Sessions);
            window.Dialogs.CloseTop(); Settle(window); Find<Button>(window, "TimelineTab").Focus();
            window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
            Assert.Equal(Now.Date.AddDays(-1), model.SelectedDate); // Still works on the owning page.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SettingsBackAndEscapeReturnToThePreviousViewAndRetainExportChoices()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            window.SelectTab(true); Press(window, "Settings"); Press(window, "SettingsExport");
            var page = window.GetVisualDescendants().OfType<SettingsView>().Single();
            Press(window, "ExportMonthChoice"); var pick = Find<ComboBox>(window, "ExportPeriodPick"); pick.SelectedIndex = 2;
            var chosen = pick.SelectedItem;
            Assert.Contains("Week overview", Find<Button>(window, "SettingsBack").Content!.ToString());
            Press(window, "SettingsBack"); Assert.True(Find<Button>(window, "WeekOverviewTab").Classes.Contains("selected"));
            Press(window, "Settings"); Assert.Same(page, window.GetVisualDescendants().OfType<SettingsView>().Single());
            Assert.Equal("Export", Find<TextBlock>(window, "SettingsTitle").Text);
            Assert.Equal(chosen, Find<ComboBox>(window, "ExportPeriodPick").SelectedItem);
            Assert.False(Find<Button>(window, "CopyHours").IsEnabled);
            Find<Button>(window, "SettingsBack").Focus(); window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Settle(window);
            Assert.True(Find<Button>(window, "WeekOverviewTab").IsEffectivelyVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SidebarManagementOpensTheExistingEditorWithoutChangingTheWorkspace()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            window.ShowNotes(); Press(window, "ManageAccounts"); Assert.IsType<AccountsDialog>(Assert.Single(window.Dialogs.Dialogs));
            Assert.True(Find<Button>(window, "NotesTab").Classes.Contains("selected"));
            window.Dialogs.CloseTop(); Settle(window);
            Assert.True(Find<Button>(window, "NotesTab").Classes.Contains("selected"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ResetAllFiltersClearsTheHeaderSearchButKeepsTheNoteDraft()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); window.ShowNotes(); Settle(window);
        try
        {
            Find<TextBox>(window, "PageNoteField").Text = "An unfinished draft";
            Press(window, "NotesView_Important"); Press(window, "NotesAccount_Accountone");
            Assert.Contains("account filters", Find<TextBlock>(window, "NotesPageEmpty").Text);
            Find<TextBox>(window, "NotesSearch").Text = "no match"; Settle(window);
            Assert.Contains("Search: no match", Find<TextBlock>(window, "NotesResultSummary").Text);
            Press(window, "NotesResetFilters");
            Assert.Equal("", Find<TextBox>(window, "NotesSearch").Text);
            Assert.Equal("An unfinished draft", Find<TextBox>(window, "PageNoteField").Text);
            Assert.Equal(NoteView.All, window.GetVisualDescendants().OfType<NotesPage>().Single().View);
            Assert.Contains("All accounts", Find<TextBlock>(window, "NotesResultSummary").Text);
            Assert.False(Find<Button>(window, "NotesResetFilters").IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void RemovedNoteHasItsOwnUndoAndDoesNotUndoALedgerEdit()
    {
        var model = Model(); model.Notes.AddNote("!! Keep the account and reminder", "Account one", Now.AddHours(1));
        var before = Assert.Single(model.Notes.All); model.Notes.Toggle(before.Id); before = Assert.Single(model.Notes.All);
        var window = new DashboardWindow(model); window.Show(); window.ShowNotes(); Settle(window);
        try
        {
            Press(window, "PageNoteRemove_" + before.Id); Assert.Empty(model.Notes.All); Assert.True(model.CanUndoNotice);
            Assert.True(Find<Button>(window, "UndoMessage").IsVisible);
            Press(window, "UndoMessage"); var after = Assert.Single(model.Notes.All);
            Assert.Equal(before with { Id = after.Id }, after); Assert.False(model.CanUndoNotice);
            model.UndoNotice(); Assert.Single(model.Notes.All);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void RemovalUndoExpiresAndANewRemovalOnlyRestoresItsOwnItem()
    {
        var model = Model(); model.Notes.AddNote("First", null, null); model.Notes.AddNote("Second", null, null);
        var ids = model.Notes.All.Select(n => n.Id).ToArray();
        model.RemoveNote(ids[0]); model.RemoveNote(ids[1]); model.UndoNotice();
        Assert.Equal("Second", Assert.Single(model.Notes.All).Text);
        model.RemoveNote(Assert.Single(model.Notes.All).Id); model.ExpireMessages(); model.UndoNotice();
        Assert.Empty(model.Notes.All); Assert.False(model.CanUndoNotice);
    }

    [AvaloniaFact]
    public void RemovalUndoPersistsMeetingTimesAndCompletionAcrossRestart()
    {
        WithStore((folder, store, model) =>
        {
            model.Notes.AddMeeting(Now.Date, TimeSpan.FromHours(10), TimeSpan.FromHours(11), "Review", "Account one", RepeatChoice.None);
            var before = Assert.Single(model.Notes.All); model.Notes.Toggle(before.Id); before = Assert.Single(model.Notes.All);
            model.RemoveNote(before.Id); Assert.Empty(store.LoadDayNotes()); model.UndoNotice();
            var restored = Assert.Single(store.LoadDayNotes()); Assert.Equal(before with { Id = restored.Id }, restored);
        }, reopened => Assert.True(Assert.Single(reopened.All).Done));
    }

    [AvaloniaFact]
    public void UndoCancelsARefusedDeleteWithoutDuplicatingTheStoredNote()
    {
        WithStore((folder, store, model) =>
        {
            model.Notes.AddNote("Preserved", null, Now); var note = Assert.Single(model.Notes.All);
            store.Database.Execute("CREATE TRIGGER block_removal BEFORE DELETE ON day_notes BEGIN SELECT RAISE(ABORT, 'locked'); END");
            model.RemoveNote(note.Id); Assert.True(model.Notes.Unsaved); model.UndoNotice();
            store.Database.Execute("DROP TRIGGER block_removal"); model.Notes.RetryUnsaved();
            Assert.False(model.Notes.Unsaved); Assert.Equal(note, Assert.Single(model.Notes.All)); Assert.Single(store.LoadDayNotes());
        }, reopened => Assert.Single(reopened.All));
    }

    [AvaloniaFact]
    public void UndoKeepsARefusedRestoreOnScreenAndRetriesAllItsFields()
    {
        WithStore((folder, store, model) =>
        {
            model.Notes.AddNote("!! Completed", "Account one", Now.AddHours(1)); var note = Assert.Single(model.Notes.All);
            model.Notes.Toggle(note.Id); note = Assert.Single(model.Notes.All); model.RemoveNote(note.Id);
            store.Database.Execute("CREATE TRIGGER block_restore BEFORE INSERT ON day_notes BEGIN SELECT RAISE(ABORT, 'full'); END");
            model.UndoNotice(); Assert.True(model.Notes.Unsaved); Assert.True(Assert.Single(model.Notes.All).Done);
            store.Database.Execute("DROP TRIGGER block_restore"); model.Notes.RetryUnsaved();
            var restored = Assert.Single(store.LoadDayNotes()); Assert.Equal(note with { Id = restored.Id }, restored); Assert.False(model.Notes.Unsaved);
        }, reopened => Assert.True(Assert.Single(reopened.All).Done));
    }

    private static void WithStore(Action<string, TimerStore, TimerModel> check, Action<NotesModel> reload)
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-ui-recovery-" + Guid.NewGuid());
        try
        {
            using (var store = new TimerStore(folder))
            {
                var engine = new TrackingEngine(clock: () => Now); var model = new TimerModel(engine, store.Load(engine), store); model.CompleteSetup();
                check(folder, store, model); model.MarkClosed();
            }
            using var reopened = new TimerStore(folder); reload(new NotesModel(reopened, () => Now, () => new[] { "Account one" }));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [AvaloniaFact]
    public void RestoredCompletionIsRetriedIfTheFollowUpUpdateIsRefused()
    {
        WithStore((folder, store, model) =>
        {
            model.Notes.AddNote("Completed", null, null); var note = Assert.Single(model.Notes.All);
            model.Notes.Toggle(note.Id); model.RemoveNote(note.Id);
            store.Database.Execute("CREATE TRIGGER block_completion BEFORE UPDATE ON day_notes BEGIN SELECT RAISE(ABORT, 'locked'); END");
            model.UndoNotice(); Assert.True(model.Notes.Unsaved); Assert.True(Assert.Single(model.Notes.All).Done);
            store.Database.Execute("DROP TRIGGER block_completion"); model.Notes.RetryUnsaved();
            Assert.False(model.Notes.Unsaved); Assert.True(Assert.Single(store.LoadDayNotes()).Done);
        }, reopened => Assert.True(Assert.Single(reopened.All).Done));
    }

    [AvaloniaFact]
    public void WeekExportUsesTheViewedWeekAndItsFeedbackSurvivesTheWorkspaceRefresh()
    {
        WithStore((folder, store, model) =>
        {
            model.Engine.Sessions.Add(new("2026-10-26", "Account one", "09:00:00", "added", "30"));
            model.Engine.Sessions.Add(new("2026-11-02", "Account one", "09:00:00", "added", "60"));
            model.GoToDate(Now.Date.AddDays(-7)); var window = new DashboardWindow(model); window.Show(); window.SelectTab(true); Settle(window);
            try
            {
                Assert.Equal(ReviewViews.WeekTableFor(model, model.SelectedDate).Table.ToTsv(), TimesheetActions.Table(model, model.SelectedDate));
                Press(window, "WeekExportCsv"); Assert.Contains("Week-20261026", model.ExportPath);
                Assert.True(File.Exists(Path.Combine(model.ExportPath!, "Summary.csv")));
                Assert.StartsWith("Both files saved", Find<TextBlock>(window, "MessageText").Text);
                Assert.Equal(Now.Date.AddDays(-7), model.SelectedDate);
            }
            finally { window.Close(); }
        }, _ => { });
    }

    [AvaloniaFact]
    public void FocusAndCheckboxBoundariesMeetThreeToOneOnBuiltInPageSurfaces()
    {
        var model = Model();
        foreach (var theme in AppThemes.BuiltIn)
        {
            model.SetTheme(theme.Name);
            var dictionary = (ResourceDictionary)Application.Current!.Resources.ThemeDictionaries[ThemeEngine.VariantFor(theme)]!;
            foreach (var key in new[] { "FocusStroke", "CheckBoxCheckBackgroundStrokeUnchecked", "CheckBoxCheckBackgroundStrokeChecked" })
            {
                var colour = Assert.IsType<SolidColorBrush>(dictionary[key]).Color.ToString();
                foreach (var surface in new[] { "Surface", "Panel" }) Assert.True(ThemeColours.Contrast(colour, theme.Colour(surface)) >= 3, theme.Name + ": " + key);
            }
        }
    }
}

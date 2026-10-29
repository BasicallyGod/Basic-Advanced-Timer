using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class EditingTests
{
    private static TrackingEngine Engine() => new(clock: () => new DateTime(2026, 10, 9, 14, 0, 0));
    private static SessionRow Row(string start = "09:00:00", string end = "10:00:00", string minutes = "60") => new("2026-10-09", "Account one", start, end, minutes);

    [Fact]
    public void EditMovesNoteAndUndoRetainsLaterRecordingAcrossRestart()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-edit-" + Guid.NewGuid());
        try
        {
            using (var store = new TimerStore(folder))
            {
                var engine = Engine(); var prefs = store.Load(engine); var editor = store.Editor!;
                engine.Sessions.Add(Row()); store.Save(engine, prefs);
                var old = engine.Sessions.Single();
                editor.Edit(old, "Account two", old.From.AddMinutes(30), old.To.AddMinutes(45), "Revised, note\nSecond line");
                store.Save(engine, prefs);
                Assert.Equal(TimeSpan.FromMinutes(75), engine.Sessions.Single().Duration);
                Assert.Equal("Revised, note\nSecond line", editor.Note(engine.Sessions.Single()));
                engine.AddSession("Internal", engine.Now.AddMinutes(-10), engine.Now);
                store.Save(engine, prefs);
            }
            using (var store = new TimerStore(folder))
            {
                var engine = Engine(); var prefs = store.Load(engine); var editor = store.Editor!;
                Assert.Equal("Revised, note\nSecond line", editor.Note(engine.Sessions[0]));
                Assert.True(editor.CanUndo); editor.Undo(); store.Save(engine, prefs);
                Assert.Equal(2, engine.Sessions.Count); Assert.Equal(Row(), engine.Sessions[0]);
                Assert.Equal("Internal", engine.Sessions[1].Account);
                Assert.Equal("", editor.Note(engine.Sessions[0]));
                Assert.False(editor.CanUndo);
            }
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void AddDeleteAndUndoAreReversibleAndRejectOverlapAndStaleEntries()
    {
        var engine = Engine(); var row = Row(); engine.Sessions.Add(row); var editor = new LedgerEditor(engine);
        editor.Add("Account two", row.To, row.To.AddMinutes(30), "Added note");
        Assert.Equal(2, engine.Sessions.Count); editor.Undo(); Assert.Single(engine.Sessions);
        row = engine.Sessions.Single(); editor.Delete(row); Assert.Empty(engine.Sessions); editor.Undo();
        Assert.Equal(Row(), engine.Sessions.Single());
        var current = engine.Sessions.Single();
        Assert.Throws<ArgumentException>(() => editor.Add("Account two", current.From.AddMinutes(20), current.To, ""));
        Assert.Throws<ArgumentException>(() => editor.Edit(current, current.Account, current.From, current.From, ""));
        Assert.Throws<InvalidOperationException>(() => editor.Delete(row)); // Undo replaced this reference.
        Assert.Single(engine.Sessions);
        engine.ToggleAccount("Internal");
        editor.Add("Account two", engine.Now.AddHours(1), engine.Now.AddHours(2), "Future entry");
        Assert.Equal(2, engine.Sessions.Count);
    }

    [Fact]
    public void AFailedSaveChangesNothingAndTheRetrySavesEditNotesAndUndoTogether()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-edit-failed-" + Guid.NewGuid());
        try
        {
            using (var store = new TimerStore(folder))
            {
                var engine = Engine(); var prefs = store.Load(engine); engine.Sessions.Add(Row()); store.Save(engine, prefs);
                var row = engine.Sessions.Single(); store.Editor!.Edit(row, row.Account, row.From, row.To.AddMinutes(15), "Checkpoint note");
                StorageFaults.Block(store);
                Assert.NotNull(Record.Exception(() => store.Save(engine, prefs)));
                Assert.Equal(TimeSpan.FromHours(1), StorageFaults.Saved(store).Single().Duration); // The failed save left the old records whole.
                StorageFaults.Unblock(store); store.Save(engine, prefs);
            }
            using (var store = new TimerStore(folder))
            {
                var engine = Engine(); var prefs = store.Load(engine);
                Assert.Equal(TimeSpan.FromMinutes(75), engine.Sessions.Single().Duration);
                Assert.Equal("Checkpoint note", store.Editor!.Note(engine.Sessions.Single()));
                Assert.True(store.Editor.CanUndo); store.Editor.Undo(); store.Save(engine, prefs);
                Assert.Equal(TimeSpan.FromHours(1), engine.Sessions.Single().Duration);
            }
        }
        finally { Directory.Delete(folder, true); }
    }

    [AvaloniaFact]
    public void TimelineBodyAndEdgesEditRealRowsAndUndoRestoresThem()
    {
        var engine = Engine(); engine.Sessions.Add(Row());
        engine.Sessions.Add(Row("11:00:00", "12:00:00") with { Account = "Account two" });
        var model = new TimerModel(engine); model.SetTimelineScale(160, 0, 0); model.TimelineOffset = null; var window = new DashboardWindow(model); window.Show(); // 160 pixels an hour.
        try
        {
            void Layout() { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); Dispatcher.UIThread.RunJobs(); using var settled = window.CaptureRenderedFrame(); }
            void Drag(double fraction, double pixels)
            {
                Layout();
                var card = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "TimelineEntry0");
                var point = card.TranslatePoint(new Point(card.Width * fraction, card.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left); window.MouseMove(point + new Vector(pixels, 0)); window.MouseUp(point + new Vector(pixels, 0), MouseButton.Left);
            }
            Drag(.5, 80); Assert.Equal(9, engine.Sessions[0].From.Hour); Assert.Equal(30, engine.Sessions[0].From.Minute);
            Assert.Equal(TimeSpan.FromHours(1), engine.Sessions[0].Duration); Assert.True(model.UndoEdit());
            Drag(.99, 80); Assert.Equal(TimeSpan.FromMinutes(90), engine.Sessions[0].Duration); Assert.True(model.UndoEdit());
            Drag(.01, 40); Assert.Equal(15, engine.Sessions[0].From.Minute); Assert.Equal(TimeSpan.FromMinutes(45), engine.Sessions[0].Duration);
            Assert.True(model.UndoEdit());
            Drag(.5, 168); Assert.Equal(10, engine.Sessions[0].From.Hour); Assert.Equal(11, engine.Sessions[0].To.Hour); Assert.Null(model.EditError); // Stops against account two.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ANoteOnlyEditKeepsFractionalCreditAndClicksSelectWhileDoubleClickAddsTime()
    {
        var engine = Engine(); var row = Row(end: "09:01:01", minutes: "1.01") with { Account = "ACCOUNT ONE" }; engine.Sessions.Add(row);
        var model = new TimerModel(engine);
        Assert.True(model.EditEntry(row, row.Account, row.From, row.To, "Short entry note"));
        Assert.Same(row, engine.Sessions.Single()); Assert.Equal("1.01", engine.Sessions.Single().Minutes); Assert.Equal("Short entry note", model.Editor.Note(row));
        DateTime? gap = null;
        model.EntryRequested += (from, _) => gap = from;
        var window = new Window { Width = 600, Height = 500, Content = ReviewViews.Timeline(model) }; window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); Dispatcher.UIThread.RunJobs(); using var settled = window.CaptureRenderedFrame();
            var card = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "TimelineEntry0");
            var point = card.TranslatePoint(new Point(card.Width / 2, card.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            Assert.Empty(model.SelectedRows); Assert.Null(gap); // Two clicks select and deselect. There is no double-click edit.
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            Assert.Same(row, Assert.Single(model.SelectedRows)); model.ClearSelection(); Dispatcher.UIThread.RunJobs();
            var canvas = window.GetVisualDescendants().OfType<Canvas>().Single(c => c.Name == "TimelineCanvas");
            var empty = canvas.TranslatePoint(new Point(ReviewViews.TimelineGutter + 9 * model.TimelinePixelsPerHour + 30, 150), window)!.Value; // Past 09:00 in the blank band: 09:20.
            window.MouseDown(empty, MouseButton.Left); window.MouseUp(empty, MouseButton.Left);
            Assert.Null(gap); // One click adds nothing.
            window.MouseDown(empty, MouseButton.Left); window.MouseUp(empty, MouseButton.Left);
            Assert.Equal(row.Day.AddHours(9).AddMinutes(20), gap);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void RetryingFailedDialogSaveDoesNotAddTheEntryTwice()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-edit-dialog-retry-" + Guid.NewGuid());
        try
        {
            using var store = new TimerStore(folder);
            var engine = Engine(); var prefs = store.Load(engine); engine.Sessions.Add(Row());
            var model = new TimerModel(engine, prefs, store); Assert.True(model.Save());
            StorageFaults.Block(store);
            var row = engine.Sessions.Single();
            var dialog = new EntryDialog(model, row.To, row.To.AddMinutes(30)).Open(model);
            try
            {
                var button = dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SaveEntry");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(dialog.IsVisible); Assert.Equal(2, engine.Sessions.Count); Assert.NotNull(model.LastError);
                StorageFaults.Unblock(store);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(dialog.IsVisible); Assert.Equal(2, engine.Sessions.Count); Assert.Null(model.LastError);
            }
            finally { dialog.Close(); }
        }
        finally { Directory.Delete(folder, true); }
    }

    [AvaloniaFact]
    public void EditingSavesNotesAndAddTimeAddsGapTime()
    {
        var engine = Engine(); engine.Sessions.Add(Row());
        var model = new TimerModel(engine); var window = new DashboardWindow(model); window.Show();
        try
        {
            var row = engine.Sessions[0];
            Assert.True(model.EditEntry(row, row.Account, row.From, row.From.AddMinutes(75), "Drafting\nClient review"));
            Assert.Equal(TimeSpan.FromMinutes(75), engine.Sessions[0].Duration);
            Assert.Equal("Drafting\nClient review", model.Editor.Note(engine.Sessions[0]));
            Dispatcher.UIThread.RunJobs();
            var output = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR") ?? Path.GetTempPath(); Directory.CreateDirectory(output);
            var add = new EntryDialog(model, row.To.AddMinutes(30), row.To.AddMinutes(45)).Open(window);
            Dispatcher.UIThread.RunJobs(); using var addFrame = add.CaptureRenderedFrame(); addFrame?.Save(Path.Combine(output, "entry-editor.png"));
            add.Close();
            Assert.True(model.AddEntry("Account two", row.To.AddMinutes(30), row.To.AddMinutes(45), "Gap note"));
            Assert.Equal(2, engine.Sessions.Count); Assert.True(model.UndoEdit()); Assert.Single(engine.Sessions);
        }
        finally { window.Close(); }
    }

    [Theory]
    [InlineData(100, 0, -1)] [InlineData(100, 5, -1)] [InlineData(100, 95, 1)] [InlineData(100, 50, 0)]
    [InlineData(18, 2, -1)] [InlineData(18, 9, 0)] [InlineData(18, 16, 1)]
    [InlineData(6, 1, -1)] [InlineData(6, 5, 1)] [InlineData(3, 0.5, -1)] [InlineData(3, 2.5, 1)]
    public void ShortCardsStillHaveBothEdgeHandles(double height, double y, int expected) => Assert.Equal(expected, TimelineGestures.EdgeAt(y, height));

    [Fact]
    public void DragsAreClampedToTheDayAndMinimumLength()
    {
        var row = Row("23:00:00", "added", "30");
        Assert.Equal(row.Day.AddDays(1) - row.To, TimelineGestures.ClampToDay(row, 0, TimeSpan.FromHours(5)));
        Assert.Equal(row.Day - row.From, TimelineGestures.ClampToDay(row, 0, TimeSpan.FromHours(-30)));
        Assert.Equal(row.Day.AddDays(1) - row.To, TimelineGestures.ClampToDay(row, 1, TimeSpan.FromHours(5)));
        Assert.Equal(TimeSpan.FromMinutes(1) - row.Duration, TimelineGestures.ClampToDay(row, 1, TimeSpan.FromHours(-2)));
        Assert.Equal(row.Duration - TimeSpan.FromMinutes(1), TimelineGestures.ClampToDay(row, -1, TimeSpan.FromHours(2)));
        Assert.Equal(TimeSpan.FromMinutes(5), TimelineGestures.ClampToDay(row, 0, TimeSpan.FromMinutes(5)));
    }

    [AvaloniaFact]
    public void BackspaceDeletesSelectedBlocksAsOneUndoableEdit()
    {
        var engine = Engine(); engine.Sessions.AddRange(new[] { Row("09:00:00", "added", "30"), Row("10:00:00", "added", "30"), Row("11:00:00", "added", "30") });
        var model = new TimerModel(engine); var window = new DashboardWindow(model); window.Show();
        try
        {
            window.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None); Assert.Equal(3, engine.Sessions.Count); // Nothing selected.
            model.ToggleSelection(new[] { engine.Sessions[0] }); model.ToggleSelection(new[] { engine.Sessions[2] });
            window.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None);
            Assert.Equal("10:00:00", Assert.Single(engine.Sessions).Start); Assert.Empty(model.SelectedRows);
            Assert.True(model.UndoEdit()); Assert.Equal(3, engine.Sessions.Count);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void CtrlZUndoesTheLastEditAndTheTooltipsNameTheKey()
    {
        var engine = Engine(); engine.Sessions.AddRange(new[] { Row("09:00:00", "added", "30"), Row("10:00:00", "added", "30") });
        var model = new TimerModel(engine); var window = new DashboardWindow(model); window.Show();
        try
        {
            model.ToggleSelection(new[] { engine.Sessions[0] }); Assert.True(model.DeleteSelection()); Assert.Single(engine.Sessions);
            window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);
            Assert.Equal(2, engine.Sessions.Count);
            window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control); // Nothing left to undo is not an error.
            Assert.Equal(2, engine.Sessions.Count);
            var undo = window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "UndoEdit");
            Assert.Contains("Ctrl Z", AllText(ToolTip.GetTip(undo)));
        }
        finally { window.Close(); }
        static string AllText(object? tip) => tip is Control c ? string.Join(" ", c.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)) : tip?.ToString() ?? "";
    }

    [AvaloniaFact]
    public void CtrlYAndCtrlShiftZPutBackWhatCtrlZTookBackAndANewEditEndsIt()
    {
        var engine = Engine(); engine.Sessions.AddRange(new[] { Row("09:00:00", "added", "30"), Row("10:00:00", "added", "30"), Row("11:00:00", "added", "30") });
        var model = new TimerModel(engine); var window = new DashboardWindow(model); window.Show();
        try
        {
            model.ToggleSelection(new[] { engine.Sessions[0] }); Assert.True(model.DeleteSelection());
            model.ToggleSelection(new[] { engine.Sessions[0] }); Assert.True(model.DeleteSelection()); Assert.Single(engine.Sessions);
            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.Control); Assert.Single(engine.Sessions); // Nothing taken back yet.
            window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control); window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control); Assert.Equal(3, engine.Sessions.Count);
            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.Control); Assert.Equal(2, engine.Sessions.Count);
            window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control | RawInputModifiers.Shift); Assert.Single(engine.Sessions);
            window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control); Assert.Equal(2, engine.Sessions.Count);
            Assert.True(model.AddEntry("Account two", engine.Sessions[^1].To.AddMinutes(30), engine.Sessions[^1].To.AddMinutes(45), "")); Assert.False(model.Editor.CanRedo);
            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.Control); Assert.Equal(3, engine.Sessions.Count); // A new edit ended it.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void UndoAndRedoAreIconButtonsWithTheirKeysInTheirTooltips()
    {
        var engine = Engine(); engine.Sessions.AddRange(new[] { Row("09:00:00", "added", "30"), Row("10:00:00", "added", "30") });
        var model = new TimerModel(engine); var window = new DashboardWindow(model); window.Show();
        try
        {
            Button Find(string name) => window.GetVisualDescendants().OfType<Button>().First(b => b.Name == name);
            static string Tip(Button b) => ToolTip.GetTip(b) is StackPanel p ? string.Join(" ", p.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)) : "";
            Assert.False(Find("UndoEdit").IsEnabled); Assert.False(Find("RedoEdit").IsEnabled);
            model.ToggleSelection(new[] { engine.Sessions[0] }); model.DeleteSelection();
            Assert.True(Find("UndoEdit").IsEnabled); Assert.False(Find("RedoEdit").IsEnabled); Assert.Equal("Undo delete entry Ctrl Z", Tip(Find("UndoEdit")));
            model.UndoEdit();
            Assert.False(Find("UndoEdit").IsEnabled); Assert.True(Find("RedoEdit").IsEnabled); Assert.Equal("Redo delete entry Ctrl Y", Tip(Find("RedoEdit")));
            Assert.IsType<IconGlyph>(Find("RedoEdit").Content); Assert.Equal("Redo", Avalonia.Automation.AutomationProperties.GetName(Find("RedoEdit")));
            model.RedoEdit(); Assert.Single(engine.Sessions); Assert.True(Find("UndoEdit").IsEnabled); Assert.False(Find("RedoEdit").IsEnabled);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PlusAndMinusLengthenAndShortenTheSelectedBlockByAMinuteAsOneUndoStepEach()
    {
        var engine = Engine(); engine.Sessions.AddRange(new[] { Row("09:00:00", "added", "30"), Row("10:00:00", "added", "30") });
        var model = new TimerModel(engine); var window = new DashboardWindow(model); window.Show();
        try
        {
            window.KeyPressQwerty(PhysicalKey.Equal, RawInputModifiers.Shift); // Nothing selected does nothing.
            Assert.Equal(30, engine.Sessions[0].Duration.TotalMinutes);
            model.ToggleSelection(new[] { engine.Sessions[0] });
            window.KeyPressQwerty(PhysicalKey.Equal, RawInputModifiers.Shift);
            Assert.Equal(31, model.SelectedBlocks[0].End.Subtract(model.SelectedBlocks[0].Start).TotalMinutes);
            window.KeyPressQwerty(PhysicalKey.Minus, RawInputModifiers.None); window.KeyPressQwerty(PhysicalKey.Minus, RawInputModifiers.None);
            Assert.Equal(29, model.SelectedBlocks[0].End.Subtract(model.SelectedBlocks[0].Start).TotalMinutes);
            Assert.Equal("10:00:00", engine.Sessions[1].Start); // The neighbour stays where it is.
            Assert.True(model.UndoEdit()); Assert.Equal(30, engine.Sessions[0].Duration.TotalMinutes);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PlusAndMinusExplainWhenTheyCannotResizeAndNeverTakeABlockToNothing()
    {
        var engine = Engine(); engine.Sessions.AddRange(new[] { Row("09:00:00", "09:01:00", "1"), Row("10:00:00", "added", "30") });
        var model = new TimerModel(engine);
        model.ToggleSelection(new[] { engine.Sessions[0] });
        Assert.False(model.NudgeSelection(-1)); Assert.Equal("Block can’t be shorter", model.EditError); Assert.Equal(1, engine.Sessions[0].Duration.TotalMinutes);
        model.ToggleSelection(new[] { engine.Sessions[1] });
        Assert.False(model.NudgeSelection(1)); Assert.Equal("Select one block", model.EditError);
    }

    [AvaloniaFact]
    public void VeryShortBlocksAreStillBigEnoughToSeeAndSelectAndSelectedBlocksAddNothingToTheirText()
    {
        var engine = Engine(); engine.Sessions.AddRange(new[] { Row("09:00:00", "09:01:00", "1"), Row("10:00:00", "10:20:00", "20") });
        var model = new TimerModel(engine); model.SetAccounts(model.AccountSettings.Select(x => x.Name == "Account one" ? x with { Colour = "#102030" } : x));
        var window = new DashboardWindow(model); window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame();
            var cards = window.GetVisualDescendants().OfType<Border>().Where(b => b.Tag is SessionRow).OrderBy(b => Canvas.GetLeft(b)).ToList();
            Assert.NotEqual(Avalonia.Media.Color.Parse("#102030"), ((Avalonia.Media.LinearGradientBrush)cards[0].Background!).GradientStops[0].Color); // A very dark colour is lifted a little so it shows.
            Assert.True(cards[0].Width >= ReviewViews.MinimumBlockWidth); // One minute is only a few pixels at this zoom, but it can be clicked.
            model.ToggleSelection(new[] { engine.Sessions[1] }); Dispatcher.UIThread.RunJobs(); using var again = window.CaptureRenderedFrame();
            var selected = window.GetVisualDescendants().OfType<Border>().Single(b => b.Tag is SessionRow row && ReferenceEquals(row, engine.Sessions[1]));
            var lines = selected.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.DoesNotContain(lines, t => t is not null && (t.Contains("✓") || t.Contains("Selected"))); // Selection is the border alone; the text is untouched.
            Assert.Contains("mergeSelected", selected.Classes);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void OnlyVeryDarkColoursAreLiftedForDisplay()
    {
        Assert.Equal("#91B7D5", Ui.Visible("#91B7D5")); Assert.Equal("#FFFFFF", Ui.Visible("#FFFFFF")); Assert.Equal("nope", Ui.Visible("nope"));
        foreach (var colour in TimerModel.Palette) Assert.Equal(colour, Ui.Visible(colour));
        var lifted = Ui.Visible("#0F1F30"); Assert.NotEqual("#0F1F30", lifted);
        Assert.True(Convert.ToInt32(lifted[1..3], 16) > 0x0F); // The same colour, lighter.
        Assert.Equal("#FFFFFF", Ui.InkOn(lifted)); // Still takes white text.
    }

    [AvaloniaFact]
    public void TheBottomLineAppearsOnlyForAMessageAndTheHeaderUsesIcons()
    {
        var engine = Engine(); var model = TimerModel.CreateDemo();
        var window = new DashboardWindow(model); window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame();
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsVisible), t => t.Text is not null && (t.Text.Contains("Sample data") || t.Text.Contains("saved locally") || t.Text.Contains("Test session")));
            var settings = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Settings");
            Assert.IsType<IconGlyph>(settings.Content); Assert.Equal(IconKind.Gear, ((IconGlyph)settings.Content!).Kind); // A gear, not words.
            Assert.Empty(window.GetVisualDescendants().OfType<Button>().Where(b => b.Name == "BackToPill")); // The clock on the rail is the way back to the pill.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AccountRowsHoldABudgetBarAndTheRailMarksTheChosenView()
    {
        var model = TimerModel.CreateDemo(); var dashboard = new DashboardWindow(model); dashboard.Show();
        try
        {
Dispatcher.UIThread.RunJobs(); using var frame = dashboard.CaptureRenderedFrame();
            var rows = dashboard.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("account")).ToList();
            Assert.NotEmpty(rows); Assert.All(rows, r => Assert.Equal(AccountList.RowHeight, r.Bounds.Height, 0.5)); // Tall enough for the name and time on one line with the bar under it.
            var tab = dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "TimelineTab");
            Assert.Contains("viewTab", tab.Classes); Assert.Contains("selected", tab.Classes);
        }
        finally { dashboard.Close(); }
    }

    [AvaloniaFact]
    public void ZoomSitsInABarUnderTheTimelineTheHintLineIsGoneAndADisabledButtonStaysOutlined()
    {
        var model = TimerModel.CreateDemo(); var dashboard = new DashboardWindow(model); dashboard.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = dashboard.CaptureRenderedFrame();
            Assert.DoesNotContain(dashboard.GetVisualDescendants().OfType<Button>(), b => b.Name == "ReviewTools"); // The Tools menu is gone.
            var undoButton = dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "UndoEdit");
            var zoom = dashboard.GetVisualDescendants().OfType<StackPanel>().Single(b => b.Name == "TimelineControls");
            var scrollAbove = dashboard.GetVisualDescendants().OfType<ScrollViewer>().Single(x => x.Name == "TimelineScroll");
            // One bar under the timeline: Undo (and Add time) on the left, the zoom controls on the right, on the same line.
            Assert.True(undoButton.TranslatePoint(new Point(0, 0), dashboard)!.Value.Y >= scrollAbove.TranslatePoint(new Point(0, scrollAbove.Bounds.Height - 1), dashboard)!.Value.Y);
            Assert.InRange(Math.Abs(undoButton.TranslatePoint(new Point(0, 0), dashboard)!.Value.Y + undoButton.Bounds.Height / 2 - (zoom.TranslatePoint(new Point(0, 0), dashboard)!.Value.Y + zoom.Bounds.Height / 2)), 0, 3);
            Assert.True(zoom.TranslatePoint(new Point(0, 0), dashboard)!.Value.X > undoButton.TranslatePoint(new Point(0, 0), dashboard)!.Value.X);
            Assert.DoesNotContain(dashboard.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsVisible), t => t.Text is not null && t.Text.Contains("Click blocks to select"));
            var undo = dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "UndoEdit");
            Assert.False(undo.IsEnabled); Assert.True(undo.Opacity < 1); // Dimmed by the style, keeping its outline, not a solid grey fill.
            dashboard.SelectTab(true); Dispatcher.UIThread.RunJobs();
            Assert.False(zoom.IsEffectivelyVisible); // Zoom belongs to the timeline only.
            // Export and Copy hours table live in Settings, and the Hours table and Entries views are gone.
            Assert.DoesNotContain(dashboard.GetVisualDescendants().OfType<Button>(), b => b.Name is "ExportWeek" or "WeekHoursTab" or "WeekEntriesTab");
            Assert.Contains(dashboard.GetVisualDescendants().OfType<Button>(), b => b.Name == "WeekOverviewTab" && Avalonia.Automation.AutomationProperties.GetName(b) == "Week overview");
        }
        finally { dashboard.Close(); }
    }

    [AvaloniaFact]
    public void RightClickingEmptyTimelineSpaceOffersAddTimeAndPasteAndNoTidyDay()
    {
        var model = TimerModel.CreateDemo();
        var window = new Window { Width = 700, Height = 600, Content = ReviewViews.Timeline(model) }; window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame();
            var canvas = window.GetVisualDescendants().OfType<Canvas>().First(c => c.ContextMenu is not null);
            var items = canvas.ContextMenu!.Items.OfType<MenuItem>().ToList();
            Assert.Contains(items, i => i.Name == "PasteHereMenu"); Assert.Contains(items, i => i.Header as string == "Add time…");
            Assert.DoesNotContain(items, i => i.Name == "TidyDayMenu");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheDayButtonIsGreenToStartAndRedToFinishWithMatchingTypeAndAWavingFlag()
    {
        var now = new DateTime(2026, 10, 9, 7, 0, 0); // Before the usual start, so the day has not begun.
        var model = TimerModel.CreateDemo(() => now); var dashboard = new DashboardWindow(model); dashboard.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = dashboard.CaptureRenderedFrame();
            var day = dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "DashboardDay"); var breakButton = dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "DashboardBreak");
            var flag = day.GetVisualDescendants().OfType<IconGlyph>().Single(g => g.Kind == IconKind.Flag); var sun = day.GetVisualDescendants().OfType<IconGlyph>().Single(g => g.Kind == IconKind.Sunrise);
            Assert.Equal(2, day.GetVisualDescendants().OfType<IconGlyph>().Count()); // A flag to finish the day and a sunrise to start it; one shows at a time.
            void Check() { var started = model.DayStarted; Assert.Equal(started, flag.IsVisible); Assert.Equal(!started, sun.IsVisible); Assert.Equal("Finish", flag.TintKey); Assert.Equal("Start", sun.TintKey); } // A red flag once running, a green sunrise before.
            Check(); model.ToggleDay(); Dispatcher.UIThread.RunJobs(); Check(); model.ToggleDay(); Dispatcher.UIThread.RunJobs(); Check();
            Assert.False(dashboard.FinishQuestionOpen); // Starting the day does not ask anything.
            // The flag waves while the pointer is over the button, and the cup steams. (Headless tests do not run animation timers, so
            // this checks the switches, not the frames.)
            var over = day.TranslatePoint(new Point(day.Bounds.Width / 2, day.Bounds.Height / 2), dashboard)!.Value;
            dashboard.MouseMoveUntil(over, () => flag.Waving); Assert.True(flag.Waving);
            dashboard.MouseMove(new Point(5, 5)); Assert.False(flag.Waving);
            var cup = breakButton.GetVisualDescendants().OfType<BreakGlyph>().Single(); var cupSpot = breakButton.TranslatePoint(new Point(breakButton.Bounds.Width / 2, breakButton.Bounds.Height / 2), dashboard)!.Value;
            var wasSteaming = cup.Steaming; dashboard.MouseMoveUntil(cupSpot, () => cup.Steaming); Assert.True(cup.Steaming);
            model.SetPillScale(null); Dispatcher.UIThread.RunJobs(); Assert.True(cup.Steaming); // A model update while the pointer is over it does not stop the steam, as on the pill.
            dashboard.MouseMove(new Point(5, 5)); Assert.Equal(wasSteaming, cup.Steaming);
        }
        finally { dashboard.Close(); }
    }

    [AvaloniaFact]
    public void MessagesAppearInTheWorkspaceOnlyAndNeverChangeTheTimelineHeight()
    {
        var model = TimerModel.CreateDemo(); var dashboard = new DashboardWindow(model); dashboard.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = dashboard.CaptureRenderedFrame();
            var scroll = dashboard.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "TimelineScroll"); var before = scroll.Bounds.Height;
            model.Notice = "A message for the footer"; model.ToggleFormat(); model.ToggleFormat(); Dispatcher.UIThread.RunJobs(); using var again = dashboard.CaptureRenderedFrame();
            var message = dashboard.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "A message for the footer" && t.IsVisible);
            Assert.True(message.TranslatePoint(new Point(0, 0), dashboard)!.Value.X > Sidebar.Width); // In the workspace, beside the sidebar.
            Assert.Equal(before, scroll.Bounds.Height, 0.5);
        }
        finally { dashboard.Close(); }
    }

    [AvaloniaFact]
    public void TheSidebarsLightFollowsBreakAndTheSidebarAndHeaderDragTheWindow()
    {
        var now = new DateTime(2026, 10, 9, 12, 0, 0);
        var model = TimerModel.CreateDemo(() => now); var dashboard = new DashboardWindow(model); dashboard.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = dashboard.CaptureRenderedFrame();
            var lights = dashboard.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "SidebarLights");
            void Settle() { Dispatcher.UIThread.RunJobs(); using var f = dashboard.CaptureRenderedFrame(); }
            // The light is amber on a break.
            if (model.IsOnBreak) model.ToggleBreak();
            model.ToggleBreak(); Settle(); Assert.Equal("On break", ToolTip.GetTip(lights));
            model.ToggleBreak(); Settle(); Assert.NotEqual("On break", ToolTip.GetTip(lights));
            // The break is at the right end of the tracking card, level with the play tile.
            var breakButton = dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "DashboardBreak"); var play = dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SidebarPause");
            Assert.InRange(Math.Abs(breakButton.TranslatePoint(new Point(0, breakButton.Bounds.Height / 2), dashboard)!.Value.Y - play.TranslatePoint(new Point(0, play.Bounds.Height / 2), dashboard)!.Value.Y), 0, 1);
            Assert.True(breakButton.TranslatePoint(default, dashboard)!.Value.X > play.TranslatePoint(default, dashboard)!.Value.X + play.Bounds.Width);
            // The window can be picked up by the sidebar and the header row, but not by a button or the timeline.
            var tab = dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "TimelineTab");
            var tabAt = tab.TranslatePoint(new Point(tab.Bounds.Width / 2, tab.Bounds.Height / 2), dashboard)!.Value;
            var sidebar = dashboard.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "Sidebar"); var empty = sidebar.TranslatePoint(new Point(sidebar.Bounds.Width / 2, sidebar.Bounds.Height - 90), dashboard)!.Value;
            Assert.True(dashboard.InDragZone(empty)); // Empty sidebar between the accounts and the foot.
            var header = dashboard.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ViewTabs"); Assert.True(dashboard.InDragZone(new Point(header.TranslatePoint(new Point(header.Bounds.Width + 60, 10), dashboard)!.Value.X, tabAt.Y)));
            var timelineScroll = dashboard.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "TimelineScroll");
            Assert.False(dashboard.InDragZone(timelineScroll.TranslatePoint(new Point(100, 100), dashboard)!.Value));
            Assert.False(Ui.IsPlainSurface(tab));
        }
        finally { dashboard.Close(); }
    }

    [AvaloniaFact]
    public void TheDashboardStaysOnTopAndTheBreakButtonSaysWhatItWillDoWhileRecording()
    {
        var model = TimerModel.CreateDemo(); var dashboard = new DashboardWindow(model); var pill = new PillWindow(model); dashboard.Show(); pill.Show();
        try
        {
            Assert.True(dashboard.Topmost); Assert.True(pill.Topmost);
            model.ToggleAccount("Account three"); Dispatcher.UIThread.RunJobs(); using var frame = pill.CaptureRenderedFrame();
            Assert.True(model.IsTracking); Assert.Equal("Recording", model.ThemeState); // However late in the day it is, there is no overtime state.
            Assert.Equal("Recording", ToolTip.GetTip(pill.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "PillLights")));
            Assert.Equal("Start break", ToolTip.GetTip(dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "DashboardBreak")));
            model.ToggleBreak(); Dispatcher.UIThread.RunJobs();
            Assert.StartsWith("End break", ToolTip.GetTip(dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "DashboardBreak"))?.ToString());
        }
        finally { dashboard.Close(); pill.Close(); }
    }
}

using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Automation.Peers;
using Avalonia.Controls.Primitives;
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

public sealed class DisplayPresentationTests
{
    private static readonly DateTime Day = new(2026, 10, 9);
    private static TimerModel Model(bool decimals = false)
    {
        var engine = new TrackingEngine(clock: () => Day.AddHours(14));
        var model = new TimerModel(engine, new StoreSettings(new() { new("NASA R&D", Colour: "#91B7D5", BudgetHours: 20, BudgetPeriod: "month"), new("API TEAM", Colour: "#AC97D5"), new("Quiet account") }, decimals, "Dark", DayOfWeek.Monday));
        model.CompleteSetup(); return model;
    }
    private static void Settle(Window w) { for (var i = 0; i < 3; i++) { Dispatcher.UIThread.RunJobs(); using var frame = w.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Click(Window w, string name) { Find<Button>(w, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(w); }
    private static void PressKey(Control target, Key key, KeyModifiers modifiers = KeyModifiers.None) => target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });

    [AvaloniaFact]
    public void AccountPresentationPreservesRealNamesAndDistinguishesHiddenAndHistoricalAccounts()
    {
        var model = Model(); model.Engine.Sessions.Add(new("2026-10-09", "Past account", "09:00:00", "added", "30"));
        model.SetAccounts(model.AccountSettings.Select(a => a.Name == "API TEAM" ? a with { Hidden = true } : a).ToArray());
        var catalogue = AccountCatalogue.Capture(model, patterns: true);
        Assert.Equal("NASA R&D", catalogue.Find("NASA R&D").DisplayName);
        Assert.True(catalogue.Find("API TEAM").Hidden); Assert.False(catalogue.Find("API TEAM").Historical);
        Assert.True(catalogue.Find("Past account").Historical);
        var pattern = catalogue.Find("NASA R&D").Pattern;
        model.SetAccounts(model.AccountSettings.Reverse().ToArray());
        Assert.Equal(pattern, AccountCatalogue.Capture(model, patterns: true).Find("NASA R&D").Pattern);
        model.WeekPresentation.TracedAccount = "NASA R&D";
        model.SetAccounts(model.AccountSettings.Select(a => a.Name == "NASA R&D" ? a with { Name = "NASA OPS" } : a).ToArray(), new Dictionary<string, string> { ["NASA R&D"] = "NASA OPS" });
        Assert.Equal("NASA OPS", model.WeekPresentation.TracedAccount);
        Assert.Equal(pattern, AccountCatalogue.Capture(model, patterns: true).Find("NASA OPS").Pattern);
        model.WeekPresentation.TracedAccount = "Quiet account";
        model.SetAccounts(model.AccountSettings.Where(a => a.Name != "Quiet account").ToArray(), deleted: new[] { "Quiet account" }); Assert.Null(model.WeekPresentation.TracedAccount);
        using var draft = model.Drafts.AcquireBlock(model.Engine.Sessions.Single());
        Assert.Equal("Past account", draft.Draft.Values.Account);
    }

    [AvaloniaFact]
    public void InkAccountMeasurementAndDrawingKeepAcronymCasing()
    {
        var model = Model(); model.SetDisplayStyle(DisplayStyle.Ink);
        try
        {
            const string name = "NASA RESEARCH"; var role = DisplaySkin.RoleFor(12);
            var exact = InkType.Width(name, role, preserveCase: true);
            Assert.NotEqual(exact, InkType.Width(name, role));
            Assert.Equal(exact, SegmentText.TextWidth(name, 12, preserveCase: true));
            var group = new DrawingGroup(); using var context = group.Open();
            Assert.Equal(exact, InkType.Draw(context, name, role, Brushes.Black, 0, 30, preserveCase: true));
            Assert.Equal(SegmentText.TextWidth(name, 12, preserveCase: true, weight: 1.3), SegmentText.Draw(context, name, 0, 0, 12, Colors.Black, weight: 1.3, preserveCase: true));
            var skin = DisplaySkin.For(model); var shortName = skin.Fit(name, 12, exact - 15, preserveCase: true);
            Assert.True(skin.Width(shortName, 12, preserveCase: true) <= exact - 15);
            Assert.StartsWith("NASA", shortName);
        }
        finally { model.SetDisplayStyle(DisplayStyle.Phosphor); }
    }

    [AvaloniaFact]
    public void InkNameKeepsOvershootAtTheLoopJoinAndOnlyHoldsOnFirstEntry()
    {
        using var motion = new EnabledMotionScope(); var model = Model(); model.SetDisplayStyle(DisplayStyle.Ink);
        try
        {
            var run = new SegmentName { Text = "NASA RESEARCH AND DEVELOPMENT" }; run.SetInkWindow(80);
            run.Step(1400); Assert.Equal(0, run.InkOffset);
            var period = (run.Text.Length + 3) * SegmentName.CellWidth;
            run.Step((period + 5) / 34 * 1000); Assert.Equal(5, run.InkOffset, 6);
            run.Step(100); Assert.Equal(8.4, run.InkOffset, 6);
            var phase = run.InkOffset; run.SetAccount(run.Text); run.SetInkWindow(80); Assert.Equal(phase, run.InkOffset);
            run.Animate = false; Assert.False(run.Scrolling);
        }
        finally { model.SetDisplayStyle(DisplayStyle.Phosphor); }
    }

    [AvaloniaFact]
    public void WeekTraceKeepsItsFocusedButtonAndSurvivesModeChanges()
    {
        var model = Model(); model.Engine.Sessions.Add(new("2026-10-09", "NASA R&D", "09:00:00", "added", "90"));
        var w = new DashboardWindow(model); w.Show(); Settle(w);
        try
        {
            Click(w, "WeekOverviewTab"); var row = Find<Button>(w, "WeekAccount_NASAR&D"); row.Focus(NavigationMethod.Tab);
            Click(w, row.Name!); Assert.Same(row, Find<Button>(w, row.Name!)); Assert.True(row.IsFocused);
            Assert.Equal(ToggleState.On, Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(row)).ToggleState);
            Click(w, "WeekKeySheet"); Assert.Equal("NASA R&D", Find<WeekInstrument>(w, WeekInstrument.PanelName).Traced);
            Click(w, "WeekClearTrace"); Assert.Null(Find<WeekInstrument>(w, WeekInstrument.PanelName).Traced);
            Assert.True(Find<Button>(w, row.Name!).IsFocused);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void TimesheetReadoutsExposeReconciledValuesAndZeroCellsWithTheirContext()
    {
        var model = Model(decimals: true); model.Engine.Sessions.Add(new("2026-10-09", "NASA R&D", "09:00:00", "added", "13.34"));
        model.Engine.Sessions.Add(new("2026-10-09", "API TEAM", "10:00:00", "added", "7.11"));
        var w = new DashboardWindow(model); w.Show(); Settle(w);
        try
        {
            Click(w, "WeekOverviewTab"); Click(w, "WeekKeySheet");
            var (table, _) = ReviewViews.WeekTableFor(model, model.WeekStart(Day)); var source = table.Rows.Single(r => r.Account == "NASA R&D");
            var cell = Find<Button>(w, "TimesheetCell_NASAR&D_20261009"); var value = Assert.IsAssignableFrom<IValueProvider>(ControlAutomationPeer.CreatePeerForElement(cell));
            Assert.True(value.IsReadOnly); Assert.Equal(source.CopyCells[Array.IndexOf(table.Dates, Day)], value.Value);
            Assert.Contains("NASA R&D, Friday 9 October", AutomationProperties.GetName(cell));
            var zero = Assert.IsAssignableFrom<IValueProvider>(ControlAutomationPeer.CreatePeerForElement(Find<Button>(w, "TimesheetCell_NASAR&D_20261005")));
            Assert.Equal("0.00h", zero.Value);
            Click(w, cell.Name!); Assert.Equal(Day, model.SelectedDate);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void AMinuteBlockIsNamedSelectableAndHasKeyboardCommands()
    {
        var model = Model(); model.Engine.Sessions.Add(new("2026-10-09", "NASA R&D", "09:00:00", "added", "1"));
        var w = new DashboardWindow(model); w.Show(); Settle(w);
        try
        {
            var block = Find<DisplayItem>(w, "TimelineEntry0"); Assert.Equal(ReviewViews.MinimumBlockWidth, block.Width);
            Assert.Contains("NASA R&D", AutomationProperties.GetName(block)); Assert.Contains("00:01", AutomationProperties.GetName(block));
            block.Focus(NavigationMethod.Tab); PressKey(block, Key.Enter); Settle(w);
            Assert.Single(model.SelectedRows); var selected = Find<DisplayItem>(w, "TimelineEntry0");
            Assert.True(Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(selected)).IsSelected);
            Assert.True(selected.IsFocused); PressKey(selected, Key.F10, KeyModifiers.Shift); Assert.True(selected.ContextMenu!.IsOpen);
            selected.ContextMenu.Close();
        }
        finally { w.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DenseStacksCanBeOpenedAndTheirMembersReachedByKeyboard(bool ink)
    {
        var model = Model(); model.SetDisplayStyle(ink ? DisplayStyle.Ink : DisplayStyle.Phosphor);
        for (var i = 0; i < 4; i++) model.Engine.Sessions.Add(new("2026-10-09", i % 2 == 0 ? "NASA R&D" : "API TEAM", $"09:{i * 5:00}:00", "added", "60"));
        var w = new DashboardWindow(model); w.Show(); Settle(w);
        try
        {
            var stack = w.GetVisualDescendants().OfType<DisplayItem>().Single(c => c.Name?.StartsWith("TimelineStack") == true);
            Assert.Contains("4 blocks", AutomationProperties.GetName(stack)); PressKey(stack, Key.Space); Settle(w);
            var members = w.GetVisualDescendants().OfType<DisplayItem>().Where(c => c.Name?.StartsWith("Stack_") == true).ToArray();
            Assert.Equal(4, members.Length); Assert.All(members, m => Assert.NotEmpty(AutomationProperties.GetName(m)));
            PressKey(members[2], Key.Enter); Settle(w); Assert.Equal("09:10:00", Assert.Single(model.SelectedRows).Start);
        }
        finally { w.Close(); model.SetDisplayStyle(DisplayStyle.Phosphor); }
    }

    [AvaloniaFact]
    public void CompactPillOpensAccountChoiceWithTheKeyboard()
    {
        var model = Model(); model.SetCompactPill(true); var w = new PillWindow(model); w.Show(); Settle(w);
        try { PressKey(w, Key.A, KeyModifiers.Alt); Settle(w); Assert.True(w.PanelOpen); Assert.Contains("NASA R&D", AutomationProperties.GetName(Find<Button>(w, "Account0"))); }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void LongLegacyNamesStayBoundedAndRetainTheirFullFilterIdentity()
    {
        var name = string.Join(" ", Enumerable.Repeat("NASA", 20));
        var model = new TimerModel(new TrackingEngine(clock: () => Day.AddHours(14)), new StoreSettings(new() { new(name) }, false, "Light", DayOfWeek.Monday)); model.CompleteSetup();
        model.Notes.AddNote("Legacy account note", name, null); var id = model.Notes.All.Single().Id;
        var w = new DashboardWindow(model); w.Show(); w.ShowNotes(); Settle(w);
        try
        {
            var account = Find<Button>(w, "PageRowAccount_" + id);
            var caption = account.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == name); Assert.InRange(caption.Bounds.Width, 1, 180);
            Click(w, account.Name!); var filter = Find<Button>(w, "NotesAccountFilter"); Assert.Contains(name, AutomationProperties.GetName(filter));
            Assert.InRange(filter.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == name).Bounds.Width, 1, 150);
            Click(w, "NotesAccountFilter"); var choice = Find<Button>(w, "NotesAccount_" + name.Replace(" ", "")); Assert.Contains(name, AutomationProperties.GetName(choice));
            Assert.Contains(choice.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == name);
        }
        finally { w.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TallPillModesExposeTheirValueAndDirectMenuChoices(bool ink)
    {
        var model = Model(); model.SetDisplayStyle(ink ? DisplayStyle.Ink : DisplayStyle.Phosphor); model.SetTallPill(true);
        model.Notes.AddMeeting(Day, TimeSpan.FromHours(15), TimeSpan.FromHours(16), "NASA Review", null, RepeatChoice.None); model.SetPillLower(PillLower.Next);
        var w = new PillWindow(model); w.Show(); Settle(w);
        try
        {
            var lower = Find<Button>(w, "PillLowerDisplay"); var value = Assert.IsAssignableFrom<IValueProvider>(ControlAutomationPeer.CreatePeerForElement(lower)); Assert.Contains("NASA Review", value.Value);
            var options = lower.ContextMenu!.ItemsSource!.OfType<MenuItem>().ToArray(); Assert.Equal(LowerDisplay.Cycle.Count, options.Length);
            Assert.Single(options.Where(m => m.IsChecked)); options.Single(m => Equals(m.Tag, PillLower.Week)).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Settle(w);
            Assert.Equal(PillLower.Week, model.PillLower); Assert.Contains("Week of", Assert.IsAssignableFrom<IValueProvider>(ControlAutomationPeer.CreatePeerForElement(lower)).Value);
        }
        finally { w.Close(); model.SetDisplayStyle(DisplayStyle.Phosphor); }
    }

    [AvaloniaTheory]
    [InlineData("Light", false)]
    [InlineData("Dark", false)]
    [InlineData("Light", true)]
    [InlineData("Dark", true)]
    public void PresentationRendersInBothMaterialsAndThemes(string theme, bool ink)
    {
        var model = Model(); model.SetTheme(theme); model.SetDisplayStyle(ink ? DisplayStyle.Ink : DisplayStyle.Phosphor);
        model.Engine.Sessions.Add(new("2026-10-09", "NASA R&D", "09:00:00", "added", "90"));
        var w = new DashboardWindow(model); w.Show(); Settle(w);
        try
        {
            Click(w, "WeekOverviewTab"); Click(w, "WeekAccount_NASAR&D");
            var path = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR");
            void Shot(string suffix) { if (path is null) return; Directory.CreateDirectory(path); using var frame = w.CaptureRenderedFrame(); frame?.Save(Path.Combine(path, $"presentation-{theme}-{ink}-{suffix}.png")); }
            Shot("week"); Click(w, "WeekKeySheet"); Shot("timesheet");
            var instrument = Find<WeekInstrument>(w, WeekInstrument.PanelName);
            Assert.InRange(instrument.GetVisualDescendants().OfType<Button>().Count(), 1, 100);
        }
        finally { w.Close(); model.SetDisplayStyle(DisplayStyle.Phosphor); model.SetTheme("Light"); }
    }
}

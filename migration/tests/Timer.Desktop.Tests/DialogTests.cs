using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Desktop;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

// Pop-ups sit over a blurred dashboard, the messages float above the buttons, and a selected block shows in its left edge.
public sealed class DialogTests
{
    private static readonly DateTime Day = new(2026, 10, 12);
    private static void Settle(Window window) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static TimerModel Model(out TrackingEngine engine)
    {
        engine = new TrackingEngine(clock: () => Day.AddHours(16));
        engine.Sessions.Add(new("2026-10-12", "Account one", "09:00:00", "recorded", "30.00"));
        engine.Sessions.Add(new("2026-10-12", "Account two", "10:00:00", "recorded", "30.00"));
        var model = new TimerModel(engine, new StoreSettings(new() { new("Account one"), new("Account two") }, false, ThemeLibrary.Default, DayOfWeek.Monday));
        model.ChangeDate((Day - model.SelectedDate.Date).Days);
        return model;
    }
    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    [AvaloniaFact]
    public void ADialogOpensOverTheDashboardWhichCannotBeClickedAndEscapeAndTheCrossTakeItAway()
    {
        var model = Model(out var engine); var dashboard = new DashboardWindow(model); dashboard.Show(); Settle(dashboard);
        var addTime = Find<Button>(dashboard, "AddTime");
        var centre = addTime.TranslatePoint(new Point(addTime.Bounds.Width / 2, addTime.Bounds.Height / 2), dashboard)!.Value;
        Assert.True(dashboard.InputHitTest(centre) is Visual before && before.GetSelfAndVisualAncestors().Contains(addTime)); // Reachable before.
        model.OpenEntry(engine.Sessions[0]); Settle(dashboard);
        var entry = Assert.Single(dashboard.Dialogs.Dialogs.OfType<EntryDialog>());
        Assert.True(dashboard.Dialogs.Any);
        Assert.False(dashboard.InputHitTest(centre) is Visual after && after.GetSelfAndVisualAncestors().Contains(addTime)); // The dialog layer takes the click.
        Assert.NotNull(Find<Border>(dashboard, "DialogCard")); Assert.NotNull(Find<Border>(dashboard, "DialogScrim"));
        // Opening it again for the same entry does not stack a second one.
        model.OpenEntry(engine.Sessions[0]); Settle(dashboard); Assert.Single(dashboard.Dialogs.Dialogs);
        Find<Button>(dashboard, "CloseDialog").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(dashboard);
        Assert.False(entry.IsVisible); Assert.False(dashboard.Dialogs.Any);
        Assert.True(dashboard.InputHitTest(centre) is Visual back && back.GetSelfAndVisualAncestors().Contains(addTime)); // Clickable again.
        model.OpenEntry(engine.Sessions[0]); Settle(dashboard); Assert.True(dashboard.Dialogs.Any);
        dashboard.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Settle(dashboard);
        Assert.False(dashboard.Dialogs.Any); // Escape took the dialog away first.
        dashboard.Close();
    }

    [AvaloniaFact]
    public void EscapeClearsTheSelectionOnlyWhenNoDialogIsOpenAndDialogsStack()
    {
        var model = Model(out var engine); var dashboard = new DashboardWindow(model); dashboard.Show(); Settle(dashboard);
        model.ToggleSelection(new[] { engine.Sessions[0] });
        dashboard.Dialogs.Show(new TidyDialog(model)); Settle(dashboard);
        dashboard.Dialogs.Show(new ExportDialog(model)); Settle(dashboard);
        Assert.Equal(2, dashboard.Dialogs.Dialogs.Count);
        dashboard.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Assert.IsType<TidyDialog>(Assert.Single(dashboard.Dialogs.Dialogs));
        Assert.Single(model.SelectedRows); // Still selected.
        dashboard.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Assert.False(dashboard.Dialogs.Any); Assert.Single(model.SelectedRows);
        dashboard.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Assert.Empty(model.SelectedRows); // Now it clears the selection.
        dashboard.Close();
    }

    [AvaloniaFact]
    public void AMessageFloatsAboveTheButtonsAndNeverMovesThem()
    {
        var model = Model(out _); var dashboard = new DashboardWindow(model); dashboard.Show(); Settle(dashboard);
        double Top(string name) { var c = Find<Button>(dashboard, name); return c.TranslatePoint(new Point(0, 0), dashboard)!.Value.Y; }
        var bar = Find<Border>(dashboard, "MessageBar"); Assert.False(bar.IsVisible);
        var addTime = Top("AddTime"); var undo = Top("UndoEdit");
        model.ExplainRunning(); Settle(dashboard); // "This block is still recording..."
        Assert.True(bar.IsVisible); Assert.Contains("Still recording", Find<TextBlock>(dashboard, "MessageText").Text);
        Assert.Equal(addTime, Top("AddTime"), 0.5); Assert.Equal(undo, Top("UndoEdit"), 0.5); // Nothing moved.
        var barBottom = bar.TranslatePoint(new Point(0, bar.Bounds.Height), dashboard)!.Value.Y;
        Assert.True(barBottom <= addTime, $"The message ends at {barBottom} but the buttons start at {addTime}."); // Above the buttons, not over them.
        Find<Button>(dashboard, "DismissMessage").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(dashboard);
        Assert.False(bar.IsVisible); Assert.Null(model.EditError);
        dashboard.Close();
    }

    [AvaloniaFact]
    public void TheMessageBarIsHiddenWhenThereIsNothingToSay()
    {
        var model = Model(out _); var dashboard = new DashboardWindow(model); dashboard.Show(); Settle(dashboard);
        var bar = (MessageBar)Find<Border>(dashboard, "MessageBar");
        Assert.Null(bar.Text); Assert.False(bar.IsVisible);
        dashboard.Close();
    }

    [AvaloniaFact]
    public void ASelectedBlockShowsInItsLeftEdgeInTheAccentColourAndHasNoOutline()
    {
        var model = Model(out var engine); var dashboard = new DashboardWindow(model); dashboard.Show(); Settle(dashboard);
        Border Edge(int index) => Find<Border>(dashboard, "TimelineEntry" + index).GetVisualDescendants().OfType<Border>().First(b => b.Name == "BlockEdge");
        var plain = Edge(0); Assert.DoesNotContain("selectedEdge", plain.Classes); Assert.Equal(5, plain.Bounds.Width, 0.5);
        model.ToggleSelection(new[] { engine.Sessions[0] }); Settle(dashboard);
        var card = Find<Border>(dashboard, "TimelineEntry0"); var edge = Edge(0);
        Assert.Contains("selectedEdge", edge.Classes); Assert.Equal(8, edge.Bounds.Width, 0.5); // Wider, in the accent colour.
        Assert.Equal(new Thickness(0), card.BorderThickness); // No outline round the block.
        Assert.True(dashboard.TryFindResource("Accent", dashboard.ActualThemeVariant, out var found));
        var accent = Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(found).Color;
        Assert.Equal(accent, Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(edge.Background).Color);
        Assert.DoesNotContain("selectedEdge", Edge(1).Classes); // The other block is untouched.
        dashboard.Close();
    }

    [AvaloniaFact]
    public void NameAndBudgetFromThePillBringsUpTheDashboardAndShowsTheAccountsDialogThere()
    {
        var model = Model(out _); var windows = new WindowCoordinator(model, () => { });
        try
        {
            windows.Pill.Show(); Assert.False(windows.Dashboard.IsVisible);
            model.OpenAccounts(); Settle(windows.Dashboard);
            Assert.True(windows.Dashboard.IsVisible); Assert.False(windows.Pill.IsVisible); // The dashboard came up first.
            Assert.IsType<AccountsDialog>(Assert.Single(windows.Dashboard.Dialogs.Dialogs));
            model.OpenAccounts(); Settle(windows.Dashboard); Assert.Single(windows.Dashboard.Dialogs.Dialogs); // Asking again does not stack a second.
            windows.Dashboard.Dialogs.CloseTop(); Assert.False(windows.Dashboard.Dialogs.Any);
        }
        finally { windows.Quit(); }
    }

    [AvaloniaFact]
    public void TheSettingsPageOpensAccountsHoursAndThemesAsDialogsAndTheThemeEditorStillAsksAboutUnsavedEdits()
    {
        var model = Model(out _); var dashboard = new DashboardWindow(model); dashboard.Show(); Settle(dashboard);
        dashboard.OpenCategory("accounts"); Settle(dashboard);
        Assert.IsType<SettingsDialog>(Assert.Single(dashboard.Dialogs.Dialogs)); Assert.NotNull(Find<Button>(dashboard, "SaveAccounts")); // The accounts are edited in the page itself.
        dashboard.OpenCategory("appearance");
        Find<Button>(dashboard, "EditThemes").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(dashboard);
        var themes = Assert.IsType<ThemesDialog>(dashboard.Dialogs.Dialogs[^1]); Assert.Equal(2, dashboard.Dialogs.Dialogs.Count); // Over the settings.
        // A close that a dialog refuses leaves it open, which is how the theme editor asks about unsaved edits.
        var refuse = true; themes.Closing += (_, e) => e.Cancel = refuse;
        Assert.True(dashboard.Dialogs.CloseTop()); Assert.True(themes.IsVisible); Assert.True(dashboard.Dialogs.Any);
        refuse = false; dashboard.Dialogs.CloseTop(); Assert.Single(dashboard.Dialogs.Dialogs); dashboard.Dialogs.CloseTop(); Assert.False(dashboard.Dialogs.Any);
        dashboard.Close();
    }

    [AvaloniaFact]
    public void TheCrossAndTitleSitInAHeaderAboveTheContentSoAScrollBarNeverRunsUnderTheCross()
    {
        var model = Model(out var engine); var dashboard = new DashboardWindow(model); dashboard.Show(); Settle(dashboard);
        var accounts = new AccountsDialog(model); dashboard.Dialogs.Show(accounts); Settle(dashboard);
        Assert.Equal("Accounts and budgets", Find<TextBlock>(dashboard, "DialogTitle").Text);
        var close = Find<Button>(dashboard, "CloseDialog");
        var crossBottom = close.TranslatePoint(new Point(0, close.Bounds.Height), dashboard)!.Value.Y;
        var contentTop = accounts.TranslatePoint(new Point(0, 0), dashboard)!.Value.Y;
        Assert.True(contentTop >= crossBottom - 0.5, $"The content starts at {contentTop} but the cross ends at {crossBottom}.");
        // Only one copy of the title: the dialog's own content no longer repeats it.
        Assert.Single(dashboard.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Text == "Accounts and budgets"));
        dashboard.Dialogs.CloseTop(); dashboard.Close();
    }

    [AvaloniaFact]
    public void ButtonsInDialogsWearTheDashboardsOutlinedLookAndTheMainActionHasAnAccentOutline()
    {
        var model = Model(out var engine); var dashboard = new DashboardWindow(model); dashboard.Show(); Settle(dashboard);
        dashboard.Dialogs.Show(new EntryDialog(model, engine.Sessions[0], engine.Sessions[0].From, engine.Sessions[0].To)); Settle(dashboard);
        var save = Find<Button>(dashboard, "SaveEntry"); var delete = Find<Button>(dashboard, "DeleteEntry");
        var cancel = dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Cancel");
        foreach (var button in new[] { save, delete, cancel }) Assert.True(button.Classes.Contains("toolButton") && button.Classes.Contains("quiet"), $"{button.Content} is not in the dashboard's style.");
        Assert.Contains("mainAction", save.Classes); Assert.DoesNotContain("mainAction", cancel.Classes); Assert.DoesNotContain("mainAction", delete.Classes);
        var addTime = Find<Button>(dashboard, "AddTime"); // The dashboard's own button shares the class, so the two match.
        Assert.Contains("toolButton", addTime.Classes);
        dashboard.Dialogs.CloseTop(); dashboard.Close();
    }
}

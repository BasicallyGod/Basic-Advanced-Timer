using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Xunit;

namespace Timer.Desktop.Tests;

// The seven settings sections, the pictures for choices that change how something looks, and the plain controls for the rest.
public sealed class SettingsRedesignTests
{
    private static (DashboardWindow Window, SettingsView View, TimerModel Model) Open()
    {
        var model = TimerModel.CreateDemo(); var window = new DashboardWindow(model); window.Show(); Dispatcher.UIThread.RunJobs();
        window.OpenSettings(); Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame();
        return (window, window.GetVisualDescendants().OfType<SettingsView>().Single(), model);
    }
    private static T Find<T>(Visual root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().First(c => c.Name == name);
    private static void Press(Visual root, string name) { Find<Button>(root, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }

    [AvaloniaFact]
    public void ThereAreSevenSectionsEachWithAnIconAndTheFirstIsLook()
    {
        var (window, view, _) = Open();
        try
        {
            foreach (var name in new[] { "Look", "Pill", "Alerts", "Day", "Accounts", "Export", "About" })
            {
                var nav = Find<Button>(view, "Settings" + name);
                Assert.Single(nav.GetVisualDescendants().OfType<IconGlyph>()); // An icon beside every name.
            }
            Assert.Equal("Look", Find<TextBlock>(view, "SettingsTitle").Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ThePillStylePicturesChangeTheModelAndShowWhichIsChosen()
    {
        var (window, view, model) = Open();
        try
        {
            view.ShowSection("pill"); Dispatcher.UIThread.RunJobs();
            Assert.Contains("selected", Find<Button>(view, "StyleFull").Classes);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Button>(), b => b.Name is "DigitsPlain" or "DigitsSegments");
            Press(view, "StyleCompact"); Assert.True(model.CompactPill); Assert.Contains("selected", Find<Button>(view, "StyleCompact").Classes); Assert.DoesNotContain("selected", Find<Button>(view, "StyleFull").Classes);
            Press(view, "StyleFull"); Assert.False(model.CompactPill);
            // Tooltips-style switches stay plain ticks: nothing about them is drawn.
            Assert.NotNull(Find<CheckBox>(view, "PillOnTop")); Assert.NotNull(Find<CheckBox>(view, "DashboardOnTop"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ThePillSizePreviewFollowsTheSliderAndAutomaticDimsIt()
    {
        var (window, view, model) = Open();
        try
        {
            view.ShowSection("pill"); Dispatcher.UIThread.RunJobs();
            var slider = Find<Slider>(view, "PillSize"); var automatic = Find<CheckBox>(view, "PillSizeAutomatic"); var note = Find<TextBlock>(view, "PillSizeNote");
            Assert.True(automatic.IsChecked); Assert.False(slider.IsEnabled); Assert.Equal("Fits the screen", note.Text);
            automatic.IsChecked = false; automatic.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.True(slider.IsEnabled); Assert.NotNull(model.PillScalePercent); Assert.Contains("per cent", note.Text);
            Assert.NotNull(Find<Border>(view, "PillSizePreview").Child); // The preview is drawn.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void WindowShadowsAreTwoPicturesAndTooltipsAreAPlainTick()
    {
        var (window, view, model) = Open();
        try
        {
            Press(view, "ShadowsOff"); Assert.False(model.Shadows); Assert.Contains("selected", Find<Button>(view, "ShadowsOff").Classes);
            Press(view, "ShadowsOn"); Assert.True(model.Shadows); Assert.Contains("selected", Find<Button>(view, "ShadowsOn").Classes);
            Assert.NotNull(Find<CheckBox>(view, "ShowTooltips"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheWeekStartsOnIsChosenInSettingsAndTheModelFollows()
    {
        var (window, view, model) = Open();
        try
        {
            view.ShowSection("day"); Dispatcher.UIThread.RunJobs();
            var box = Find<ChoiceStrip>(view, "WeekStartsOn"); Assert.Equal((int)DayOfWeek.Monday == 1 ? 0 : 0, box.SelectedIndex);
            box.SelectedIndex = 6; Assert.Equal(DayOfWeek.Sunday, model.WeekStartsOn);
            model.SetWeekStart(DayOfWeek.Wednesday); Dispatcher.UIThread.RunJobs(); Assert.Equal(2, box.SelectedIndex); // Changed elsewhere: the box follows.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheAccountsSectionSummarisesAndListsEveryAccountAndOpensTheDialog()
    {
        var (window, view, model) = Open();
        try
        {
            view.ShowSection("accounts"); Dispatcher.UIThread.RunJobs();
            var summary = Find<TextBlock>(view, "AccountsSummary").Text!;
            Assert.Contains("accounts", summary); Assert.Contains("with budgets", summary); Assert.Contains("hidden from the tracker", summary);
            Assert.Contains(view.GetVisualDescendants().OfType<Grid>(), g => g.Name == "SettingsAccount_Archive" && g.Opacity < 1); // The hidden account is dimmed.
            Press(view, "EditAccounts"); Assert.IsType<AccountsDialog>(Assert.Single(window.Dialogs.Dialogs));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AlertsListsWhatCountsAndSettingsShowsAllSectionsWithoutASearchBox()
    {
        var (window, view, _) = Open();
        try
        {
            view.ShowSection("alerts"); Dispatcher.UIThread.RunJobs();
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "A budget is at 80 per cent or spent");
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBox>(), t => t.Name == "SettingsSearch");
            foreach (var section in new[] { "Look", "Pill", "Alerts", "Day", "Accounts", "Export", "About" })
                Assert.True(Find<Button>(view, "Settings" + section).IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void CopyingTheHoursTableIsDimmedInMonthWithTheReasonBesideIt()
    {
        var (window, view, _) = Open();
        try
        {
            view.ShowSection("export"); Dispatcher.UIThread.RunJobs();
            var note = Find<TextBlock>(view, "CopyHoursNote"); Assert.False(note.IsVisible);
            Press(view, "ExportMonthChoice"); Assert.False(Find<Button>(view, "CopyHours").IsEnabled); Assert.True(note.IsVisible);
            Press(view, "ExportWeekChoice"); Assert.True(Find<Button>(view, "CopyHours").IsEnabled); Assert.False(note.IsVisible);
        }
        finally { window.Close(); }
    }
}

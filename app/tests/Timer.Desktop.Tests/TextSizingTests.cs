using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Desktop;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class TextSizingTests
{
    private static void Settle(Window w) { for (var i = 0; i < 3; i++) { Dispatcher.UIThread.RunJobs(); using var frame = w.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Preview(Window window, string name)
    {
        var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR"); if (folder is null) return;
        Directory.CreateDirectory(folder); using var frame = window.CaptureRenderedFrame(); frame?.Save(Path.Combine(folder, name + ".png"));
    }
    [Theory]
    [InlineData(100, 1)] [InlineData(150, 1.5)] [InlineData(225, 2.25)] [InlineData(0, 1)] [InlineData(10000, 1)]
    public void WindowsPreferenceIsBoundedAndDistinctFromDpi(int percent, double expected) => Assert.Equal(expected, TextSizing.DecodeFactor(percent));

    [AvaloniaFact]
    public void OwnInheritedAndTemplateFontsScaleOnceAndRestoreTheirDesignSizes()
    {
        var own = Ui.Text("Own font", 12);
        var inherited = new TextBlock { Text = "Inherited font" };
        var parent = new StackPanel { Children = { inherited } }; parent.SetValue(TextElement.FontSizeProperty, 18);
        var box = new TextBox { Text = "12:30", Width = 118, Height = 38, FontSize = 13 };
        var window = new Window { Width = 700, Height = 500, Content = new StackPanel { Children = { own, parent, box } } }; window.Show(); Settle(window);
        try
        {
            using (TextSizing.ForTests(1.5))
            {
                Settle(window); Assert.Equal(18, own.FontSize); Assert.Equal(27, inherited.FontSize); Assert.Equal(19.5, box.FontSize);
                Assert.All(box.GetVisualDescendants().OfType<TextPresenter>(), p => Assert.Equal(box.FontSize, p.FontSize));
                Assert.Equal(177, box.Width); Assert.True(box.Height > 38);
                using (TextSizing.ForTests(2.25)) { Settle(window); Assert.Equal(27, own.FontSize); Assert.Equal(40.5, inherited.FontSize); }
                Assert.Equal(18, own.FontSize); Assert.Equal(27, inherited.FontSize);
            }
            Settle(window); Assert.Equal(12, own.FontSize); Assert.Equal(18, inherited.FontSize); Assert.Equal(118, box.Width); Assert.Equal(38, box.Height);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1.5)] [InlineData(2.25)]
    public void ColdCreationAndLaterFontChangesUseDesignSizes(double factor)
    {
        using var scale = TextSizing.ForTests(factor);
        var label = Ui.Text("Default-sized explicit label", 12);
        Assert.Equal(12, TextSizing.BaseFontSize(label));
        var box = new TextBox { FontSize = 13, Width = 118, Height = 38 };
        var window = new Window { Content = new StackPanel { Children = { label, box } } }; window.Show(); Settle(window);
        try
        {
            Assert.Equal(12 * factor, label.FontSize); Assert.Equal(13 * factor, box.FontSize);
            Assert.Equal(118 * factor, box.Width); Assert.Equal(38 + (factor - 1) * 13 * 1.5, box.Height);
            label.FontSize = 16; Settle(window); Assert.Equal(16 * factor, label.FontSize);
            Assert.Equal(16, TextSizing.BaseFontSize(label));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DetachedLayoutsStopFollowingAndReattachWithoutDuplicateCallbacks()
    {
        var calls = 0; var panel = new StackPanel(); TextSizing.Follow(panel, () => calls++);
        var window = new Window { Content = panel }; window.Show(); Settle(window);
        try
        {
            window.Content = null; var detached = calls;
            using (TextSizing.ForTests(1.5)) Assert.Equal(detached, calls);
            window.Content = panel; Settle(window); var attached = calls;
            using (TextSizing.ForTests(1.5)) Assert.Equal(attached + 1, calls);
            Assert.Equal(attached + 2, calls);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void LiveScalingRetainsFocusedDraftTextAndSelection()
    {
        var model = TimerModel.CreateDemo(() => new DateTime(2026, 11, 15, 14, 15, 0)); model.CompleteSetup();
        model.Notes.AddNote("Original", null, null); var id = model.Notes.All.Last().Id;
        var window = new DashboardWindow(model); window.Show(); model.SelectNote(id); Settle(window);
        try
        {
            var box = Find<TextBox>(window, "DetailsNoteWords"); box.Focus(); box.Text = "An unfinished focused draft"; box.SelectionStart = 3; box.SelectionEnd = 12;
            using (TextSizing.ForTests(1.5))
            {
                Settle(window); Assert.Same(box, Find<TextBox>(window, "DetailsNoteWords")); Assert.True(box.IsFocused); Assert.Equal("An unfinished focused draft", box.Text);
                Assert.Equal(3, box.SelectionStart); Assert.Equal(12, box.SelectionEnd); Assert.Equal("Original", model.Notes.Words(model.Notes.Find(id)!));
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public void InstrumentGeometryStaysFixedWhileOrdinaryWorkspaceTextGrows(bool ink)
    {
        var model = TimerModel.CreateDemo(() => new DateTime(2026, 11, 15, 14, 15, 0)); model.CompleteSetup(); model.SetDisplayStyle(ink ? DisplayStyle.Ink : DisplayStyle.Phosphor);
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var face = window.GetVisualDescendants().OfType<StatusFace>().Single(); var before = face.Bounds.Size;
            var heading = Find<TextBlock>(window, "SidebarAccountsHeading"); var font = heading.FontSize;
            using (TextSizing.ForTests(1.5)) { Settle(window); Assert.Equal(font * 1.5, heading.FontSize); Assert.Equal(before, face.Bounds.Size); }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, false, 1.5)] [InlineData(false, true, 1.5)] [InlineData(true, false, 1.5)] [InlineData(true, true, 1.5)]
    [InlineData(false, false, 2.25)] [InlineData(false, true, 2.25)] [InlineData(true, false, 2.25)] [InlineData(true, true, 2.25)]
    public void SettingsRenderAtLargerTextSizesInBothMaterials(bool dark, bool ink, double factor)
    {
        using var scale = TextSizing.ForTests(factor);
        var model = TimerModel.CreateDemo(() => new DateTime(2026, 11, 15, 14, 15, 0)); model.CompleteSetup(); model.SetTheme(dark ? "Dark" : "Light"); model.SetDisplayStyle(ink ? DisplayStyle.Ink : DisplayStyle.Phosphor);
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Find<Button>(window, "Settings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(window);
            Assert.Equal(22 * factor, Find<TextBlock>(window, "SettingsTitle").FontSize);
            var nav = Find<Button>(window, "SettingsDay");
            var navLabel = nav.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Day and time");
            Assert.True(navLabel.TranslatePoint(default, nav)!.Value.X + navLabel.Bounds.Width <= nav.Bounds.Width + 1);
            var effect = Find<TextBlock>(window, "InkStrengthNote"); Assert.True(effect.Bounds.Width >= 80 * factor - 1);
            var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR");
            if (folder is not null) { Directory.CreateDirectory(folder); using var frame = window.CaptureRenderedFrame(); frame?.Save(Path.Combine(folder, $"text-{factor}-{dark}-{ink}-settings.png")); }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1.5)] [InlineData(2.25)]
    public void OnboardingRemainsScrollableInsideTheWindow(double factor)
    {
        using var scale = TextSizing.ForTests(factor);
        var model = TimerModel.CreateDemo(() => new DateTime(2026, 11, 15, 14, 15, 0));
        var setup = new OnboardingView(model);
        var window = new Window { Width = 1000, Height = 700, Content = setup }; window.Show(); Settle(window);
        try
        {
            var card = Find<Border>(window, "SetupCard");
            Assert.True(card.Bounds.Width <= window.Bounds.Width - 31); Assert.True(card.Bounds.Height <= window.Bounds.Height - 31);
            Assert.IsType<ScrollViewer>(card.Child);
            var period = setup.GetVisualDescendants().OfType<ComboBox>().First(); Assert.Equal(100 * factor - 8, period.Bounds.Width, 1);
            var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR");
            if (folder is not null) { using var frame = window.CaptureRenderedFrame(); frame?.Save(Path.Combine(folder, $"text-{factor}-setup.png")); }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1.5)] [InlineData(2.25)]
    public void SettingsSectionsAndAccountDialogKeepTheirControlsWithinTheWorkspace(double factor)
    {
        using var scale = TextSizing.ForTests(factor);
        var model = TimerModel.CreateDemo(() => new DateTime(2026, 11, 15, 14, 15, 0)); model.CompleteSetup();
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Find<Button>(window, "Settings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(window);
            var page = window.GetVisualDescendants().OfType<SettingsView>().Single();
            foreach (var section in new[] { "look", "pill", "alerts", "day", "accounts", "export", "about" })
            {
                page.ShowSection(section); Settle(window); Preview(window, $"text-{factor}-{section}");
                var card = Find<Border>(window, "SettingsCard"); Assert.True(card.Bounds.Width > 300);
                foreach (var button in card.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible))
                    Assert.True(button.TranslatePoint(default, card)!.Value.X + button.Bounds.Width <= card.Bounds.Width + 1, section + ": " + button.Name);
                if (section == "pill") Assert.Equal(112 * factor, Find<TextBlock>(window, "PillSizeNote").Width);
            }
            page.ShowSection("accounts"); Settle(window);
            var summary = Find<TextBlock>(window, "AccountsSummary"); var edit = Find<Button>(window, "EditAccounts");
            Assert.True(edit.TranslatePoint(default, page)!.Value.Y >= summary.TranslatePoint(default, page)!.Value.Y + summary.Bounds.Height - 1);
            edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(window); Preview(window, $"text-{factor}-account-dialog");
            var dialog = window.GetVisualDescendants().OfType<AccountsDialog>().Single();
            var colour = dialog.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Colour");
            var tracker = dialog.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "In tracker");
            Assert.True(colour.TranslatePoint(default, dialog)!.Value.X + colour.Bounds.Width <= tracker.TranslatePoint(default, dialog)!.Value.X + 1);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1.5)] [InlineData(2.25)]
    public void CalendarAndReminderCardsGrowWithoutScalingTheirTimeInstrument(double factor)
    {
        using var scale = TextSizing.ForTests(factor);
        var model = TimerModel.CreateDemo(() => new DateTime(2026, 11, 15, 14, 15, 0)); model.CompleteSetup();
        var reminder = new ReminderPickerCard(model, "Test", null, _ => { }, () => { });
        var day = new ReminderPopup(() => model.Engine.Now, "", null, _ => { }, () => { });
        var calendar = new SoftDatePicker();
        var host = new Grid(); var window = new Window { Width = 1000, Height = 700, Content = host }; window.Show();
        try
        {
            foreach (var control in new Control[] { reminder, day, calendar })
            {
                host.Children.Clear(); host.Children.Add(control); Settle(window); Preview(window, $"text-{factor}-{control.GetType().Name}");
                Assert.True(control.Bounds.Width <= window.Bounds.Width);
            }
            Assert.Equal(ReminderPickerCard.CardWidth * factor, reminder.Width);
            Assert.Equal(ReminderPopup.DayOnlyWide * factor, day.Width); Assert.Equal(266 * factor, calendar.Width);
            Assert.Equal(RescheduleStrip.StripHeight, reminder.Picker.Strip.Height);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1)] [InlineData(1.5)] [InlineData(2.25)]
    public void SettingsKeyKeepsItsPositionAtEveryTextSize(double factor)
    {
        using var scale = TextSizing.ForTests(factor);
        var model = TimerModel.CreateDemo(); model.CompleteSetup(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var gear = Find<Button>(window, "Settings"); var before = gear.TranslatePoint(default, window)!.Value;
            gear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(window);
            Assert.Equal(before.Y, gear.TranslatePoint(default, window)!.Value.Y, 1);
            gear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(window);
            Find<Button>(window, "NotesTab").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(window);
            Assert.Equal(before.Y, gear.TranslatePoint(default, window)!.Value.Y, 1); Preview(window, $"text-{factor}-notes");
            var views = Find<Border>(window, "NotesViews");
            Assert.All(views.GetVisualDescendants().OfType<Button>(), button => Assert.True(button.TranslatePoint(default, views)!.Value.X + button.Bounds.Width <= views.Bounds.Width + 1));
            var filter = Find<Button>(window, "NotesAccountFilter").GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "All accounts");
            Assert.Equal(150 * factor, filter.MaxWidth);
        }
        finally { window.Close(); }
    }
}

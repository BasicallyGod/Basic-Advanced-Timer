using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Timer.Desktop.Tests;

/// <summary>Writes pictures of the Settings page and the calendar into TIMER_PREVIEW_DIR when it is set, so layout can be looked at. Does nothing otherwise.</summary>
public sealed class PreviewCaptureTests
{
    [AvaloniaFact]
    public void WriteRailIcons()
    {
        var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR"); if (folder is null) return;
        Directory.CreateDirectory(folder);
        var grid = new Avalonia.Controls.StackPanel { Orientation = Avalonia.Layout.Orientation.Vertical, Background = Avalonia.Media.Brushes.White };
        foreach (var seconds in new[] { 0.0, 0.7, 1.4, 2.0 })
        {
            var row = new Avalonia.Controls.StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 20, Margin = new Avalonia.Thickness(10) };
            foreach (var kind in new[] { IconKind.Timeline, IconKind.Week, IconKind.NotesList, IconKind.NotesList })
            {
                var glyph = new IconGlyph(kind) { PlaySeconds = kind == IconKind.NotesList && row.Children.Count == 3 ? seconds + 1.3 : seconds };
                row.Children.Add(new Avalonia.Controls.Viewbox { Width = 110, Height = 110, Child = glyph });
            }
            grid.Children.Add(row);
        }
        var window = new Window { Content = grid, Width = 560, Height = 520 }; window.Show(); Dispatcher.UIThread.RunJobs();
        try { using var f = window.CaptureRenderedFrame(); Dispatcher.UIThread.RunJobs(); using var g = window.CaptureRenderedFrame(); g?.Save(Path.Combine(folder, "rail-icons.png")); }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void WriteRecordingTimeline()
    {
        var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR"); if (folder is null) return;
        Directory.CreateDirectory(folder);
        foreach (var theme in new[] { "Light", "Dark" })
        {
            var model = TimerModel.CreateDemo(); model.SetTheme(theme); model.ToggleAccount(model.Accounts.First().Name);
            var window = new DashboardWindow(model); window.Show(); Dispatcher.UIThread.RunJobs();
            try { using var f = window.CaptureRenderedFrame(); Dispatcher.UIThread.RunJobs(); using var g = window.CaptureRenderedFrame(); g?.Save(Path.Combine(folder, theme + "-recording.png")); }
            finally { window.Close(); }
        }
    }

    [AvaloniaFact]
    public void WriteSidebarPages()
    {
        var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR"); if (folder is null) return;
        Directory.CreateDirectory(folder);
        foreach (var theme in new[] { "Light", "Dark" })
        {
            var model = TimerModel.CreateDemo(); model.SetTheme(theme);
            var window = new DashboardWindow(model); window.Show(); Dispatcher.UIThread.RunJobs();
            try
            {
                void Shot(string name) { Dispatcher.UIThread.RunJobs(); using var f = window.CaptureRenderedFrame(); Dispatcher.UIThread.RunJobs(); using var g = window.CaptureRenderedFrame(); g?.Save(Path.Combine(folder, $"sidebar-{theme}-{name}.png")); }
                void Press(string name) { window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
                foreach (var (page, tab) in new[] { ("timeline", "TimelineTab"), ("notes", "NotesTab"), ("week", "WeekOverviewTab") }) { Press(tab); Shot(page); }
                Press("TimelineTab"); model.SelectBlock(model.Engine.Sessions.Take(1).ToArray(), false); Shot("timeline-selected");
                model.SelectBlock(Array.Empty<Timer.Core.SessionRow>(), false);
                Press("NotesTab"); Press("NotesAccount_Accountone"); Shot("notes-filtered");
            }
            finally { window.Close(); }
        }
    }

    [AvaloniaFact]
    public void WriteRound20Pages()
    {
        var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR"); if (folder is null) return;
        Directory.CreateDirectory(folder);
        foreach (var theme in new[] { "Light", "Dark" })
        {
            var model = TimerModel.CreateDemo(); model.SetTheme(theme); model.ToggleAccount(model.Accounts.First().Name);
            var window = new DashboardWindow(model); window.Show(); Dispatcher.UIThread.RunJobs();
            try
            {
                void Shot(string name) { Dispatcher.UIThread.RunJobs(); using var f = window.CaptureRenderedFrame(); Dispatcher.UIThread.RunJobs(); using var g = window.CaptureRenderedFrame(); g?.Save(Path.Combine(folder, $"r20-{theme}-{name}.png")); }
                void Press(string name) { window.GetVisualDescendants().OfType<Button>().First(b => b.Name == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
                var due = model.Engine.Now.Date.AddHours(14);
                model.Notes.AddNote("Follow up on the new brief", "Account two", due); model.Notes.AddNote("Follow up on the new brief", "Account two", due); Dispatcher.UIThread.RunJobs();
                Press("TimelineTab"); Shot("timeline");
                var pin = window.GetVisualDescendants().OfType<Border>().First(b => b.Name?.StartsWith("TimelinePinMark_") == true);
                var at = pin.TranslatePoint(new Point(pin.Bounds.Width / 2, pin.Bounds.Height / 2), window)!.Value;
                Avalonia.Headless.HeadlessWindowExtensions.MouseMove(window, at); Dispatcher.UIThread.RunJobs(); Shot("pin-hover");
                Avalonia.Headless.HeadlessWindowExtensions.MouseMove(window, new Point(5, 5)); Dispatcher.UIThread.RunJobs();
                Press("AddNote");
                window.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "NoteField").Text = "Follow #Accountt"; Dispatcher.UIThread.RunJobs();
                Press("Suggest_Accounttwo");
                window.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "NoteField").Text = "Follow up on the new brief"; Dispatcher.UIThread.RunJobs();
                Press("FormBell"); Press("ReminderSet"); Shot("note-form");
                Press("AddMeeting"); Shot("meeting-form");
                Press("NotesTab"); Shot("notes"); Press("PageKindMeeting"); Shot("notes-meeting");
                Press("WeekOverviewTab"); Shot("week");
            }
            finally { window.Close(); }
        }
    }

    [AvaloniaFact]
    public void WriteSunsetSettings()
    {
        var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR"); if (folder is null) return;
        Directory.CreateDirectory(folder);
        foreach (var theme in new[] { "Light", "Dark" })
        {
            var model = TimerModel.CreateDemo(); model.SetTheme(theme);
            var window = new DashboardWindow(model); window.Show(); Dispatcher.UIThread.RunJobs();
            try
            {
                void Shot(string name) { Dispatcher.UIThread.RunJobs(); using var f = window.CaptureRenderedFrame(); Dispatcher.UIThread.RunJobs(); using var g = window.CaptureRenderedFrame(); g?.Save(Path.Combine(folder, $"sun-{theme}-{name}.png")); }
                void Press(string name) { window.GetVisualDescendants().OfType<Button>().First(b => b.Name == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
                Press("Settings"); Press("ThemeSunset"); model.SetSunPlace("London"); Shot("city");
                model.SetSunPlace(SunTimes.ByHand); Shot("hand");
            }
            finally { window.Close(); }
        }
    }

    [AvaloniaFact]
    public void WriteFinishFlag()
    {
        var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR"); if (folder is null) return;
        Directory.CreateDirectory(folder);
        foreach (var theme in new[] { "Light", "Dark" })
        {
            var day = new DateTime(2026, 10, 9); var now = day.AddHours(15);
            var engine = new Timer.Core.TrackingEngine(clock: () => now);
            var model = new TimerModel(engine, new Timer.Storage.StoreSettings(new() { new("Account one"), new("Account two"), new("Account three") }, false, theme, DayOfWeek.Monday)); model.CompleteSetup();
            model.SetShifts(new() { [DayOfWeek.Friday] = new(TimeSpan.FromHours(9), TimeSpan.FromHours(17.5)) });
            engine.AddSession("Account one", day.AddHours(9), day.AddHours(10.5)); engine.AddSession("Account two", day.AddHours(10.5), day.AddHours(12));
            engine.AddSession(Timer.Core.BreakAccount.Name, day.AddHours(12), day.AddHours(12.5)); engine.AddSession("Account one", day.AddHours(12.5), day.AddHours(15));
            engine.FinishDay(); now = day.AddHours(16.75); engine.AddSession("Account three", day.AddHours(15 + 1.0 / 3), day.AddHours(16.5));
            var window = new DashboardWindow(model); window.Show(); Dispatcher.UIThread.RunJobs();
            try { using var f = window.CaptureRenderedFrame(); Dispatcher.UIThread.RunJobs(); using var g = window.CaptureRenderedFrame(); g?.Save(Path.Combine(folder, $"finish-{theme}.png")); }
            finally { window.Close(); }
        }
    }

    public static void SetSwollen(Border ring, double opacity)
    {
        ring.SetValue(Avalonia.Layout.Layoutable.WidthProperty, 20.0, Avalonia.Data.BindingPriority.Animation); ring.SetValue(Avalonia.Layout.Layoutable.HeightProperty, 28.0, Avalonia.Data.BindingPriority.Animation);
        ring.SetValue(Border.CornerRadiusProperty, new CornerRadius(10), Avalonia.Data.BindingPriority.Animation); ring.SetValue(Visual.OpacityProperty, opacity, Avalonia.Data.BindingPriority.Animation);
    }

    [AvaloniaFact]
    public void WriteRailLights()
    {
        var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR"); if (folder is null) return;
        Directory.CreateDirectory(folder);
        foreach (var scale in new[] { 1.0, .8 })
        {
            var model = TimerModel.CreateDemo(); model.SetTheme("Light");
            var window = new DashboardWindow(model); window.Show(); Dispatcher.UIThread.RunJobs();
            try
            {
                var ring = window.GetVisualDescendants().OfType<Border>().First(b => b.Name == "SidebarReminderRing"); SetSwollen(ring, .55);
                using var f = window.CaptureRenderedFrame(); Dispatcher.UIThread.RunJobs(); using var g = window.CaptureRenderedFrame(); g?.Save(Path.Combine(folder, $"rail-lights-{scale}.png"));
            }
            finally { window.Close(); }
        }
    }

    [AvaloniaFact]
    public void WritePillLights()
    {
        var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR"); if (folder is null) return;
        Directory.CreateDirectory(folder);
        foreach (var horizontal in new[] { false, true })
        {
            var model = TimerModel.CreateDemo(); model.SetTheme("Light"); model.SetPillHorizontal(horizontal);
            var pill = new PillWindow(model); pill.Show(); Dispatcher.UIThread.RunJobs();
            try
            {
                // Animations do not tick here, so the ring is set part way through its swell by hand.
                var ring = pill.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PillReminderRing"); SetSwollen(ring, .55);
                using var f = pill.CaptureRenderedFrame(); Dispatcher.UIThread.RunJobs(); using var g = pill.CaptureRenderedFrame(); g?.Save(Path.Combine(folder, (horizontal ? "h" : "v") + "-pill-lights.png"));
            }
            finally { pill.Close(); }
        }
    }

    [AvaloniaFact]
    public void WriteHangingCards()
    {
        var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR"); if (folder is null) return;
        Directory.CreateDirectory(folder);
        foreach (var theme in new[] { "Light", "Dark" })
        {
            var model = TimerModel.CreateDemo(); model.SetTheme(theme); model.SetPillHorizontal(true);
            var pill = new PillWindow(model); pill.Show(); Dispatcher.UIThread.RunJobs();
            try
            {
                void Save(Window host, string name) { host.Show(); Dispatcher.UIThread.RunJobs(); using var f = host.CaptureRenderedFrame(); Dispatcher.UIThread.RunJobs(); using var g = host.CaptureRenderedFrame(); g?.Save(Path.Combine(folder, theme + "-" + name + ".png")); host.Close(); }
                // The list, hung with its point under the account's box, under a strip drawn like the pill's lower edge.
                var hung = HangingCard.Hang(pill.MakeDropdownCard(), 300 - 139, "PillDropdownPoint", "PillBody");
                var strip = new Border { Height = 40, Width = 470, CornerRadius = new CornerRadius(0, 0, 36, 36), BorderThickness = new Thickness(1.5), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, -HangingCard.Room.Top + 2) };
                strip.Bind(Border.BorderBrushProperty, strip.GetResourceObservable("Line")); strip.Bind(Border.BackgroundProperty, strip.GetResourceObservable("PillBody"));
                var stack = new StackPanel { Children = { strip, new Border { Margin = new Thickness(40 - HangingCard.Room.Left, 11 - HangingCard.Room.Top + 14, 0, 0), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, Child = hung } } };
                Save(new Window { Width = 520, Height = 520, RequestedThemeVariant = pill.ActualThemeVariant, Content = stack }, "hung-dropdown");
                // The bell's card, with the point set past the corner as the dashboard places it.
                var shift = HangingCard.ShiftForCorner(19, 18);
                var bell = HangingCard.Hang(new NotificationsFlyout(model), 19 + shift);
                Save(new Window { Width = 440, Height = 400, RequestedThemeVariant = pill.ActualThemeVariant, Content = new Border { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, Child = bell } }, "hung-bell");
            }
            finally { pill.Close(); }
        }
    }

    [AvaloniaFact]
    public void WritePillShapeChoice()
    {
        var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR"); if (folder is null) return;
        Directory.CreateDirectory(folder);
        foreach (var theme in new[] { "Light", "Dark" })
        {
            var fresh = new TimerModel(new Timer.Core.TrackingEngine(clock: () => new DateTime(2026, 10, 9, 10, 0, 0)), new Timer.Storage.StoreSettings(new() { new("Account one"), new("Account two") }, false, theme, DayOfWeek.Monday, SetupDone: false));
            var window = new DashboardWindow(fresh); window.Show(); Dispatcher.UIThread.RunJobs();
            try
            {
                void Press(string name) { window.GetVisualDescendants().OfType<Button>().First(b => b.Name == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
                void Shot(string name) { Dispatcher.UIThread.RunJobs(); using var f = window.CaptureRenderedFrame(); Dispatcher.UIThread.RunJobs(); using var g = window.CaptureRenderedFrame(); g?.Save(Path.Combine(folder, theme + "-" + name + ".png")); }
                for (var i = 0; i < 3; i++) Press("SetupNext");
                Shot("setup-shape-vertical"); Press("SetupHorizontal"); Shot("setup-shape-horizontal");
                Press("SetupNext"); Shot("setup-ready-horizontal");
                Press("SetupSkip"); Press("Settings"); Shot("settings-shape");
            }
            finally { window.Close(); }
        }
    }

    [AvaloniaFact]
    public void WritePreviews()
    {
        var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR"); if (folder is null) return;
        Directory.CreateDirectory(folder);
        foreach (var theme in new[] { "Light", "Dark" })
        {
            var model = TimerModel.CreateDemo(); model.SetTheme(theme);
            var window = new DashboardWindow(model); window.Show(); Dispatcher.UIThread.RunJobs();
            try
            {
                void Shot(string name) { Dispatcher.UIThread.RunJobs(); using var f = window.CaptureRenderedFrame(); Dispatcher.UIThread.RunJobs(); using var g = window.CaptureRenderedFrame(); g?.Save(Path.Combine(folder, theme + "-" + name + ".png")); }
                void Press(string name) { window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
                Press("TimelineTab"); Shot("timeline"); Press("CardKindMeetings"); Shot("timeline-meetings"); Press("CardKindNotes");
                model.Tell("Merged · 00:05 gap added"); Shot("toast"); model.Refuse("End: try 10:30"); Shot("toast-refused"); model.Tell("Undone");
                var firstBlock = model.Engine.Sessions.First(); model.SelectBlock(new[] { firstBlock }, false); Dispatcher.UIThread.RunJobs();
                var copyButton = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CopySelected"); ToolTip.SetIsOpen(copyButton, true); Shot("tooltip"); ToolTip.SetIsOpen(copyButton, false); model.ClearSelection();
                Press("Notifications"); Shot("bell"); Press("Notifications"); Press("DatePick"); Shot("datepick"); Press("DatePick");
                Press("WeekOverviewTab"); Shot("week"); Press("WeekTable"); Shot("weektable"); Press("WeekChart"); Press("NotesTab"); Shot("notes"); Press("TimelineTab");
                window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Settings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Shot("settings");
                var cal = new Window { Width = 360, Height = 380, Content = new Border { Padding = new Avalonia.Thickness(16), Child = new Calendar { SelectedDate = new DateTime(2026, 10, 16), DisplayDate = new DateTime(2026, 10, 16) } } };
                cal.RequestedThemeVariant = window.RequestedThemeVariant; cal.Show(); Dispatcher.UIThread.RunJobs(); using var c1 = cal.CaptureRenderedFrame(); Dispatcher.UIThread.RunJobs(); using var c2 = cal.CaptureRenderedFrame(); c2?.Save(Path.Combine(folder, theme + "-calendar.png")); cal.Close();
            }
            finally { window.Close(); }
        }
    }
}

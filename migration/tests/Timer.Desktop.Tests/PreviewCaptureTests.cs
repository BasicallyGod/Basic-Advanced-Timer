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
                Press("TimelineTab"); Shot("timeline");
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

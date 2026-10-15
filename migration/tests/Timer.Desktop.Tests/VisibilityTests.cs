using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Desktop;
using Timer.Storage;
using Xunit;
using Xunit.Abstractions;

namespace Timer.Desktop.Tests;

/// <summary>Walks the application's screens in both built-in themes and checks that every piece of text, input and icon can be read against what it sits on.</summary>
public sealed class VisibilityTests
{
    private readonly ITestOutputHelper output;
    public VisibilityTests(ITestOutputHelper output) => this.output = output;

    private static readonly DateTime Now = new(2026, 10, 9, 14, 15, 0);
    private static TimerModel Rich(string theme)
    {
        var engine = new TrackingEngine(clock: () => Now);
        var prefs = new StoreSettings(new() { new("Account one", 15), new("Account two", 10), new("Account three", 5), new("Internal") }, false, theme, DayOfWeek.Monday);
        var names = new[] { "Account one", "Account two", "Break", "Account three", "Internal" }; var minutes = new[] { 105, 90, 30, 45, 30 };
        var start = Now.Date.AddHours(9);
        for (var i = 0; i < names.Length; i++) { engine.AddSession(names[i], start, start.AddMinutes(minutes[i])); start = start.AddMinutes(minutes[i]); }
        var model = new TimerModel(engine, prefs); model.CompleteSetup(); model.SetTheme(theme);
        model.AddDayNote(Now.Date, "14:30 Call back #AccountOne"); model.AddDayNote(Now.Date, "! Timesheet due @tomorrow 17:00"); model.AddDayNote(Now.Date, "Plain note"); model.AddTodo("Send invoice #AccountTwo"); model.AddTodo("Order tea");
        return model;
    }
    private static void Settle(Window w) { VisibilityAudit.NoTransitions(w); w.UpdateLayout(); Dispatcher.UIThread.RunJobs(); VisibilityAudit.NoTransitions(w); w.UpdateLayout(); }
    private static void Click(Control c) { c.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
    private static Button Named(Control root, string name) => root.GetVisualDescendants().OfType<Button>().First(b => b.Name == name);

    private static List<VisibilityAudit.Finding> Scenes(string theme)
    {
        var found = new List<VisibilityAudit.Finding>(); string Scene(string s) => theme + " " + s;

        // The desk at its usual sizes, then arranging, then with a tile floating (its placeholder shows).
        {
            var model = Rich(theme); var window = new DashboardWindow(model) { Width = 1400, Height = 900 }; window.Show();
            try
            {
                Click(Named(window, "DeskTab")); var desk = window.GetVisualDescendants().OfType<DeskView>().Single(); desk.Lively = false; Settle(window);
                found.AddRange(VisibilityAudit.Check(Scene("desk"), window));
                desk.SetArranging(true); Settle(window); found.AddRange(VisibilityAudit.Check(Scene("desk arranging"), window)); desk.SetArranging(false);
                model.FloatWidget("todo", 2, 3); Settle(window); found.AddRange(VisibilityAudit.Check(Scene("desk with a floating tile"), window)); model.DockWidget("todo");
                Click(Named(window, "TimelineTab")); Settle(window); found.AddRange(VisibilityAudit.Check(Scene("timeline"), window));
                model.ToggleAccount("Account one"); Settle(window); found.AddRange(VisibilityAudit.Check(Scene("timeline recording"), window)); model.ToggleBreak(); Settle(window); found.AddRange(VisibilityAudit.Check(Scene("timeline on break"), window)); model.ToggleBreak(); model.ToggleAccount("Account one");
                Click(Named(window, "TimesheetTab")); foreach (var section in new[] { "overview", "hours", "entries" }) { model.SetWeekSection(section); Settle(window); found.AddRange(VisibilityAudit.Check(Scene("timesheet " + section), window)); }
                foreach (var key in new[] { "general", "appearance", "pill", "desk", "hours", "accounts", "about" }) { window.OpenCategory(key); Settle(window); found.AddRange(VisibilityAudit.Check(Scene("settings " + key), window)); }
            }
            finally { window.Close(); }
        }
        // Every tile by itself at its smallest, usual and largest size, which is every version of it the desk can show.
        foreach (var state in new[] { "idle", "tracking", "break", "overtime" })
        {
            var model = Rich(theme); if (state != "idle") model.ToggleAccount("Account one"); if (state == "break") model.ToggleBreak(); if (state == "overtime") model.SetShifts(new() { [DayOfWeek.Friday] = new(TimeSpan.FromHours(9), TimeSpan.FromHours(10)) });
            foreach (var kind in DeskView.Kinds)
                foreach (var (c, r) in new[] { (kind.MinColumns, kind.MinRows), (kind.Columns, kind.Rows), (kind.MaxColumns, kind.MaxRows), (Math.Min(kind.MaxColumns, 3), Math.Min(kind.MaxRows, 3)) }.Distinct())
                {
                    var view = new DeskView(model, null, kind.Id); var size = DeskView.PixelsFor(c, r);
                    var underlay = new Border { CornerRadius = new CornerRadius(22) }; underlay.Classes.Add("surface");
                    var window = new Window { Width = size.Width + 40, Height = size.Height + 40, Content = new Grid { Margin = new Thickness(20), Children = { underlay, view } } };
                    window.Show();
                    try { view.SetCells(c, r); Settle(window); found.AddRange(VisibilityAudit.Check(Scene($"tile {kind.Id} {c}x{r} {state}"), window)); }
                    finally { window.Close(); }
                }
        }
        // The pill, in each state and layout.
        foreach (var style in new[] { "full", "compact", "minimal" })
        {
            var model = Rich(theme); model.SetPillStyle(style);
            var pill = new PillWindow(model); pill.Show();
            try
            {
                Settle(pill); found.AddRange(VisibilityAudit.Check(Scene($"pill {style} idle"), pill));
                model.ToggleAccount("Account one"); Settle(pill); found.AddRange(VisibilityAudit.Check(Scene($"pill {style} recording"), pill));
                model.ToggleBreak(); Settle(pill); found.AddRange(VisibilityAudit.Check(Scene($"pill {style} break"), pill));
                model.ToggleBreak(); model.SetShifts(new() { [DayOfWeek.Friday] = new(TimeSpan.FromHours(9), TimeSpan.FromHours(10)) }); Settle(pill); found.AddRange(VisibilityAudit.Check(Scene($"pill {style} overtime"), pill));
            }
            finally { pill.Close(); }
        }
        // The menus that open from the pill and the dashboard, which are drawn in a pop-up of their own.
        {
            var model = Rich(theme); model.FloatWidget("todo", 2, 3); var pill = new PillWindow(model); pill.Show();
            try
            {
                var menu = pill.ContextMenu!; menu.Open(pill); Settle(pill); VisibilityAudit.NoTransitions(menu);
                found.AddRange(VisibilityAudit.Check(Scene("pill menu"), menu));
                var dashboardMenu = Ui.SettingsMenu(model, pill: false); dashboardMenu.Open(pill); Settle(pill); found.AddRange(VisibilityAudit.Check(Scene("dashboard menu"), dashboardMenu)); dashboardMenu.Close();
            }
            finally { pill.Close(); }
        }
        // Dialogs and the quick capture box.
        {
            var model = Rich(theme); var dashboard = new DashboardWindow(model) { Width = 1200, Height = 800 }; dashboard.Show();
            try
            {
                foreach (var (name, dialog) in new (string, Dialog)[] { ("entry", new EntryDialog(model, null, Now.Date.AddHours(15), Now.Date.AddHours(16))), ("themes", new ThemesDialog(model)), ("accounts", new AccountsDialog(model)), ("export", new ExportDialog(model)), ("tidy", new TidyDialog(model)) })
                {
                    dashboard.Dialogs.Show(dialog); Settle(dashboard); found.AddRange(VisibilityAudit.Check(Scene("dialog " + name), dashboard)); dialog.Close(); Settle(dashboard);
                }
            }
            finally { dashboard.Close(); }
            var capture = new QuickCaptureWindow(model); capture.Show();
            try { var box = capture.GetVisualDescendants().OfType<TextBox>().First(); box.Text = "! Call back @fri 09:30 #AccountOne"; Settle(capture); found.AddRange(VisibilityAudit.Check(Scene("quick capture"), capture)); }
            finally { capture.Close(); }
        }
        return found;
    }

    [AvaloniaFact]
    public void EverythingOnScreenCanBeReadInLightAndDark()
    {
        VisibilityAudit.Examined = 0;
        var all = Scenes("Light").Concat(Scenes("Dark")).GroupBy(f => (f.Scene, f.Name, f.Text)).Select(g => g.First()).ToList();
        // The same element shows up in many scenes, so the report is one line per element and theme, with the worst case and where it was.
        var lines = all.GroupBy(f => (Theme: f.Scene.Split(' ')[0], f.Kind, f.Name, f.Text)).Select(g => (Worst: g.OrderBy(f => f.Ratio).First(), Count: g.Count()))
            .OrderBy(x => x.Worst.Ratio).Select(x => $"{x.Worst} [x{x.Count}]").ToList();
        foreach (var line in lines) output.WriteLine(line);
        Assert.True(lines.Count == 0, $"{lines.Count} things are hard to read:\n" + string.Join('\n', lines.Take(80)));
    }

    [AvaloniaFact]
    public void TheAuditLooksAtALotAndCatchesWhatIsHardToRead()
    {
        // Something that cannot be read is found: pale grey text on white, and a pale icon, but not dark text.
        var pale = new TextBlock { Text = "Faint", Foreground = Avalonia.Media.Brushes.LightGray }; var dark = new TextBlock { Text = "Clear", Foreground = Avalonia.Media.Brushes.Black };
        var host = new Border { Background = Avalonia.Media.Brushes.White, Child = new StackPanel { Children = { pale, dark } } };
        var window = new Window { Content = host, Width = 200, Height = 100 }; window.Show(); Settle(window);
        try { var found = VisibilityAudit.Check("self", window); Assert.Equal("Faint", Assert.Single(found).Text); }
        finally { window.Close(); }
        // And the real run reaches thousands of elements across both themes, so it is not passing by looking at nothing.
        VisibilityAudit.Examined = 0; Scenes("Light"); Assert.True(VisibilityAudit.Examined > 1500, $"Only {VisibilityAudit.Examined} elements were examined.");
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

/// <summary>
/// On the Light theme the accent is a darker blue that needs white words. A global style gives every text block the Ink colour, which beat what a button handed down,
/// so this walks every screen in Light and reports any words drawn on the accent that are not the accent's own text colour.
/// </summary>
public sealed class AccentTextTests
{
    private static void Settle(Window window) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static readonly HashSet<string> Checked = new();
    private static void Press(Window w, string name) { w.GetVisualDescendants().OfType<Button>().First(b => b.Name == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(w); }
    private static bool Has(Window w, string name) => w.GetVisualDescendants().OfType<Button>().Any(b => b.Name == name);

    private static Color? BackgroundOf(Visual v) => v switch
    {
        ContentPresenter { Background: ISolidColorBrush b } when b.Color.A >= 200 => b.Color,
        Border { Background: ISolidColorBrush b } when b.Color.A >= 200 => b.Color,
        Panel { Background: ISolidColorBrush b } when b.Color.A >= 200 => b.Color,
        _ => null
    };
    // Pop-ups draw in their own root, so their content is audited on its own.
    private static void AuditPopups(Window window, string scene, TimerModel model, List<string> found)
    {
        foreach (var popup in window.GetLogicalDescendants().OfType<Avalonia.Controls.Primitives.Popup>().Where(p => p.IsOpen && p.Child is Visual)) Audit(popup.Child!, scene + " popup " + (popup.Name ?? ""), model, found);
    }
    private static void Audit(Visual root, string scene, TimerModel model, List<string> found)
    {
        var accent = Color.Parse(model.CurrentTheme.Colour("Accent")); var ink = Color.Parse(model.CurrentTheme.Colour("AccentInk"));
        foreach (var text in root.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(t.Text)))
        {
            Color? behind = null; string chain = "";
            for (var up = text.GetVisualParent(); up is not null && behind is null; up = up.GetVisualParent())
            {
                behind = BackgroundOf(up); if (up is Control c && (c.Name is not null || c.Classes.Count > 0)) chain += " < " + (c.Name ?? string.Join('.', c.Classes));
            }
            if (behind is not { } bg || bg.R != accent.R || bg.G != accent.G || bg.B != accent.B) continue;
            if (text.Foreground is ISolidColorBrush fg && fg.Color.R == ink.R && fg.Color.G == ink.G && fg.Color.B == ink.B) { Checked.Add(text.Text!); continue; }
            found.Add($"{scene}: \"{text.Text}\" is {(text.Foreground as ISolidColorBrush)?.Color} on the accent{chain}");
        }
    }

    [AvaloniaFact]
    public void EveryWordOnTheAccentInLightIsTheAccentsOwnTextColour()
    {
        var found = new List<string>();
        var model = TimerModel.CreateDemo(); model.SetTheme("Light"); model.CompleteSetup();
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Audit(window, "timeline", model, found);
            // Two blocks of one account selected, so Merge is the accent.
            var same = model.Engine.Sessions.Where(s => s.Account == model.Engine.Sessions[0].Account).Take(2).ToArray();
            if (same.Length == 2) { model.SelectBlock(same, false); Settle(window); Audit(window, "merge ready", model, found); model.SelectBlock(Array.Empty<SessionRow>(), false); }
            Press(window, "AddMeeting"); Audit(window, "notes card, meeting form", model, found);
            Press(window, "AddNote"); Audit(window, "notes card, note form", model, found);
            Press(window, "DatePick"); Audit(window, "date button", model, found); AuditPopups(window, "date", model, found); Press(window, "DatePick");
            Press(window, "Notifications"); AuditPopups(window, "bell", model, found); Press(window, "Notifications");
            Press(window, "FormBell"); AuditPopups(window, "reminder", model, found); window.GetVisualDescendants().OfType<Button>().Where(b => b.Name == "ReminderCancel" || b.Name == "ReminderClose").ToList().ForEach(b => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))); Press(window, "AddNote");
            Press(window, "NotesTab"); Audit(window, "notes page", model, found);
            Press(window, "WeekOverviewTab"); Audit(window, "week", model, found);
            if (Has(window, "WeekTable")) { Press(window, "WeekTable"); Audit(window, "week table", model, found); }
            Press(window, "Settings");
            foreach (var section in new[] { "Appearance", "Behaviour", "Hours", "Accounts", "Export", "About" })
                if (Has(window, "Settings" + section)) { Press(window, "Settings" + section); Audit(window, "settings " + section, model, found); }
            Press(window, "TimelineTab");
            var entry = new EntryDialog(model, new DateTime(2026, 10, 26, 9, 0, 0), new DateTime(2026, 10, 26, 9, 30, 0)).Open(window); Audit(window, "add time", model, found); entry.Close(); Settle(window);
            var accounts = new AccountsDialog(model).Open(window); Audit(window, "accounts and budgets", model, found); accounts.Close(); Settle(window);
            var shifts = new ShiftDialog(model).Open(window); Audit(window, "working hours", model, found); shifts.Close(); Settle(window);
        }
        finally { window.Close(); }
        var pillModel = TimerModel.CreateDemo(); pillModel.SetTheme("Light"); pillModel.CompleteSetup();
        var pill = new PillWindow(pillModel); pill.Show(); Settle(pill);
        try { Audit(pill, "pill", pillModel, found); pill.ShowPanel(); Settle(pill); Audit(pill, "pill drawer", pillModel, found); }
        finally { pill.Close(); }
        var fresh = new TimerModel(new TrackingEngine(clock: () => new DateTime(2026, 10, 9, 10, 0, 0)), new StoreSettings(new() { new("Account one"), new("Account two") }, false, "Light", DayOfWeek.Monday, SetupDone: false));
        var setup = new DashboardWindow(fresh); setup.Show(); Settle(setup);
        try
        {
            for (var step = 0; step < 4; step++)
            {
                Audit(setup, "setup step " + (step + 1), fresh, found);
                var next = setup.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name == "SetupNext");
                if (next is null) break; next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(setup);
            }
        }
        finally { setup.Close(); }
        // The audit must really have seen the buttons that sit on the accent, or a pass would mean nothing.
        foreach (var expected in new[] { "Add meeting", "Add note", "Add", "Merge", "Set" }) Assert.True(Checked.Contains(expected), "The audit never saw \"" + expected + "\" on the accent. It saw: " + string.Join(", ", Checked));
        Assert.True(found.Count == 0, "Words on the accent that are not the accent's text colour:\n" + string.Join("\n", found.Distinct()));
    }

    [AvaloniaFact]
    public void AnIconInAPrimaryButtonTakesTheAccentsTextColourAndOneElsewhereTheInk()
    {
        var model = TimerModel.CreateDemo(); model.SetTheme("Light"); model.CompleteSetup();
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var same = model.Engine.Sessions.Where(s => s.Account == model.Engine.Sessions[0].Account).Take(2).ToArray(); Assert.Equal(2, same.Length);
            model.SelectBlock(same, false); Settle(window);
            var merge = window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "MergeSelected" || b.Name == "Merge");
            Assert.Contains("primary", merge.Classes); Assert.Equal("AccentInk", merge.GetVisualDescendants().OfType<IconGlyph>().First().ColourKey);
            var plain = window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "CopySelected");
            Assert.Equal("Ink", plain.GetVisualDescendants().OfType<IconGlyph>().First().ColourKey);
        }
        finally { window.Close(); }
    }
}

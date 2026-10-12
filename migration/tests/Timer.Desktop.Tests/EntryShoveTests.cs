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

namespace Timer.Desktop.Tests;

public sealed class EntryShoveTests
{
    private static DateTime Day => new(2026, 10, 9);
    private static SessionRow Row(string start, string minutes, string account = "Account one") => new(Day.ToString("yyyy-MM-dd"), account, start, "added", minutes);
    private static TrackingEngine Engine(DateTime? now = null) => new(clock: () => now ?? Day.AddHours(16));
    private static void Click(Control root, string name)
    {
        root.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void LengtheningABlockShovesOnlyTheBlocksInTheWayAndStopsAtTheFirstGap()
    {
        var engine = Engine(); engine.Sessions.AddRange(new[] { Row("09:00:00", "60"), Row("10:00:00", "30", "Account two"), Row("10:30:00", "30"), Row("12:00:00", "30") });
        var editor = new LedgerEditor(engine); var first = engine.Sessions[0];
        var plan = editor.PlanExtend(first, Day.AddHours(10).AddMinutes(15));
        Assert.Equal(3, plan.Count); // The block itself and the two that follow it closely; the one after the gap stays put.
        Assert.Equal(Day.AddHours(10).AddMinutes(15), plan.Single(p => ReferenceEquals(p.Source, engine.Sessions[1])).Updated.From);
        Assert.Equal(Day.AddHours(10).AddMinutes(45), plan.Single(p => ReferenceEquals(p.Source, engine.Sessions[2])).Updated.From);
        var credited = engine.Credited("Account one", Day, Day.AddDays(1)) + engine.Credited("Account two", Day, Day.AddDays(1));
        editor.ExtendEnd(first, Day.AddHours(10).AddMinutes(15), "Overran");
        Assert.Equal(new[] { "09:00:00", "10:15:00", "10:45:00", "12:00:00" }, engine.Sessions.Select(s => s.Start));
        Assert.Equal("75.00", engine.Sessions[0].Minutes.Contains('.') ? engine.Sessions[0].Minutes : engine.Sessions[0].Minutes + ".00");
        Assert.Equal(credited + TimeSpan.FromMinutes(15), engine.Credited("Account one", Day, Day.AddDays(1)) + engine.Credited("Account two", Day, Day.AddDays(1))); // Only the lengthening adds time.
        Assert.Equal("Overran", editor.Note(engine.Sessions[0]));
        Assert.Equal("Extend entry and move later blocks", editor.UndoLabel);
        editor.Undo();
        Assert.Equal(new[] { "09:00:00", "10:00:00", "10:30:00", "12:00:00" }, engine.Sessions.Select(s => s.Start)); Assert.Equal("60", engine.Sessions[0].Minutes);
    }

    [Fact]
    public void ShovingIsRefusedWhenThereIsNoRoomBeforeMidnightOrItWouldHitRunningTime()
    {
        var late = Engine(); late.Sessions.AddRange(new[] { Row("22:00:00", "60"), Row("23:00:00", "60", "Account two") });
        Assert.Contains("before midnight", Assert.Throws<ArgumentException>(() => new LedgerEditor(late).PlanExtend(late.Sessions[0], Day.AddHours(23).AddMinutes(30))).Message);
        var now = Day.AddHours(10).AddMinutes(10); var running = Engine(now); running.Sessions.Add(Row("09:00:00", "60")); running.ToggleAccount("Account two");
        Assert.Throws<ArgumentException>(() => new LedgerEditor(running).PlanExtend(running.Sessions[0], Day.AddHours(10).AddMinutes(30)).Count);
    }

    [AvaloniaFact]
    public void ExtendingAnEndIntoTheNextBlockAsksFirstAndOnlyMovesItAfterConfirmation()
    {
        var engine = Engine(); engine.Sessions.AddRange(new[] { Row("09:00:00", "60"), Row("10:00:00", "30", "Account two") });
        var model = new TimerModel(engine, new StoreSettings(new() { new("Account one"), new("Account two") }, false, "Light", DayOfWeek.Monday));
        var row = engine.Sessions[0]; var window = new EntryDialog(model, row, row.From, row.To).Open(model);
        try
        {
            Dispatcher.UIThread.RunJobs();
            var prompt = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ShovePrompt"); Assert.False(prompt.IsVisible);
            window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "EntryEnd").Text = "10:05:00"; Dispatcher.UIThread.RunJobs();
            Click(window, "SaveEntry");
            Assert.True(prompt.IsVisible); Assert.Equal("10:00:00", engine.Sessions[1].Start); Assert.Equal("60", engine.Sessions[0].Minutes); // Asked, and nothing changed yet.
            Assert.Contains("move later by up to 0:05", window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "ShoveText").Text);
            Click(window, "ShoveCancel"); Assert.False(prompt.IsVisible); Assert.Equal("10:00:00", engine.Sessions[1].Start);
            Click(window, "SaveEntry"); Click(window, "ShoveConfirm");
            Assert.Equal("10:05:00", engine.Sessions[1].Start); Assert.Equal("65.00", engine.Sessions[0].Minutes); Assert.False(window.IsVisible);
            Assert.Equal("Extend entry and move later blocks", model.Editor.UndoLabel);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ADashSitsMidwayBetweenTheStartAndEndBoxesInBothWindows()
    {
        var engine = Engine(); engine.Sessions.Add(Row("09:00:00", "60"));
        var model = new TimerModel(engine, new StoreSettings(new() { new("Account one") }, false, "Light", DayOfWeek.Monday));
        var entry = new EntryDialog(model, engine.Sessions[0], engine.Sessions[0].From, engine.Sessions[0].To).Open(model);
        var shifts = new ShiftWindow(model); shifts.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            foreach (var window in new Control[] { entry, shifts })
            {
                var dash = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "–");
                var siblings = dash.GetVisualParent()!.GetVisualChildren().OfType<TextBox>().ToList();
                var left = siblings.Where(b => b.Bounds.Right <= dash.Bounds.X + 1).OrderBy(b => b.Bounds.X).Last();
                var right = siblings.Where(b => b.Bounds.X >= dash.Bounds.Right - 1).OrderBy(b => b.Bounds.X).First();
                var middle = (left.Bounds.Right + right.Bounds.X) / 2;
                var centre = dash.Bounds.X + dash.Bounds.Width / 2;
                Assert.InRange(Math.Abs(centre - middle), 0, 1.5);
                Assert.Equal(Avalonia.Layout.HorizontalAlignment.Center, dash.HorizontalAlignment);
            }
        }
        finally { entry.Close(); shifts.Close(); }
    }
}

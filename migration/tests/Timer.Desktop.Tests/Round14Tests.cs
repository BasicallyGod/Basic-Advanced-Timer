using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

// Round 14: the account box on the pill, the pill without a name, and the narrow account panel with its one line rows.
public sealed class Round14Tests
{
    private static DateTime Day => new(2026, 10, 14);
    private static TimerModel Model()
    {
        var model = new TimerModel(new TrackingEngine(clock: () => Day.AddHours(10)), new StoreSettings(new List<AccountSettings> { new("Account one", 10, "week"), new("Account two") }, false, "Light", DayOfWeek.Monday));
        model.CompleteSetup(); return model;
    }
    private static void Settle(Window w) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = w.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static T Find<T>(Visual root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    [AvaloniaFact]
    public void TheAccountsBoxShowsTheStateDotAndItsChevronPointsDownAndUp()
    {
        var model = Model(); var pill = new PillWindow(model); pill.Show(); Settle(pill);
        try
        {
            var box = Find<Border>(pill, "PillAccountSelector");
            // Two marks and no letters: the state's dot beside a chevron, side by side and centred in the box.
            Assert.Empty(box.GetVisualDescendants().OfType<TextBlock>());
            var dot = box.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().Single(); var chevron = box.GetVisualDescendants().OfType<Chevron>().Single();
            var dotBox = dot.TranslatePoint(default, box)!.Value; var chevronBox = chevron.TranslatePoint(default, box)!.Value;
            Assert.True(dotBox.X < chevronBox.X); Assert.Equal(dotBox.Y + dot.Bounds.Height / 2, chevronBox.Y + chevron.Bounds.Height / 2, 1);
            Assert.Equal(box.Bounds.Width / 2, (dotBox.X + chevronBox.X + chevron.Bounds.Width) / 2, 1.5);
            Assert.False(pill.PanelOpen); pill.AccountsButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Assert.True(pill.PanelOpen);
            Assert.True(chevron.Expanded); // Down turns to up while the list is open.
            pill.CloseFlyouts(); Settle(pill); Assert.False(chevron.Expanded);
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void ThePillKeepsItsPartsInside()
    {
        Assert.Equal(416, PillWindow.PillWidth); // The display, the well and the break, 12 apart, between the two insets.
        var model = Model();
        var pill = new PillWindow(model); pill.Show(); Settle(pill);
        try
        {
            var surface = Find<Border>(pill, "PillSurface"); Assert.Equal(PillWindow.PillWidth, surface.Bounds.Width, 0.5);
            // Every control lies inside the card with the room asked for at each end.
            var left = surface.Bounds.Left; var right = surface.Bounds.Right;
            foreach (var name in new[] { "PillDisplay", "PillStateLight", "PillDashboard", "PillPause", "PillAccounts", "PillBreak" })
            {
                var c = Find<Control>(pill, name); var box = c.TranslatePoint(default, surface)!.Value;
                Assert.True(box.X >= 0 && box.X + c.Bounds.Width <= PillWindow.PillWidth, $"{name} {box.X}+{c.Bounds.Width}");
            }
            var brk = Find<Control>(pill, "PillBreak"); var edge = brk.TranslatePoint(new Point(brk.Bounds.Width, 0), surface)!.Value.X;
            Assert.InRange(PillWindow.PillWidth - edge, PillWindow.InsetRight - 5, PillWindow.InsetRight + 1); // A ring's room is part of its button.
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void TheAccountRowsAreOneLineWithItsBarUnder()
    {
        Assert.True(PillWindow.DropdownWidth < 230); // Still a narrow drawer; the raised rows need a little more room than the flat ones did.
        var model = Model(); var window = new DashboardWindow(model); window.Show();Settle(window);
        try
        {
            var rows = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("accountCard")).ToList();
            Assert.Equal(2, rows.Count); Assert.True(rows.Count == 2 && rows[0].Bounds.Width <= Sidebar.Width - 24 + 1); Assert.All(rows, r => Assert.Equal(AccountList.RowHeight, r.Bounds.Height, 0.5));
            var first = rows[0];
            // Name and today's time share a line, and nothing else in the row is words: the budget is in the tooltip.
            var words = first.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsVisible && !string.IsNullOrEmpty(t.Text)).ToList();
            Assert.Equal(2, words.Count); Assert.Equal(words[0].TranslatePoint(default, first)!.Value.Y, words[1].TranslatePoint(default, first)!.Value.Y, 6);
            Assert.NotEmpty(first.GetVisualDescendants().OfType<BudgetBar>());
            Assert.Contains("00:00 / 10:00", ToolTip.GetTip(first)?.ToString()); Assert.Contains("this week", ToolTip.GetTip(first)?.ToString());
            Assert.Equal(AccountIndicator.RowSize, first.GetVisualDescendants().OfType<AccountIndicator>().Single().Bounds.Width, 0.5);
        }
        finally { window.Close(); }
    }

    // Pictures for looking at the layout: TIMER_PREVIEW_DIR names the folder.
    [AvaloniaFact]
    public void WriteRound14Pictures()
    {
        var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR"); if (folder is null) return;
        Directory.CreateDirectory(folder);
        foreach (var theme in new[] { "Light", "Dark" })
            {
                var model = Model(); model.SetTheme(theme); model.ToggleAccount("Account two");
                var pill = new PillWindow(model); pill.Show(); Settle(pill);
                try
                {
                    using (var shut = pill.CaptureRenderedFrame()) shut?.Save(Path.Combine(folder, $"r14-pill-{theme}.png"));
                    pill.ShowPanel(); Settle(pill);
                    using var open = pill.CaptureRenderedFrame(); open?.Save(Path.Combine(folder, $"r14-pill-open-{theme}.png"));
                }
                finally { pill.Close(); }
            }
        var demo = TimerModel.CreateDemo(); var window = new DashboardWindow(demo); window.Show();Settle(window);
        try { using var frame = window.CaptureRenderedFrame(); frame?.Save(Path.Combine(folder, "r14-dashboard.png")); } finally { window.Close(); }
    }
}

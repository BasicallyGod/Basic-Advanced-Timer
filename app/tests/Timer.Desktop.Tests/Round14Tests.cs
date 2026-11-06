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
    public void TheAccountsZoneHoldsTheNameAndOpensAndClosesTheList()
    {
        var model = Model(); var pill = new PillWindow(model); pill.Show(); Settle(pill);
        try
        {
            var name = Find<SegmentName>(pill, "PillAccountName"); Assert.Equal(SegmentName.Cells, 6);
            Assert.Empty(pill.AccountsButton.GetVisualDescendants().OfType<Chevron>()); // No chevron: pressing the name is enough.
            Assert.False(pill.PanelOpen); pill.AccountsButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Assert.True(pill.PanelOpen);
            pill.CloseFlyouts(); Settle(pill); Assert.False(pill.PanelOpen);
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void ThePillKeepsItsPartsInside()
    {
        Assert.Equal(StatusFace.DisplayWidth + 12, PillWindow.PillWidth, 1.0); // The display and a 6 pixel bezel each side.
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
            Assert.InRange(PillWindow.PillWidth - edge, PillWindow.InsetRight, PillWindow.InsetRight + 8); // The bezel and the display's own right padding.
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void TheAccountRowsSeparateTodaysTimeFromBudgetUsageAndPeriod()
    {
        Assert.True(PillWindow.DropdownWidth < 230); // Still a narrow drawer; the raised rows need a little more room than the flat ones did.
        var model = Model(); var window = new DashboardWindow(model); window.Show();Settle(window);
        try
        {
            var rows = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("accountCard")).ToList();
            Assert.Equal(2, rows.Count); Assert.True(rows.Count == 2 && rows[0].Bounds.Width <= Sidebar.Width - 24 + 1); Assert.All(rows, r => Assert.Equal(AccountList.RowHeight, r.Bounds.Height, 0.5));
            var first = rows[0];
            // Name and today's time share a line; budget usage and period occupy their own line.
            var words = first.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsVisible && !string.IsNullOrEmpty(t.Text)).ToList();
            Assert.Equal(4, words.Count); Assert.Equal(words[0].TranslatePoint(default, first)!.Value.Y, words[1].TranslatePoint(default, first)!.Value.Y, 6);
            var usage = first.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "AccountBudgetUsage");
            var period = first.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "AccountBudgetPeriod");
            Assert.Equal("00:00 / 10:00", usage.Text); Assert.Equal("This week", period.Text);
            Assert.True(usage.TranslatePoint(default, first)!.Value.Y > words[0].TranslatePoint(default, first)!.Value.Y);
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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

// Round 14: the account chip under the play tile, the horizontal pill without a name, and the narrow account panel with its one line rows.
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
    public void TheVerticalPillsTilePlaysAndPausesAndTheChipUnderItShowsTheAccountsColour()
    {
        var model = Model(); var pill = new PillWindow(model); pill.Show(); Settle(pill);
        try
        {
            var chip = Find<Border>(pill, "PillAccountChip"); var tile = Find<Border>(pill, "PillPrimaryTile");
            // Two marks and no letters: the colour of the account it would start (or is recording) beside a chevron.
            Assert.Empty(chip.GetVisualDescendants().OfType<TextBlock>());
            var dot = chip.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().Single(); var chevron = chip.GetVisualDescendants().OfType<Chevron>().Single();
            // Side by side on one line, the dot first, and the pair centred in the chip.
            var dotBox = dot.TranslatePoint(default, chip)!.Value; var chevronBox = chevron.TranslatePoint(default, chip)!.Value;
            Assert.True(dotBox.X < chevronBox.X); Assert.Equal(dotBox.Y + dot.Bounds.Height / 2, chevronBox.Y + chevron.Bounds.Height / 2, 1);
            Assert.Equal(chip.Bounds.Width / 2, (dotBox.X + chevronBox.X + chevron.Bounds.Width) / 2, 1.5);
            Assert.Equal(Avalonia.Media.Color.Parse(Ui.Visible(model.Accounts[0].Colour)), ((Avalonia.Media.ISolidColorBrush)dot.Fill!).Color);
            model.ToggleAccount("Account two"); Settle(pill);
            Assert.Equal(Avalonia.Media.Color.Parse(Ui.Visible(model.Accounts[1].Colour)), ((Avalonia.Media.ISolidColorBrush)dot.Fill!).Color);
            // While an account runs the tile carries the pause symbol, not the initials: the chip does that.
            Assert.All(tile.GetVisualDescendants().OfType<TextBlock>(), t => Assert.False(t.IsVisible));
            Assert.Contains(tile.GetVisualDescendants().OfType<Viewbox>(), v => v.IsVisible);
            // The chip sits under the tile, inside the same well, and the well keeps its height.
            var well = Find<Border>(pill, "PillWell"); var tilePoint = tile.TranslatePoint(default, well)!.Value; var chipPoint = chip.TranslatePoint(default, well)!.Value;
            Assert.True(chipPoint.Y >= tilePoint.Y + tile.Bounds.Height - 0.5);
            Assert.Equal(StatusFace.TileSize + StatusFace.ChipHeight + StatusFace.WellGap + 2 * StatusFace.WellPadding, well.Bounds.Height, 0.5);
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void TheChipOpensTheListAndShowsTheAccountsOwnColour()
    {
        var model = Model(); var pill = new PillWindow(model); pill.Show(); Settle(pill);
        try
        {
            var dot = Find<Border>(pill, "PillAccountChip").GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().Single();
            Assert.Equal(Avalonia.Media.Color.Parse(Ui.Visible(model.Accounts[0].Colour)), ((Avalonia.Media.ISolidColorBrush)dot.Fill!).Color);
            Assert.False(pill.PanelOpen); pill.AccountsButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Assert.True(pill.PanelOpen);
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void TheChevronPointsTheWayTheListOpensSideToSideOnTheVerticalPillAndDownOnTheHorizontalOne()
    {
        foreach (var horizontal in new[] { false, true })
        {
            var model = Model(); model.SetPillHorizontal(horizontal); var pill = new PillWindow(model); pill.Show(); Settle(pill);
            try
            {
                var chevron = Find<Button>(pill, "PillAccounts").GetVisualDescendants().OfType<Chevron>().Single();
                Assert.Equal(!horizontal, chevron.Sideways); // The vertical pill's list slides out to the side, the horizontal pill's hangs below.
                Assert.False(chevron.Expanded); pill.ShowPanel(); Settle(pill); Assert.True(chevron.Expanded); // Right turns to left, down turns to up, while it is open.
                pill.CloseFlyouts(); Settle(pill); Assert.False(chevron.Expanded);
            }
            finally { pill.Close(); }
        }
    }

    [AvaloniaFact]
    public void TheHorizontalPillIsNarrowAndKeepsItsPartsInside()
    {
        Assert.Equal(336, PillWindow.HorizontalWidth);
        var model = Model(); model.SetPillHorizontal(true);
        var pill = new PillWindow(model); pill.Show(); Settle(pill);
        try
        {
            var surface = Find<Border>(pill, "PillSurface"); Assert.Equal(PillWindow.HorizontalWidth, surface.Bounds.Width, 0.5);
            // Every control lies inside the card with the room asked for at each end.
            var left = surface.Bounds.Left; var right = surface.Bounds.Right;
            foreach (var name in new[] { "PillStateLight", "PillDashboard", "PillPause", "PillAccounts", "PillBreak" })
            {
                var c = Find<Control>(pill, name); var box = c.TranslatePoint(default, surface)!.Value;
                Assert.True(box.X >= 0 && box.X + c.Bounds.Width <= PillWindow.HorizontalWidth, $"{name} {box.X}+{c.Bounds.Width}");
            }
            var brk = Find<Control>(pill, "PillBreak"); var edge = brk.TranslatePoint(new Point(brk.Bounds.Width, 0), surface)!.Value.X;
            Assert.InRange(PillWindow.HorizontalWidth - edge, PillWindow.HorizontalInsetRight - 5, PillWindow.HorizontalInsetRight + 1); // A ring's room is part of its button.
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void TheAccountRowsAreOneLineWithItsBarUnder()
    {
        Assert.True(PillWindow.DrawerWidth < 220);
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
            foreach (var horizontal in new[] { false, true })
            {
                var model = Model(); model.SetTheme(theme); model.SetPillHorizontal(horizontal); model.ToggleAccount("Account two");
                var pill = new PillWindow(model); pill.Show(); Settle(pill);
                try
                {
                    using (var shut = pill.CaptureRenderedFrame()) shut?.Save(Path.Combine(folder, $"r14-pill-{(horizontal ? "h" : "v")}-{theme}.png"));
                    pill.ShowPanel(); Settle(pill);
                    using var open = pill.CaptureRenderedFrame(); open?.Save(Path.Combine(folder, $"r14-pill-{(horizontal ? "h" : "v")}-open-{theme}.png"));
                }
                finally { pill.Close(); }
            }
        var demo = TimerModel.CreateDemo(); var window = new DashboardWindow(demo); window.Show();Settle(window);
        try { using var frame = window.CaptureRenderedFrame(); frame?.Save(Path.Combine(folder, "r14-dashboard.png")); } finally { window.Close(); }
    }
}

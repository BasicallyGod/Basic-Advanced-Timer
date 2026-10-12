using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Desktop;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

// Switching accounts quickly, taking a slip back, and how blocks and the Today button look.
public sealed class SwitchingAndBlockTests
{
    private static void Settle(Window window) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static readonly DateTime Nine = new(2026, 10, 14, 9, 0, 0);

    private static (TimerModel Model, TrackingEngine Engine, Func<DateTime> Now, Action<TimeSpan> Advance) Setup()
    {
        var now = Nine; var engine = new TrackingEngine(clock: () => now);
        var model = new TimerModel(engine, new StoreSettings(new() { new("Account one"), new("Account two"), new("Account three") }, false, ThemeLibrary.Default, DayOfWeek.Monday));
        model.ChangeDate((Nine.Date - model.SelectedDate.Date).Days);
        return (model, engine, () => now, span => now += span);
    }

    [Fact]
    public void SwitchingAccountsNeverLeavesBlocksOverlappingWhateverTheFractionsOfASecond()
    {
        var random = new Random(7);
        for (var round = 0; round < 300; round++)
        {
            var now = Nine.AddMilliseconds(random.Next(0, 1000)); var engine = new TrackingEngine(clock: () => now);
            var accounts = new[] { "Account one", "Account two", "Account three" };
            for (var step = 0; step < 8; step++)
            {
                engine.ToggleAccount(accounts[random.Next(accounts.Length)]);
                now = now.AddMilliseconds(random.Next(200, 90_000));
            }
            engine.StopAccount();
            var ordered = engine.Sessions.OrderBy(s => s.From).ToList();
            for (var i = 1; i < ordered.Count; i++)
                Assert.True(ordered[i - 1].To <= ordered[i].From, $"Round {round}: {ordered[i - 1].Account} ends {ordered[i - 1].To:HH:mm:ss.fff} after {ordered[i].Account} starts {ordered[i].From:HH:mm:ss.fff}.");
        }
    }

    [Fact]
    public void GoingBackToTheFirstAccountQuicklyOffersToContinueItsEarlierBlockAndTakesTheSlipBack()
    {
        var (model, engine, _, advance) = Setup();
        model.ToggleAccount("Account one"); advance(TimeSpan.FromMinutes(30));
        model.ToggleAccount("Account two"); advance(TimeSpan.FromSeconds(20)); // A slip of the finger.
        model.ToggleAccount("Account one"); advance(TimeSpan.FromSeconds(10));
        Assert.True(model.CanContinuePrevious); Assert.Contains("09:00", model.ContinuePreviousText);
        Assert.Equal(2, engine.Sessions.Count);
        Assert.True(model.ContinuePrevious());
        Assert.Empty(engine.Sessions); // The slip and the earlier block are both gone, the account is running from the start of the earlier block.
        Assert.Equal("Account one", engine.Active!.Account); Assert.Equal(Nine, engine.Active.Start);
        Assert.False(model.CanContinuePrevious);
        advance(TimeSpan.FromMinutes(5)); model.TogglePause();
        var only = Assert.Single(engine.Sessions); Assert.Equal("Account one", only.Account); Assert.Equal(Nine, only.From);
        Assert.Equal(35.5, (double)decimal.Parse(only.Minutes, System.Globalization.CultureInfo.InvariantCulture), 0.1); // The whole stretch, including the slip, is Account one's.
    }

    [Fact]
    public void TheOptionIsNotOfferedWhenTheSwitchWasNotASlip()
    {
        var (model, engine, _, advance) = Setup();
        model.ToggleAccount("Account one"); advance(TimeSpan.FromMinutes(30));
        model.ToggleAccount("Account two"); advance(TimeSpan.FromMinutes(10)); // Too long to be a slip.
        model.ToggleAccount("Account one"); advance(TimeSpan.FromMinutes(1));
        Assert.False(model.CanContinuePrevious);
        model.ToggleAccount("Account three"); advance(TimeSpan.FromSeconds(20)); model.ToggleAccount("Account two"); // Back to a different account from the one before.
        Assert.False(model.CanContinuePrevious);
        advance(TimeSpan.FromMinutes(1)); model.ToggleAccount("Account one"); advance(TimeSpan.FromMinutes(1));
        model.ToggleAccount("Account two"); advance(TimeSpan.FromSeconds(20)); model.ToggleAccount("Account one");
        Assert.True(model.CanContinuePrevious); // Offered again for the new slip...
        advance(TimeSpan.FromMinutes(4));
        Assert.False(model.CanContinuePrevious); // ...but only for a few minutes.
    }

    [Fact]
    public void AShortBreakAndBackCanBeTakenBackToo_ButNotIntoAGroup()
    {
        var (model, engine, _, advance) = Setup();
        model.ToggleAccount("Account one"); advance(TimeSpan.FromMinutes(20));
        model.ToggleBreak(); advance(TimeSpan.FromSeconds(30)); model.ToggleBreak();
        Assert.Equal("Account one", engine.Active!.Account);
        Assert.True(model.CanContinuePrevious);
    }

    [AvaloniaFact]
    public void TheDashboardPillAndMenusShowTheOptionOnlyWhileItIsOnOffer()
    {
        var (model, _, _, advance) = Setup();
        var dashboard = new DashboardWindow(model); dashboard.Show(); var pill = new PillWindow(model); pill.Show(); Settle(dashboard);
        Assert.False(dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ContinuePrevious").IsVisible);
        model.ToggleAccount("Account one"); advance(TimeSpan.FromMinutes(30)); model.ToggleAccount("Account two"); advance(TimeSpan.FromSeconds(15)); model.ToggleAccount("Account one"); Settle(dashboard);
        var button = dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ContinuePrevious");
        Assert.True(button.IsVisible); Assert.Contains("Account one", (string)button.Content!);
        Assert.True(pill.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PillContinue").IsVisible);
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle(dashboard);
        Assert.False(button.IsVisible); Assert.False(pill.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PillContinue").IsVisible);
        dashboard.Close(); pill.Close();
    }

    [AvaloniaFact]
    public void EveryBlockHasTheSameSmallCornerAtEveryZoomLevelAndTheTodayButtonMatchesTheOthers()
    {
        var (model, engine, _, _) = Setup();
        engine.Sessions.Add(new("2026-10-14", "Account one", "09:00:00", "recorded", "0.50")); // Half a minute: a tiny block.
        engine.Sessions.Add(new("2026-10-14", "Account two", "10:00:00", "recorded", "90.00"));
        var dashboard = new DashboardWindow(model); dashboard.Show(); Settle(dashboard);
        foreach (var zoom in new[] { 0.0, 0.5, 1.0 })
        {
            model.SetTimelineScale(TimerModel.ScaleAt(zoom), 300, 0); Settle(dashboard);
            var cards = dashboard.GetVisualDescendants().OfType<Border>().Where(b => b.Name?.StartsWith("TimelineEntry") == true).ToList();
            Assert.Equal(2, cards.Count);
            foreach (var card in cards) Assert.Equal(Math.Min(ReviewViews.BlockRadius, card.Height / 2), card.CornerRadius.TopLeft, 3);
        }
        var today = dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "GoToToday");
        Assert.True(today.Classes.Contains("toolButton") && today.Classes.Contains("quiet"));
        dashboard.Close();
    }

    [AvaloniaFact]
    public void BlocksThatOverlapByASecondShareALaneInsteadOfSplittingTheDay()
    {
        var (model, engine, _, _) = Setup();
        engine.Sessions.Add(new("2026-10-14", "Account one", "09:00:00", "recorded", "30.02")); // Ends 1.2 seconds into the next block.
        engine.Sessions.Add(new("2026-10-14", "Account two", "09:30:00", "recorded", "10.00"));
        var dashboard = new DashboardWindow(model); dashboard.Show(); Settle(dashboard);
        var cards = dashboard.GetVisualDescendants().OfType<Border>().Where(b => b.Name?.StartsWith("TimelineEntry") == true).ToList();
        Assert.Equal(2, cards.Count); Assert.Equal(cards[0].Width, cards[1].Width, 1);
        Assert.Equal(Canvas.GetLeft(cards[0]), Canvas.GetLeft(cards[1]), 1); // Same lane.
        dashboard.Close();
    }
}

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Timer.Desktop;
using Xunit;

namespace Timer.Desktop.Tests;

/// <summary>The wheel steps an hour by one and a minute by five, wraps, and moves by press, key and mouse wheel.</summary>
public class TimeWheelTests
{
    [Theory]
    [InlineData(true, 23, 1, 0)] [InlineData(true, 0, -1, 23)] [InlineData(true, 9, 1, 10)]
    [InlineData(false, 55, 1, 0)] [InlineData(false, 0, -1, 55)] [InlineData(false, 31, 1, 35)] [InlineData(false, 31, -1, 30)] [InlineData(false, 30, -1, 25)]
    public void StepsWrapAndMinutesLandOnFives(bool isHour, int from, int direction, int expected) => Assert.Equal(expected, TimeWheel.Next(isHour, from, direction));

    [AvaloniaFact]
    public void PressingTheDimmedRowsMovesTheTimeAndRaisesMoved()
    {
        var wheel = new TimeWheel(14, 31); var window = new Window { Width = 300, Height = 200, Content = wheel }; window.Show();
        try
        {
            var moved = 0; wheel.Moved += () => moved++;
            var after = wheel.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "WheelMinuteAfter"); after.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal((14, 35, 1), (wheel.Hour, wheel.Minute, moved));
            wheel.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "WheelHourBefore").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal((13, 35, 2), (wheel.Hour, wheel.Minute, moved));
            wheel.Set(9, 0); Assert.Equal(2, moved); // Setting it from outside raises nothing.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ReminderWheelKeepsFocusWhileSteppingAndSwitchingColumns()
    {
        var now = new DateTime(2026, 10, 14, 14, 15, 0);
        var card = new ReminderPopup(() => now, "Call the client back", now.AddMinutes(16), _ => { }, () => { });
        var window = new Window { Width = 420, Height = 620, Content = card };
        window.Show();
        try
        {
            Button Find(string name) => card.GetVisualDescendants().OfType<Button>().Single(button => button.Name == name);
            void Press(PhysicalKey key)
            {
                HeadlessWindowExtensions.KeyPressQwerty(window, key, RawInputModifiers.None);
                HeadlessWindowExtensions.KeyReleaseQwerty(window, key, RawInputModifiers.None);
            }

            Find("ReminderWheelToggle").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
            var minute = Find("WheelMinuteChosen");
            Assert.True(minute.Focus());
            Press(PhysicalKey.ArrowDown);
            Press(PhysicalKey.ArrowDown);
            Assert.Same(minute, window.FocusManager!.GetFocusedElement());
            Assert.Equal("Minute 40", AutomationProperties.GetName(minute));
            Assert.Equal(now.Date.AddHours(14).AddMinutes(40), card.Chosen);

            Press(PhysicalKey.ArrowLeft);
            Assert.Same(Find("WheelHourChosen"), window.FocusManager.GetFocusedElement());
            Press(PhysicalKey.ArrowUp);
            Assert.Equal(now.Date.AddHours(13).AddMinutes(40), card.Chosen);
            Press(PhysicalKey.ArrowRight);
            Assert.Same(minute, window.FocusManager.GetFocusedElement());
        }
        finally { window.Close(); }
    }

    // A picture of the reminder card with its wheel open, for looking at it: TIMER_PREVIEW_DIR names the folder.
    [AvaloniaFact]
    public void WriteReminderPicture()
    {
        var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR"); if (folder is null) return;
        Directory.CreateDirectory(folder);
        foreach (var theme in new[] { "Light", "Dark" })
        {
            var model = TimerModel.CreateDemo(() => new DateTime(2026, 10, 14, 14, 15, 0)); model.SetTheme(theme); model.CompleteSetup();
            var card = new ReminderPopup(() => model.Engine.Now, "Call the client back", new DateTime(2026, 10, 15, 14, 31, 0), _ => { }, () => { }) { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
            var window = new Window { Width = 420, Height = 620, Content = new Border { Padding = new Avalonia.Thickness(30), Child = card } }; window.Show();
            try { card.GetVisualDescendants().OfType<Button>().First(b => b.Name == "ReminderWheelToggle").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(folder, "reminder-" + theme + ".png")); }
            finally { window.Close(); }
        }
    }
}

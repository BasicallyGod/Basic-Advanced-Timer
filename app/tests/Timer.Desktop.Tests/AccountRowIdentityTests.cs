using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class AccountRowIdentityTests
{
    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Settle(Window window) { for (var i = 0; i < 3; i++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); } }

    [AvaloniaFact]
    public void NewRowDraggedAheadOfRenamedAccountsKeepsHistoryNotesGroupsAndColoursThroughRestart()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-account-identity-" + Guid.NewGuid());
        var day = new DateTime(2026, 11, 2);
        Dictionary<string, string?> colours = null!;
        try
        {
            using (var store = new TimerStore(folder))
            {
                var engine = new TrackingEngine(clock: () => day.AddHours(12));
                var model = new TimerModel(engine, store.Load(engine), store); model.CompleteSetup();
                engine.Sessions.AddRange(new[] {
                    new SessionRow("2026-11-02", "Account one", "09:00:00", "added", "30"),
                    new SessionRow("2026-11-02", "Account one", "09:30:00", "added", "30"),
                    new SessionRow("2026-11-02", "Account two", "10:00:00", "added", "20"),
                    new SessionRow("2026-11-02", "Internal", "11:00:00", "added", "10") });
                model.Editor.Group(engine.Sessions.Take(2).ToArray());
                model.Notes.AddNote("Keep the account link", "Account one", null); Assert.True(model.Save());
                colours = model.AccountSettings.ToDictionary(a => a.Name, a => a.Colour);
                var window = new DashboardWindow(model); window.Show();
                try
                {
                    window.Dialogs.Show(new AccountsDialog(model)); Settle(window);
                    var edits = new[] { ("Account one", "Client one"), ("Account two", "Client two"), ("Internal", "Operations") };
                    foreach (var (from, to) in edits) window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Text == from).Text = to;
                    Find<TextBox>(window, "NewAccountName").Text = "New account";
                    Find<Button>(window, "AddAccount").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(window);
                    var grip = Find<Border>(window, "AccountGrip3"); var first = Find<Border>(window, "AccountGrip0");
                    var fromPoint = grip.TranslatePoint(new Point(11, 15), window)!.Value;
                    var toPoint = first.TranslatePoint(new Point(11, 0), window)!.Value;
                    window.MouseDown(fromPoint, MouseButton.Left);
                    for (var step = 1; step <= 4; step++) { window.MouseMove(new Point(fromPoint.X, fromPoint.Y + (toPoint.Y - fromPoint.Y) * step / 4)); Settle(window); }
                    window.MouseUp(toPoint, MouseButton.Left);
                    Find<Button>(window, "SaveAccounts").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.Null(model.LastError);
                    Assert.Equal(new[] { "New account", "Client one", "Client two", "Operations" }, model.AccountSettings.Select(a => a.Name));
                    Assert.Equal(TimeSpan.Zero, model.AccountHistory("New account").Time);
                    Assert.Equal(TimeSpan.FromHours(1), model.AccountHistory("Client one").Time);
                    Assert.Equal(TimeSpan.FromMinutes(20), model.AccountHistory("Client two").Time);
                    Assert.Equal(TimeSpan.FromMinutes(10), model.AccountHistory("Operations").Time);
                }
                finally { window.Close(); }
            }
            using (var store = new TimerStore(folder))
            {
                var engine = new TrackingEngine(clock: () => day.AddHours(12));
                var model = new TimerModel(engine, store.Load(engine), store);
                Assert.Equal(new[] { "New account", "Client one", "Client two", "Operations" }, model.AccountSettings.Select(a => a.Name));
                Assert.Equal(TimeSpan.FromHours(1), model.AccountHistory("Client one").Time);
                Assert.Equal(TimeSpan.FromMinutes(20), model.AccountHistory("Client two").Time);
                Assert.Equal(TimeSpan.FromMinutes(10), model.AccountHistory("Operations").Time);
                Assert.Contains("Client one", model.Editor.GroupsCsv);
                Assert.Equal("Client one", model.Notes.AccountOf(model.Notes.All.Single()));
                Assert.Equal(colours["Account one"], model.AccountSettings.Single(a => a.Name == "Client one").Colour);
                Assert.Equal(colours["Account two"], model.AccountSettings.Single(a => a.Name == "Client two").Colour);
                Assert.Equal(colours["Internal"], model.AccountSettings.Single(a => a.Name == "Operations").Colour);
            }
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}

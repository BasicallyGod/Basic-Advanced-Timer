using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class StartupPreparationTests
{
    [AvaloniaFact]
    public void PillAndPassiveOperationsDoNotConstructTheDashboard()
    {
        var model = TimerModel.CreateDemo(); model.CompleteSetup();
        var windows = new WindowCoordinator(model, () => { });
        try
        {
            Assert.False(windows.DashboardCreated);
            windows.Pill.Show();
            Assert.False(windows.DashboardCreated); // Before the first render callback.
            model.Tick(); windows.HideToTray = true; model.TrayAvailable = true; model.SetTaskbarButton(false);
            windows.RescueOffScreen(); windows.Hide();
            Assert.False(windows.IsShown); Assert.False(windows.DashboardCreated);
            windows.ShowTimer(); Assert.True(windows.Pill.IsVisible); Assert.False(windows.DashboardCreated);
        }
        finally { windows.Quit(); }
        Assert.False(windows.DashboardCreated); // Quitting must not allocate an unused window.
    }

    [AvaloniaFact]
    public void FirstAccountsRequestCreatesOneDashboardAndOneDialog()
    {
        var model = TimerModel.CreateDemo(); model.CompleteSetup();
        var windows = new WindowCoordinator(model, () => { });
        try
        {
            Assert.False(windows.DashboardCreated);
            model.OpenAccounts();
            Assert.True(windows.DashboardCreated); Assert.True(windows.Dashboard.IsVisible);
            Assert.Single(windows.Dashboard.Dialogs.Dialogs.OfType<AccountsDialog>());
            var dashboard = windows.Dashboard;
            model.OpenAccounts(); Assert.Same(dashboard, windows.Dashboard);
            Assert.Single(dashboard.Dialogs.Dialogs.OfType<AccountsDialog>());
        }
        finally { windows.Quit(); }
    }

    [AvaloniaFact]
    public void FirstPageRequestIsNotLostAndImmediateOpenCollapseReusesTheDashboard()
    {
        var model = TimerModel.CreateDemo(); model.CompleteSetup();
        var windows = new WindowCoordinator(model, () => { });
        try
        {
            model.RequestPage("notes");
            Assert.True(windows.DashboardCreated);
            windows.ShowDashboard();
            Assert.Contains(windows.Dashboard.GetVisualDescendants().OfType<Button>(), b => b.Name == "NotesTab" && b.Classes.Contains("selected"));
            var dashboard = windows.Dashboard;
            dashboard.OpenPill!(); Assert.True(windows.Pill.IsVisible); Assert.False(dashboard.IsVisible);
            windows.Pill.OpenDashboard!(); Assert.Same(dashboard, windows.Dashboard); Assert.True(dashboard.IsVisible);
        }
        finally { windows.Quit(); }
    }
}

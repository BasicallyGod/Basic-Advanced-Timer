using Avalonia.Media;
using Avalonia;
using Timer.Core;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;

namespace Timer.Desktop;

public sealed class WindowCoordinator
{
    public PillWindow Pill { get; }
    public DashboardWindow Dashboard { get; }
    private readonly TimerModel model;
    private bool quitting, dashboardWasShown;
    private int visibilityRun;
    // Whether closing a window hides the timer to the tray. Without a tray the window really closes, as it always did.
    public bool HideToTray { get => hideToTray; set { hideToTray = value; ApplyTaskbar(); } }
    private bool hideToTray;
    private void ApplyTaskbar()
    {
        // Only a timer that can hide in the tray may leave the taskbar.
        var show = model.TaskbarButton || !hideToTray;
        if (Pill.ShowInTaskbar != show) Pill.ShowInTaskbar = show;
        if (Dashboard.ShowInTaskbar != (show && !warming)) Dashboard.ShowInTaskbar = show && !warming;
    }
    public bool IsShown => Pill.IsVisible || Dashboard.IsVisible;
    public WindowCoordinator(TimerModel model, Action exit)
    {
        this.model = model;
        using var construction = PerformanceTimings.Measure("window construction");
        Pill = new PillWindow(model);
        Dashboard = new DashboardWindow(model);
        // A taskbar button by default, so the timer can be pinned and launched like any app. It can be switched off to run from the tray alone.
        ApplyTaskbar();
        model.PropertyChanged += (_, _) => ApplyTaskbar();
        // The dashboard grows out of the pill's card (see WindowMorph) and shrinks back into a pill at the sidebar's top left.
        Pill.OpenDashboard = () =>
        {
            Dashboard.PrepareMorph(false);
            using var preparation = PerformanceTimings.Measure("expand preparation");
            EndWarm(false);
            model.ShowToday();
            Pill.CloseFlyouts(); // So it is a capsule again when you come back.
            var picture = WindowMorph.Capture(Pill);
            PlaceByCard(Dashboard, Pill, DashboardWindow.DashboardWidth + 2 * Ui.ShadowMargin, DashboardWindow.DashboardHeight + 2 * Ui.ShadowMargin, 1, -WindowMorph.PillInset);
            var (card, radius, at) = PillInDashboard();
            Dashboard.GrowFrom(card, radius, picture, at);
            Pill.Hide();
            Dashboard.Activate();
        };
        Dashboard.Reveal = ShowDashboard;
        Dashboard.OpenPill = () =>
        {
            if (Dashboard.Morphing) return;
            Dashboard.PrepareMorph(true);
            using var preparation = PerformanceTimings.Measure("collapse preparation");
            var visibility = ++visibilityRun;
            Dashboard.CloseFlyouts(); Pill.CloseFlyouts();
            PlaceByCard(Pill, Dashboard, Pill.WindowWidthNow, Pill.WindowHeightNow, Pill.Scale, WindowMorph.PillInset);
            Pill.WindowState = WindowState.Normal;
            var (card, radius, at) = PillInDashboard();
            var picture = WindowMorph.Capture(Pill);
            Dashboard.ShrinkTo(card, radius, picture, at, () =>
            {
                Pill.Show(); Dashboard.Hide(); BringToFront(Pill);
                Avalonia.Threading.DispatcherTimer.RunOnce(() => { if (visibility == visibilityRun) KeepPillReachable(); }, TimeSpan.FromMilliseconds(400));
            });
        };
        Pill.Closed += (_, _) => exit();
        Dashboard.Closed += (_, _) => exit();
        Pill.Closing += OnClosing; Dashboard.Closing += OnClosing;
        model.QuitRequested += Quit;
        // Tucks the timer away with tracking still running. Without a tray it goes to the taskbar instead, so it can never be lost.
        model.HideRequested += () =>
        {
            if (HideToTray) Hide();
            else foreach (var window in new Window[] { Pill, Dashboard }) if (window.IsVisible) window.WindowState = WindowState.Minimized;
        };
        KeepInFront();
        // The first open used to stutter while the dashboard's controls were first laid out and their templates built. Doing that once, unseen, as soon
        // as the pill is up, leaves the grow animation only drawing. Headless tests and any failure just skip it.
        Pill.Opened += WarmDashboard;
    }
    private bool warming; private double warmOpacity = 1;
    private void WarmDashboard(object? sender, EventArgs e)
    {
        Pill.Opened -= WarmDashboard;
        Avalonia.Threading.DispatcherTimer.RunOnce(() =>
        {
            try
            {
                if (Dashboard.IsVisible || !Pill.IsVisible || quitting || Motion.Reduced) return;
                // Shown once, fully see-through, far off screen and not in the taskbar, for a few frames: its native window, its drawing surface and every shadow and
                // control are built then, so the first grow from the pill only has to draw. Hidden again at once, so nobody sees it.
                warming = true; warmOpacity = Dashboard.Opacity;
                Dashboard.Opacity = 0; Dashboard.ShowActivated = false; Dashboard.ShowInTaskbar = false; Dashboard.Position = new PixelPoint(-32000, -32000);
                using var warmTiming = PerformanceTimings.Measure("warm-up show");
                Dashboard.Show();
                Avalonia.Threading.DispatcherTimer.RunOnce(() => EndWarm(true), TimeSpan.FromMilliseconds(500));
            }
            catch (Exception) { EndWarm(true); /* Only a warm-up; restore ordinary window state. */ }
        }, TimeSpan.FromMilliseconds(800));
    }
    // Ends the warm-up: the window is made ordinary again, and hidden unless somebody asked for the dashboard in the meantime.
    private void EndWarm(bool hide)
    {
        if (!warming) return;
        warming = false;
        try { if (hide) Dashboard.Hide(); }
        finally { Dashboard.Opacity = warmOpacity; Dashboard.ShowActivated = true; ApplyTaskbar(); }
    }
    /// <summary>
    /// Opens the dashboard the first time the pill is shown, which is where the getting started setup waits. It must happen once only: the pill raises
    /// Opened every time it is shown, so staying subscribed sent you straight back to the dashboard whenever you collapsed it while setup was pending,
    /// which is how every fresh data folder (a new Store install, say) starts.
    /// </summary>
    public void OpenDashboardOnFirstShow()
    {
        void First(object? sender, EventArgs e) { Pill.Opened -= First; ShowDashboard(); }
        Pill.Opened += First;
    }
    // Collapsing the dashboard must always leave the pill somewhere you can see it. If it came back off every screen, or something hid both windows
    // in the meantime, it is brought back to the main screen instead of leaving the timer reachable only from the tray.
    private void KeepPillReachable()
    {
        if (!IsShown) { ShowTimer(); return; }
        if (Pill.IsVisible) RescueOffScreen();
    }
    // Snapping another window over the timer, or a window that is itself on top, can leave the pill or dashboard behind it. So whenever one loses focus
    // it is put back at the front of the topmost windows, and a slow check does the same for one that was covered without a focus change.
    private void KeepInFront()
    {
        // Not while a topmost window of another app has the focus (the screenshot tool's overlay, say), which this would otherwise jump on top of.
        void Reassert(Window window) { if (window.Topmost && ZOrder.ShouldReassert(window.IsVisible, window.IsActive, window.WindowState == WindowState.Minimized, ZOrder.ForegroundIsTopmost())) { window.Topmost = false; window.Topmost = true; } }
        foreach (var window in new Window[] { Pill, Dashboard })
        {
            var self = window;
            self.Deactivated += (_, _) => Avalonia.Threading.DispatcherTimer.RunOnce(() => Reassert(self), TimeSpan.FromMilliseconds(250));
        }
        var check = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        check.Tick += (_, _) => { Reassert(Pill); Reassert(Dashboard); RescueOffScreen(); };
        check.Start();
        // A monitor being plugged or unplugged is dealt with at once, and once more when Windows has settled.
        Pill.Screens.Changed += (_, _) => { Avalonia.Threading.Dispatcher.UIThread.Post(RescueOffScreen); Avalonia.Threading.DispatcherTimer.RunOnce(RescueOffScreen, TimeSpan.FromMilliseconds(1500)); };
    }
    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // The close button hides the timer and leaves tracking running. Quitting, or Windows shutting down, saves and really closes.
        if (HideToTray && !quitting && e.CloseReason == WindowCloseReason.WindowClosing) { e.Cancel = true; Hide(); return; }
        // Windows closing the timer for a restart or shutdown must never be held up, or crashed, by a failed save.
        try { e.Cancel = !model.StopForClose(); }
        catch (Exception) { e.Cancel = false; }
    }
    public void Hide()
    {
        visibilityRun++;
        EndWarm(true);
        dashboardWasShown = Dashboard.IsVisible;
        Pill.Hide(); Dashboard.Hide();
    }
    public void ShowTimer()
    {
        EndWarm(true);
        if (IsShown) { (Dashboard.IsVisible ? (Window)Dashboard : Pill).Activate(); return; }
        if (dashboardWasShown) ShowDashboard(); else { Pill.Show(); BringToFront(Pill); }
        RescueOffScreen();
    }
    public void ShowDashboard()
    {
        EndWarm(false);
        if (!Dashboard.IsVisible) { model.ShowToday(); if (!Pill.IsVisible) Pill.FitToScreen(Pill.Screens.ScreenFromWindow(Pill) ?? Pill.Screens.Primary); PlaceByCard(Dashboard, Pill, DashboardWindow.DashboardWidth + 2 * Ui.ShadowMargin, DashboardWindow.DashboardHeight + 2 * Ui.ShadowMargin, 1); Dashboard.Show(); }
        Pill.CloseFlyouts(); Pill.Hide(); BringToFront(Dashboard);
        RescueOffScreen();
    }
    // The tray icon's left click shows the timer, or hides it when it is already in front.
    public void ToggleFromTray()
    {
        if ((Pill.IsVisible && Pill.IsActive) || (Dashboard.IsVisible && Dashboard.IsActive)) Hide(); else ShowTimer();
    }
    private void BringToFront(Window window)
    {
        // Raising it once keeps it on top only when that window is set to stay in front.
        window.Topmost = false; window.Topmost = window == Pill ? model.PillOnTop : model.DashboardOnTop; window.Activate();
    }
    public void ResetPosition()
    {
        // Back to the top right of the main screen, which also recovers a window dragged off a disconnected monitor.
        Dashboard.Hide();
        if (Pill.Screens.Primary is { } screen) Pill.FitToScreen(screen);
        Pill.Show(); PlacePillAtTopRight(); BringToFront(Pill);
    }
    // Windows is asked where the main monitor is, not Avalonia, whose list of screens and position setter did not bring the pill back after a monitor was
    // unplugged. The pill is placed once it is showing, so nothing puts it back where it was. Avalonia's own figures are the fallback.
    private void PlacePillAtTopRight()
    {
        if (ScreenGuard.TryPrimaryWorkArea(out var area, out var scale)
            && ScreenGuard.Place(Pill, ScreenGuard.TopRight(area, (int)Math.Round(Pill.WindowWidthNow * scale), (int)Math.Round(Pill.WindowHeightNow * scale), 24))) return;
        if (Pill.Screens.Primary is not { } screen) return;
        var work = screen.WorkingArea;
        Pill.Position = new PixelPoint(work.Right - (int)(Pill.Width * screen.Scaling) - 24, work.Y + 24);
    }
    /// <summary>
    /// Brings a window that is on no monitor back to the main one: the pill to its top right corner, the dashboard to the top left of the work area. It is run
    /// when the screens change, every few seconds, and whenever the timer is brought back from the tray, because a window left on an unplugged screen
    /// cannot be reached, and Windows does not always move one that was hidden when the screen went.
    /// </summary>
    public void RescueOffScreen()
    {
        try
        {
            if (Pill.IsVisible && Pill.WindowState != WindowState.Minimized && !ScreenGuard.IsOnAnyMonitor(Pill)) PlacePillAtTopRight();
            if (Dashboard.IsVisible && Dashboard.WindowState != WindowState.Minimized && !ScreenGuard.IsOnAnyMonitor(Dashboard)
                && ScreenGuard.TryPrimaryWorkArea(out var area, out _) && ScreenGuard.TryBoundsOf(Dashboard, out var now))
                ScreenGuard.Place(Dashboard, ScreenGuard.SlideInside(area, now with { X = area.X, Y = area.Y }));
        }
        catch (Exception) { /* Never let a failed rescue take the timer down. */ }
    }
    public void Quit()
    {
        if (!model.StopForClose()) { ShowTimer(); return; }
        quitting = true;
        Pill.Close(); Dashboard.Close();
    }
    /// <summary>
    /// Puts the target window so its card (the window less the room kept round it for the shadow) starts where the source's card starts, so the rail
    /// opens where the pill was and the pill comes back where the rail ended up. It slides up or left when it would run off the screen.
    /// The pill is drawn smaller than its design size, so its room round the card is smaller in the same proportion.
    /// </summary>
    /// <summary>How far, in device pixels, one window must sit from the other for their cards to line up: the room round a card shrinks with the pill's scale.</summary>
    public static int CardShift(double sourceScale, double targetScale, double screenScaling) => (int)Math.Round(Ui.ShadowMargin * (sourceScale - targetScale) * screenScaling);
    /// <summary>
    /// Where the pill's card is inside the dashboard's card (in device independent pixels, the dashboard's own coordinates), the pill's corner radius, and where the pill's
    /// whole window sits there (for its picture). Both windows have been placed, so this is read from their positions.
    /// </summary>
    private (Rect Card, double Radius, Rect Window) PillInDashboard()
    {
        var scaling = Pill.Screens.ScreenFromWindow(Pill)?.Scaling ?? Dashboard.Screens.ScreenFromWindow(Dashboard)?.Scaling ?? 1.0;
        var dashCard = new Point(Dashboard.Position.X + Ui.ShadowMargin * scaling, Dashboard.Position.Y + Ui.ShadowMargin * scaling);
        var window = new Rect((Pill.Position.X - dashCard.X) / scaling, (Pill.Position.Y - dashCard.Y) / scaling, Pill.WindowWidthNow, Pill.WindowHeightNow);
        var card = new Rect(window.X + Ui.ShadowMargin * Pill.Scale, window.Y + Ui.ShadowMargin * Pill.Scale, Pill.CardSizeNow.Width, Pill.CardSizeNow.Height);
        return (card, Pill.CardRadiusNow, window);
    }
    private static void PlaceByCard(Window target, Window source, double width, double height, double targetScale, double insetDips = 0)
    {
        var screen = source.Screens.ScreenFromWindow(source) ?? target.Screens.ScreenFromWindow(target);
        if (screen is null) return;
        var area = screen.WorkingArea; var scale = screen.Scaling;
        var sourceScale = source is PillWindow pill ? pill.Scale : 1.0;
        target.Width = Math.Min(width, area.Width / scale);
        target.Height = Math.Min(height, area.Height / scale);
        // The card sits one margin in from its window, and the margin shrinks with the pill's scale.
        var shift = CardShift(sourceScale, targetScale, scale) + (int)Math.Round(insetDips * scale);
        target.Position = new PixelPoint(
            Math.Clamp(source.Position.X + shift, area.X, Math.Max(area.X, area.Right - (int)(target.Width * scale))),
            Math.Clamp(source.Position.Y + shift, area.Y, Math.Max(area.Y, area.Bottom - (int)(target.Height * scale))));
    }
}

public sealed class DashboardWindow : Window
{
    /// <summary>The dashboard's fixed size, before the room kept around it for its shadow: the page keeps the 982 pixels it had, and the sidebar's width is added to it.</summary>
    public const double DashboardWidth = 1100 - 76 + Sidebar.Width, DashboardHeight = 760;
    public Action? OpenPill { get; set; }
    private readonly TimerModel model;
    private readonly TextBlock workspaceTitle = new() { FontSize = 26, Classes = { "clock" }, VerticalAlignment = VerticalAlignment.Center };
    private Button todayButton = null!; private TextBlock todayWords = null!;
    private Control? totalsStrip; private Button zoomOut = null!, zoomIn = null!;
    private Sidebar sidebar = null!; private StatusFace face = null!;
    private readonly TextBlock workingValue = Value(), allocatedValue = Value(), unallocatedValue = Value();
    private static TextBlock Value() => new() { FontSize = 14, FontWeight = Avalonia.Media.FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock date = Ui.Text("", 14); private Button datePick = null!;
    private readonly ContentControl workspace = new(), timelineHost = new() { Name = "TimelineHost" };
    private Border timelineCard = null!; private MessageBar messageBar = null!; private Grid timelinePage = null!; private SelectedBlockCard selectedCard = null!; private NotesCard notesCard = null!;
    private Button timelineRail = null!, previous = null!, next = null!, overviewTab = null!, notesTab = null!, addTime = null!, bell = null!; private Control viewTabs = null!;
    private Border bellBadge = null!; private Control bellHostPanel = null!; private TextBlock bellBadgeText = null!; private TextBox searchBox = null!; private Control searchHost = null!; private Popup bellPopup = null!; private NotesPage? notesPage;
    private readonly Button undo, redo;
    private EntryDialog? entryEditor;
    private bool weekMode, settingsMode, notesMode, renderedSettings, renderedNotes;
    private Button settingsRail = null!; private Control? headerRow;
    private readonly Grid layers = new(); private Control? dashboardBody; private OnboardingView? onboarding;
    /// <summary>The pop-ups open over the dashboard. They share its blurred backdrop with the getting started setup.</summary>
    public DialogHost Dialogs { get; } = new();
    private bool? renderedWeekMode;
    private DateTime? renderedDate;
    public DashboardWindow(TimerModel model)
    {
        this.model = model;
        Title = "Basic’s Advanced Timer"; WindowChrome.Attach(this);
        ContextMenu = Ui.SettingsMenu(model);
        Width = DashboardWidth + 2 * Ui.ShadowMargin;
        Height = DashboardHeight + 2 * Ui.ShadowMargin;
        Topmost = model.DashboardOnTop; model.PropertyChanged += (_, _) => { if (Topmost != model.DashboardOnTop) Topmost = model.DashboardOnTop; }; // In front of other windows unless switched off in settings.
        CanResize = false;
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Avalonia.Media.Brushes.Transparent;
        // The sidebar is the pill drawn out with its accounts shown: it runs the full height with the lights, the day clock and the tracking card at its top, the accounts
        // below them, and Quit timer at its foot. The workspace beside it has a header with the three views as tabs, the date and the gear.
        var root = Ui.Grid("Auto,*", "*");
        // The clock goes back to the pill, and the play tile pauses or resumes, as on the pill.
        sidebar = new Sidebar(model, () => OpenPill?.Invoke(), OnPlay, () => model.OpenAccounts(), () => model.RequestQuit());
        face = sidebar.Face;
        Ui.Put(root, sidebar);
        model.OpenDayRequested += _ => SelectTab(false);
        model.PageRequested += page => { if (page == "week") SelectTab(true); else if (page == "notes") ShowNotes(); };
        // A pin on the timeline opens its note for editing in the Notes card; the Notes page's own open request shows that page.
        model.Notes.OpenNoteRequested += id => { ShowNotes(); NotesPageInstance().OpenNote(id); };
        var body = Ui.Grid("*", "*");
        var review = Ui.Grid("*", "Auto,*");
        review.Margin = new Thickness(2, 16, 22, 18); // Two on the left, so the sidebar rows and the first card are 14 apart, as the cards are from each other.
        // The workspace's name on the left; on the right the date to step through and a button back to today.
        // In Week views the arrows move a whole week, and forward stops at the current week. In Timeline they move a day.
        // The date has a fixed width, so the arrows stay put whatever its text says.
        var nav = Ui.Grid("*,Auto,Auto", "*"); nav.VerticalAlignment = VerticalAlignment.Center;
        // The tabs (36 tall on a 3 pixel track) set the header's height; the settings page has no tabs, so without a fixed height the row shrank and the gear moved.
        nav.Height = 42;
        // The three views are tabs where the page's name was: Timeline, Notes, Week overview. The settings page has its name there instead.
        Button ViewTab(IconKind kind, string name, string tip, Action click)
        {
            var tab = Ui.Button("", click, name); tab.Classes.Add("viewTab");
            tab.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new IconGlyph(kind), new TextBlock { Text = tip, VerticalAlignment = VerticalAlignment.Center } } };
            Avalonia.Automation.AutomationProperties.SetName(tab, tip); return tab;
        }
        timelineRail = ViewTab(IconKind.Timeline, "TimelineTab", "Timeline", () => SelectTab(false));
        notesTab = ViewTab(IconKind.NotesList, "NotesTab", "Notes", ShowNotes);
        overviewTab = ViewTab(IconKind.Week, "WeekOverviewTab", "Week overview", () => SelectTab(true));
        var tabRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 }; foreach (var tab in new[] { timelineRail, notesTab, overviewTab }) tabRow.Children.Add(tab);
        var tabTrack = new Border { Name = "ViewTabs", Child = tabRow, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left }; tabTrack.Classes.Add("viewTabs"); viewTabs = tabTrack;
        var leftOfHeader = new Grid { Children = { tabTrack, workspaceTitle }, Margin = new Thickness(4, 0, 0, 0) }; workspaceTitle.HorizontalAlignment = HorizontalAlignment.Left;
        Ui.Put(nav, leftOfHeader); headerRow = nav;
        date.Width = 124; date.TextAlignment = Avalonia.Media.TextAlignment.Center;
        // Pressing the date opens a calendar to jump to any day, so a day weeks away is one click instead of many arrows.
        datePick = Ui.Ctl("DatePick", null, null, "Pick a day", () => dateCalendar.IsOpen = !dateCalendar.IsOpen);
        datePick.Width = double.NaN; datePick.Padding = new Thickness(12, 0); datePick.Content = Ui.Horizontal(new IconGlyph(IconKind.Today), date);
        // The calendar hangs from the date button by a point, like the bell's card, and the same button closes it again.
        var datePicker = new SoftDatePicker { Name = "DateCalendar", BoxShadow = HangingCard.Shadow }; datePicker.Classes.Add("flyoutCard");
        dateCalendar = new Popup { Placement = Avalonia.Controls.PlacementMode.Bottom, Name = "DateCalendarPopup", PlacementTarget = datePick, Child = HangingCard.Hang(datePicker, -1), VerticalOffset = HangingCard.TopOffset };
        HangingCard.CloseOnOutsidePress(dateCalendar, datePick);
        dateCalendar.Opened += (_, _) => { datePicker.View(model.SelectedDate, model.SelectedDate); datePick.Classes.Set("on", true); };
        dateCalendar.Closed += (_, _) => { if (((Avalonia.LogicalTree.ILogical)datePick).IsAttachedToLogicalTree) datePick.Classes.Set("on", false); };
        datePicker.DayPicked += picked => { dateCalendar.IsOpen = false; model.GoToDate(picked, weekMode); };
        previous = Ui.Ctl("PreviousPeriod", null, IconKind.Previous, "Previous day or week", () => { if (weekMode) model.ChangeWeek(-1); else model.ChangeDate(-1); });
        next = Ui.Ctl("NextPeriod", null, IconKind.Next, "Next day or week", () => { if (weekMode) model.ChangeWeek(1); else model.ChangeDate(1); });
        todayButton = Ui.Ctl("GoToToday", "Today", IconKind.Today, "Back to today", () => { if (weekMode) model.GoToDate(model.Engine.Now.Date, true); else model.ChangeDate((model.Engine.Now.Date - model.SelectedDate.Date).Days); });
        todayWords = (TextBlock)((StackPanel)todayButton.Content!).Children[1];
        // The bell opens what is waiting (the reminders that have passed, and the ones coming up), with a count of the ones that need attention. In Notes the header also holds the search box.
        bell = Ui.Ctl("Notifications", null, IconKind.Bell, "Notifications", ToggleBell); bell.Width = 42;
        // A margin of 1 on the left and top moves the digit half a pixel right and down: a 1 is weighted to its left, and the digits sit a little high in their line box, so the number looked off the dot's centre.
        bellBadgeText = new TextBlock { FontSize = 11, FontWeight = Avalonia.Media.FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(1, 1, 0, 0) };
        bellBadgeText.Bind(TextBlock.ForegroundProperty, bellBadgeText.GetResourceObservable("Panel"));
        bellBadge = new Border { Name = "NotificationsBadge", MinWidth = 18, Height = 18, CornerRadius = new CornerRadius(9), Padding = new Thickness(4, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -6, -6, 0), IsHitTestVisible = false, IsVisible = false, Child = bellBadgeText }; bellBadge.Classes.Add("badge");
        bellPopup = new Popup { Placement = PlacementMode.BottomEdgeAlignedRight, Name = "NotificationsPopup" }; HangingCard.CloseOnOutsidePress(bellPopup, bell);
        bellPopup.Opened += (_, _) => bell.Classes.Set("on", true); bellPopup.Closed += (_, _) => { if (((Avalonia.LogicalTree.ILogical)bell).IsAttachedToLogicalTree) bell.Classes.Set("on", false); };
        var bellHost = new Grid { Name = "NotificationsHost" }; bellHostPanel = bellHost; bellHost.Children.Add(bell); bellHost.Children.Add(bellBadge); bellHost.Children.Add(bellPopup); bellPopup.PlacementTarget = bell;
        searchBox = new TextBox { Name = "NotesSearch", Watermark = "Search notes", Width = 240, Height = 38, MinHeight = 38, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(34, 0, 8, 0) };
        searchBox.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty && notesPage is not null) notesPage.Query = searchBox.Text ?? ""; };
        var searchGrid = new Grid { Width = 240 }; searchGrid.Children.Add(searchBox); searchGrid.Children.Add(new IconGlyph(IconKind.Search) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(11, 0, 0, 0), IsHitTestVisible = false, Opacity = .7 });
        searchHost = searchGrid;
        var dateHost = new Grid { Children = { datePick, dateCalendar } };
        todayButton.Margin = new Thickness(8, 0, 0, 0);
        searchHost.Margin = new Thickness(0, 0, 8, 0); dateHost.Margin = new Thickness(8, 0, 0, 0); next.Margin = new Thickness(8, 0, 0, 0); bellHost.Margin = new Thickness(8, 0, 0, 0);
        var steps = Ui.Horizontal(searchHost, previous, dateHost, next, todayButton, bellHost); steps.Spacing = 0;
        Ui.Put(nav, steps, 1);
        // Settings is a gear at the top right, which also closes it again.
        settingsRail = Ui.Ctl("Settings", null, IconKind.Gear, "Settings", ShowSettings); settingsRail.Margin = new Thickness(8, 0, 0, 0);
        Ui.Put(nav, settingsRail, 2);
        Ui.Put(review, nav);
        // The bar under the timeline, as in the mock: Add time and Undo on the left, the day's figures in the middle and the zoom buttons on the right.
        addTime = Ui.Ctl("AddTime", "Add time", IconKind.Plus, "Add time to this day", () => model.OpenGap(), tint: "Accent"); addTime.Classes.Add("accentOutline");
        undo = Ui.Ctl("UndoEdit", null, IconKind.Undo, "Undo", () => model.UndoEdit(), key: "Ctrl Z");
        redo = Ui.Ctl("RedoEdit", null, IconKind.Redo, "Redo", () => model.RedoEdit(), key: "Ctrl Y");
        // Zoom steps by a fifth each press, about the middle of what is showing. Hold Ctrl and use the wheel to zoom about the pointer.
        ScrollViewer? TimelineScroll() => timelineHost.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(s => s.Name == "TimelineScroll");
        zoomOut = Ui.Ctl("TimelineZoomOut", null, IconKind.Minus, "Zoom out", () => { if (TimelineScroll() is { } s) model.ZoomTimeline(-1, s.Viewport.Width / 2, s.Offset.X); }, key: "Ctrl scroll");
        zoomIn = Ui.Ctl("TimelineZoomIn", null, IconKind.Plus, "Zoom in", () => { if (TimelineScroll() is { } s) model.ZoomTimeline(1, s.Viewport.Width / 2, s.Offset.X); }, key: "Ctrl scroll");
        var zoom = Ui.KeyPair(zoomOut, zoomIn, "TimelineControls");
        totalsStrip = BuildTotals();
        var tools = Ui.Grid("Auto,*,Auto"); tools.Margin = new Thickness(14, 13, 14, 0);
        var editKeys = Ui.Horizontal(addTime, Ui.KeyPair(undo, redo, "EditKeys")); editKeys.Spacing = 8; editKeys.VerticalAlignment = VerticalAlignment.Center; zoom.VerticalAlignment = VerticalAlignment.Center; Ui.Put(tools, editKeys); Ui.Put(tools, totalsStrip, 1); Ui.Put(tools, zoom, 2);
        workspace.Margin = new Thickness(0, 14, 0, 0); // Room under the title and date, so content never touches them.
        workspace.ClipToBounds = false; // A content control clips by default, which cut the raised cards' shadows square at the page's edge; the lists inside scroll and clip themselves.
        Ui.Put(review, workspace, row: 1);
        // The timeline is a card (the day across the top, its bar of buttons and figures under it) over the Selected block card. The week and the settings
        // fill the workspace instead, so these two are shown for the timeline alone.
        // The day itself sits in a sunk trough set into the card, as the mock draws it: the card is the stage, the timeline a place you look into.
        // The left and right edges are the display's lip: they cast a soft shadow over the day, so it reads as lying below them. Room above the hour labels keeps them off the top edge.
        var lip = new Grid(); lip.Children.Add(timelineHost);
        Border LipShadow(bool left)
        {
            var edge = new Border { Width = 18, IsHitTestVisible = false, HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right, Name = left ? "TimelineLipLeft" : "TimelineLipRight" };
            void Paint()
            {
                var ink = Ui.ThemeColour(edge, "ShadowColor", Colors.Black); var dark = edge.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
                var strong = Color.FromArgb((byte)(dark ? 60 : 28), ink.R, ink.G, ink.B); var none = Color.FromArgb(0, ink.R, ink.G, ink.B);
                edge.Background = new LinearGradientBrush { StartPoint = new RelativePoint(left ? 0 : 1, .5, RelativeUnit.Relative), EndPoint = new RelativePoint(left ? 1 : 0, .5, RelativeUnit.Relative), GradientStops = { new GradientStop(strong, 0), new GradientStop(none, 1) } };
            }
            edge.ActualThemeVariantChanged += (_, _) => Paint(); edge.AttachedToVisualTree += (_, _) => Paint();
            return edge;
        }
        lip.Children.Add(LipShadow(true)); lip.Children.Add(LipShadow(false));
        var trough = new Border { Name = "TimelineTrough", Child = lip, CornerRadius = new CornerRadius(14), Margin = new Thickness(14, 0, 14, 0), Padding = new Thickness(0, 6, 0, 0), ClipToBounds = true }; trough.Classes.Add("trough");
        var cardGrid = Ui.Grid("*", "Auto,Auto"); Ui.Put(cardGrid, trough); Ui.Put(cardGrid, tools, 0, 1);
        timelineCard = new Border { Name = "TimelineCard", Child = cardGrid, CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1), Padding = new Thickness(0, 14, 0, 9), VerticalAlignment = VerticalAlignment.Top };
        timelineCard.Classes.Add("dayCard");
        selectedCard = new SelectedBlockCard(model) { Margin = new Thickness(0, 14, 7, 0) };
        notesCard = new NotesCard(model) { Margin = new Thickness(7, 14, 0, 0) };
        timelinePage = Ui.Grid("1.15*,*", "Auto,*"); timelinePage.Margin = new Thickness(0, 14, 0, 0); timelinePage.Name = "TimelinePage";
        Ui.Put(timelinePage, timelineCard); Grid.SetColumnSpan(timelineCard, 2); Ui.Put(timelinePage, selectedCard, 0, 1); Ui.Put(timelinePage, notesCard, 1, 1);
        Ui.Put(review, timelinePage, row: 1);
        // The title row's empty middle still drags the window.
        var rightColumn = Ui.Grid("*", "*");
        Ui.Put(rightColumn, review); Ui.DragHeader(nav, this);
        Ui.Put(body, rightColumn);
        Ui.Put(root, body, 2);
        // Notes, refused edits and failed saves float over the lowest part of the workspace, so they never push anything up.
        messageBar = new MessageBar(model); Ui.Put(review, messageBar, row: 1);
        // The setup overlay, when it is wanted, sits over everything in the card, which is blurred and cannot be clicked while it is up.
        layers.Children.Add(root); dashboardBody = root;
        layers.Children.Add(new FrameChip(() => settingsMode ? "Settings" : notesMode ? "Notes" : weekMode ? "Week overview" : "Timeline")); // Only shows while the frame monitor runs (Settings, Development).
        layers.Children.Add(Dialogs); Dialogs.OpenChanged += UpdateBackdrop; // Last, so a dialog is above the setup too.
        surface = new Border { Name = "DashboardSurface", Child = layers, ClipToBounds = true };
        surface.Classes.Add("surface");
        // The picture of the pill that the dashboard grows from (and shrinks to) sits over everything inside the card; it only shows while the window is moving.
        morphOverlay = new Avalonia.Controls.Image { Name = "MorphPill", Stretch = Avalonia.Media.Stretch.Fill, IsHitTestVisible = false, IsVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        layers.Children.Add(morphOverlay);
        shadowHost = Ui.Shadowed(surface, 20); shadowFade = shadowHost.Transitions; Content = shadowHost;
        model.PropertyChanged += (_, _) => UpdateShadow();
        ActualThemeVariantChanged += (_, _) => UpdateShadow();
        UpdateShadow();
        // The window can be picked up by any bare part of its top: the whole identity block and the accounts heading on the left, and the tab
        // and action rows on the right, not only the thin strips between the controls. Buttons, boxes and the timeline itself are left alone.
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || !Ui.IsPlainSurface(e.Source) || !InDragZone(e.GetPosition(this))) return;
            Ui.CloseMenus(this); BeginMoveDrag(e); e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        // Left and right arrow keys step by a week or a day, like the buttons, unless a box is being typed in.
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (settingsMode || e.KeyModifiers != Avalonia.Input.KeyModifiers.None || e.Key is not (Avalonia.Input.Key.Left or Avalonia.Input.Key.Right)) return;
            if (FocusManager?.GetFocusedElement() is TextBox or ComboBox) return;
            var step = e.Key == Avalonia.Input.Key.Left ? -1 : 1;
            if (weekMode) { if (step < 0 || model.CanStepForwardAWeek) model.ChangeWeek(step); } else model.ChangeDate(step);
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        KeyDown += (_, e) =>
        {
            // Escape takes the top dialog away first, and only then clears the selection.
            if (e.Key == Avalonia.Input.Key.Escape) { if (Dialogs.CloseTop()) { } else model.ClearSelection(); e.Handled = true; }
            // Ctrl+C copies the selected blocks and Ctrl+V pastes them on the timeline, but never while typing in a text box.
            else if (e.KeyModifiers == Avalonia.Input.KeyModifiers.Control && e.Key is Avalonia.Input.Key.C or Avalonia.Input.Key.V && !settingsMode && FocusManager?.GetFocusedElement() is not TextBox)
            {
                if (e.Key == Avalonia.Input.Key.C) model.CopySelection(); else if (!weekMode) model.PasteBlocks(model.DefaultPasteTime());
                e.Handled = true;
            }
            // Ctrl+Z undoes the last edit on the Timeline page, but never while typing in a box (which keeps its own undo) or behind a dialog.
            else if (e.KeyModifiers == Avalonia.Input.KeyModifiers.Control && e.Key == Avalonia.Input.Key.Z && !settingsMode && !weekMode && !notesMode && !Dialogs.Any
                && FocusManager?.GetFocusedElement() is not (TextBox or ComboBox))
            { if (model.Editor.CanUndo) model.UndoEdit(); e.Handled = true; }
            // Ctrl+Y (or Ctrl+Shift+Z) puts back what Ctrl+Z took back, under the same rules.
            else if ((e.KeyModifiers == Avalonia.Input.KeyModifiers.Control && e.Key == Avalonia.Input.Key.Y
                    || e.KeyModifiers == (Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Shift) && e.Key == Avalonia.Input.Key.Z)
                && !settingsMode && !weekMode && !notesMode && !Dialogs.Any && FocusManager?.GetFocusedElement() is not (TextBox or ComboBox))
            { if (model.Editor.CanRedo) model.RedoEdit(); e.Handled = true; }
            // + and - lengthen or shorten the selected block by a minute (Shift is allowed, since + needs it on most keyboards), never while typing.
            else if (e.KeyModifiers is Avalonia.Input.KeyModifiers.None or Avalonia.Input.KeyModifiers.Shift && !settingsMode && !weekMode && !notesMode && !Dialogs.Any
                && e.Key is Avalonia.Input.Key.OemPlus or Avalonia.Input.Key.Add or Avalonia.Input.Key.OemMinus or Avalonia.Input.Key.Subtract
                && FocusManager?.GetFocusedElement() is not (TextBox or ComboBox))
            { if (model.SelectedRows.Count > 0) { model.NudgeSelection(e.Key is Avalonia.Input.Key.OemPlus or Avalonia.Input.Key.Add ? 1 : -1); e.Handled = true; } }
            // Backspace and Delete remove the selected blocks, but never while typing in a text box.
            else if (e.Key is Avalonia.Input.Key.Back or Avalonia.Input.Key.Delete && FocusManager?.GetFocusedElement() is not TextBox)
            { if (model.SelectedRows.Count > 0) { model.DeleteSelection(); e.Handled = true; } }
        };
        model.PropertyChanged += Changed;
        model.PropertyChanged += (_, _) => UpdateOnboarding();
        model.EntryRequested += ShowEntry;
        model.AccountsRequested += ShowAccounts;
        Opened += (_, _) => FlushRefresh();
        PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty && !IsVisible) CancelMorph(); };
        Closed += (_, _) => { CancelMorph(); SetPillPicture(null, default); model.PropertyChanged -= Changed; model.EntryRequested -= ShowEntry; model.AccountsRequested -= ShowAccounts; };
        Refresh(true);
        dashboardReady = true;
        UpdateOnboarding();
    }
    // ---- the shadow, and the grow and shrink between the pill and the dashboard
    private Border surface = null!, shadowHost = null!; private Avalonia.Controls.Image morphOverlay = null!;
    private (bool, Avalonia.Styling.ThemeVariant)? lastShadow;
    private void UpdateShadow()
    {
        var state = (model.Shadows, ActualThemeVariant);
        if (lastShadow != state) { lastShadow = state; if (morph >= 1) Ui.SetShadow(shadowHost, model.Shadows, null); }
    }
    private Avalonia.Animation.Transitions? shadowFade;
    private double morph = 1, morphRadius = WindowMorph.DashboardRadius; private Rect morphFrom;
    private AnimationFramePump? morphPump;
    private TransitionTimings.Transition? transitionTiming;
    private Avalonia.Threading.DispatcherTimer? morphWatchdog;
    private bool dashboardReady, refreshPending, workspacePending;
    /// <summary>How far the dashboard has grown from the pill, 0 (the pill's card) to 1 (the whole window).</summary>
    public double MorphProgress => morph;
    /// <summary>Whether the window is in the middle of growing or shrinking, when nothing in it can be pressed.</summary>
    public bool Morphing => morphPump?.Running == true || morph < 1;
    /// <summary>How much of the dashboard shows, and whether it takes presses (for tests).</summary>
    public double ContentOpacity => dashboardBody!.Opacity; public bool ContentTakesPresses => layers.IsHitTestVisible;
    /// <summary>
    /// Sets how far the dashboard has grown from the pill's card (<paramref name="from"/>, in the card's own coordinates), 0 to 1: the content is clipped to a rounded rectangle
    /// between the two, the pill's picture fades out as the dashboard fades in, and at 1 everything is back to normal.
    /// </summary>
    public void SetMorph(double m)
    {
        morph = Math.Clamp(m, 0, 1);
        var full = new Rect(0, 0, DashboardWidth, DashboardHeight);
        if (morph >= 1)
        {
            surface.Clip = null; dashboardBody!.Opacity = 1; layers.IsHitTestVisible = true; morphOverlay.IsVisible = false;
            shadowHost.Transitions = shadowFade; lastShadow = null; UpdateShadow();
            return;
        }
        surface.Clip = WindowMorph.RoundedRect(WindowMorph.Lerp(morphFrom, full, morph), morphRadius + (WindowMorph.DashboardRadius - morphRadius) * morph);
        dashboardBody!.Opacity = WindowMorph.DashboardOpacity(morph);
        morphOverlay.Opacity = WindowMorph.PillOpacity(morph); morphOverlay.IsVisible = morphOverlay.Source is not null && morphOverlay.Opacity > 0;
        layers.IsHitTestVisible = false;
        // The card's shadow comes back when it has all grown. Its fade is switched off while it moves, or a shadow left from before the window was shown faded out over
        // the first 350 ms, and the window wore a shadow round a card that was still small.
        if (shadowHost.Transitions is not null) shadowHost.Transitions = null;
        shadowHost.BoxShadow = default;
    }
    private void StartMorph(double from, double to, bool closing, Action? done)
    {
        morphPump?.Stop(); morphWatchdog?.Stop();
        transitionTiming ??= TransitionTimings.Begin(closing ? "collapse" : "expand");
        if (!WindowMorph.Enabled || Motion.Reduced) { SetMorph(to); CompleteMorph(done, "instant"); return; }
        SetMorph(from);
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var previous = 0d;
        var first = true;
        var direction = closing ? "collapse" : "expand";
        morphPump ??= new AnimationFramePump(RequestAnimationFrame,
            () => System.Diagnostics.Stopwatch.GetTimestamp() * 1000d / System.Diagnostics.Stopwatch.Frequency);
        morphWatchdog ??= new Avalonia.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(25), Avalonia.Threading.DispatcherPriority.Render,
            (_, _) => morphPump.Watchdog());
        morphPump.Start(closing ? WindowMorph.CloseMilliseconds : WindowMorph.OpenMilliseconds, elapsed =>
        {
            elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            PerformanceTimings.Record(direction + (first ? " first callback" : " callback gap"), elapsed - previous);
            previous = elapsed; first = false;
            var eased = WindowMorph.Ease(elapsed, closing);
            SetMorph(closing ? 1 - eased : eased);
        }, () =>
        {
            morphWatchdog.Stop();
            SetMorph(to);
            PerformanceTimings.Record(direction + " total", System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            CompleteMorph(done, "completed");
        }, (elapsed, watchdog) => transitionTiming?.Sample(elapsed, watchdog));
        morphWatchdog.Start();
        layers.IsHitTestVisible = false;
    }
    private void CancelMorph()
    {
        morphPump?.Stop(); morphWatchdog?.Stop();
        transitionTiming?.Finish("cancelled"); transitionTiming = null;
        SetMorph(1);
    }
    public void PrepareMorph(bool closing)
    {
        CancelMorph();
        transitionTiming = TransitionTimings.Begin(closing ? "collapse" : "expand");
    }
    private void CompleteMorph(Action? done, string outcome)
    {
        var timing = transitionTiming; transitionTiming = null;
        done?.Invoke();
        FlushRefresh();
        timing?.Finish(outcome);
    }
    private void FlushRefresh()
    {
        if (!refreshPending || !IsVisible || morphPump?.Running == true) return;
        var workspace = workspacePending;
        refreshPending = workspacePending = false;
        Refresh(workspace);
    }
    private void SetPillPicture(Avalonia.Media.Imaging.Bitmap? picture, Rect where)
    {
        var previous = morphOverlay.Source;
        morphOverlay.Source = picture;
        if (!ReferenceEquals(previous, picture)) (previous as IDisposable)?.Dispose();
        morphOverlay.Margin = new Thickness(where.X, where.Y, 0, 0); morphOverlay.Width = where.Width; morphOverlay.Height = where.Height;
    }
    /// <summary>Shows the window grown from the pill's card: it starts as the pill and eases out to the whole dashboard.</summary>
    /// <param name="card">The pill's card in this window's card coordinates.</param><param name="radius">The pill's corner radius.</param>
    /// <param name="picture">A picture of the pill window, drawn at <paramref name="pictureAt"/> (its window's top left and size in the same coordinates).</param>
    public void GrowFrom(Rect card, double radius, Avalonia.Media.Imaging.Bitmap? picture, Rect pictureAt)
    {
        morphFrom = card; morphRadius = radius; SetPillPicture(picture, pictureAt); SetMorph(0);
        Show(); StartMorph(0, 1, false, null);
    }
    /// <summary>Shrinks the window back to the pill's card and then calls <paramref name="done"/>, when the real pill takes over and this window is hidden.</summary>
    public void ShrinkTo(Rect card, double radius, Avalonia.Media.Imaging.Bitmap? picture, Rect pictureAt, Action done)
    {
        morphFrom = card; morphRadius = radius; SetPillPicture(picture, pictureAt);
        StartMorph(1, 0, true, () => { done(); SetMorph(1); });
    }

    /// <summary>The account list's scroll box, so tests can read its limit.</summary>
    public ScrollViewer? AccountScroll => sidebar.AccountScroll;
    /// <summary>The sidebar, for tests.</summary>
    public Sidebar SidebarPanel => sidebar;
    /// <summary>Nothing on the dashboard hangs open any more; kept so the coordinator can still put every window's flyouts away together.</summary>
    public void CloseFlyouts() { }
    private void OnPlay()
    {
        var chosen = model.Accounts.Any(a => a.Name == model.SelectedAccount);
        // With nothing to run or resume, the accounts are already there to pick from, so it says so.
        if (model.IsTracking || chosen) model.TogglePause(); else model.Refuse("Choose an account");
    }
    // The day's figures under the timeline: Working time, a bar of the scheduled day, then Allocated, Unallocated and Breaks.
    private readonly TextBlock breaksValue = Value(); private readonly Border dayBar = new() { Name = "DayBar", Width = 150, Height = 10, CornerRadius = new CornerRadius(5), ClipToBounds = true, VerticalAlignment = VerticalAlignment.Center };
    private Control BuildTotals()
    {
        Control Stat(string label, TextBlock value, string name, Control? dot = null)
        {
            var words = Ui.Text(label, 11, "muted"); words.VerticalAlignment = VerticalAlignment.Center;
            var head = dot is null ? (Control)words : Ui.Horizontal(dot, words);
            if (head is StackPanel headRow) headRow.Spacing = 5;
            var column = new StackPanel { Name = name, Spacing = 0 }; column.Children.Add(head); column.Children.Add(value); return column;
        }
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0, Name = "DayTotals", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        workingValue.Name = "TotalsWorkingValue";
        strip.Children.Add(Stat("Working time", workingValue, "TotalsWorking"));
        dayBar.Margin = new Thickness(16, 0, 0, 0);
        strip.Children.Add(dayBar);
        var allocated = Stat("Allocated", allocatedValue, "TotalsAllocated"); allocated.Margin = new Thickness(16, 0, 0, 0); strip.Children.Add(allocated);
        var unallocated = Stat("Unallocated", unallocatedValue, "TotalsUnallocated"); unallocated.Margin = new Thickness(16, 0, 0, 0); strip.Children.Add(unallocated);
        var amber = new Avalonia.Controls.Shapes.Ellipse { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center }; amber.Bind(Avalonia.Controls.Shapes.Shape.FillProperty, amber.GetResourceObservable("Break"));
        var breaksStat = Stat("Breaks", breaksValue, "TotalsBreaks", amber); breaksStat.Margin = new Thickness(16, 0, 0, 0); strip.Children.Add(breaksStat);
        return strip;
    }
    // The bar is the scheduled day, its hours less the break allowance: time on accounts solid, working time with nothing on an account hatched. A day off has no bar.
    private Grid? totalsBarGrid;
    private Border? totalsBarStripes;
    private string? totalsBarColour;
    private void UpdateTotals()
    {
        var shown = model.SelectedDate.Date;
        var totals = model.Engine.Totals(shown); var plan = model.Engine.Plan(shown);
        var working = model.WorkingOn(shown);
        workingValue.Text = model.Format(working); allocatedValue.Text = model.Format(totals.Allocated); unallocatedValue.Text = model.Format(model.UnallocatedOn(shown));
        breaksValue.Text = model.Format(model.BreakTimeOn(shown));
        var scheduled = model.ScheduledOn(shown);
        // The bar is always there and always the same width, so the figures beside it never move from one day to the next. A day with nothing scheduled and no time
        // shows an empty track; a day with time but no schedule (a weekend) shows the time on it, all of it as working time.
        var breaks = model.BreakTimeOn(shown);
        if (scheduled <= TimeSpan.Zero && working <= TimeSpan.Zero)
        {
            dayBar.Child = null; dayBar.Classes.Add("track");
            ToolTip.SetTip(dayBar, "No schedule"); Avalonia.Automation.AutomationProperties.SetName(dayBar, "Nothing is scheduled or recorded");
        }
        else
        {
            // The bar is as long as the longer of the schedule (its hours less the break allowance) and the time worked, so time past the schedule has room at the end.
            var whole = Math.Max(scheduled.TotalSeconds, working.TotalSeconds);
            var allocatedPart = Math.Min(totals.Allocated.TotalSeconds, working.TotalSeconds);
            var unallocatedPart = Math.Max(0, working.TotalSeconds - allocatedPart);
            if (totalsBarGrid is null)
            {
                totalsBarGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*") };
                var allocated = new Border { Name = "DayBarAllocated" }; allocated.Classes.Add("barAllocated");
                totalsBarStripes = new Border { Name = "DayBarUnallocated", Opacity = .7, [Grid.ColumnProperty] = 1 };
                totalsBarGrid.Children.Add(allocated); totalsBarGrid.Children.Add(totalsBarStripes);
            }
            totalsBarGrid.ColumnDefinitions[0].Width = new GridLength(allocatedPart, GridUnitType.Star);
            totalsBarGrid.ColumnDefinitions[1].Width = new GridLength(unallocatedPart, GridUnitType.Star);
            totalsBarGrid.ColumnDefinitions[2].Width = new GridLength(Math.Max(0, whole - allocatedPart - unallocatedPart), GridUnitType.Star);
            var colour = model.CurrentTheme.Colour("Muted");
            if (totalsBarColour != colour) { totalsBarStripes!.Background = ReviewViews.Stripes(model); totalsBarColour = colour; }
            if (!ReferenceEquals(dayBar.Child, totalsBarGrid)) dayBar.Child = totalsBarGrid;
            dayBar.Classes.Add("track");
            ToolTip.SetTip(dayBar, (scheduled > TimeSpan.Zero ? $"Scheduled {model.Format(scheduled)} ({plan.Start:HH:mm}–{plan.End:HH:mm}" + (model.BreakAllowance > TimeSpan.Zero ? $" less {model.Format(model.BreakAllowance)} break" : "") + ")" : "No schedule") + $"\nAllocated {model.Format(totals.Allocated)} · Unallocated {model.Format(model.UnallocatedOn(shown))} · Breaks {model.Format(breaks)}");
            Avalonia.Automation.AutomationProperties.SetName(dayBar, $"The scheduled day, {model.Format(working)} worked");
        }
    }
    /// <summary>Whether the getting started setup is showing over the dashboard.</summary>
    public bool SetupShowing => onboarding is not null;
    // The setup shows for as long as the model says it is not done, over a blurred dashboard that cannot be clicked, and goes when it is finished or skipped.
    private void UpdateOnboarding()
    {
        var wanted = !model.SetupDone;
        if (wanted && onboarding is null && dashboardBody is not null)
        {
            onboarding = new OnboardingView(model);
            onboarding.Finished += () => { model.CompleteSetup(); UpdateOnboarding(); };
            layers.Children.Insert(layers.Children.IndexOf(Dialogs), onboarding);
            UpdateBackdrop();
            Avalonia.Threading.Dispatcher.UIThread.Post(() => onboarding?.Focus());
        }
        else if (!wanted && onboarding is not null && dashboardBody is not null)
        {
            layers.Children.Remove(onboarding); onboarding = null;
            UpdateBackdrop();
        }
    }
    // The dashboard is blurred and cannot be clicked while the setup or any dialog is over it.
    private void UpdateBackdrop()
    {
        if (dashboardBody is null) return;
        var covered = onboarding is not null || Dialogs.Any;
        dashboardBody.Effect = covered ? new Avalonia.Media.BlurEffect { Radius = 7 } : null; dashboardBody.IsHitTestVisible = !covered;
    }
    /// <summary>Whether a point (in window coordinates) is in the part of the dashboard that drags the window.</summary>
    public bool InDragZone(Point point)
    {
        if (headerRow is null) return false;
        double Bottom(Control c) => c.TranslatePoint(new Point(0, c.Bounds.Height), this)?.Y ?? 0;
        var sidebarRight = sidebar.TranslatePoint(new Point(sidebar.Bounds.Width, 0), this)?.X ?? 0;
        // The whole sidebar and the header row, wherever there is no control.
        return point.X <= sidebarRight || point.Y <= Bottom(headerRow);
    }
    private void ShowEntry(DateTime start, DateTime end)
    {
        if (!IsVisible) return;
        if (entryEditor is { IsVisible: true }) return;
        entryEditor = new EntryDialog(model, start, end);
        entryEditor.Closed += (_, _) => entryEditor = null;
        Dialogs.Show(entryEditor);
    }
    /// <summary>Set by the coordinator so a request that arrives while only the pill is showing can bring the dashboard up first.</summary>
    public Action? Reveal { get; set; }
    private void ShowAccounts()
    {
        if (!IsVisible) Reveal?.Invoke();
        if (IsVisible && !Dialogs.Dialogs.OfType<AccountsDialog>().Any()) Dialogs.Show(new AccountsDialog(model));
    }
    public void SelectTab(bool showWeek) { settingsMode = false; notesMode = false; weekMode = showWeek; Refresh(true); }
    /// <summary>Shows the Notes page.</summary>
    public void ShowNotes() { settingsMode = false; weekMode = false; notesMode = true; Refresh(true); }
    private Popup dateCalendar = null!;
    private void ToggleBell()
    {
        if (bellPopup.IsOpen) { bellPopup.IsOpen = false; return; }
        // The card hangs from a point under the bell, so it reads as coming from it: a small triangle of the card's own fill over its top edge, with only its two slanted
        // sides drawn in the line colour, so the card's border runs into it. The room round the card is for its shadow.
        var flyout = new NotificationsFlyout(model);
        // No taller than the window below the bell, so a long list scrolls inside the card instead of running past the app.
        flyout.LimitHeight(NotificationsFlyout.MaxHeightFor(bell.TranslatePoint(new Point(0, bell.Bounds.Height), this)?.Y ?? 60, Ui.ShadowMargin + DashboardHeight));
        var half = Math.Max(38, bell.Bounds.Width) / 2; var shift = HangingCard.ShiftForCorner(half, 18); // The bell is near the card's end, so the card sits a little past it to keep the point off the rounded corner.
        bellPopup.Child = HangingCard.Hang(flyout, half + shift); bellPopup.HorizontalOffset = HangingCard.Room.Right + shift; bellPopup.VerticalOffset = HangingCard.TopOffset; bellPopup.IsOpen = true;
    }
    // The gear opens the settings page and the same gear closes it, going back to the view that was showing.
    // "19 – 25 Oct", or "28 Sep – 4 Oct" when the week crosses a month.
    private static string WeekRange(DateTime start, System.Globalization.CultureInfo culture)
    {
        var end = start.AddDays(6);
        return start.Month == end.Month ? $"{start.Day} – {end.ToString("d MMM", culture)}" : $"{start.ToString("d MMM", culture)} – {end.ToString("d MMM", culture)}";
    }
    private void ShowSettings() { settingsMode = !settingsMode; Refresh(true); }
    // The Notes page is built once and kept: it listens to the model itself, so coming back to it finds the search and the form as they were.
    private NotesPage NotesPageInstance() { if (notesPage is null) notesPage = new NotesPage(model) { Query = searchBox.Text ?? "" }; return notesPage; }
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        var rebuild = e.PropertyName is not (nameof(TimerModel.Elapsed) or TimerModel.PresentationChange);
        if (e.PropertyName == TimerModel.CheckpointChange)
        {
            // The marker moves independently. Only a running block changes today's timeline geometry;
            // current-week totals still change while idle, whereas an earlier week's data does not.
            rebuild = weekMode ? model.WeekStart(model.SelectedDate) == model.WeekStart(model.Engine.Now)
                : model.Engine.Active is not null && model.SelectedDate.Date == model.Engine.Now.Date;
        }
        Refresh(rebuild);
    }
    private void Refresh(bool refreshWorkspace)
    {
        if (dashboardReady && (!IsVisible || morphPump?.Running == true))
        {
            refreshPending = true; workspacePending |= refreshWorkspace;
            return;
        }
        sidebar.Refresh(model);
        var british = System.Globalization.CultureInfo.GetCultureInfo("en-GB");
        date.Text = weekMode ? WeekRange(model.WeekStart(model.SelectedDate), british) : model.SelectedDate.ToString("ddd d MMM", british);
        todayWords.Text = weekMode ? "This week" : "Today";
        next.IsEnabled = !weekMode || model.CanStepForwardAWeek;
        ToolTip.SetTip(previous, weekMode ? "Previous week" : "Previous day"); ToolTip.SetTip(next, weekMode ? "Next week" : "Next day");
        UpdateTotals();
        // The bar under the timeline belongs to the Timeline page alone, and zoom stops at its ends.
        zoomOut.IsEnabled = model.TimelinePixelsPerHour > 60.5; zoomIn.IsEnabled = model.TimelinePixelsPerHour < 639.5;
        face.Break.Classes.Set("onBreak", model.IsOnBreak);
        undo.IsEnabled = model.Editor.CanUndo; redo.IsEnabled = model.Editor.CanRedo;
        ToolTip.SetTip(undo, Ui.Tip(model.Editor.UndoLabel is { } label ? $"Undo {label.ToLowerInvariant()}" : "Undo", "Ctrl Z"));
        ToolTip.SetTip(redo, Ui.Tip(model.Editor.RedoLabel is { } again ? $"Redo {again.ToLowerInvariant()}" : "Redo", "Ctrl Y"));
        void Tab(Button button, bool selected) { button.Classes.Set("selected", selected); if ((button.Content as StackPanel)?.Children.OfType<IconGlyph>().FirstOrDefault() is { } glyph) glyph.TintKey = selected ? "Ink" : "Muted"; }
        Tab(timelineRail, !weekMode && !settingsMode && !notesMode);
        Tab(overviewTab, !settingsMode && weekMode);
        Tab(notesTab, !settingsMode && notesMode);
        // The bell shows how many things need attention: reminders that have passed, budgets at 80 per cent or spent, and a break running long, as the pill counts them.
        var waiting = model.Alerts.Count; bellBadge.IsVisible = waiting > 0; bellBadgeText.Text = waiting > 99 ? "99+" : waiting.ToString();
        Avalonia.Automation.AutomationProperties.SetName(bell, waiting > 0 ? $"Notifications, {waiting} need attention" : "Notifications");
        // The settings page has no date to step through and no editing buttons, so the header shows only its name.
        foreach (var control in new Control[] { previous, datePick, next, todayButton }) control.IsVisible = !settingsMode && !notesMode;
        bellHostPanel.IsVisible = !settingsMode; searchHost.IsVisible = notesMode && !settingsMode;
        settingsRail.Classes.Set("on", settingsMode);
        // The timeline card and the Selected block card are the Timeline page's alone; the week and the settings fill the workspace.
        timelinePage.IsVisible = !settingsMode && !weekMode && !notesMode; workspace.IsVisible = settingsMode || weekMode || notesMode;
        // The tabs say which page this is; the settings page has its name there instead.
        workspaceTitle.Text = "Settings"; workspaceTitle.IsVisible = settingsMode; viewTabs.IsVisible = !settingsMode;
        todayButton.IsEnabled = weekMode ? model.WeekStart(model.SelectedDate) != model.WeekStart(model.Engine.Now) : model.SelectedDate.Date != model.Engine.Now.Date;
        // The settings page is built once when it opens, so a model change does not rebuild it under the pointer and lose what is being typed.
        if (settingsMode ? !renderedSettings : notesMode ? !renderedNotes : refreshWorkspace)
        {
            using var rebuild = PerformanceTimings.Measure("workspace rebuild");
            var timeline = !settingsMode && !weekMode && !notesMode;
            ContentControl host = timeline ? timelineHost : workspace;
            var oldScroll = host.Content is ScrollViewer directOld ? directOld
                : (host.Content as Control)?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            var same = renderedSettings == settingsMode && !settingsMode && renderedWeekMode == weekMode && renderedDate == model.SelectedDate;
            // The scroll position is kept when the same day is drawn again: along the day for the timeline, down the page for the week.
            Vector? previous = !same ? null : timeline ? (model.TimelineOffset is double timelineOffset ? new Vector(timelineOffset, 0) : oldScroll?.Tag is Vector pendingAlong ? pendingAlong : oldScroll?.Offset)
                : oldScroll?.Tag is Vector pending ? pending : oldScroll?.Offset;
            // What the other host showed is dropped, so only the page being shown is in the window.
            (timeline ? workspace : timelineHost).Content = null;
            host.Content = settingsMode ? new SettingsView(model) : notesMode ? NotesPageInstance() : weekMode ? ReviewViews.Week(model) : ReviewViews.Timeline(model);
            if (previous is { } offset && host.Content is Control control)
            {
                var scroll = control is ScrollViewer direct ? direct : control.GetLogicalDescendants().OfType<ScrollViewer>().FirstOrDefault();
                if (scroll is not null) scroll.Tag = offset;
            }
            renderedWeekMode = weekMode; renderedSettings = settingsMode; renderedNotes = notesMode && !settingsMode;
            renderedDate = model.SelectedDate;
        }
    }
}

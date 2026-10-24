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
    // Whether closing a window hides the timer to the tray. Without a tray the window really closes, as it always did.
    public bool HideToTray { get => hideToTray; set { hideToTray = value; ApplyTaskbar(); } }
    private bool hideToTray;
    private void ApplyTaskbar()
    {
        // Only a timer that can hide in the tray may leave the taskbar.
        var show = model.TaskbarButton || !hideToTray;
        if (Pill.ShowInTaskbar != show) Pill.ShowInTaskbar = show;
        if (Dashboard.ShowInTaskbar != show) Dashboard.ShowInTaskbar = show;
    }
    public bool IsShown => Pill.IsVisible || Dashboard.IsVisible;
    public WindowCoordinator(TimerModel model, Action exit)
    {
        this.model = model;
        Pill = new PillWindow(model);
        Dashboard = new DashboardWindow(model);
        // The rail is drawn at the pill's size, whatever that is on this screen, so the two match when one becomes the other.
        Dashboard.RailScaleSource = () => Pill.Scale; Dashboard.SetRailScale(Pill.Scale);
        // A taskbar button by default, so the timer can be pinned and launched like any app. It can be switched off to run from the tray alone.
        ApplyTaskbar();
        model.PropertyChanged += (_, _) => ApplyTaskbar();
        Pill.OpenDashboard = () =>
        {
            model.ShowToday(); Dashboard.SetRailScale(Pill.Scale);
            PlaceByCard(Dashboard, Pill, DashboardWindow.DashboardWidth + 2 * Ui.ShadowMargin, DashboardWindow.DashboardHeight + 2 * Ui.ShadowMargin, 1);
            Dashboard.Show();
            Pill.CloseFlyouts(); // So it is a capsule again when you come back.
            Pill.Hide();
            Dashboard.Activate();
        };
        Dashboard.Reveal = ShowDashboard;
        Dashboard.OpenPill = () =>
        {
            Dashboard.CloseFlyouts(); Pill.CloseFlyouts();
            PlaceByCard(Pill, Dashboard, Pill.WindowWidthNow, Pill.WindowHeightNow, Pill.Scale);
            Pill.Show(); Dashboard.Hide(); Pill.Activate();
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
        check.Tick += (_, _) => { Reassert(Pill); Reassert(Dashboard); };
        check.Start();
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
        dashboardWasShown = Dashboard.IsVisible;
        Pill.Hide(); Dashboard.Hide();
    }
    public void ShowTimer()
    {
        if (IsShown) { (Dashboard.IsVisible ? (Window)Dashboard : Pill).Activate(); return; }
        if (dashboardWasShown) ShowDashboard(); else { Pill.Show(); BringToFront(Pill); }
    }
    public void ShowDashboard()
    {
        if (!Dashboard.IsVisible) { model.ShowToday(); if (!Pill.IsVisible) Pill.FitToScreen(Pill.Screens.ScreenFromWindow(Pill) ?? Pill.Screens.Primary); Dashboard.SetRailScale(Pill.Scale); PlaceByCard(Dashboard, Pill, DashboardWindow.DashboardWidth + 2 * Ui.ShadowMargin, DashboardWindow.DashboardHeight + 2 * Ui.ShadowMargin, 1); Dashboard.Show(); }
        Pill.CloseFlyouts(); Pill.Hide(); BringToFront(Dashboard);
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
        var screen = Pill.Screens.Primary;
        if (screen is not null)
        {
            var area = screen.WorkingArea;
            Pill.FitToScreen(screen);
            Pill.Position = new PixelPoint(area.Right - (int)(Pill.Width * screen.Scaling) - 24, area.Y + 24);
        }
        Dashboard.Hide(); Pill.Show(); BringToFront(Pill);
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
    private static void PlaceByCard(Window target, Window source, double width, double height, double targetScale)
    {
        var screen = source.Screens.ScreenFromWindow(source) ?? target.Screens.ScreenFromWindow(target);
        if (screen is null) return;
        var area = screen.WorkingArea; var scale = screen.Scaling;
        var sourceScale = source is PillWindow pill ? pill.Scale : 1.0;
        target.Width = Math.Min(width, area.Width / scale);
        target.Height = Math.Min(height, area.Height / scale);
        // The card sits one margin in from its window, and the margin shrinks with the pill's scale.
        var shift = CardShift(sourceScale, targetScale, scale);
        target.Position = new PixelPoint(
            Math.Clamp(source.Position.X + shift, area.X, Math.Max(area.X, area.Right - (int)(target.Width * scale))),
            Math.Clamp(source.Position.Y + shift, area.Y, Math.Max(area.Y, area.Bottom - (int)(target.Height * scale))));
    }
}

public sealed class DashboardWindow : Window
{
    /// <summary>The dashboard's fixed size, before the room kept around it for its shadow.</summary>
    public const double DashboardWidth = 1100, DashboardHeight = 720;
    /// <summary>The rail's width, the same as the pill's, so it reads as the pill opened out.</summary>
    public const double RailWidth = PillWindow.PillWidth;
    public Action? OpenPill { get; set; }
    private readonly TimerModel model;
    private readonly TextBlock workspaceTitle = new() { FontSize = 26, Classes = { "clock" }, VerticalAlignment = VerticalAlignment.Center };
    private Button todayButton = null!; private TextBlock todayWords = null!;
    private Control? totalsStrip, railBorder; private Button zoomOut = null!, zoomIn = null!;
    private readonly IconGlyph dayFlag = new(IconKind.Flag), daySun = new(IconKind.Sunrise);
    private StatusFace face = null!;
    private Popup finishPopup = null!;
    private Grid accountsLayer = null!; private Border accountsScrim = null!, accountsDrawer = null!;
    private BreakButton breakButton = null!;
    private readonly Avalonia.Media.ScaleTransform railScale = new(1, 1); private double currentRailScale = 1; private StackPanel railTopStack = null!;
    /// <summary>Where the rail finds out the size the pill is drawn at, so its top matches the pill's exactly and nothing jumps when one becomes the other.</summary>
    public Func<double>? RailScaleSource { get; set; }
    private readonly TextBlock finishSummary = Ui.Text("", 13, "muted");
    private readonly Border dayTile = new() { Name = "DayTile", Width = 40, Height = 40, CornerRadius = new CornerRadius(13), Background = Avalonia.Media.Brushes.Transparent };
    private readonly TextBlock workingValue = Value(), allocatedValue = Value(), unallocatedValue = Value(), overtimeValue = Value();
    private Control overtimeStat = null!;
    private static TextBlock Value() => new() { FontSize = 14, FontWeight = Avalonia.Media.FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock date = Ui.Text("", 14); private Button datePick = null!;
    private readonly ContentControl workspace = new(), timelineHost = new() { Name = "TimelineHost" };
    private Border timelineCard = null!; private MessageBar messageBar = null!; private Grid timelinePage = null!; private SelectedBlockCard selectedCard = null!; private NotesCard notesCard = null!;
    private readonly Button day;
    private Button timelineRail = null!, previous = null!, next = null!, overviewTab = null!, notesTab = null!, addTime = null!, bell = null!;
    private Border bellBadge = null!; private Control bellHostPanel = null!; private TextBlock bellBadgeText = null!; private TextBox searchBox = null!; private Control searchHost = null!; private Popup bellPopup = null!; private NotesPage? notesPage;
    private readonly Button undo;
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
        // The rail is the pill opened out: it runs the full height, with the same lights, clock and circle at its top, the views below them, and the
        // break, day and close controls at its foot. The workspace beside it has a header with the date and the gear.
        var root = Ui.Grid("Auto,*", "*");
        var rail = new Border { Width = RailWidth, BorderThickness = new Thickness(0, 0, 1, 0), Name = "DashboardRail" }; rail.Classes.Add("panel");
        rail.Bind(Border.BorderBrushProperty, rail.GetResourceObservable("Line")); railBorder = rail;
        var railGrid = Ui.Grid("*", "*,Auto");
        var railTop = new StackPanel { Margin = new Thickness(0, PillWindow.TopInset, 0, 0), Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center }; railTopStack = railTop;
        Button RailButton(IconKind kind, string tip, string name, Action click)
        {
            var button = Ui.Button("", click, name); button.Classes.Add("rail"); button.Content = new IconGlyph(kind); button.HorizontalAlignment = HorizontalAlignment.Center;
            ToolTip.SetTip(button, tip); Avalonia.Automation.AutomationProperties.SetName(button, tip); return button;
        }
        // The clock goes back to the pill, and the circle pauses or resumes, as on the pill. With no account to run, it opens the accounts.
        face = new StatusFace(model, "Rail", "DashboardBreak", "Collapse to pill", () => OpenPill?.Invoke(), OnRailCircle, ToggleAccountsPanel);
        // The top is drawn at the pill's own size, so the clock, circle and accounts button stay put and the same size when one becomes the other.
        railTop.Children.Add(new LayoutTransformControl { LayoutTransform = railScale, Child = face, HorizontalAlignment = HorizontalAlignment.Center });
        timelineRail = RailButton(IconKind.Timeline, "Timeline", "TimelineTab", () => SelectTab(false));
        overviewTab = RailButton(IconKind.Week, "Week overview", "WeekOverviewTab", () => SelectTab(true));
        model.OpenDayRequested += _ => SelectTab(false);
        var viewsRule = Ui.Rule(); viewsRule.Margin = new Thickness(10, 4); railTop.Children.Add(viewsRule);
        notesTab = RailButton(IconKind.NotesList, "Notes", "NotesTab", ShowNotes);
        // Timeline, then Notes, then the Week overview.
        foreach (var button in new[] { timelineRail, notesTab, overviewTab }) railTop.Children.Add(button);
        Ui.Put(railGrid, railTop);
        var railFoot = new StackPanel { Margin = new Thickness(0, 0, 0, 14), Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
        // The break is in the face, under the primary control, as on the pill.
        breakButton = face.Break;
        dayTile.Child = new Grid { Children = { dayFlag, daySun }, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        day = Ui.Button("", OnDayButton, "DashboardDay");
        day.Classes.Add("pillBare"); day.Content = dayTile; day.HorizontalAlignment = HorizontalAlignment.Center;
        day.PointerEntered += (_, _) => { dayFlag.Waving = true; HoverTile(dayTile, true); }; day.PointerExited += (_, _) => { dayFlag.Waving = false; HoverTile(dayTile, false); }; // The flag waves under the pointer.
        railFoot.Children.Add(day);
        // Closes the timer for good. It goes red under the pointer, so it is never pressed by accident.
        var quit = RailButton(IconKind.Close, "Close timer", "QuitTimer", () => model.RequestQuit()); quit.Classes.Add("quit");
        var quitGlyph = (IconGlyph)quit.Content!;
        quit.PointerEntered += (_, _) => quitGlyph.TintKey = "Finish"; quit.PointerExited += (_, _) => quitGlyph.TintKey = null;
        railFoot.Children.Add(quit);
        Ui.Put(railGrid, railFoot, row: 1);
        BuildRailPopups(railGrid);
        rail.Child = railGrid; Ui.Put(root, rail);
        Ui.DragHeader(railTop, this, "RailDragHeader");
        var body = Ui.Grid("*", "*");
        var review = Ui.Grid("*", "Auto,*");
        review.Margin = new Thickness(22, 16, 22, 18);
        // The workspace's name on the left; on the right the date to step through and a button back to today.
        // In Week views the arrows move a whole week, and forward stops at the current week. In Timeline they move a day.
        // The date has a fixed width, so the arrows stay put whatever its text says.
        var nav = Ui.Grid("*,Auto,Auto"); nav.VerticalAlignment = VerticalAlignment.Center;
        Ui.Put(nav, workspaceTitle); headerRow = nav;
        date.Width = 124; date.TextAlignment = Avalonia.Media.TextAlignment.Center;
        // Pressing the date opens a calendar to jump to any day, so a day weeks away is one click instead of many arrows.
        datePick = Ui.Ctl("DatePick", null, null, "Pick a day", () => dateCalendar.IsOpen = !dateCalendar.IsOpen);
        datePick.Width = double.NaN; datePick.Padding = new Thickness(12, 0); datePick.Content = Ui.Horizontal(new IconGlyph(IconKind.Today), date);
        // The calendar hangs from the date button by a point, like the bell's card, and the same button closes it again.
        var datePicker = new SoftDatePicker { Name = "DateCalendar", BoxShadow = Avalonia.Media.BoxShadows.Parse("0 10 28 0 #29141E46") }; datePicker.Classes.Add("flyoutCard");
        dateCalendar = new Popup { Placement = Avalonia.Controls.PlacementMode.Bottom, Name = "DateCalendarPopup", PlacementTarget = datePick, Child = HangingCard.Hang(datePicker, -1), VerticalOffset = 2 };
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
        bellBadgeText = new TextBlock { FontSize = 11, FontWeight = Avalonia.Media.FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
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
        var steps = Ui.Horizontal(searchHost, previous, dateHost, next, todayButton, bellHost); steps.Spacing = 8;
        Ui.Put(nav, steps, 1);
        // Settings is a gear at the top right, which also closes it again.
        settingsRail = Ui.Ctl("Settings", null, IconKind.Gear, "Settings", ShowSettings); settingsRail.Margin = new Thickness(8, 0, 0, 0);
        Ui.Put(nav, settingsRail, 2);
        Ui.Put(review, nav);
        // The bar under the timeline, as in the mock: Add time and Undo on the left, the day's figures in the middle and the zoom buttons on the right.
        addTime = Ui.Ctl("AddTime", "Add time", IconKind.Plus, "Add time to this day", () => model.OpenGap(), tint: "Accent"); addTime.Classes.Add("accentOutline");
        undo = Ui.Ctl("UndoEdit", "Undo", IconKind.Undo, "Undo the last change", () => model.UndoEdit());
        // Zoom steps by a fifth each press, about the middle of what is showing. Hold Ctrl and use the wheel to zoom about the pointer.
        ScrollViewer? TimelineScroll() => timelineHost.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(s => s.Name == "TimelineScroll");
        zoomOut = Ui.Ctl("TimelineZoomOut", null, IconKind.Minus, "Zoom out", () => { if (TimelineScroll() is { } s) model.ZoomTimeline(-1, s.Viewport.Width / 2, s.Offset.X); }, key: "Ctrl scroll");
        zoomIn = Ui.Ctl("TimelineZoomIn", null, IconKind.Plus, "Zoom in", () => { if (TimelineScroll() is { } s) model.ZoomTimeline(1, s.Viewport.Width / 2, s.Offset.X); }, key: "Ctrl scroll");
        var zoom = Ui.Horizontal(zoomOut, zoomIn); zoom.Spacing = 6; zoom.Name = "TimelineControls";
        totalsStrip = BuildTotals();
        var tools = Ui.Grid("Auto,*,Auto"); tools.Margin = new Thickness(14, 6, 14, 0);
        Ui.Put(tools, Ui.Horizontal(addTime, undo)); Ui.Put(tools, totalsStrip, 1); Ui.Put(tools, zoom, 2);
        workspace.Margin = new Thickness(0, 14, 0, 0); // Room under the title and date, so content never touches them.
        Ui.Put(review, workspace, row: 1);
        // The timeline is a card (the day across the top, its bar of buttons and figures under it) over the Selected block card. The week and the settings
        // fill the workspace instead, so these two are shown for the timeline alone.
        var cardGrid = Ui.Grid("*", "Auto,Auto"); Ui.Put(cardGrid, timelineHost); Ui.Put(cardGrid, tools, 0, 1);
        timelineCard = new Border { Name = "TimelineCard", Child = cardGrid, CornerRadius = new CornerRadius(20), BorderThickness = new Thickness(1), Padding = new Thickness(0, 14, 0, 12), VerticalAlignment = VerticalAlignment.Top };
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
        Ui.Put(root, body, 1);
        // Notes, refused edits and failed saves float over the lowest part of the workspace, so they never push anything up.
        messageBar = new MessageBar(model); Ui.Put(review, messageBar, row: 1);
        // The setup overlay, when it is wanted, sits over everything in the card, which is blurred and cannot be clicked while it is up.
        layers.Children.Add(root); dashboardBody = root;
        BuildAccountsLayer(); layers.Children.Add(accountsLayer);
        layers.Children.Add(Dialogs); Dialogs.OpenChanged += UpdateBackdrop; // Last, so a dialog is above the setup too.
        var surface = new Border { Name = "DashboardSurface", Child = layers, ClipToBounds = true };
        surface.Classes.Add("surface");
        var host = Ui.Shadowed(surface, 20); Content = host;
        (bool, Avalonia.Styling.ThemeVariant)? lastShadow = null;
        void UpdateShadow()
        {
            var state = (model.Shadows, ActualThemeVariant);
            if (lastShadow != state) { lastShadow = state; Ui.SetShadow(host, model.Shadows, null); }
        }
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
            if (e.Key == Avalonia.Input.Key.Escape) { if (Dialogs.CloseTop()) { } else if (accountsLayer.IsVisible) CloseAccounts(); else model.ClearSelection(); e.Handled = true; }
            // Ctrl+C copies the selected blocks and Ctrl+V pastes them on the timeline, but never while typing in a text box.
            else if (e.KeyModifiers == Avalonia.Input.KeyModifiers.Control && e.Key is Avalonia.Input.Key.C or Avalonia.Input.Key.V && !settingsMode && FocusManager?.GetFocusedElement() is not TextBox)
            {
                if (e.Key == Avalonia.Input.Key.C) model.CopySelection(); else if (!weekMode) model.PasteBlocks(model.DefaultPasteTime());
                e.Handled = true;
            }
            // Backspace and Delete remove the selected blocks, but never while typing in a text box.
            else if (e.Key is Avalonia.Input.Key.Back or Avalonia.Input.Key.Delete && FocusManager?.GetFocusedElement() is not TextBox)
            { if (model.SelectedRows.Count > 0) { model.DeleteSelection(); e.Handled = true; } }
        };
        model.PropertyChanged += Changed;
        model.PropertyChanged += (_, _) => UpdateOnboarding();
        model.EntryRequested += ShowEntry;
        model.AccountsRequested += ShowAccounts;
        Closed += (_, _) => { model.PropertyChanged -= Changed; model.EntryRequested -= ShowEntry; model.AccountsRequested -= ShowAccounts; };
        Refresh(true);
        UpdateOnboarding();
    }
    // ---- the account drawer and the Finish day question
    private void BuildRailPopups(Panel host)
    {
        // Finishing the day asks first, in a small card beside the button, since it stops the clock and the running account.
        var title = Ui.Text("Finish day?", 15); title.FontWeight = Avalonia.Media.FontWeight.SemiBold;
        finishSummary.Name = "FinishDaySummary"; finishSummary.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        var cancel = Ui.DialogButton("Cancel", () => finishPopup.IsOpen = false, "FinishDayCancel", false);
        var confirm = Ui.DialogButton("Finish day", () => { finishPopup.IsOpen = false; model.ToggleDay(); }, "FinishDayConfirm", true);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right }; buttons.Children.Add(cancel); buttons.Children.Add(confirm);
        var card = new Border { Name = "FinishDayCard", Width = 280, Padding = new Thickness(16), CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1) };
        card.Classes.Add("surface"); card.Classes.Add("flyoutCard");
        var stack = new StackPanel { Spacing = 10 }; stack.Children.Add(title); stack.Children.Add(finishSummary); stack.Children.Add(buttons); card.Child = stack;
        finishPopup = new Popup { PlacementTarget = day, Placement = PlacementMode.Right, HorizontalOffset = 8, IsLightDismissEnabled = true, Child = card,
            PlacementConstraintAdjustment = Avalonia.Controls.Primitives.PopupPositioning.PopupPositionerConstraintAdjustment.FlipX | Avalonia.Controls.Primitives.PopupPositioning.PopupPositionerConstraintAdjustment.SlideY };
        host.Children.Add(finishPopup);
    }
    // The account list unfolds from the rail's right edge, full height and in the rail's own colours, with the page behind it dimmed. It is a layer
    // over the workspace rather than a pop-up, so it sits on the page's grid and the rail stays where it is. The accounts button, Escape or a click
    // on the dimmed page folds it away.
    private void BuildAccountsLayer()
    {
        var content = new AccountsPanel(model, "Rail", compact: false, reorderable: true, () => { CloseAccounts(); model.OpenAccounts(); });
        railAccountScroll = content.Scroll;
        accountsDrawer = new Border { Name = "AccountsDrawer", Width = 300, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(14, 20, 14, 20), BorderThickness = new Thickness(0, 0, 1, 0),
            BoxShadow = Avalonia.Media.BoxShadows.Parse("8 0 28 0 #29141E46"), Child = content };
        accountsDrawer.Classes.Add("panel"); accountsDrawer.Bind(Border.BorderBrushProperty, accountsDrawer.GetResourceObservable("Line"));
        accountsScrim = new Border { Name = "AccountsScrim", Background = Avalonia.Media.Brushes.Transparent };
        accountsScrim.PointerPressed += (_, e) => { CloseAccounts(); e.Handled = true; };
        accountsLayer = new Grid { Name = "AccountsLayer", IsVisible = false, Margin = new Thickness(RailWidth, 0, 0, 0) };
        accountsLayer.Children.Add(accountsScrim); accountsLayer.Children.Add(accountsDrawer);
    }
    private ScrollViewer? railAccountScroll;
    /// <summary>Whether the account drawer beside the rail, or the Finish day question, is open.</summary>
    public bool AccountsOpen => accountsLayer.IsVisible;
    public bool FinishQuestionOpen => finishPopup.IsOpen;
    /// <summary>The account list's scroll box, so tests can read its limit.</summary>
    public ScrollViewer? AccountScroll => railAccountScroll;
    /// <summary>Opens the accounts beside the rail. Choosing one starts or switches to it and folds the list away.</summary>
    public void ShowAccountsPanel()
    {
        finishPopup.IsOpen = false;
        accountsScrim.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark ? (byte)0x57 : (byte)0x29, 0, 0, 0));
        accountsActive = model.ActiveAccount; accountsLayer.IsVisible = true; face.SetAccountsOpen(true);
    }
    private void CloseAccounts() { accountsLayer.IsVisible = false; face.SetAccountsOpen(false); }
    private void ToggleAccountsPanel() { if (accountsLayer.IsVisible) CloseAccounts(); else ShowAccountsPanel(); }
    private string? accountsActive;
    /// <summary>Puts the account drawer and the Finish day question away, so the dashboard is plain again when it comes back.</summary>
    public void CloseFlyouts() { CloseAccounts(); finishPopup.IsOpen = false; }
    /// <summary>Draws the rail's top at the size the pill is drawn, and narrows the rail to the pill's width at that size.</summary>
    public void SetRailScale(double scale)
    {
        scale = Math.Clamp(scale, .5, 1.2);
        if (Math.Abs(scale - currentRailScale) < .001) return;
        currentRailScale = scale; railScale.ScaleX = scale; railScale.ScaleY = scale;
        if (railBorder is not null) railBorder.Width = RailWidth * scale;
        railTopStack.Margin = new Thickness(0, PillWindow.TopInset * scale, 0, 0);
        if (accountsLayer is not null) accountsLayer.Margin = new Thickness(RailWidth * scale, 0, 0, 0);
    }
    public double RailWidthNow => RailWidth * currentRailScale;
    private void OnRailCircle()
    {
        var chosen = model.Accounts.Any(a => a.Name == model.SelectedAccount);
        // With nothing to run or resume, the accounts open so one can be picked.
        if (model.IsTracking || chosen) model.TogglePause(); else ShowAccountsPanel();
    }
    // Starting the day just does it. Finishing it asks first, and says what will stop.
    private void OnDayButton()
    {
        if (!model.DayStarted) { model.ToggleDay(); return; }
        CloseAccounts();
        // Just the working time: what stops is plain from the button, and the card stays small.
        finishSummary.Text = "Working time " + model.Format(model.Engine.Totals(model.Engine.Now).Working) + ".";
        finishPopup.IsOpen = true;
    }
    private static void HoverTile(Border tile, bool on) => tile.Background = on ? (tile.TryFindResource("Hover", tile.ActualThemeVariant, out var brush) && brush is Avalonia.Media.IBrush hover ? hover : Avalonia.Media.Brushes.Transparent) : Avalonia.Media.Brushes.Transparent;
    // The day's figures in one line under the timeline: the working time (the clock), what is on accounts and what is not, and any overtime.
    // The day's figures under the timeline: Working time (red once over the schedule), a bar of the scheduled day, then Allocated, Unallocated, Breaks and Overtime.
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
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Name = "DayTotals", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        workingValue.Name = "TotalsWorkingValue";
        strip.Children.Add(Stat("Working time", workingValue, "TotalsWorking"));
        strip.Children.Add(dayBar);
        strip.Children.Add(Stat("Allocated", allocatedValue, "TotalsAllocated"));
        strip.Children.Add(Stat("Unallocated", unallocatedValue, "TotalsUnallocated"));
        var amber = new Avalonia.Controls.Shapes.Ellipse { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center }; amber.Bind(Avalonia.Controls.Shapes.Shape.FillProperty, amber.GetResourceObservable("Break"));
        strip.Children.Add(Stat("Breaks", breaksValue, "TotalsBreaks", amber));
        overtimeStat = Stat("Overtime", overtimeValue, "TotalsOvertime"); strip.Children.Add(overtimeStat);
        overtimeValue.Classes.Add("overtimeText");
        return strip;
    }
    // The bar is the scheduled day: time on accounts solid, working time with nothing on an account hatched, overtime in red at the end. A day off has no bar.
    private void UpdateTotals()
    {
        var shown = model.SelectedDate.Date;
        var totals = model.Engine.Totals(shown); var plan = model.Engine.Plan(shown);
        var working = totals.Working + totals.Overtime;
        workingValue.Text = model.Format(working); allocatedValue.Text = model.Format(totals.Allocated); unallocatedValue.Text = model.Format(totals.Unallocated);
        breaksValue.Text = model.Format(model.BreakTimeOn(shown));
        overtimeValue.Text = model.Format(totals.Overtime); overtimeStat.IsVisible = totals.Overtime > TimeSpan.Zero;
        workingValue.Classes.Set("overtimeText", totals.Overtime > TimeSpan.Zero);
        var scheduled = plan.Working ? plan.End - plan.Start : TimeSpan.Zero;
        // The bar is always there and always the same width, so the figures beside it never move from one day to the next. A day with nothing scheduled and no time
        // shows an empty track; a day with time but no schedule (a weekend) shows the time on it, all of it as working time.
        var breaks = model.BreakTimeOn(shown);
        if (scheduled <= TimeSpan.Zero && working + breaks <= TimeSpan.Zero)
        {
            dayBar.Child = null; dayBar.Classes.Add("track");
            ToolTip.SetTip(dayBar, "No schedule"); Avalonia.Automation.AutomationProperties.SetName(dayBar, "Nothing is scheduled or recorded");
        }
        else
        {
            // The bar is as long as the longer of the schedule and the time worked, so overtime has room at the end.
            var whole = Math.Max(scheduled.TotalSeconds, working.TotalSeconds + breaks.TotalSeconds);
            var reach = scheduled > TimeSpan.Zero ? scheduled.TotalSeconds : working.TotalSeconds;
            var allocatedPart = Math.Min(totals.Allocated.TotalSeconds, Math.Min(working.TotalSeconds, reach));
            var unallocatedPart = Math.Max(0, Math.Min(reach, working.TotalSeconds) - allocatedPart);
            var overtimePart = totals.Overtime.TotalSeconds;
            var breaksPart = breaks.TotalSeconds;
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{allocatedPart:0.###}*,{unallocatedPart:0.###}*,{breaksPart:0.###}*,{overtimePart:0.###}*,{Math.Max(0, whole - allocatedPart - unallocatedPart - breaksPart - overtimePart):0.###}*") };
            Border Part(string key, int column, bool hatched = false)
            {
                var part = new Border { Name = "DayBar" + key, [Grid.ColumnProperty] = column };
                if (hatched) { part.Background = ReviewViews.Stripes(model); part.Opacity = .7; } else part.Classes.Add("bar" + key);
                return part;
            }
            grid.Children.Add(Part("Allocated", 0)); grid.Children.Add(Part("Unallocated", 1, true)); grid.Children.Add(Part("Breaks", 2)); grid.Children.Add(Part("Overtime", 3));
            dayBar.Child = grid; dayBar.Classes.Add("track");
            ToolTip.SetTip(dayBar, (scheduled > TimeSpan.Zero ? $"Scheduled {plan.Start:HH:mm}–{plan.End:HH:mm}" : "No schedule") + $"\nAllocated {model.Format(totals.Allocated)} · Unallocated {model.Format(totals.Unallocated)} · Breaks {model.Format(breaks)}" + (totals.Overtime > TimeSpan.Zero ? $"\nOvertime {model.Format(totals.Overtime)}" : ""));
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
        if (railBorder is null || headerRow is null) return false;
        double Bottom(Control c) => c.TranslatePoint(new Point(0, c.Bounds.Height), this)?.Y ?? 0;
        var railRight = railBorder.TranslatePoint(new Point(railBorder.Bounds.Width, 0), this)?.X ?? 0;
        // The whole rail and the header row, wherever there is no control.
        return point.X <= railRight || point.Y <= Bottom(headerRow);
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
        bellPopup.Child = HangingCard.Hang(flyout, Math.Max(38, bell.Bounds.Width) / 2); bellPopup.HorizontalOffset = 8; bellPopup.VerticalOffset = 2; bellPopup.IsOpen = true;
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
    private NotesPage NotesPageInstance() { if (notesPage is null) { notesPage = new NotesPage(model) { Query = searchBox.Text ?? "" }; } return notesPage; }
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Refresh(e.PropertyName != nameof(TimerModel.Elapsed));
    private void Refresh(bool refreshWorkspace)
    {
        face.Refresh();
        var onBreak = model.IsOnBreak;
        var british = System.Globalization.CultureInfo.GetCultureInfo("en-GB");
        date.Text = weekMode ? WeekRange(model.WeekStart(model.SelectedDate), british) : model.SelectedDate.ToString("ddd d MMM", british);
        todayWords.Text = weekMode ? "This week" : "Today";
        next.IsEnabled = !weekMode || model.CanStepForwardAWeek;
        ToolTip.SetTip(previous, weekMode ? "Previous week" : "Previous day"); ToolTip.SetTip(next, weekMode ? "Next week" : "Next day");
        UpdateTotals();
        // The day button is a flag to finish the day, or a sunrise to start it (again). Finish is red and Start is green.
        dayFlag.IsVisible = model.DayStarted; daySun.IsVisible = !model.DayStarted;
        dayFlag.TintKey = "Finish"; daySun.TintKey = "Start";
        day.Classes.Set("startDay", !model.DayStarted); day.Classes.Set("finishDay", model.DayStarted);
        var dayTip = model.DayStarted ? "Finish day" : model.Engine.Finished ? "Start day again" : "Start day";
        ToolTip.SetTip(day, dayTip); Avalonia.Automation.AutomationProperties.SetName(day, dayTip);
        if (finishPopup.IsOpen && !model.DayStarted) finishPopup.IsOpen = false;
        if (accountsLayer.IsVisible && model.ActiveAccount != accountsActive) CloseAccounts(); // Choosing an account puts the list away.
        // The bar under the timeline belongs to the Timeline page alone, and zoom stops at its ends.
        zoomOut.IsEnabled = model.TimelinePixelsPerHour > 60.5; zoomIn.IsEnabled = model.TimelinePixelsPerHour < 639.5;
        breakButton.Refresh(); breakButton.Classes.Set("onBreak", onBreak);
        if (RailScaleSource is { } source) SetRailScale(source());
        undo.IsEnabled = model.Editor.CanUndo;
        ToolTip.SetTip(undo, model.Editor.UndoLabel is { } label ? $"Undo {label.ToLowerInvariant()}" : null);
        void Rail(Button button, bool selected) { button.Classes.Set("selected", selected); var glyph = button.Content as IconGlyph ?? (button.Content as Grid)?.Children.OfType<IconGlyph>().FirstOrDefault(); if (glyph is not null) glyph.TintKey = selected ? "RailIconOn" : "RailIcon"; }
        Rail(timelineRail, !weekMode && !settingsMode && !notesMode);
        Rail(overviewTab, !settingsMode && weekMode);
        Rail(notesTab, !settingsMode && notesMode);
        // The bell shows how many reminders have passed without being done.
        var waiting = model.Notes.AttentionCount; bellBadge.IsVisible = waiting > 0; bellBadgeText.Text = waiting > 99 ? "99+" : waiting.ToString();
        Avalonia.Automation.AutomationProperties.SetName(bell, waiting > 0 ? $"Notifications, {waiting} need attention" : "Notifications");
        // The settings page has no date to step through and no editing buttons, so the header shows only its name.
        foreach (var control in new Control[] { previous, datePick, next, todayButton }) control.IsVisible = !settingsMode && !notesMode;
        bellHostPanel.IsVisible = !settingsMode; searchHost.IsVisible = notesMode && !settingsMode;
        settingsRail.Classes.Set("on", settingsMode);
        // The timeline card and the Selected block card are the Timeline page's alone; the week and the settings fill the workspace.
        timelinePage.IsVisible = !settingsMode && !weekMode && !notesMode; workspace.IsVisible = settingsMode || weekMode || notesMode;
        workspaceTitle.Text = settingsMode ? "Settings" : notesMode ? "Notes" : !weekMode ? "Timeline" : "Week overview";
        todayButton.IsEnabled = weekMode ? model.WeekStart(model.SelectedDate) != model.WeekStart(model.Engine.Now) : model.SelectedDate.Date != model.Engine.Now.Date;
        // The settings page is built once when it opens, so a model change does not rebuild it under the pointer and lose what is being typed.
        if (settingsMode ? !renderedSettings : notesMode ? !renderedNotes : refreshWorkspace)
        {
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

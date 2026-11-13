using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Timer.Core;
using Timer.Storage;

namespace Timer.Desktop;

/// <summary>
/// The settings page, shown in the dashboard's workspace: seven sections down the left, each with an icon (Look, The pill, Alerts, Day and time, Accounts,
/// Export and About), and the chosen one on a card on the right. A choice that changes how something looks is a picture of the result (the theme, shadows,
/// the pill's style and clock digits, and the three alert modes); everything else is a plain tick, list, box or key. Every section is built once and the others
/// are hidden, so a section keeps what is being typed in it while another is looked at, and the page is never rebuilt under the pointer.
/// </summary>
public sealed class SettingsView : Grid
{
    /// <summary>How many quick clicks on the About tile unlock the developer features, and the longest pause allowed between them.</summary>
    public const int UnlockClicks = 10;
    public static readonly TimeSpan UnlockWindow = TimeSpan.FromMilliseconds(700);
    /// <summary>The clock the quick clicks are timed by; tests replace it, because a slow runner can take longer than the window to deliver ten clicks.</summary>
    public static Func<DateTime> UnlockClock { get; set; } = () => DateTime.UtcNow;

    private readonly TimerModel model;
    private readonly List<Action> syncs = new();
    private readonly Dictionary<string, (Button Nav, Control Body, string Title, string Intro)> sections = new();
    private readonly TextBlock title = Ui.Heading("", 22), intro = Ui.Text("", 13, "muted");

    public SettingsView(TimerModel model)
    {
        this.model = model; Name = "SettingsPage"; ColumnDefinitions = new ColumnDefinitions("210,*"); Margin = new Thickness(0, 0, 0, 8);
        var list = new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, 14, 0), Name = "SettingsSections" };
        var body = new StackPanel { Spacing = 0 };
        Add("look", "Look", IconKind.Sunrise, "Colours, shadows and hints.", BuildLook(), list, body);
        Add("pill", "The pill", IconKind.Pill, "How the small always-on-top timer looks and behaves.", BuildPill(), list, body);
        Add("alerts", "Alerts", IconKind.Bell, "How loudly the pill tells you what is waiting.", BuildAlerts(), list, body);
        Add("day", "Day and time", IconKind.Clock, "The hours you are measured against, and how time is written.", BuildDay(), list, body);
        Add("accounts", "Accounts", IconKind.Accounts, "The accounts you track time against, and their budgets.", BuildAccounts(), list, body);
        Add("export", "Export", IconKind.Export, "Save a week or a month as CSV files, or copy the hours table for a timesheet.", BuildExport(), list, body);
        Add("about", "About", IconKind.Info, "Setup and version.", BuildAbout(), list, body);
        var rail = new StackPanel { Margin = new Thickness(0, 2, 0, 0) }; rail.Children.Add(list); Children.Add(rail);

        // The chosen section sits on a card, under its name and a short line about it.
        title.Name = "SettingsTitle"; intro.Name = "SettingsIntro"; intro.TextWrapping = TextWrapping.Wrap; intro.Margin = new Thickness(0, 2, 0, 4);
        var scroll = new ScrollViewer { Content = new Border { Child = body, Margin = new Thickness(8, 8, 14, 10) } /* room round the keys so the scroll view does not cut their shadows */, AllowAutoHide = false,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        var inside = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        Ui.Put(inside, title); Ui.Put(inside, intro, 0, 1); Ui.Put(inside, scroll, 0, 2);
        var card = new Border { Name = "SettingsCard", CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1), Padding = new Thickness(16, 20, 10, 6), Child = inside };
        card.Classes.Add("dayCard"); SetColumn(card, 1); Children.Add(card);
        ShowSection("look");

        // The page is built once, so it listens for changes made elsewhere (a theme or hours format chosen from another place) and keeps its boxes true.
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { foreach (var sync in syncs) sync(); }
        AttachedToVisualTree += (_, _) => model.PropertyChanged += Changed;
        DetachedFromVisualTree += (_, _) => model.PropertyChanged -= Changed;
    }

    private void Add(string id, string name, IconKind icon, string text, Control content, StackPanel list, StackPanel body)
    {
        var glyph = new IconGlyph(icon) { TintKey = "Muted", Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center };
        var label = Ui.Text(name, 14); label.VerticalAlignment = VerticalAlignment.Center;
        var nav = new Button { Name = "Settings" + char.ToUpperInvariant(id[0]) + id[1..], Content = Ui.Horizontal(glyph, label), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
        ((StackPanel)nav.Content).Spacing = 10;
        nav.Classes.Add("settingsNav"); nav.Click += (_, _) => ShowSection(id);
        Avalonia.Automation.AutomationProperties.SetName(nav, name);
        list.Children.Add(nav); content.IsVisible = false; body.Children.Add(content);
        sections[id] = (nav, content, name, text);
    }

    /// <summary>Shows one section and hides the others.</summary>
    public void ShowSection(string id)
    {
        if (!sections.ContainsKey(id)) return;
        foreach (var (key, part) in sections) { part.Body.IsVisible = key == id; part.Nav.Classes.Set("selected", key == id); }
        title.Text = sections[id].Title; intro.Text = sections[id].Intro;
        if (id == "export") RefreshPeriods();
    }

    // ---- look
    private Control BuildLook()
    {
        var stack = new StackPanel { Spacing = 0 };
        stack.Children.Add(SectionLabel("Theme"));
        // Dark first, as it is the default, then Light, then System (which follows the computer's light or dark setting) and Sunset. Each tile is a small picture of the dashboard in that theme.
        // Four equal tiles that share the card's width up to their own 172, so with the card narrower they narrow too, side by side, instead of running past the card's edge.
        var tiles = new Avalonia.Controls.Primitives.UniformGrid { Rows = 1, Columns = 4, Margin = new Thickness(0, 6, 0, 10), HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 4 * 172 + 36 };
        var buttons = new Dictionary<string, Button>();
        foreach (var (choice, words) in new[] { (AppThemes.Dark.Name, "Dark"), (AppThemes.Light.Name, "Light"), (ThemeLibrary.System, "System"), (AppThemes.Sun, "Sunset") })
        {
            var tile = new Button { Name = "Theme" + words, BorderThickness = new Thickness(2), MaxWidth = 172, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 12, 0), HorizontalContentAlignment = HorizontalAlignment.Stretch }; tile.Classes.Add("choiceTile"); tile.Classes.Add("themeTile"); tile.Padding = new Thickness(8);
            var caption = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(2, 8, 0, 0) };
            caption.Children.Add(Ui.Text(words, 14)); ((TextBlock)caption.Children[0]).FontWeight = FontWeight.SemiBold;
            var picture = ThemePicture(words);
            var inner = new StackPanel(); inner.Children.Add(picture); inner.Children.Add(caption);
            tile.Content = inner; tile.Click += (_, _) => { if (model.Theme != choice) model.SetTheme(choice); };
            Avalonia.Automation.AutomationProperties.SetName(tile, words + " theme");
            buttons[choice] = tile; tiles.Children.Add(tile);
        }
        void SyncThemes() { foreach (var (choice, tile) in buttons) tile.Classes.Set("selected", choice == model.Theme); }
        syncs.Add(SyncThemes); SyncThemes();
        stack.Children.Add(tiles);
        stack.Children.Add(BuildSunOptions());

        stack.Children.Add(Section("Hints", Toggle("Show tooltips", "Short hints on hover. With them off, budget percentages are still in Week overview.", "ShowTooltips", () => model.Tooltips, v => model.SetTooltips(v))));
        stack.Children.Add(Section("Timeline", Toggle("Display timeline", "Draw the timeline as the pill's display: lit blocks through the honeycomb and segment figures.", "DisplayTimeline", () => model.DisplayTimeline, v => model.SetDisplayTimeline(v))));
        stack.Children.Add(Section("Seasonal", Toggle("Seasonal decorations", "A wreath and lights round the pill at Christmas, 1 December to 6 January. Off by default.", "SeasonalDecorations", () => model.SeasonalDecorations, v => model.SetSeasonalDecorations(v))));
        return stack;
    }

    // ---- the pill
    private Control BuildPill()
    {
        var stack = new StackPanel { Spacing = 0 };
        // Size: the real pill, drawn at the size chosen, over a slider and an Automatic tick. Automatic fits the pill to the screen it is on; a size of your own overrides it.
        var stage = new Border { Height = 92, CornerRadius = new CornerRadius(14), Name = "PillSizePreview" }; stage.Bind(Border.BackgroundProperty, stage.GetResourceObservable("Well")); stage.Bind(Border.BoxShadowProperty, stage.GetResourceObservable("ShadowSunk"));
        var slider = new Slider { Name = "PillSize", Minimum = 50, Maximum = 120, TickFrequency = 10, IsSnapToTickEnabled = true, MinWidth = 160, VerticalAlignment = VerticalAlignment.Center, Value = model.PillScalePercent ?? 100 };
        var automatic = new CheckBox { Name = "PillSizeAutomatic", Content = "Automatic", IsChecked = model.PillScalePercent is null, VerticalAlignment = VerticalAlignment.Center };
        var percent = Ui.Text("", 13); percent.FontWeight = FontWeight.SemiBold; percent.Name = "PillSizeNote"; percent.Width = 112; percent.TextAlignment = TextAlignment.Right; percent.VerticalAlignment = VerticalAlignment.Center;
        Avalonia.Automation.AutomationProperties.SetName(slider, "Pill size");
        void SyncSize()
        {
            var auto = model.PillScalePercent is null;
            if (automatic.IsChecked != auto) automatic.IsChecked = auto;
            slider.IsEnabled = !auto; if (!auto && Math.Abs(slider.Value - model.PillScalePercent!.Value) > .5) slider.Value = model.PillScalePercent.Value;
            var shown = auto ? 95 : model.PillScalePercent!.Value;
            percent.Text = auto ? "Fits the screen" : $"{shown} per cent";
            // The picture is the pill at that size, kept inside the well.
            stage.Child = SettingsPictures.Pill(true, null, Math.Clamp(1.2 * shown / 100.0, 0.7, 1.6));
        }
        slider.PropertyChanged += (_, e) => { if (e.Property == Avalonia.Controls.Primitives.RangeBase.ValueProperty && slider.IsEnabled) { var chosen = (int)Math.Round(slider.Value / 10) * 10; if (chosen != model.PillScalePercent) model.SetPillScale(chosen); } };
        automatic.Click += (_, _) => { model.SetPillScale(automatic.IsChecked == true ? null : (int)Math.Round(slider.Value / 10) * 10); SyncSize(); };
        syncs.Add(SyncSize); SyncSize();
        var sizeRow = Ui.Grid("Auto,*,Auto"); sizeRow.Margin = new Thickness(0, 10, 0, 0); sizeRow.ColumnSpacing = 14;
        Ui.Put(sizeRow, automatic); Ui.Put(sizeRow, slider, 1); Ui.Put(sizeRow, percent, 2);
        var sizeStack = new StackPanel(); sizeStack.Children.Add(stage); sizeStack.Children.Add(sizeRow);
        stack.Children.Add(Section("Size", sizeStack));

        var style = Choices(
            Choice("StyleFull", "Full", "Clock, keys and alert count", SettingsPictures.Pill(true, null, 1), () => !model.CompactPill && !model.TallPill, () => { model.SetCompactPill(false); model.SetTallPill(false); }),
            Choice("StyleCompact", "Compact", "Only the clock. The keys move to its right-click menu", SettingsPictures.Pill(false, null, 1), () => model.CompactPill, () => model.SetCompactPill(true)),
            Choice("StyleTall", "Tall", "A second display below: press it for the day, up next, budgets, the week and more", SettingsPictures.Pill(true, null, 1, tall: true), () => model.TallPill, () => model.SetTallPill(true)));
        stack.Children.Add(Section("Style", style));

        // The windows: nothing here changes how anything looks, so these are plain ticks.
        var rows = new List<Control>
        {
            Toggle("Keep the pill on top", "The pill stays above other windows.", "PillOnTop", () => model.PillOnTop, v => model.SetPillOnTop(v)),
            Toggle("Keep the dashboard on top", "The dashboard stays above other windows.", "DashboardOnTop", () => model.DashboardOnTop, v => model.SetDashboardOnTop(v))
        };
        if (model.TrayAvailable) rows.Add(Toggle("Show in taskbar", "Off runs the timer from the tray alone.", "ShowInTaskbar", () => model.TaskbarButton, v => model.SetTaskbarButton(v)));
        if (model.AutoStart?.Available == true) rows.Add(Toggle("Start with Windows", "Opens the timer when you sign in.", "StartWithWindows", () => model.AutoStart?.IsEnabled == true, v => model.SetAutoStart(v)));
        stack.Children.Add(Section("Windows", Rows(rows.ToArray())));
        return stack;
    }

    // ---- alerts
    private Control BuildAlerts()
    {
        var stack = new StackPanel { Spacing = 0 };
        // The three choices are three different pills: a card that opens under it, only the red count, or nothing.
        void Pick(string mode) => model.SetPillAlertsMode(mode);
        stack.Children.Add(Section("Pill alerts", Choices(
            Choice("AlertsPeek", "Peek", "A card opens under the pill for a new alert, and again when you press the count", SettingsPictures.Alerts("peek", 1), () => model.PillAlertsMode == "peek", () => Pick("peek")),
            Choice("AlertsLight", "Light only", "Only the red count. Press it to see the list", SettingsPictures.Alerts("light", 1), () => model.PillAlertsMode == "light", () => Pick("light")),
            Choice("AlertsOff", "Off", "No count, no card. The sidebar still lists them", SettingsPictures.Alerts("off", 1), () => model.PillAlertsMode == "off", () => Pick("off")))));
        var counts = new StackPanel();
        foreach (var (what, where) in new[] { ("A reminder has passed", "A note's reminder time has gone."), ("A budget is at 80 per cent or spent", "Budgets are set under Accounts."), ("A break has run past the allowance", "The daily break is set under Day and time.") })
        {
            var row = Ui.Grid("Auto,230,*"); row.Margin = new Thickness(0, 8); row.ColumnSpacing = 10;
            var dot = new Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center }; dot.Bind(Shape.FillProperty, dot.GetResourceObservable("Overtime"));
            Ui.Put(row, dot); var w = Ui.Text(what, 13.5); w.FontWeight = FontWeight.SemiBold; Ui.Put(row, w, 1); Ui.Put(row, Ui.Text(where, 12.5, "muted"), 2);
            counts.Children.Add(row);
        }
        var order = Ui.Text("Reminders from an earlier day come first, then a spent budget, then the rest. A card that opens by itself folds after 12 seconds, unless a budget is spent.", 12, "muted"); order.TextWrapping = TextWrapping.Wrap; counts.Children.Add(order);
        stack.Children.Add(Section("What counts as an alert", counts));
        return stack;
    }

    // ---- day and time
    private Control BuildDay()
    {
        var grid = Ui.Grid("1.15*,*"); grid.ColumnSpacing = 34;
        var left = Section("Working hours", new ShiftEditor(model, () => { }, null));
        // The first day of the week used to be asked for only in the first-run setup; it is here too, and the week on screen follows it.
        var days = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
        var start = new ChoiceStrip { Name = "WeekStartsOn", Look = ChoiceLook.Days, ItemsSource = days.Select(d => d.ToString()).ToList(), ShortLabel = d => d[..3] };
        Avalonia.Automation.AutomationProperties.SetName(start, "Week starts on");
        var syncing = false;
        void SyncStart() { syncing = true; start.SelectedIndex = Array.IndexOf(days, model.WeekStartsOn); syncing = false; }
        start.SelectionChanged += (_, _) => { if (!syncing && start.SelectedIndex >= 0) model.SetWeekStart(days[start.SelectedIndex]); };
        syncs.Add(SyncStart); SyncStart();
        // How time is written is a two-way choice whose options are the examples themselves.
        var hm = NotesPage.Seg("TimeFormatHm", "01:30", !model.DecimalHours, () => { if (model.DecimalHours) model.ToggleFormat(); });
        var dec = NotesPage.Seg("TimeFormatDecimal", "1.50h", model.DecimalHours, () => { if (!model.DecimalHours) model.ToggleFormat(); });
        var format = new Border { Name = "TimeFormat", HorizontalAlignment = HorizontalAlignment.Right, Child = Ui.Horizontal(hm, dec) }; format.Classes.Add("segTrack");
        void SyncFormat() { hm.Classes.Set("selected", !model.DecimalHours); dec.Classes.Set("selected", model.DecimalHours); }
        syncs.Add(SyncFormat); SyncFormat();
        // Where a day begins: midnight unless someone works through it. A night shift is then one day on one page and one column in the week.
        var dayStart = new TextBox { Name = "DayStartBox", Width = 84, Height = 36, MinHeight = 36, FontSize = 13.5, HorizontalContentAlignment = HorizontalAlignment.Center };
        Avalonia.Automation.AutomationProperties.SetName(dayStart, "Day starts at");
        void SyncDayStart() { syncing = true; dayStart.Text = model.DayStart.ToString("hh\\:mm", CultureInfo.InvariantCulture); syncing = false; }
        void CommitDayStart()
        {
            if (syncing) return;
            if (TimeSpan.TryParseExact((dayStart.Text ?? "").Trim(), new[] { "h\\:mm", "hh\\:mm" }, CultureInfo.InvariantCulture, out var at) && at >= TimeSpan.Zero && at < TimeSpan.FromDays(1)) model.SetDayStart(at);
            else { model.Refuse("Time, like 18:00"); SyncDayStart(); }
        }
        dayStart.LostFocus += (_, _) => CommitDayStart(); dayStart.KeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitDayStart(); e.Handled = true; } };
        dayStart.HorizontalAlignment = HorizontalAlignment.Right; // Only the time box: the whole setting is one time.
        syncs.Add(SyncDayStart); SyncDayStart();
        var side = new StackPanel();
        side.Children.Add(Section("Week", Rows(Row("Week starts on", "The Week overview and the exports begin here.", new Border(), start), Row("Day starts at", "Where one day ends and the next begins. Leave it at midnight unless you work through it.", dayStart), Row("How time is written", "A 90 minute block reads 01:30, or 1.50h. Exports follow it.", format))));
        Ui.Put(grid, left); Ui.Put(grid, side, 1);
        return grid;
    }

    // ---- accounts
    private StackPanel accountList = null!; private TextBlock accountSummary = null!;
    private Control BuildAccounts()
    {
        var stack = new StackPanel { Spacing = 10 };
        accountSummary = Ui.Text("", 13, "muted"); accountSummary.Name = "AccountsSummary"; accountSummary.VerticalAlignment = VerticalAlignment.Center;
        var edit = Ui.DialogButton("Edit accounts and budgets…", () => { if (TopLevel.GetTopLevel(this) is DashboardWindow dashboard) dashboard.Dialogs.Show(new AccountsDialog(model)); }, "EditAccounts", true);
        edit.Content = Ui.Horizontal(new IconGlyph(IconKind.Edit), Ui.Text("Edit accounts and budgets…", 14));
        var top = Ui.Grid("*,Auto"); Ui.Put(top, accountSummary); Ui.Put(top, edit, 1); stack.Children.Add(top);
        accountList = new StackPanel { Name = "AccountList" }; stack.Children.Add(accountList);
        var note = Ui.Text("Hiding an account only removes it from the tracker list. Its time stays in every report.", 12, "muted"); note.TextWrapping = TextWrapping.Wrap; stack.Children.Add(note);
        syncs.Add(RefreshAccounts); RefreshAccounts();
        return stack;
    }
    // A read-only look at each account: its colour, its budget and whether it is in the tracker. The form that changes them is the dialog.
    private void RefreshAccounts()
    {
        var all = model.ReviewAccounts.Where(a => !BreakAccount.Is(a.Name)).ToList();
        var shown = model.Accounts.Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        accountSummary.Text = $"{all.Count} accounts · {all.Count(a => a.Budget is not null)} with budgets · {all.Count(a => !shown.Contains(a.Name))} hidden from the tracker";
        accountList.Children.Clear();
        foreach (var account in all)
        {
            var row = Ui.Grid("Auto,*,150,130"); row.Height = 36; row.ColumnSpacing = 12; row.Name = "SettingsAccount_" + account.Name.Replace(" ", "");
            var visible = shown.Contains(account.Name);
            Ui.Put(row, NoteParts.Dot(model, account.Name, 12));
            var name = Ui.Text(account.Display, 13.5); name.FontWeight = FontWeight.Medium; name.VerticalAlignment = VerticalAlignment.Center; Ui.Put(row, name, 1);
            Ui.Put(row, Ui.Text(account.Budget is { } budget ? $"{model.FormatShort(budget)} per {account.Period}" : "No budget", 13, "muted"), 2);
            Ui.Put(row, Ui.Text(visible ? "In the tracker" : "Hidden from tracker", 12, "muted"), 3);
            foreach (var cell in row.Children.OfType<TextBlock>()) cell.VerticalAlignment = VerticalAlignment.Center;
            if (!visible) row.Opacity = .55;
            var line = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = row }; line.Bind(Border.BorderBrushProperty, line.GetResourceObservable("Hover"));
            accountList.Children.Add(line);
        }
    }

    // Under the tiles while Sunset is chosen: the place whose sun sets the times, or two times typed in, and what they come to today.
    private Control BuildSunOptions()
    {
        var places = new List<string>(SunTimes.Cities.Select(c => c.Name)) { "Set times by hand" };
        var place = new ComboBox { Name = "SunPlace", ItemsSource = places, Width = 190, Height = 36, MinHeight = 36, FontSize = 13.5 };
        Avalonia.Automation.AutomationProperties.SetName(place, "Place for sunrise and sunset");
        TextBox Time(string name, string words)
        {
            var box = new TextBox { Name = name, Width = 74, Height = 36, MinHeight = 36, TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, MaxLength = 5 };
            Avalonia.Automation.AutomationProperties.SetName(box, words); return box;
        }
        var dark = Time("SunDarkFrom", "Dark from"); var light = Time("SunLightFrom", "Light from");
        var summary = Ui.Text("", 12, "muted"); summary.Name = "SunSummary"; summary.TextWrapping = TextWrapping.Wrap; summary.Margin = new Thickness(0, 4, 0, 0);
        var hand = Ui.Horizontal(Ui.Text("Dark from", 13), dark, Ui.Text("Light from", 13), light); hand.Name = "SunHandTimes";
        var host = new StackPanel { Name = "SunOptions", Margin = new Thickness(0, 0, 0, 16), Spacing = 8 };
        host.Children.Add(Ui.Horizontal(Ui.Text("Place", 13), place)); host.Children.Add(hand); host.Children.Add(summary);
        var syncing = false;
        void Sync()
        {
            host.IsVisible = model.Theme == AppThemes.Sun; if (!host.IsVisible) return;
            syncing = true;
            var chosen = model.SunPlace == SunTimes.ByHand ? "Set times by hand" : model.SunPlace;
            if (!Equals(place.SelectedItem, chosen)) place.SelectedItem = chosen;
            var byHand = model.SunPlace == SunTimes.ByHand; hand.IsVisible = byHand;
            var times = model.SunHandTimes; if (!dark.IsFocused) dark.Text = times.DarkFrom.ToString(@"hh\:mm"); if (!light.IsFocused) light.Text = times.LightFrom.ToString(@"hh\:mm");
            var (lightFrom, darkFrom) = model.SunDay(model.Engine.Now);
            summary.IsVisible = !byHand;
            summary.Text = $"Today in {model.SunPlace}: light from {lightFrom:hh\\:mm}, dark from {darkFrom:hh\\:mm}. Follows the sun through the year.";
            syncing = false;
        }
        place.SelectionChanged += (_, _) => { if (!syncing && place.SelectedItem is string picked) model.SetSunPlace(picked == "Set times by hand" ? SunTimes.ByHand : picked); };
        void Commit()
        {
            if (syncing) return;
            if (TimeText.TryParseClock(dark.Text, model.Engine.Now.Date, false, out var d) && TimeText.TryParseClock(light.Text, model.Engine.Now.Date, false, out var l)) model.SetSunHandTimes(d.TimeOfDay, l.TimeOfDay);
            else { model.Refuse("Time, like 19:00"); Sync(); }
        }
        foreach (var box in new[] { dark, light }) { box.LostFocus += (_, _) => Commit(); box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } }; }
        syncs.Add(Sync); Sync();
        return host;
    }

    // A small picture of the dashboard in a theme: the page, the rail, a bar for the title and three blocks. System shows Light and Dark side by side.
    private static Control ThemePicture(string words)
    {
        Border Half(string page, string rail, string bar)
        {
            var blocks = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,1.3*,1*"), Margin = new Thickness(0, 6, 0, 0), Height = 20 };
            Border Block(string a, string b, int column) { var block = new Border { CornerRadius = new CornerRadius(5), Margin = new Thickness(column == 0 ? 0 : 2, 0, 0, 0), Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative), GradientStops = { new GradientStop(Color.Parse(a), 0), new GradientStop(Color.Parse(b), 1) } } }; Grid.SetColumn(block, column); return block; }
            blocks.Children.Add(Block("#A8D4FF", "#7F98F7", 0)); blocks.Children.Add(Block("#B2EFD6", "#5FD6AE", 1)); blocks.Children.Add(Block("#FFD9B8", "#FF9F84", 2));
            var content = new StackPanel { Margin = new Thickness(8, 10, 6, 6) };
            content.Children.Add(new Border { Height = 9, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, Width = 46, Background = new SolidColorBrush(Color.Parse(bar)) }); content.Children.Add(blocks);
            var half = new Grid { ColumnDefinitions = new ColumnDefinitions("16,*"), Background = new SolidColorBrush(Color.Parse(page)) };
            half.Children.Add(new Border { Background = new SolidColorBrush(Color.Parse(rail)) }); Grid.SetColumn(content, 1); half.Children.Add(content);
            return new Border { Child = half };
        }
        var light = Half("#D9D9D3", "#FFFFFF", "#9A9DA6"); var dark = Half("#080C14", "#2C3858", "#7684AB");
        Control inside = words switch { "Light" => light, "Dark" => dark, _ => new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Children = { light, dark } } };
        if (inside is Grid system)
        {
            Grid.SetColumn(dark, 1);
            // Sunset is the same two halves with a small sun over the light one and a moon over the dark one. Each half and its mark share one cell of their own grid, so
            // the mark sits over the picture on purpose and is not a neighbour the layout audit would count as an overlap.
            if (words == "Sunset")
            {
                Ellipse Mark(string colour) => new() { Width = 12, Height = 12, Fill = new SolidColorBrush(Color.Parse(colour)), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 6, 5), IsHitTestVisible = false };
                system.Children.Clear(); Grid.SetColumn(dark, 0);
                var day = new Grid { Children = { light, Mark("#F2B134") } }; var night = new Grid { Children = { dark, Mark("#C9D2F0") } };
                Grid.SetColumn(night, 1); system.Children.Add(day); system.Children.Add(night);
            }
        }
        var frame = new Border { Height = 72, CornerRadius = new CornerRadius(10), ClipToBounds = true, BorderThickness = new Thickness(1), Child = inside };
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("Line"));
        return frame;
    }

    // ---- export
    private ComboBox periodPick = null!;
    private Button weekChoice = null!, monthChoice = null!, copyHours = null!;
    private TextBlock periodLabel = null!, exportResult = null!, exportError = null!, weekOnly = null!;
    private List<DateTime> weeks = new(), months = new();
    private bool wholeMonth;
    private Control BuildExport()
    {
        var stack = new StackPanel { Spacing = 12 };
        weekChoice = Ui.DialogButton("Week", () => ChoosePeriod(false), "ExportWeekChoice", true); monthChoice = Ui.DialogButton("Month", () => ChoosePeriod(true), "ExportMonthChoice", false);
        stack.Children.Add(Ui.Horizontal(weekChoice, monthChoice));
        periodLabel = Ui.Text("Week", 13, "muted"); periodPick = new ComboBox { Name = "ExportPeriodPick", HorizontalAlignment = HorizontalAlignment.Stretch, MaxWidth = 360 };
        // The lists run newest first, so the earlier period is the next place down the list.
        var earlier = Ui.IconButton(IconKind.Previous, "Earlier", "ExportPeriodEarlier", () => { if (periodPick.SelectedIndex + 1 < periodPick.ItemCount) periodPick.SelectedIndex++; });
        var later = Ui.IconButton(IconKind.Next, "Later", "ExportPeriodLater", () => { if (periodPick.SelectedIndex > 0) periodPick.SelectedIndex--; });
        var pickLine = Ui.Grid("Auto,*,Auto"); pickLine.ColumnSpacing = 6; Ui.Put(pickLine, earlier); Ui.Put(pickLine, periodPick, 1); Ui.Put(pickLine, later, 2); earlier.VerticalAlignment = later.VerticalAlignment = VerticalAlignment.Center;
        var pickRow = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Left }; pickRow.Children.Add(periodLabel); pickRow.Children.Add(pickLine); pickRow.Width = 360;
        stack.Children.Add(pickRow);
        var note = Ui.Text("Two CSV files are written: the hours by account and day, and every entry. Hundredths of an hour are shared out so each total adds up exactly. The running entry is provisional until it is saved.", 12, "muted");
        note.TextWrapping = TextWrapping.Wrap; stack.Children.Add(note);
        var export = Ui.DialogButton("Export CSV files", ExportFiles, "ExportCsv"); export.Content = Ui.Horizontal(new IconGlyph(IconKind.Export), Ui.Text("Export CSV files", 14));
        copyHours = Ui.DialogButton("Copy hours table", CopyTable, "CopyHours", false); copyHours.Content = Ui.Horizontal(new IconGlyph(IconKind.Copy), Ui.Text("Copy hours table", 14));
        weekOnly = Ui.Text("The table is for a week.", 12.5, "muted"); weekOnly.Name = "CopyHoursNote"; weekOnly.VerticalAlignment = VerticalAlignment.Center; weekOnly.IsVisible = false;
        stack.Children.Add(Ui.Horizontal(export, copyHours, weekOnly));
        exportResult = Ui.Text("", 13); exportResult.Name = "ExportResult"; exportResult.TextWrapping = TextWrapping.Wrap; exportResult.IsVisible = false;
        exportError = Ui.Text("", 13); exportError.Classes.Add("overtime"); exportError.Name = "ExportError"; exportError.TextWrapping = TextWrapping.Wrap; exportError.IsVisible = false;
        stack.Children.Add(exportResult); stack.Children.Add(exportError);
        return stack;
    }
    // Each choice has its own list, from the current one back to the earliest recorded (or a few months if there is little), so any week or month can be picked.
    private void RefreshPeriods()
    {
        var today = model.TodayKey;
        var earliest = model.Engine.Sessions.Select(s => model.DayOf(s.From)).DefaultIfEmpty(today).Min();
        var shown = model.SelectedDate.Date;
        var oldest = new[] { earliest, shown, today.AddDays(-7 * 12) }.Min();
        var latest = today > shown ? today : shown;
        weeks = new List<DateTime>();
        for (var week = model.WeekStart(latest); week >= model.WeekStart(oldest) && weeks.Count < 156; week = week.AddDays(-7)) weeks.Add(week);
        var oldestMonth = new[] { earliest, shown, today.AddMonths(-11) }.Min();
        months = new List<DateTime>();
        for (var month = new DateTime(latest.Year, latest.Month, 1); month >= new DateTime(oldestMonth.Year, oldestMonth.Month, 1) && months.Count < 60; month = month.AddMonths(-1)) months.Add(month);
        FillPeriods(selectShown: true);
    }
    private void FillPeriods(bool selectShown)
    {
        var shown = model.SelectedDate.Date;
        periodPick.ItemsSource = wholeMonth ? months.Select(m => m.ToString("MMMM yyyy")).ToList() : weeks.Select(w => $"{w:d MMM} to {w.AddDays(6):d MMM yyyy}").ToList();
        periodPick.SelectedIndex = selectShown ? Math.Max(0, wholeMonth ? months.IndexOf(new DateTime(shown.Year, shown.Month, 1)) : weeks.IndexOf(model.WeekStart(shown))) : 0;
        periodLabel.Text = wholeMonth ? "Month" : "Week";
        weekChoice.Classes.Set("mainAction", !wholeMonth); monthChoice.Classes.Set("mainAction", wholeMonth);
        // The hours table is laid out by week, so there is nothing to copy for a month.
        copyHours.IsEnabled = !wholeMonth; ToolTip.SetTip(copyHours, wholeMonth ? "Week only" : null); weekOnly.IsVisible = wholeMonth;
    }
    private void ChoosePeriod(bool month) { wholeMonth = month; FillPeriods(selectShown: true); exportResult.IsVisible = exportError.IsVisible = false; }
    private DateTime? ChosenDay() => wholeMonth ? months.ElementAtOrDefault(periodPick.SelectedIndex) : weeks.ElementAtOrDefault(periodPick.SelectedIndex);
    private void ExportFiles()
    {
        var day = ChosenDay();
        var result = TimesheetActions.Export(model, wholeMonth, day == default(DateTime) ? null : day, out var success);
        exportError.IsVisible = !success; exportResult.IsVisible = success;
        if (success) exportResult.Text = result; else exportError.Text = result;
    }
    private async void CopyTable()
    {
        if (wholeMonth || ChosenDay() is not { } week || week == default) return;
        var text = TimesheetActions.Table(model, week); CopiedText = text;
        var result = await TimesheetActions.Copy(TopLevel.GetTopLevel(this), text);
        exportError.IsVisible = !result.Success; exportResult.IsVisible = result.Success;
        if (result.Success) exportResult.Text = result.Message; else exportError.Text = result.Message;
    }
    /// <summary>The last hours table copied, so a test can read it back without a clipboard.</summary>
    public string? CopiedText { get; private set; }

    // ---- about
    private Control BuildAbout()
    {
        var stack = new StackPanel { Spacing = 8 };
        // The getting started setup, for anyone who skipped it or wants to look again. Nothing is lost: it starts from the current settings.
        stack.Children.Add(Row("Getting started", "Go through the first-run setup again. Nothing is lost.", Ui.DialogButton("Run setup again", () => model.RestartSetup(), "RunSetup")));
        // Hidden until unlocked: ten quick clicks on the About tile. Once unlocked it holds the sample-data demo and a switch to hide the features again.
        var developer = new StackPanel { Spacing = 10 };
        // In the demo the developer features are simply on: there is no demo to open from inside it, and the switch is fixed on and greyed out.
        var openDemo = Row("Sample data", null, Ui.DialogButton("Open sample-data demo", Ui.StartDemo, "OpenDemo")); openDemo.IsVisible = !model.IsDemo;
        developer.Children.Add(openDemo);
        var inDemo = Ui.Text("You are in the sample-data demo, so the developer features are on. Nothing here is saved.", 13, "muted"); inDemo.Name = "InDemoNote"; inDemo.TextWrapping = TextWrapping.Wrap; inDemo.IsVisible = model.IsDemo;
        developer.Children.Add(inDemo);
        developer.Children.Add(Row("Controls gallery", "Compare Light and Dark controls and try their states with local samples.",
            Ui.DialogButton("Open controls gallery", () => { if (TopLevel.GetTopLevel(this) is Window owner) NeumorphicGalleryWindow.Open(owner); }, "OpenControlsGallery")));
        var developerToggle = Toggle("Developer features", "The version on the right-click menu.", "DeveloperFeatures", () => model.DeveloperUnlocked, v => model.SetDeveloper(v), out var developerBox);
        developerBox.IsEnabled = !model.IsDemo;
        developer.Children.Add(developerToggle);
        // Frame timing: while it is on, the dashboard shows how long its frames take and Copy report puts the figures on the clipboard. Never saved; it ends when the app does.
        var frames = Ui.Text(FrameMonitor.Instance.Running ? "Running. The figures are in the corner of the dashboard." : "Off.", 13, "muted"); frames.Name = "FrameTimingState"; frames.TextWrapping = TextWrapping.Wrap;
        var frameSwitch = Ui.DialogButton(FrameMonitor.Instance.Running ? "Stop frame timing" : "Start frame timing", () => { }, "FrameTimingToggle");
        frameSwitch.Click += (_, _) =>
        {
            if (FrameMonitor.Instance.Running) FrameMonitor.Instance.Stop();
            else if (TopLevel.GetTopLevel(frameSwitch) is { } window) FrameMonitor.Instance.Start(window);
            frameSwitch.Content = FrameMonitor.Instance.Running ? "Stop frame timing" : "Start frame timing";
            frames.Text = FrameMonitor.Instance.Running ? "Running. The figures are in the corner of the dashboard." : "Off.";
        };
        var frameCopy = Ui.DialogButton("Copy report", async () =>
        {
            if (TopLevel.GetTopLevel(frameSwitch)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(FrameMonitor.Instance.Report());
        }, "FrameTimingCopy");
        // Two switches for finding what costs frames: one draws every raised and sunk shadow empty, the other stops the one-second refresh. Neither is saved.
        var flat = Ui.DialogButton(ThemeEngine.FlatShadows ? "Shadows back on" : "Draw flat (no shadows)", () => { }, "FrameFlatToggle");
        flat.Click += (_, _) => { ThemeEngine.FlatShadows = !ThemeEngine.FlatShadows; if (Avalonia.Application.Current is { } app) ThemeEngine.Register(app, AppThemes.BuiltIn); flat.Content = ThemeEngine.FlatShadows ? "Shadows back on" : "Draw flat (no shadows)"; };
        var pauseTick = Ui.DialogButton(TimerModel.TickPaused ? "Resume the 1 s refresh" : "Pause the 1 s refresh", () => { }, "FrameTickToggle");
        pauseTick.Click += (_, _) => { TimerModel.TickPaused = !TimerModel.TickPaused; pauseTick.Content = TimerModel.TickPaused ? "Resume the 1 s refresh" : "Pause the 1 s refresh"; };
        var fineTimer = Ui.DialogButton(TimerResolution.Requested ? "Give back the 1 ms timer" : "Ask for a 1 ms timer", () => { }, "FrameTimerToggle");
        fineTimer.Click += (_, _) => { TimerResolution.Request(!TimerResolution.Requested); fineTimer.Content = TimerResolution.Requested ? "Give back the 1 ms timer" : "Ask for a 1 ms timer"; };
        developer.Children.Add(Row("Find the cost", "Draw flat takes every raised and sunk shadow away; pausing the refresh stops routine clock redraws while checkpoint saves, reminder maintenance and important notifications continue. Asking for a 1 ms timer makes Windows tick finely while the app runs (the report names the current tick). Try each on the Timeline for 15 seconds, then copy the report.", Ui.Horizontal(flat, pauseTick, fineTimer)));
        developer.Children.Add(Row("Frame timing", "How long the dashboard's frames take, for judging smoothness. Switch it on, use the page you want to test for a few seconds, then copy the report.", Ui.Horizontal(frameSwitch, frameCopy), frames));
        // Preview a season: shows one now whatever the date, so each can be tried on any day. Not saved.
        var seasonNames = new List<string> { "Automatic" }; seasonNames.AddRange(Seasons.All.Select(x => x.Name));
        var seasonPick = new ComboBox { Name = "SeasonPreview", ItemsSource = seasonNames, Width = 170, Height = 36, MinHeight = 36, FontSize = 13.5 };
        Avalonia.Automation.AutomationProperties.SetName(seasonPick, "Preview a season");
        var seasonSyncing = false;
        void SyncSeason() { seasonSyncing = true; seasonPick.SelectedIndex = model.SeasonPreview is { } id ? 1 + Seasons.All.ToList().FindIndex(x => x.Id == id) : 0; seasonSyncing = false; }
        seasonPick.SelectionChanged += (_, _) => { if (!seasonSyncing && seasonPick.SelectedIndex >= 0) model.SetSeasonPreview(seasonPick.SelectedIndex == 0 ? null : Seasons.All[seasonPick.SelectedIndex - 1].Id); };
        syncs.Add(SyncSeason); SyncSeason();
        developer.Children.Add(Row("Preview a season", "Shows the chosen season on the pill now, whatever the date and whether or not seasonal decorations are on. Not saved.", seasonPick));
        var developerSection = new Border { Name = "DevelopmentSection", Child = developer, IsVisible = model.DeveloperUnlocked, Padding = new Thickness(0, 4) };
        syncs.Add(() => developerSection.IsVisible = model.DeveloperUnlocked);
        stack.Children.Add(developerSection);
        var aboutText = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var name = Ui.Text("Basic's Advanced Timer", 14); name.FontWeight = FontWeight.SemiBold; aboutText.Children.Add(name);
        var about = Ui.Text(BuildInfo.Current, 13, "muted"); about.Name = "AboutVersion"; about.TextWrapping = TextWrapping.Wrap; aboutText.Children.Add(about);
        aboutText.Children.Add(Ui.Text("Your data stays on this computer.", 13, "muted"));
        var unlockHint = Ui.Text("", 12, "muted"); unlockHint.Name = "UnlockHint"; unlockHint.IsVisible = false; aboutText.Children.Add(unlockHint);
        var aboutSection = new Border { Name = "AboutSection", Child = aboutText, Padding = new Thickness(0, 10), Background = Brushes.Transparent };
        // Ten presses, each soon after the one before, unlock the developer features. A pause starts the count again, so ordinary clicks never do it.
        var presses = 0; var lastPress = DateTime.MinValue;
        aboutSection.PointerPressed += (_, e) =>
        {
            if (!BuildInfo.DeveloperFeatures || model.DeveloperUnlocked) return;
            var now = UnlockClock(); presses = now - lastPress <= UnlockWindow ? presses + 1 : 1; lastPress = now;
            if (presses >= UnlockClicks) { model.SetDeveloper(true); presses = 0; unlockHint.Text = "Developer features unlocked."; unlockHint.IsVisible = true; }
            else if (presses >= UnlockClicks - 4) { unlockHint.Text = $"{UnlockClicks - presses} more clicks to unlock developer features."; unlockHint.IsVisible = true; }
        };
        stack.Children.Add(aboutSection);
        return stack;
    }

    // ---- pieces
    private static TextBlock SectionLabel(string text) { var t = Ui.Text(text.ToUpperInvariant(), 11.5, "muted"); t.FontWeight = FontWeight.Bold; t.LetterSpacing = 0.6; return t; }
    /// <summary>A small heading over what it holds, with room under it.</summary>
    private static StackPanel Section(string label, Control content)
    {
        var stack = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 18) };
        stack.Children.Add(SectionLabel(label)); stack.Children.Add(content);
        return stack;
    }
    /// <summary>A picture of what a choice does, with its name and one line under it. The chosen one wears the accent ring, as the theme tiles do.</summary>
    private Button Choice(string name, string label, string description, Control picture, Func<bool> chosen, Action choose, double pictureHeight = 66)
    {
        var frame = new Border { Height = pictureHeight, CornerRadius = new CornerRadius(9), ClipToBounds = true, Child = picture }; frame.Bind(Border.BackgroundProperty, frame.GetResourceObservable("Hover"));
        var words = new StackPanel { Spacing = 2, Margin = new Thickness(2, 8, 2, 0) };
        var head = Ui.Text(label, 13.5); head.FontWeight = FontWeight.SemiBold; words.Children.Add(head);
        var detail = Ui.Text(description, 12, "muted"); detail.TextWrapping = TextWrapping.Wrap; words.Children.Add(detail);
        var inner = new StackPanel(); inner.Children.Add(frame); inner.Children.Add(words);
        var tile = new Button { Name = name, Content = inner, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(8), BorderThickness = new Thickness(2), Margin = new Thickness(0, 0, 10, 0) };
        tile.Classes.Add("choiceTile"); tile.Classes.Add("themeTile");
        Avalonia.Automation.AutomationProperties.SetName(tile, label);
        tile.Click += (_, _) => { if (!chosen()) choose(); };
        void Sync() => tile.Classes.Set("selected", chosen());
        syncs.Add(Sync); Sync();
        return tile;
    }
    /// <summary>Each group shares equal widths and the height of its tallest caption; other groups size themselves independently.</summary>
    private static Control Choices(params Button[] tiles)
    {
        var grid = new Avalonia.Controls.Primitives.UniformGrid { Rows = 1, Columns = tiles.Length, HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 250 * tiles.Length };
        foreach (var tile in tiles) grid.Children.Add(tile);
        return grid;
    }
    private static TextBlock Label(string text) { var t = Ui.Text(text, 14); t.FontWeight = FontWeight.SemiBold; return t; }
    /// <summary>Rows set one under another, with a hairline between them.</summary>
    private static StackPanel Rows(params Control[] rows)
    {
        var stack = new StackPanel();
        foreach (var row in rows)
        {
            if (stack.Children.Count > 0) { var rule = Ui.Rule(); rule.Opacity = .6; stack.Children.Add(rule); }
            stack.Children.Add(row);
        }
        return stack;
    }
    /// <summary>A line of settings: its name and what it does on the left, its control on the right.</summary>
    private static Grid Row(string label, string? description, Control control, Control? below = null)
    {
        var grid = Ui.Grid("*,Auto"); grid.Margin = new Thickness(0, 11);
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        text.Children.Add(Label(label));
        if (description is not null) { var d = Ui.Text(description, 12, "muted"); d.TextWrapping = TextWrapping.Wrap; text.Children.Add(d); }
        if (below is not null) text.Children.Add(below);
        Ui.Put(grid, text); control.VerticalAlignment = VerticalAlignment.Center; Ui.Put(grid, control, 1);
        return grid;
    }
    /// <summary>A tick box with its name and what it does beside it.</summary>
    private Grid Toggle(string label, string description, string name, Func<bool> read, Action<bool> write) => Toggle(label, description, name, read, write, out _);
    private Grid Toggle(string label, string description, string name, Func<bool> read, Action<bool> write, out CheckBox box)
    {
        var check = new CheckBox { Name = name, IsChecked = read(), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0), MinWidth = 0 };
        Avalonia.Automation.AutomationProperties.SetName(check, label);
        // Clicking applies the change and then reads the truth back, so a refused change (such as hiding the taskbar button with no tray) never leaves the box lying.
        check.Click += (_, _) => { write(check.IsChecked == true); check.IsChecked = read(); };
        syncs.Add(() => { if (check.IsChecked != read()) check.IsChecked = read(); });
        var grid = Ui.Grid("Auto,*"); grid.Margin = new Thickness(0, 11);
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Label(label)); var d = Ui.Text(description, 12, "muted"); d.TextWrapping = TextWrapping.Wrap; text.Children.Add(d);
        if (name == "ShowTooltips") d.Name = "TooltipsNote";
        Ui.Put(grid, check); Ui.Put(grid, text, 1); text.Margin = new Thickness(4, 0, 0, 0);
        box = check; return grid;
    }
}

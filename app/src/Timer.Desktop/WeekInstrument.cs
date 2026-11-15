using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Timer.Core;

namespace Timer.Desktop;

/// <summary>
/// How a display draws one piece of text, a cell or a ghost: lit segments with a bloom (Phosphor) or plain type and grain on paper (E-ink). Drawn displays (the week,
/// later the tall pill's rows) ask a skin instead of asking the style themselves, so one layout serves both looks.
/// </summary>
public sealed class DisplaySkin(bool ink, bool dark, Color text, Color quiet, Color accent, Color amber, Color red, Color live, Color paper, bool pale = false)
{
    public bool Ink { get; } = ink; public bool Dark { get; } = dark;
    /// <summary>Phosphor on a Light theme: the pale flat well the pill and sidebar wear, dark segments on it and no bloom.</summary>
    public bool Pale { get; } = pale;
    public Color Text { get; } = text;
    public Color Quiet { get; } = quiet;
    public Color Accent { get; } = accent;
    public Color Amber { get; } = amber;
    public Color Red { get; } = red;
    public Color Live { get; } = live;
    public Color Paper { get; } = paper;
    private DisplayInk.Colours Colours => DisplayInk.For(Dark);

    /// <summary>The skin for the current style: the dark glass of the pill in Phosphor, the theme's paper in E-ink.</summary>
    public static DisplaySkin For(TimerModel model)
    {
        if (DisplayInk.On)
        {
            var c = DisplayInk.For(model.CurrentTheme.Dark);
            return new DisplaySkin(true, model.CurrentTheme.Dark, c.Ink, c.Muted, c.Lit, c.Amber, c.Red, c.Live, c.Paper);
        }
        if (!model.CurrentTheme.Dark) return new DisplaySkin(false, false, Color.Parse("#10141B"), Color.Parse("#3C414A"), Color.Parse("#5D389A"), Color.Parse("#8A4F00"), Color.Parse("#C42424"), Color.Parse("#0F6E40"), Color.Parse("#F0F0EE"), pale: true);
        var glass = model.GlassTheme; Color G(string key) => Color.Parse(glass.Colour(key));
        return new DisplaySkin(false, true, G("Ink"), G("Muted"), Color.Parse(ThemeEngine.LitOf(glass)), Color.Parse("#F5B63A"), G("Overtime"), G("Live"), G("Surface"));
    }

    /// <summary>The type role for a segment-cell height: the e-ink sizes the mockup settled on for each.</summary>
    public static InkRole RoleFor(double cell) => cell switch
    {
        <= 6.5 => InkRole.Dense, <= 8.5 => InkRole.Caption, <= 9.5 => InkRole.Label, <= 11 => InkRole.Status, <= 13.5 => InkRole.Name, <= 16 => InkRole.Figure,
        <= 20 => new InkRole(22, FontWeight.Bold), <= 26 => InkRole.Hours, _ => InkRole.Clock
    };
    /// <summary>The height of the line a cell of this size stands for. E-ink type is taller than the segments it replaces, so layouts keep a little slack.</summary>
    public static double Line(double cell) => cell;

    public double Width(string text, double cell, bool preserveCase = false) => Ink ? InkType.Width(text, RoleFor(cell), preserveCase) : SegmentText.WidthOf(LowerDisplay.Drawable(text).Length, cell);
    /// <summary>Draws <paramref name="text"/> with its top at <paramref name="top"/> and its cell height <paramref name="cell"/>; returns its width. Right and centre align about x.</summary>
    public double Draw(DrawingContext c, string text, double x, double top, double cell, Color colour, bool ghosts = false, TextAlignment align = TextAlignment.Left, bool preserveCase = false)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var width = Width(text, cell, preserveCase); var left = align == TextAlignment.Right ? x - width : align == TextAlignment.Center ? x - width / 2 : x;
        if (Ink)
        {
            var role = RoleFor(cell); InkType.Draw(c, text, role, new SolidColorBrush(colour), left, top + cell / 2 + role.Size * .365, preserveCase: preserveCase);
        }
        else SegmentText.Draw(c, LowerDisplay.Drawable(text), left, top, cell, colour, ghosts);
        return width;
    }
    /// <summary>The longest start of <paramref name="text"/> that fits in <paramref name="room"/>, with an ellipsis in E-ink where it was cut.</summary>
    public string Fit(string text, double cell, double room, bool preserveCase = false)
    {
        if (Width(text, cell, preserveCase) <= room) return text;
        for (var n = text.Length - 1; n > 0; n--) { var cut = Ink ? text[..n].TrimEnd() + "…" : text[..n]; if (Width(cut, cell, preserveCase) <= room) return cut; }
        return "";
    }

    /// <summary>A lit cell, bar or slice: through the honeycomb with a bloom in Phosphor; ink with grain (and the account's texture) in E-ink.</summary>
    public void Lit(DrawingContext c, Color colour, Rect r, double radius, int texture = 0, bool bloom = true)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        if (Ink) { DisplayInk.Cell(c, Colours, new SolidColorBrush(colour), r, radius); DisplayInk.Texture(c, texture, r, radius, colour); return; }
        if (bloom && !Pale) new PhosphorBloom(colour, 0, .78).Draw(c, new RectangleGeometry(r, radius, radius), true);
        using (Honeycomb.MeshedLarge(c, r)) c.DrawRectangle(new SolidColorBrush(colour), null, r, radius, radius);
    }
    /// <summary>An unlit slot or cell.</summary>
    public void Unlit(DrawingContext c, Rect r, double radius) => c.DrawRectangle(new SolidColorBrush(Ink ? Colours.Ghost : VfdLook.Ghost(Text)), null, r, radius, radius);
    public void Rule(DrawingContext c, Rect r) => c.DrawRectangle(new SolidColorBrush(Ink ? Colours.Rule : SoftTint.WithAlpha(Quiet, .3)), null, r);
    public void Stripes(DrawingContext c, Rect r)
    {
        c.DrawRectangle(new SolidColorBrush(SoftTint.WithAlpha(Text, .1)), null, r, 2, 2);
        using (c.PushClip(r)) { var pen = new Pen(new SolidColorBrush(SoftTint.WithAlpha(Text, .5)), 1.2); for (var k = -r.Height; k < r.Width; k += 4) c.DrawLine(pen, new Point(r.X + k, r.Bottom), new Point(r.X + k + r.Height, r.Y)); }
    }
    /// <summary>A badge as the display's keys are drawn: an outline with the word centred, filled when it is a warning.</summary>
    public double Badge(DrawingContext c, string word, double x, double y, double width, Color colour, bool filled)
    {
        var r = new Rect(x, y, width, 20);
        if (filled) { c.DrawRectangle(new SolidColorBrush(Ink ? colour : SoftTint.WithAlpha(colour, .14)), Ink ? null : new Pen(new SolidColorBrush(colour), 1.2), r, Ink ? 6 : 3.5, Ink ? 6 : 3.5); }
        else c.DrawRectangle(null, new Pen(new SolidColorBrush(Ink ? SoftTint.WithAlpha(Text, .7) : colour), Ink ? 1.5 : 1.2), r.Deflate(.7), Ink ? 5.5 : 3.5, Ink ? 5.5 : 3.5);
        Draw(c, word, x + width / 2, y + 5, 9, filled && Ink ? Paper : Ink ? Text : colour, align: TextAlignment.Center);
        return width;
    }
    /// <summary>A key (Week, Timesheet): filled with ink when chosen in E-ink, an outline in the lit colour in Phosphor.</summary>
    public void Key(DrawingContext c, string word, Rect r, bool chosen)
    {
        if (Ink)
        {
            if (chosen) c.DrawRectangle(new SolidColorBrush(Text), null, r, 8, 8); else c.DrawRectangle(null, new Pen(new SolidColorBrush(SoftTint.WithAlpha(Text, .75)), 1.5), r.Deflate(.75), 7.25, 7.25);
            Draw(c, word, r.Center.X, r.Y + (r.Height - 9) / 2, 9, chosen ? Paper : Text, align: TextAlignment.Center); return;
        }
        c.DrawRectangle(chosen ? new SolidColorBrush(SoftTint.WithAlpha(Accent, .14)) : null, new Pen(new SolidColorBrush(chosen ? Accent : SoftTint.WithAlpha(Text, .35)), 1.2), r, 4, 4);
        Draw(c, word, r.Center.X, r.Y + (r.Height - 9) / 2, 9, chosen ? Accent : Quiet, align: TextAlignment.Center);
    }
}

/// <summary>
/// The Week page as one instrument (the board "The week as one instrument"): the week's totals, each day as a strip of its blocks against its schedule, and the accounts with
/// their budgets, on one display in either look. A Timesheet key turns the day strips into hours a day. Accounts with no time and no budget fold into one row, and a long list
/// scrolls by whole rows with the wheel. The figures are <see cref="WeekOverview.Figures"/>, so the screen and the tests read the same numbers.
/// </summary>
public sealed class WeekInstrument : Panel
{
    public const string PanelName = "WeekInstrument";
    private const double Pad = 24, RowH = 30, DayH = 40, ScrollColumn = 4;
    private readonly TimerModel model; private readonly WeekFigures w; private readonly WeekTable grid; private readonly DisplaySkin skin;
    private readonly Surface surface; private readonly Canvas overlay = new();
    private int scroll { get => model.WeekPresentation.Scroll; set => model.WeekPresentation.Scroll = value; }
    private bool showQuiet { get => model.WeekPresentation.ShowQuiet; set => model.WeekPresentation.ShowQuiet = value; }
    private string? pick { get => model.WeekPresentation.TracedAccount; set => model.WeekPresentation.TracedAccount = value; }
    private readonly Dictionary<string, Button> zones = new();
    private AccountCatalogue catalogue = null!;
    private static readonly CultureInfo British = CultureInfo.GetCultureInfo("en-GB");

    private sealed record Bar(DateTime From, DateTime To, string Account, bool Break);
    private sealed record DayLine(WeekDay Day, List<Bar> Bars, DateTime Origin);
    private sealed record AccountLine(TimerAccount Account, TimeSpan Week, BudgetTableRow? Budget, double Elapsed, string Pace, TimeSpan[] PerDay, WeekTableRow? Table, string Description);
    private sealed record Row(AccountLine? Account, int Folded);

    private readonly List<DayLine> days = new(); private readonly List<AccountLine> accounts = new(); private List<Row> rows = new();
    private double axisFrom = 7, axisTo = 19;

    public WeekInstrument(TimerModel model, WeekFigures w, WeekTable grid)
    {
        this.model = model; this.w = w; this.grid = grid; skin = DisplaySkin.For(model);
        Name = PanelName; surface = new Surface(this); Children.Add(surface); Children.Add(overlay);
        Gather();
        ActualThemeVariantChanged += (_, _) => surface.InvalidateVisual();
    }
    public bool Sheet => model.WeekAsTable;
    /// <summary>The account whose blocks are being traced (the others are dimmed), or none.</summary>
    public string? Traced => pick;
    /// <summary>The accounts whose rows are on show now, in order: the scrolled window of the list, without the fold row.</summary>
    public IReadOnlyList<string> ShownAccounts { get { var f = Layout(surface.Bounds.Size); var first = Math.Min(scroll, Math.Max(0, rows.Count - f.Capacity)); return rows.Skip(first).Take(f.Capacity).Where(r => r.Account is not null).Select(r => r.Account!.Account.Name).ToList(); } }
    /// <summary>How many accounts are folded into the one "more" row (no time this week and no budget).</summary>
    public int Folded => rows.FirstOrDefault(r => r.Account is null)?.Folded ?? 0;
    /// <summary>How many rows fit on the instrument at its present size.</summary>
    public int Capacity => Layout(surface.Bounds.Size).Capacity;
    public int RowCount => rows.Count;

    // ---- the data, gathered once per build
    private void Gather()
    {
        var allAccounts = model.ReviewAccounts; catalogue = AccountCatalogue.Capture(model, allAccounts, patterns: skin.Ink);
        var engine = model.Engine; double lo = 24, hi = 0;
        foreach (var d in w.Days)
        {
            var origin = model.DayBegins(d.Date);
            var bars = engine.Sessions.Where(s => model.CountsOn(s, d.Date) && s.Duration > TimeSpan.Zero).Select(s => new Bar(s.From, s.To, s.Account, BreakAccount.Is(s.Account))).ToList();
            if (engine.Active is { } active && model.DayOf(active.Start) == d.Date && engine.Now > active.Start) bars.Add(new Bar(active.Start, engine.Now, active.Account, BreakAccount.Is(active.Account)));
            days.Add(new DayLine(d, bars.OrderBy(b => b.From).ToList(), origin));
            foreach (var b in bars) { lo = Math.Min(lo, (b.From - origin).TotalHours); hi = Math.Max(hi, (b.To - origin).TotalHours); }
            var plan = engine.Plan(d.Date); if (plan.Working) { lo = Math.Min(lo, (plan.Start - origin).TotalHours); hi = Math.Max(hi, (plan.End - origin).TotalHours); }
        }
        // Whole hours round everything shown, and never less than a working day's width, so a quiet week is not stretched across the strip.
        axisFrom = lo >= hi ? 7 : Math.Floor(lo); axisTo = lo >= hi ? 19 : Math.Ceiling(hi);
        if (axisTo - axisFrom < 8) { var extra = (8 - (axisTo - axisFrom)) / 2; axisFrom = Math.Max(0, Math.Floor(axisFrom - extra)); axisTo = Math.Min(24, axisFrom + 8); }
        axisFrom = Math.Clamp(axisFrom, 0, 22); axisTo = Math.Clamp(axisTo, axisFrom + 2, 24);
        foreach (var a in allAccounts.Where(a => !BreakAccount.Is(a.Name)))
        {
            var perDay = Enumerable.Range(0, 7).Select(i => model.OnDay(a, w.Start.AddDays(i))).ToArray();
            var week = perDay.Aggregate(TimeSpan.Zero, (s, t) => s + t);
            var budget = a.Budget is not null ? grid.Budgets.FirstOrDefault(b => b.Account == a.Display) : null;
            double elapsed = 0; var pace = ""; var description = "No budget";
            if (budget is not null && a.Budget is { } target)
            {
                var reference = model.BudgetReference(w.Start); var used = model.BudgetUsedAt(a, reference); var (from, to) = model.BudgetWindowAt(a, reference);
                elapsed = Math.Clamp((Min(model.Engine.Now, Min(w.Start.AddDays(7), to)) - from).TotalSeconds / Math.Max(1, (to - from).TotalSeconds), 0, 1);
                pace = WeekOverview.PaceOf(used, target, elapsed, model.FormatShort);
                description = AccountPresentation.BudgetDescription(model, a, used, reference);
            }
            accounts.Add(new AccountLine(a, week, budget, elapsed, pace, perDay, grid.Rows.FirstOrDefault(r => r.Account == a.Name), description));
        }
        BuildRows();
    }
    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
    private void BuildRows()
    {
        // The saved order, with the accounts that have no time this week and no budget folded into one row (SHOW puts them back).
        var quiet = accounts.Where(a => a.Week == TimeSpan.Zero && a.Budget is null).ToList();
        rows = accounts.Where(a => !quiet.Contains(a)).Select(a => new Row(a, 0)).ToList();
        if (showQuiet) rows.AddRange(quiet.Select(a => new Row(a, 0)));
        if (quiet.Count > 0) rows.Add(new Row(null, quiet.Count));
    }

    // ---- the layout, shared by drawing and by the buttons laid over it
    private sealed record Frame(double Width, double Height, double L, double R, double S, double Fr, double B0, double B1, double TotalsY, double DaysRuleY, double DaysTop,
        double AccountsRuleY, double AccountsTop, int Capacity, double FootY, double[] DayY, double[] ColumnX);
    private Frame Layout(Size size)
    {
        double W = size.Width, H = size.Height, L = Pad, R = W - Pad - ScrollColumn - 10, S = R - 198, Fr = S - 24, B1 = Fr - 106, B0 = L + 168;
        const double totalsY = 34; var daysRule = 112.0; var top = daysRule + 14; var y = top;
        var dayY = new double[days.Count]; double[] columns = Array.Empty<double>();
        if (!Sheet) { y += 18; for (var i = 0; i < days.Count; i++) { dayY[i] = y; y += DayH; } }
        else
        {
            var n = Math.Max(1, days.Count); var span = (B1 + 40 - B0) / n; columns = Enumerable.Range(0, n).Select(i => B0 + i * span).ToArray();
            y += 22; for (var i = 0; i < days.Count; i++) dayY[i] = y; y += 40; // the Working row
        }
        var accountsRule = y + 10; var accountsTop = accountsRule + 16; var foot = H - 56;
        var capacity = Math.Max(1, (int)Math.Floor((foot - 18 - accountsTop) / RowH));
        return new Frame(W, H, L, R, S, Fr, B0, B1, totalsY, daysRule, top, accountsRule, accountsTop, capacity, foot, dayY, columns);
    }
    private double X(Frame f, double hours) => f.B0 + (hours - axisFrom) / (axisTo - axisFrom) * (f.B1 - f.B0);

    // ---- drawing
    private sealed class Surface(WeekInstrument owner) : Control
    {
        public override void Render(DrawingContext context) { context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size)); owner.Paint(context, Bounds.Size); }
    }
    // The wheel scrolls the accounts from anywhere over them: the buttons laid over the rows would otherwise swallow it between the columns of figures.
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        var f = Layout(surface.Bounds.Size);
        if (rows.Count > f.Capacity && e.GetPosition(this).Y >= f.AccountsRuleY - 6 && e.GetPosition(this).Y < f.FootY - 10) { ScrollBy(e.Delta.Y < 0 ? 1 : e.Delta.Y > 0 ? -1 : 0); e.Handled = true; return; }
        base.OnPointerWheelChanged(e);
    }
    /// <summary>Moves the list of accounts by whole rows, as the wheel does.</summary>
    public void ScrollBy(int delta)
    {
        var f = Layout(surface.Bounds.Size); var max = Math.Max(0, rows.Count - f.Capacity); var next = Math.Clamp(scroll + delta, 0, max);
        if (next == scroll) return; scroll = next; Refresh();
    }
    private void Refresh() { surface.InvalidateVisual(); PlaceButtons(); }
    protected override Size ArrangeOverride(Size finalSize) { var r = base.ArrangeOverride(finalSize); PlaceButtons(); return r; }

    // Dark takes the bright phosphor colours (through ChartColour); the pale display takes the soft ones a little towards its ink, a quieter printed look.
    private Color Tone(string colour) { var c = Color.Parse(model.ChartColour(colour)); return skin.Pale ? Color.Parse(ThemeColours.Mix(ColorHex(c), "#10141B", .16)) : c; }
    private static string ColorHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    private Color Account(AccountLine a) => Tone(a.Account.Colour);
    private Color Account(string name) => Tone(catalogue.Find(name).Colour);
    private bool Dimmed(string account) => pick is not null && !account.Equals(pick, StringComparison.OrdinalIgnoreCase);
    private string Hm(TimeSpan t) => model.FormatShort(t);

    private void Paint(DrawingContext c, Size size)
    {
        if (size.Width < 200 || size.Height < 200) return;
        var f = Layout(size); var s = skin; var quiet = s.Quiet;
        if (s.Ink) DisplayInk.PaperGrain(c, !s.Dark, new Rect(size), 14);
        // Column heads
        s.Draw(c, "Schedule", f.L, 12, 8, quiet); s.Draw(c, "Hours", f.Fr, 12, 8, quiet, align: TextAlignment.Right); s.Draw(c, "Status", f.S, 12, 8, quiet);
        // The week's totals: the scheduled week as a bar of cells, the working time and where it stands.
        var scheduled = w.Scheduled.TotalHours; var span = Math.Max(Math.Max(scheduled, w.Working.TotalHours), 1);
        const int cells = 62; var cw = (f.B1 - f.L + 0) / cells;
        for (var k = 0; k < cells; k++)
        {
            var mid = (k + .5) / cells * span; var r = new Rect(f.L + k * cw, f.TotalsY, cw - 2.5, 18);
            if (mid <= w.Allocated.TotalHours) s.Lit(c, s.Text, r, 1.5, 0, false); else if (mid <= w.Working.TotalHours) s.Stripes(c, r); else s.Unlit(c, r, 1.5);
        }
        if (w.Current && scheduled > 0) { var nx = f.L + (f.B1 - f.L) * Math.Min(1, ElapsedScheduled() / span); c.DrawLine(new Pen(new SolidColorBrush(s.Live), 2), new Point(nx, f.TotalsY - 5), new Point(nx, f.TotalsY + 23)); }
        s.Draw(c, Hm(w.Working), f.Fr, f.TotalsY - 4, 22, s.Text, true, TextAlignment.Right);
        var gap = w.Scheduled - w.Working;
        var (statusWords, statusColour) = gap > TimeSpan.Zero ? ((w.Current ? "" : "Under ") + Hm(gap) + (w.Current ? " to go" : ""), w.Current ? s.Text : s.Amber) : (gap < TimeSpan.Zero ? "Over " + Hm(-gap) : "On schedule", s.Text);
        s.Draw(c, statusWords, f.S, f.TotalsY + 3, 11, statusColour);
        s.Draw(c, $"Allocated {Hm(w.Allocated)}   Unallocated {Hm(w.Unallocated)}   Scheduled {Hm(w.Scheduled)}", f.L, f.TotalsY + 50, 8, quiet);
        s.Draw(c, "Breaks " + Hm(w.Breaks), f.S, f.TotalsY + 50, 8, s.Amber);

        Heading(c, f, "Days", f.DaysRuleY);
        if (!Sheet) DrawDays(c, f); else DrawSheetHead(c, f);
        // Accounts
        var first = Math.Min(scroll, Math.Max(0, rows.Count - f.Capacity)); var shownTo = Math.Min(rows.Count, first + f.Capacity);
        Heading(c, f, rows.Count > f.Capacity ? $"Accounts {first + 1}–{shownTo} of {rows.Count}" : "Accounts", f.AccountsRuleY);
        using (c.PushClip(new Rect(f.L - 12, f.AccountsTop - 5, f.R - f.L + 24, f.Capacity * RowH + 2)))
            for (var k = first; k < shownTo; k++) DrawRow(c, f, rows[k], f.AccountsTop + (k - first) * RowH);
        if (rows.Count > f.Capacity)
        {
            // Where the window is: one piece a row, the rows on show lit, in the margin beside the status column.
            var tx = f.R + 10; var th = f.Capacity * RowH - 6; var piece = th / rows.Count;
            for (var k = 0; k < rows.Count; k++) { var r = new Rect(tx, f.AccountsTop + k * piece, ScrollColumn, Math.Max(2, piece - 2)); if (k >= first && k < shownTo) s.Lit(c, s.Text, r, 1, 0, false); else s.Unlit(c, r, 1); }
        }
        // The foot: the two views.
        s.Rule(c, new Rect(f.L - 8, f.FootY - 10, f.R - f.L + 16, 1));
        s.Key(c, "Week", new Rect(f.L, f.FootY - 3, 70, 28), !Sheet); s.Key(c, "Timesheet", new Rect(f.L + 76, f.FootY - 3, 104, 28), Sheet);
        if (pick is not null) s.Key(c, "Clear trace", new Rect(f.L + 200, f.FootY - 3, 110, 28), false);
    }
    private double ElapsedScheduled()
    {
        // How much of the week's schedule the clock has passed, for the tick on the totals bar.
        var total = TimeSpan.Zero; var today = model.TodayKey;
        foreach (var i in Enumerable.Range(0, 7)) { var d = w.Start.AddDays(i); if (d < today) total += model.ScheduledOn(d); else if (d == today) { var plan = model.Engine.Plan(d); if (plan.Working) total += TimeSpan.FromTicks(Math.Min(model.ScheduledOn(d).Ticks, Math.Max(0, (model.Engine.Now - plan.Start).Ticks))); } }
        return total.TotalHours;
    }
    private void Heading(DrawingContext c, Frame f, string label, double y)
    {
        var s = skin; var width = s.Draw(c, label, f.L, y - 5, 8, s.Quiet); s.Rule(c, new Rect(f.L + width + 10, y, Math.Max(0, f.R - f.L - width - 10 + 12), 1));
    }

    private void DrawDays(DrawingContext c, Frame f)
    {
        var s = skin;
        for (var h = (int)Math.Ceiling(axisFrom); h <= axisTo; h += 2) s.Draw(c, h.ToString("00", CultureInfo.InvariantCulture), X(f, h), f.DaysTop, 8, s.Quiet, align: TextAlignment.Center);
        for (var i = 0; i < days.Count; i++)
        {
            var line = days[i]; var d = line.Day; var by = f.DayY[i] + 6; const double bh = 20; var future = line.Bars.Count == 0;
            var dayName = d.Date.ToString("ddd", British); var nameWidth = s.Draw(c, dayName, f.L, by + 3, 13, d.IsToday ? s.Text : s.Quiet);
            if (d.IsToday) c.DrawRectangle(new SolidColorBrush(s.Accent), null, new Rect(f.L, by + 22, nameWidth, 1.6));
            s.Unlit(c, new Rect(f.B0, by + bh / 2 - 1, f.B1 - f.B0, 2), 1);
            // The scheduled hours as a dashed outline the day's blocks sit inside.
            var plan = model.Engine.Plan(d.Date);
            if (plan.Working)
            {
                var a = X(f, Math.Clamp((plan.Start - line.Origin).TotalHours, axisFrom, axisTo)); var b = X(f, Math.Clamp((plan.End - line.Origin).TotalHours, axisFrom, axisTo));
                c.DrawRectangle(null, new Pen(new SolidColorBrush(SoftTint.WithAlpha(s.Text, future ? .3 : .16)), 1) { DashStyle = new DashStyle(new double[] { 2, 3 }, 0) }, new Rect(a + .5, by - 2.5, Math.Max(2, b - a - 1), bh + 5), 4, 4);
            }
            foreach (var bar in line.Bars)
            {
                var a = X(f, Math.Clamp((bar.From - line.Origin).TotalHours, axisFrom, axisTo)) + 1; var b = X(f, Math.Clamp((bar.To - line.Origin).TotalHours, axisFrom, axisTo)) - 1; if (b - a < 1) continue;
                var r = new Rect(a, by, b - a, bh);
                if (bar.Break) { c.DrawRectangle(null, new Pen(new SolidColorBrush(s.Amber), 1.2), r.Deflate(.6), 3, 3); continue; }
                using (c.PushOpacity(Dimmed(bar.Account) ? .14 : 1)) s.Lit(c, Account(bar.Account), r, 3, model.TextureOf(bar.Account), pick is not null && !Dimmed(bar.Account));
            }
            if (d.IsToday && line.Bars.Count > 0) { var nx = X(f, Math.Clamp((model.Engine.Now - line.Origin).TotalHours, axisFrom, axisTo)); c.DrawLine(new Pen(new SolidColorBrush(s.Live), 2), new Point(nx, by - 9), new Point(nx, by + bh + 4)); }
            s.Draw(c, future ? "--:--" : Hm(d.Working), f.Fr, by + 2, 14, future ? SoftTint.WithAlpha(s.Quiet, .6) : s.Text, true, TextAlignment.Right);
            // A day's status, in words only where there is something to say: time on no account, or blocks laid over each other.
            var note = d.Overlaps ? "Overlap" : d.Unallocated >= TimeSpan.FromMinutes(30) ? Hm(d.Unallocated) + " unallocated" : d.Off ? "Off" : "";
            if (note.Length > 0) s.Draw(c, note, f.S, by + 4, 10, d.Overlaps || d.Unallocated >= TimeSpan.FromMinutes(30) ? s.Amber : s.Quiet);
        }
    }
    private void DrawSheetHead(DrawingContext c, Frame f)
    {
        var s = skin;
        for (var i = 0; i < days.Count; i++) s.Draw(c, days[i].Day.Date.ToString("ddd", British), f.ColumnX[i] + 16, f.DaysTop + 2, 9, days[i].Day.IsToday ? s.Text : s.Quiet);
        var y = f.DayY[0]; s.Draw(c, "Working", f.L, y + 2, 12, s.Quiet);
        for (var i = 0; i < days.Count; i++) { var d = days[i].Day; s.Draw(c, d.HasTime ? Hm(d.Working) : "--", f.ColumnX[i] + 16, y, 14, d.HasTime ? s.Text : SoftTint.WithAlpha(s.Quiet, .6), true); }
        s.Draw(c, Hm(w.Working), f.Fr, y, 14, s.Text, true, TextAlignment.Right);
        var unallocated = days.Where(l => l.Day.Unallocated >= TimeSpan.FromMinutes(15)).OrderByDescending(l => l.Day.Unallocated).FirstOrDefault();
        if (unallocated is not null) s.Draw(c, Hm(unallocated.Day.Unallocated) + " unallocated " + unallocated.Day.Date.ToString("ddd", British), f.S, y + 3, 10, s.Amber);
    }

    private void DrawRow(DrawingContext c, Frame f, Row row, double ry)
    {
        var s = skin; var mid = ry + 11;
        if (row.Account is null)
        {
            s.Draw(c, $"{row.Folded} more · no time or budget", f.L + 12, ry + 6, 10, s.Quiet);
            s.Unlit(c, new Rect(f.B0 + 210, mid - 1, Math.Max(0, f.B1 - f.B0 - 210), 2), 1);
            s.Badge(c, showQuiet ? "Hide" : "Show", f.S + 126, ry + 3, 72, s.Quiet, false); return;
        }
        var a = row.Account; var dim = pick is not null && !a.Account.Name.Equals(pick, StringComparison.OrdinalIgnoreCase);
        using (c.PushOpacity(dim ? .4 : 1))
        {
            if (!dim && pick is not null) c.DrawRectangle(new SolidColorBrush(SoftTint.WithAlpha(s.Accent, .1)), null, new Rect(f.L - 10, ry - 3, f.R - f.L + 20, 28), 5, 5);
            var colour = Account(a);
            s.Lit(c, colour, new Rect(f.L, ry + 3, s.Ink ? 14 : 4, 16), s.Ink ? 3 : 2, catalogue.Find(a.Account.Name).Pattern ?? 0, false);
            var nameX = f.L + (s.Ink ? 24 : 12);
            s.Draw(c, s.Fit(a.Account.Display, 12, f.B0 - 12 - nameX, preserveCase: true), nameX, ry + 3, 12, s.Text, preserveCase: true);
            if (Sheet)
            {
                for (var i = 0; i < days.Count; i++) { var v = a.PerDay[(int)(days[i].Day.Date - w.Start).TotalDays]; var cell = Array.IndexOf(grid.Dates, days[i].Day.Date); var value = cell >= 0 && a.Table is { } table ? table.Cells[cell] : Hm(v); s.Draw(c, v > TimeSpan.Zero ? value : "--", f.ColumnX[i] + 16, ry + 3, 12, v > TimeSpan.Zero ? s.Text : SoftTint.WithAlpha(s.Quiet, .5), v > TimeSpan.Zero); }
            }
            else if (a.Budget is { } budget)
            {
                var over = budget.Fraction >= 1; var warn = budget.Fraction >= .8 && !over; var ahead = a.Pace.StartsWith("Ahead", StringComparison.Ordinal);
                var tone = over ? s.Red : warn || ahead ? s.Amber : s.Text; const int n = 40; var cw = (f.B1 - f.B0) / n;
                for (var q = 0; q < n; q++) { var r = new Rect(f.B0 + q * cw, ry + 6, cw - 2.5, 10); if ((q + .5) / n <= Math.Min(1, budget.Fraction)) s.Lit(c, tone, r, 1, 0, false); else s.Unlit(c, r, 1); }
                var tick = f.B0 + (f.B1 - f.B0) * a.Elapsed; c.DrawLine(new Pen(new SolidColorBrush(SoftTint.WithAlpha(s.Live, .6)), 1.5), new Point(tick, ry + 1), new Point(tick, ry + 21));
                var pct = Math.Round(budget.Fraction * 100) + (s.Ink ? "%" : " pct"); var pctEnd = f.S + 56;
                s.Draw(c, pct, pctEnd, ry + 5, 10, over ? s.Red : s.Text, align: TextAlignment.Right); s.Draw(c, a.Account.Period switch { "day" => "day", "month" => "month", "quarter" => "quarter", _ => "week" }, pctEnd + 7, ry + 6, 9, s.Quiet);
                s.Badge(c, over ? "Over" : ahead ? "Ahead" : "On pace", f.S + 126, ry + 1, 72, over ? s.Red : s.Amber, over || ahead);
            }
            else
            {
                s.Unlit(c, new Rect(f.B0, mid - 1, f.B1 - f.B0, 2), 1); s.Draw(c, "--", f.S + 56, ry + 6, 10, SoftTint.WithAlpha(s.Quiet, .6), align: TextAlignment.Right);
            }
            s.Draw(c, a.Table?.Total is { } total && total != "–" ? total : Hm(a.Week), f.Fr, ry + 3, 13, a.Week > TimeSpan.Zero ? s.Text : SoftTint.WithAlpha(s.Quiet, .7), true, TextAlignment.Right);
        }
    }

    // ---- real buttons over the drawing, for the pointer, the keyboard and screen readers
    private void PlaceButtons()
    {
        var size = surface.Bounds.Size; if (size.Width < 200 || size.Height < 200) return;
        var f = Layout(size); var used = new HashSet<string>();
        Button Zone(string name, Rect r, string label, Action click, bool? selected = null, string? identity = null, string? value = null)
        {
            var key = identity ?? name; used.Add(key);
            if (!zones.TryGetValue(key, out var b))
            {
                b = value is not null ? new ReadoutButton() : selected is not null ? new ModelToggleButton() : new Button();
                b.Name = name; b.Background = Brushes.Transparent; b.BorderThickness = new Thickness(0); b.Padding = new Thickness(0); b.Cursor = new Cursor(StandardCursorType.Hand);
                b.FocusAdorner = DisplayFocus.For(b);
                b.Classes.Add("pillBare"); b.Click += (_, _) => ((Action)b.Tag!).Invoke(); zones[key] = b; overlay.Children.Add(b);
            }
            b.Tag = click; b.Width = Math.Max(1, r.Width); b.Height = Math.Max(1, r.Height);
            b.TabIndex = used.Count; Avalonia.Automation.AutomationProperties.SetAutomationId(b, key);
            Avalonia.Automation.AutomationProperties.SetName(b, label);
            if (b is Avalonia.Controls.Primitives.ToggleButton toggle) toggle.IsChecked = selected == true;
            if (b is ReadoutButton readout) readout.Value = value ?? "";
            Canvas.SetLeft(b, r.X); Canvas.SetTop(b, r.Y); return b;
        }
        void Trace(string account) { pick = string.Equals(pick, account, StringComparison.OrdinalIgnoreCase) ? null : account; Refresh(); }
        void Mode(bool sheet)
        {
            var root = TopLevel.GetTopLevel(this); model.SetWeekAsTable(sheet);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => root?.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name == (sheet ? "WeekKeySheet" : "WeekKeyWeek"))?.Focus(NavigationMethod.Tab), Avalonia.Threading.DispatcherPriority.Loaded);
        }
        if (!Sheet)
            for (var i = 0; i < days.Count; i++)
            {
                var d = days[i].Day; var y = f.DayY[i];
                var b = Zone("WeekDay_" + d.Date.ToString("yyyyMMdd"), new Rect(f.L - 10, y, f.R - f.L + 20, DayH), $"{d.Date.ToString("dddd d MMMM", British)}, {model.Format(d.Working)} working. Open in the Timeline", () => model.OpenDayInTimeline(d.Date));
                ToolTip.SetTip(b, WeekOverview.DayTip(model, d, British));
                var line = days[i]; var n = 0;
                foreach (var bar in line.Bars.Where(x => !x.Break))
                {
                    var from = X(f, Math.Clamp((bar.From - line.Origin).TotalHours, axisFrom, axisTo)) + 1; var to = X(f, Math.Clamp((bar.To - line.Origin).TotalHours, axisFrom, axisTo)) - 1; if (to - from < 2) continue;
                    var account = bar.Account; var shown = catalogue.Find(account).DisplayName; var chosen = string.Equals(pick, account, StringComparison.OrdinalIgnoreCase);
                    var block = Zone($"WeekBlock_{d.Date:yyyyMMdd}_{n++}", new Rect(from, y + 6, to - from, 20), $"{shown}, {bar.From:HH:mm} to {bar.To:HH:mm}. {(chosen ? "Clear trace" : "Trace account")}", () => Trace(account), chosen);
                    ToolTip.SetTip(block, $"{shown} · {bar.From:HH:mm}–{bar.To:HH:mm}");
                }
            }
        else
            for (var i = 0; i < days.Count; i++)
            {
                var d = days[i].Day; var width = Math.Max(40, (f.B1 + 40 - f.B0) / Math.Max(1, days.Count) - 8);
                Zone("WeekDay_" + d.Date.ToString("yyyyMMdd"), new Rect(f.ColumnX[i] + 8, f.DaysTop - 2, width, 22), $"{d.Date.ToString("dddd d MMMM", British)}. Open in the Timeline", () => model.OpenDayInTimeline(d.Date));
                Zone("TimesheetWorking_" + d.Date.ToString("yyyyMMdd"), new Rect(f.ColumnX[i] + 8, f.DayY[i] - 3, width, 28), $"Working time, {d.Date.ToString("dddd d MMMM", British)}, {model.Format(d.Working)}" + (d.Overlaps ? ", some blocks overlap" : ""), () => model.OpenDayInTimeline(d.Date), value: model.Format(d.Working));
            }
        var first = Math.Min(scroll, Math.Max(0, rows.Count - f.Capacity));
        for (var k = first; k < Math.Min(rows.Count, first + f.Capacity); k++)
        {
            var row = rows[k]; var ry = f.AccountsTop + (k - first) * RowH;
            if (row.Account is null) { Zone("WeekShowQuiet", new Rect(f.S + 126, ry + 3, 72, 20), (showQuiet ? "Hide" : "Show") + " the accounts with no time or budget", () => { showQuiet = !showQuiet; BuildRows(); Refresh(); }); continue; }
            var a = row.Account; var name = a.Account.Name; var chosen = string.Equals(pick, name, StringComparison.OrdinalIgnoreCase);
            var summary = a.Table?.CopyTotal ?? model.Format(a.Week);
            var budgetWords = a.Description;
            var z = Zone("WeekAccount_" + name.Replace(" ", ""), new Rect(f.L - 10, ry - 3, Sheet ? f.B0 - f.L - 10 : f.S - f.L - 12, 28), $"{catalogue.Find(name).DisplayName}, {summary} week of {w.Start.ToString("d MMM", British)}. {budgetWords}. {(chosen ? "Clear trace" : "Trace account")}", () => Trace(name), chosen, "account:" + name);
            if (a.Budget is { } budget) ToolTip.SetTip(z, $"{budget.Used} of {budget.Budget} · {budget.Period} · {a.Pace}");
            if (Sheet)
                for (var i = 0; i < days.Count; i++)
                {
                    var date = days[i].Day.Date; var at = Array.IndexOf(grid.Dates, date);
                    var value = at >= 0 && a.Table is { } table ? table.CopyCells[at] : model.Format(a.PerDay[(int)(date - w.Start).TotalDays]);
                    var width = Math.Max(40, (f.B1 + 40 - f.B0) / Math.Max(1, days.Count) - 8);
                    Zone($"TimesheetCell_{name.Replace(" ", "")}_{date:yyyyMMdd}", new Rect(f.ColumnX[i] + 8, ry - 3, width, 28), $"{a.Account.Display}, {date.ToString("dddd d MMMM", British)}, {value}", () => model.OpenDayInTimeline(date), identity: $"cell:{name}:{date:yyyyMMdd}", value: value);
                }
        }
        Zone("WeekKeyWeek", new Rect(f.L, f.FootY - 3, 70, 28), "Week", () => Mode(false), !Sheet);
        Zone("WeekKeySheet", new Rect(f.L + 76, f.FootY - 3, 104, 28), "Timesheet", () => Mode(true), Sheet);
        if (pick is not null) Zone("WeekClearTrace", new Rect(f.L + 200, f.FootY - 3, 110, 28), "Clear account trace", () =>
        {
            var previous = pick; pick = null; Refresh();
            if (previous is not null && zones.TryGetValue("account:" + previous, out var row)) row.Focus(NavigationMethod.Tab);
            else if (zones.TryGetValue("WeekKeyWeek", out var week)) week.Focus(NavigationMethod.Tab);
        });
        foreach (var stale in zones.Keys.Where(key => !used.Contains(key)).ToArray()) { overlay.Children.Remove(zones[stale]); zones.Remove(stale); }
    }
}

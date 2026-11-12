using Avalonia;
using Avalonia.Media;
using Timer.Core;

namespace Timer.Desktop;

/// <summary>What the tall pill's lower display shows. Pressing it steps to the next, in this order.</summary>
public enum PillLower { Strip, Next, Budgets, Week, Both, Bat }

/// <summary>
/// The tall pill's second display row, under the clock and the account name (the board "The tall pill"). It is the same vacuum-fluorescent glass: every shape is drawn
/// through the honeycomb, unlit segments stay faintly drawn, and on a dark theme the lit ones bloom outside the mesh. It shows one of <see cref="PillLower"/> at a time:
/// the day as a strip of blocks, what is up next as a ticker, budgets as level meters, the week as columns, the strip and the ticker together, or a bat at night.
/// It draws from the model each time it is drawn; the owner redraws it when the model changes, and its own timer runs only while the ticker or the bat moves.
/// </summary>
public sealed class LowerDisplay : SteppedControl
{
    public const double RowHeight = 40, BothHeight = 68, Pad = 10;
    public static double HeightFor(PillLower shown) => shown == PillLower.Both ? BothHeight : RowHeight;
    /// <summary>The name of each display, for its tooltip and the Settings words.</summary>
    public static string NameOf(PillLower shown) => shown switch
    {
        PillLower.Strip => "Day strip", PillLower.Next => "Up next", PillLower.Budgets => "Budgets", PillLower.Week => "Week", PillLower.Both => "Day strip and up next", _ => "Night bat"
    };
    /// <summary>The ticker steps a cell in this time, a little faster than the account name, since it carries more.</summary>
    public const double TickerStepMilliseconds = 300;

    private readonly TimerModel model;
    private PillLower shown;
    private double elapsed;
    /// <summary>Seconds of motion so far; tests set it to see the ticker and the bat at a known moment.</summary>
    public double Seconds { get => elapsed / 1000; set { elapsed = value * 1000; InvalidateVisual(); } }
    public PillLower Shown => shown;
    /// <summary>The ribbon the ticker scrolls, as of the last draw (for tests and the accessible name).</summary>
    public string Ribbon { get; private set; } = "";

    public LowerDisplay(TimerModel model) { this.model = model; IsHitTestVisible = false; Height = RowHeight; }

    public void Show(PillLower what)
    {
        if (shown != what) { shown = what; Height = HeightFor(what); }
        InvalidateVisual(); Wake();
    }
    protected override int IntervalMilliseconds => 100;
    protected override bool Animating => IsVisible && !Motion.Reduced && shown is PillLower.Next or PillLower.Both or PillLower.Bat && (shown == PillLower.Bat || Ribbon.Length > TickerCells);
    protected override void Advance(double milliseconds) => elapsed += milliseconds;

    // ---------- drawing: the shapes are collected first, then drawn as bloom, ghosts and lit phosphor ----------
    private readonly record struct Op(Geometry Shape, Color Colour, double Width, bool Filled, bool Lit);
    private sealed class Painter
    {
        public readonly List<Op> Ops = new();
        public Color Ghost;
        public void Line(Point a, Point b, double width, Color colour, bool lit) => Ops.Add(new Op(new LineGeometry(a, b), colour, width, false, lit));
        public void Box(Rect r, double radius, Color colour, bool lit) => Ops.Add(new Op(new RectangleGeometry(r, radius, radius), colour, 0, true, lit));
        public void Outline(Rect r, double radius, double width, Color colour, bool lit) => Ops.Add(new Op(new RectangleGeometry(r, radius, radius), colour, width, false, lit));
        public void Dot(Point c, double radius, Color colour, bool lit) => Ops.Add(new Op(new EllipseGeometry(new Rect(c.X - radius, c.Y - radius, 2 * radius, 2 * radius)), colour, 0, true, lit));
        public void Shape(Geometry g, Color colour, bool lit) => Ops.Add(new Op(g, colour, 0, true, lit));

        /// <summary>A run of sixteen-segment cells, slanted like the account name, at any height: the name's cell scaled (19 wide for 28 tall). Returns its width.</summary>
        public double Text(string text, double x, double y, double h, Func<int, Color> colour, bool ghosts = true)
        {
            var cw = h * 19 / 28; var gw = h * 13.4 / 28; var th = Math.Max(1.1, SegmentName.Thickness * h / 20); var ins = th / 2; var inner = h - 2 * ins; var trimMax = 1.1 * h / 20;
            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i]; var x0 = x + i * cw; var c = colour(i);
                Point P(double fx, double fy) { var yy = ins + fy * inner; return new Point(x0 + ins + fx * gw + (h - yy) * SegmentName.Slant, y + yy); }
                // A colon in running text is two leaning dots, as the clock draws it, not the name's tall pair of bars.
                if (ch == ':') { foreach (var f in new[] { .3, .7 }) Dot(P(.5, f), th * .62, c, true); continue; }
                var on = SegmentName.Font.TryGetValue(ch, out var lit) ? lit : "";
                foreach (var (id, ax, ay, bx, by) in Bars)
                {
                    var isOn = on.Contains(id); if (!isOn && !ghosts) continue;
                    var a = P(ax, ay); var b = P(bx, by); var d = b - a; var len = Math.Sqrt(d.X * d.X + d.Y * d.Y); var trim = Math.Min(trimMax, len / 4); var u = d / len;
                    Line(a + u * trim, b - u * trim, th, isOn ? c : Ghost, isOn);
                }
            }
            return text.Length * cw;
        }
    }
    private static readonly (char Id, double X1, double Y1, double X2, double Y2)[] Bars =
    {
        ('a', 0, 0, .5, 0), ('A', .5, 0, 1, 0), ('d', 0, 1, .5, 1), ('D', .5, 1, 1, 1), ('g', 0, .5, .5, .5), ('G', .5, .5, 1, .5),
        ('f', 0, 0, 0, .5), ('e', 0, .5, 0, 1), ('b', 1, 0, 1, .5), ('c', 1, .5, 1, 1), ('i', .5, 0, .5, .5), ('l', .5, .5, .5, 1),
        ('h', 0, 0, .5, .5), ('j', 1, 0, .5, .5), ('k', 0, 1, .5, .5), ('m', 1, 1, .5, .5),
    };
    /// <summary>Whether the display has a letter for this character (a colon is drawn as two dots).</summary>
    public static bool CanDraw(char ch) => ch == ':' || SegmentName.Font.ContainsKey(ch);
    /// <summary>Words as the display can draw them: upper case, accents stripped, anything it has no letter for a space.</summary>
    public static string Drawable(string words)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var raw in words.ToUpperInvariant().Normalize(System.Text.NormalizationForm.FormKD))
        {
            if (char.GetUnicodeCategory(raw) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            sb.Append(raw is '·' or '–' or '—' ? '-' : SegmentName.Font.ContainsKey(raw) ? raw : ' ');
        }
        return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), " {2,}", " ").Trim();
    }

    private Color C(string key, string fallback) => Ui.ThemeColour(this, key, Color.Parse(fallback));
    private Color AccountColour(string name)
    {
        var account = model.ReviewAccounts.FirstOrDefault(a => a.Name == name);
        return account is null ? C("Muted", "#8A8F98") : Color.Parse(model.ChartColour(account.Colour));
    }

    public override void Render(DrawingContext context)
    {
        FrameMonitor.Bump("draw LowerDisplay");
        var dark = SoftTint.IsDark(this);
        var p = new Painter { Ghost = SoftTint.WithAlpha(C("Ink", "#18233A"), dark ? .085 : .09) };
        switch (shown)
        {
            case PillLower.Strip: Strip(p, 0); break;
            case PillLower.Next: Ticker(p, 0); break;
            case PillLower.Budgets: Budgets(p); break;
            case PillLower.Week: Week(p); break;
            case PillLower.Both: Strip(p, 0); Ticker(p, 28); break;
            default: Bat(p); break;
        }
        // Bloom first, outside the mesh, so it fills the gaps between the cells; then the ghosts and the lit phosphor through the honeycomb.
        var strength = dark ? .78 : .28;
        foreach (var op in p.Ops.Where(o => o.Lit)) new PhosphorBloom(op.Colour, op.Width, strength).Draw(context, op.Shape, op.Filled);
        using (Honeycomb.Meshed(context, new Rect(Bounds.Size)))
            foreach (var op in p.Ops.OrderBy(o => o.Lit))
            {
                var brush = new SolidColorBrush(op.Colour);
                if (op.Filled) context.DrawGeometry(brush, null, op.Shape);
                else context.DrawGeometry(null, new Pen(brush, op.Width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), op.Shape);
            }
    }

    // ---------- the day strip ----------
    /// <summary>The stretch the strip spans today: the scheduled hours, widened to take in every block and now, in whole hours (08 to 18 on a day with no schedule).</summary>
    public (DateTime From, DateTime Until) StripRange()
    {
        var day = model.TodayKey; var plan = model.Engine.Plan(day); var now = model.Engine.Now;
        var from = plan.Working ? plan.Start : model.DayBegins(day).AddHours(8); var until = plan.Working ? plan.End : from.AddHours(10);
        foreach (var s in model.Engine.Sessions.Where(s => model.CountsOn(s, day) && s.Duration > TimeSpan.Zero)) { if (s.From < from) from = s.From; if (s.To > until) until = s.To; }
        if (model.Engine.Active is { } running && running.Start < from) from = running.Start;
        if (now > until && now < model.DayEnds(day)) until = now;
        from = from.Date.AddHours(from.Hour); until = until.Minute == 0 && until.Second == 0 ? until : until.Date.AddHours(until.Hour + 1);
        return (from, until);
    }
    private void Strip(Painter p, double y0)
    {
        var (from, until) = StripRange(); var now = model.Engine.Now; var day = model.TodayKey;
        var quiet = C("Muted", "#8A8F98"); var amber = StatusFace.BreakGlyphColour(this); var accent = C("AccentLit", "#9A62C9");
        var x0 = 30.0; var x1 = Bounds.Width - 30; var span = Math.Max(1, (until - from).TotalHours);
        double X(DateTime t) => x0 + Math.Clamp((t - from).TotalHours / span, 0, 1) * (x1 - x0);
        p.Text(from.Hour.ToString("00"), Pad, y0 + 10, 11, _ => quiet); p.Text((until.Hour % 24).ToString("00"), x1 + 4, y0 + 10, 11, _ => quiet);
        for (var h = from; h <= until; h = h.AddHours(1)) p.Box(new Rect(X(h) - .8, y0 + 26, 1.6, 3), .8, h <= now ? SoftTint.WithAlpha(quiet, .55) : p.Ghost, h <= now);
        // The rest of the day is a faint track, so the strip always reads as the whole day.
        p.Box(new Rect(x0, y0 + 15, x1 - x0, 4), 2, p.Ghost, false);
        foreach (var m in model.Notes.MeetingsOn(day))
        {
            var a = X(ReviewViews.MeetingStart(m)); var b = X(ReviewViews.MeetingEnd(m)); if (b - a < 1) continue;
            p.Box(new Rect(a + 1, y0 + 6, Math.Max(1, b - a - 2), 2.2), 1.1, quiet, true);
        }
        var blocks = model.Engine.Sessions.Where(s => model.CountsOn(s, day) && s.Duration > TimeSpan.Zero).Select(s => (s.Account, From: s.From, To: s.To)).ToList();
        if (model.Engine.Active is { } running) blocks.Add((running.Account, running.Start, now));
        foreach (var (account, start, end) in blocks.OrderBy(b => BreakAccount.Is(b.Account)))
        {
            var a = X(start); var b = X(end); if (b - a < .5) continue;
            if (BreakAccount.Is(account)) p.Outline(new Rect(a + 1.5, y0 + 12.7, Math.Max(1, b - a - 3), 8.6), 2.6, 1.3, amber, true);
            else p.Box(new Rect(a + 1, y0 + 12, Math.Max(1, b - a - 2), 10), 3, AccountColour(account), true);
        }
        if (now >= from && now <= until)
        {
            var x = X(now);
            p.Line(new Point(x, y0 + 4), new Point(x, y0 + 31), 1.4, accent, true);
            p.Shape(new PolylineGeometry(new[] { new Point(x - 3, y0 + 2), new Point(x + 3, y0 + 2), new Point(x, y0 + 6) }, true), accent, true);
        }
    }

    // ---------- up next ----------
    public const double TickerHeight = 15, TickerLeft = 32;
    private int TickerCells => (int)Math.Max(1, Math.Floor((Bounds.Width - TickerLeft - Pad) / (TickerHeight * 19 / 28)));
    /// <summary>What the ticker says, with the colour role of each piece: the next meeting and the next reminder today, or that nothing is coming up.</summary>
    public IReadOnlyList<(string Words, string Role)> UpNext()
    {
        var now = model.Engine.Now; var day = model.TodayKey; var parts = new List<(string, string)>();
        string In(DateTime t) { var m = (int)Math.Ceiling((t - now).TotalMinutes); return m < 60 ? $"IN {m} MIN" : $"IN {m / 60}H {m % 60:00}"; }
        var meeting = model.Notes.MeetingsOn(day).Concat(model.Notes.MeetingsOn(day.AddDays(1))).FirstOrDefault(m => ReviewViews.MeetingStart(m) >= now);
        var going = model.Notes.MeetingsOn(day).FirstOrDefault(m => ReviewViews.MeetingStart(m) < now && ReviewViews.MeetingEnd(m) > now);
        if (going is not null) parts.AddRange(new[] { ("NOW ", "quiet"), ($"{Drawable(model.Notes.Text(going))} TILL {ReviewViews.MeetingEnd(going):HH:mm}", "ink"), ("   ", "ink") });
        if (meeting is not null) parts.AddRange(new[] { ("NEXT ", "quiet"), ($"{ReviewViews.MeetingStart(meeting):HH:mm} {Drawable(model.Notes.Text(meeting))}", "ink"), (" " + In(ReviewViews.MeetingStart(meeting)), "accent"), ("   ", "ink") });
        var reminder = model.Notes.PinnedOn(day).FirstOrDefault(n => NotesModel.ReminderOf(n) >= now);
        if (reminder is not null && NotesModel.ReminderOf(reminder) is { } due) parts.AddRange(new[] { ("REMIND ", "quiet"), ($"{due:HH:mm} {Drawable(model.Notes.Text(reminder))}", "ink"), ("   ", "ink") });
        if (model.Notes.Overdue.Count > 0) parts.AddRange(new[] { ("LATE ", "red"), (Drawable(model.Notes.Text(model.Notes.Overdue[0])), "ink"), ("   ", "ink") });
        if (parts.Count == 0) parts.Add(("NOTHING COMING UP TODAY", "quiet"));
        return parts;
    }
    private void Ticker(Painter p, double y0)
    {
        var accent = C("AccentLit", "#9A62C9");
        // A small calendar mark, then the ribbon in cells 15 tall.
        p.Outline(new Rect(11, y0 + 8, 13, 13), 3, 1.3, accent, true); p.Line(new Point(11.5, y0 + 12), new Point(23.5, y0 + 12), 1.3, accent, true); p.Box(new Rect(14, y0 + 15, 2.4, 2.4), .6, accent, true);
        var roles = new Dictionary<string, Color> { ["quiet"] = C("Muted", "#8A8F98"), ["ink"] = C("Ink", "#18233A"), ["accent"] = accent, ["red"] = C("Overtime", "#E5484D") };
        var chars = new List<(char, Color)>();
        foreach (var (words, role) in UpNext()) foreach (var ch in words) chars.Add((ch, roles[role]));
        Ribbon = new string(chars.Select(c => c.Item1).ToArray());
        var cells = TickerCells; var text = new char[cells]; var colours = new Color[cells];
        // A ribbon that fits stands still; a longer one loops a cell at a time with a gap, as the account name does.
        var scroll = chars.Count > cells && !Motion.Reduced; if (scroll) chars.AddRange("    ".Select(c => (c, roles["ink"])));
        var offset = scroll ? (int)(elapsed / TickerStepMilliseconds) % chars.Count : 0;
        for (var i = 0; i < cells; i++) { var k = offset + i; var (ch, col) = scroll ? chars[k % chars.Count] : k < chars.Count ? chars[k] : (' ', roles["ink"]); text[i] = ch; colours[i] = col; }
        p.Text(new string(text), TickerLeft, y0 + 7, TickerHeight, i => colours[i]);
    }

    // ---------- budgets ----------
    private void Budgets(Painter p)
    {
        var ink = C("Ink", "#18233A"); var quiet = C("Muted", "#8A8F98"); var amber = StatusFace.BreakGlyphColour(this); var red = C("Overtime", "#E5484D");
        var rows = model.Accounts.Where(a => model.BudgetRatio(a) is not null).Take(3).ToList();
        if (rows.Count == 0) { p.Text("NO BUDGETS SET", Pad, 13, 13, _ => quiet); return; }
        var top = 4 + (3 - rows.Count) * 5.75;
        for (var r = 0; r < rows.Count; r++)
        {
            var a = rows[r]; var ratio = model.BudgetRatio(a)!.Value; var y = top + r * 11.5;
            var colour = ratio >= 1 ? red : ratio >= .8 ? amber : ink;
            p.Dot(new Point(13, y + 4.5), 2, AccountColour(a.Name), true);
            // The account's initials, as the pill's key used to carry them (Account one reads AO), since a first three letters were often the same for every account.
            var code = Drawable(PillWindow.Initials(a.Display)).Replace(" ", ""); p.Text(code.Length > 3 ? code[..3] : code.PadRight(3), 19, y, 9, _ => quiet);
            var cells = 30; var lit = (int)Math.Min(cells, Math.Round(ratio * cells)); var right = Bounds.Width - 41; var step = (right - 46) / cells;
            for (var c = 0; c < cells; c++) p.Box(new Rect(46 + c * step, y + 1, step - 2, 7), 1.2, c < lit ? colour : p.Ghost, c < lit);
            var pct = Math.Min(999, (int)Math.Round(ratio * 100)).ToString(); p.Text(pct.PadLeft(3), right + 3, y, 9, _ => colour);
        }
    }

    // ---------- the week ----------
    private void Week(Painter p)
    {
        var ink = C("Ink", "#18233A"); var quiet = C("Muted", "#8A8F98"); var accent = C("AccentLit", "#9A62C9");
        var start = model.WeekStart(model.TodayKey); var days = Enumerable.Range(0, 7).Select(i => start.AddDays(i)).ToList();
        var hours = days.Select(d => model.WorkingOn(d).TotalHours).ToList(); var schedule = days.Max(d => model.ScheduledOn(d).TotalHours);
        var top = Math.Max(1, Math.Max(schedule, hours.Max())); const int rows = 8; var per = top / rows; var column = (Bounds.Width - 2 * Pad) / 7;
        for (var i = 0; i < 7; i++)
        {
            var today = days[i] == model.TodayKey; var lit = (int)Math.Round(hours[i] / per); var cx = Pad + i * column + 2;
            for (var r = 0; r < rows; r++) p.Box(new Rect(cx, 25 - r * 3.1, column - 18, 2.2), 1, r < lit ? (today ? accent : ink) : p.Ghost, r < lit);
            p.Text(days[i].ToString("ddd")[..1].ToUpperInvariant(), cx + column - 15, 15, 11, _ => today ? accent : quiet);
        }
        if (schedule > 0)
        {
            var y = 25 - (schedule / per - 1) * 3.1 - .8;
            for (var x = Pad; x < Bounds.Width - Pad; x += 4.5) p.Line(new Point(x, y), new Point(Math.Min(x + 2, Bounds.Width - Pad), y), 1, quiet, true);
        }
    }

    // ---------- the bat ----------
    private static readonly string[][] BatFrames =
    {
        new[] { "....X.....X....", "...XXX...XXX...", "XXXXXXX.XXXXXXX", ".XXXXXXXXXXXXX.", "..XX..XXX..XX..", ".......X......." },
        new[] { "...............", "......X.X......", "...XXXXXXXXX...", "XXXXXXXXXXXXXXX", "X.XX..XXX..XX.X", ".......X......." },
    };
    private void Bat(Painter p)
    {
        var quiet = C("Muted", "#8A8F98"); var accent = C("AccentLit", "#9A62C9"); var t = elapsed / 1000; var w = Bounds.Width;
        // A crescent moon on the right and a few twinkling stars.
        var moon = new CombinedGeometry(GeometryCombineMode.Exclude, new EllipseGeometry(new Rect(w - 45, 7, 18, 18)), new EllipseGeometry(new Rect(w - 50, 4, 16, 16)));
        p.Shape(moon, quiet, true);
        var stars = new[] { (30.0, 8.0), (70, 20), (118, 6), (166, 24), (214, 10), (262, 22), (92, 28) };
        for (var i = 0; i < stars.Length; i++) { var on = Motion.Reduced || (int)(t * 1.3 + i) % 4 != 0; p.Box(new Rect(stars[i].Item1, stars[i].Item2, 1.8, 1.8), .9, on ? quiet : p.Ghost, on); }
        var frame = BatFrames[Motion.Reduced ? 0 : (int)(t * 5) % 2];
        var x = Motion.Reduced ? w / 2 - 16 : (t * 22) % (w + 70) - 40; var y = 9 + (Motion.Reduced ? 0 : Math.Sin(t * 2.2) * 4);
        for (var r = 0; r < frame.Length; r++) for (var c = 0; c < frame[r].Length; c++) if (frame[r][c] == 'X') p.Box(new Rect(x + c * 2.2, y + r * 2.2, 2, 2), .3, accent, true);
        var hour = model.Engine.Now.Hour; var hello = hour < 12 ? "GOOD MORNING" : hour < 18 ? "GOOD AFTERNOON" : "GOOD EVENING";
        p.Text(hello, Pad, 27, 9, _ => quiet, ghosts: false);
    }
}

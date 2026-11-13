using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Timer.Core;

namespace Timer.Desktop;

/// <summary>
/// The Week overview in the display look (the board "The week as a display", option A), behind the same setting as the timeline (<see cref="TimerModel.DisplayTimeline"/>):
/// the totals, the day chart as lit slices in unlit slots, and the accounts with cell budget bars, each a dark panel whose lit fills go through the honeycomb. The table view keeps its plain type.
/// The figures are <see cref="WeekOverview.Figures"/>, so they match the plain page exactly.
/// </summary>
public static class VfdWeek
{
    /// <summary>A dark display panel: the trough's glass and what is drawn in it (only the lit fills are meshed).</summary>
    public static Control Panel(Control content, double height, string name)
    {
        var grid = new Grid { Height = height, Children = { content } };
        var panel = new Border { Name = name, CornerRadius = new CornerRadius(14), ClipToBounds = true, Child = grid, Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative), GradientStops = { new GradientStop(Color.Parse("#0B0E13"), 0), new GradientStop(Color.Parse("#10141B"), 1) } } };
        return new ThemeVariantScope { RequestedThemeVariant = ThemeVariant.Dark, Child = panel };
    }

    /// <summary>A control that draws itself with a function, redrawn when asked.</summary>
    private sealed class Drawn(Action<DrawingContext, Size> draw) : Control
    {
        public override void Render(DrawingContext context) => draw(context, Bounds.Size);
    }

    private static Color Glass(TimerModel model, string key) => Color.Parse(model.GlassTheme.Colour(key));
    private static string Hm(TimerModel model, TimeSpan t) => LowerDisplay.Drawable(model.FormatShort(t)).Replace(" ", "");
    // A lit fill with its soft glow round it, and the unlit slot or cell beside it.
    // Only lit fills go through the honeycomb, with their bloom between the cells; the figures and words stay whole so they read cleanly.
    private static void Lit(DrawingContext c, Color colour, Rect r, double radius)
    {
        new PhosphorBloom(colour, 0, .78).Draw(c, new RectangleGeometry(r, radius, radius), true);
        using (Honeycomb.Meshed(c, r)) c.DrawRectangle(new SolidColorBrush(colour), null, r, radius, radius);
    }
    private static void Unlit(DrawingContext c, Color ink, Rect r, double radius) => c.DrawRectangle(new SolidColorBrush(VfdLook.Ghost(ink)), null, r, radius, radius);
    private static void Stripes(DrawingContext c, Color ink, Rect r)
    {
        c.DrawRectangle(new SolidColorBrush(SoftTint.WithAlpha(ink, .1)), null, r, 2, 2);
        using (c.PushClip(r)) { var pen = new Pen(new SolidColorBrush(SoftTint.WithAlpha(ink, .5)), 1.2); for (var k = -r.Height; k < r.Width; k += 4) c.DrawLine(pen, new Point(r.X + k, r.Bottom), new Point(r.X + k + r.Height, r.Y)); }
    }

    // ---- the totals
    public static Control Totals(TimerModel model, WeekFigures w)
    {
        var ink = Glass(model, "Ink"); var quiet = Glass(model, "Muted"); var accent = Color.Parse(ThemeEngine.LitOf(model.GlassTheme)); var amber = Color.Parse("#F5B63A");
        var drawn = new Drawn((c, size) =>
        {
            SegmentText.Draw(c, "WORKING TIME", 18, 14, 8, quiet);
            SegmentText.Draw(c, Hm(model, w.Working), 18, 30, 28, ink, ghosts: true);
            // The scheduled week as one bar of cells: allocated lit, unallocated striped, the rest unlit; time past the schedule fills the bar.
            double x0 = 210, x1 = Math.Max(x0 + 160, size.Width - 280); const int cells = 40; var cw = (x1 - x0) / cells;
            var span = Math.Max(w.Scheduled.TotalHours, w.Working.TotalHours); var per = span > 0 ? span / cells : 1;
            for (var i = 0; i < cells; i++)
            {
                var mid = (i + .5) * per; var r = new Rect(x0 + i * cw + .6, 32, cw - 1.2, 14);
                if (mid <= w.Allocated.TotalHours) Lit(c, ink, r, 2); else if (mid <= w.Working.TotalHours) Stripes(c, ink, r); else Unlit(c, ink, r, 2);
            }
            SegmentText.Draw(c, "SCHEDULED " + Hm(model, w.Scheduled), x0, 54, 8, quiet);
            var left = w.Scheduled - w.Working;
            var go = left >= TimeSpan.Zero ? (w.Current ? "TO GO " : "UNDER ") + Hm(model, left) : "OVER " + Hm(model, -left);
            SegmentText.Draw(c, go, x1 - SegmentText.WidthOf(go.Length, 8), 54, 8, accent);
            var x = size.Width - 250;
            foreach (var (label, value, colour) in new[] { ("ALLOCATED", w.Allocated, ink), ("UNALLOC", w.Unallocated, ink), ("BREAKS", w.Breaks, amber) })
            { SegmentText.Draw(c, label, x, 20, 7, quiet); SegmentText.Draw(c, Hm(model, value), x, 34, 14, colour, ghosts: true); x += 82; }
        });
        var panel = Panel(drawn, 84, "WeekTotals");
        ToolTip.SetTip(panel, $"Working time {model.Format(w.Working)} · scheduled {model.FormatShort(w.Scheduled)}");
        return panel;
    }

    // ---- time by day, as lit slices in unlit slots, with a readout row along the top for the day under the pointer
    public static Control Chart(TimerModel model, WeekFigures w)
    {
        const double height = 250, top = 48, below = 30, gutterLeft = 18, gutterRight = 96;
        var ink = Glass(model, "Ink"); var quiet = Glass(model, "Muted"); var accent = Color.Parse(ThemeEngine.LitOf(model.GlassTheme));
        var schedule = w.Days.Select(d => d.Scheduled).DefaultIfEmpty().Max();
        var topHours = Math.Max(Math.Max(schedule.TotalHours, w.Days.Select(d => d.Working.TotalHours).DefaultIfEmpty().Max()), 8) + .75;
        var hover = -1; var british = CultureInfo.GetCultureInfo("en-GB");
        var columns = new Grid { Margin = new Thickness(gutterLeft, 0, gutterRight, 0) };
        Drawn? drawn = null;
        double Y(double hours) => height - below - hours / topHours * (height - below - top);
        drawn = new Drawn((c, size) =>
        {
            var width = size.Width - gutterLeft - gutterRight; var n = Math.Max(1, w.Days.Count); var slot = width / n; var colW = Math.Min(96, slot * .7);
            // The readout: the day under the pointer and its split, or a hint while none is.
            if (hover >= 0 && hover < w.Days.Count)
            {
                var d = w.Days[hover]; var x = gutterLeft;
                x += SegmentText.Draw(c, d.Date.ToString("ddd", british).ToUpperInvariant(), x, 14, 11, accent) + 8;
                x += SegmentText.Draw(c, Hm(model, d.Working), x, 14, 11, ink) + 14;
                foreach (var (account, time) in d.Accounts)
                {
                    var colour = Color.Parse(model.ChartColour(account.Colour)); c.DrawEllipse(new SolidColorBrush(colour), null, new Point(x + 3, 19.5), 3, 3); x += 10;
                    var words = LowerDisplay.Drawable(PillWindow.Initials(account.Display)) + " " + Hm(model, time); if (x + SegmentText.WidthOf(words.Length, 9) > size.Width - 10) break;
                    x += SegmentText.Draw(c, words, x, 15, 9, ink) + 12;
                }
                if (d.Unallocated > TimeSpan.FromSeconds(30) && x + 80 < size.Width) { Stripes(c, ink, new Rect(x, 15, 8, 8)); SegmentText.Draw(c, "NONE " + Hm(model, d.Unallocated), x + 12, 15, 9, quiet); }
            }
            else SegmentText.Draw(c, "POINT AT A DAY FOR ITS SPLIT", gutterLeft, 15, 9, SoftTint.WithAlpha(quiet, .7));
            c.DrawRectangle(new SolidColorBrush(SoftTint.WithAlpha(quiet, .3)), null, new Rect(gutterLeft, 36, size.Width - 2 * gutterLeft, 1));
            // Hour lines every two hours, and the schedule as a lit dashed line with its words in the gutter.
            for (var h = 2; h < topHours - .5; h += 2) { c.DrawRectangle(new SolidColorBrush(VfdLook.Ghost(ink)), null, new Rect(gutterLeft, Y(h), width, 1)); if (Math.Abs(h - schedule.TotalHours) > .9) SegmentText.Draw(c, h.ToString("00"), size.Width - gutterRight + 16, Y(h) - 4, 8, quiet); }
            if (schedule > TimeSpan.Zero)
            {
                var y = Y(schedule.TotalHours); var pen = new Pen(new SolidColorBrush(accent), 1.4) { DashStyle = new DashStyle(new double[] { 3.5, 3 }, 0) };
                c.DrawLine(pen, new Point(gutterLeft, y), new Point(gutterLeft + width, y));
                SegmentText.Draw(c, "SCHED", size.Width - gutterRight + 16, y - 12, 8, accent); SegmentText.Draw(c, Hm(model, schedule), size.Width - gutterRight + 16, y + 2, 8, accent);
            }
            for (var i = 0; i < w.Days.Count; i++)
            {
                var d = w.Days[i]; var x = gutterLeft + i * slot + (slot - colW) / 2; var y = height - below;
                // An unlit slot as tall as the scheduled day, so every day reads as a place to fill.
                if (d.Scheduled > TimeSpan.Zero) Unlit(c, ink, new Rect(x, Y(d.Scheduled.TotalHours), colW, y - Y(d.Scheduled.TotalHours)), 6);
                var accountTotal = d.Accounts.Aggregate(TimeSpan.Zero, (s, p) => s + p.Time).TotalHours;
                var squeeze = accountTotal > 0 && accountTotal + d.Unallocated.TotalHours > d.Working.TotalHours && d.Working.TotalHours > 0 ? (d.Working.TotalHours - d.Unallocated.TotalHours) / accountTotal : 1;
                foreach (var (account, time) in d.Accounts)
                {
                    var hgt = time.TotalHours * squeeze / topHours * (height - below - top); if (hgt < .5) continue;
                    Lit(c, Color.Parse(model.ChartColour(account.Colour)), new Rect(x, y - hgt + 1, colW, Math.Max(1, hgt - 2)), 3); y -= hgt;
                }
                if (d.Unallocated > TimeSpan.FromSeconds(30)) { var uh = d.Unallocated.TotalHours / topHours * (height - below - top); Stripes(c, ink, new Rect(x, y - uh + 1, colW, Math.Max(1, uh - 2))); y -= uh; }
                // The day's total above its column and above the schedule line, whichever is higher; the day's name under it, today's in the accent and underlined.
                var label = d.HasTime ? Hm(model, d.Working) : d.Off ? "OFF" : "--:--"; var lw = SegmentText.WidthOf(label.Length, 11);
                SegmentText.Draw(c, label, x + (colW - lw) / 2, Math.Min(y, schedule > TimeSpan.Zero ? Y(schedule.TotalHours) : y) - 18, 11, d.HasTime ? ink : quiet, ghosts: true);
                var name = d.Date.ToString("ddd", british).ToUpperInvariant(); var nw = SegmentText.WidthOf(name.Length, 11);
                SegmentText.Draw(c, name, x + (colW - nw) / 2, height - below + 8, 11, d.IsToday ? accent : quiet);
                if (d.IsToday) c.DrawRectangle(new SolidColorBrush(accent), null, new Rect(x + (colW - nw) / 2, height - below + 22, nw, 1.6), .8, .8);
                if (d.Marked) c.DrawEllipse(new SolidColorBrush(quiet), null, new Point(x + colW - 4, height - below + 14), 2, 2);
                if (i == hover) c.DrawRectangle(null, new Pen(new SolidColorBrush(accent), 1.4), new Rect(x - 4, Y(Math.Max(d.Working.TotalHours, d.Scheduled.TotalHours)) - 22, colW + 8, height - below - Y(Math.Max(d.Working.TotalHours, d.Scheduled.TotalHours)) + 26), 8, 8);
            }
        });
        // A transparent button over each day: it takes the pointer for the readout and opens the day in the Timeline, as the plain chart's columns do.
        for (var i = 0; i < w.Days.Count; i++)
        {
            var d = w.Days[i]; var index = i;
            columns.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            var press = new Button { Name = "WeekDay_" + d.Date.ToString("yyyyMMdd"), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, Margin = new Thickness(0, 40, 0, 0) };
            press.Classes.Add("pillBare");
            Avalonia.Automation.AutomationProperties.SetName(press, $"{d.Date.ToString("dddd d MMMM", british)}, {model.Format(d.Working)} working. Open in the Timeline");
            press.Click += (_, _) => model.OpenDayInTimeline(d.Date);
            press.PointerEntered += (_, _) => { hover = index; drawn.InvalidateVisual(); };
            press.PointerExited += (_, _) => { if (hover == index) { hover = -1; drawn.InvalidateVisual(); } };
            Grid.SetColumn(press, i); columns.Children.Add(press);
        }
        var holder = new Grid { Name = "WeekChart_", Children = { drawn, columns } };
        return Panel(holder, height, "WeekVfdChart");
    }

    // ---- by account, with the budget as a bar of cells
    public static Control Accounts(TimerModel model, WeekFigures w, WeekTable grid)
    {
        var ink = Glass(model, "Ink"); var quiet = Glass(model, "Muted"); var amber = Color.Parse("#F5B63A"); var red = Glass(model, "Overtime");
        var accounts = model.ReviewAccounts.Where(a => !BreakAccount.Is(a.Name)).ToList();
        var rows = accounts.Select(a =>
        {
            var week = Enumerable.Range(0, 7).Aggregate(TimeSpan.Zero, (sum, i) => sum + model.OnDay(a, w.Start.AddDays(i)));
            var budget = a.Budget is not null ? grid.Budgets.FirstOrDefault(b => b.Account == a.Display) : null;
            return (Account: a, Week: week, Budget: budget);
        }).ToList();
        var height = 14 + rows.Count * 26;
        var drawn = new Drawn((c, size) =>
        {
            for (var i = 0; i < rows.Count; i++)
            {
                var (account, week, budget) = rows[i]; var y = 10 + i * 26;
                c.DrawEllipse(new SolidColorBrush(Color.Parse(model.ChartColour(account.Colour))), null, new Point(22, y + 6), 4, 4);
                var name = LowerDisplay.Drawable(account.Display); var room = (int)((200 - 36) / SegmentText.CellWidth(12)); SegmentText.Draw(c, name.Length > room ? name[..room] : name, 36, y, 12, ink);
                SegmentText.Draw(c, Hm(model, week), 214, y, 12, ink, ghosts: true);
                if (budget is null) { SegmentText.Draw(c, "NO BUDGET", 300, y + 2, 9, quiet); continue; }
                var over = budget.Fraction >= 1; var warn = budget.Fraction >= .8 && !over; var colour = over ? red : warn ? amber : ink;
                double x0 = 300, x1 = Math.Max(x0 + 120, size.Width - 230); const int cells = 40; var cw = (x1 - x0) / cells; var lit = (int)Math.Min(cells, Math.Round(budget.Fraction * cells));
                for (var k = 0; k < cells; k++) { var r = new Rect(x0 + k * cw, y + 2, cw - 2, 9); if (k < lit) Lit(c, colour, r, 1.5); else Unlit(c, ink, r, 1.5); }
                var period = model.BudgetPeriodNameAt(account, model.BudgetReference(w.Start));
                var caption = LowerDisplay.Drawable($"{budget.Used} OF {budget.Budget}" + (period.Contains("week", StringComparison.OrdinalIgnoreCase) ? "" : " " + period.Split(' ').Last()));
                SegmentText.Draw(c, caption, x1 + 14, y + 1, 10, over || warn ? colour : quiet);
            }
        });
        var panel = Panel(drawn, height, "WeekVfdAccounts");
        var scroll = new ScrollViewer { Content = panel, AllowAutoHide = false, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        return scroll;
    }
}

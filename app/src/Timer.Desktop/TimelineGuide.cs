using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;

namespace Timer.Desktop;

/// <summary>
/// What the timeline adds for notes that have a reminder on the day it shows: a pin hung from the ruler at each reminder's time (red once it has passed and the note is not
/// ticked off, the accent while it is coming; notes within a pin's width of each other share one pin that carries their count, and an important note's pin carries a !), and a
/// dashed guide line that follows the pointer along the day so it is clear what is being pointed at. The guide has a hollow dot on the ruler and a time chip over the hour labels.
/// <para>One thing speaks at a time, by where the pointer is. Over the ruler and the pins the guide snaps to a pin that is within a few pixels, which swells, and the notes'
/// words appear beside the line. Down among the blocks and meetings it rings what the line crosses and leaves the tooltips to the block under the pointer, so a card for a
/// note and a tooltip for a block never compete.</para>
/// It is dashed and hollow where the now line is solid and filled, so the two are never mistaken for each other. Pressing a pin opens its first note for editing in the Notes
/// card. A note without a reminder has no time to sit at, so it has no pin.
/// </summary>
public static class TimelineGuide
{
    /// <summary>The room the pins take under the ruler: the meeting row and the blocks sit this much lower on a day that has one.</summary>
    public const double PinRoom = 10;
    /// <summary>How near the pointer must be to a pin, in pixels, for the guide to snap to it.</summary>
    public const double SnapPixels = 8;
    /// <summary>Notes whose reminders are this near each other, in pixels, share one pin.</summary>
    public const double ClusterPixels = 14;
    /// <summary>A pin's dot, with its ring, at rest and while the guide is on it; a pin that carries a count or a ! is a little larger.</summary>
    public const double RestDot = 15, ActiveDot = 17, BadgeDot = 19, BadgeActiveDot = 21;
    /// <summary>How many notes the guide's card lists for a shared pin before it says how many more there are.</summary>
    public const int CardLines = 4;

    private sealed class Pin(IReadOnlyList<DayNote> notes, double x, Canvas visual, Ellipse dot, Ellipse halo, Border stem, Border? badge, double rest, double active)
    {
        public IReadOnlyList<DayNote> Notes { get; } = notes; public DayNote First => Notes[0];
        public double X { get; } = x; public Canvas Visual { get; } = visual; public Ellipse Dot { get; } = dot; public Ellipse Halo { get; } = halo; public Border Stem { get; } = stem; public Border? Badge { get; } = badge;
        public double Rest { get; } = rest; public double Active { get; } = active;
    }

    private static void Paint(Control control, AvaloniaProperty property, string key) => control.Bind(property, control.GetResourceObservable(key));

    /// <summary>
    /// Hangs the pins and the guide on the timeline's canvas. <paramref name="pinned"/> are the day's notes with a reminder, in time order. <paramref name="lanesTop"/> is where
    /// the meeting row or the blocks begin: above it the pointer is on the ruler and the pins, below it on the day's own blocks.
    /// </summary>
    public static void Attach(Canvas canvas, TimerModel model, DateTime day, double pixelsPerHour, double gutter, double axisY, double lanesTop, IReadOnlyList<DayNote> pinned)
    {
        double X(DateTime time) => gutter + (time - day).TotalHours * pixelsPerHour;
        var pins = new List<Pin>();
        // The display look: reminders are lit tags above the line among the hour digits, and the reminder pointed at reads along the ruler (see VfdReadout).
        var vfd = model.DisplayTimeline; Action<IReadOnlyList<DayNote>, double>? VfdList = null;
        bool Late(Pin pin) => pin.Notes.Any(model.Notes.IsLate);
        string ColourKey(Pin pin) => Late(pin) ? "Overtime" : "AccentLit";
        // Notes within a pin's width of each other are one pin, so none is hidden behind another.
        var groups = new List<List<DayNote>>();
        foreach (var note in pinned)
        {
            var x = X(NotesModel.ReminderOf(note)!.Value);
            if (groups.Count > 0 && x - X(NotesModel.ReminderOf(groups[^1][0])!.Value) <= ClusterPixels) groups[^1].Add(note); else groups.Add(new List<DayNote> { note });
        }
        foreach (var group in groups)
        {
            var first = group[0]; var due = NotesModel.ReminderOf(first)!.Value; var x = X(due);
            var important = group.Any(NotesModel.IsImportant);
            var mark = group.Count > 1 ? group.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) : important ? "!" : null;
            var rest = mark is null ? RestDot : BadgeDot; var grown = mark is null ? ActiveDot : BadgeActiveDot; var haloSize = rest + 7;
            var visual = new Canvas { Name = "TimelinePin_" + first.Id, Width = 20, Height = 28, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), ZIndex = 4 };
            var stem = new Border { Width = 1.5, Height = 10, Opacity = .7, IsHitTestVisible = false }; Canvas.SetLeft(stem, 9.25);
            var halo = new Ellipse { Width = haloSize, Height = haloSize, StrokeThickness = 3, Opacity = 0, IsHitTestVisible = false }; Canvas.SetLeft(halo, 10 - haloSize / 2); Canvas.SetTop(halo, 11.5 - haloSize / 2);
            // The stroke (the colour of the card) is inside the size, so the dot's colour is 10 across and a ring of the card's colour keeps it clear of the lines behind.
            var dot = new Ellipse { Width = rest, Height = rest, StrokeThickness = 2.5, IsHitTestVisible = false }; Canvas.SetLeft(dot, 10 - rest / 2); Canvas.SetTop(dot, 11.5 - rest / 2);
            Paint(dot, Shape.StrokeProperty, "Panel");
            visual.Children.Add(stem); visual.Children.Add(halo); visual.Children.Add(dot);
            Border? badge = null;
            if (mark is not null)
            {
                badge = new Border { Name = "TimelinePinMark_" + first.Id, Width = rest, Height = rest, IsHitTestVisible = false, Child = new TextBlock { Text = mark, FontSize = 10.5, FontWeight = FontWeight.Bold, Foreground = Brushes.White, TextAlignment = TextAlignment.Center, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center } };
                Canvas.SetLeft(badge, 10 - rest / 2); Canvas.SetTop(badge, 11.5 - rest / 2); visual.Children.Add(badge);
            }
            Canvas.SetLeft(visual, x - 10); Canvas.SetTop(visual, axisY);
            if (vfd)
            {
                // A lit tag in the hour row on a short stem down to the line, its count (or a ! for one reminder) in dark ink.
                visual.Children.Clear(); visual.Height = axisY; Canvas.SetTop(visual, 0);
                stem.Height = axisY - 19; Canvas.SetTop(stem, 19); visual.Children.Add(stem);
                var tag = new Border { Name = "TimelineTag_" + first.Id, Width = 14, Height = 14, CornerRadius = new CornerRadius(3.5), IsHitTestVisible = false, Tag = "VfdTag" };
                Canvas.SetLeft(tag, 3); Canvas.SetTop(tag, 5); visual.Children.Add(tag);
                Control inked = group.Count > 1
                    ? new SegmentText(Math.Min(group.Count, 99).ToString(System.Globalization.CultureInfo.InvariantCulture), 8, VfdLook.BlockInk, weight: 1.15)
                    : new Canvas { Width = 2, Height = 9, Children = { new Border { Width = 2, Height = 6, CornerRadius = new CornerRadius(1), Background = new SolidColorBrush(VfdLook.BlockInk) }, new Border { Width = 2, Height = 2, CornerRadius = new CornerRadius(1), Background = new SolidColorBrush(VfdLook.BlockInk), [Canvas.TopProperty] = 7.5 } } };
                inked.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center; inked.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center; tag.Child = inked;
                Paint(tag, Border.BackgroundProperty, group.Any(model.Notes.IsLate) ? "Overtime" : "AccentLit");
                Paint(tag, Border.BorderBrushProperty, group.Any(model.Notes.IsLate) ? "Overtime" : "AccentLit"); tag.BorderThickness = new Thickness(1.2);
            }
            var when = NoteDates.When(due, model.Engine.Now);
            var late = group.Any(model.Notes.IsLate);
            var words = group.Count == 1 ? model.Notes.Text(first) : group.Count + " reminders: " + string.Join(", ", group.Select(model.Notes.Text));
            Avalonia.Automation.AutomationProperties.SetName(visual, (group.Count == 1 ? "Reminder " : "") + words + ", " + when + (important ? ", important" : "") + (late ? ", late" : ""));
            var id = first.Id;
            var members = group;
            visual.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(visual).Properties.IsLeftButtonPressed) return;
                // On the display a tag holding several reminders lists them; one opens straight in Details, as it always has.
                if (vfd && members.Count > 1) VfdList?.Invoke(members, x); else model.Notes.RequestEditNote(id);
                e.Handled = true;
            };
            canvas.Children.Add(visual);
            pins.Add(new Pin(group, x, visual, dot, halo, stem, badge, rest, grown));
        }
        void Recolour(Pin pin)
        {
            var key = ColourKey(pin);
            Paint(pin.Dot, Shape.FillProperty, key); Paint(pin.Halo, Shape.StrokeProperty, key); Paint(pin.Stem, Border.BackgroundProperty, key);
            if (pin.Visual.Children.OfType<Border>().FirstOrDefault(b => Equals(b.Tag, "VfdTag")) is { } tag) { Paint(tag, Border.BackgroundProperty, key); Paint(tag, Border.BorderBrushProperty, key); }
            var due = NotesModel.ReminderOf(pin.First)!.Value;
            var words = pin.Notes.Count == 1 ? model.Notes.Text(pin.First) : pin.Notes.Count + " reminders: " + string.Join(", ", pin.Notes.Select(model.Notes.Text));
            Avalonia.Automation.AutomationProperties.SetName(pin.Visual, (pin.Notes.Count == 1 ? "Reminder " : "") + words + ", " + NoteDates.When(due, model.Engine.Now)
                + (pin.Notes.Any(NotesModel.IsImportant) ? ", important" : "") + (Late(pin) ? ", late" : ""));
        }
        foreach (var pin in pins) Recolour(pin);
        // A pin turns red when its time passes, without the whole timeline being drawn again.
        if (pins.Count > 0 && model.DayOf(day) == model.TodayKey)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            timer.Tick += (_, _) => { foreach (var pin in pins) Recolour(pin); };
            _ = new ViewActivity(canvas, active => { if (active) { foreach (var pin in pins) Recolour(pin); timer.Start(); } else timer.Stop(); });
        }
        if (vfd && pins.Any(Late) && !Motion.Reduced)
        {
            // A late tag blinks: lit, then only its outline, twice a second.
            var blink = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) }; var lit = true;
            blink.Tick += (_, _) =>
            {
                lit = !lit;
                foreach (var pin in pins.Where(Late)) if (pin.Visual.Children.OfType<Border>().FirstOrDefault(b => Equals(b.Tag, "VfdTag")) is { } tag)
                { if (lit) Paint(tag, Border.BackgroundProperty, "Overtime"); else tag.Background = Brushes.Transparent; if (tag.Child is { } ink) ink.IsVisible = lit; }
            };
            _ = new ViewActivity(canvas, active => { if (active) blink.Start(); else blink.Stop(); });
        }

        // ---- the guide
        var line = new Line { Name = "TimelineGuideLine", StartPoint = new Point(1, 0), EndPoint = new Point(1, canvas.Height - axisY), StrokeThickness = 2, StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 2.5, 2 }, Opacity = .75, IsHitTestVisible = false, IsVisible = false, ZIndex = 9 };
        Paint(line, Shape.StrokeProperty, "AccentLit");
        var guideDot = new Ellipse { Name = "TimelineGuideDot", Width = 9, Height = 9, StrokeThickness = 2, IsHitTestVisible = false, IsVisible = false, ZIndex = 9 };
        Paint(guideDot, Shape.StrokeProperty, "AccentLit"); Paint(guideDot, Shape.FillProperty, "Panel");
        var chipText = new TextBlock { FontSize = 12, FontWeight = FontWeight.Bold, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        Paint(chipText, TextBlock.ForegroundProperty, "AccentText");
        var chip = new Border { Name = "TimelineGuideChip", Width = 52, Height = 21, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1), Child = chipText, IsHitTestVisible = false, IsVisible = false, ZIndex = 9 };
        var noteCard = new Border { Name = "TimelineGuideNote", CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(11, 9), BoxShadow = HangingCard.Shadow, IsHitTestVisible = false, IsVisible = false, ZIndex = 12 };
        Paint(noteCard, Border.BackgroundProperty, "Panel"); Paint(noteCard, Border.BorderBrushProperty, "Line");
        canvas.Children.Add(line); canvas.Children.Add(guideDot); canvas.Children.Add(chip); canvas.Children.Add(noteCard);
        var readout = vfd ? new VfdReadout(canvas, model, axisY) : null;
        if (vfd) VfdList = (notes, x) => readout!.ShowList(notes, x);
        // What the line crosses among the blocks is ringed by the block's own shadow, the way a selected block is, so the ring is always the same width all round (a ring drawn as a
        // separate shape was sometimes a pixel or two off). A selected or recording block already has its own ring and is left alone.
        var ringed = new Dictionary<Control, (BoxShadows Shadow, int Z)>();
        var accent = Color.Parse(ThemeEngine.LitOf(model.GlassTheme)); var ringColour = Color.FromArgb(0x99, accent.R, accent.G, accent.B);
        void Unring(Func<Control, bool>? keep = null)
        {
            foreach (var (card, before) in ringed.ToList())
            {
                if (keep is not null && keep(card)) continue;
                if (card is Border b) b.BoxShadow = before.Shadow;
                card.ZIndex = before.Z; card.Classes.Remove("guideOver"); ringed.Remove(card);
            }
        }
        Pin? active = null;
        void Hide()
        {
            line.IsVisible = guideDot.IsVisible = chip.IsVisible = noteCard.IsVisible = false; readout?.Show(null, 0);
            Unring(); Activate(null);
        }
        void Activate(Pin? pin)
        {
            if (ReferenceEquals(active, pin)) return;
            if (active is { } old) Resize(old, old.Rest, 0);
            active = pin;
            if (pin is not null) Resize(pin, pin.Active, .3);
        }
        void Resize(Pin pin, double size, double halo)
        {
            pin.Dot.Width = pin.Dot.Height = size; Canvas.SetLeft(pin.Dot, 10 - size / 2); Canvas.SetTop(pin.Dot, 11.5 - size / 2); pin.Halo.Opacity = halo;
            if (pin.Badge is { } badge) { badge.Width = badge.Height = size; Canvas.SetLeft(badge, 10 - size / 2); Canvas.SetTop(badge, 11.5 - size / 2); }
        }
        bool Names(Control c, string prefix) => c.Name is { } n && n.StartsWith(prefix, StringComparison.Ordinal);
        void Update(double pointerX, double pointerY)
        {
            var span = 24 * pixelsPerHour;
            if (pointerX < gutter || pointerX > gutter + span) { Hide(); return; }
            // On the ruler and the pins the guide snaps and speaks for the notes; among the blocks it rings them and keeps quiet, so the block's own tooltip is the only card.
            var onRuler = pointerY < lanesTop;
            var near = onRuler ? pins.OrderBy(p => Math.Abs(p.X - pointerX)).FirstOrDefault(p => Math.Abs(p.X - pointerX) <= SnapPixels) : null;
            var x = near?.X ?? pointerX;
            var time = near is not null ? NotesModel.ReminderOf(near.First)!.Value : day.AddMinutes(Math.Round((x - gutter) / pixelsPerHour * 60));
            Activate(near);
            line.IsVisible = guideDot.IsVisible = chip.IsVisible = true;
            // Snapped to a pin, the line starts under the pin so it does not run through the dot or the count in it.
            var skip = near is null ? 0 : 11.5 + near.Active / 2 + 4;
            // Follow the full display, including the space below the blocks and the now chip.
            line.EndPoint = new Point(1, Math.Max(0, canvas.Bounds.Height - axisY - skip));
            Canvas.SetLeft(line, x - 1); Canvas.SetTop(line, axisY + skip);
            Canvas.SetLeft(guideDot, x - 4.5); Canvas.SetTop(guideDot, axisY - 5);
            chipText.Text = time.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            Canvas.SetLeft(chip, x - chip.Width / 2); Canvas.SetTop(chip, 1);
            // Quiet while free, the accent wash while it is on a note.
            if (near is null) { Paint(chip, Border.BackgroundProperty, "Panel"); Paint(chip, Border.BorderBrushProperty, "Line"); }
            else { Paint(chip, Border.BackgroundProperty, "Selected"); Paint(chip, Border.BorderBrushProperty, "AccentLit"); }
            var under = new List<Control>();
            if (!onRuler)
                foreach (var child in canvas.Children.OfType<Control>().ToList())
                {
                    var isBlock = Names(child, "TimelineEntry") || Names(child, "TimelineStack") || Names(child, "TimelineGroup") || Names(child, "TimelineRecording") || Names(child, "TimelineMeeting");
                    if (!isBlock || child.Bounds.Width <= 0) continue;
                    var left = Canvas.GetLeft(child); if (x >= left && x <= left + child.Bounds.Width) under.Add(child);
                }
            Unring(under.Contains);
            foreach (var card in under)
            {
                if (ringed.ContainsKey(card) || card is not Border b || b.Classes.Contains("mergeSelected") || b.Classes.Contains("recording")) continue;
                ringed[card] = (b.BoxShadow, b.ZIndex); b.BoxShadow = new BoxShadows(new BoxShadow { Spread = 2, Color = ringColour }); b.ZIndex = 3; b.Classes.Add("guideOver");
            }
            // The notes' words beside the line, when it is on a pin (and tooltips are on).
            if (readout is not null) { readout.Show(near?.Notes, x); chip.IsVisible = near is null; noteCard.IsVisible = false; return; }
            noteCard.IsVisible = near is not null && model.Tooltips;
            if (near is not null) ShowNote(near, x);
        }
        void ShowNote(Pin pin, double x)
        {
            var due = NotesModel.ReminderOf(pin.First)!.Value; var late = pin.Notes.Any(model.Notes.IsLate);
            var rows = new StackPanel { Spacing = 3 };
            Control NoteLine(DayNote note)
            {
                var words = new TextBlock { Text = model.Notes.Text(note), FontSize = 12.5, FontWeight = FontWeight.SemiBold, MaxWidth = 240, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                var head = Ui.Horizontal(NoteParts.Dot(model, model.Notes.AccountOf(note)), words); head.Spacing = 7;
                if (NotesModel.IsImportant(note)) head.Children.Add(new Avalonia.Controls.Viewbox { Width = 11, Height = 11, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 1), Child = new IconGlyph(IconKind.StarFilled) }); // Scaled to sit within the words' line; the glyph draws at its own size and ignores a Width.
                return head;
            }
            var minutes = (int)Math.Max(0, (model.Engine.Now - due).TotalMinutes);
            var lateWords = minutes < 60 ? $"{minutes} minutes late" : $"{minutes / 60}h {minutes % 60:00}m late";
            if (pin.Notes.Count == 1)
            {
                rows.Children.Add(NoteLine(pin.First));
                var detail = new TextBlock { Text = NoteDates.When(due, model.Engine.Now) + " · " + (late ? lateWords : "reminder"), FontSize = 11.5 }; detail.Classes.Add(late ? "overtime" : "muted"); rows.Children.Add(detail);
            }
            else
            {
                var detail = new TextBlock { Text = NoteDates.When(due, model.Engine.Now) + " · " + pin.Notes.Count + " reminders" + (late ? " · " + lateWords : ""), FontSize = 11.5 }; detail.Classes.Add(late ? "overtime" : "muted"); rows.Children.Add(detail);
                foreach (var note in pin.Notes.Take(CardLines)) rows.Children.Add(NoteLine(note));
                if (pin.Notes.Count > CardLines) { var more = new TextBlock { Text = $"+{pin.Notes.Count - CardLines} more", FontSize = 11.5 }; more.Classes.Add("muted"); rows.Children.Add(more); }
            }
            noteCard.Child = rows;
            noteCard.Measure(Size.Infinity); var width = noteCard.DesiredSize.Width;
            var left = x + 14; if (left + width > canvas.Width) left = x - 14 - width;
            Canvas.SetLeft(noteCard, Math.Max(0, left)); Canvas.SetTop(noteCard, axisY + 34 + PinRoom);
        }
        canvas.PointerMoved += (_, e) =>
        {
            var point = e.GetCurrentPoint(canvas);
            // A press (dragging a block, resizing, a menu) has the pointer's attention, so the guide steps aside.
            if (point.Properties.IsLeftButtonPressed || point.Properties.IsRightButtonPressed) { Hide(); return; }
            model.GuidePointer = point.Position; Update(point.Position.X, point.Position.Y);
        };
        // The timeline is drawn again whenever the model changes (a click that opens the meeting form switches the Notes card, which does it), and the pointer has not
        // moved, so the line would be gone until it did. The pointer's last place is kept on the model and the new canvas picks the line up from it as soon as it is shown.
        // Leaving the canvas forgets it, but not when the exit is only the old canvas being taken away.
        canvas.PointerExited += (_, _) => { if (canvas.GetVisualRoot() is not null) model.GuidePointer = null; Hide(); };
        canvas.AttachedToVisualTree += (_, _) =>
        {
            if (model.GuidePointer is not { } last) return;
            Dispatcher.UIThread.Post(() => { if (canvas.GetVisualRoot() is not null && model.GuidePointer is { } again) Update(again.X, again.Y); }, DispatcherPriority.Loaded);
        };
    }
}

/// <summary>
/// On the display look, the reminder the pointer is on reads along the ruler: the hour digits switch off and its time, words and how soon take their row, running away
/// from its tag on the left half of what shows and up to it on the right half. Several reminders at one tag take turns every <see cref="TurnSeconds"/>, with which of how
/// many before them and a track under that count, at most <see cref="TrackPieces"/> pieces lit in proportion. Pressing such a tag drops a list of them all.
/// </summary>
public sealed class VfdReadout
{
    public const double TurnSeconds = 2.5, Height = 11; public const int TrackPieces = 10;
    private readonly Canvas canvas; private readonly TimerModel model; private readonly double axisY;
    private readonly StackPanel row = new() { Name = "TimelineReadout", Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 7, IsHitTestVisible = false, ZIndex = 9 };
    private readonly Canvas track = new() { Name = "TimelineReadoutTrack", IsHitTestVisible = false, ZIndex = 9 };
    private Border? list;
    private IReadOnlyList<DayNote>? showing; private int turn; private double at;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(TurnSeconds) };
    /// <summary>Which reminder of the tag is showing, for tests.</summary>
    public int Turn => turn;
    public VfdReadout(Canvas canvas, TimerModel model, double axisY)
    {
        this.canvas = canvas; this.model = model; this.axisY = axisY;
        row.IsVisible = track.IsVisible = false; canvas.Children.Add(row); canvas.Children.Add(track);
        timer.Tick += (_, _) => { if (showing is { Count: > 1 } notes) { turn = (turn + 1) % notes.Count; Draw(); } };
        canvas.DetachedFromVisualTree += (_, _) => timer.Stop();
        // A press anywhere else on the timeline puts a list away.
        canvas.AddHandler(InputElement.PointerPressedEvent, (_, e) => { if (list is not null && e.Source is Visual v && !v.GetSelfAndVisualAncestors().Contains(list)) CloseList(); }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }
    private Color Key(string key) => Color.Parse(model.GlassTheme.Colour(key));
    // The other tags step back while one reads along the ruler, so its words never sit on top of them.
    private void OtherTags(bool dim) { foreach (var pin in canvas.Children.OfType<Canvas>().Where(c => c.Name?.StartsWith("TimelinePin_", StringComparison.Ordinal) == true)) pin.Opacity = dim && Math.Abs(Canvas.GetLeft(pin) + 10 - at) > 1 ? .15 : 1; }
    private void HourLabels(bool on) { foreach (var label in canvas.Children.OfType<Control>().Where(c => c.Name?.StartsWith("TimelineHour_", StringComparison.Ordinal) == true)) label.Opacity = on ? 1 : 0; }
    /// <summary>Shows the reminders at a tag (or puts the readout away with null).</summary>
    public void Show(IReadOnlyList<DayNote>? notes, double x)
    {
        if (notes is null) { if (showing is null) return; showing = null; timer.Stop(); row.IsVisible = track.IsVisible = false; HourLabels(true); OtherTags(false); return; }
        if (ReferenceEquals(notes, showing)) return;
        showing = notes; turn = 0; at = x; HourLabels(false); OtherTags(true); Draw();
        timer.Stop(); if (notes.Count > 1 && !Motion.Reduced) timer.Start();
    }
    private void Draw()
    {
        if (showing is not { } notes) return;
        var note = notes[turn]; var due = NotesModel.ReminderOf(note)!.Value; var now = model.Engine.Now;
        var accent = Color.Parse(ThemeEngine.LitOf(model.GlassTheme)); var ink = Key("Ink"); var quiet = Key("Muted");
        row.Children.Clear();
        SegmentText? count = null;
        if (notes.Count > 1) { count = new SegmentText($"{turn + 1}/{notes.Count}", Height, quiet) { Name = "TimelineReadoutCount" }; row.Children.Add(count); }
        row.Children.Add(new SegmentText(due.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture), Height, model.Notes.IsLate(note) ? Key("Overtime") : accent));
        var wordsText = new SegmentText(LowerDisplay.Drawable(model.Notes.Text(note)), Height, ink, ghosts: true) { Name = "TimelineReadoutWords" }; row.Children.Add(wordsText);
        if (notes.Count == 1)
        {
            var minutes = (int)Math.Round((due - now).TotalMinutes);
            var soon = minutes >= 0 ? (minutes < 60 ? $"- IN {minutes} MIN" : $"- IN {minutes / 60}H {minutes % 60:00}") : (-minutes < 60 ? $"- {-minutes} MIN LATE" : $"- {-minutes / 60}H {-minutes % 60:00} LATE");
            row.Children.Add(new SegmentText(soon, Height, quiet));
        }
        row.IsVisible = true; row.Measure(Size.Infinity); var width = row.DesiredSize.Width; // A hidden panel measures to nothing.
        // Away from the tag on whichever side has more room in what shows, never over the tag and never past either edge: words that do not fit are cut to the room.
        var (left, right) = Visible();
        var roomRight = right - 4 - (at + 14); var roomLeft = at - 14 - (left + 4); var toRight = roomRight >= roomLeft; var room = Math.Max(0, toRight ? roomRight : roomLeft);
        if (width > room)
        {
            var cut = (int)Math.Floor((width - room) / SegmentText.CellWidth(Height)) + 1; var words = wordsText.Text;
            wordsText.Text = words.Length > cut ? words[..(words.Length - cut)] : "";
            row.Measure(Size.Infinity); width = row.DesiredSize.Width;
        }
        var x = toRight ? at + 14 : at - 14 - width;
        Canvas.SetLeft(row, x); Canvas.SetTop(row, 5); row.IsVisible = true;
        // The track sits exactly under the count: one piece a reminder up to ten, then ten pieces each standing for a share, lit up to the one showing.
        track.Children.Clear(); track.IsVisible = count is not null;
        if (count is not null)
        {
            var pieces = Math.Min(TrackPieces, notes.Count); var lit = (int)Math.Ceiling((turn + 1) / (double)notes.Count * pieces);
            var trackWidth = Math.Max(SegmentText.WidthOf(count.Text.Length, Height) - 4, 24); var gap = pieces > 6 ? 1.0 : 2.0; var piece = (trackWidth - gap * (pieces - 1)) / pieces;
            for (var i = 0; i < pieces; i++)
            {
                var cell = new Border { Name = "TimelineReadoutPiece_" + i, Width = piece, Height = 2.5, CornerRadius = new CornerRadius(1.2), Background = new SolidColorBrush(i < lit ? accent : SoftTint.WithAlpha(quiet, .35)) };
                Canvas.SetLeft(cell, i * (piece + gap)); track.Children.Add(cell);
            }
            Canvas.SetLeft(track, x + 1); Canvas.SetTop(track, 5 + Height + 3);
        }
    }
    private (double Left, double Right) Visible()
    {
        if (canvas.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault() is { Viewport.Width: > 0 } scroll) return (scroll.Offset.X, scroll.Offset.X + scroll.Viewport.Width);
        return (0, canvas.Width);
    }
    /// <summary>The pieces of the track, lit or not, for tests.</summary>
    public IReadOnlyList<bool> Pieces(Color lit) => track.Children.OfType<Border>().Select(b => b.Background is SolidColorBrush s && s.Color == lit).ToList();

    /// <summary>Drops a dashed list of every reminder at a tag under it, numbered; pressing one opens it in Details.</summary>
    public void ShowList(IReadOnlyList<DayNote> notes, double x)
    {
        CloseList();
        var ink = Key("Ink"); var quiet = Key("Muted"); var accent = Color.Parse(ThemeEngine.LitOf(model.GlassTheme));
        var perColumn = (int)Math.Ceiling(notes.Count / 2.0); var columns = notes.Count > 4 ? 2 : 1; if (columns == 1) perColumn = notes.Count;
        var grid = new Grid { Margin = new Thickness(10, 8), ColumnSpacing = 18, RowSpacing = 4 };
        for (var c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (var r = 0; r < perColumn; r++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var i = 0; i < notes.Count; i++)
        {
            var note = notes[i]; var id = note.Id;
            var line = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
            line.Children.Add(new SegmentText((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(2), 8, quiet));
            var words = LowerDisplay.Drawable(model.Notes.Text(note)); line.Children.Add(new SegmentText(words.Length > 26 ? words[..26] : words, 8, ink));
            var press = new Border { Name = "TimelineListRow_" + i, Child = line, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Padding = new Thickness(2) };
            press.PointerPressed += (_, e) => { if (e.GetCurrentPoint(press).Properties.IsLeftButtonPressed) { CloseList(); model.Notes.RequestEditNote(id); e.Handled = true; } };
            Grid.SetColumn(press, i / perColumn); Grid.SetRow(press, i % perColumn); grid.Children.Add(press);
        }
        // Dashed and filled dark, so it is plainly a list and never a block or a meeting.
        var outline = new Rectangle { Stroke = new SolidColorBrush(accent), StrokeThickness = 1.2, StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 3.5, 2.5 }, RadiusX = 6, RadiusY = 6, IsHitTestVisible = false };
        var body = new Grid { Children = { outline, grid } };
        list = new Border { Name = "TimelineReminderList", Child = body, Background = new SolidColorBrush(Color.Parse("#0B0E13")), CornerRadius = new CornerRadius(6), ZIndex = 39 };
        list.Measure(Size.Infinity);
        var (left, right) = Visible(); var lx = Math.Clamp(x - 8, left + 4, Math.Max(left + 4, right - 4 - list.DesiredSize.Width));
        Canvas.SetLeft(list, lx); Canvas.SetTop(list, axisY + 8); canvas.Children.Add(list);
    }
    public bool ListOpen => list is not null;
    public void CloseList() { if (list is null) return; canvas.Children.Remove(list); list = null; }
}

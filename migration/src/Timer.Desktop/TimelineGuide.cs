using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
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
    public static void Attach(Canvas canvas, TimerModel model, DateTime day, double pixelsPerHour, double gutter, double axisY, double bandBottom, double lanesTop, IReadOnlyList<DayNote> pinned)
    {
        double X(DateTime time) => gutter + (time - day).TotalHours * pixelsPerHour;
        var pins = new List<Pin>();
        bool Late(Pin pin) => pin.Notes.Any(model.Notes.IsLate);
        string ColourKey(Pin pin) => Late(pin) ? "Overtime" : "Accent";
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
            var halo = new Ellipse { Width = haloSize, Height = haloSize, StrokeThickness = 3, Opacity = 0, IsHitTestVisible = false }; Canvas.SetLeft(halo, 10 - haloSize / 2); Canvas.SetTop(halo, 13 - haloSize / 2);
            // The stroke (the colour of the card) is inside the size, so the dot's colour is 10 across and a ring of the card's colour keeps it clear of the lines behind.
            var dot = new Ellipse { Width = rest, Height = rest, StrokeThickness = 2.5, IsHitTestVisible = false }; Canvas.SetLeft(dot, 10 - rest / 2); Canvas.SetTop(dot, 11.5 - rest / 2);
            Paint(dot, Shape.StrokeProperty, "Panel");
            visual.Children.Add(stem); visual.Children.Add(halo); visual.Children.Add(dot);
            Border? badge = null;
            if (mark is not null)
            {
                badge = new Border { Name = "TimelinePinMark_" + first.Id, Width = rest, Height = rest, IsHitTestVisible = false, Child = new TextBlock { Text = mark, FontSize = 10.5, FontWeight = FontWeight.Bold, Foreground = Brushes.White, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center } };
                Canvas.SetLeft(badge, 10 - rest / 2); Canvas.SetTop(badge, 11.5 - rest / 2); visual.Children.Add(badge);
            }
            Canvas.SetLeft(visual, x - 10); Canvas.SetTop(visual, axisY);
            var when = NoteDates.When(due, model.Engine.Now);
            var late = group.Any(model.Notes.IsLate);
            var words = group.Count == 1 ? model.Notes.Text(first) : group.Count + " reminders: " + string.Join(", ", group.Select(model.Notes.Text));
            Avalonia.Automation.AutomationProperties.SetName(visual, (group.Count == 1 ? "Reminder " : "") + words + ", " + when + (important ? ", important" : "") + (late ? ", late" : ""));
            var id = first.Id;
            visual.PointerPressed += (_, e) => { if (e.GetCurrentPoint(visual).Properties.IsLeftButtonPressed) { model.Notes.RequestEditNote(id); e.Handled = true; } };
            canvas.Children.Add(visual);
            pins.Add(new Pin(group, x, visual, dot, halo, stem, badge, rest, grown));
        }
        void Recolour(Pin pin)
        {
            var key = ColourKey(pin);
            Paint(pin.Dot, Shape.FillProperty, key); Paint(pin.Halo, Shape.StrokeProperty, key); Paint(pin.Stem, Border.BackgroundProperty, key);
        }
        foreach (var pin in pins) Recolour(pin);
        // A pin turns red when its time passes, without the whole timeline being drawn again.
        if (pins.Count > 0 && day == model.Engine.Now.Date)
        {
            DispatcherTimer? timer = null;
            canvas.AttachedToVisualTree += (_, _) => { timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) }; timer.Tick += (_, _) => { foreach (var pin in pins) Recolour(pin); }; timer.Start(); };
            canvas.DetachedFromVisualTree += (_, _) => { timer?.Stop(); timer = null; };
        }

        // ---- the guide
        var line = new Line { Name = "TimelineGuideLine", StartPoint = new Point(1, 0), EndPoint = new Point(1, bandBottom + 8 - axisY), StrokeThickness = 2, StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 2.5, 2 }, Opacity = .75, IsHitTestVisible = false, IsVisible = false, ZIndex = 9 };
        Paint(line, Shape.StrokeProperty, "Accent");
        var guideDot = new Ellipse { Name = "TimelineGuideDot", Width = 9, Height = 9, StrokeThickness = 2, IsHitTestVisible = false, IsVisible = false, ZIndex = 9 };
        Paint(guideDot, Shape.StrokeProperty, "Accent"); Paint(guideDot, Shape.FillProperty, "Panel");
        var chipText = new TextBlock { FontSize = 12, FontWeight = FontWeight.Bold, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        Paint(chipText, TextBlock.ForegroundProperty, "AccentText");
        var chip = new Border { Name = "TimelineGuideChip", Width = 52, Height = 21, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1), Child = chipText, IsHitTestVisible = false, IsVisible = false, ZIndex = 9 };
        var noteCard = new Border { Name = "TimelineGuideNote", CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(11, 9), BoxShadow = HangingCard.Shadow, IsHitTestVisible = false, IsVisible = false, ZIndex = 12 };
        Paint(noteCard, Border.BackgroundProperty, "Panel"); Paint(noteCard, Border.BorderBrushProperty, "Line");
        canvas.Children.Add(line); canvas.Children.Add(guideDot); canvas.Children.Add(chip); canvas.Children.Add(noteCard);
        // What the line crosses among the blocks is ringed by the block's own shadow, the way a selected block is, so the ring is always the same width all round (a ring drawn as a
        // separate shape was sometimes a pixel or two off). A selected or recording block already has its own ring and is left alone.
        var ringed = new Dictionary<Control, (BoxShadows Shadow, int Z)>();
        var accent = Color.Parse(model.CurrentTheme.Colour("Accent")); var ringColour = Color.FromArgb(0x99, accent.R, accent.G, accent.B);
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
            line.IsVisible = guideDot.IsVisible = chip.IsVisible = noteCard.IsVisible = false;
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
            Canvas.SetLeft(line, x - 1); Canvas.SetTop(line, axisY);
            Canvas.SetLeft(guideDot, x - 4.5); Canvas.SetTop(guideDot, axisY - 5);
            chipText.Text = time.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            Canvas.SetLeft(chip, x - chip.Width / 2); Canvas.SetTop(chip, 1);
            // Quiet while free, the accent wash while it is on a note.
            if (near is null) { Paint(chip, Border.BackgroundProperty, "Panel"); Paint(chip, Border.BorderBrushProperty, "Line"); }
            else { Paint(chip, Border.BackgroundProperty, "Selected"); Paint(chip, Border.BorderBrushProperty, "Accent"); }
            var under = new List<Control>();
            if (!onRuler)
                foreach (var child in canvas.Children.OfType<Control>().ToList())
                {
                    var isBlock = Names(child, "TimelineEntry") || Names(child, "TimelineGroup") || Names(child, "TimelineRecording") || Names(child, "TimelineMeeting");
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
                if (NotesModel.IsImportant(note)) head.Children.Add(new IconGlyph(IconKind.StarFilled) { Width = 13, Height = 13, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
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
            Update(point.Position.X, point.Position.Y);
        };
        canvas.PointerExited += (_, _) => Hide();
    }
}

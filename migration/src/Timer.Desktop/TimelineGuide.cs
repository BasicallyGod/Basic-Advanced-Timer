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
/// ticked off, the accent while it is coming), and a dashed guide line that follows the pointer along the day so it is clear what is being pointed at. The guide has a hollow
/// dot on the ruler and a time chip over the hour labels, rings whatever block or meeting lies under it, and snaps to a pin that is within a few pixels, when the pin swells and
/// the note's words appear beside the line. It is dashed and hollow where the now line is solid and filled, so the two are never mistaken for each other. Pressing a pin opens
/// the note in the Notes page. A note without a reminder has no time to sit at, so it has no pin.
/// </summary>
public static class TimelineGuide
{
    /// <summary>The room the pins take under the ruler: the meeting row and the blocks sit this much lower on a day that has one.</summary>
    public const double PinRoom = 10;
    /// <summary>How near the pointer must be to a pin, in pixels, for the guide to snap to it.</summary>
    public const double SnapPixels = 8;
    /// <summary>A pin's dot, with its ring, at rest and while the guide is on it.</summary>
    public const double RestDot = 15, ActiveDot = 17;

    private sealed class Pin(DayNote note, double x, Canvas visual, Ellipse dot, Ellipse halo, Border stem)
    {
        public DayNote Note { get; } = note; public double X { get; } = x; public Canvas Visual { get; } = visual; public Ellipse Dot { get; } = dot; public Ellipse Halo { get; } = halo; public Border Stem { get; } = stem;
    }

    private static void Paint(Control control, AvaloniaProperty property, string key) => control.Bind(property, control.GetResourceObservable(key));

    /// <summary>Hangs the pins and the guide on the timeline's canvas. <paramref name="pinned"/> are the day's notes with a reminder, in time order.</summary>
    public static void Attach(Canvas canvas, TimerModel model, DateTime day, double pixelsPerHour, double gutter, double axisY, double bandBottom, IReadOnlyList<DayNote> pinned)
    {
        double X(DateTime time) => gutter + (time - day).TotalHours * pixelsPerHour;
        var pins = new List<Pin>();
        string ColourKey(DayNote note) => model.Notes.IsLate(note) ? "Overtime" : "Accent";
        foreach (var note in pinned)
        {
            var due = NotesModel.ReminderOf(note)!.Value; var x = X(due);
            var visual = new Canvas { Name = "TimelinePin_" + note.Id, Width = 20, Height = 28, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), ZIndex = 4 };
            var stem = new Border { Width = 1.5, Height = 10, Opacity = .7, IsHitTestVisible = false }; Canvas.SetLeft(stem, 9.25);
            var halo = new Ellipse { Width = 22, Height = 22, StrokeThickness = 3, Opacity = 0, IsHitTestVisible = false }; Canvas.SetLeft(halo, -1); Canvas.SetTop(halo, 2);
            // The stroke (the colour of the card) is inside the size, so the dot's colour is 10 across and a ring of the card's colour keeps it clear of the lines behind.
            var dot = new Ellipse { Width = RestDot, Height = RestDot, StrokeThickness = 2.5, IsHitTestVisible = false }; Canvas.SetLeft(dot, 10 - RestDot / 2); Canvas.SetTop(dot, 11.5 - RestDot / 2);
            Paint(dot, Shape.StrokeProperty, "Panel");
            visual.Children.Add(stem); visual.Children.Add(halo); visual.Children.Add(dot);
            Canvas.SetLeft(visual, x - 10); Canvas.SetTop(visual, axisY);
            var words = model.Notes.Text(note); var when = NoteDates.When(due, model.Engine.Now);
            Avalonia.Automation.AutomationProperties.SetName(visual, "Reminder " + words + ", " + when + (model.Notes.IsLate(note) ? ", late" : ""));
            var id = note.Id;
            visual.PointerPressed += (_, e) => { if (e.GetCurrentPoint(visual).Properties.IsLeftButtonPressed) { model.Notes.RequestOpenNote(id); e.Handled = true; } };
            canvas.Children.Add(visual);
            pins.Add(new Pin(note, x, visual, dot, halo, stem));
        }
        void Recolour(Pin pin)
        {
            var key = ColourKey(pin.Note);
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
        var rings = new List<Border>();
        Border Ring(int index)
        {
            while (rings.Count <= index)
            {
                var ring = new Border { BorderThickness = new Thickness(2), Opacity = .5, IsHitTestVisible = false, IsVisible = false, ZIndex = 8, Tag = "GuideRing" };
                Paint(ring, Border.BorderBrushProperty, "Accent"); canvas.Children.Add(ring); rings.Add(ring);
            }
            return rings[index];
        }
        Pin? active = null;
        void Hide()
        {
            line.IsVisible = guideDot.IsVisible = chip.IsVisible = noteCard.IsVisible = false;
            foreach (var ring in rings) ring.IsVisible = false;
            Activate(null);
        }
        void Activate(Pin? pin)
        {
            if (ReferenceEquals(active, pin)) return;
            if (active is { } old) { old.Dot.Width = old.Dot.Height = RestDot; Canvas.SetLeft(old.Dot, 10 - RestDot / 2); Canvas.SetTop(old.Dot, 11.5 - RestDot / 2); old.Halo.Opacity = 0; }
            active = pin;
            if (pin is not null) { pin.Dot.Width = pin.Dot.Height = ActiveDot; Canvas.SetLeft(pin.Dot, 10 - ActiveDot / 2); Canvas.SetTop(pin.Dot, 11.5 - ActiveDot / 2); pin.Halo.Opacity = .3; }
        }
        bool Names(Control c, string prefix) => c.Name is { } n && n.StartsWith(prefix, StringComparison.Ordinal);
        void Update(double pointerX)
        {
            var span = 24 * pixelsPerHour;
            if (pointerX < gutter || pointerX > gutter + span) { Hide(); return; }
            var near = pins.OrderBy(p => Math.Abs(p.X - pointerX)).FirstOrDefault(p => Math.Abs(p.X - pointerX) <= SnapPixels);
            var x = near?.X ?? pointerX;
            var time = near is not null ? NotesModel.ReminderOf(near.Note)!.Value : day.AddMinutes(Math.Round((x - gutter) / pixelsPerHour * 60));
            Activate(near);
            line.IsVisible = guideDot.IsVisible = chip.IsVisible = true;
            Canvas.SetLeft(line, x - 1); Canvas.SetTop(line, axisY);
            Canvas.SetLeft(guideDot, x - 4.5); Canvas.SetTop(guideDot, axisY - 5);
            chipText.Text = time.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            Canvas.SetLeft(chip, x - chip.Width / 2); Canvas.SetTop(chip, 1);
            // Quiet while free, the accent wash while it is on a note.
            if (near is null) { Paint(chip, Border.BackgroundProperty, "Panel"); Paint(chip, Border.BorderBrushProperty, "Line"); }
            else { Paint(chip, Border.BackgroundProperty, "Selected"); Paint(chip, Border.BorderBrushProperty, "Accent"); }
            // A soft ring round whatever lies under the line.
            var count = 0;
            // Rings join the canvas's children as they are first needed, so walk a copy of the list.
            foreach (var child in canvas.Children.OfType<Control>().ToList())
            {
                var isBlock = Names(child, "TimelineEntry") || Names(child, "TimelineGroup") || Names(child, "TimelineRecording") || Names(child, "TimelineMeeting");
                if (!isBlock || child.Bounds.Width <= 0) continue;
                var left = Canvas.GetLeft(child); if (x < left || x > left + child.Bounds.Width) continue;
                var ring = Ring(count++);
                ring.CornerRadius = child is Border b ? b.CornerRadius : new CornerRadius(12);
                ring.Width = child.Bounds.Width + 4; ring.Height = child.Bounds.Height + 4; Canvas.SetLeft(ring, left - 2); Canvas.SetTop(ring, Canvas.GetTop(child) - 2); ring.IsVisible = true;
            }
            for (var i = count; i < rings.Count; i++) rings[i].IsVisible = false;
            // The note's words beside the line, when it is on a pin (and tooltips are on).
            noteCard.IsVisible = near is not null && model.Tooltips;
            if (near is not null) ShowNote(near, x);
        }
        void ShowNote(Pin pin, double x)
        {
            var late = model.Notes.IsLate(pin.Note); var due = NotesModel.ReminderOf(pin.Note)!.Value;
            var words = new TextBlock { Text = model.Notes.Text(pin.Note), FontSize = 12.5, FontWeight = FontWeight.SemiBold, MaxWidth = 240, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            var head = Ui.Horizontal(NoteParts.Dot(model, model.Notes.AccountOf(pin.Note)), words); head.Spacing = 7;
            var minutes = (int)Math.Max(0, (model.Engine.Now - due).TotalMinutes);
            var state = late ? (minutes < 60 ? $"{minutes} minutes late" : $"{minutes / 60}h {minutes % 60:00}m late") : "reminder";
            var detail = new TextBlock { Text = NoteDates.When(due, model.Engine.Now) + " · " + state, FontSize = 11.5 }; detail.Classes.Add(late ? "overtime" : "muted");
            noteCard.Child = new StackPanel { Spacing = 3, Children = { head, detail } };
            noteCard.Measure(Size.Infinity); var width = noteCard.DesiredSize.Width;
            var left = x + 14; if (left + width > canvas.Width) left = x - 14 - width;
            Canvas.SetLeft(noteCard, Math.Max(0, left)); Canvas.SetTop(noteCard, axisY + 34 + PinRoom);
        }
        canvas.PointerMoved += (_, e) =>
        {
            var point = e.GetCurrentPoint(canvas);
            // A press (dragging a block, resizing, a menu) has the pointer's attention, so the guide steps aside.
            if (point.Properties.IsLeftButtonPressed || point.Properties.IsRightButtonPressed) { Hide(); return; }
            Update(point.Position.X);
        };
        canvas.PointerExited += (_, _) => Hide();
    }
}

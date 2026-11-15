using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Timer.Core;

namespace Timer.Desktop;

// Stack gestures are immediate actions; the fields below use the shared explicit RecordEditor transaction.
public sealed partial class SelectedBlockCard
{
    private static string Hm(TimeSpan? time) => time is { } t ? t.ToString(@"hh\:mm") : "";
    // The timeline of a stack of meetings, in place of the blocks' one; drag a picked meeting's ends and it moves by five minutes (a repeating meeting for this day only).
    private void ShowMeetingStack(DayNote meeting, IReadOnlyList<DayNote>? stack)
    {
        if (kindText is not null) kindText.Text = stack is null ? "Meeting" : "Meetings";
        if (stackHost is null) return;
        stackHost.Child = null; stackHost.IsVisible = stack is not null;
        if (stack is null) return;
        var fill = Color.Parse(model.CurrentTheme.Colour("Hover")); var ink = Color.Parse(model.CurrentTheme.Colour("Ink"));
        var bars = stack.Select(m => new StackTimeline.Bar("M" + m.Id, model.Notes.Text(m), "", $"{model.Notes.Text(m)} · {Hm(m.At)}–{Hm(m.End)}", m.Day.Date + m.At!.Value, m.Day.Date + m.End!.Value, fill, ink,
            m.Id == model.SelectedMeetingId, false, m.Id == model.SelectedMeetingId, new IconGlyph(IconKind.Today) { VerticalAlignment = VerticalAlignment.Center })).ToList();
        var timeline = new StackTimeline(model, bars, 5);
        timeline.PickedBar += bar => { var id = stack[bars.IndexOf(bar)].Id; if (id != model.SelectedMeetingId) model.SelectMeeting(id); };
        timeline.Resized += (bar, start, end) =>
        {
            var target = stack[bars.IndexOf(bar)];
            var problem = model.Notes.MoveMeeting(target.Id, start, end);
            if (problem is not null) model.Refuse(problem);
            else model.Tell((model.Notes.SeriesOf(target) is not null ? "Moved for this day only" : "Meeting moved") + " · " + start.ToString("HH:mm") + "–" + end.ToString("HH:mm"));
        };
        timeline.Margin = new Thickness(3, 0, 3, 4);
        stackHost.Child = new ScrollViewer { Content = timeline, MaxHeight = 160, Name = "StackScroll", HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 10, 0) };
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace Timer.Desktop.Tests;

/// <summary>
/// A sweep for cut-off shadows. A raised key, card or well draws its shadow outside its own edge, so any ancestor that clips to its bounds shows it cut off square if the shadow
/// reaches past that ancestor's edge. For every shadow on every page and in both looks, this finds the clipping ancestors the shadow would be cut by.
/// </summary>
public sealed class ShadowClipTests
{
    private static void Settle(Window w) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = w.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static void Click(Window w, string name) { var b = w.GetVisualDescendants().OfType<Button>().FirstOrDefault(x => x.Name == name); b?.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle(w); }

    private static BoxShadows ShadowsOf(Visual v) => v switch { Border b => b.BoxShadow, Avalonia.Controls.Presenters.ContentPresenter p => p.BoxShadow, _ => default };

    // Parts of the framework that clip as a matter of course (a scroll view, a text box's own template) and are meant to.
    private static bool Clips(Visual a) => a is Control c && (c.ClipToBounds || a is Avalonia.Controls.Presenters.ScrollContentPresenter) && a is not Window;

    public static IEnumerable<string> Cut(Window window)
    {
        foreach (var v in window.GetVisualDescendants().Where(d => d is Visual { IsEffectivelyVisible: true } && d.Bounds.Width > 0 && d.Bounds.Height > 0))
        {
            var shadows = ShadowsOf(v); if (shadows.Count == 0) continue;
            var left = 0.0; var top = 0.0; var right = 0.0; var bottom = 0.0;
            for (var i = 0; i < shadows.Count; i++)
            {
                var s = shadows[i]; if (s.IsInset) continue;
                var reach = Math.Max(0, s.Blur) / 2 + Math.Max(0, s.Spread);
                if (reach < 3) continue; // A hairline ring or a 1 pixel lift is not worth the sweep.
                left = Math.Max(left, reach - s.OffsetX); right = Math.Max(right, reach + s.OffsetX); top = Math.Max(top, reach - s.OffsetY); bottom = Math.Max(bottom, reach + s.OffsetY);
            }
            if (left + right + top + bottom == 0) continue;
            foreach (var a in v.GetVisualAncestors().Where(Clips))
            {
                if (v.GetVisualAncestors().OfType<Control>().Any(x => x.Name is "ShadowsOn" or "ShadowsOff")) continue; // A picture of a shadow, drawn inside its frame on purpose.
                if (a is Border { Name: "DashboardSurface" }) continue; // The window's own edge: a card against it is meant to end there.
                if (v.TranslatePoint(default, a) is not { } origin) continue;
                var outer = new Rect(origin.X - left, origin.Y - top, v.Bounds.Width + left + right, v.Bounds.Height + top + bottom);
                var inner = new Rect(origin, v.Bounds.Size);
                var clip = new Rect(a.Bounds.Size);
                // Cut when the shadow would reach past the clip but the thing itself is inside it (a thing already half out is a scroll, not a bug).
                if (clip.Contains(inner.TopLeft) && clip.Contains(inner.BottomRight) && !(clip.Contains(outer.TopLeft) && clip.Contains(outer.BottomRight)))
                    yield return $"{Describe(v)} is cut by {Describe(a)} (shadow reaches {Math.Round(Math.Max(Math.Max(clip.Left - outer.Left, outer.Right - clip.Right), Math.Max(clip.Top - outer.Top, outer.Bottom - clip.Bottom)), 1)} px past it)";
            }
        }
    }
    private static string Describe(Visual v)
    {
        var text = v.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
        var nearest = v is Control { Name: { Length: > 0 } } ? "" : v.GetVisualAncestors().OfType<Control>().FirstOrDefault(c => c.Name is { Length: > 0 })?.Name;
        return v.GetType().Name + (v is Control { Name: { Length: > 0 } n } ? " " + n : nearest is null ? "" : " in " + nearest) + (v is Control c && c.Classes.Count > 0 ? " ." + string.Join(".", c.Classes) : "") + (text is null ? "" : " “" + text + "”") + $" {v.Bounds.Width:0}x{v.Bounds.Height:0}";
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void NoRaisedKeyCardOrWellHasItsShadowCutOffSquare(string theme)
    {
        var model = TimerModel.CreateDemo(() => new DateTime(2026, 10, 14, 14, 15, 0)); model.SetTheme(theme); model.CompleteSetup();
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        var found = new List<string>();
        try
        {
            void Look(string page) { foreach (var line in Cut(window).Distinct()) found.Add(page + ": " + line); }
            Look("timeline");
            Click(window, "CardKindMeetings"); Look("timeline, meetings form");
            Click(window, "CardKindNotes"); Look("timeline, notes form");
            Click(window, "NotesTab"); Look("notes page");
            Click(window, "WeekOverviewTab"); Look("week");
            var entry = new EntryDialog(model, new DateTime(2026, 10, 14, 9, 0, 0), new DateTime(2026, 10, 14, 9, 30, 0)).Open(window); Look("add time dialog"); entry.Close(); Settle(window);
            var accounts = new AccountsDialog(model).Open(window); Look("accounts dialog"); accounts.Close(); Settle(window);
            Click(window, "Settings"); Look("settings");
            foreach (var section in new[] { "SettingsLook", "SettingsPill", "SettingsAlerts", "SettingsDay", "SettingsAccounts", "SettingsExport", "SettingsAbout" }) { Click(window, section); Look("settings " + section); }
        }
        finally { window.Close(); }
        Assert.True(found.Count == 0, string.Join("\n", found));
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void NoKeyOnThePillHasItsShadowCutOffSquare(string theme)
    {
        var model = TimerModel.CreateDemo(() => new DateTime(2026, 10, 14, 14, 15, 0)); model.SetTheme(theme); model.CompleteSetup();
        var pill = new PillWindow(model); pill.Show(); Settle(pill);
        try { var found = Cut(pill).Distinct().ToList(); Assert.True(found.Count == 0, string.Join("\n", found)); }
        finally { pill.Close(); }
    }
}

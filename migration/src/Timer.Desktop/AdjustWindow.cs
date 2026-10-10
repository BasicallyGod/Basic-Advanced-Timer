using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Timer.Core;

namespace Timer.Desktop;

// Adds or removes minutes on an account for a day, for the times a recording is a little off and editing a block would be fuss.
public sealed class AdjustWindow : Window
{
    // Accepts +15, -10, 15, 1:30, -0:20 and 7.5, as minutes except where h:mm is used.
    public static bool TryParse(string? text, out decimal minutes)
    {
        minutes = 0; text = text?.Trim().Replace(" ", "");
        if (string.IsNullOrEmpty(text)) return false;
        var sign = 1m;
        if (text[0] is '+' or '-') { sign = text[0] == '-' ? -1 : 1; text = text[1..]; }
        if (text.Contains(':'))
        {
            var parts = text.Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var mins) || mins >= 60) return false;
            minutes = sign * (hours * 60 + mins); return minutes != 0;
        }
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)) return false;
        minutes = sign * value; return minutes != 0 && decimal.Round(minutes, 2) == minutes;
    }

    public AdjustWindow(TimerModel model, string account, DateTime? day = null)
    {
        var date = (day ?? model.Engine.Now).Date;
        Title = "Adjust time"; Width = 460; SizeToContent = SizeToContent.Height; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var label = BreakAccount.Display(account);
        var root = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        root.Children.Add(Ui.Text("Adjust " + label, 22));
        var today = date == model.Engine.Now.Date ? "today" : date.ToString("dddd d MMMM", CultureInfo.GetCultureInfo("en-GB"));
        root.Children.Add(Ui.Text($"Add or remove time on {today} without changing any recorded block.", 13, "muted"));
        var current = model.Engine.Credited(account, date, date.AddDays(1));
        var minutes = new TextBox { Name = "AdjustMinutes", Watermark = "+15 or -10", Width = 160, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        var preview = Ui.Text("", 13, "muted");
        void Update()
        {
            preview.Text = TryParse(minutes.Text, out var amount)
                ? $"{label} {today}: {model.FormatShort(current)} to {model.FormatShort(current + TimeSpan.FromMinutes((double)amount))}"
                : $"{label} {today} so far: {model.FormatShort(current)}";
        }
        minutes.TextChanged += (_, _) => Update(); Update();
        void Nudge(int by)
        {
            var now = TryParse(minutes.Text, out var amount) ? amount : 0m;
            var next = now + by; minutes.Text = next == 0 ? "" : (next > 0 ? "+" : "") + next.ToString("0.##", CultureInfo.InvariantCulture);
        }
        var quick = Ui.Horizontal(Ui.Button("-15", () => Nudge(-15), "AdjustMinus15"), Ui.Button("-5", () => Nudge(-5), "AdjustMinus5"),
            Ui.Button("+5", () => Nudge(5), "AdjustPlus5"), Ui.Button("+15", () => Nudge(15), "AdjustPlus15"), Ui.Button("+30", () => Nudge(30), "AdjustPlus30"));
        root.Children.Add(Ui.Text("Minutes to add or remove", 13));
        root.Children.Add(minutes); root.Children.Add(quick); root.Children.Add(preview);
        var note = new TextBox { Name = "AdjustNote", Watermark = "Note (optional)", AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Height = 80 };
        root.Children.Add(note);
        var error = new TextBlock { Name = "AdjustError", TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "overtime" } };
        root.Children.Add(error);
        var save = Ui.Button("Save", () =>
        {
            if (!TryParse(minutes.Text, out var amount)) { error.Text = "Enter minutes to add or remove, such as +15, -10 or 1:30."; return; }
            if (model.AdjustTime(account, date, amount, note.Text ?? "")) Close(); else error.Text = model.LastError ?? model.EditError;
        }, "SaveAdjustment");
        root.Children.Add(Ui.Horizontal(save, Ui.Button("Cancel", Close)));
        Content = root;
    }
}

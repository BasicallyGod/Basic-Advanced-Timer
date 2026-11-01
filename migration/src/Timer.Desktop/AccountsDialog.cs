using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Timer.Storage;

namespace Timer.Desktop;

public sealed class AccountsDialog : Dialog
{
    public AccountsDialog(TimerModel model)
    {
        Title = "Accounts and budgets";
        Width = 700; Height = 540;
        var root = Ui.Grid("*", "Auto,*,Auto,Auto,Auto,Auto"); root.Margin = new Thickness(24, 4, 24, 24);
        var intro = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 16) };
        intro.Children.Add(new TextBlock { Text = "Rename, reorder (drag the grip) or delete accounts and set budgets in hours. A blank budget means no limit. Untick In tracker to take an account off the tracker list. Its recorded time stays in every report.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        Ui.Put(root, intro);
        var confirm = new StackPanel { Name = "DeleteConfirm", Spacing = 8, IsVisible = false, Margin = new Thickness(0, 8, 0, 0) };
        var list = new StackPanel { Spacing = 10 };
        var entries = new List<AccountRow>();
        var headings = Ui.Grid("26,*,50,80,85,110,44");
        Ui.Put(headings, Ui.Text("Account", 12, "muted"), 1); Ui.Put(headings, Ui.Text("Colour", 12, "muted"), 2); Ui.Put(headings, Ui.Text("In tracker", 12, "muted"), 3);
        Ui.Put(headings, Ui.Text("Budget", 12, "muted"), 4); Ui.Put(headings, Ui.Text("Period", 12, "muted"), 5);
        list.Children.Add(headings);
        // The grip at the left of a row drags it up or down: the row follows the pointer and swaps places as it passes a neighbour's middle. The order is the order saved.
        Control DragHandle(AccountRow entry)
        {
            var grip = new Border { Name = "AccountGrip" + entries.Count, Width = 22, Height = 30, Background = Avalonia.Media.Brushes.Transparent, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.SizeAll), Child = new IconGlyph(IconKind.Grip) { Width = 18, Height = 18, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center } };
            ToolTip.SetTip(grip, "Drag to reorder"); Avalonia.Automation.AutomationProperties.SetName(grip, "Reorder account");
            var dragging = false;
            grip.PointerPressed += (_, e) => { if (e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed) { dragging = true; e.Pointer.Capture(grip); entry.Grid.Opacity = .85; e.Handled = true; } };
            grip.PointerMoved += (_, e) =>
            {
                if (!dragging) return;
                var y = e.GetPosition(list).Y; var at = entries.IndexOf(entry); var to = at;
                while (to > 0 && y < entries[to - 1].Grid.Bounds.Center.Y) to--;
                while (to < entries.Count - 1 && y > entries[to + 1].Grid.Bounds.Center.Y) to++;
                if (to != at) Move(entry, to);
                e.Handled = true;
            };
            grip.PointerReleased += (_, e) => { if (!dragging) return; dragging = false; e.Pointer.Capture(null); entry.Grid.Opacity = entry.Deleting ? 1 : 1; e.Handled = true; };
            grip.PointerCaptureLost += (_, _) => { dragging = false; entry.Grid.Opacity = 1; };
            return grip;
        }
        void Move(AccountRow entry, int to)
        {
            entries.Remove(entry); entries.Insert(to, entry);
            // Moved in place, not taken out and put back: that would end the drag, which the grip holds by capturing the pointer. The heading is the first child.
            ((Avalonia.Collections.AvaloniaList<Control>)list.Children).Move(list.Children.IndexOf(entry.Grid), to + 1);
        }
        void Add(AccountSettings account, bool existing = true)
        {
            var row = Ui.Grid("26,*,50,80,85,110,44");
            var name = new TextBox { Text = account.Name, Margin = new Thickness(0, 0, 8, 0) };
            Ui.Put(row, name, 1);
            // A new account starts on the first palette colour nobody here is using, as the model would give it.
            var chosen = account.Colour ?? TimerModel.Palette.FirstOrDefault(c => !entries.Any(e => e.Colour().Equals(c, StringComparison.OrdinalIgnoreCase))) ?? TimerModel.Palette[0];
            Ui.Put(row, ColourButton(() => chosen, value => chosen = value, account.Name), 2);
            var visible = new CheckBox { IsChecked = !account.Hidden }; ToolTip.SetTip(visible, "In tracker");
            Ui.Put(row, visible, 3);
            var budget = new TextBox { Text = account.BudgetHours?.ToString("0.###", CultureInfo.InvariantCulture) ?? "", Margin = new Thickness(0, 0, 8, 0) };
            Ui.Put(row, budget, 4);
            var period = new ComboBox { ItemsSource = new[] { "day", "week", "month", "quarter" }, SelectedItem = account.BudgetPeriod, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            Ui.Put(row, period, 5);
            var entry = new AccountRow { Original = existing ? account.Name : null, Name = name, Visible = visible, Budget = budget, Period = period, Colour = () => chosen, Grid = row };
            // The bin removes a new row at once. For an existing account it marks the row for deleting, dimmed, and a second press takes the mark off.
            var bin = Ui.IconButton(IconKind.Trash, existing ? "Delete account" : "Remove row", "DeleteAccount" + entries.Count, () =>
            {
                if (entry.Original is null) { list.Children.Remove(entry.Grid); entries.Remove(entry); return; }
                entry.Deleting = !entry.Deleting;
                // The row dims, apart from the bin, so it is clear what will go and the same button can take the mark off again.
                foreach (var child in entry.Grid.Children.Where(c => !ReferenceEquals(c, entry.Bin))) { child.Opacity = entry.Deleting ? .4 : 1; child.IsEnabled = !entry.Deleting; }
                ((IconGlyph)entry.Bin!.Content!).TintKey = entry.Deleting ? "Overtime" : null; ((IconGlyph)entry.Bin.Content).InvalidateVisual();
                ToolTip.SetTip(entry.Bin, entry.Deleting ? "Keep account" : "Delete account");
                confirm.IsVisible = false;
            });
            entry.Bin = bin; Ui.Put(row, bin, 6);
            Ui.Put(row, DragHandle(entry), 0);
            list.Children.Add(row); entries.Add(entry);
        }
        foreach (var account in model.AccountSettings) Add(account);
        bool Taken(string name) => entries.Any(e => (e.Name.Text ?? "").Trim().Equals(name, StringComparison.OrdinalIgnoreCase));
        Ui.Put(root, new ScrollViewer { Content = list, AllowAutoHide = false }, row: 1);
        var error = new TextBlock { Classes = { "overtime" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(0, 10) };
        var input = new TextBox { Watermark = "New account name", Width = 280 };
        var add = Ui.Horizontal(input, Ui.DialogButton("Add account", () =>
        {
            var name = input.Text?.Trim() ?? "";
            if (name.Length == 0 || name.IndexOfAny(new[] { '\t', '\r', '\n' }) >= 0 || name.Equals(Timer.Core.BreakAccount.Name, StringComparison.OrdinalIgnoreCase) || name.Equals(Timer.Core.BreakAccount.Label, StringComparison.OrdinalIgnoreCase)
                || Taken(name))
            { error.Text = "Choose a unique account name. Break is reserved."; return; }
            Add(new(name), false); input.Text = ""; error.Text = "";
        }));
        add.Margin = new Thickness(0, 14, 0, 0); Ui.Put(root, add, row: 2); Ui.Put(root, error, row: 3);
        // Saves the list. Anything marked for deleting that has recorded time asks first, in a panel above the buttons, and only goes once confirmed.
        void Commit(bool confirmed)
        {
            try
            {
                var kept = entries.Where(e => !e.Deleting).ToList(); var deleted = entries.Where(e => e.Deleting && e.Original is not null).Select(e => e.Original!).ToList();
                if (kept.Count == 0) { error.Text = "Keep at least one account."; return; }
                var withHistory = deleted.Select(d => (Name: d, History: model.AccountHistory(d))).Where(d => d.History.Entries > 0).ToList();
                if (withHistory.Count > 0 && !confirmed)
                {
                    var entriesGone = withHistory.Sum(d => d.History.Entries); var timeGone = withHistory.Aggregate(TimeSpan.Zero, (sum, d) => sum + d.History.Time);
                    confirm.Children.Clear();
                    confirm.Children.Add(new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontWeight = Avalonia.Media.FontWeight.SemiBold,
                        Text = $"Delete {string.Join(", ", withHistory.Select(d => d.Name))}? This removes {entriesGone} recorded {(entriesGone == 1 ? "entry" : "entries")} ({model.Format(timeGone)}) and any notes on them, for good. A backup copy is saved first, but Undo cannot bring them back." });
                    var yes = Ui.DialogButton("Delete for good", () => { confirm.IsVisible = false; Commit(true); }, "ConfirmDeleteAccounts"); yes.Classes.Add("finishDay"); yes.Classes.Add("outlined"); yes.Height = 36; yes.Padding = new Thickness(16, 0);
                    confirm.Children.Add(Ui.Horizontal(yes, Ui.DialogButton("Cancel", () => confirm.IsVisible = false, "CancelDeleteAccounts")));
                    confirm.IsVisible = true; error.Text = ""; return;
                }
                // An account keeps its colour through editing, renaming and reordering; a new one is given one by the model.
                var accounts = kept.Select(e => new AccountSettings((e.Name.Text ?? "").Trim(),
                    string.IsNullOrWhiteSpace(e.Budget.Text) ? null : double.Parse(e.Budget.Text, NumberStyles.Float, CultureInfo.InvariantCulture),
                    (string?)e.Period.SelectedItem ?? "week", e.Visible.IsChecked != true, e.Colour())).ToList();
                var renames = kept.Where(e => e.Original is not null).Zip(accounts, (e, a) => (From: e.Original!, To: a.Name))
                    .Where(r => r.From != r.To).ToDictionary(r => r.From, r => r.To);
                model.SetAccounts(accounts, renames, deleted);
                if (model.LastError is not null) { error.Text = model.LastError; return; }
                Close();
            }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            { error.Text = "Use positive budget hours, such as 7.5, or leave the budget blank."; }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or InvalidDataException or IOException)
            { error.Text = ex.Message; }
        }
        Ui.Put(root, confirm, row: 4);
        Ui.Put(root, Ui.Horizontal(Ui.DialogButton("Save", () => Commit(false), "SaveAccounts"), Ui.DialogButton("Cancel", Close)), row: 5);
        Content = root;
    }
    private sealed class AccountRow
    {
        public string? Original; public TextBox Name = null!; public CheckBox Visible = null!; public TextBox Budget = null!; public ComboBox Period = null!;
        public Func<string> Colour = null!; public Grid Grid = null!; public Button? Bin; public bool Deleting;
    }
    // A round swatch showing the account's colour. It opens the palette, and a box for any other colour as #RRGGBB.
    private static Button ColourButton(Func<string> get, Action<string> set, string accountName)
    {
        var swatch = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = Avalonia.Media.Brush.Parse(get()), BorderThickness = new Thickness(1), BorderBrush = Avalonia.Media.Brushes.Gray };
        var button = new Button { Content = swatch, Name = "AccountColour", Padding = new Thickness(4), Background = Avalonia.Media.Brushes.Transparent, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        ToolTip.SetTip(button, "Colour");
        void Choose(string colour) { set(colour); swatch.Background = Avalonia.Media.Brush.Parse(colour); }
        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(4) };
        var grid = new UniformGrid { Columns = 8, Width = 8 * 34 }; // Two even rows of eight.
        var flyout = new Flyout { Content = panel };
        foreach (var colour in TimerModel.Palette)
        {
            var pick = new Button { Name = "Colour" + colour[1..], Width = 28, Height = 28, Margin = new Thickness(3), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Padding = new Thickness(0), CornerRadius = new CornerRadius(14), Background = Avalonia.Media.Brush.Parse(colour) };
            ToolTip.SetTip(pick, colour);
            pick.Click += (_, _) => { Choose(colour); flyout.Hide(); };
            grid.Children.Add(pick);
        }
        panel.Children.Add(grid);
        var hex = new TextBox { Name = "ColourHex", Watermark = "#RRGGBB", Text = get(), Width = 120, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        var note = Ui.Text("", 12, "muted");
        var apply = Ui.DialogButton("Use", () =>
        {
            var text = (hex.Text ?? "").Trim();
            if (!text.StartsWith('#')) text = "#" + text;
            if (!Ui.IsColour(text)) { note.Text = "Use six digits, such as #3A7CA5."; return; }
            Choose(text.ToUpperInvariant()); note.Text = ""; flyout.Hide();
        }, "UseColour");
        panel.Children.Add(Ui.Horizontal(hex, apply)); panel.Children.Add(note);
        flyout.Opened += (_, _) => hex.Text = get();
        button.Flyout = flyout;
        return button;
    }
}

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Timer.Storage;

namespace Timer.Desktop;

public sealed class AccountsWindow : Window
{
    public AccountsWindow(TimerModel model)
    {
        Title = "Accounts and budgets";
        Width = 640; Height = 500; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = Ui.Grid("*", "Auto,*,Auto,Auto,Auto"); root.Margin = new Thickness(24);
        var intro = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 16) };
        intro.Children.Add(Ui.Text("Accounts and budgets", 22));
        intro.Children.Add(new TextBlock { Text = "Add accounts, rename them, show or hide them, and set budgets in hours. Drag accounts on the dashboard to reorder them.\nA blank budget means no limit. Hidden accounts keep their recorded time.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        Ui.Put(root, intro);
        var list = new StackPanel { Spacing = 10 };
        var entries = new List<(string? Original, TextBox Name, CheckBox Visible, TextBox Budget, ComboBox Period)>();
        var headings = Ui.Grid("*,65,85,110");
        Ui.Put(headings, Ui.Text("Account", 12, "muted")); Ui.Put(headings, Ui.Text("Show", 12, "muted"), 1);
        Ui.Put(headings, Ui.Text("Hours", 12, "muted"), 2); Ui.Put(headings, Ui.Text("Period", 12, "muted"), 3);
        list.Children.Add(headings);
        void Add(AccountSettings account, bool existing = true)
        {
            var row = Ui.Grid("*,65,85,110");
            var name = new TextBox { Text = account.Name, Margin = new Thickness(0, 0, 8, 0) };
            ToolTip.SetTip(name, "Edit the name to rename this account"); Ui.Put(row, name);
            var visible = new CheckBox { IsChecked = !account.Hidden };
            Ui.Put(row, visible, 1);
            var budget = new TextBox { Text = account.BudgetHours?.ToString("0.###", CultureInfo.InvariantCulture) ?? "", Margin = new Thickness(0, 0, 8, 0) };
            Ui.Put(row, budget, 2);
            var period = new ComboBox { ItemsSource = new[] { "week", "month", "quarter" }, SelectedItem = account.BudgetPeriod, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            Ui.Put(row, period, 3);
            list.Children.Add(row); entries.Add((existing ? account.Name : null, name, visible, budget, period));
        }
        foreach (var account in model.AccountSettings) Add(account);
        bool Taken(string name) => entries.Any(e => (e.Name.Text ?? "").Trim().Equals(name, StringComparison.OrdinalIgnoreCase));
        Ui.Put(root, new ScrollViewer { Content = list, AllowAutoHide = false }, row: 1);
        var error = new TextBlock { Classes = { "overtime" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(0, 10) };
        var input = new TextBox { Watermark = "New account name", Width = 280 };
        var add = Ui.Horizontal(input, Ui.Button("Add account", () =>
        {
            var name = input.Text?.Trim() ?? "";
            if (name.Length == 0 || name.IndexOfAny(new[] { '\t', '\r', '\n' }) >= 0 || name.Equals(Timer.Core.BreakAccount.Name, StringComparison.OrdinalIgnoreCase) || name.Equals(Timer.Core.BreakAccount.Label, StringComparison.OrdinalIgnoreCase)
                || Taken(name))
            { error.Text = "Choose a unique account name. Break is reserved."; return; }
            Add(new(name), false); input.Text = ""; error.Text = "";
        }));
        add.Margin = new Thickness(0, 14, 0, 0); Ui.Put(root, add, row: 2); Ui.Put(root, error, row: 3);
        Ui.Put(root, Ui.Horizontal(Ui.Button("Save", async () =>
        {
            try
            {
                // An account keeps its colour through editing, renaming and reordering; a new one is given one by the model.
                var colours = model.AccountSettings.ToDictionary(a => a.Name, a => a.Colour);
                var accounts = entries.Select(e => new AccountSettings((e.Name.Text ?? "").Trim(),
                    string.IsNullOrWhiteSpace(e.Budget.Text) ? null : double.Parse(e.Budget.Text, NumberStyles.Float, CultureInfo.InvariantCulture),
                    (string?)e.Period.SelectedItem ?? "week", e.Visible.IsChecked != true, e.Original is { } original ? colours.GetValueOrDefault(original) : null)).ToList();
                var renames = entries.Where(e => e.Original is not null).Zip(accounts, (e, a) => (From: e.Original!, To: a.Name))
                    .Where(r => r.From != r.To).ToDictionary(r => r.From, r => r.To);
                var history = false;
                if (renames.Count > 0)
                {
                    var answer = await new RenameChoiceWindow(renames).ShowDialog<string?>(this);
                    if (answer is not ("all" or "new")) return;
                    history = answer == "all";
                }
                model.SetAccounts(accounts, renames, history);
                if (model.LastError is not null) { error.Text = model.LastError; return; }
                Close();
            }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            { error.Text = "Use positive budget hours, such as 7.5, or leave the budget blank."; }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or InvalidDataException or IOException)
            { error.Text = ex.Message; }
        }), Ui.Button("Cancel", Close)), row: 4);
        Content = root;
    }
}

// Asks what a rename should do to the entries already recorded, since either choice is reasonable.
public sealed class RenameChoiceWindow : Window
{
    public RenameChoiceWindow(IReadOnlyDictionary<string, string> renames)
    {
        Title = "Rename accounts"; Width = 520; SizeToContent = SizeToContent.Height; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(24) };
        panel.Children.Add(Ui.Text("Rename past entries too?", 20));
        panel.Children.Add(new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Text = string.Join("\n", renames.Select(r => $"{r.Key} to {r.Value}")) });
        panel.Children.Add(new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Text = "Renaming past entries moves all recorded time, notes, groups and positioned credit to the new name, so totals and exports carry on unbroken. A backup copy is saved first, and earlier Undo steps are cleared.\n\nStarting afresh leaves the past entries under the old name, which stays in Timeline, Week and exports as a separate historical account." });
        var buttons = Ui.Horizontal(Ui.Button("Rename past entries", () => Close("all"), "RenameAll"),
            Ui.Button("Start afresh from now", () => Close("new"), "RenameNew"), Ui.Button("Cancel", () => Close(null), "RenameCancel"));
        panel.Children.Add(buttons);
        Content = panel;
    }
}

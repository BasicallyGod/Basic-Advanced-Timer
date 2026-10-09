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
        intro.Children.Add(new TextBlock { Text = "Add accounts, show or hide them, and set budgets in hours.\nA blank budget means no limit. Hidden accounts keep their recorded time.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        Ui.Put(root, intro);
        var list = new StackPanel { Spacing = 10 };
        var entries = new List<(string Name, CheckBox Visible, TextBox Budget, ComboBox Period)>();
        var headings = Ui.Grid("*,65,85,110");
        Ui.Put(headings, Ui.Text("Account", 12, "muted")); Ui.Put(headings, Ui.Text("Show", 12, "muted"), 1);
        Ui.Put(headings, Ui.Text("Hours", 12, "muted"), 2); Ui.Put(headings, Ui.Text("Period", 12, "muted"), 3);
        list.Children.Add(headings);
        void Add(AccountSettings account)
        {
            var row = Ui.Grid("*,65,85,110");
            var name = Ui.Text(account.Name, 14); name.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
            ToolTip.SetTip(name, account.Name); Ui.Put(row, name);
            var visible = new CheckBox { IsChecked = !account.Hidden };
            Ui.Put(row, visible, 1);
            var budget = new TextBox { Text = account.BudgetHours?.ToString("0.###", CultureInfo.InvariantCulture) ?? "", Margin = new Thickness(0, 0, 8, 0) };
            Ui.Put(row, budget, 2);
            var period = new ComboBox { ItemsSource = new[] { "week", "month", "quarter" }, SelectedItem = account.BudgetPeriod, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            Ui.Put(row, period, 3);
            list.Children.Add(row); entries.Add((account.Name, visible, budget, period));
        }
        foreach (var account in model.AccountSettings) Add(account);
        Ui.Put(root, new ScrollViewer { Content = list, AllowAutoHide = false }, row: 1);
        var error = new TextBlock { Classes = { "overtime" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(0, 10) };
        var input = new TextBox { Watermark = "New account name", Width = 280 };
        var add = Ui.Horizontal(input, Ui.Button("Add account", () =>
        {
            var name = input.Text?.Trim() ?? "";
            if (name.Length == 0 || name.IndexOfAny(new[] { '\t', '\r', '\n' }) >= 0 || name.Equals("Lunch", StringComparison.OrdinalIgnoreCase)
                || entries.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            { error.Text = "Choose a unique account name. Lunch is reserved."; return; }
            Add(new(name)); input.Text = ""; error.Text = "";
        }));
        add.Margin = new Thickness(0, 14, 0, 0); Ui.Put(root, add, row: 2); Ui.Put(root, error, row: 3);
        Ui.Put(root, Ui.Horizontal(Ui.Button("Save", () =>
        {
            try
            {
                var accounts = entries.Select(e => new AccountSettings(e.Name,
                    string.IsNullOrWhiteSpace(e.Budget.Text) ? null : double.Parse(e.Budget.Text, NumberStyles.Float, CultureInfo.InvariantCulture),
                    (string?)e.Period.SelectedItem ?? "week", e.Visible.IsChecked != true)).ToList();
                model.SetAccounts(accounts);
                if (model.LastError is not null) { error.Text = model.LastError; return; }
                Close();
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
            { error.Text = "Use positive budget hours, such as 7.5, or leave the budget blank."; }
        }), Ui.Button("Cancel", Close)), row: 4);
        Content = root;
    }
}

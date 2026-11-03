using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace TimerBranchLauncher;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using var window = new LauncherWindow();
        if (args is ["--smoke-test"])
            window.Shown += (_, _) => window.BeginInvoke(new Action(window.Close));
        Application.Run(window);
    }
}

internal sealed class LauncherWindow : Form
{
    private const string Repository = "BasicallyGod/Basic-Advanced-Timer";
    private readonly ComboBox branch = new() { Dock = DockStyle.Fill };
    private readonly TextBox log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly Label location = new() { AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly List<Button> commands = new();
    private string folder = Directory.Exists(Path.Combine(AppContext.BaseDirectory, "test-install"))
        ? AppContext.BaseDirectory
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BAT Launcher");
    private bool busy;

    public LauncherWindow()
    {
        Text = "Timer Branch Launcher";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(660, 400);
        MinimumSize = new Size(620, 360);
        AutoScaleMode = AutoScaleMode.Dpi;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 5 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = "Choose a branch with a successful Windows build", AutoSize = true, Margin = new Padding(0, 0, 0, 8) }, 0, 0);

        var picker = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, Margin = new Padding(0) };
        picker.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        picker.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        picker.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        picker.Controls.Add(branch, 0, 0);
        picker.Controls.Add(Command("Refresh branches", RefreshBranches), 1, 0);
        picker.Controls.Add(Command("GitHub sign-in", SignIn), 2, 0);
        layout.Controls.Add(picker, 0, 1);

        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0, 8, 0, 0) };
        var download = Command("Download and launch", () => RunWorker("Update"));
        actions.Controls.Add(download);
        actions.Controls.Add(Command("Launch installed", () => RunWorker("Launch")));
        actions.Controls.Add(Command("Roll back and launch", () => RunWorker("Rollback")));
        actions.Controls.Add(Command("Choose folder", ChooseFolder));
        layout.Controls.Add(actions, 0, 2);
        layout.Controls.Add(location, 0, 3);
        layout.Controls.Add(log, 0, 4);
        Controls.Add(layout);
        AcceptButton = download;
        FormClosing += (_, e) => { if (busy && e.CloseReason == CloseReason.UserClosing) e.Cancel = true; };
        ShowFolder();
        branch.Text = "alpha";
        Append("Choose a branch or type its exact name. GitHub CLI sign-in is needed for downloads and branch refresh.");
    }

    private Button Command(string title, Func<Task> action)
    {
        var button = new Button { Text = title, AutoSize = true };
        commands.Add(button);
        button.Click += async (_, _) =>
        {
            busy = true;
            branch.Enabled = false;
            foreach (var command in commands) command.Enabled = false;
            try { await action(); }
            catch (Exception error) { Append(error.Message); }
            finally
            {
                busy = false;
                branch.Enabled = true;
                foreach (var command in commands) command.Enabled = true;
            }
        };
        return button;
    }

    private void Append(string message) => log.AppendText(message + Environment.NewLine);

    private void SetBranches(IEnumerable<string> names)
    {
        var selected = branch.Text;
        branch.Items.Clear();
        branch.Items.AddRange(names.Where(name => !name.Equals("main", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Cast<object>().ToArray());
        branch.Text = selected;
    }

    private void ShowFolder()
    {
        location.Text = Path.Combine(folder, "test-install");
        var names = new List<string> { "alpha", "experimental" };
        var channels = Path.Combine(folder, "test-install", "channels");
        if (Directory.Exists(channels))
        {
            foreach (var channel in Directory.EnumerateDirectories(channels))
            {
                try
                {
                    using var state = JsonDocument.Parse(File.ReadAllText(Path.Combine(channel, "state.json")));
                    if (state.RootElement.GetProperty("current").TryGetProperty("branch", out var name) && name.GetString() is { } installed)
                        names.Add(installed);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException)
                {
                    // A missing or incomplete cache can still be selected by typing its branch name.
                }
            }
        }
        SetBranches(names);
    }

    private Task ChooseFolder()
    {
        using var dialog = new FolderBrowserDialog { Description = "Choose the folder that contains test-install, or a folder for a fresh installation." };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            folder = dialog.SelectedPath;
            ShowFolder();
        }
        return Task.CompletedTask;
    }

    private Task SignIn()
    {
        using var process = Process.Start(new ProcessStartInfo("gh.exe", "auth login --hostname github.com --web") { UseShellExecute = true });
        Append("Finish GitHub sign-in in the opened window, then refresh branches.");
        return Task.CompletedTask;
    }

    private async Task RefreshBranches()
    {
        Append("Fetching branches from GitHub…");
        var start = HiddenProcess("gh.exe", "api", "--paginate", "--hostname", "github.com", $"repos/{Repository}/branches?per_page=100", "--jq", ".[].name");
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw new IOException("Branch refresh timed out. You can type a branch name or launch an installed build.");
        }
        if (process.ExitCode != 0) throw new IOException((await errors).Trim() + Environment.NewLine + "Check GitHub CLI sign-in and repository access.");
        var names = (await output).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        SetBranches(branch.Items.Cast<string>().Concat(names).ToArray());
        Append("Branch list refreshed. Feature branches need a successful portable Windows artifact.");
    }

    private async Task RunWorker(string mode)
    {
        var selected = branch.Text.Trim();
        if (selected.Length == 0) throw new InvalidOperationException("Choose a branch first.");
        if (selected.Equals("main", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The main branch is installed through Microsoft Store. Choose a preview branch.");
        Directory.CreateDirectory(folder);
        var worker = Path.Combine(folder, $"timer-launcher-{Guid.NewGuid():N}.ps1");
        try
        {
            using (var source = Assembly.GetExecutingAssembly().GetManifestResourceStream("UpdaterWorker")!)
            using (var destination = new FileStream(worker, FileMode.CreateNew))
            {
                // Windows PowerShell 5.1 needs a BOM for UTF-8 source.
                destination.Write(System.Text.Encoding.UTF8.GetPreamble());
                source.CopyTo(destination);
            }
            Append($"{selected} · {mode}");
            var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            using var process = new Process { StartInfo = HiddenProcess(powershell, "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", worker, "-Mode", mode, "-Branch", selected) };
            void Report(object sender, DataReceivedEventArgs e)
            {
                if (e.Data is { } line && !IsDisposed) BeginInvoke(new Action(() => Append(line)));
            }
            process.OutputDataReceived += Report;
            process.ErrorDataReceived += Report;
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();
            Append(process.ExitCode == 0 ? "Timer launched." : "Launch stopped. See the message above.");
        }
        finally { File.Delete(worker); }
    }

    private static ProcessStartInfo HiddenProcess(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }
}

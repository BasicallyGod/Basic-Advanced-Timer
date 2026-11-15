using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Timer.Desktop;

/// <summary>One route to retained edits, including records hidden by filters or removed elsewhere.</summary>
public sealed class DraftReviewDialog : Dialog
{
    private readonly TimerModel model;
    private readonly StackPanel list = new() { Spacing = 8 };
    private readonly TextBlock error = Ui.Text("", 13, "overtime");
    private readonly Action<string, bool> openComposer;
    private readonly Action? quit;
    public DraftReviewDialog(TimerModel model, Action<string, bool> openComposer, Action? quit = null)
    {
        this.model = model; this.openComposer = openComposer; this.quit = quit;
        Title = quit is null ? "Drafts" : "Save changes before quitting?"; Width = 650; Height = 510;
        var body = Ui.Grid("*", "Auto,*,Auto,Auto"); body.Margin = new Thickness(24, 16, 24, 24); body.RowSpacing = 12;
        var hint = Ui.Text(quit is null ? "Unfinished edits stay here until you save or cancel them." : "Choose what to do with your unfinished edits.", 13, "muted"); hint.TextWrapping = TextWrapping.Wrap;
        Ui.Put(body, hint); Ui.Put(body, new ScrollViewer { Content = list }, row: 1); Ui.Put(body, error, row: 2);
        var actions = new WrapPanel { ItemSpacing = 8, LineSpacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        if (quit is not null)
        {
            var discard = Ui.DialogButton("Discard changes", () =>
            {
                foreach (var draft in model.Drafts.Outstanding.ToArray()) model.Drafts.Cancel(draft);
                model.CreationDrafts.DiscardAll(); Close(); quit();
            }); discard.Name = "DiscardAllDrafts"; actions.Children.Add(discard);
        }
        var keep = Ui.DialogButton(quit is null ? "Close" : "Keep editing", Close); keep.Name = "KeepEditingDrafts"; actions.Children.Add(keep);
        var save = Ui.DialogButton("Save changes", Save); save.Name = "SaveAllDrafts"; save.Classes.Add("primary"); actions.Children.Add(save);
        Ui.Put(body, actions, row: 3); Content = body;
        AttachedToVisualTree += (_, _) => { model.Drafts.Changed += Refresh; model.CreationDrafts.Changed += Refresh; Refresh(); };
        DetachedFromVisualTree += (_, _) => { model.Drafts.Changed -= Refresh; model.CreationDrafts.Changed -= Refresh; };
        Refresh();
    }
    private void Refresh()
    {
        list.Children.Clear();
        foreach (var draft in model.Drafts.Outstanding)
        {
            var summary = draft.Values.Words; if (summary.Length > 65) summary = summary[..65] + "…";
            var button = Ui.DialogButton($"{draft.Kind} · {summary}", () => Host?.Show(new DraftEditorDialog(model, draft)));
            button.Name = "ReviewDraft_" + draft.Identity; button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Left;
            list.Children.Add(button);
        }
        foreach (var form in model.CreationDrafts.Outstanding)
        {
            var button = Ui.DialogButton($"New {(form.Meeting ? "meeting" : "note")} · {(form.Context == "notes" ? "Notes page" : "Timeline")}", () => { Close(); openComposer(form.Context, form.Meeting); });
            button.HorizontalAlignment = HorizontalAlignment.Stretch; list.Children.Add(button);
        }
        if (list.Children.Count == 0) list.Children.Add(Ui.Text("No unfinished edits", 13, "muted"));
    }
    private void Save()
    {
        var additions = model.CreationDrafts.Outstanding;
        var invalid = additions.Select(f => model.CreationDrafts.Validate(f.Meeting, f.Values)).FirstOrDefault(p => p is not null);
        if (invalid is not null) { error.Text = invalid; return; }
        var results = model.Drafts.SaveAll();
        if (results.FirstOrDefault(r => r.Result.Outcome is not (DraftSaveOutcome.Saved or DraftSaveOutcome.NoChanges or DraftSaveOutcome.PendingPersistence)).Result is { } failure)
        { error.Text = failure.Errors.FirstOrDefault()?.Message ?? failure.Message ?? "Review the unfinished edits."; Refresh(); return; }
        foreach (var form in additions)
            if (model.CreationDrafts.Save(form.Context, form.Meeting) is { } problem) { error.Text = problem; Refresh(); return; }
        if (model.Drafts.HasUnsubmittedChanges || model.CreationDrafts.Outstanding.Count > 0) { error.Text = "Some edits still need attention."; Refresh(); return; }
        Close(); quit?.Invoke();
    }
}

public sealed class DraftEditorDialog : Dialog
{
    public DraftEditorDialog(TimerModel model, RecordDraft draft)
    {
        Title = draft.Kind + " draft"; Width = 640; Height = 520;
        var editor = new RecordEditor(model, model.Drafts.Acquire(draft));
        var body = Ui.Grid("*", "Auto,*"); body.Margin = new Thickness(24, 12, 24, 24); body.RowSpacing = 12;
        Ui.Put(body, editor.HeaderActions); Ui.Put(body, editor, row: 1); Content = body;
    }
}

using Avalonia.Controls;

namespace Timer.Desktop;

/// <summary>Updates a realized row's status without invalidating the virtualized Notes list.</summary>
internal sealed class DraftBadge : TextBlock
{
    public DraftBadge(TimerModel model, long id)
    {
        FontSize = 11; Classes.Add("muted"); Name = "DraftStatus_" + id;
        void Refresh()
        {
            var resolved = model.Notes.Find(id)?.Id ?? id;
            var draft = model.Drafts.NoteDraft(resolved);
            var label = draft?.IsDirty == true ? "Draft" : draft?.PendingPersistence == true ? "Retrying" : "";
            if (Text != label) Text = label; IsVisible = label.Length > 0;
        }
        AttachedToVisualTree += (_, _) => { model.Drafts.Changed += Refresh; Refresh(); };
        DetachedFromVisualTree += (_, _) => model.Drafts.Changed -= Refresh;
        Refresh();
    }
}

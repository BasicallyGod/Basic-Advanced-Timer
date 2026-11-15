using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Timer.Core;

namespace Timer.Desktop;

public sealed partial class SelectedBlockCard
{
    private RecordEditor? recordEditor;
    private string? sharedStackSignature;
    private bool SyncSharedRecord()
    {
        DraftLease? lease = null; DayNote? meeting = null; TimerModel.SelectedBlock? block = null;
        if ((model.SelectedMeetingId ?? model.SelectedNoteId) is { } id && model.Notes.Find(id) is { } note)
        { lease = model.Drafts.AcquireNote(id); if (NotesModel.IsMeeting(note)) meeting = note; }
        else if (model.SelectedBlocks is { Count: 1 } blocks && !blocks[0].IsGroup && blocks[0].Row is { } row)
        { block = blocks[0]; lease = model.Drafts.AcquireBlock(row); }
        if (lease is null)
        {
            if (recordEditor is not null) { recordEditor.Release(); recordEditor = null; shown = null; current = null; }
            return false;
        }
        var items = block is null ? null : ReviewViews.BlockStackOf(model, block.Rows);
        var stack = meeting is null ? null : ReviewViews.MeetingStackOf(model, meeting);
        var stackSignature = model.CurrentTheme.ToString() + "#" + (items is null ? "" : string.Join("|", items.Select(i => $"{i.Account};{i.Start.Ticks};{i.End.Ticks};{i.Duration.Ticks};{i.Recording};{model.IsSelected(i.Sources)}")))
            + "#" + (stack is null ? "" : string.Join("|", stack.Select(n => $"{n.Id};{n.Day.Ticks};{n.At};{n.End};{n.Text}")))
            + (items is null && stack is null ? "" : "#" + model.CurrentTheme.Colour("Ink") + "#" + model.CurrentTheme.Colour("Hover") + "#" + string.Join("|", model.ReviewAccounts.Select(a => a.Name + ";" + a.Colour)));
        if (recordEditor?.Draft == lease.Draft)
        {
            lease.Dispose(); current = block;
            recordEditor.Refresh();
            if (sharedStackSignature != stackSignature)
            { sharedStackSignature = stackSignature; if (meeting is not null) ShowMeetingStack(meeting, stack); else if (block is not null) ShowStack(items); }
            return true;
        }
        recordEditor?.Release(); current = block; shown = null; sharedStackSignature = stackSignature;
        var kind = lease.Draft.Kind;
        var editorDraft = lease.Draft;
        var (header, actions) = Header(kind == DraftKind.Block ? "Block" : kind == DraftKind.Meeting ? "Meeting" : "Note");
        void Delete()
        {
            if (editorDraft.NoteId is { } noteId)
            {
                if (kind == DraftKind.Meeting && editorDraft.AllLater) model.Notes.Remove(noteId, true);
                else model.RemoveNote(noteId);
            }
            else model.DeleteSelection();
        }
        var editor = new RecordEditor(model, lease, delete: Delete); recordEditor = editor;
        if (kind == DraftKind.Block) actions.Children.Add(Ui.Ctl("CopySelected", "Copy", IconKind.Copy, "Copy", () => model.CopySelection(), small: true, key: "Ctrl C"));
        actions.Children.Add(editor.HeaderActions);
        stackHost = new Border { Name = "StackHost", IsVisible = false };
        var content = Ui.Grid("*", "Auto,*"); content.RowSpacing = 10; Ui.Put(content, stackHost); Ui.Put(content, editor, row: 1);
        var page = Ui.Grid("*", "Auto,*"); page.RowSpacing = 12; Ui.Put(page, header); Ui.Put(page, content, row: 1); Child = page;
        if (meeting is not null) ShowMeetingStack(meeting, stack); else if (block is not null) ShowStack(items);
        return true;
    }
}

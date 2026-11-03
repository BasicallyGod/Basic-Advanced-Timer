using Avalonia.Controls;

namespace Timer.Desktop;

/// <summary>The same timesheet data and result handling for Settings and Week overview.</summary>
public static class TimesheetActions
{
    public static string Table(TimerModel model, DateTime week) => ReviewViews.WeekTableFor(model, week).Table.ToTsv();

    public static string Export(TimerModel model, bool month, DateTime? period, out bool success)
    {
        success = model.ExportWeek(month, period);
        return success ? "Both files saved to " + model.ExportPath : model.LastError ?? model.EditError ?? "The files could not be saved.";
    }

    public static async Task<(bool Success, string Message)> Copy(TopLevel? owner, string text)
    {
        try
        {
            if (owner?.Clipboard is not { } clipboard) return (false, "The clipboard could not be reached.");
            await clipboard.SetTextAsync(text);
            return (true, "Copied. Paste it into a timesheet.");
        }
        catch (Exception) { return (false, "The hours table could not be copied. Try again."); }
    }
}

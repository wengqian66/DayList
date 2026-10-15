namespace DayList.Core;

/// <summary>
/// Checkbox events from the task row. WinUI raises Checked/Unchecked when a
/// binding writes IsChecked, including the first time an already-done row is
/// materialized. That is not a user edit.
/// </summary>
public static class TaskCheckInteraction
{
    public static bool DoneFlagChanged(bool wasDone, bool isChecked) => wasDone != isChecked;
}

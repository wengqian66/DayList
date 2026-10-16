namespace DayList.Core;

public readonly record struct PixelRect(int X, int Y, int Width, int Height);

/// <summary>
/// Bottom-right placement for a reminder card inside a monitor work area.
/// Stack index 0 sits on the corner; later cards step upward.
/// </summary>
public static class ReminderCorner
{
    public const int Margin = 16;
    public const int StackGap = 10;

    public static PixelRect Place(PixelRect work, int width, int height, int stackIndex)
    {
        if (width < 1)
            width = 1;
        if (height < 1)
            height = 1;
        if (stackIndex < 0)
            stackIndex = 0;

        var x = work.X + work.Width - width - Margin;
        var y = work.Y + work.Height - height - Margin - stackIndex * (height + StackGap);
        if (x < work.X)
            x = work.X;
        if (y < work.Y)
            y = work.Y;
        return new PixelRect(x, y, width, height);
    }
}

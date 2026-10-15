namespace DayList.Core;

/// <summary>
/// Scroll geometry for the reminder hour and minute wheels.
/// Two spacer rows above and below let the first and last values sit in the center band.
/// </summary>
public static class ReminderWheelLayout
{
    public const double ItemHeight = 36;
    public const int SpacerRows = 2;
    public const int VisibleRows = 5;

    public static double BandHeight => ItemHeight * VisibleRows;

    public static int SlotCount(int valueCount) => valueCount + SpacerRows * 2;

    public static double Extent(int valueCount) => SlotCount(valueCount) * ItemHeight;

    public static int IndexForValue(int value) => value + SpacerRows;

    public static int? ValueAtOffset(double verticalOffset, double viewportHeight, int valueCount)
    {
        if (valueCount <= 0 || viewportHeight <= 0 || double.IsNaN(verticalOffset))
            return null;

        var raw = (verticalOffset + viewportHeight / 2) / ItemHeight - 0.5;
        var index = (int)Math.Round(raw, MidpointRounding.AwayFromZero);
        var value = index - SpacerRows;
        if (value < 0 || value >= valueCount)
            return null;

        return value;
    }

    public static double OffsetForValue(int value, double viewportHeight, double extentHeight, int valueCount)
    {
        if (valueCount <= 0)
            return 0;

        if (value < 0)
            value = 0;
        else if (value >= valueCount)
            value = valueCount - 1;

        var target = IndexForValue(value) * ItemHeight - (viewportHeight - ItemHeight) / 2.0;
        var max = Math.Max(0, extentHeight - viewportHeight);
        if (target < 0)
            return 0;
        if (target > max)
            return max;
        return target;
    }
}

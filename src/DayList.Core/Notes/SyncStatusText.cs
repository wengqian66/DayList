namespace DayList.Core;

public enum SyncPhase
{
    Idle,
    Saved,
    Error
}

public static class SyncStatusText
{
    public static string Format(SyncPhase phase, DateTimeOffset? savedAt, DateTimeOffset now)
    {
        if (phase == SyncPhase.Error)
            return "写入未成功";

        if (phase == SyncPhase.Saved && savedAt is DateTimeOffset at)
            return "已写入笔记库 · " + Relative(at, now);

        return "已连接笔记库";
    }

    public static string Relative(DateTimeOffset at, DateTimeOffset now)
    {
        var delta = now - at;
        if (delta < TimeSpan.Zero)
            delta = TimeSpan.Zero;

        if (delta < TimeSpan.FromSeconds(45))
            return "刚刚";

        if (delta < TimeSpan.FromHours(1))
        {
            var minutes = Math.Max(1, (int)delta.TotalMinutes);
            return minutes + " 分钟前";
        }

        var clock = at.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        if (at.Date == now.Date)
            return clock;

        return at.Month + "月" + at.Day + "日 " + clock;
    }
}

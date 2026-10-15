using System.Globalization;
using System.Text;

namespace DayList.Core;

public static class MonthDigest
{
    public static string Build(AppSettings settings, DateOnly anyDayInMonth)
    {
        var first = new DateOnly(anyDayInMonth.Year, anyDayInMonth.Month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        var builder = new StringBuilder();
        builder.Append("# ").Append(first.ToString("yyyy-MM", CultureInfo.InvariantCulture)).Append(" DayList\n\n");
        var any = false;

        for (var day = first; day <= last; day = day.AddDays(1))
        {
            var snapshot = DailyNoteStore.Read(settings, day);
            if (!snapshot.FileExists || !snapshot.SectionFound)
                continue;

            var body = InspirationSection.Strip(snapshot.SectionBody, settings.InspirationHeading).Trim();
            if (body.Length == 0)
                continue;

            any = true;
            builder.Append("## ").Append(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append("\n\n");
            builder.Append(body).Append("\n\n");
        }

        if (!any)
            return "";

        return builder.ToString().TrimEnd() + "\n";
    }

    public static string WrapReport(DateOnly month, string summary, DateTimeOffset generatedAt)
    {
        var stamp = generatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var body = ChatProtocol.StripFences(summary).Trim();
        var title = month.ToString("yyyy-MM", CultureInfo.InvariantCulture) + " 月报";
        return $"# {title}\n\n> DayList 生成于 {stamp}。材料来自本月每日笔记里的 DayList 小节。\n\n{body}\n";
    }
}

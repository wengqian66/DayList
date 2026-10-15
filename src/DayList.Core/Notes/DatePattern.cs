using System.Globalization;
using System.Text;

namespace DayList.Core;

public static class DatePattern
{
    public static string ToDotNet(string? pattern)
    {
        var source = string.IsNullOrWhiteSpace(pattern) ? "yyyy-MM-dd" : pattern.Trim();
        var builder = new StringBuilder(source.Length);
        var index = 0;
        while (index < source.Length)
        {
            if (char.IsLetter(source[index]))
            {
                var start = index;
                while (index < source.Length && char.IsLetter(source[index]))
                    index++;

                var token = source[start..index];
                builder.Append(token switch
                {
                    "YYYY" => "yyyy",
                    "YY" => "yy",
                    "DD" => "dd",
                    "D" => "d",
                    _ => token
                });
            }
            else
            {
                builder.Append(source[index]);
                index++;
            }
        }

        return builder.ToString();
    }

    public static string Format(DateOnly date, string? pattern)
    {
        var source = string.IsNullOrWhiteSpace(pattern) ? "yyyy-MM-dd" : pattern.Trim();
        var moment = date.ToDateTime(TimeOnly.MinValue);
        var builder = new StringBuilder(source.Length);
        var index = 0;
        while (index < source.Length)
        {
            if (!char.IsLetter(source[index]))
            {
                builder.Append(source[index]);
                index++;
                continue;
            }

            var start = index;
            while (index < source.Length && char.IsLetter(source[index]))
                index++;

            builder.Append(Expand(moment, source[start..index], source));
        }

        return builder.ToString();
    }

    private static string Expand(DateTime moment, string token, string pattern)
    {
        var format = token switch
        {
            "YYYY" or "yyyy" => "yyyy",
            "YY" or "yy" => "yy",
            "Y" or "y" => "yyyy",
            "MMMM" => "MMMM",
            "MMM" => "MMM",
            "MM" => "MM",
            "M" => "%M",
            "dddd" => "dddd",
            "ddd" => "ddd",
            "DD" or "dd" => "dd",
            "D" or "d" => "%d",
            _ => null
        };

        if (format is null)
        {
            if (IsRepeatedDateLetter(token))
                throw new FormatException($"无法使用文件名格式“{pattern}”。");
            return token;
        }

        return moment.ToString(format, CultureInfo.InvariantCulture);
    }

    private static bool IsRepeatedDateLetter(string token)
    {
        var first = token[0];
        if (first is not ('y' or 'Y' or 'M' or 'D' or 'd'))
            return false;

        foreach (var ch in token)
        {
            if (ch != first)
                return false;
        }

        return true;
    }
}

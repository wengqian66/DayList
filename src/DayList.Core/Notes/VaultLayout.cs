namespace DayList.Core;

public static class VaultLayout
{
    public static string DailyRelative(AppSettings settings, DateOnly date)
    {
        settings.Normalize();
        var folder = settings.DailyFolder;
        var name = DatePattern.Format(date, settings.DailyFilePattern).Replace('\\', '/').Trim('/');
        if (name.Length == 0)
            throw new InvalidOperationException("文件名格式没有生成文件名。");
        if (!name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            name += ".md";

        var relative = folder.Length == 0 ? name : folder + "/" + name;
        EnsureInside(relative);
        return relative;
    }

    public static (string FullPath, string RelativePath) Daily(AppSettings settings, DateOnly date)
    {
        var relative = DailyRelative(settings, date);
        return (Combine(settings.VaultPath, relative), relative);
    }

    public static string ReportRelative(AppSettings settings, DateOnly day)
    {
        settings.Normalize();
        var folder = settings.ReportFolder;
        var name = DatePattern.Format(day, settings.ReportFilePattern).Replace('\\', '/').Trim('/');
        if (name.Length == 0)
            throw new InvalidOperationException("月报文件名格式没有生成文件名。");
        if (!name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            name += ".md";

        if (folder.Equals(".obsidian", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("月报不能写进 .obsidian 配置目录。");

        var relative = folder.Length == 0 ? name : folder + "/" + name;
        EnsureInside(relative);
        if (relative.StartsWith(".obsidian/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("月报不能写进 .obsidian 配置目录。");
        return relative;
    }

    public static (string FullPath, string RelativePath) Report(AppSettings settings, DateOnly day)
    {
        var relative = ReportRelative(settings, day);
        return (Combine(settings.VaultPath, relative), relative);
    }

    public static string Combine(string vaultPath, string relative)
    {
        if (string.IsNullOrWhiteSpace(vaultPath))
            throw new InvalidOperationException("还没有选择 Obsidian 库。");

        var vaultFull = Path.GetFullPath(vaultPath);
        var combined = Path.GetFullPath(Path.Combine(vaultFull, relative.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = vaultFull.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("路径超出了 Obsidian 库：" + relative);
        return combined;
    }

    private static void EnsureInside(string relative)
    {
        if (relative.Length == 0)
            throw new InvalidOperationException("笔记的相对路径是空的。");

        foreach (var part in relative.Split('/'))
        {
            if (part.Length == 0 || part == "." || part == "..")
                throw new InvalidOperationException("路径不能跳出库：" + relative);
            if (part.Contains(':'))
                throw new InvalidOperationException("路径里有不能用于文件名的字符：" + relative);
            if (part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidOperationException("路径里有不能用于文件名的字符：" + relative);
        }
    }
}

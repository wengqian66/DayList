using System.Security.Cryptography;
using System.Text;

namespace DayList.Core;

public sealed record DailyNoteSnapshot(
    string FullPath,
    string RelativePath,
    bool FileExists,
    bool SectionFound,
    string SectionBody,
    IReadOnlyList<DayTask> Tasks,
    string ContentHash,
    IReadOnlyList<InspirationEntry> Inspirations);

public sealed record WriteResult(string FullPath, string RelativePath, string ContentHash, bool Wrote);

public static class ContentHash
{
    public static string Of(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}

public static class DailyNoteStore
{
    public static DailyNoteSnapshot Read(AppSettings settings, DateOnly date)
    {
        var (fullPath, relative) = VaultLayout.Daily(settings, date);
        if (!File.Exists(fullPath))
        {
            return new DailyNoteSnapshot(
                fullPath,
                relative,
                false,
                false,
                "",
                Array.Empty<DayTask>(),
                ContentHash.Of(ReadOnlySpan<byte>.Empty),
                Array.Empty<InspirationEntry>());
        }

        var bytes = File.ReadAllBytes(fullPath);
        var (text, _) = Decode(bytes);
        var document = DailyNoteDocument.Parse(text, settings.SectionHeading);
        var inspirations = ReadInspirations(text, settings);
        return new DailyNoteSnapshot(
            fullPath,
            relative,
            true,
            document.SectionFound,
            document.SectionBody(),
            document.Tasks.Select(task => task.Copy()).ToArray(),
            ContentHash.Of(bytes),
            inspirations);
    }

    public static WriteResult WriteInspirations(AppSettings settings, DateOnly date, IReadOnlyList<InspirationEntry> entries)
    {
        if (InspirationSection.SameHeading(settings.SectionHeading, settings.InspirationHeading))
            throw new InvalidOperationException("灵感小节不能和待办小节使用同一个标题。");

        var (fullPath, relative) = VaultLayout.Daily(settings, date);
        var exists = File.Exists(fullPath);
        var bom = false;
        var original = "";
        byte[] existing = Array.Empty<byte>();
        if (exists)
        {
            existing = File.ReadAllBytes(fullPath);
            (original, bom) = Decode(existing);
        }

        if (!exists && entries.Count == 0)
            return new WriteResult(fullPath, relative, ContentHash.Of(ReadOnlySpan<byte>.Empty), false);

        var rendered = InspirationSection.Render(original, settings.InspirationHeading, entries);
        if (exists && string.Equals(rendered, original, StringComparison.Ordinal))
            return new WriteResult(fullPath, relative, ContentHash.Of(existing), false);

        if (!exists && rendered.Length == 0)
            return new WriteResult(fullPath, relative, ContentHash.Of(ReadOnlySpan<byte>.Empty), false);

        var bytes = Encode(rendered, bom);
        AtomicWrite(fullPath, bytes);
        return new WriteResult(fullPath, relative, ContentHash.Of(bytes), true);
    }

    private static IReadOnlyList<InspirationEntry> ReadInspirations(string text, AppSettings settings)
    {
        if (InspirationSection.SameHeading(settings.SectionHeading, settings.InspirationHeading))
            return Array.Empty<InspirationEntry>();

        return InspirationSection.Read(text, settings.InspirationHeading)
            .Select(entry => new InspirationEntry
            {
                TimeLabel = entry.TimeLabel,
                Text = entry.Text,
                RawLine = entry.RawLine
            })
            .ToArray();
    }

    public static WriteResult Write(AppSettings settings, DateOnly date, IReadOnlyList<DayTask> tasks)
    {
        var (fullPath, relative) = VaultLayout.Daily(settings, date);
        var exists = File.Exists(fullPath);
        var bom = false;
        var original = "";
        byte[] existing = Array.Empty<byte>();
        if (exists)
        {
            existing = File.ReadAllBytes(fullPath);
            (original, bom) = Decode(existing);
        }

        if (!exists && tasks.Count == 0)
        {
            return new WriteResult(fullPath, relative, ContentHash.Of(ReadOnlySpan<byte>.Empty), false);
        }

        var rendered = DailyNoteDocument.Parse(original, settings.SectionHeading).Render(tasks);
        if (exists && string.Equals(rendered, original, StringComparison.Ordinal))
            return new WriteResult(fullPath, relative, ContentHash.Of(existing), false);

        var bytes = Encode(rendered, bom);
        AtomicWrite(fullPath, bytes);
        return new WriteResult(fullPath, relative, ContentHash.Of(bytes), true);
    }

    public static WriteResult RewriteSectionHeading(AppSettings settings, DateOnly date, string? oldHeadingSetting)
    {
        var (fullPath, relative) = VaultLayout.Daily(settings, date);
        if (!File.Exists(fullPath))
            return new WriteResult(fullPath, relative, ContentHash.Of(ReadOnlySpan<byte>.Empty), false);

        var existing = File.ReadAllBytes(fullPath);
        var (original, bom) = Decode(existing);
        var (changed, rendered) = DailyNoteDocument.TryRenameSectionTitle(original, oldHeadingSetting, HeadingSpec.Parse(settings.SectionHeading).Text);
        if (!changed)
            return new WriteResult(fullPath, relative, ContentHash.Of(existing), false);

        var bytes = Encode(rendered, bom);
        AtomicWrite(fullPath, bytes);
        return new WriteResult(fullPath, relative, ContentHash.Of(bytes), true);
    }

    private static (string Text, bool Bom) Decode(byte[] bytes)
    {
        var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var text = Encoding.UTF8.GetString(bom ? bytes.AsSpan(3) : bytes);
        return (text, bom);
    }

    private static byte[] Encode(string text, bool bom)
    {
        var body = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);
        if (!bom)
            return body;

        var bytes = new byte[body.Length + 3];
        bytes[0] = 0xEF;
        bytes[1] = 0xBB;
        bytes[2] = 0xBF;
        body.CopyTo(bytes, 3);
        return bytes;
    }

    private static void AtomicWrite(string path, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temp = path + ".daylist.tmp";
        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try
                {
                    File.Delete(temp);
                }
                catch (IOException)
                {
                    // The replace already consumed the temp file.
                }
            }
        }
    }
}

namespace DayList.Core;

/// <summary>
/// A file referenced from a task line. The path is vault-relative and lives under
/// <see cref="AttachmentVault.FolderName"/> so Obsidian can open the wiki link.
/// </summary>
public sealed class TaskAttachment
{
    public string RelativePath { get; init; } = "";

    public string DisplayName { get; init; } = "";

    public bool IsImage { get; init; }

    /// <summary>
    /// Exact wiki or Markdown token from the task line. Kept so a checkbox toggle
    /// does not rewrite a link the user wrote by hand.
    /// </summary>
    public string? OriginalToken { get; init; }

    public TaskAttachment Copy() => new()
    {
        RelativePath = RelativePath,
        DisplayName = DisplayName,
        IsImage = IsImage,
        OriginalToken = OriginalToken
    };

    public string ToCanonicalToken()
    {
        var path = (RelativePath ?? "").Replace('\\', '/');
        var fileName = Path.GetFileName(path);
        var alias = (DisplayName ?? "").Trim();
        var inner = alias.Length > 0 && !string.Equals(alias, fileName, StringComparison.Ordinal)
            ? path + "|" + alias
            : path;
        return IsImage ? "![[" + inner + "]]" : "[[" + inner + "]]";
    }

    public static bool SameSequence(IReadOnlyList<TaskAttachment>? left, IReadOnlyList<TaskAttachment>? right)
    {
        var a = left ?? Array.Empty<TaskAttachment>();
        var b = right ?? Array.Empty<TaskAttachment>();
        if (a.Count != b.Count)
            return false;

        for (var i = 0; i < a.Count; i++)
        {
            if (!string.Equals(Normalize(a[i].RelativePath), Normalize(b[i].RelativePath), StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.Equals(a[i].OriginalToken, b[i].OriginalToken, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static string Normalize(string? path) =>
        (path ?? "").Replace('\\', '/').Trim('/');
}

public static class AttachmentKinds
{
    public const long MaxBytes = 20L * 1024 * 1024;

    private static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp"
    };

    private static readonly HashSet<string> Documents = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".txt", ".md", ".csv", ".rtf", ".odt", ".ods", ".odp", ".zip"
    };

    public static IReadOnlyList<string> PickerFilters { get; } =
        Images.Concat(Documents).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray();

    public static bool IsAllowed(string? extension) =>
        Includes(Images, extension) || Includes(Documents, extension);

    public static bool IsImage(string? extension) => Includes(Images, extension);

    private static bool Includes(HashSet<string> set, string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return false;
        var dotted = extension.StartsWith('.') ? extension : "." + extension;
        return set.Contains(dotted);
    }
}

/// <summary>
/// Pulls DayList attachment links out of a task line and writes them back.
/// Other wiki links and the rest of the line stay in the task text.
/// </summary>
public static class TaskAttachmentSyntax
{
    private const char Marker = '\u0001';

    public static (string Text, List<TaskAttachment> Attachments) Split(string? raw)
    {
        var attachments = new List<TaskAttachment>();
        if (string.IsNullOrEmpty(raw))
            return ("", attachments);

        var marked = new System.Text.StringBuilder(raw.Length);
        var index = 0;
        while (index < raw.Length)
        {
            if (TryReadLink(raw, index, out var length, out var attachment))
            {
                if (attachment is not null)
                {
                    attachments.Add(attachment);
                    marked.Append(Marker);
                }
                else
                {
                    marked.Append(raw, index, length);
                }

                index += length;
                continue;
            }

            marked.Append(raw[index]);
            index++;
        }

        var text = attachments.Count == 0 ? raw : CollapseMarkers(marked.ToString());
        return (text, attachments);
    }

    public static string Compose(string? text, IReadOnlyList<TaskAttachment>? attachments)
    {
        var body = text ?? "";
        if (attachments is null || attachments.Count == 0)
            return body;

        var links = string.Join(
            " ",
            attachments.Select(item =>
                string.IsNullOrEmpty(item.OriginalToken) ? item.ToCanonicalToken() : item.OriginalToken));
        if (body.TrimEnd().Length == 0)
            return links;
        return body.TrimEnd() + " " + links;
    }

    private static bool TryReadLink(string raw, int index, out int length, out TaskAttachment? attachment)
    {
        attachment = null;
        length = 0;
        if ((uint)index >= (uint)raw.Length)
            return false;

        var start = index;
        if (raw[index] == '!')
        {
            if (index + 1 >= raw.Length || raw[index + 1] != '[')
                return false;
            index++;
        }

        if (raw[index] != '[')
            return false;

        if (index + 1 < raw.Length && raw[index + 1] == '[')
        {
            var close = raw.IndexOf("]]", index + 2, StringComparison.Ordinal);
            if (close < 0)
                return false;
            length = close + 2 - start;
            var body = raw.Substring(index + 2, close - (index + 2));
            var token = raw.Substring(start, length);
            attachment = TryFromWiki(body, token);
            return true;
        }

        var labelEnd = raw.IndexOf(']', index + 1);
        if (labelEnd < 0 || labelEnd + 1 >= raw.Length || raw[labelEnd + 1] != '(')
            return false;
        var urlEnd = raw.IndexOf(')', labelEnd + 2);
        if (urlEnd < 0)
            return false;
        length = urlEnd + 1 - start;
        var alt = raw.Substring(index + 1, labelEnd - (index + 1));
        var url = raw.Substring(labelEnd + 2, urlEnd - (labelEnd + 2));
        var markdown = raw.Substring(start, length);
        attachment = TryFromMarkdown(url, alt, markdown);
        return true;
    }

    private static TaskAttachment? TryFromWiki(string body, string token)
    {
        var target = body;
        string? alias = null;
        var pipe = body.IndexOf('|');
        if (pipe >= 0)
        {
            target = body[..pipe];
            alias = body[(pipe + 1)..];
        }

        var hash = target.IndexOf('#');
        if (hash >= 0)
            target = target[..hash];

        if (!TryNormalizeRelative(target, out var relative))
            return null;

        return Create(relative, alias, token);
    }

    private static TaskAttachment? TryFromMarkdown(string url, string alt, string token)
    {
        var target = url.Trim();
        var space = target.IndexOfAny(new[] { ' ', '\t' });
        if (space >= 0)
            target = target[..space];
        if (target.Length >= 2 && target[0] == '<' && target[^1] == '>')
            target = target[1..^1].Trim();

        if (!TryNormalizeRelative(target, out var relative))
            return null;

        return Create(relative, alt, token);
    }

    private static TaskAttachment Create(string relative, string? alias, string token)
    {
        var fileName = Path.GetFileName(relative);
        return new TaskAttachment
        {
            RelativePath = relative,
            DisplayName = ChooseDisplayName(fileName, alias),
            IsImage = AttachmentKinds.IsImage(Path.GetExtension(fileName)),
            OriginalToken = token
        };
    }

    private static string ChooseDisplayName(string fileName, string? alias)
    {
        var trimmed = (alias ?? "").Trim();
        if (trimmed.Length == 0 || trimmed.All(char.IsDigit))
            return fileName;
        return trimmed;
    }

    internal static bool TryNormalizeRelative(string rawPath, out string relative)
    {
        relative = "";
        var path = (rawPath ?? "").Trim().Replace('\\', '/');
        if (path.Length == 0 || path.Contains("://", StringComparison.Ordinal))
            return false;

        try
        {
            path = Uri.UnescapeDataString(path);
        }
        catch (UriFormatException)
        {
            return false;
        }

        path = path.Replace('\\', '/');
        var parts = new List<string>();
        foreach (var part in path.Split('/'))
        {
            if (part.Length == 0 || part == ".")
                continue;
            if (part == "..")
            {
                if (parts.Count > 0)
                    parts.RemoveAt(parts.Count - 1);
                continue;
            }

            parts.Add(part);
        }

        if (parts.Count < 2)
            return false;
        if (!parts[0].Equals(AttachmentVault.FolderName, StringComparison.OrdinalIgnoreCase))
            return false;

        parts[0] = AttachmentVault.FolderName;
        relative = string.Join('/', parts);
        return true;
    }

    private static string CollapseMarkers(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != Marker)
            {
                builder.Append(value[i]);
                continue;
            }

            var next = i + 1;
            while (next < value.Length && value[next] == Marker)
                next++;

            var prevSpace = builder.Length > 0 && char.IsWhiteSpace(builder[^1]);
            var nextSpace = next < value.Length && char.IsWhiteSpace(value[next]);
            if (prevSpace && next >= value.Length)
            {
                while (builder.Length > 0 && char.IsWhiteSpace(builder[^1]))
                    builder.Length--;
                i = next - 1;
                continue;
            }

            if (prevSpace && nextSpace)
            {
                while (next < value.Length && char.IsWhiteSpace(value[next]))
                    next++;
                i = next - 1;
                continue;
            }

            if (!prevSpace && nextSpace)
            {
                if (builder.Length > 0)
                    builder.Append(' ');
                while (next < value.Length && char.IsWhiteSpace(value[next]))
                    next++;
                i = next - 1;
                continue;
            }
        }

        return builder.ToString();
    }
}

namespace DayList.Core;

/// <summary>
/// Files copied for one task. <see cref="Error"/> is the last failure;
/// earlier successes stay in <see cref="Added"/>.
/// </summary>
public sealed class AttachmentImportBatch
{
    public List<TaskAttachment> Added { get; } = new();

    public string? Error { get; set; }
}

/// <summary>
/// Task files are copied into the Obsidian vault at
/// <c>DayList-attachments/yyyy-MM-dd/</c> (vault root, not beside the daily note).
/// The folder name is owned by DayList so it does not sit inside a user's note folder,
/// and a wiki link from that root path opens in Obsidian no matter where the daily note lives.
/// Removing a task or an attachment deletes the file only after every Markdown note
/// outside this folder has been scanned and none of them still mention the path or the file name.
/// If the scan cannot finish, the file stays.
/// </summary>
public static class AttachmentVault
{
    public const string FolderName = "DayList-attachments";

    public static string DayFolderRelative(DateOnly date) =>
        FolderName + "/" + date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public static bool IsManagedRelativePath(string? relativePath)
    {
        var normalized = (relativePath ?? "").Replace('\\', '/').Trim('/');
        if (!normalized.StartsWith(FolderName + "/", StringComparison.OrdinalIgnoreCase))
            return false;
        return !normalized.Split('/').Any(part => part.Length == 0 || part == "." || part == "..");
    }

    public static TaskAttachment Import(string vaultPath, DateOnly date, string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(vaultPath) || !Directory.Exists(vaultPath))
            throw new InvalidOperationException("还没有选择 Obsidian 库。");
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            throw new InvalidOperationException("找不到这个文件。");

        var extension = Path.GetExtension(sourcePath);
        if (!AttachmentKinds.IsAllowed(extension))
        {
            var shown = string.IsNullOrEmpty(extension) ? "无扩展名" : extension;
            throw new InvalidOperationException("不支持这种附件（" + shown + "）。");
        }

        var sourceFull = Path.GetFullPath(sourcePath);
        var length = new FileInfo(sourceFull).Length;
        if (length > AttachmentKinds.MaxBytes)
            throw new InvalidOperationException("附件超过 20MB，没有添加。");

        var folderRelative = DayFolderRelative(date);
        var folderFull = VaultLayout.Combine(vaultPath, folderRelative);
        Directory.CreateDirectory(folderFull);

        var safeName = SanitizeFileName(Path.GetFileName(sourceFull));
        var destName = UniqueName(folderFull, safeName, sourceFull, length);
        var destFull = Path.Combine(folderFull, destName);
        if (!File.Exists(destFull))
            File.Copy(sourceFull, destFull, overwrite: false);

        var relative = folderRelative + "/" + destName;
        var attachment = new TaskAttachment
        {
            RelativePath = relative,
            DisplayName = destName,
            IsImage = AttachmentKinds.IsImage(Path.GetExtension(destName)),
            OriginalToken = null
        };
        return new TaskAttachment
        {
            RelativePath = attachment.RelativePath,
            DisplayName = attachment.DisplayName,
            IsImage = attachment.IsImage,
            OriginalToken = attachment.ToCanonicalToken()
        };
    }

    /// <summary>
    /// Copies several files with the same rules as <see cref="Import"/>.
    /// A folder, a duplicate, or a rejected file does not stop the rest.
    /// </summary>
    public static AttachmentImportBatch ImportMany(
        string vaultPath,
        DateOnly date,
        IEnumerable<string> sourcePaths,
        IEnumerable<string>? alreadyAttached = null)
    {
        var batch = new AttachmentImportBatch();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (alreadyAttached is not null)
        {
            foreach (var path in alreadyAttached)
            {
                var key = NormalizeRelative(path);
                if (key.Length > 0)
                    seen.Add(key);
            }
        }

        foreach (var path in sourcePaths)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                {
                    batch.Error = "只能附加文件。";
                    continue;
                }

                var imported = Import(vaultPath, date, path);
                if (!seen.Add(NormalizeRelative(imported.RelativePath)))
                {
                    batch.Error = "这个文件已经附在这条任务上。";
                    continue;
                }

                batch.Added.Add(imported);
            }
            catch (Exception ex)
            {
                batch.Error = string.IsNullOrWhiteSpace(ex.Message) ? "没有添加这个附件。" : ex.Message;
            }
        }

        return batch;
    }

    /// <summary>
    /// Copies managed attachment files into <paramref name="date"/>'s folder and
    /// returns attachments whose path and wiki token point at that day.
    /// The previous day's files stay in place; that note still links them.
    /// A file that cannot be copied is returned unchanged.
    /// </summary>
    public static IReadOnlyList<TaskAttachment> CopyIntoDay(
        string vaultPath,
        DateOnly date,
        IEnumerable<TaskAttachment>? attachments)
    {
        if (attachments is null)
            return Array.Empty<TaskAttachment>();

        var source = attachments as IReadOnlyCollection<TaskAttachment> ?? attachments.ToList();
        if (source.Count == 0)
            return Array.Empty<TaskAttachment>();

        if (string.IsNullOrWhiteSpace(vaultPath) || !Directory.Exists(vaultPath))
        {
            var kept = new List<TaskAttachment>(source.Count);
            foreach (var item in source)
            {
                if (item is not null)
                    kept.Add(item.Copy());
            }

            return kept;
        }

        var copied = new List<TaskAttachment>(source.Count);
        foreach (var attachment in source)
        {
            if (attachment is null || string.IsNullOrWhiteSpace(attachment.RelativePath))
                continue;
            copied.Add(CopyOneIntoDay(vaultPath, date, attachment));
        }

        return copied;
    }

    private static TaskAttachment CopyOneIntoDay(string vaultPath, DateOnly date, TaskAttachment source)
    {
        var relative = (source.RelativePath ?? "").Replace('\\', '/').Trim('/');
        string sourceFull;
        try
        {
            sourceFull = VaultLayout.Combine(vaultPath, relative);
        }
        catch (InvalidOperationException)
        {
            return source.Copy();
        }

        if (!File.Exists(sourceFull))
            return source.Copy();

        TaskAttachment imported;
        try
        {
            imported = Import(vaultPath, date, sourceFull);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return source.Copy();
        }

        return RewriteForDay(imported, source);
    }

    private static TaskAttachment RewriteForDay(TaskAttachment imported, TaskAttachment source)
    {
        var relative = imported.RelativePath.Replace('\\', '/');
        var display = PreservedDisplayName(source, relative);
        var token = RewriteToken(source, relative) ?? CanonicalToken(relative, display, imported.IsImage);
        return new TaskAttachment
        {
            RelativePath = relative,
            DisplayName = display,
            IsImage = imported.IsImage,
            OriginalToken = token
        };
    }

    private static string PreservedDisplayName(TaskAttachment source, string newRelative)
    {
        var newFile = Path.GetFileName(newRelative);
        var oldFile = Path.GetFileName((source.RelativePath ?? "").Replace('\\', '/'));
        var alias = (source.DisplayName ?? "").Trim();
        if (alias.Length == 0
            || alias.All(char.IsDigit)
            || string.Equals(alias, oldFile, StringComparison.OrdinalIgnoreCase))
        {
            return newFile;
        }

        return alias;
    }

    private static string? RewriteToken(TaskAttachment source, string newRelative)
    {
        var token = source.OriginalToken;
        if (string.IsNullOrEmpty(token))
            return null;

        var oldRelative = (source.RelativePath ?? "").Replace('\\', '/').Trim('/');
        if (oldRelative.Length == 0)
            return null;

        if (token.Contains(oldRelative, StringComparison.Ordinal))
            return token.Replace(oldRelative, newRelative, StringComparison.Ordinal);

        var encodedOld = EncodePath(oldRelative);
        if (!string.Equals(encodedOld, oldRelative, StringComparison.Ordinal)
            && token.Contains(encodedOld, StringComparison.Ordinal))
        {
            return token.Replace(encodedOld, EncodePath(newRelative), StringComparison.Ordinal);
        }

        return null;
    }

    private static string EncodePath(string relative) =>
        string.Join("/", relative.Split('/').Select(Uri.EscapeDataString));

    private static string CanonicalToken(string relative, string display, bool isImage)
    {
        var draft = new TaskAttachment
        {
            RelativePath = relative,
            DisplayName = display,
            IsImage = isImage
        };
        return draft.ToCanonicalToken();
    }

    private static string NormalizeRelative(string? path) =>
        (path ?? "").Replace('\\', '/').Trim('/');

    /// <summary>
    /// Deletes a managed vault file when no Markdown note still points at it.
    /// Returns false when the file is kept, missing, or the scan could not be trusted.
    /// </summary>
    public static bool TryDeleteIfUnreferenced(string vaultPath, string relativePath)
    {
        try
        {
            if (!IsManagedRelativePath(relativePath))
                return false;
            if (string.IsNullOrWhiteSpace(vaultPath) || !Directory.Exists(vaultPath))
                return false;

            var full = VaultLayout.Combine(vaultPath, relativePath);
            if (!File.Exists(full))
                return false;

            var fileName = Path.GetFileName(relativePath.Replace('\\', '/'));
            if (string.IsNullOrEmpty(fileName))
                return false;
            if (AnyMarkdownReferences(vaultPath, relativePath.Replace('\\', '/'), fileName))
                return false;

            File.Delete(full);
            TryRemoveEmptyDayFolder(Path.GetDirectoryName(full));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string UniqueName(string folderFull, string safeName, string sourceFull, long sourceLength)
    {
        var sourceHash = ContentHash.Of(File.ReadAllBytes(sourceFull));
        var extension = Path.GetExtension(safeName);
        var baseName = Path.GetFileNameWithoutExtension(safeName);
        for (var n = 1; n < 1000; n++)
        {
            var candidate = n == 1 ? safeName : baseName + "-" + n.ToString(System.Globalization.CultureInfo.InvariantCulture) + extension;
            var dest = Path.Combine(folderFull, candidate);
            if (!File.Exists(dest))
                return candidate;
            if (string.Equals(Path.GetFullPath(dest), sourceFull, StringComparison.OrdinalIgnoreCase))
                return candidate;

            var info = new FileInfo(dest);
            if (info.Length == sourceLength && ContentHash.Of(File.ReadAllBytes(dest)) == sourceHash)
                return candidate;
        }

        throw new InvalidOperationException("附件文件名冲突太多，没有添加。");
    }

    private static string SanitizeFileName(string name)
    {
        var extension = Path.GetExtension(name);
        var baseName = Path.GetFileNameWithoutExtension(name);
        if (!AttachmentKinds.IsAllowed(extension))
            extension = "";
        var invalid = Path.GetInvalidFileNameChars();
        var chars = baseName.Select(ch =>
            invalid.Contains(ch) || ch is '#' or '|' or '[' or ']' or '!'
                ? '_'
                : ch).ToArray();
        baseName = new string(chars).Trim().Trim('.');
        if (baseName.Length == 0)
            baseName = "file";
        if (baseName.Length > 80)
            baseName = baseName[..80].TrimEnd('.', ' ');
        if (baseName.Length == 0)
            baseName = "file";
        return baseName + extension.ToLowerInvariant();
    }

    private static bool AnyMarkdownReferences(string vaultPath, string relativePath, string fileName)
    {
        var root = Path.GetFullPath(vaultPath);
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(dir);
            }
            catch
            {
                return true;
            }

            foreach (var entry in entries)
            {
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch
                {
                    return true;
                }

                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    continue;

                var name = Path.GetFileName(entry);
                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    if (name.Equals(".obsidian", StringComparison.OrdinalIgnoreCase)
                        || name.Equals(".git", StringComparison.OrdinalIgnoreCase)
                        || name.Equals(FolderName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    pending.Push(entry);
                    continue;
                }

                if (!name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var info = new FileInfo(entry);
                    if (info.Length > 8L * 1024 * 1024)
                        return true;
                    var text = File.ReadAllText(entry);
                    if (text.Contains(relativePath, StringComparison.OrdinalIgnoreCase)
                        || text.Contains(fileName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void TryRemoveEmptyDayFolder(string? dayFolder)
    {
        if (string.IsNullOrEmpty(dayFolder) || !Directory.Exists(dayFolder))
            return;
        if (Directory.EnumerateFileSystemEntries(dayFolder).Any())
            return;

        var parent = Path.GetFileName(Path.GetDirectoryName(dayFolder) ?? "");
        if (!parent.Equals(FolderName, StringComparison.OrdinalIgnoreCase))
            return;
        Directory.Delete(dayFolder);
    }
}

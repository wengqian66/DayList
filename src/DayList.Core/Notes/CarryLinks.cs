using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DayList.Core;

/// <summary>
/// Obsidian block id stamped on a task that 「带入今日」 copied forward.
/// The id stays at the end of the Markdown line so a later title edit still
/// points at the source. It is not part of the title shown in the app.
/// </summary>
public static class CarryMarker
{
    public const string Prefix = "dl-";

    public const int IdLength = 11;

    public static string NewId() => Prefix + Guid.NewGuid().ToString("N")[..8];

    public static string Normalize(string? carryId)
    {
        if (string.IsNullOrWhiteSpace(carryId))
            return "";

        var trimmed = carryId.Trim();
        if (trimmed.StartsWith('^'))
            trimmed = trimmed[1..];
        if (!IsId(trimmed))
            return "";

        return Prefix + trimmed[Prefix.Length..].ToLowerInvariant();
    }

    public static bool IsId(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length != IdLength)
            return false;
        if (!value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        for (var i = Prefix.Length; i < value.Length; i++)
        {
            var c = value[i];
            if (c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F')
                continue;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Title text with a trailing <c>^dl-xxxxxxxx</c> removed. Other block ids stay in the title.
    /// </summary>
    public static string Visible(string? text) => Split(text).Text.Trim();

    public static (string Text, string CarryId) Split(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return ("", "");

        var end = raw.TrimEnd();
        var marker = end.LastIndexOf("^" + Prefix, StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
            return (raw, "");

        var idPart = end[(marker + 1)..];
        if (!IsId(idPart))
            return (raw, "");
        if (marker > 0 && !char.IsWhiteSpace(end[marker - 1]))
            return (raw, "");

        var body = marker == 0 ? "" : end[..(marker - 1)].TrimEnd();
        return (body, Normalize(idPart));
    }

    public static string Append(string? text, string? carryId)
    {
        var id = Normalize(carryId);
        if (id.Length == 0)
            return text ?? "";

        var body = Split(text).Text.TrimEnd();
        return body.Length == 0 ? "^" + id : body + " ^" + id;
    }
}

/// <summary>
/// One 「带入今日」 hop: the open source line, and the carry id written on the later day.
/// </summary>
public sealed record CarryLink(
    string CarryId,
    DateOnly SourceDate,
    string Category,
    string SourceIdentity,
    string AttachmentKey,
    DateOnly BroughtTo)
{
    public bool PointsAt(OverdueTaskItem item)
    {
        if (SourceDate != item.Date)
            return false;
        if (DayTaskCategory.Normalize(Category) != DayTaskCategory.Normalize(item.Category))
            return false;

        var identity = OverdueIdentity.IdentityText(item.Text, item.Attachments);
        if (identity.Length == 0 || !string.Equals(SourceIdentity ?? "", identity, StringComparison.Ordinal))
            return false;

        if (string.IsNullOrWhiteSpace(item.Text))
        {
            var key = OverdueIdentity.AttachmentKey(item.Attachments);
            if (!string.Equals(AttachmentKey ?? "", key, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    public static CarryLink Create(OverdueTaskItem item, DateOnly broughtTo, string carryId) =>
        new(
            CarryMarker.Normalize(carryId),
            item.Date,
            DayTaskCategory.Normalize(item.Category),
            OverdueIdentity.IdentityText(item.Text, item.Attachments),
            OverdueIdentity.AttachmentKey(item.Attachments),
            broughtTo);
}

public static class CarryLineage
{
    public static (string CarryId, CarryLink Link) Stamp(
        OverdueTaskItem source,
        DateOnly broughtTo,
        CarryLinkTable? existing = null)
    {
        string id;
        do
        {
            id = CarryMarker.NewId();
        }
        while (existing?.Find(id) is not null);

        return (id, CarryLink.Create(source, broughtTo, id));
    }
}

/// <summary>In-memory copy of <c>carry-links.json</c>.</summary>
public sealed class CarryLinkTable
{
    public List<CarryLink> Links { get; set; } = new();

    public CarryLink? Find(string? carryId)
    {
        var id = CarryMarker.Normalize(carryId);
        if (id.Length == 0)
            return null;

        var links = Items();
        for (var i = links.Count - 1; i >= 0; i--)
        {
            if (string.Equals(CarryMarker.Normalize(links[i].CarryId), id, StringComparison.Ordinal))
                return links[i];
        }

        return null;
    }

    public void Remember(CarryLink link)
    {
        var id = CarryMarker.Normalize(link.CarryId);
        if (id.Length == 0)
            return;

        var links = Items();
        links.RemoveAll(item => string.Equals(CarryMarker.Normalize(item.CarryId), id, StringComparison.Ordinal));
        links.Add(id == link.CarryId ? link : link with { CarryId = id });
    }

    public bool Remove(string? carryId)
    {
        var id = CarryMarker.Normalize(carryId);
        if (id.Length == 0)
            return false;

        var links = Items();
        var before = links.Count;
        links.RemoveAll(item => string.Equals(CarryMarker.Normalize(item.CarryId), id, StringComparison.Ordinal));
        return links.Count != before;
    }

    private List<CarryLink> Items()
    {
        Links ??= new List<CarryLink>();
        return Links;
    }
}

public static class CarryLinkStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static string DefaultPath
    {
        get
        {
            var directory = Path.GetDirectoryName(CarryOverStore.DefaultPath);
            if (string.IsNullOrEmpty(directory))
                directory = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(directory, "carry-links.json");
        }
    }

    public static CarryLinkTable Load() => Load(DefaultPath);

    public static CarryLinkTable Load(string path)
    {
        if (!File.Exists(path))
            return new CarryLinkTable();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<CarryLinkTable>(json, JsonOptions) ?? new CarryLinkTable();
        }
        catch (JsonException)
        {
            return new CarryLinkTable();
        }
    }

    public static void Save(CarryLinkTable table) => Save(table, DefaultPath);

    public static void Save(CarryLinkTable table, string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(table, JsonOptions);
        var temp = path + ".tmp";
        File.WriteAllText(temp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temp, path, overwrite: true);
    }
}

using System.Text.Json;

namespace DayList.Core;

public sealed record ObsidianDailyNotes(string Folder, string Format)
{
    public static ObsidianDailyNotes? TryRead(string vaultPath)
    {
        if (string.IsNullOrWhiteSpace(vaultPath))
            return null;

        var path = Path.Combine(vaultPath, ".obsidian", "daily-notes.json");
        if (!File.Exists(path))
            return null;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            return new ObsidianDailyNotes(
                ReadString(document.RootElement, "folder"),
                ReadString(document.RootElement, "format"));
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string ReadString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            return "";
        return value.GetString()?.Trim() ?? "";
    }
}

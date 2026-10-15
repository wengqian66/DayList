using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DayList.Core;

public static class SettingsStore
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

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DayList", "settings.json");

    public static AppSettings Load() => Load(DefaultPath);

    public static AppSettings Load(string path)
    {
        if (!File.Exists(path))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            settings.Normalize();
            return settings;
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings) => Save(settings, DefaultPath);

    public static void Save(AppSettings settings, string path)
    {
        settings.Normalize();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var temp = path + ".tmp";
        File.WriteAllText(temp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temp, path, overwrite: true);
    }
}

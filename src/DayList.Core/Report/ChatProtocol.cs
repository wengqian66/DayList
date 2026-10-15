using System.Text.Encodings.Web;
using System.Text.Json;

namespace DayList.Core;

public static class ChatProtocol
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static Uri Endpoint(string baseUrl)
    {
        var trimmed = baseUrl.Trim();
        if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            return new Uri(trimmed);
        return new Uri(trimmed.TrimEnd('/') + "/chat/completions");
    }

    public static string BuildRequest(string model, string system, string user)
    {
        var payload = new
        {
            model,
            temperature = 0.2,
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user }
            }
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public static string? ReadAssistantContent(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                return null;

            if (!choices[0].TryGetProperty("message", out var message))
                return null;
            if (!message.TryGetProperty("content", out var content))
                return null;

            if (content.ValueKind == JsonValueKind.String)
                return content.GetString();

            if (content.ValueKind != JsonValueKind.Array)
                return null;

            var builder = new System.Text.StringBuilder();
            foreach (var part in content.EnumerateArray())
            {
                if (part.ValueKind == JsonValueKind.String)
                    builder.Append(part.GetString());
                else if (part.ValueKind == JsonValueKind.Object && part.TryGetProperty("text", out var text))
                    builder.Append(text.GetString());
            }

            var combined = builder.ToString();
            return combined.Length == 0 ? null : combined;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? ReadError(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String)
            {
                return message.GetString();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    public static string StripFences(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            return trimmed;

        var firstNewline = trimmed.IndexOf('\n');
        if (firstNewline < 0)
            return trimmed;

        var body = trimmed[(firstNewline + 1)..];
        if (body.EndsWith("```", StringComparison.Ordinal))
            body = body[..^3];
        return body.Trim();
    }
}

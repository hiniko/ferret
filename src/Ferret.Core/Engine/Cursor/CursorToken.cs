using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ferret.Core.Engine.Cursor;

public sealed record CursorPayload
{
    [JsonPropertyName("v")] public int Version { get; init; } = 1;
    [JsonPropertyName("k")] public IReadOnlyList<string> SortKeys { get; init; } = [];
    [JsonPropertyName("pks")] public IReadOnlyList<string> PrimaryKeys { get; init; } = [];
    [JsonPropertyName("fp")] public string Fingerprint { get; init; } = "";
    [JsonPropertyName("o")] public int? Offset { get; init; }
}

public static class CursorToken
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    public static string Encode(CursorPayload payload)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOpts);
        return Base64UrlEncode(json);
    }

    public static CursorPayload Decode(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new FormatException("Empty cursor token.");
        byte[] bytes;
        try { bytes = Base64UrlDecode(token); }
        catch (Exception ex) { throw new FormatException("Cursor token is not valid base64-url.", ex); }
        try
        {
            return JsonSerializer.Deserialize<CursorPayload>(bytes, JsonOpts)
                ?? throw new FormatException("Cursor token deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new FormatException("Cursor token JSON payload is malformed.", ex);
        }
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        var b64 = Convert.ToBase64String(bytes);
        return b64.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string s)
    {
        var b64 = s.Replace('-', '+').Replace('_', '/');
        switch (b64.Length % 4)
        {
            case 2: b64 += "=="; break;
            case 3: b64 += "="; break;
            case 1: throw new FormatException("Invalid base64-url length.");
        }
        return Convert.FromBase64String(b64);
    }
}

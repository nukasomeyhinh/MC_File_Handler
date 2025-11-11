using System.Text.Json;

public class HandlerLog
{
    public string? ActiveHost { get; set; }
    public DateTimeOffset LastUpdated { get; set; }

    public static HandlerLog Create(string hostId)
    {
        return new HandlerLog
        {
            ActiveHost = hostId,
            LastUpdated = DateTimeOffset.UtcNow
        };
    }

    public string ToJson() => JsonSerializer.Serialize(this);

    public static HandlerLog? FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<HandlerLog>(json);
        }
        catch
        {
            return null;
        }
    }
}

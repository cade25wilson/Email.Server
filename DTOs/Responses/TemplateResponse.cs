using System.Text.Json.Serialization;

namespace Email.Server.DTOs.Responses;

public class TemplateResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("html_body")]
    public string? HtmlBody { get; set; }

    [JsonPropertyName("text_body")]
    public string? TextBody { get; set; }

    [JsonPropertyName("variables")]
    public List<string> Variables { get; set; } = [];

    [JsonPropertyName("created_at_utc")]
    public DateTime CreatedAtUtc { get; set; }
}

public class TemplateListResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("created_at_utc")]
    public DateTime CreatedAtUtc { get; set; }
}

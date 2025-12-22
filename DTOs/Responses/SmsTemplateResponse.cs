using System.Text.Json.Serialization;

namespace Email.Server.DTOs.Responses;

public class SmsTemplateResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;

    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; }

    [JsonPropertyName("created_at_utc")]
    public DateTime CreatedAtUtc { get; set; }

    [JsonPropertyName("updated_at_utc")]
    public DateTime? UpdatedAtUtc { get; set; }
}

public class SmsTemplateListResponse
{
    [JsonPropertyName("templates")]
    public List<SmsTemplateResponse> Templates { get; set; } = [];

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("page_size")]
    public int PageSize { get; set; }
}

using System.Text.Json.Serialization;

namespace Email.Server.DTOs.Responses;

public class EmailListResponse
{
    [JsonPropertyName("items")]
    public List<MessageResponse> Items { get; set; } = new();

    [JsonPropertyName("total_count")]
    public int TotalCount { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("page_size")]
    public int PageSize { get; set; }

    [JsonPropertyName("has_more")]
    public bool HasMore { get; set; }
}

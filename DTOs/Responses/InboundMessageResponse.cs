using System.Text.Json.Serialization;

namespace Email.Server.DTOs.Responses;

public class InboundMessageResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("domain_id")]
    public Guid? DomainId { get; set; }

    [JsonPropertyName("domain_name")]
    public string? DomainName { get; set; }

    [JsonPropertyName("recipient")]
    public required string Recipient { get; set; }

    [JsonPropertyName("from_address")]
    public required string FromAddress { get; set; }

    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("received_at_utc")]
    public DateTime ReceivedAtUtc { get; set; }

    [JsonPropertyName("size_bytes")]
    public long? SizeBytes { get; set; }

    [JsonPropertyName("region")]
    public required string Region { get; set; }

    [JsonPropertyName("ses_message_id")]
    public string? SesMessageId { get; set; }

    [JsonPropertyName("processed_at_utc")]
    public DateTime? ProcessedAtUtc { get; set; }
}

public class InboundMessageListResponse
{
    [JsonPropertyName("items")]
    public List<InboundMessageResponse> Items { get; set; } = [];

    [JsonPropertyName("total_count")]
    public int TotalCount { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("page_size")]
    public int PageSize { get; set; }

    [JsonPropertyName("total_pages")]
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
}

public class InboundEmailDownloadResponse
{
    [JsonPropertyName("download_url")]
    public required string DownloadUrl { get; set; }

    [JsonPropertyName("expires_at_utc")]
    public DateTime ExpiresAtUtc { get; set; }
}

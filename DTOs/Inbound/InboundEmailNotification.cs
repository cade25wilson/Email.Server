using System.Text.Json.Serialization;

namespace Email.Server.DTOs.Inbound;

/// <summary>
/// Notification received from Lambda function when an inbound email is processed.
/// </summary>
public class InboundEmailNotification
{
    [JsonPropertyName("ses_message_id")]
    public required string SesMessageId { get; set; }

    [JsonPropertyName("blob_key")]
    public required string BlobKey { get; set; }

    [JsonPropertyName("recipient")]
    public required string Recipient { get; set; }

    [JsonPropertyName("from_address")]
    public required string FromAddress { get; set; }

    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("received_at_utc")]
    public DateTime ReceivedAtUtc { get; set; }

    [JsonPropertyName("size_bytes")]
    public long SizeBytes { get; set; }

    [JsonPropertyName("region")]
    public required string Region { get; set; }

    [JsonPropertyName("headers")]
    public InboundEmailHeaders? Headers { get; set; }
}

public class InboundEmailHeaders
{
    [JsonPropertyName("to")]
    public List<string>? To { get; set; }

    [JsonPropertyName("cc")]
    public List<string>? Cc { get; set; }

    [JsonPropertyName("reply_to")]
    public string? ReplyTo { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("message_id")]
    public string? MessageId { get; set; }
}

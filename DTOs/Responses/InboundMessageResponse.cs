using System.Text.Json.Serialization;

namespace Email.Server.DTOs.Responses;

public class InboundMessageResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("to")]
    public List<string> To { get; set; } = [];

    [JsonPropertyName("from")]
    public required string From { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("bcc")]
    public List<string> Bcc { get; set; } = [];

    [JsonPropertyName("cc")]
    public List<string> Cc { get; set; } = [];

    [JsonPropertyName("reply_to")]
    public List<string> ReplyTo { get; set; } = [];

    [JsonPropertyName("message_id")]
    public string? MessageId { get; set; }

    [JsonPropertyName("attachments")]
    public List<InboundAttachmentResponse> Attachments { get; set; } = [];
}

public class InboundAttachmentResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("filename")]
    public required string Filename { get; set; }

    [JsonPropertyName("content_type")]
    public required string ContentType { get; set; }

    [JsonPropertyName("content_id")]
    public string? ContentId { get; set; }

    [JsonPropertyName("content_disposition")]
    public string ContentDisposition { get; set; } = "attachment";

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

public class InboundMessageListResponse
{
    [JsonPropertyName("object")]
    public string Object { get; set; } = "list";

    [JsonPropertyName("has_more")]
    public bool HasMore { get; set; }

    [JsonPropertyName("data")]
    public List<InboundMessageResponse> Data { get; set; } = [];
}

public class InboundEmailDownloadResponse
{
    [JsonPropertyName("download_url")]
    public required string DownloadUrl { get; set; }

    [JsonPropertyName("expires_at_utc")]
    public DateTime ExpiresAtUtc { get; set; }
}

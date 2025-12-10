using System.Text.Json.Serialization;

namespace Email.Server.DTOs.Responses;

public class AttachmentResponse
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("file_name")]
    public required string FileName { get; set; }

    [JsonPropertyName("content_type")]
    public required string ContentType { get; set; }

    [JsonPropertyName("size_bytes")]
    public long SizeBytes { get; set; }

    [JsonPropertyName("is_inline")]
    public bool IsInline { get; set; }

    [JsonPropertyName("content_id")]
    public string? ContentId { get; set; }

    [JsonPropertyName("uploaded_at_utc")]
    public DateTime UploadedAtUtc { get; set; }
}

public class AttachmentDownloadResponse
{
    [JsonPropertyName("download_url")]
    public required string DownloadUrl { get; set; }

    [JsonPropertyName("expires_at_utc")]
    public DateTime ExpiresAtUtc { get; set; }
}

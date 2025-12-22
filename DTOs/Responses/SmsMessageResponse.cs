using System.Text.Json.Serialization;

namespace Email.Server.DTOs.Responses;

public class SmsMessageResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("from_number")]
    public string FromNumber { get; set; } = string.Empty;

    [JsonPropertyName("to_number")]
    public string ToNumber { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;

    [JsonPropertyName("template_id")]
    public Guid? TemplateId { get; set; }

    [JsonPropertyName("aws_message_id")]
    public string? AwsMessageId { get; set; }

    [JsonPropertyName("status")]
    public byte Status { get; set; }

    [JsonPropertyName("status_text")]
    public string StatusText => Status switch
    {
        1 => "Sent",
        2 => "Failed",
        4 => "Scheduled",
        _ => "Queued"
    };

    [JsonPropertyName("segment_count")]
    public int SegmentCount { get; set; }

    [JsonPropertyName("requested_at_utc")]
    public DateTime RequestedAtUtc { get; set; }

    [JsonPropertyName("scheduled_at_utc")]
    public DateTime? ScheduledAtUtc { get; set; }

    [JsonPropertyName("sent_at_utc")]
    public DateTime? SentAtUtc { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public class SmsMessageListResponse
{
    [JsonPropertyName("messages")]
    public List<SmsMessageResponse> Messages { get; set; } = [];

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("page_size")]
    public int PageSize { get; set; }
}

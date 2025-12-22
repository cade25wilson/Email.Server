using System.Text.Json.Serialization;

namespace Email.Server.DTOs.Responses;

public class SendSmsResponse
{
    [JsonPropertyName("message_id")]
    public Guid MessageId { get; set; }

    [JsonPropertyName("aws_message_id")]
    public string? AwsMessageId { get; set; }

    [JsonPropertyName("status")]
    public byte Status { get; set; } // 0=Queued, 1=Sent, 2=Failed, 4=Scheduled

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

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

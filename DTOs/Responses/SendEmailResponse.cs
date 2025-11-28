using System.Text.Json.Serialization;

namespace Email.Server.DTOs.Responses;

public class SendEmailResponse
{
    [JsonPropertyName("message_id")]
    public Guid MessageId { get; set; }

    [JsonPropertyName("ses_message_id")]
    public string? SesMessageId { get; set; }

    [JsonPropertyName("status")]
    public byte Status { get; set; } // 0=Queued,1=Sent,2=Failed,3=Partial,4=Scheduled

    [JsonPropertyName("status_text")]
    public string StatusText => Status switch
    {
        1 => "Sent",
        2 => "Failed",
        3 => "Partial",
        4 => "Scheduled",
        _ => "Queued"
    };

    [JsonPropertyName("requested_at_utc")]
    public DateTime RequestedAtUtc { get; set; }

    [JsonPropertyName("scheduled_at_utc")]
    public DateTime? ScheduledAtUtc { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

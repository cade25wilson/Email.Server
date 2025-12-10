using System.Text.Json.Serialization;

namespace Email.Server.DTOs.Responses;

public class MessageResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("from_email")]
    public required string FromEmail { get; set; }

    [JsonPropertyName("from_name")]
    public string? FromName { get; set; }

    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("ses_message_id")]
    public string? SesMessageId { get; set; }

    [JsonPropertyName("status")]
    public byte Status { get; set; }

    [JsonPropertyName("status_text")]
    public string StatusText => Status switch
    {
        1 => "Sent",
        2 => "Failed",
        3 => "Partial",
        4 => "Scheduled",
        5 => "Cancelled",
        _ => "Queued"
    };

    [JsonPropertyName("scheduled_at_utc")]
    public DateTime? ScheduledAtUtc { get; set; }

    [JsonPropertyName("requested_at_utc")]
    public DateTime RequestedAtUtc { get; set; }

    [JsonPropertyName("sent_at_utc")]
    public DateTime? SentAtUtc { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("recipients")]
    public List<MessageRecipientResponse> Recipients { get; set; } = new();

    [JsonPropertyName("events")]
    public List<MessageEventResponse> Events { get; set; } = new();

    [JsonPropertyName("tags")]
    public Dictionary<string, string> Tags { get; set; } = new();
}

public class MessageRecipientResponse
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("kind")]
    public byte Kind { get; set; } // 0=To,1=CC,2=BCC

    [JsonPropertyName("kind_text")]
    public string KindText => Kind switch
    {
        1 => "CC",
        2 => "BCC",
        _ => "To"
    };

    [JsonPropertyName("email")]
    public required string Email { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("delivery_status")]
    public byte DeliveryStatus { get; set; } // 0=Pending,1=Delivered,2=Bounced,3=Complained

    [JsonPropertyName("delivery_status_text")]
    public string DeliveryStatusText => DeliveryStatus switch
    {
        1 => "Delivered",
        2 => "Bounced",
        3 => "Complained",
        _ => "Pending"
    };

    [JsonPropertyName("ses_delivery_id")]
    public string? SesDeliveryId { get; set; }
}

public class MessageEventResponse
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("event_type")]
    public required string EventType { get; set; }

    [JsonPropertyName("occurred_at_utc")]
    public DateTime OccurredAtUtc { get; set; }

    [JsonPropertyName("recipient")]
    public string? Recipient { get; set; }
}

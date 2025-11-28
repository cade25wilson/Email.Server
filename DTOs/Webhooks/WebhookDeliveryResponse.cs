namespace Email.Server.DTOs.Webhooks;

public class WebhookDeliveryResponse
{
    public long Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public byte Status { get; set; }
    public string StatusText { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public DateTime? LastAttemptUtc { get; set; }
    public int? ResponseStatusCode { get; set; }
    public DateTime? NextRetryAtUtc { get; set; }
}

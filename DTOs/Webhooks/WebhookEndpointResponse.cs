namespace Email.Server.DTOs.Webhooks;

public class WebhookEndpointResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public List<string> EventTypes { get; set; } = [];
    public bool Enabled { get; set; }
    public string SecretPreview { get; set; } = string.Empty; // First 8 chars + "..."
    public DateTime CreatedAtUtc { get; set; }
}

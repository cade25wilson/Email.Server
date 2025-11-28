namespace Email.Server.DTOs.Webhooks;

public class WebhookTestResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int? StatusCode { get; set; }
}

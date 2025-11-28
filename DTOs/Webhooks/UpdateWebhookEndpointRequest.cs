using System.ComponentModel.DataAnnotations;

namespace Email.Server.DTOs.Webhooks;

public class UpdateWebhookEndpointRequest
{
    [MaxLength(100)]
    public string? Name { get; set; }

    [Url]
    [MaxLength(2048)]
    public string? Url { get; set; }

    public List<string>? EventTypes { get; set; }

    public bool? Enabled { get; set; }
}

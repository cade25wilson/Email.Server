using System.ComponentModel.DataAnnotations;

namespace Email.Server.DTOs.Webhooks;

public class CreateWebhookEndpointRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Url]
    [MaxLength(2048)]
    public string Url { get; set; } = string.Empty;

    [Required]
    [MinLength(1)]
    public List<string> EventTypes { get; set; } = [];
}

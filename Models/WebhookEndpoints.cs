using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Email.Server.Models;

public class WebhookEndpoints
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [ForeignKey("Tenant")]
    public Guid TenantId { get; set; }

    [MaxLength(100)]
    public string? Name { get; set; } // User-friendly name for the endpoint

    [MaxLength(2048)]
    public required string Url { get; set; }

    [MaxLength(500)]
    public string? EventTypes { get; set; } // Comma-separated list of event types (e.g., "email.delivered,email.bounced")

    [MaxLength(64)]
    public byte[]? Secret { get; set; } // HMAC signing secret (encrypted at rest)

    public bool Enabled { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Tenants? Tenant { get; set; }
    public ICollection<WebhookDeliveries>? Deliveries { get; set; }
}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Email.Server.Models;

public class SmsTemplates
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [ForeignKey("Tenant")]
    public Guid TenantId { get; set; }

    [MaxLength(100)]
    public required string Name { get; set; }

    [MaxLength(1600)]
    public required string Body { get; set; } // Supports {{variable}} syntax

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAtUtc { get; set; }

    // Navigation properties
    public Tenants? Tenant { get; set; }
}

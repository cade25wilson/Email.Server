using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Email.Server.Models;

public class Messages
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [ForeignKey("Tenant")]
    public Guid TenantId { get; set; }

    [ForeignKey("RegionCatalog")]
    [MaxLength(32)]
    public required string Region { get; set; }

    [ForeignKey("ConfigSet")]
    public Guid? ConfigSetId { get; set; }

    [MaxLength(320)]
    public required string FromEmail { get; set; }

    [MaxLength(200)]
    public string? FromName { get; set; }

    [MaxLength(998)]
    public string? Subject { get; set; }

    public string? HtmlBody { get; set; }

    public string? TextBody { get; set; }

    public Guid? TemplateId { get; set; }

    [MaxLength(256)]
    public string? SesMessageId { get; set; }

    public byte Status { get; set; } = 0; // 0=Queued,1=Sent,2=Failed,3=Partial,4=Scheduled

    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? ScheduledAtUtc { get; set; }

    public DateTime? SentAtUtc { get; set; }

    public string? Error { get; set; }

    // Navigation properties
    public Tenants? Tenant { get; set; }
    public RegionsCatalog? RegionCatalog { get; set; }
    public ConfigSets? ConfigSet { get; set; }
}

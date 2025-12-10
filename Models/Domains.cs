using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Email.Server.Models;

public class Domains
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [ForeignKey("Tenant")]
    public Guid TenantId { get; set; }

    [ForeignKey("RegionCatalog")]
    [MaxLength(32)]
    public required string Region { get; set; }

    [MaxLength(255)]
    public required string Domain { get; set; }

    public byte VerificationStatus { get; set; } = 0; // 0=Pending,1=Verified,2=Failed

    public byte DkimMode { get; set; } = 1; // 1=Easy,2=BYODKIM

    public byte DkimStatus { get; set; } = 0; // 0=Pending,1=Success,2=Failed

    [MaxLength(255)]
    public string? MailFromSubdomain { get; set; }

    public byte MailFromStatus { get; set; } = 0; // 0=Off,1=Configured

    [MaxLength(2048)]
    public string? IdentityArn { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? VerifiedAtUtc { get; set; }

    // Inbound email settings
    public bool InboundEnabled { get; set; } = false;

    public byte InboundStatus { get; set; } = 0; // 0=Off, 1=Pending, 2=Active

    // Navigation properties
    public Tenants? Tenant { get; set; }
    public RegionsCatalog? RegionCatalog { get; set; }
}

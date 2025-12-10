using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Email.Server.Models;

public class InboundMessages
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [ForeignKey("Tenant")]
    public Guid TenantId { get; set; }

    [ForeignKey("Domain")]
    public Guid? DomainId { get; set; }

    [ForeignKey("RegionCatalog")]
    [MaxLength(32)]
    public required string Region { get; set; }

    [MaxLength(320)]
    public required string Recipient { get; set; } // address that received it (your inbound domain)

    [MaxLength(320)]
    public required string FromAddress { get; set; }

    [MaxLength(998)]
    public string? Subject { get; set; }

    public DateTime ReceivedAtUtc { get; set; }

    [MaxLength(1024)]
    public required string BlobKey { get; set; } // raw MIME in Azure Blob Storage

    [MaxLength(256)]
    public string? SesMessageId { get; set; }

    public long? SizeBytes { get; set; }

    public string? ParsedJson { get; set; } // optional parsed metadata

    public DateTime? ProcessedAtUtc { get; set; }

    // Navigation properties
    public Tenants? Tenant { get; set; }
    public Domains? Domain { get; set; }
    public RegionsCatalog? RegionCatalog { get; set; }
}

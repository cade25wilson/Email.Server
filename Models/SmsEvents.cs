using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Email.Server.Models;

public class SmsEvents
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [ForeignKey("SmsMessage")]
    public Guid? SmsMessageId { get; set; }

    [ForeignKey("Tenant")]
    public Guid TenantId { get; set; }

    [MaxLength(50)]
    public required string EventType { get; set; } // queued,sent,delivered,failed

    public DateTime OccurredAtUtc { get; set; }

    [MaxLength(20)]
    public required string Recipient { get; set; }

    public string? PayloadJson { get; set; }

    // Navigation properties
    public SmsMessages? SmsMessage { get; set; }
    public Tenants? Tenant { get; set; }
}

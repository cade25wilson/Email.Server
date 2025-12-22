using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Email.Server.Models;

public class SmsMessages
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [ForeignKey("Tenant")]
    public Guid TenantId { get; set; }

    [ForeignKey("PhoneNumber")]
    public Guid? PhoneNumberId { get; set; }

    [MaxLength(20)]
    public required string FromNumber { get; set; }

    [MaxLength(20)]
    public required string ToNumber { get; set; }

    [MaxLength(1600)]
    public required string Body { get; set; }

    [ForeignKey("Template")]
    public Guid? TemplateId { get; set; }

    [MaxLength(255)]
    public string? AwsMessageId { get; set; }

    public byte Status { get; set; } = 0; // 0=Queued,1=Sent,2=Failed,4=Scheduled

    public int SegmentCount { get; set; } = 1;

    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? ScheduledAtUtc { get; set; }

    public DateTime? SentAtUtc { get; set; }

    [MaxLength(500)]
    public string? Error { get; set; }

    // Navigation properties
    public Tenants? Tenant { get; set; }
    public SmsPhoneNumbers? PhoneNumber { get; set; }
    public SmsTemplates? Template { get; set; }
}

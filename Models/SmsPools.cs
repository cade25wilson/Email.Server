using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Email.Server.Models;

public class SmsPools
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [ForeignKey("Tenant")]
    public Guid TenantId { get; set; }

    /// <summary>
    /// AWS Pool ID (e.g., "pool-1234567890abcdef0")
    /// </summary>
    [MaxLength(64)]
    public string? AwsPoolId { get; set; }

    /// <summary>
    /// AWS Pool ARN (e.g., "arn:aws:sms-voice:us-east-1:123456789012:pool/pool-1234567890abcdef0")
    /// </summary>
    [MaxLength(255)]
    public string? AwsPoolArn { get; set; }

    /// <summary>
    /// Human-readable pool name (e.g., "{TenantName}-pool")
    /// </summary>
    [MaxLength(128)]
    public required string PoolName { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Tenants? Tenant { get; set; }
    public ICollection<SmsPhoneNumbers> PhoneNumbers { get; set; } = [];
}

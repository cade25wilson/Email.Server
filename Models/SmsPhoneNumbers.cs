using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Email.Server.Models;

public enum SmsNumberType : byte
{
    LongCode = 0,
    TollFree = 1,
    ShortCode = 2
}

public class SmsPhoneNumbers
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [ForeignKey("Tenant")]
    public Guid TenantId { get; set; }

    [MaxLength(20)]
    public required string PhoneNumber { get; set; }

    [MaxLength(255)]
    public string? PhoneNumberArn { get; set; }

    public SmsNumberType NumberType { get; set; } = SmsNumberType.TollFree;

    [MaxLength(2)]
    public string Country { get; set; } = "US";

    public int MonthlyFeeCents { get; set; } = 200; // $2/month for toll-free

    public bool IsDefault { get; set; } = false;

    public bool IsActive { get; set; } = true;

    public DateTime? ProvisionedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Tenants? Tenant { get; set; }
}

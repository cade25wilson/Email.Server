using System.ComponentModel.DataAnnotations;

namespace Email.Server.Models;

public class BillingPlans
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(50)]
    public required string Name { get; set; }

    [MaxLength(100)]
    public string? DisplayName { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    [Required]
    [MaxLength(100)]
    public required string StripePriceId { get; set; }

    [MaxLength(100)]
    public string? StripeMeteredPriceId { get; set; }

    [MaxLength(100)]
    public string? StripeProductId { get; set; }

    [MaxLength(200)]
    public string? StripePaymentLinkUrl { get; set; }

    public int MonthlyPriceCents { get; set; }

    public int IncludedEmails { get; set; }

    public int OverageRateCentsPer1K { get; set; }

    public bool AllowsOverage { get; set; }

    // SMS limits
    public int IncludedSms { get; set; } = 0;

    public int SmsOverageRateCentsPer100 { get; set; } = 150; // $1.50 per 100 = $0.015 each

    public bool AllowsSmsOverage { get; set; } = true;

    // Push notification limits
    public int IncludedPush { get; set; } = 0;

    public int PushOverageRateCentsPer1K { get; set; } = 100; // $1.00 per 1K

    public bool AllowsPushOverage { get; set; } = true;

    public int MaxPushCredentials { get; set; } = 2;

    public int MaxApiKeys { get; set; }

    public int MaxDomains { get; set; }

    public int MaxTeamMembers { get; set; }

    public int MaxWebhooks { get; set; }

    public int MaxTemplates { get; set; }

    public int AnalyticsRetentionDays { get; set; }

    public bool HasDedicatedIp { get; set; }

    [MaxLength(50)]
    public string? SupportLevel { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

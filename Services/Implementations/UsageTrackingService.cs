using Email.Server.Configuration;
using Email.Server.Data;
using Email.Server.Models;
using Email.Server.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Stripe.Billing;

namespace Email.Server.Services.Implementations;

public class UsageTrackingService : IUsageTrackingService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<UsageTrackingService> _logger;
    private readonly StripeSettings _stripeSettings;
    private readonly BillingSettings _billingSettings;

    public UsageTrackingService(
        ApplicationDbContext context,
        ILogger<UsageTrackingService> logger,
        IOptions<StripeSettings> stripeSettings,
        IOptions<BillingSettings> billingSettings)
    {
        _context = context;
        _logger = logger;
        _stripeSettings = stripeSettings.Value;
        _billingSettings = billingSettings.Value;

        Stripe.StripeConfiguration.ApiKey = _stripeSettings.SecretKey;
    }

    public async Task RecordEmailSendAsync(
        Guid tenantId,
        int emailCount,
        string source,
        CancellationToken ct = default)
    {
        var period = await GetOrCreateCurrentPeriodAsync(tenantId, ct);

        // Atomically increment usage
        await _context.UsagePeriods
            .Where(p => p.Id == period.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.EmailsSent, p => p.EmailsSent + emailCount)
                .SetProperty(p => p.OverageEmails, p =>
                    p.EmailsSent + emailCount > p.IncludedEmailsLimit
                        ? (p.EmailsSent + emailCount) - p.IncludedEmailsLimit
                        : 0), ct);

        _logger.LogDebug(
            "Recorded {Count} email(s) for tenant {TenantId} from {Source}",
            emailCount, tenantId, source);
    }

    public async Task<UsageLimitCheckResult> CheckUsageLimitAsync(
        Guid tenantId,
        int requestedCount,
        CancellationToken ct = default)
    {
        var subscription = await _context.TenantSubscriptions
            .Include(s => s.BillingPlan)
            .Include(s => s.Tenant)
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

        // No subscription = free tier with daily limit
        if (subscription == null)
        {
            return await CheckFreeTierLimitAsync(tenantId, requestedCount, ct);
        }

        // Check subscription status
        if (subscription.Status is SubscriptionStatus.Canceled or SubscriptionStatus.Unpaid)
        {
            // Allow free tier for canceled/unpaid subscriptions
            return await CheckFreeTierLimitAsync(tenantId, requestedCount, ct);
        }

        // Check if sending is disabled
        if (subscription.Tenant?.SendingDisabledAt != null)
        {
            return new UsageLimitCheckResult
            {
                Allowed = false,
                DenialReason = subscription.Tenant.SendingDisabledReason ?? "Sending is disabled"
            };
        }

        var period = await GetOrCreateCurrentPeriodAsync(tenantId, ct);
        var currentUsage = period.EmailsSent;
        var includedLimit = period.IncludedEmailsLimit;

        // Check if plan allows overage
        if (!subscription.BillingPlan?.AllowsOverage == true)
        {
            if (currentUsage + requestedCount > includedLimit)
            {
                return new UsageLimitCheckResult
                {
                    Allowed = false,
                    CurrentUsage = currentUsage,
                    IncludedLimit = includedLimit,
                    RemainingIncluded = Math.Max(0, includedLimit - currentUsage),
                    DenialReason = $"Email limit of {includedLimit:N0} reached. Please upgrade to continue sending."
                };
            }
        }

        // Plans with overage allowed
        var isOverage = currentUsage + requestedCount > includedLimit;

        return new UsageLimitCheckResult
        {
            Allowed = true,
            IsOverage = isOverage,
            CurrentUsage = currentUsage,
            IncludedLimit = includedLimit,
            RemainingIncluded = Math.Max(0, includedLimit - currentUsage)
        };
    }

    private async Task<UsageLimitCheckResult> CheckFreeTierLimitAsync(
        Guid tenantId,
        int requestedCount,
        CancellationToken ct)
    {
        var dailyLimit = _billingSettings.FreeTierDailyLimit;

        // Get today's usage (UTC day)
        var todayStart = DateTime.UtcNow.Date;
        var todayEnd = todayStart.AddDays(1);

        // Count emails sent today from Messages table
        var todayUsage = await _context.Messages
            .CountAsync(m =>
                m.TenantId == tenantId &&
                m.RequestedAtUtc >= todayStart &&
                m.RequestedAtUtc < todayEnd &&
                m.Status != 2, // Exclude failed messages
                ct);

        if (todayUsage + requestedCount > dailyLimit)
        {
            return new UsageLimitCheckResult
            {
                Allowed = false,
                CurrentUsage = todayUsage,
                IncludedLimit = dailyLimit,
                RemainingIncluded = Math.Max(0, dailyLimit - todayUsage),
                DenialReason = $"Free tier limit of {dailyLimit} emails per day reached. Subscribe to a plan for higher limits."
            };
        }

        _logger.LogDebug(
            "Free tier check for tenant {TenantId}: {Used}/{Limit} daily emails",
            tenantId, todayUsage, dailyLimit);

        return new UsageLimitCheckResult
        {
            Allowed = true,
            IsOverage = false,
            CurrentUsage = todayUsage,
            IncludedLimit = dailyLimit,
            RemainingIncluded = Math.Max(0, dailyLimit - todayUsage)
        };
    }

    public async Task ReportOverageToStripeAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_stripeSettings.MeterId))
        {
            _logger.LogWarning("Stripe MeterId not configured, skipping overage reporting");
            return;
        }

        // Find periods with unreported overage
        var periodsWithOverage = await _context.UsagePeriods
            .Include(p => p.Subscription)
            .Where(p =>
                p.OverageEmails > 0 &&
                p.OverageEmails > p.OverageReportedToStripe &&
                p.Subscription != null &&
                p.Subscription.Status == SubscriptionStatus.Active)
            .ToListAsync(ct);

        foreach (var period in periodsWithOverage)
        {
            try
            {
                var unreportedOverage = period.OverageEmails - period.OverageReportedToStripe;

                if (unreportedOverage <= 0)
                    continue;

                // Report to Stripe Billing Meter
                var meterEventService = new MeterEventService();
                await meterEventService.CreateAsync(new MeterEventCreateOptions
                {
                    EventName = "email_send",
                    Payload = new Dictionary<string, string>
                    {
                        { "stripe_customer_id", period.Subscription!.StripeCustomerId ?? "" },
                        { "value", unreportedOverage.ToString() }
                    },
                    Timestamp = DateTime.UtcNow
                }, cancellationToken: ct);

                // Update reported amount
                period.OverageReportedToStripe = period.OverageEmails;
                period.LastStripeReportUtc = DateTime.UtcNow;

                _logger.LogInformation(
                    "Reported {Count} overage emails to Stripe for tenant {TenantId}",
                    unreportedOverage, period.TenantId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to report overage to Stripe for period {PeriodId}",
                    period.Id);
            }
        }

        await _context.SaveChangesAsync(ct);
    }

    public async Task<UsagePeriods> GetOrCreateCurrentPeriodAsync(
        Guid tenantId,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // Try to find existing current period
        var existingPeriod = await _context.UsagePeriods
            .FirstOrDefaultAsync(p =>
                p.TenantId == tenantId &&
                p.PeriodStart <= now &&
                p.PeriodEnd > now, ct);

        // Get subscription to check/update limits
        var subscription = await _context.TenantSubscriptions
            .Include(s => s.BillingPlan)
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

        if (existingPeriod != null)
        {
            // Fix period if it has wrong limits (e.g., created before subscription)
            if (subscription?.BillingPlan != null &&
                existingPeriod.IncludedEmailsLimit != subscription.BillingPlan.IncludedEmails)
            {
                existingPeriod.SubscriptionId = subscription.Id;
                existingPeriod.IncludedEmailsLimit = subscription.BillingPlan.IncludedEmails;
                existingPeriod.OverageEmails = Math.Max(0, existingPeriod.EmailsSent - subscription.BillingPlan.IncludedEmails);
                await _context.SaveChangesAsync(ct);

                _logger.LogInformation(
                    "Fixed usage period {PeriodId} for tenant {TenantId}: updated limit to {Limit}",
                    existingPeriod.Id, tenantId, subscription.BillingPlan.IncludedEmails);
            }

            return existingPeriod;
        }

        // Subscription was already fetched above
        DateTime periodStart;
        DateTime periodEnd;
        long includedLimit;

        if (subscription != null)
        {
            periodStart = subscription.CurrentPeriodStart;
            periodEnd = subscription.CurrentPeriodEnd;
            includedLimit = subscription.BillingPlan?.IncludedEmails ?? 0;
        }
        else
        {
            // No subscription - use calendar month
            periodStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            periodEnd = periodStart.AddMonths(1);
            includedLimit = 0;
        }

        // Create new period
        var newPeriod = new UsagePeriods
        {
            TenantId = tenantId,
            SubscriptionId = subscription?.Id,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            EmailsSent = 0,
            IncludedEmailsLimit = includedLimit,
            OverageEmails = 0,
            OverageReportedToStripe = 0
        };

        _context.UsagePeriods.Add(newPeriod);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Created new usage period for tenant {TenantId}: {Start} to {End}",
            tenantId, periodStart, periodEnd);

        return newPeriod;
    }

    public async Task RecordSmsSendAsync(
        Guid tenantId,
        int smsCount,
        int segmentCount,
        string source,
        CancellationToken ct = default)
    {
        var period = await GetOrCreateCurrentPeriodAsync(tenantId, ct);

        // Atomically increment SMS usage
        await _context.UsagePeriods
            .Where(p => p.Id == period.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.SmsSent, p => p.SmsSent + smsCount)
                .SetProperty(p => p.SmsSegmentsSent, p => p.SmsSegmentsSent + segmentCount)
                .SetProperty(p => p.OverageSms, p =>
                    p.SmsSent + smsCount > p.IncludedSmsLimit
                        ? (p.SmsSent + smsCount) - p.IncludedSmsLimit
                        : 0), ct);

        _logger.LogDebug(
            "Recorded {Count} SMS ({Segments} segments) for tenant {TenantId} from {Source}",
            smsCount, segmentCount, tenantId, source);
    }

    public async Task<UsageLimitCheckResult> CheckSmsLimitAsync(
        Guid tenantId,
        int requestedCount,
        CancellationToken ct = default)
    {
        var subscription = await _context.TenantSubscriptions
            .Include(s => s.BillingPlan)
            .Include(s => s.Tenant)
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

        // No subscription = no SMS access (SMS requires a paid plan)
        if (subscription == null)
        {
            return new UsageLimitCheckResult
            {
                Allowed = false,
                DenialReason = "SMS requires a paid subscription. Please subscribe to a plan."
            };
        }

        // Check subscription status
        if (subscription.Status is SubscriptionStatus.Canceled or SubscriptionStatus.Unpaid)
        {
            return new UsageLimitCheckResult
            {
                Allowed = false,
                DenialReason = "SMS requires an active subscription."
            };
        }

        // Check if sending is disabled
        if (subscription.Tenant?.SendingDisabledAt != null)
        {
            return new UsageLimitCheckResult
            {
                Allowed = false,
                DenialReason = subscription.Tenant.SendingDisabledReason ?? "Sending is disabled"
            };
        }

        var period = await GetOrCreateCurrentPeriodAsync(tenantId, ct);
        var currentUsage = period.SmsSent;
        var includedLimit = period.IncludedSmsLimit;

        // Check if plan allows SMS overage
        if (!subscription.BillingPlan?.AllowsSmsOverage == true)
        {
            if (currentUsage + requestedCount > includedLimit)
            {
                return new UsageLimitCheckResult
                {
                    Allowed = false,
                    CurrentUsage = currentUsage,
                    IncludedLimit = includedLimit,
                    RemainingIncluded = Math.Max(0, includedLimit - currentUsage),
                    DenialReason = $"SMS limit of {includedLimit:N0} reached. Please upgrade to continue sending."
                };
            }
        }

        // Plans with overage allowed
        var isOverage = currentUsage + requestedCount > includedLimit;

        return new UsageLimitCheckResult
        {
            Allowed = true,
            IsOverage = isOverage,
            CurrentUsage = currentUsage,
            IncludedLimit = includedLimit,
            RemainingIncluded = Math.Max(0, includedLimit - currentUsage)
        };
    }

    public async Task RecordPushSendAsync(
        Guid tenantId,
        int pushCount,
        string source,
        CancellationToken ct = default)
    {
        var period = await GetOrCreateCurrentPeriodAsync(tenantId, ct);

        // Atomically increment push usage
        await _context.UsagePeriods
            .Where(p => p.Id == period.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.PushSent, p => p.PushSent + pushCount)
                .SetProperty(p => p.OveragePush, p =>
                    p.PushSent + pushCount > p.IncludedPushLimit
                        ? (p.PushSent + pushCount) - p.IncludedPushLimit
                        : 0), ct);

        _logger.LogDebug(
            "Recorded {Count} push notification(s) for tenant {TenantId} from {Source}",
            pushCount, tenantId, source);
    }

    public async Task<UsageLimitCheckResult> CheckPushLimitAsync(
        Guid tenantId,
        int requestedCount,
        CancellationToken ct = default)
    {
        var subscription = await _context.TenantSubscriptions
            .Include(s => s.BillingPlan)
            .Include(s => s.Tenant)
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

        // No subscription = no push access (push requires a paid plan)
        if (subscription == null)
        {
            return new UsageLimitCheckResult
            {
                Allowed = false,
                DenialReason = "Push notifications require a paid subscription. Please subscribe to a plan."
            };
        }

        // Check subscription status
        if (subscription.Status is SubscriptionStatus.Canceled or SubscriptionStatus.Unpaid)
        {
            return new UsageLimitCheckResult
            {
                Allowed = false,
                DenialReason = "Push notifications require an active subscription."
            };
        }

        // Check if sending is disabled
        if (subscription.Tenant?.SendingDisabledAt != null)
        {
            return new UsageLimitCheckResult
            {
                Allowed = false,
                DenialReason = subscription.Tenant.SendingDisabledReason ?? "Sending is disabled"
            };
        }

        var period = await GetOrCreateCurrentPeriodAsync(tenantId, ct);
        var currentUsage = period.PushSent;
        var includedLimit = period.IncludedPushLimit;

        // Check if plan allows push overage
        if (!subscription.BillingPlan?.AllowsPushOverage == true)
        {
            if (currentUsage + requestedCount > includedLimit)
            {
                return new UsageLimitCheckResult
                {
                    Allowed = false,
                    CurrentUsage = currentUsage,
                    IncludedLimit = includedLimit,
                    RemainingIncluded = Math.Max(0, includedLimit - currentUsage),
                    DenialReason = $"Push notification limit of {includedLimit:N0} reached. Please upgrade to continue sending."
                };
            }
        }

        // Plans with overage allowed
        var isOverage = currentUsage + requestedCount > includedLimit;

        return new UsageLimitCheckResult
        {
            Allowed = true,
            IsOverage = isOverage,
            CurrentUsage = currentUsage,
            IncludedLimit = includedLimit,
            RemainingIncluded = Math.Max(0, includedLimit - currentUsage)
        };
    }
}

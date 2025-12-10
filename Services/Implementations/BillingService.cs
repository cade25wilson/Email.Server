using Email.Server.Configuration;
using Email.Server.Data;
using Email.Server.DTOs.Responses.Billing;
using Email.Server.Exceptions;
using Email.Server.Models;
using Email.Server.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace Email.Server.Services.Implementations;

public class BillingService : IBillingService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<BillingService> _logger;
    private readonly StripeSettings _stripeSettings;
    private readonly BillingSettings _billingSettings;

    public BillingService(
        ApplicationDbContext context,
        ILogger<BillingService> logger,
        IOptions<StripeSettings> stripeSettings,
        IOptions<BillingSettings> billingSettings)
    {
        _context = context;
        _logger = logger;
        _stripeSettings = stripeSettings.Value;
        _billingSettings = billingSettings.Value;

        StripeConfiguration.ApiKey = _stripeSettings.SecretKey;
    }

    public async Task<IEnumerable<BillingPlanResponse>> GetAvailablePlansAsync(CancellationToken ct = default)
    {
        var plans = await _context.BillingPlans
            .Where(p => p.IsActive)
            .OrderBy(p => p.MonthlyPriceCents)
            .ToListAsync(ct);

        return plans.Select(MapToPlanResponse);
    }

    public async Task<SubscriptionResponse?> GetCurrentSubscriptionAsync(Guid tenantId, CancellationToken ct = default)
    {
        var subscription = await _context.TenantSubscriptions
            .Include(s => s.BillingPlan)
            .Include(s => s.Tenant)
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

        if (subscription == null)
        {
            return null;
        }

        return MapToSubscriptionResponse(subscription);
    }

    public async Task<CheckoutSessionResponse> CreateCheckoutSessionAsync(
        Guid tenantId,
        Guid planId,
        string successUrl,
        string cancelUrl,
        CancellationToken ct = default)
    {
        var tenant = await _context.Tenants.FindAsync([tenantId], ct)
            ?? throw new ArgumentException("Tenant not found");

        var plan = await _context.BillingPlans.FindAsync([planId], ct)
            ?? throw new ArgumentException("Plan not found");

        if (!plan.IsActive)
        {
            throw new ArgumentException("Plan is not available");
        }

        // Get or create Stripe customer
        var stripeCustomer = await GetOrCreateStripeCustomerAsync(tenantId, ct);

        var sessionOptions = new SessionCreateOptions
        {
            Customer = stripeCustomer.StripeCustomerId,
            PaymentMethodTypes = ["card"],
            Mode = "subscription",
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Price = plan.StripePriceId,
                    Quantity = 1  // Base flat fee price
                }
            ],
            SubscriptionData = new SessionSubscriptionDataOptions
            {
                Metadata = new Dictionary<string, string>
                {
                    { "tenant_id", tenantId.ToString() },
                    { "plan_id", planId.ToString() }
                }
            },
            Metadata = new Dictionary<string, string>
            {
                { "tenant_id", tenantId.ToString() },
                { "plan_id", planId.ToString() }
            }
        };

        // Add metered overage price if plan has one
        if (!string.IsNullOrEmpty(plan.StripeMeteredPriceId))
        {
            sessionOptions.LineItems.Add(new SessionLineItemOptions
            {
                Price = plan.StripeMeteredPriceId
            });
        }

        var service = new SessionService();
        var session = await service.CreateAsync(sessionOptions, cancellationToken: ct);

        _logger.LogInformation(
            "Created checkout session {SessionId} for tenant {TenantId} plan {PlanName}",
            session.Id, tenantId, plan.Name);

        return new CheckoutSessionResponse
        {
            SessionId = session.Id,
            CheckoutUrl = session.Url
        };
    }

    public async Task<CustomerPortalResponse> CreateCustomerPortalSessionAsync(
        Guid tenantId,
        string returnUrl,
        CancellationToken ct = default)
    {
        var stripeCustomer = await _context.StripeCustomers
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, ct)
            ?? throw new SubscriptionRequiredException(tenantId);

        var options = new Stripe.BillingPortal.SessionCreateOptions
        {
            Customer = stripeCustomer.StripeCustomerId,
            ReturnUrl = returnUrl
        };

        var service = new Stripe.BillingPortal.SessionService();
        var session = await service.CreateAsync(options, cancellationToken: ct);

        _logger.LogInformation(
            "Created customer portal session for tenant {TenantId}",
            tenantId);

        return new CustomerPortalResponse
        {
            PortalUrl = session.Url
        };
    }

    public async Task<SubscriptionResponse> ChangePlanAsync(
        Guid tenantId,
        Guid newPlanId,
        CancellationToken ct = default)
    {
        var subscription = await _context.TenantSubscriptions
            .Include(s => s.BillingPlan)
            .Include(s => s.Tenant)
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct)
            ?? throw new SubscriptionRequiredException(tenantId);

        var newPlan = await _context.BillingPlans.FindAsync([newPlanId], ct)
            ?? throw new ArgumentException("Plan not found");

        if (!newPlan.IsActive)
        {
            throw new ArgumentException("Plan is not available");
        }

        if (subscription.BillingPlanId == newPlanId)
        {
            throw new ArgumentException("Already on this plan");
        }

        // Update subscription in Stripe
        var stripeService = new SubscriptionService();
        var stripeSubscription = await stripeService.GetAsync(subscription.StripeSubscriptionId, cancellationToken: ct);

        var items = new List<SubscriptionItemOptions>();

        // Replace base price item
        var basePriceItem = stripeSubscription.Items.Data
            .FirstOrDefault(i => i.Price.Recurring?.UsageType != "metered");

        if (basePriceItem != null)
        {
            items.Add(new SubscriptionItemOptions
            {
                Id = basePriceItem.Id,
                Price = newPlan.StripePriceId
            });
        }

        // Handle metered price changes
        var meteredItem = stripeSubscription.Items.Data
            .FirstOrDefault(i => i.Price.Recurring?.UsageType == "metered");

        if (meteredItem != null && !string.IsNullOrEmpty(newPlan.StripeMeteredPriceId))
        {
            items.Add(new SubscriptionItemOptions
            {
                Id = meteredItem.Id,
                Price = newPlan.StripeMeteredPriceId
            });
        }
        else if (meteredItem != null && string.IsNullOrEmpty(newPlan.StripeMeteredPriceId))
        {
            items.Add(new SubscriptionItemOptions
            {
                Id = meteredItem.Id,
                Deleted = true
            });
        }
        else if (meteredItem == null && !string.IsNullOrEmpty(newPlan.StripeMeteredPriceId))
        {
            items.Add(new SubscriptionItemOptions
            {
                Price = newPlan.StripeMeteredPriceId
            });
        }

        var updateOptions = new SubscriptionUpdateOptions
        {
            Items = items,
            ProrationBehavior = "create_prorations",
            Metadata = new Dictionary<string, string>
            {
                { "tenant_id", tenantId.ToString() },
                { "plan_id", newPlanId.ToString() }
            }
        };

        await stripeService.UpdateAsync(subscription.StripeSubscriptionId, updateOptions, cancellationToken: ct);

        // Update local subscription
        subscription.BillingPlanId = newPlanId;
        subscription.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Changed plan for tenant {TenantId} from {OldPlan} to {NewPlan}",
            tenantId, subscription.BillingPlan?.Name, newPlan.Name);

        // Reload with new plan
        subscription = await _context.TenantSubscriptions
            .Include(s => s.BillingPlan)
            .Include(s => s.Tenant)
            .FirstAsync(s => s.TenantId == tenantId, ct);

        return MapToSubscriptionResponse(subscription);
    }

    public async Task<SubscriptionResponse> CancelSubscriptionAsync(
        Guid tenantId,
        bool atPeriodEnd,
        CancellationToken ct = default)
    {
        var subscription = await _context.TenantSubscriptions
            .Include(s => s.BillingPlan)
            .Include(s => s.Tenant)
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct)
            ?? throw new SubscriptionRequiredException(tenantId);

        var stripeService = new SubscriptionService();

        if (atPeriodEnd)
        {
            await stripeService.UpdateAsync(subscription.StripeSubscriptionId, new SubscriptionUpdateOptions
            {
                CancelAtPeriodEnd = true
            }, cancellationToken: ct);

            subscription.CancelAtPeriodEnd = true;
        }
        else
        {
            await stripeService.CancelAsync(subscription.StripeSubscriptionId, cancellationToken: ct);
            subscription.Status = SubscriptionStatus.Canceled;
        }

        subscription.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Canceled subscription for tenant {TenantId} (at period end: {AtPeriodEnd})",
            tenantId, atPeriodEnd);

        return MapToSubscriptionResponse(subscription);
    }

    public async Task<SubscriptionResponse> ReactivateSubscriptionAsync(Guid tenantId, CancellationToken ct = default)
    {
        var subscription = await _context.TenantSubscriptions
            .Include(s => s.BillingPlan)
            .Include(s => s.Tenant)
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct)
            ?? throw new SubscriptionRequiredException(tenantId);

        if (!subscription.CancelAtPeriodEnd)
        {
            throw new ArgumentException("Subscription is not scheduled for cancellation");
        }

        var stripeService = new SubscriptionService();
        await stripeService.UpdateAsync(subscription.StripeSubscriptionId, new SubscriptionUpdateOptions
        {
            CancelAtPeriodEnd = false
        }, cancellationToken: ct);

        subscription.CancelAtPeriodEnd = false;
        subscription.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Reactivated subscription for tenant {TenantId}", tenantId);

        return MapToSubscriptionResponse(subscription);
    }

    public async Task<UsageSummaryResponse> GetCurrentUsageAsync(Guid tenantId, CancellationToken ct = default)
    {
        var subscription = await _context.TenantSubscriptions
            .Include(s => s.BillingPlan)
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

        if (subscription == null)
        {
            var defaultEnd = DateTime.UtcNow.Date.AddMonths(1);
            return new UsageSummaryResponse
            {
                CurrentPeriodStart = DateTime.UtcNow.Date,
                CurrentPeriodEnd = defaultEnd,
                EmailsSent = 0,
                EmailsIncluded = 0,
                EmailsRemaining = 0,
                OverageEmails = 0,
                UsagePercentage = 0,
                EstimatedOverageCostCents = 0,
                DaysRemainingInPeriod = (defaultEnd - DateTime.UtcNow.Date).Days,
                IsCurrentPeriod = true
            };
        }

        var currentPeriod = await _context.UsagePeriods
            .FirstOrDefaultAsync(p =>
                p.TenantId == tenantId &&
                p.PeriodStart <= DateTime.UtcNow &&
                p.PeriodEnd > DateTime.UtcNow, ct);

        if (currentPeriod == null)
        {
            var includedEmails = subscription.BillingPlan?.IncludedEmails ?? 0;
            var daysRemaining = Math.Max(0, (subscription.CurrentPeriodEnd - DateTime.UtcNow).Days);
            return new UsageSummaryResponse
            {
                CurrentPeriodStart = subscription.CurrentPeriodStart,
                CurrentPeriodEnd = subscription.CurrentPeriodEnd,
                EmailsSent = 0,
                EmailsIncluded = includedEmails,
                EmailsRemaining = includedEmails,
                OverageEmails = 0,
                UsagePercentage = 0,
                EstimatedOverageCostCents = 0,
                DaysRemainingInPeriod = daysRemaining,
                IsCurrentPeriod = true
            };
        }

        var remainingIncluded = Math.Max(0, currentPeriod.IncludedEmailsLimit - currentPeriod.EmailsSent);
        var usagePercentage = currentPeriod.IncludedEmailsLimit > 0
            ? (decimal)currentPeriod.EmailsSent / currentPeriod.IncludedEmailsLimit * 100
            : 0;
        var overageCost = subscription.BillingPlan != null
            ? (int)(currentPeriod.OverageEmails / 1000m * subscription.BillingPlan.OverageRateCentsPer1K)
            : 0;
        var periodDaysRemaining = Math.Max(0, (currentPeriod.PeriodEnd - DateTime.UtcNow).Days);

        return new UsageSummaryResponse
        {
            PeriodId = currentPeriod.Id,
            CurrentPeriodStart = currentPeriod.PeriodStart,
            CurrentPeriodEnd = currentPeriod.PeriodEnd,
            EmailsSent = currentPeriod.EmailsSent,
            EmailsIncluded = currentPeriod.IncludedEmailsLimit,
            EmailsRemaining = remainingIncluded,
            OverageEmails = currentPeriod.OverageEmails,
            UsagePercentage = Math.Min(100, usagePercentage),
            EstimatedOverageCostCents = overageCost,
            DaysRemainingInPeriod = periodDaysRemaining,
            IsCurrentPeriod = true
        };
    }

    public async Task<IEnumerable<UsageSummaryResponse>> GetUsageHistoryAsync(
        Guid tenantId,
        int months = 6,
        CancellationToken ct = default)
    {
        var cutoffDate = DateTime.UtcNow.AddMonths(-months);

        var periods = await _context.UsagePeriods
            .Include(p => p.Subscription)
            .ThenInclude(s => s!.BillingPlan)
            .Where(p => p.TenantId == tenantId && p.PeriodStart >= cutoffDate)
            .OrderByDescending(p => p.PeriodStart)
            .ToListAsync(ct);

        return periods.Select(p =>
        {
            var remainingIncluded = Math.Max(0, p.IncludedEmailsLimit - p.EmailsSent);
            var usagePercentage = p.IncludedEmailsLimit > 0
                ? (decimal)p.EmailsSent / p.IncludedEmailsLimit * 100
                : 0;
            var overageCost = p.Subscription?.BillingPlan != null
                ? (int)(p.OverageEmails / 1000m * p.Subscription.BillingPlan.OverageRateCentsPer1K)
                : 0;
            var isCurrentPeriod = p.PeriodStart <= DateTime.UtcNow && p.PeriodEnd > DateTime.UtcNow;
            var daysRemaining = isCurrentPeriod ? Math.Max(0, (p.PeriodEnd - DateTime.UtcNow).Days) : 0;

            return new UsageSummaryResponse
            {
                PeriodId = p.Id,
                CurrentPeriodStart = p.PeriodStart,
                CurrentPeriodEnd = p.PeriodEnd,
                EmailsSent = p.EmailsSent,
                EmailsIncluded = p.IncludedEmailsLimit,
                EmailsRemaining = remainingIncluded,
                OverageEmails = p.OverageEmails,
                UsagePercentage = Math.Min(100, usagePercentage),
                EstimatedOverageCostCents = overageCost,
                DaysRemainingInPeriod = daysRemaining,
                IsCurrentPeriod = isCurrentPeriod
            };
        });
    }

    public async Task<IEnumerable<InvoiceResponse>> GetInvoicesAsync(Guid tenantId, CancellationToken ct = default)
    {
        var invoices = await _context.Invoices
            .Where(i => i.TenantId == tenantId)
            .OrderByDescending(i => i.CreatedAtUtc)
            .Take(24)
            .ToListAsync(ct);

        return invoices.Select(i => new InvoiceResponse
        {
            Id = i.Id,
            StripeInvoiceId = i.StripeInvoiceId,
            InvoiceNumber = i.InvoiceNumber,
            Status = i.Status.ToString(),
            AmountDueCents = i.AmountDueCents,
            AmountPaidCents = i.AmountPaidCents,
            SubtotalCents = i.SubtotalCents,
            TaxCents = i.TaxCents,
            TotalCents = i.TotalCents,
            Currency = i.Currency,
            InvoicePdfUrl = i.InvoicePdfUrl,
            HostedInvoiceUrl = i.HostedInvoiceUrl,
            PeriodStart = i.PeriodStart,
            PeriodEnd = i.PeriodEnd,
            DueDate = i.DueDate,
            CreatedAtUtc = i.CreatedAtUtc,
            PaidAtUtc = i.PaidAtUtc
        });
    }

    private async Task<StripeCustomers> GetOrCreateStripeCustomerAsync(Guid tenantId, CancellationToken ct)
    {
        var existing = await _context.StripeCustomers
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);

        if (existing != null)
        {
            return existing;
        }

        var tenant = await _context.Tenants.FindAsync([tenantId], ct)
            ?? throw new ArgumentException("Tenant not found");

        // Get owner's email for the Stripe customer (cached from Entra in TenantMembers)
        var ownerMember = await _context.TenantMembers
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.TenantRole == TenantRole.Owner, ct);

        var customerService = new CustomerService();
        var customer = await customerService.CreateAsync(new CustomerCreateOptions
        {
            Name = tenant.Name,
            Email = ownerMember?.UserEmail,
            Metadata = new Dictionary<string, string>
            {
                { "tenant_id", tenantId.ToString() }
            }
        }, cancellationToken: ct);

        var stripeCustomer = new StripeCustomers
        {
            TenantId = tenantId,
            StripeCustomerId = customer.Id,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.StripeCustomers.Add(stripeCustomer);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Created Stripe customer {CustomerId} for tenant {TenantId}",
            customer.Id, tenantId);

        return stripeCustomer;
    }

    private static BillingPlanResponse MapToPlanResponse(BillingPlans plan)
    {
        return new BillingPlanResponse
        {
            Id = plan.Id,
            Name = plan.Name,
            DisplayName = plan.DisplayName ?? plan.Name,
            MonthlyPriceCents = plan.MonthlyPriceCents,
            IncludedEmails = plan.IncludedEmails,
            OverageRateCentsPer1K = plan.OverageRateCentsPer1K,
            AllowsOverage = plan.AllowsOverage,
            MaxApiKeys = plan.MaxApiKeys,
            MaxDomains = plan.MaxDomains,
            MaxTeamMembers = plan.MaxTeamMembers,
            MaxWebhooks = plan.MaxWebhooks,
            MaxTemplates = plan.MaxTemplates,
            AnalyticsRetentionDays = plan.AnalyticsRetentionDays,
            HasDedicatedIp = plan.HasDedicatedIp,
            SupportLevel = plan.SupportLevel ?? "Email",
            StripePaymentLinkUrl = plan.StripePaymentLinkUrl
        };
    }

    private static SubscriptionResponse MapToSubscriptionResponse(TenantSubscriptions subscription)
    {
        return new SubscriptionResponse
        {
            Id = subscription.Id,
            TenantId = subscription.TenantId,
            Status = subscription.Status.ToString(),
            Plan = MapToPlanResponse(subscription.BillingPlan!),
            CurrentPeriodStart = subscription.CurrentPeriodStart,
            CurrentPeriodEnd = subscription.CurrentPeriodEnd,
            CancelAtPeriodEnd = subscription.CancelAtPeriodEnd,
            CanceledAt = subscription.CanceledAt,
            IsInGracePeriod = subscription.Tenant?.IsInGracePeriod ?? false,
            GracePeriodEndsAt = subscription.Tenant?.GracePeriodEndsAt,
            SendingEnabled = subscription.Tenant?.SendingDisabledAt == null
        };
    }
}

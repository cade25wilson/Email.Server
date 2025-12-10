using Email.Server.Configuration;
using Email.Server.Data;
using Email.Server.Models;
using Email.Server.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Stripe;

namespace Email.Server.Services.Implementations;

public class StripeWebhookService : IStripeWebhookService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<StripeWebhookService> _logger;
    private readonly StripeSettings _stripeSettings;
    private readonly BillingSettings _billingSettings;
    private readonly ISubscriptionEnforcementService _enforcementService;

    public StripeWebhookService(
        ApplicationDbContext context,
        ILogger<StripeWebhookService> logger,
        IOptions<StripeSettings> stripeSettings,
        IOptions<BillingSettings> billingSettings,
        ISubscriptionEnforcementService enforcementService)
    {
        _context = context;
        _logger = logger;
        _stripeSettings = stripeSettings.Value;
        _billingSettings = billingSettings.Value;
        _enforcementService = enforcementService;

        StripeConfiguration.ApiKey = _stripeSettings.SecretKey;
    }

    public async Task<bool> ProcessWebhookAsync(
        string payload,
        string signature,
        CancellationToken ct = default)
    {
        Event stripeEvent;

        try
        {
            stripeEvent = EventUtility.ConstructEvent(
                payload,
                signature,
                _stripeSettings.WebhookSecret);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Invalid Stripe webhook signature");
            return false;
        }

        // Idempotency check
        var existingEvent = await _context.StripeWebhookEvents
            .FirstOrDefaultAsync(e => e.EventId == stripeEvent.Id, ct);

        if (existingEvent?.Processed == true)
        {
            _logger.LogDebug("Skipping already processed event {EventId}", stripeEvent.Id);
            return true;
        }

        // Record event
        var webhookEvent = existingEvent ?? new StripeWebhookEvents
        {
            EventId = stripeEvent.Id,
            EventType = stripeEvent.Type,
            PayloadJson = payload,
            ReceivedAtUtc = DateTime.UtcNow
        };

        if (existingEvent == null)
        {
            _context.StripeWebhookEvents.Add(webhookEvent);
            await _context.SaveChangesAsync(ct);
        }

        try
        {
            await ProcessEventAsync(stripeEvent, ct);

            webhookEvent.Processed = true;
            webhookEvent.ProcessedSuccessfully = true;
            webhookEvent.ProcessedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Stripe event {EventId} ({EventType})",
                stripeEvent.Id, stripeEvent.Type);

            webhookEvent.Processed = true;
            webhookEvent.ProcessedSuccessfully = false;
            webhookEvent.ErrorMessage = ex.Message.Length > 2000
                ? ex.Message[..2000]
                : ex.Message;
            webhookEvent.ProcessedAtUtc = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(ct);
        return webhookEvent.ProcessedSuccessfully;
    }

    private async Task ProcessEventAsync(Event stripeEvent, CancellationToken ct)
    {
        _logger.LogInformation("Processing Stripe event {EventId} ({EventType})",
            stripeEvent.Id, stripeEvent.Type);

        switch (stripeEvent.Type)
        {
            case "customer.subscription.created":
                await HandleSubscriptionCreatedAsync(stripeEvent, ct);
                break;

            case "customer.subscription.updated":
                await HandleSubscriptionUpdatedAsync(stripeEvent, ct);
                break;

            case "customer.subscription.deleted":
                await HandleSubscriptionDeletedAsync(stripeEvent, ct);
                break;

            case "invoice.paid":
                await HandleInvoicePaidAsync(stripeEvent, ct);
                break;

            case "invoice.payment_failed":
                await HandleInvoicePaymentFailedAsync(stripeEvent, ct);
                break;

            case "invoice.created":
            case "invoice.updated":
            case "invoice.finalized":
                await HandleInvoiceUpdatedAsync(stripeEvent, ct);
                break;

            case "checkout.session.completed":
                await HandleCheckoutCompletedAsync(stripeEvent, ct);
                break;

            default:
                _logger.LogDebug("Ignoring unhandled event type: {EventType}", stripeEvent.Type);
                break;
        }
    }

    private async Task HandleSubscriptionCreatedAsync(Event stripeEvent, CancellationToken ct)
    {
        var subscription = stripeEvent.Data.Object as Subscription
            ?? throw new InvalidOperationException("Invalid subscription object");

        // Try to get tenant_id from metadata first, then fall back to customer lookup
        var tenantId = GetTenantIdFromMetadata(subscription.Metadata);
        if (tenantId == null)
        {
            // Try to find tenant via customer ID (linked during checkout)
            tenantId = await GetTenantIdFromCustomerAsync(subscription.CustomerId, ct);
        }

        if (tenantId == null)
        {
            _logger.LogWarning("No tenant_id in subscription metadata and customer not linked: {SubscriptionId}",
                subscription.Id);
            return;
        }

        var planId = GetPlanIdFromMetadata(subscription.Metadata);
        if (planId == null)
        {
            // Try to look up plan by Stripe price ID
            var priceId = subscription.Items.Data.FirstOrDefault()?.Price.Id;
            if (priceId != null)
            {
                var plan = await _context.BillingPlans
                    .FirstOrDefaultAsync(p => p.StripePriceId == priceId, ct);
                planId = plan?.Id;
            }
        }

        if (planId == null)
        {
            _logger.LogWarning("Could not determine plan for subscription {SubscriptionId}",
                subscription.Id);
            return;
        }

        // Check if subscription already exists
        var existingSubscription = await _context.TenantSubscriptions
            .FirstOrDefaultAsync(s => s.StripeSubscriptionId == subscription.Id, ct);

        if (existingSubscription != null)
        {
            _logger.LogDebug("Subscription {SubscriptionId} already exists, updating",
                subscription.Id);
            await UpdateLocalSubscriptionAsync(existingSubscription, subscription, planId.Value, ct);
            return;
        }

        // Get period dates from subscription item (v50 API)
        var firstItem = subscription.Items.Data.FirstOrDefault();
        var periodStart = firstItem?.CurrentPeriodStart ?? DateTime.UtcNow;
        var periodEnd = firstItem?.CurrentPeriodEnd ?? DateTime.UtcNow.AddMonths(1);

        var tenantSubscription = new TenantSubscriptions
        {
            TenantId = tenantId.Value,
            BillingPlanId = planId.Value,
            StripeSubscriptionId = subscription.Id,
            StripeCustomerId = subscription.CustomerId,
            Status = MapSubscriptionStatus(subscription.Status),
            CurrentPeriodStart = periodStart,
            CurrentPeriodEnd = periodEnd,
            TrialStart = subscription.TrialStart,
            TrialEnd = subscription.TrialEnd,
            CancelAtPeriodEnd = subscription.CancelAtPeriodEnd,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _context.TenantSubscriptions.Add(tenantSubscription);
        await _context.SaveChangesAsync(ct);

        // Create or update usage period for the new subscription
        await CreateOrUpdateUsagePeriodForSubscriptionAsync(tenantSubscription, planId.Value, ct);

        _logger.LogInformation(
            "Created subscription {SubscriptionId} for tenant {TenantId}",
            subscription.Id, tenantId);
    }

    private async Task HandleSubscriptionUpdatedAsync(Event stripeEvent, CancellationToken ct)
    {
        var subscription = stripeEvent.Data.Object as Subscription
            ?? throw new InvalidOperationException("Invalid subscription object");

        var existingSubscription = await _context.TenantSubscriptions
            .FirstOrDefaultAsync(s => s.StripeSubscriptionId == subscription.Id, ct);

        if (existingSubscription == null)
        {
            _logger.LogWarning("Subscription not found for update: {SubscriptionId}",
                subscription.Id);
            return;
        }

        // Determine plan ID
        var planId = existingSubscription.BillingPlanId;
        var priceId = subscription.Items.Data.FirstOrDefault()?.Price.Id;
        if (priceId != null)
        {
            var plan = await _context.BillingPlans
                .FirstOrDefaultAsync(p => p.StripePriceId == priceId, ct);
            if (plan != null)
            {
                planId = plan.Id;
            }
        }

        await UpdateLocalSubscriptionAsync(existingSubscription, subscription, planId, ct);

        // Update usage period if plan changed
        await CreateOrUpdateUsagePeriodForSubscriptionAsync(existingSubscription, planId, ct);
    }

    private async Task UpdateLocalSubscriptionAsync(
        TenantSubscriptions local,
        Subscription stripe,
        Guid planId,
        CancellationToken ct)
    {
        // Get period dates from subscription item (v50 API)
        var firstItem = stripe.Items.Data.FirstOrDefault();

        local.BillingPlanId = planId;
        local.Status = MapSubscriptionStatus(stripe.Status);
        local.CurrentPeriodStart = firstItem?.CurrentPeriodStart ?? local.CurrentPeriodStart;
        local.CurrentPeriodEnd = firstItem?.CurrentPeriodEnd ?? local.CurrentPeriodEnd;
        local.TrialStart = stripe.TrialStart;
        local.TrialEnd = stripe.TrialEnd;
        local.CancelAtPeriodEnd = stripe.CancelAtPeriodEnd;
        local.CanceledAt = stripe.CanceledAt;
        local.CancelAt = stripe.CancelAt;
        local.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Updated subscription {SubscriptionId} status to {Status}",
            stripe.Id, stripe.Status);
    }

    private async Task HandleSubscriptionDeletedAsync(Event stripeEvent, CancellationToken ct)
    {
        var subscription = stripeEvent.Data.Object as Subscription
            ?? throw new InvalidOperationException("Invalid subscription object");

        var existingSubscription = await _context.TenantSubscriptions
            .FirstOrDefaultAsync(s => s.StripeSubscriptionId == subscription.Id, ct);

        if (existingSubscription == null)
        {
            _logger.LogWarning("Subscription not found for deletion: {SubscriptionId}",
                subscription.Id);
            return;
        }

        existingSubscription.Status = SubscriptionStatus.Canceled;
        existingSubscription.CanceledAt = DateTime.UtcNow;
        existingSubscription.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Marked subscription {SubscriptionId} as canceled for tenant {TenantId}",
            subscription.Id, existingSubscription.TenantId);
    }

    private async Task HandleInvoicePaidAsync(Event stripeEvent, CancellationToken ct)
    {
        var invoice = stripeEvent.Data.Object as Invoice
            ?? throw new InvalidOperationException("Invalid invoice object");

        // Update or create local invoice
        await UpsertInvoiceAsync(invoice, ct);

        // Clear grace period if tenant was in one
        var tenantId = await GetTenantIdFromCustomerAsync(invoice.CustomerId, ct);
        if (tenantId != null)
        {
            await _enforcementService.EndGracePeriodAsync(tenantId.Value, ct);
            await _enforcementService.EnableSendingAsync(tenantId.Value, ct);

            _logger.LogInformation(
                "Invoice {InvoiceId} paid - cleared grace period for tenant {TenantId}",
                invoice.Id, tenantId);
        }
    }

    private async Task HandleInvoicePaymentFailedAsync(Event stripeEvent, CancellationToken ct)
    {
        var invoice = stripeEvent.Data.Object as Invoice
            ?? throw new InvalidOperationException("Invalid invoice object");

        await UpsertInvoiceAsync(invoice, ct);

        var tenantId = await GetTenantIdFromCustomerAsync(invoice.CustomerId, ct);
        if (tenantId != null)
        {
            await _enforcementService.StartGracePeriodAsync(
                tenantId.Value,
                $"Payment failed for invoice {invoice.Number ?? invoice.Id}",
                ct);

            _logger.LogWarning(
                "Invoice {InvoiceId} payment failed - started grace period for tenant {TenantId}",
                invoice.Id, tenantId);
        }
    }

    private async Task HandleInvoiceUpdatedAsync(Event stripeEvent, CancellationToken ct)
    {
        var invoice = stripeEvent.Data.Object as Invoice
            ?? throw new InvalidOperationException("Invalid invoice object");

        await UpsertInvoiceAsync(invoice, ct);
    }

    private async Task UpsertInvoiceAsync(Invoice stripeInvoice, CancellationToken ct)
    {
        var tenantId = await GetTenantIdFromCustomerAsync(stripeInvoice.CustomerId, ct);
        if (tenantId == null)
        {
            _logger.LogWarning("Could not find tenant for customer {CustomerId}",
                stripeInvoice.CustomerId);
            return;
        }

        var existingInvoice = await _context.Invoices
            .FirstOrDefaultAsync(i => i.StripeInvoiceId == stripeInvoice.Id, ct);

        // Calculate tax from subtotal and total (total - subtotal = tax + fees)
        var taxCents = (int)Math.Max(0, stripeInvoice.Total - stripeInvoice.Subtotal);

        if (existingInvoice != null)
        {
            existingInvoice.Status = MapInvoiceStatus(stripeInvoice.Status);
            existingInvoice.AmountDueCents = (int)stripeInvoice.AmountDue;
            existingInvoice.AmountPaidCents = (int)stripeInvoice.AmountPaid;
            existingInvoice.SubtotalCents = (int)stripeInvoice.Subtotal;
            existingInvoice.TaxCents = taxCents;
            existingInvoice.TotalCents = (int)stripeInvoice.Total;
            existingInvoice.InvoicePdfUrl = stripeInvoice.InvoicePdf;
            existingInvoice.HostedInvoiceUrl = stripeInvoice.HostedInvoiceUrl;
            existingInvoice.PaidAtUtc = stripeInvoice.StatusTransitions?.PaidAt;
        }
        else
        {
            var invoice = new Invoices
            {
                TenantId = tenantId.Value,
                StripeInvoiceId = stripeInvoice.Id,
                InvoiceNumber = stripeInvoice.Number,
                Status = MapInvoiceStatus(stripeInvoice.Status),
                AmountDueCents = (int)stripeInvoice.AmountDue,
                AmountPaidCents = (int)stripeInvoice.AmountPaid,
                SubtotalCents = (int)stripeInvoice.Subtotal,
                TaxCents = taxCents,
                TotalCents = (int)stripeInvoice.Total,
                Currency = stripeInvoice.Currency ?? "usd",
                InvoicePdfUrl = stripeInvoice.InvoicePdf,
                HostedInvoiceUrl = stripeInvoice.HostedInvoiceUrl,
                PeriodStart = stripeInvoice.PeriodStart,
                PeriodEnd = stripeInvoice.PeriodEnd,
                DueDate = stripeInvoice.DueDate,
                CreatedAtUtc = DateTime.UtcNow,
                PaidAtUtc = stripeInvoice.StatusTransitions?.PaidAt
            };

            _context.Invoices.Add(invoice);
        }

        await _context.SaveChangesAsync(ct);
    }

    private async Task HandleCheckoutCompletedAsync(Event stripeEvent, CancellationToken ct)
    {
        var session = stripeEvent.Data.Object as Stripe.Checkout.Session
            ?? throw new InvalidOperationException("Invalid checkout session object");

        // Try to get tenant_id from metadata first, then fall back to client_reference_id (for Payment Links)
        var tenantId = GetTenantIdFromMetadata(session.Metadata);
        if (tenantId == null && !string.IsNullOrEmpty(session.ClientReferenceId))
        {
            if (Guid.TryParse(session.ClientReferenceId, out var refId))
            {
                tenantId = refId;
                _logger.LogInformation("Got tenant_id from client_reference_id: {TenantId}", tenantId);
            }
        }

        if (tenantId == null)
        {
            _logger.LogWarning("No tenant_id in checkout session metadata or client_reference_id: {SessionId}",
                session.Id);
            return;
        }

        // Link customer to tenant if not already linked
        var existingCustomer = await _context.StripeCustomers
            .FirstOrDefaultAsync(c => c.TenantId == tenantId.Value, ct);

        if (existingCustomer == null && session.CustomerId != null)
        {
            var stripeCustomer = new StripeCustomers
            {
                TenantId = tenantId.Value,
                StripeCustomerId = session.CustomerId,
                CreatedAtUtc = DateTime.UtcNow
            };

            _context.StripeCustomers.Add(stripeCustomer);
            await _context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Linked Stripe customer {CustomerId} to tenant {TenantId}",
                session.CustomerId, tenantId);
        }

        // Enable billing for tenant
        var tenant = await _context.Tenants.FindAsync([tenantId.Value], ct);
        if (tenant != null)
        {
            tenant.IsBillingEnabled = true;
            await _context.SaveChangesAsync(ct);
        }
    }

    private async Task<Guid?> GetTenantIdFromCustomerAsync(string? customerId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(customerId))
            return null;

        var customer = await _context.StripeCustomers
            .FirstOrDefaultAsync(c => c.StripeCustomerId == customerId, ct);

        return customer?.TenantId;
    }

    private static Guid? GetTenantIdFromMetadata(Dictionary<string, string>? metadata)
    {
        if (metadata == null || !metadata.TryGetValue("tenant_id", out var tenantIdStr))
            return null;

        return Guid.TryParse(tenantIdStr, out var tenantId) ? tenantId : null;
    }

    private static Guid? GetPlanIdFromMetadata(Dictionary<string, string>? metadata)
    {
        if (metadata == null || !metadata.TryGetValue("plan_id", out var planIdStr))
            return null;

        return Guid.TryParse(planIdStr, out var planId) ? planId : null;
    }

    private static SubscriptionStatus MapSubscriptionStatus(string stripeStatus)
    {
        return stripeStatus switch
        {
            "trialing" => SubscriptionStatus.Trialing,
            "active" => SubscriptionStatus.Active,
            "past_due" => SubscriptionStatus.PastDue,
            "canceled" => SubscriptionStatus.Canceled,
            "unpaid" => SubscriptionStatus.Unpaid,
            "paused" => SubscriptionStatus.Paused,
            _ => SubscriptionStatus.Active
        };
    }

    private static InvoiceStatus MapInvoiceStatus(string? stripeStatus)
    {
        return stripeStatus switch
        {
            "draft" => InvoiceStatus.Draft,
            "open" => InvoiceStatus.Open,
            "paid" => InvoiceStatus.Paid,
            "void" => InvoiceStatus.Void,
            "uncollectible" => InvoiceStatus.Uncollectible,
            _ => InvoiceStatus.Draft
        };
    }

    private async Task CreateOrUpdateUsagePeriodForSubscriptionAsync(
        TenantSubscriptions subscription,
        Guid planId,
        CancellationToken ct)
    {
        var plan = await _context.BillingPlans.FindAsync([planId], ct);
        if (plan == null)
        {
            _logger.LogWarning("Plan {PlanId} not found when creating usage period", planId);
            return;
        }

        var now = DateTime.UtcNow;

        // Find existing period that overlaps with the subscription period
        var existingPeriod = await _context.UsagePeriods
            .FirstOrDefaultAsync(p =>
                p.TenantId == subscription.TenantId &&
                p.PeriodStart <= now &&
                p.PeriodEnd > now, ct);

        if (existingPeriod != null)
        {
            // Update existing period with correct subscription info and limits
            existingPeriod.SubscriptionId = subscription.Id;
            existingPeriod.PeriodStart = subscription.CurrentPeriodStart;
            existingPeriod.PeriodEnd = subscription.CurrentPeriodEnd;
            existingPeriod.IncludedEmailsLimit = plan.IncludedEmails;

            // Recalculate overage based on new limit
            existingPeriod.OverageEmails = Math.Max(0, existingPeriod.EmailsSent - plan.IncludedEmails);

            _logger.LogInformation(
                "Updated usage period {PeriodId} for tenant {TenantId} with limit {Limit}",
                existingPeriod.Id, subscription.TenantId, plan.IncludedEmails);
        }
        else
        {
            // Create new period
            var newPeriod = new UsagePeriods
            {
                TenantId = subscription.TenantId,
                SubscriptionId = subscription.Id,
                PeriodStart = subscription.CurrentPeriodStart,
                PeriodEnd = subscription.CurrentPeriodEnd,
                EmailsSent = 0,
                IncludedEmailsLimit = plan.IncludedEmails,
                OverageEmails = 0,
                OverageReportedToStripe = 0
            };

            _context.UsagePeriods.Add(newPeriod);

            _logger.LogInformation(
                "Created usage period for tenant {TenantId} with limit {Limit}",
                subscription.TenantId, plan.IncludedEmails);
        }

        await _context.SaveChangesAsync(ct);
    }
}

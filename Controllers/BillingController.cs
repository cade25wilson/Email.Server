using Email.Server.DTOs.Requests.Billing;
using Email.Server.Exceptions;
using Email.Server.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Email.Server.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize(AuthenticationSchemes = "ApiKey,Bearer")]
public class BillingController(
    IBillingService billingService,
    ISubscriptionEnforcementService enforcementService,
    ITenantContextService tenantContext,
    ILogger<BillingController> logger) : ControllerBase
{
    private readonly IBillingService _billingService = billingService;
    private readonly ISubscriptionEnforcementService _enforcementService = enforcementService;
    private readonly ITenantContextService _tenantContext = tenantContext;
    private readonly ILogger<BillingController> _logger = logger;

    /// <summary>
    /// Get available billing plans
    /// </summary>
    [HttpGet("plans")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPlans(CancellationToken ct)
    {
        try
        {
            var plans = await _billingService.GetAvailablePlansAsync(ct);
            return Ok(plans);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving billing plans");
            return StatusCode(500, new { error = "An error occurred while retrieving plans" });
        }
    }

    /// <summary>
    /// Get current subscription for the authenticated tenant
    /// </summary>
    [HttpGet("subscription")]
    [Authorize]
    public async Task<IActionResult> GetSubscription(CancellationToken ct)
    {
        try
        {
            var tenantId = _tenantContext.GetTenantId();
            var subscription = await _billingService.GetCurrentSubscriptionAsync(tenantId, ct);

            if (subscription == null)
            {
                return NotFound(new { error = "No active subscription found" });
            }

            return Ok(subscription);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving subscription");
            return StatusCode(500, new { error = "An error occurred while retrieving subscription" });
        }
    }

    /// <summary>
    /// Create a Stripe checkout session for plan purchase/upgrade
    /// </summary>
    [HttpPost("checkout")]
    [Authorize]
    public async Task<IActionResult> CreateCheckoutSession(
        [FromBody] CreateCheckoutRequest request,
        CancellationToken ct)
    {
        try
        {
            var tenantId = _tenantContext.GetTenantId();
            var session = await _billingService.CreateCheckoutSessionAsync(
                tenantId,
                request.PlanId,
                request.SuccessUrl,
                request.CancelUrl,
                ct);

            return Ok(session);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating checkout session");
            return StatusCode(500, new { error = "An error occurred while creating checkout session" });
        }
    }

    /// <summary>
    /// Create a Stripe customer portal session for subscription management
    /// </summary>
    [HttpPost("portal")]
    [Authorize]
    public async Task<IActionResult> CreatePortalSession(
        [FromBody] CreatePortalSessionRequest request,
        CancellationToken ct)
    {
        try
        {
            var tenantId = _tenantContext.GetTenantId();
            var session = await _billingService.CreateCustomerPortalSessionAsync(
                tenantId,
                request.ReturnUrl,
                ct);

            return Ok(session);
        }
        catch (SubscriptionRequiredException)
        {
            return BadRequest(new { error = "No active subscription found. Please subscribe to a plan first." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating customer portal session");
            return StatusCode(500, new { error = "An error occurred while creating portal session" });
        }
    }

    /// <summary>
    /// Change subscription plan
    /// </summary>
    [HttpPut("subscription/plan")]
    [Authorize]
    public async Task<IActionResult> ChangePlan(
        [FromBody] ChangePlanRequest request,
        CancellationToken ct)
    {
        try
        {
            var tenantId = _tenantContext.GetTenantId();
            var subscription = await _billingService.ChangePlanAsync(tenantId, request.NewPlanId, ct);
            return Ok(subscription);
        }
        catch (SubscriptionRequiredException)
        {
            return BadRequest(new { error = "No active subscription found" });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing plan");
            return StatusCode(500, new { error = "An error occurred while changing plan" });
        }
    }

    /// <summary>
    /// Cancel subscription
    /// </summary>
    [HttpPost("subscription/cancel")]
    [Authorize]
    public async Task<IActionResult> CancelSubscription(
        [FromBody] CancelSubscriptionRequest request,
        CancellationToken ct)
    {
        try
        {
            var tenantId = _tenantContext.GetTenantId();
            var subscription = await _billingService.CancelSubscriptionAsync(
                tenantId,
                request.AtPeriodEnd,
                ct);

            return Ok(subscription);
        }
        catch (SubscriptionRequiredException)
        {
            return BadRequest(new { error = "No active subscription found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error canceling subscription");
            return StatusCode(500, new { error = "An error occurred while canceling subscription" });
        }
    }

    /// <summary>
    /// Reactivate a subscription scheduled for cancellation
    /// </summary>
    [HttpPost("subscription/reactivate")]
    [Authorize]
    public async Task<IActionResult> ReactivateSubscription(CancellationToken ct)
    {
        try
        {
            var tenantId = _tenantContext.GetTenantId();
            var subscription = await _billingService.ReactivateSubscriptionAsync(tenantId, ct);
            return Ok(subscription);
        }
        catch (SubscriptionRequiredException)
        {
            return BadRequest(new { error = "No active subscription found" });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reactivating subscription");
            return StatusCode(500, new { error = "An error occurred while reactivating subscription" });
        }
    }

    /// <summary>
    /// Get current usage summary
    /// </summary>
    [HttpGet("usage")]
    [Authorize]
    public async Task<IActionResult> GetUsage(CancellationToken ct)
    {
        try
        {
            var tenantId = _tenantContext.GetTenantId();
            var usage = await _billingService.GetCurrentUsageAsync(tenantId, ct);
            return Ok(usage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving usage");
            return StatusCode(500, new { error = "An error occurred while retrieving usage" });
        }
    }

    /// <summary>
    /// Get usage history
    /// </summary>
    [HttpGet("usage/history")]
    [Authorize]
    public async Task<IActionResult> GetUsageHistory(
        [FromQuery] int months = 6,
        CancellationToken ct = default)
    {
        try
        {
            var tenantId = _tenantContext.GetTenantId();
            var history = await _billingService.GetUsageHistoryAsync(tenantId, months, ct);
            return Ok(history);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving usage history");
            return StatusCode(500, new { error = "An error occurred while retrieving usage history" });
        }
    }

    /// <summary>
    /// Get invoice history
    /// </summary>
    [HttpGet("invoices")]
    [Authorize]
    public async Task<IActionResult> GetInvoices(CancellationToken ct)
    {
        try
        {
            var tenantId = _tenantContext.GetTenantId();
            var invoices = await _billingService.GetInvoicesAsync(tenantId, ct);
            return Ok(invoices);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving invoices");
            return StatusCode(500, new { error = "An error occurred while retrieving invoices" });
        }
    }

    /// <summary>
    /// Get current plan limits and usage
    /// </summary>
    [HttpGet("limits")]
    [Authorize]
    public async Task<IActionResult> GetLimits(CancellationToken ct)
    {
        try
        {
            var tenantId = _tenantContext.GetTenantId();
            var limits = await _enforcementService.GetCurrentLimitsAsync(tenantId, ct);
            return Ok(limits);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving plan limits");
            return StatusCode(500, new { error = "An error occurred while retrieving plan limits" });
        }
    }
}

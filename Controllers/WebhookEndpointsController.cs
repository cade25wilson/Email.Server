using Email.Server.DTOs.Webhooks;
using Email.Server.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Email.Server.Controllers;

[ApiController]
[Route("api/v1/webhook-endpoints")]
[Authorize(AuthenticationSchemes = "ApiKey,Bearer")]
public class WebhookEndpointsController(
    IWebhookDeliveryService webhookDeliveryService,
    ILogger<WebhookEndpointsController> logger) : ControllerBase
{
    private readonly IWebhookDeliveryService _webhookDeliveryService = webhookDeliveryService;
    private readonly ILogger<WebhookEndpointsController> _logger = logger;

    private Guid GetTenantId()
    {
        var tenantIdClaim = User.FindFirstValue("TenantId");
        if (string.IsNullOrEmpty(tenantIdClaim) || !Guid.TryParse(tenantIdClaim, out var tenantId))
        {
            throw new UnauthorizedAccessException("Tenant ID not found in claims");
        }
        return tenantId;
    }

    /// <summary>
    /// Get all webhook endpoints for the current tenant.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<WebhookEndpointResponse>>> GetEndpoints(
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var endpoints = await _webhookDeliveryService.GetEndpointsAsync(tenantId, cancellationToken);
        return Ok(endpoints);
    }

    /// <summary>
    /// Get available event types for webhooks.
    /// </summary>
    [HttpGet("event-types")]
    public ActionResult<IEnumerable<WebhookEventTypeResponse>> GetEventTypes()
    {
        var eventTypes = _webhookDeliveryService.GetAvailableEventTypes();
        return Ok(eventTypes);
    }

    /// <summary>
    /// Get a specific webhook endpoint.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<WebhookEndpointResponse>> GetEndpoint(
        Guid id,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var endpoint = await _webhookDeliveryService.GetEndpointAsync(tenantId, id, cancellationToken);

        if (endpoint == null)
            return NotFound(new { message = $"Webhook endpoint {id} not found" });

        return Ok(endpoint);
    }

    /// <summary>
    /// Create a new webhook endpoint.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<WebhookEndpointCreatedResponse>> CreateEndpoint(
        [FromBody] CreateWebhookEndpointRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var tenantId = GetTenantId();
            var (endpoint, secret) = await _webhookDeliveryService.CreateEndpointAsync(
                tenantId, request, cancellationToken);

            _logger.LogInformation("Created webhook endpoint {EndpointId} for tenant {TenantId}",
                endpoint.Id, tenantId);

            // Return the secret only on creation - user must save it now
            return CreatedAtAction(
                nameof(GetEndpoint),
                new { id = endpoint.Id },
                new WebhookEndpointCreatedResponse
                {
                    Endpoint = endpoint,
                    Secret = secret
                });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Update a webhook endpoint.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<WebhookEndpointResponse>> UpdateEndpoint(
        Guid id,
        [FromBody] UpdateWebhookEndpointRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = GetTenantId();
            var endpoint = await _webhookDeliveryService.UpdateEndpointAsync(
                tenantId, id, request, cancellationToken);

            return Ok(endpoint);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = $"Webhook endpoint {id} not found" });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Delete a webhook endpoint.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> DeleteEndpoint(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = GetTenantId();
            await _webhookDeliveryService.DeleteEndpointAsync(tenantId, id, cancellationToken);

            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = $"Webhook endpoint {id} not found" });
        }
    }

    /// <summary>
    /// Test a webhook endpoint.
    /// </summary>
    [HttpPost("{id:guid}/test")]
    public async Task<ActionResult<WebhookTestResponse>> TestEndpoint(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = GetTenantId();
            var result = await _webhookDeliveryService.TestWebhookAsync(tenantId, id, cancellationToken);

            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = $"Webhook endpoint {id} not found" });
        }
    }

    /// <summary>
    /// Get delivery history for a webhook endpoint.
    /// </summary>
    [HttpGet("{id:guid}/deliveries")]
    public async Task<ActionResult<IEnumerable<WebhookDeliveryResponse>>> GetDeliveries(
        Guid id,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tenantId = GetTenantId();
            var deliveries = await _webhookDeliveryService.GetDeliveriesAsync(
                tenantId, id, Math.Min(limit, 100), cancellationToken);

            return Ok(deliveries);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = $"Webhook endpoint {id} not found" });
        }
    }
}

/// <summary>
/// Response DTO for webhook creation that includes the secret.
/// </summary>
public class WebhookEndpointCreatedResponse
{
    public WebhookEndpointResponse Endpoint { get; set; } = null!;
    public string Secret { get; set; } = string.Empty;
}

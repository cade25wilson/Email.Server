using Email.Server.DTOs.Webhooks;
using Email.Server.Models;

namespace Email.Server.Services.Interfaces;

public interface IWebhookDeliveryService
{
    /// <summary>
    /// Creates a new webhook endpoint for a tenant.
    /// </summary>
    Task<(WebhookEndpointResponse Endpoint, string Secret)> CreateEndpointAsync(
        Guid tenantId,
        CreateWebhookEndpointRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing webhook endpoint.
    /// </summary>
    Task<WebhookEndpointResponse> UpdateEndpointAsync(
        Guid tenantId,
        Guid endpointId,
        UpdateWebhookEndpointRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a webhook endpoint.
    /// </summary>
    Task DeleteEndpointAsync(
        Guid tenantId,
        Guid endpointId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all webhook endpoints for a tenant.
    /// </summary>
    Task<IEnumerable<WebhookEndpointResponse>> GetEndpointsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific webhook endpoint.
    /// </summary>
    Task<WebhookEndpointResponse?> GetEndpointAsync(
        Guid tenantId,
        Guid endpointId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets delivery history for an endpoint.
    /// </summary>
    Task<IEnumerable<WebhookDeliveryResponse>> GetDeliveriesAsync(
        Guid tenantId,
        Guid endpointId,
        int limit = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Triggers webhooks for a message event (called by SesNotificationService).
    /// </summary>
    Task TriggerWebhooksForEventAsync(
        MessageEvents messageEvent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests a webhook endpoint by sending a test payload.
    /// </summary>
    Task<WebhookTestResponse> TestWebhookAsync(
        Guid tenantId,
        Guid endpointId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes pending webhook deliveries (called by background service).
    /// </summary>
    Task ProcessPendingDeliveriesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the list of available event types.
    /// </summary>
    IEnumerable<WebhookEventTypeResponse> GetAvailableEventTypes();

    /// <summary>
    /// Queues webhook delivery for a custom event (like inbound email).
    /// Delivers immediately to all matching endpoints without using MessageEvents.
    /// </summary>
    Task QueueWebhookDeliveryAsync(
        Guid tenantId,
        string eventType,
        string payloadJson,
        CancellationToken cancellationToken = default);
}

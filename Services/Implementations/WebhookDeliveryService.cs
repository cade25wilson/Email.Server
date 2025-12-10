using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Email.Server.Data;
using Email.Server.DTOs.Webhooks;
using Email.Server.Models;
using Email.Server.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Email.Server.Services.Implementations;

public class WebhookDeliveryService(
    ApplicationDbContext context,
    IHttpClientFactory httpClientFactory,
    ILogger<WebhookDeliveryService> logger) : IWebhookDeliveryService
{
    private readonly ApplicationDbContext _context = context;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly ILogger<WebhookDeliveryService> _logger = logger;

    private static readonly List<WebhookEventTypeResponse> AvailableEventTypes =
    [
        new() { Name = "email.sent", Description = "Email was successfully sent to SES" },
        new() { Name = "email.delivered", Description = "Email was delivered to recipient" },
        new() { Name = "email.bounced", Description = "Email bounced (hard or soft)" },
        new() { Name = "email.complained", Description = "Recipient marked as spam" },
        new() { Name = "email.opened", Description = "Email was opened (requires tracking)" },
        new() { Name = "email.clicked", Description = "Link in email was clicked (requires tracking)" },
        new() { Name = "email.rejected", Description = "SES rejected the email" },
        new() { Name = "email.rendering_failed", Description = "Template rendering failed" },
        new() { Name = "email.inbound", Description = "Inbound email received" }
    ];

    // Retry intervals in minutes: 1, 5, 15, 60, 240 (4 hours)
    private static readonly int[] RetryIntervalsMinutes = [1, 5, 15, 60, 240];
    private const int MaxRetryAttempts = 5;
    private const int WebhookTimeoutSeconds = 30;
    private const int MaxResponseBodyLength = 1000;

    public IEnumerable<WebhookEventTypeResponse> GetAvailableEventTypes() => AvailableEventTypes;

    public async Task<(WebhookEndpointResponse Endpoint, string Secret)> CreateEndpointAsync(
        Guid tenantId,
        CreateWebhookEndpointRequest request,
        CancellationToken cancellationToken = default)
    {
        // Validate event types
        var validEventTypes = AvailableEventTypes.Select(e => e.Name).ToHashSet();
        var invalidTypes = request.EventTypes.Where(t => !validEventTypes.Contains(t)).ToList();
        if (invalidTypes.Count > 0)
        {
            throw new ArgumentException($"Invalid event types: {string.Join(", ", invalidTypes)}");
        }

        // Validate HTTPS URL
        if (!request.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Webhook URL must use HTTPS");
        }

        // Generate HMAC secret
        var secretBytes = RandomNumberGenerator.GetBytes(32);
        var secretHex = Convert.ToHexString(secretBytes);

        var endpoint = new WebhookEndpoints
        {
            TenantId = tenantId,
            Name = request.Name,
            Url = request.Url,
            EventTypes = string.Join(",", request.EventTypes),
            Secret = secretBytes,
            Enabled = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.WebhookEndpoints.Add(endpoint);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created webhook endpoint {EndpointId} for tenant {TenantId}", endpoint.Id, tenantId);

        return (MapToResponse(endpoint), secretHex);
    }

    public async Task<WebhookEndpointResponse> UpdateEndpointAsync(
        Guid tenantId,
        Guid endpointId,
        UpdateWebhookEndpointRequest request,
        CancellationToken cancellationToken = default)
    {
        var endpoint = await _context.WebhookEndpoints
            .FirstOrDefaultAsync(e => e.Id == endpointId && e.TenantId == tenantId, cancellationToken)
            ?? throw new KeyNotFoundException($"Webhook endpoint {endpointId} not found");

        if (request.Name != null)
            endpoint.Name = request.Name;

        if (request.Url != null)
        {
            if (!request.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Webhook URL must use HTTPS");
            }
            endpoint.Url = request.Url;
        }

        if (request.EventTypes != null)
        {
            var validEventTypes = AvailableEventTypes.Select(e => e.Name).ToHashSet();
            var invalidTypes = request.EventTypes.Where(t => !validEventTypes.Contains(t)).ToList();
            if (invalidTypes.Count > 0)
            {
                throw new ArgumentException($"Invalid event types: {string.Join(", ", invalidTypes)}");
            }
            endpoint.EventTypes = string.Join(",", request.EventTypes);
        }

        if (request.Enabled.HasValue)
            endpoint.Enabled = request.Enabled.Value;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated webhook endpoint {EndpointId}", endpointId);

        return MapToResponse(endpoint);
    }

    public async Task DeleteEndpointAsync(
        Guid tenantId,
        Guid endpointId,
        CancellationToken cancellationToken = default)
    {
        var endpoint = await _context.WebhookEndpoints
            .FirstOrDefaultAsync(e => e.Id == endpointId && e.TenantId == tenantId, cancellationToken)
            ?? throw new KeyNotFoundException($"Webhook endpoint {endpointId} not found");

        // Delete associated deliveries first
        var deliveries = await _context.WebhookDeliveries
            .Where(d => d.EndpointId == endpointId)
            .ToListAsync(cancellationToken);

        _context.WebhookDeliveries.RemoveRange(deliveries);
        _context.WebhookEndpoints.Remove(endpoint);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted webhook endpoint {EndpointId}", endpointId);
    }

    public async Task<IEnumerable<WebhookEndpointResponse>> GetEndpointsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var endpoints = await _context.WebhookEndpoints
            .Where(e => e.TenantId == tenantId)
            .OrderByDescending(e => e.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return endpoints.Select(MapToResponse);
    }

    public async Task<WebhookEndpointResponse?> GetEndpointAsync(
        Guid tenantId,
        Guid endpointId,
        CancellationToken cancellationToken = default)
    {
        var endpoint = await _context.WebhookEndpoints
            .FirstOrDefaultAsync(e => e.Id == endpointId && e.TenantId == tenantId, cancellationToken);

        return endpoint == null ? null : MapToResponse(endpoint);
    }

    public async Task<IEnumerable<WebhookDeliveryResponse>> GetDeliveriesAsync(
        Guid tenantId,
        Guid endpointId,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        // Verify endpoint belongs to tenant
        var endpointExists = await _context.WebhookEndpoints
            .AnyAsync(e => e.Id == endpointId && e.TenantId == tenantId, cancellationToken);

        if (!endpointExists)
            throw new KeyNotFoundException($"Webhook endpoint {endpointId} not found");

        var deliveries = await _context.WebhookDeliveries
            .Include(d => d.Event)
            .Where(d => d.EndpointId == endpointId)
            .OrderByDescending(d => d.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return deliveries.Select(MapDeliveryToResponse);
    }

    public async Task TriggerWebhooksForEventAsync(
        MessageEvents messageEvent,
        CancellationToken cancellationToken = default)
    {
        // Map SES event type to our webhook event type
        var webhookEventType = MapSesEventTypeToWebhookType(messageEvent.EventType);
        if (webhookEventType == null)
        {
            _logger.LogWarning("Unknown SES event type: {EventType}", messageEvent.EventType);
            return;
        }

        // Find all enabled endpoints for this tenant that subscribe to this event type
        var endpoints = await _context.WebhookEndpoints
            .Where(e => e.TenantId == messageEvent.TenantId && e.Enabled)
            .ToListAsync(cancellationToken);

        var matchingEndpoints = endpoints
            .Where(e => !string.IsNullOrEmpty(e.EventTypes) &&
                        e.EventTypes.Split(',').Contains(webhookEventType))
            .ToList();

        if (matchingEndpoints.Count == 0)
        {
            _logger.LogDebug("No webhook endpoints configured for event type {EventType} in tenant {TenantId}",
                webhookEventType, messageEvent.TenantId);
            return;
        }

        // Create delivery records for each matching endpoint
        foreach (var endpoint in matchingEndpoints)
        {
            var delivery = new WebhookDeliveries
            {
                EndpointId = endpoint.Id,
                EventId = messageEvent.Id,
                Status = 0, // Pending
                AttemptCount = 0
            };

            _context.WebhookDeliveries.Add(delivery);
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Queued {Count} webhook deliveries for event {EventId} ({EventType})",
            matchingEndpoints.Count, messageEvent.Id, webhookEventType);
    }

    public async Task<WebhookTestResponse> TestWebhookAsync(
        Guid tenantId,
        Guid endpointId,
        CancellationToken cancellationToken = default)
    {
        var endpoint = await _context.WebhookEndpoints
            .FirstOrDefaultAsync(e => e.Id == endpointId && e.TenantId == tenantId, cancellationToken)
            ?? throw new KeyNotFoundException($"Webhook endpoint {endpointId} not found");

        var testPayload = new
        {
            id = $"test_{Guid.NewGuid():N}",
            type = "test",
            timestamp = DateTime.UtcNow.ToString("O"),
            data = new
            {
                message = "This is a test webhook from your Email API",
                endpoint_id = endpointId.ToString()
            }
        };

        var (success, statusCode, errorMessage) = await DeliverWebhookAsync(
            endpoint.Url,
            endpoint.Secret,
            testPayload,
            cancellationToken);

        return new WebhookTestResponse
        {
            Success = success,
            StatusCode = statusCode,
            Message = success
                ? "Test webhook delivered successfully"
                : $"Test webhook failed: {errorMessage}"
        };
    }

    public async Task ProcessPendingDeliveriesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        // Get pending deliveries or retries that are due
        var deliveries = await _context.WebhookDeliveries
            .Include(d => d.Endpoint)
            .Include(d => d.Event)
            .Where(d => (d.Status == 0) || // Pending
                        (d.Status == 2 && d.NextRetryAtUtc != null && d.NextRetryAtUtc <= now)) // Retry due
            .Where(d => d.Endpoint != null && d.Endpoint.Enabled)
            .Take(100) // Process in batches
            .ToListAsync(cancellationToken);

        if (deliveries.Count == 0)
            return;

        _logger.LogInformation("Processing {Count} pending webhook deliveries", deliveries.Count);

        foreach (var delivery in deliveries)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            await ProcessDeliveryAsync(delivery, cancellationToken);
        }
    }

    private async Task ProcessDeliveryAsync(WebhookDeliveries delivery, CancellationToken cancellationToken)
    {
        if (delivery.Endpoint == null || delivery.Event == null)
            return;

        var webhookEventType = MapSesEventTypeToWebhookType(delivery.Event.EventType);
        if (webhookEventType == null)
            return;

        var payload = new
        {
            id = $"evt_{delivery.Event.Id}",
            type = webhookEventType,
            timestamp = DateTime.UtcNow.ToString("O"),
            data = new
            {
                message_id = delivery.Event.MessageId?.ToString(),
                recipient = delivery.Event.Recipient,
                event_type = delivery.Event.EventType,
                occurred_at_utc = delivery.Event.OccurredAtUtc.ToString("O"),
                details = JsonSerializer.Deserialize<object>(delivery.Event.PayloadJson)
            }
        };

        delivery.AttemptCount++;
        delivery.LastAttemptUtc = DateTime.UtcNow;

        var (success, statusCode, errorMessage) = await DeliverWebhookAsync(
            delivery.Endpoint.Url,
            delivery.Endpoint.Secret,
            payload,
            cancellationToken);

        delivery.ResponseStatusCode = statusCode;

        if (success)
        {
            delivery.Status = 1; // Sent
            delivery.NextRetryAtUtc = null;
            _logger.LogInformation("Webhook delivery {DeliveryId} succeeded", delivery.Id);
        }
        else
        {
            // Truncate response body if present
            if (errorMessage?.Length > MaxResponseBodyLength)
                errorMessage = errorMessage[..MaxResponseBodyLength] + "...";
            delivery.ResponseBody = errorMessage;

            if (delivery.AttemptCount >= MaxRetryAttempts)
            {
                delivery.Status = 3; // Failed
                delivery.NextRetryAtUtc = null;
                _logger.LogWarning("Webhook delivery {DeliveryId} failed after {Attempts} attempts",
                    delivery.Id, delivery.AttemptCount);
            }
            else
            {
                delivery.Status = 2; // Retry
                var retryIndex = Math.Min(delivery.AttemptCount - 1, RetryIntervalsMinutes.Length - 1);
                delivery.NextRetryAtUtc = DateTime.UtcNow.AddMinutes(RetryIntervalsMinutes[retryIndex]);
                _logger.LogWarning("Webhook delivery {DeliveryId} failed, will retry at {RetryAt}",
                    delivery.Id, delivery.NextRetryAtUtc);
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<(bool Success, int? StatusCode, string? ErrorMessage)> DeliverWebhookAsync(
        string url,
        byte[]? secret,
        object payload,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("WebhookClient");
            client.Timeout = TimeSpan.FromSeconds(WebhookTimeoutSeconds);

            var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            });

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var signature = ComputeSignature(secret, timestamp, payloadJson);

            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
            };

            request.Headers.Add("X-Webhook-Timestamp", timestamp);
            request.Headers.Add("X-Webhook-Signature", $"sha256={signature}");
            request.Headers.Add("User-Agent", "EmailAPI-Webhook/1.0");

            var response = await client.SendAsync(request, cancellationToken);
            var statusCode = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                return (true, statusCode, null);
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            return (false, statusCode, responseBody);
        }
        catch (TaskCanceledException)
        {
            return (false, null, "Request timed out");
        }
        catch (HttpRequestException ex)
        {
            return (false, null, $"HTTP error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error delivering webhook to {Url}", url);
            return (false, null, $"Unexpected error: {ex.Message}");
        }
    }

    private static string ComputeSignature(byte[]? secret, string timestamp, string payload)
    {
        if (secret == null || secret.Length == 0)
            return string.Empty;

        var signaturePayload = $"{timestamp}.{payload}";
        using var hmac = new HMACSHA256(secret);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signaturePayload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? MapSesEventTypeToWebhookType(string sesEventType)
    {
        return sesEventType.ToLowerInvariant() switch
        {
            "send" => "email.sent",
            "delivery" => "email.delivered",
            "bounce" => "email.bounced",
            "complaint" => "email.complained",
            "open" => "email.opened",
            "click" => "email.clicked",
            "reject" => "email.rejected",
            "renderingfailure" or "rendering failure" => "email.rendering_failed",
            _ => null
        };
    }

    private static WebhookEndpointResponse MapToResponse(WebhookEndpoints endpoint)
    {
        var secretPreview = endpoint.Secret != null && endpoint.Secret.Length > 0
            ? Convert.ToHexString(endpoint.Secret)[..8].ToLowerInvariant() + "..."
            : "";

        return new WebhookEndpointResponse
        {
            Id = endpoint.Id,
            Name = endpoint.Name ?? "",
            Url = endpoint.Url,
            EventTypes = string.IsNullOrEmpty(endpoint.EventTypes)
                ? []
                : [.. endpoint.EventTypes.Split(',', StringSplitOptions.RemoveEmptyEntries)],
            Enabled = endpoint.Enabled,
            SecretPreview = secretPreview,
            CreatedAtUtc = endpoint.CreatedAtUtc
        };
    }

    private static WebhookDeliveryResponse MapDeliveryToResponse(WebhookDeliveries delivery)
    {
        var statusText = delivery.Status switch
        {
            0 => "Pending",
            1 => "Sent",
            2 => "Retry",
            3 => "Failed",
            _ => "Unknown"
        };

        var eventType = delivery.Event != null
            ? MapSesEventTypeToWebhookType(delivery.Event.EventType) ?? delivery.Event.EventType
            : "";

        return new WebhookDeliveryResponse
        {
            Id = delivery.Id,
            EventType = eventType,
            Status = delivery.Status,
            StatusText = statusText,
            AttemptCount = delivery.AttemptCount,
            LastAttemptUtc = delivery.LastAttemptUtc,
            ResponseStatusCode = delivery.ResponseStatusCode,
            NextRetryAtUtc = delivery.NextRetryAtUtc
        };
    }

    public async Task QueueWebhookDeliveryAsync(
        Guid tenantId,
        string eventType,
        string payloadJson,
        CancellationToken cancellationToken = default)
    {
        // Find all enabled endpoints for this tenant that subscribe to this event type
        var endpoints = await _context.WebhookEndpoints
            .Where(e => e.TenantId == tenantId && e.Enabled)
            .ToListAsync(cancellationToken);

        var matchingEndpoints = endpoints
            .Where(e => !string.IsNullOrEmpty(e.EventTypes) &&
                        e.EventTypes.Split(',').Contains(eventType))
            .ToList();

        if (matchingEndpoints.Count == 0)
        {
            _logger.LogDebug("No webhook endpoints configured for event type {EventType} in tenant {TenantId}",
                eventType, tenantId);
            return;
        }

        _logger.LogInformation("Delivering {EventType} webhook to {Count} endpoints for tenant {TenantId}",
            eventType, matchingEndpoints.Count, tenantId);

        // Deliver to each endpoint immediately (fire and forget style, but log failures)
        var payload = JsonSerializer.Deserialize<object>(payloadJson);
        foreach (var endpoint in matchingEndpoints)
        {
            try
            {
                var (success, statusCode, errorMessage) = await DeliverWebhookAsync(
                    endpoint.Url,
                    endpoint.Secret,
                    payload!,
                    cancellationToken);

                if (success)
                {
                    _logger.LogInformation("Webhook delivered to {Url} for {EventType}", endpoint.Url, eventType);
                }
                else
                {
                    _logger.LogWarning("Webhook delivery failed to {Url}: {StatusCode} - {Error}",
                        endpoint.Url, statusCode, errorMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception delivering webhook to {Url}", endpoint.Url);
            }
        }
    }
}

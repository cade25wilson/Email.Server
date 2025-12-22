using System.Text.Json;
using Email.Server.Data;
using Email.Server.Services.Interfaces;
using Email.Shared.DTOs.Requests;
using Email.Shared.DTOs.Responses;
using Email.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using IPushClientService = Email.Shared.Services.Interfaces.IPushClientService;
using IPushCredentialService = Email.Shared.Services.Interfaces.IPushCredentialService;
using IPushDeviceService = Email.Shared.Services.Interfaces.IPushDeviceService;
using IPushService = Email.Shared.Services.Interfaces.IPushService;
using IPushTemplateService = Email.Shared.Services.Interfaces.IPushTemplateService;
using PushQueryParams = Email.Shared.Services.Interfaces.PushQueryParams;
using PlatformPushOptions = Email.Shared.Services.Interfaces.PlatformPushOptions;

namespace Email.Server.Services.Implementations;

public class PushService : IPushService
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContextService _tenantContext;
    private readonly IPushClientService _pushClient;
    private readonly IPushCredentialService _credentialService;
    private readonly IPushDeviceService _deviceService;
    private readonly IPushTemplateService _templateService;
    private readonly IUsageTrackingService _usageTracking;
    private readonly ILogger<PushService> _logger;

    public PushService(
        ApplicationDbContext context,
        ITenantContextService tenantContext,
        IPushClientService pushClient,
        IPushCredentialService credentialService,
        IPushDeviceService deviceService,
        IPushTemplateService templateService,
        IUsageTrackingService usageTracking,
        ILogger<PushService> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _pushClient = pushClient;
        _credentialService = credentialService;
        _deviceService = deviceService;
        _templateService = templateService;
        _usageTracking = usageTracking;
        _logger = logger;
    }

    public async Task<SendPushResponse> SendPushAsync(SendPushRequest request, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        // Get credential
        PushCredentials? credential;
        if (request.CredentialId.HasValue)
        {
            credential = await _credentialService.GetCredentialAsync(request.CredentialId.Value, cancellationToken);
        }
        else
        {
            credential = await _credentialService.GetDefaultCredentialAsync(null, cancellationToken);
        }

        if (credential == null || !credential.IsActive)
        {
            throw new InvalidOperationException("No active push credential found");
        }

        // Render template if specified
        var title = request.Title;
        var body = request.Body;

        if (request.TemplateId.HasValue)
        {
            var template = await _templateService.GetTemplateAsync(request.TemplateId.Value, cancellationToken);
            if (template != null)
            {
                (title, body) = _templateService.RenderTemplate(template.Title, template.Body, request.TemplateVariables);
            }
        }
        else if (request.TemplateVariables != null && request.TemplateVariables.Count > 0)
        {
            (title, body) = _templateService.RenderTemplate(title, body, request.TemplateVariables);
        }

        // Determine target devices
        var targetDevices = new List<PushDeviceTokens>();

        if (request.DeviceTokenId.HasValue)
        {
            // Single device
            var device = await _deviceService.GetDeviceAsync(request.DeviceTokenId.Value, cancellationToken);
            if (device != null && device.IsActive)
            {
                targetDevices.Add(device);
            }
        }
        else if (!string.IsNullOrEmpty(request.ExternalUserId))
        {
            // All devices for a user
            targetDevices = await _deviceService.GetDevicesByExternalUserAsync(request.ExternalUserId, cancellationToken);
        }
        else
        {
            throw new InvalidOperationException("Either device_token_id or external_user_id is required");
        }

        if (targetDevices.Count == 0)
        {
            throw new InvalidOperationException("No active devices found for the target");
        }

        // Check usage limits
        var usageCheck = await _usageTracking.CheckPushLimitAsync(tenantId, targetDevices.Count, cancellationToken);
        if (!usageCheck.Allowed)
        {
            throw new InvalidOperationException(usageCheck.DenialReason ?? "Push notification limit exceeded");
        }

        // Create message record
        var message = new PushMessages
        {
            TenantId = tenantId,
            CredentialId = credential.Id,
            DeviceTokenId = request.DeviceTokenId,
            TemplateId = request.TemplateId,
            ExternalUserId = request.ExternalUserId,
            Title = title,
            Body = body,
            DataJson = request.Data != null ? JsonSerializer.Serialize(request.Data) : null,
            PlatformOptionsJson = request.PlatformOptions != null ? JsonSerializer.Serialize(request.PlatformOptions) : null,
            Status = 0, // Queued
            TargetCount = targetDevices.Count,
            RequestedAtUtc = DateTime.UtcNow,
            ScheduledAtUtc = request.ScheduledAtUtc
        };

        _context.PushMessages.Add(message);
        await _context.SaveChangesAsync(cancellationToken);

        // If scheduled, save and return
        if (request.ScheduledAtUtc.HasValue && request.ScheduledAtUtc.Value > DateTime.UtcNow)
        {
            message.Status = 4; // Scheduled
            await _context.SaveChangesAsync(cancellationToken);

            await RecordEventAsync(message.Id, tenantId, "queued", null, cancellationToken);

            _logger.LogInformation(
                "Scheduled push notification. Id: {Id}, ScheduledAt: {ScheduledAt}",
                message.Id, request.ScheduledAtUtc);

            return new SendPushResponse
            {
                MessageId = message.Id,
                Status = message.Status,
                TargetCount = targetDevices.Count,
                RequestedAtUtc = message.RequestedAtUtc,
                ScheduledAtUtc = message.ScheduledAtUtc
            };
        }

        // Send immediately
        return await SendToDevicesAsync(message, credential, targetDevices, request.Data, request.PlatformOptions, cancellationToken);
    }

    public async Task<SendPushResponse> SendScheduledPushAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var message = await _context.PushMessages
            .FirstOrDefaultAsync(m => m.Id == messageId && m.TenantId == tenantId && m.Status == 4, cancellationToken);

        if (message == null)
        {
            throw new InvalidOperationException("Scheduled push message not found");
        }

        var credential = await _credentialService.GetCredentialAsync(message.CredentialId, cancellationToken);
        if (credential == null || !credential.IsActive)
        {
            message.Status = 2; // Failed
            message.Error = "Push credential is no longer active";
            await _context.SaveChangesAsync(cancellationToken);

            return new SendPushResponse
            {
                MessageId = message.Id,
                Status = message.Status,
                Error = message.Error
            };
        }

        // Get target devices
        var targetDevices = new List<PushDeviceTokens>();

        if (message.DeviceTokenId.HasValue)
        {
            var device = await _deviceService.GetDeviceAsync(message.DeviceTokenId.Value, cancellationToken);
            if (device != null && device.IsActive)
            {
                targetDevices.Add(device);
            }
        }
        else if (!string.IsNullOrEmpty(message.ExternalUserId))
        {
            targetDevices = await _deviceService.GetDevicesByExternalUserAsync(message.ExternalUserId, cancellationToken);
        }

        if (targetDevices.Count == 0)
        {
            message.Status = 2; // Failed
            message.Error = "No active devices found";
            await _context.SaveChangesAsync(cancellationToken);

            return new SendPushResponse
            {
                MessageId = message.Id,
                Status = message.Status,
                Error = message.Error
            };
        }

        // Re-check usage limits
        var usageCheck = await _usageTracking.CheckPushLimitAsync(tenantId, targetDevices.Count, cancellationToken);
        if (!usageCheck.Allowed)
        {
            message.Status = 2; // Failed
            message.Error = usageCheck.DenialReason ?? "Push notification limit exceeded";
            await _context.SaveChangesAsync(cancellationToken);

            return new SendPushResponse
            {
                MessageId = message.Id,
                Status = message.Status,
                Error = message.Error
            };
        }

        // Parse stored options
        Dictionary<string, string>? data = null;
        if (!string.IsNullOrEmpty(message.DataJson))
        {
            data = JsonSerializer.Deserialize<Dictionary<string, string>>(message.DataJson);
        }

        PlatformPushOptionsDto? platformOptions = null;
        if (!string.IsNullOrEmpty(message.PlatformOptionsJson))
        {
            platformOptions = JsonSerializer.Deserialize<PlatformPushOptionsDto>(message.PlatformOptionsJson);
        }

        return await SendToDevicesAsync(message, credential, targetDevices, data, platformOptions, cancellationToken);
    }

    private async Task<SendPushResponse> SendToDevicesAsync(
        PushMessages message,
        PushCredentials credential,
        List<PushDeviceTokens> devices,
        Dictionary<string, string>? data,
        PlatformPushOptionsDto? platformOptions,
        CancellationToken cancellationToken)
    {
        var successCount = 0;
        string? lastError = null;
        string? awsMessageId = null;

        // Convert platform options
        PlatformPushOptions? options = null;
        if (platformOptions != null)
        {
            options = new PlatformPushOptions
            {
                ApnsPriority = platformOptions.ApnsPriority,
                ApnsCollapseId = platformOptions.ApnsCollapseId,
                ApnsBadge = platformOptions.ApnsBadge,
                ApnsSound = platformOptions.ApnsSound,
                FcmPriority = platformOptions.FcmPriority,
                FcmCollapseKey = platformOptions.FcmCollapseKey,
                FcmTtlSeconds = platformOptions.FcmTtlSeconds
            };
        }

        foreach (var device in devices)
        {
            if (string.IsNullOrEmpty(device.AwsEndpointArn))
            {
                continue;
            }

            var result = await _pushClient.SendPushAsync(
                device.AwsEndpointArn,
                device.Platform,
                message.Title,
                message.Body,
                data,
                options,
                cancellationToken);

            if (result.Success)
            {
                successCount++;
                awsMessageId ??= result.MessageId;

                await RecordEventAsync(message.Id, message.TenantId, "sent", device.Token, cancellationToken);
            }
            else
            {
                lastError = result.Error;

                await RecordEventAsync(message.Id, message.TenantId, "failed", device.Token, cancellationToken,
                    new { error = result.Error });

                // Disable device if endpoint is invalid
                if (result.Error?.Contains("disabled", StringComparison.OrdinalIgnoreCase) == true)
                {
                    await _deviceService.UnregisterDeviceAsync(device.Id, cancellationToken);
                }
            }
        }

        // Update message status
        if (successCount > 0)
        {
            message.Status = 1; // Sent
            message.SentAtUtc = DateTime.UtcNow;
            message.AwsMessageId = awsMessageId;
            message.DeliveredCount = successCount;

            // Record usage
            await _usageTracking.RecordPushSendAsync(message.TenantId, successCount, "API", cancellationToken);
        }
        else
        {
            message.Status = 2; // Failed
            message.Error = lastError ?? "All devices failed";
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Push notification completed. Id: {Id}, Success: {Success}/{Total}",
            message.Id, successCount, devices.Count);

        return new SendPushResponse
        {
            MessageId = message.Id,
            AwsMessageId = message.AwsMessageId,
            Status = message.Status,
            TargetCount = devices.Count,
            RequestedAtUtc = message.RequestedAtUtc,
            ScheduledAtUtc = message.ScheduledAtUtc,
            Error = message.Error
        };
    }

    public async Task<PushMessages?> GetPushAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        return await _context.PushMessages
            .FirstOrDefaultAsync(m => m.Id == id && m.TenantId == tenantId, cancellationToken);
    }

    public async Task<PushMessageListResponse> ListPushAsync(PushQueryParams query, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var dbQuery = _context.PushMessages
            .Where(m => m.TenantId == tenantId);

        if (!string.IsNullOrEmpty(query.Status) && byte.TryParse(query.Status, out var status))
        {
            dbQuery = dbQuery.Where(m => m.Status == status);
        }

        if (query.CredentialId.HasValue)
        {
            dbQuery = dbQuery.Where(m => m.CredentialId == query.CredentialId.Value);
        }

        if (!string.IsNullOrEmpty(query.ExternalUserId))
        {
            dbQuery = dbQuery.Where(m => m.ExternalUserId == query.ExternalUserId);
        }

        if (query.FromDate.HasValue)
        {
            dbQuery = dbQuery.Where(m => m.RequestedAtUtc >= query.FromDate.Value);
        }

        if (query.ToDate.HasValue)
        {
            dbQuery = dbQuery.Where(m => m.RequestedAtUtc <= query.ToDate.Value);
        }

        var total = await dbQuery.CountAsync(cancellationToken);

        var messages = await dbQuery
            .OrderByDescending(m => m.RequestedAtUtc)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PushMessageListResponse
        {
            Messages = messages.Select(MapToResponse).ToList(),
            Total = total,
            Page = query.Page,
            PageSize = query.PageSize
        };
    }

    public async Task<bool> CancelScheduledPushAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var message = await _context.PushMessages
            .FirstOrDefaultAsync(m => m.Id == id && m.TenantId == tenantId && m.Status == 4, cancellationToken);

        if (message == null)
        {
            return false;
        }

        message.Status = 2; // Failed/Cancelled
        message.Error = "Cancelled by user";

        await _context.SaveChangesAsync(cancellationToken);

        await RecordEventAsync(message.Id, tenantId, "cancelled", null, cancellationToken);

        _logger.LogInformation("Cancelled scheduled push. Id: {Id}", id);

        return true;
    }

    private async Task RecordEventAsync(Guid messageId, Guid tenantId, string eventType, string? deviceToken, CancellationToken cancellationToken, object? payload = null)
    {
        var evt = new PushEvents
        {
            PushMessageId = messageId,
            TenantId = tenantId,
            EventType = eventType,
            OccurredAtUtc = DateTime.UtcNow,
            DeviceToken = deviceToken,
            PayloadJson = payload != null ? JsonSerializer.Serialize(payload) : null
        };

        _context.PushEvents.Add(evt);
        await _context.SaveChangesAsync(cancellationToken);
    }

    private static PushMessageResponse MapToResponse(PushMessages message)
    {
        Dictionary<string, string>? data = null;
        if (!string.IsNullOrEmpty(message.DataJson))
        {
            data = JsonSerializer.Deserialize<Dictionary<string, string>>(message.DataJson);
        }

        return new PushMessageResponse
        {
            Id = message.Id,
            CredentialId = message.CredentialId,
            DeviceTokenId = message.DeviceTokenId,
            ExternalUserId = message.ExternalUserId,
            Title = message.Title,
            Body = message.Body,
            Data = data,
            Status = message.Status,
            TargetCount = message.TargetCount,
            DeliveredCount = message.DeliveredCount,
            RequestedAtUtc = message.RequestedAtUtc,
            ScheduledAtUtc = message.ScheduledAtUtc,
            SentAtUtc = message.SentAtUtc,
            Error = message.Error
        };
    }
}

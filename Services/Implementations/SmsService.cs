using Email.Server.Data;
using Email.Server.DTOs.Requests;
using Email.Server.DTOs.Responses;
using Email.Server.Models;
using Email.Server.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Email.Server.Services.Implementations;

/// <summary>
/// SMS service supporting both AWS SNS (shared routes) and Pinpoint pools (tenant-isolated).
/// Automatically uses tenant's pool if provisioned, otherwise falls back to SNS.
/// </summary>
public class SmsService : ISmsService
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContextService _tenantContext;
    private readonly ISmsClientService _smsClient;
    private readonly ISmsPoolService _poolService;
    private readonly IUsageTrackingService _usageTracking;
    private readonly ISmsTemplateService _templateService;
    private readonly ILogger<SmsService> _logger;

    public SmsService(
        ApplicationDbContext context,
        ITenantContextService tenantContext,
        ISmsClientService smsClient,
        ISmsPoolService poolService,
        IUsageTrackingService usageTracking,
        ISmsTemplateService templateService,
        ILogger<SmsService> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _smsClient = smsClient;
        _poolService = poolService;
        _usageTracking = usageTracking;
        _templateService = templateService;
        _logger = logger;
    }

    public async Task<SendSmsResponse> SendSmsAsync(SendSmsRequest request, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        // Check suppression list for the recipient
        var isSuppressed = await _context.Suppressions
            .AnyAsync(s => s.TenantId == tenantId &&
                          s.Type == SuppressionType.Phone &&
                          s.PhoneNumber == request.To, cancellationToken);

        if (isSuppressed)
        {
            throw new InvalidOperationException($"Recipient {request.To} is on the suppression list");
        }

        // Check usage limits before sending
        var usageCheck = await _usageTracking.CheckSmsLimitAsync(tenantId, 1, cancellationToken);
        if (!usageCheck.Allowed)
        {
            throw new InvalidOperationException(usageCheck.DenialReason ?? "SMS sending limit exceeded. Please upgrade your plan or wait for the next billing period.");
        }

        // Render template if specified
        string body = request.Body;
        if (request.TemplateId.HasValue)
        {
            var template = await _templateService.GetTemplateAsync(request.TemplateId.Value, cancellationToken);
            if (template == null)
            {
                throw new InvalidOperationException($"Template {request.TemplateId.Value} not found");
            }
            body = _templateService.RenderTemplate(template.Body, request.TemplateVariables);
        }

        // Calculate segment count
        var segmentCount = _smsClient.CalculateSegmentCount(body);

        // Determine if this is a scheduled send
        var isScheduled = request.ScheduledAtUtc.HasValue && request.ScheduledAtUtc.Value > DateTime.UtcNow;

        // Get tenant's pool if available (for tenant-isolated sending)
        var pool = await _poolService.GetPoolAsync(cancellationToken);

        // Ensure pool is set up in AWS (lazy initialization)
        var hasPool = false;
        if (pool != null)
        {
            hasPool = await _poolService.EnsurePoolSetupAsync(pool, cancellationToken);
        }

        var fromNumber = hasPool ? pool!.PhoneNumbers.FirstOrDefault()?.PhoneNumber ?? "Pool" : "SNS";

        // Create message entity
        var message = new SmsMessages
        {
            TenantId = tenantId,
            PhoneNumberId = hasPool ? pool!.PhoneNumbers.FirstOrDefault()?.Id : null,
            FromNumber = fromNumber,
            ToNumber = request.To,
            Body = body,
            TemplateId = request.TemplateId,
            Status = isScheduled ? (byte)4 : (byte)0, // 4=Scheduled, 0=Queued
            SegmentCount = segmentCount,
            RequestedAtUtc = DateTime.UtcNow,
            ScheduledAtUtc = request.ScheduledAtUtc
        };

        _context.SmsMessages.Add(message);

        // If scheduled, save and return - don't send yet
        if (isScheduled)
        {
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("SMS scheduled for {ScheduledAtUtc}. MessageId: {MessageId}",
                request.ScheduledAtUtc, message.Id);

            return new SendSmsResponse
            {
                MessageId = message.Id,
                AwsMessageId = null,
                Status = message.Status,
                SegmentCount = segmentCount,
                RequestedAtUtc = message.RequestedAtUtc,
                ScheduledAtUtc = message.ScheduledAtUtc,
                Error = null
            };
        }

        // Send via AWS (pool if available, otherwise SNS shared routes)
        try
        {
            SmsSendResult result;
            if (hasPool)
            {
                // Use tenant's pool - AWS will pick a number from the pool
                result = await _smsClient.SendSmsViaPoolAsync(pool!.AwsPoolArn!, request.To, body, true, cancellationToken);
                _logger.LogDebug("Sending SMS via pool {PoolArn}", pool.AwsPoolArn);
            }
            else
            {
                // Fallback to SNS shared routes
                result = await _smsClient.SendSmsAsync(request.To, body, true, cancellationToken);
                _logger.LogDebug("Sending SMS via SNS shared routes (no pool configured)");
            }

            if (result.Success)
            {
                message.AwsMessageId = result.MessageId;
                message.Status = 1; // Sent
                message.SentAtUtc = DateTime.UtcNow;

                _logger.LogInformation("SMS sent successfully. MessageId: {MessageId}, AwsMessageId: {AwsMessageId}, ViaPool: {ViaPool}",
                    message.Id, result.MessageId, hasPool);

                // Record usage for billing
                await _usageTracking.RecordSmsSendAsync(tenantId, 1, segmentCount, "API", cancellationToken);

                // Create sent event
                _context.SmsEvents.Add(new SmsEvents
                {
                    SmsMessageId = message.Id,
                    TenantId = tenantId,
                    EventType = "sent",
                    OccurredAtUtc = DateTime.UtcNow,
                    Recipient = request.To
                });
            }
            else
            {
                message.Status = 2; // Failed
                message.Error = result.Error;
                _logger.LogError("Failed to send SMS. MessageId: {MessageId}, Error: {Error}", message.Id, result.Error);

                // Create failed event
                _context.SmsEvents.Add(new SmsEvents
                {
                    SmsMessageId = message.Id,
                    TenantId = tenantId,
                    EventType = "failed",
                    OccurredAtUtc = DateTime.UtcNow,
                    Recipient = request.To,
                    PayloadJson = System.Text.Json.JsonSerializer.Serialize(new { error = result.Error })
                });
            }
        }
        catch (Exception ex)
        {
            message.Status = 2; // Failed
            message.Error = ex.Message;
            _logger.LogError(ex, "Failed to send SMS. MessageId: {MessageId}", message.Id);
        }

        await _context.SaveChangesAsync(cancellationToken);

        return new SendSmsResponse
        {
            MessageId = message.Id,
            AwsMessageId = message.AwsMessageId,
            Status = message.Status,
            SegmentCount = segmentCount,
            RequestedAtUtc = message.RequestedAtUtc,
            ScheduledAtUtc = message.ScheduledAtUtc,
            Error = message.Error
        };
    }

    public async Task<SendSmsResponse> SendScheduledSmsAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var message = await _context.SmsMessages
            .FirstOrDefaultAsync(m => m.Id == messageId && m.Status == 4, cancellationToken);

        if (message == null)
        {
            throw new InvalidOperationException($"Scheduled SMS {messageId} not found or already processed");
        }

        // Check usage limits before sending
        var usageCheck = await _usageTracking.CheckSmsLimitAsync(message.TenantId, 1, cancellationToken);
        if (!usageCheck.Allowed)
        {
            message.Status = 2; // Failed
            message.Error = usageCheck.DenialReason ?? "SMS sending limit exceeded.";
            await _context.SaveChangesAsync(cancellationToken);

            return new SendSmsResponse
            {
                MessageId = message.Id,
                Status = message.Status,
                SegmentCount = message.SegmentCount,
                RequestedAtUtc = message.RequestedAtUtc,
                ScheduledAtUtc = message.ScheduledAtUtc,
                Error = message.Error
            };
        }

        // Get tenant's pool if available
        var pool = await _context.SmsPools
            .Include(p => p.PhoneNumbers)
            .FirstOrDefaultAsync(p => p.TenantId == message.TenantId && p.IsActive, cancellationToken);

        // Ensure pool is set up in AWS (lazy initialization)
        var hasPool = false;
        if (pool != null)
        {
            hasPool = await _poolService.EnsurePoolSetupAsync(pool, cancellationToken);
        }

        // Send via AWS (pool if available, otherwise SNS shared routes)
        try
        {
            SmsSendResult result;
            if (hasPool)
            {
                result = await _smsClient.SendSmsViaPoolAsync(pool!.AwsPoolArn!, message.ToNumber, message.Body, true, cancellationToken);
            }
            else
            {
                result = await _smsClient.SendSmsAsync(message.ToNumber, message.Body, true, cancellationToken);
            }

            if (result.Success)
            {
                message.AwsMessageId = result.MessageId;
                message.Status = 1; // Sent
                message.SentAtUtc = DateTime.UtcNow;

                _logger.LogInformation("Scheduled SMS sent successfully. MessageId: {MessageId}, AwsMessageId: {AwsMessageId}, ViaPool: {ViaPool}",
                    message.Id, result.MessageId, hasPool);

                // Record usage for billing
                await _usageTracking.RecordSmsSendAsync(message.TenantId, 1, message.SegmentCount, "Scheduled", cancellationToken);

                // Create sent event
                _context.SmsEvents.Add(new SmsEvents
                {
                    SmsMessageId = message.Id,
                    TenantId = message.TenantId,
                    EventType = "sent",
                    OccurredAtUtc = DateTime.UtcNow,
                    Recipient = message.ToNumber
                });
            }
            else
            {
                message.Status = 2; // Failed
                message.Error = result.Error;
                _logger.LogError("Failed to send scheduled SMS. MessageId: {MessageId}, Error: {Error}", message.Id, result.Error);
            }
        }
        catch (Exception ex)
        {
            message.Status = 2; // Failed
            message.Error = ex.Message;
            _logger.LogError(ex, "Failed to send scheduled SMS. MessageId: {MessageId}", message.Id);
        }

        await _context.SaveChangesAsync(cancellationToken);

        return new SendSmsResponse
        {
            MessageId = message.Id,
            AwsMessageId = message.AwsMessageId,
            Status = message.Status,
            SegmentCount = message.SegmentCount,
            RequestedAtUtc = message.RequestedAtUtc,
            ScheduledAtUtc = message.ScheduledAtUtc,
            Error = message.Error
        };
    }

    public async Task<SmsMessages?> GetSmsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();
        return await _context.SmsMessages
            .FirstOrDefaultAsync(m => m.Id == id && m.TenantId == tenantId, cancellationToken);
    }

    public async Task<SmsMessageListResponse> ListSmsAsync(SmsQueryParams query, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var queryable = _context.SmsMessages
            .Where(m => m.TenantId == tenantId);

        // Apply filters
        if (query.Status.HasValue)
        {
            queryable = queryable.Where(m => m.Status == query.Status.Value);
        }

        if (query.From.HasValue)
        {
            queryable = queryable.Where(m => m.RequestedAtUtc >= query.From.Value);
        }

        if (query.To.HasValue)
        {
            queryable = queryable.Where(m => m.RequestedAtUtc <= query.To.Value);
        }

        var total = await queryable.CountAsync(cancellationToken);

        var messages = await queryable
            .OrderByDescending(m => m.RequestedAtUtc)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(m => new SmsMessageResponse
            {
                Id = m.Id,
                FromNumber = m.FromNumber,
                ToNumber = m.ToNumber,
                Body = m.Body,
                TemplateId = m.TemplateId,
                AwsMessageId = m.AwsMessageId,
                Status = m.Status,
                SegmentCount = m.SegmentCount,
                RequestedAtUtc = m.RequestedAtUtc,
                ScheduledAtUtc = m.ScheduledAtUtc,
                SentAtUtc = m.SentAtUtc,
                Error = m.Error
            })
            .ToListAsync(cancellationToken);

        return new SmsMessageListResponse
        {
            Messages = messages,
            Total = total,
            Page = query.Page,
            PageSize = query.PageSize
        };
    }

    public async Task<bool> CancelScheduledSmsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var message = await _context.SmsMessages
            .FirstOrDefaultAsync(m => m.Id == id && m.TenantId == tenantId && m.Status == 4, cancellationToken);

        if (message == null)
        {
            return false;
        }

        _context.SmsMessages.Remove(message);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Cancelled scheduled SMS. MessageId: {MessageId}", id);

        return true;
    }
}

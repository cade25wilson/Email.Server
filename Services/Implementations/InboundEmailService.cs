using System.Text.Json;
using Email.Server.Data;
using Email.Server.DTOs.Inbound;
using Email.Server.DTOs.Responses;
using Email.Server.Models;
using Email.Server.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Email.Server.Services.Implementations;

public class InboundEmailService : IInboundEmailService
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContextService _tenantContext;
    private readonly IInboundEmailStorageService _storageService;
    private readonly IWebhookDeliveryService _webhookService;
    private readonly IUsageTrackingService _usageTracking;
    private readonly ILogger<InboundEmailService> _logger;

    public InboundEmailService(
        ApplicationDbContext context,
        ITenantContextService tenantContext,
        IInboundEmailStorageService storageService,
        IWebhookDeliveryService webhookService,
        IUsageTrackingService usageTracking,
        ILogger<InboundEmailService> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _storageService = storageService;
        _webhookService = webhookService;
        _usageTracking = usageTracking;
        _logger = logger;
    }

    public async Task<InboundMessageResponse> ProcessInboundEmailAsync(
        InboundEmailNotification notification,
        CancellationToken cancellationToken = default)
    {
        // Extract domain from recipient email
        var recipientDomain = notification.Recipient.Split('@').LastOrDefault()?.ToLowerInvariant();
        if (string.IsNullOrEmpty(recipientDomain))
        {
            throw new InvalidOperationException($"Invalid recipient email: {notification.Recipient}");
        }

        // Find the domain and tenant
        var domain = await _context.Domains
            .Include(d => d.Tenant)
            .FirstOrDefaultAsync(d =>
                d.Domain.ToLower() == recipientDomain &&
                d.InboundEnabled &&
                d.Region == notification.Region,
                cancellationToken) ?? throw new InvalidOperationException($"No inbound-enabled domain found for {recipientDomain} in region {notification.Region}");

        // Create the inbound message record
        var inboundMessage = new InboundMessages
        {
            TenantId = domain.TenantId,
            DomainId = domain.Id,
            Region = notification.Region,
            Recipient = notification.Recipient,
            FromAddress = notification.FromAddress,
            Subject = notification.Subject,
            ReceivedAtUtc = notification.ReceivedAtUtc,
            BlobKey = notification.BlobKey,
            SesMessageId = notification.SesMessageId,
            SizeBytes = notification.SizeBytes,
            ParsedJson = notification.Headers != null ? JsonSerializer.Serialize(notification.Headers) : null,
            ProcessedAtUtc = DateTime.UtcNow
        };

        _context.InboundMessages.Add(inboundMessage);
        await _context.SaveChangesAsync(cancellationToken);

        // Track inbound email as usage (counts same as sent emails)
        await _usageTracking.RecordEmailSendAsync(domain.TenantId, 1, "Inbound", cancellationToken);

        _logger.LogInformation(
            "Processed inbound email {MessageId} from {From} to {To} for tenant {TenantId}",
            inboundMessage.Id, notification.FromAddress, notification.Recipient, domain.TenantId);

        // Trigger webhook for inbound email
        await TriggerInboundWebhookAsync(inboundMessage, domain.Domain, cancellationToken);

        return MapToResponse(inboundMessage);
    }

    public async Task<InboundMessageListResponse> GetInboundMessagesAsync(
        int page = 1,
        int pageSize = 50,
        Guid? domainId = null,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var query = _context.InboundMessages
            .AsNoTracking()
            .Include(m => m.Domain)
            .Where(m => m.TenantId == tenantId);

        if (domainId.HasValue)
        {
            query = query.Where(m => m.DomainId == domainId.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var messages = await query
            .OrderByDescending(m => m.ReceivedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize + 1) // Fetch one extra to determine has_more
            .ToListAsync(cancellationToken);

        var hasMore = messages.Count > pageSize;
        if (hasMore)
        {
            messages = messages.Take(pageSize).ToList();
        }

        return new InboundMessageListResponse
        {
            Data = messages.Select(m => MapToResponse(m)).ToList(),
            HasMore = hasMore
        };
    }

    public async Task<InboundMessageResponse?> GetInboundMessageAsync(
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var message = await _context.InboundMessages
            .AsNoTracking()
            .Include(m => m.Domain)
            .FirstOrDefaultAsync(m => m.Id == messageId && m.TenantId == tenantId, cancellationToken);

        return message == null ? null : MapToResponse(message);
    }

    public async Task<InboundEmailDownloadResponse> GetRawEmailUrlAsync(
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var message = await _context.InboundMessages
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == messageId && m.TenantId == tenantId, cancellationToken)
            ?? throw new KeyNotFoundException($"Inbound message {messageId} not found");

        var (url, expiresAt) = await _storageService.GetSignedDownloadUrlAsync(message.BlobKey, null, cancellationToken);

        return new InboundEmailDownloadResponse
        {
            DownloadUrl = url,
            ExpiresAtUtc = expiresAt
        };
    }

    public async Task DeleteInboundMessageAsync(
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var message = await _context.InboundMessages
            .FirstOrDefaultAsync(m => m.Id == messageId && m.TenantId == tenantId, cancellationToken)
            ?? throw new KeyNotFoundException($"Inbound message {messageId} not found");

        // Delete from blob storage
        await _storageService.DeleteEmailAsync(message.BlobKey, cancellationToken);

        // Delete from database
        _context.InboundMessages.Remove(message);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted inbound message {MessageId}", messageId);
    }

    private async Task TriggerInboundWebhookAsync(
        InboundMessages message,
        string domainName,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = new
            {
                event_type = "email.inbound",
                timestamp = DateTime.UtcNow,
                data = new
                {
                    id = message.Id,
                    domain = domainName,
                    recipient = message.Recipient,
                    from_address = message.FromAddress,
                    subject = message.Subject,
                    received_at_utc = message.ReceivedAtUtc,
                    size_bytes = message.SizeBytes,
                    ses_message_id = message.SesMessageId
                }
            };

            await _webhookService.QueueWebhookDeliveryAsync(
                message.TenantId,
                "email.inbound",
                JsonSerializer.Serialize(payload),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to trigger webhook for inbound message {MessageId}", message.Id);
            // Don't throw - webhook failure shouldn't fail the inbound email processing
        }
    }

    private static InboundMessageResponse MapToResponse(InboundMessages message)
    {
        // Parse additional headers from ParsedJson if available
        List<string> cc = [];
        List<string> bcc = [];
        List<string> replyTo = [];

        if (!string.IsNullOrEmpty(message.ParsedJson))
        {
            try
            {
                var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(message.ParsedJson);
                if (headers != null)
                {
                    if (headers.TryGetValue("cc", out var ccValue) && !string.IsNullOrEmpty(ccValue))
                        cc = ccValue.Split(',').Select(e => e.Trim()).ToList();
                    if (headers.TryGetValue("bcc", out var bccValue) && !string.IsNullOrEmpty(bccValue))
                        bcc = bccValue.Split(',').Select(e => e.Trim()).ToList();
                    if (headers.TryGetValue("reply-to", out var replyToValue) && !string.IsNullOrEmpty(replyToValue))
                        replyTo = replyToValue.Split(',').Select(e => e.Trim()).ToList();
                }
            }
            catch
            {
                // Ignore parsing errors
            }
        }

        return new InboundMessageResponse
        {
            Id = message.Id,
            To = [message.Recipient],
            From = message.FromAddress,
            CreatedAt = message.ReceivedAtUtc,
            Subject = message.Subject,
            Cc = cc,
            Bcc = bcc,
            ReplyTo = replyTo,
            MessageId = message.SesMessageId,
            Attachments = [] // TODO: Parse attachments from email if needed
        };
    }
}

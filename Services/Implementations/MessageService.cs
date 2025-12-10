using AutoMapper;
using Email.Server.Data;
using Email.Server.DTOs.Requests;
using Email.Server.DTOs.Responses;
using Email.Server.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Email.Server.Services.Implementations;

public class MessageService : IMessageService
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContextService _tenantContext;
    private readonly IAttachmentStorageService _attachmentStorage;
    private readonly IMapper _mapper;
    private readonly ILogger<MessageService> _logger;

    public MessageService(
        ApplicationDbContext context,
        ITenantContextService tenantContext,
        IAttachmentStorageService attachmentStorage,
        IMapper mapper,
        ILogger<MessageService> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _attachmentStorage = attachmentStorage;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<MessageResponse> GetMessageAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var message = await _context.Messages
            .FirstOrDefaultAsync(m => m.Id == messageId && m.TenantId == tenantId, cancellationToken);

        if (message == null)
        {
            throw new KeyNotFoundException($"Message {messageId} not found");
        }

        var recipients = await _context.MessageRecipients
            .Where(r => r.MessageId == messageId)
            .ToListAsync(cancellationToken);

        var events = await _context.MessageEvents
            .Where(e => e.MessageId == messageId)
            .OrderBy(e => e.OccurredAtUtc)
            .ToListAsync(cancellationToken);

        var tags = await _context.MessageTags
            .Where(t => t.MessageId == messageId)
            .ToListAsync(cancellationToken);

        var response = _mapper.Map<MessageResponse>(message);
        response.Recipients = _mapper.Map<List<MessageRecipientResponse>>(recipients);
        response.Events = _mapper.Map<List<MessageEventResponse>>(events);
        response.Tags = tags.ToDictionary(t => t.Name, t => t.Value);

        return response;
    }

    public async Task<IEnumerable<MessageResponse>> GetMessagesAsync(int page = 1, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var messages = await _context.Messages
            .Where(m => m.TenantId == tenantId)
            .OrderByDescending(m => m.RequestedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return _mapper.Map<List<MessageResponse>>(messages);
    }

    public async Task<EmailListResponse> ListEmailsAsync(ListEmailsRequest request, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var query = _context.Messages.Where(m => m.TenantId == tenantId);

        // Apply filters
        if (request.Status.HasValue)
        {
            query = query.Where(m => m.Status == request.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.FromEmail))
        {
            query = query.Where(m => m.FromEmail.Contains(request.FromEmail));
        }

        if (!string.IsNullOrWhiteSpace(request.ToEmail))
        {
            var toEmail = request.ToEmail;
            query = query.Where(m => _context.MessageRecipients
                .Any(r => r.MessageId == m.Id && r.Email.Contains(toEmail)));
        }

        if (request.Since.HasValue)
        {
            query = query.Where(m => m.RequestedAtUtc >= request.Since.Value);
        }

        if (request.Until.HasValue)
        {
            query = query.Where(m => m.RequestedAtUtc <= request.Until.Value);
        }

        // Get total count
        var totalCount = await query.CountAsync(cancellationToken);

        // Apply sorting
        query = request.SortBy?.ToLowerInvariant() switch
        {
            "sent_at" => request.SortOrder?.ToLowerInvariant() == "asc"
                ? query.OrderBy(m => m.SentAtUtc)
                : query.OrderByDescending(m => m.SentAtUtc),
            "subject" => request.SortOrder?.ToLowerInvariant() == "asc"
                ? query.OrderBy(m => m.Subject)
                : query.OrderByDescending(m => m.Subject),
            _ => request.SortOrder?.ToLowerInvariant() == "asc"
                ? query.OrderBy(m => m.RequestedAtUtc)
                : query.OrderByDescending(m => m.RequestedAtUtc)
        };

        // Apply pagination
        var messages = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var items = _mapper.Map<List<MessageResponse>>(messages);

        // Load recipients for each message
        var messageIds = messages.Select(m => m.Id).ToList();
        var recipients = await _context.MessageRecipients
            .Where(r => messageIds.Contains(r.MessageId))
            .ToListAsync(cancellationToken);

        var tags = await _context.MessageTags
            .Where(t => messageIds.Contains(t.MessageId))
            .ToListAsync(cancellationToken);

        foreach (var item in items)
        {
            item.Recipients = _mapper.Map<List<MessageRecipientResponse>>(
                recipients.Where(r => r.MessageId == item.Id).ToList());
            item.Tags = tags
                .Where(t => t.MessageId == item.Id)
                .ToDictionary(t => t.Name, t => t.Value);
        }

        return new EmailListResponse
        {
            Items = items,
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize,
            HasMore = (request.Page * request.PageSize) < totalCount
        };
    }

    public async Task<MessageResponse> UpdateScheduledEmailAsync(Guid messageId, UpdateScheduledEmailRequest request, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var message = await _context.Messages
            .FirstOrDefaultAsync(m => m.Id == messageId && m.TenantId == tenantId, cancellationToken);

        if (message == null)
        {
            throw new KeyNotFoundException($"Message {messageId} not found");
        }

        if (message.Status != 4) // 4 = Scheduled
        {
            throw new InvalidOperationException("Only scheduled emails can be updated");
        }

        // Update allowed fields
        if (request.Subject != null)
        {
            message.Subject = request.Subject;
        }

        if (request.HtmlBody != null)
        {
            message.HtmlBody = request.HtmlBody;
        }

        if (request.TextBody != null)
        {
            message.TextBody = request.TextBody;
        }

        if (request.ScheduledAtUtc.HasValue)
        {
            if (request.ScheduledAtUtc.Value <= DateTime.UtcNow)
            {
                throw new InvalidOperationException("Scheduled time must be in the future");
            }
            message.ScheduledAtUtc = request.ScheduledAtUtc.Value;
        }

        // Update tags if provided
        if (request.Tags != null)
        {
            // Remove existing tags
            var existingTags = await _context.MessageTags
                .Where(t => t.MessageId == messageId)
                .ToListAsync(cancellationToken);
            _context.MessageTags.RemoveRange(existingTags);

            // Add new tags
            foreach (var tag in request.Tags)
            {
                _context.MessageTags.Add(new Models.MessageTags
                {
                    MessageId = messageId,
                    Name = tag.Key,
                    Value = tag.Value
                });
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated scheduled email {MessageId}", messageId);

        return await GetMessageAsync(messageId, cancellationToken);
    }

    public async Task CancelScheduledEmailAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var message = await _context.Messages
            .FirstOrDefaultAsync(m => m.Id == messageId && m.TenantId == tenantId, cancellationToken);

        if (message == null)
        {
            throw new KeyNotFoundException($"Message {messageId} not found");
        }

        if (message.Status != 4) // 4 = Scheduled
        {
            throw new InvalidOperationException("Only scheduled emails can be cancelled");
        }

        message.Status = 5; // 5 = Cancelled
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Cancelled scheduled email {MessageId}", messageId);
    }

    public async Task<List<AttachmentResponse>> GetAttachmentsAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        // Verify message belongs to tenant
        var messageExists = await _context.Messages
            .AnyAsync(m => m.Id == messageId && m.TenantId == tenantId, cancellationToken);

        if (!messageExists)
        {
            throw new KeyNotFoundException($"Message {messageId} not found");
        }

        var attachments = await _context.MessageAttachments
            .Where(a => a.MessageId == messageId)
            .ToListAsync(cancellationToken);

        return attachments.Select(a => new AttachmentResponse
        {
            Id = a.Id,
            FileName = a.FileName,
            ContentType = a.ContentType,
            SizeBytes = a.SizeBytes,
            IsInline = a.IsInline,
            ContentId = a.ContentId,
            UploadedAtUtc = a.UploadedAtUtc
        }).ToList();
    }

    public async Task<AttachmentDownloadResponse> GetAttachmentDownloadUrlAsync(Guid messageId, long attachmentId, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        // Verify message belongs to tenant
        var messageExists = await _context.Messages
            .AnyAsync(m => m.Id == messageId && m.TenantId == tenantId, cancellationToken);

        if (!messageExists)
        {
            throw new KeyNotFoundException($"Message {messageId} not found");
        }

        var attachment = await _context.MessageAttachments
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.MessageId == messageId, cancellationToken);

        if (attachment == null)
        {
            throw new KeyNotFoundException($"Attachment {attachmentId} not found");
        }

        var (url, expiresAt) = await _attachmentStorage.GetSignedDownloadUrlAsync(attachment.BlobUrl, cancellationToken);

        return new AttachmentDownloadResponse
        {
            DownloadUrl = url,
            ExpiresAtUtc = expiresAt
        };
    }
}

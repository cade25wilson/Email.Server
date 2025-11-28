using Amazon.SimpleEmailV2.Model;
using Email.Server.Data;
using Email.Server.DTOs.Requests;
using Email.Server.DTOs.Responses;
using Email.Server.Models;
using Email.Server.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Email.Server.Services.Implementations;

public class EmailSendingService : IEmailSendingService
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContextService _tenantContext;
    private readonly ISesClientService _sesClient;
    private readonly ILogger<EmailSendingService> _logger;

    public EmailSendingService(
        ApplicationDbContext context,
        ITenantContextService tenantContext,
        ISesClientService sesClient,
        ILogger<EmailSendingService> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _sesClient = sesClient;
        _logger = logger;
    }

    public async Task<DTOs.Responses.SendEmailResponse> SendEmailAsync(DTOs.Requests.SendEmailRequest request, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        // Extract domain from FromEmail
        var fromDomain = request.FromEmail.Split('@')[1];

        // Verify domain exists and is verified
        var domain = await _context.Domains
            .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.Domain == fromDomain && d.VerificationStatus == 1, cancellationToken);

        if (domain == null)
        {
            throw new InvalidOperationException($"Domain {fromDomain} is not verified for sending");
        }

        // Get AWS SES tenant name from SesRegions
        var sesRegion = await _context.SesRegions
            .FirstOrDefaultAsync(sr => sr.TenantId == tenantId && sr.Region == domain.Region, cancellationToken);

        if (sesRegion == null)
        {
            throw new InvalidOperationException($"Region {domain.Region} is not configured for tenant");
        }

        // Check suppression list for all recipients
        var allRecipients = new List<string>();
        allRecipients.AddRange(request.To.Select(r => r.Email));
        if (request.Cc != null) allRecipients.AddRange(request.Cc.Select(r => r.Email));
        if (request.Bcc != null) allRecipients.AddRange(request.Bcc.Select(r => r.Email));

        var suppressedEmails = await _context.Suppressions
            .Where(s => s.TenantId == tenantId && allRecipients.Contains(s.Email))
            .Select(s => s.Email)
            .ToListAsync(cancellationToken);

        if (suppressedEmails.Any())
        {
            throw new InvalidOperationException($"The following recipients are suppressed: {string.Join(", ", suppressedEmails)}");
        }

        // Determine if this is a scheduled send
        var isScheduled = request.ScheduledAtUtc.HasValue && request.ScheduledAtUtc.Value > DateTime.UtcNow;

        // Create message entity
        var message = new Messages
        {
            TenantId = tenantId,
            Region = domain.Region,
            ConfigSetId = request.ConfigSetId,
            FromEmail = request.FromEmail,
            FromName = request.FromName,
            Subject = request.Subject,
            HtmlBody = request.HtmlBody,
            TextBody = request.TextBody,
            TemplateId = request.TemplateId,
            Status = isScheduled ? (byte)4 : (byte)0, // 4=Scheduled, 0=Queued
            RequestedAtUtc = DateTime.UtcNow,
            ScheduledAtUtc = request.ScheduledAtUtc
        };

        _context.Messages.Add(message);

        // Add recipients
        foreach (var recipient in request.To)
        {
            _context.MessageRecipients.Add(new MessageRecipients
            {
                MessageId = message.Id,
                Kind = 0, // To
                Email = recipient.Email,
                Name = recipient.Name,
                DeliveryStatus = 0 // Pending
            });
        }

        if (request.Cc != null)
        {
            foreach (var recipient in request.Cc)
            {
                _context.MessageRecipients.Add(new MessageRecipients
                {
                    MessageId = message.Id,
                    Kind = 1, // CC
                    Email = recipient.Email,
                    Name = recipient.Name,
                    DeliveryStatus = 0
                });
            }
        }

        if (request.Bcc != null)
        {
            foreach (var recipient in request.Bcc)
            {
                _context.MessageRecipients.Add(new MessageRecipients
                {
                    MessageId = message.Id,
                    Kind = 2, // BCC
                    Email = recipient.Email,
                    Name = recipient.Name,
                    DeliveryStatus = 0
                });
            }
        }

        // Add tags
        if (request.Tags != null)
        {
            foreach (var tag in request.Tags)
            {
                _context.MessageTags.Add(new MessageTags
                {
                    MessageId = message.Id,
                    Name = tag.Key,
                    Value = tag.Value
                });
            }
        }

        // If scheduled, save and return - don't send yet
        if (isScheduled)
        {
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Email scheduled for {ScheduledAtUtc}. MessageId: {MessageId}",
                request.ScheduledAtUtc, message.Id);

            return new DTOs.Responses.SendEmailResponse
            {
                MessageId = message.Id,
                SesMessageId = null,
                Status = message.Status,
                RequestedAtUtc = message.RequestedAtUtc,
                ScheduledAtUtc = message.ScheduledAtUtc,
                Error = null
            };
        }

        // Send via SES
        try
        {
            var sesRequest = new Amazon.SimpleEmailV2.Model.SendEmailRequest
            {
                FromEmailAddress = request.FromName != null
                    ? $"{request.FromName} <{request.FromEmail}>"
                    : request.FromEmail,
                Destination = new Destination
                {
                    ToAddresses = request.To.Select(r => r.Email).ToList(),
                    CcAddresses = request.Cc?.Select(r => r.Email).ToList(),
                    BccAddresses = request.Bcc?.Select(r => r.Email).ToList()
                },
                Content = new EmailContent
                {
                    Simple = new Message
                    {
                        Subject = new Content { Data = request.Subject },
                        Body = new Body
                        {
                            Html = request.HtmlBody != null ? new Content { Data = request.HtmlBody } : null,
                            Text = request.TextBody != null ? new Content { Data = request.TextBody } : null
                        }
                    }
                }
            };

            // Use specified ConfigSet or fall back to default
            if (request.ConfigSetId.HasValue)
            {
                var configSet = await _context.ConfigSets.FindAsync(request.ConfigSetId.Value);
                if (configSet != null)
                {
                    sesRequest.ConfigurationSetName = configSet.ConfigSetName;
                }
            }
            else
            {
                // Use the default ConfigSet for this tenant's region
                var defaultConfigSet = await _context.ConfigSets
                    .FirstOrDefaultAsync(cs => cs.SesRegionId == sesRegion.Id && cs.IsDefault, cancellationToken);
                if (defaultConfigSet != null)
                {
                    sesRequest.ConfigurationSetName = defaultConfigSet.ConfigSetName;
                    _logger.LogDebug("Using default ConfigSet '{ConfigSetName}' for email", defaultConfigSet.ConfigSetName);
                }
            }

            // Add AWS SES tenant name to the request for reputation isolation
            if (!string.IsNullOrEmpty(sesRegion.AwsSesTenantName))
            {
                sesRequest.TenantName = sesRegion.AwsSesTenantName;
                _logger.LogInformation("Sending email with AWS SES tenant: {AwsSesTenantName}", sesRegion.AwsSesTenantName);
            }

            var sesResponse = await _sesClient.SendEmailAsync(sesRequest, cancellationToken);

            message.SesMessageId = sesResponse.MessageId;
            message.Status = 1; // Sent
            message.SentAtUtc = DateTime.UtcNow;

            _logger.LogInformation("Email sent successfully. MessageId: {MessageId}, SesMessageId: {SesMessageId}",
                message.Id, sesResponse.MessageId);
        }
        catch (Exception ex)
        {
            message.Status = 2; // Failed
            message.Error = ex.Message;
            _logger.LogError(ex, "Failed to send email. MessageId: {MessageId}", message.Id);
        }

        await _context.SaveChangesAsync(cancellationToken);

        return new DTOs.Responses.SendEmailResponse
        {
            MessageId = message.Id,
            SesMessageId = message.SesMessageId,
            Status = message.Status,
            RequestedAtUtc = message.RequestedAtUtc,
            ScheduledAtUtc = message.ScheduledAtUtc,
            Error = message.Error
        };
    }

    public async Task<DTOs.Responses.SendEmailResponse> SendScheduledMessageAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        // Load the scheduled message with recipients
        var message = await _context.Messages
            .Include(m => m.Tenant)
            .FirstOrDefaultAsync(m => m.Id == messageId && m.Status == 4, cancellationToken);

        if (message == null)
        {
            throw new InvalidOperationException($"Scheduled message {messageId} not found or already processed");
        }

        var recipients = await _context.MessageRecipients
            .Where(r => r.MessageId == messageId)
            .ToListAsync(cancellationToken);

        // Get domain info for SES region
        var fromDomain = message.FromEmail.Split('@')[1];
        var domain = await _context.Domains
            .FirstOrDefaultAsync(d => d.TenantId == message.TenantId && d.Domain == fromDomain && d.VerificationStatus == 1, cancellationToken);

        if (domain == null)
        {
            message.Status = 2; // Failed
            message.Error = $"Domain {fromDomain} is no longer verified";
            await _context.SaveChangesAsync(cancellationToken);

            return new DTOs.Responses.SendEmailResponse
            {
                MessageId = message.Id,
                Status = message.Status,
                RequestedAtUtc = message.RequestedAtUtc,
                ScheduledAtUtc = message.ScheduledAtUtc,
                Error = message.Error
            };
        }

        // Get SES region config
        var sesRegion = await _context.SesRegions
            .FirstOrDefaultAsync(sr => sr.TenantId == message.TenantId && sr.Region == domain.Region, cancellationToken);

        if (sesRegion == null)
        {
            message.Status = 2; // Failed
            message.Error = $"Region {domain.Region} is not configured for tenant";
            await _context.SaveChangesAsync(cancellationToken);

            return new DTOs.Responses.SendEmailResponse
            {
                MessageId = message.Id,
                Status = message.Status,
                RequestedAtUtc = message.RequestedAtUtc,
                ScheduledAtUtc = message.ScheduledAtUtc,
                Error = message.Error
            };
        }

        // Build and send via SES
        try
        {
            var toRecipients = recipients.Where(r => r.Kind == 0).Select(r => r.Email).ToList();
            var ccRecipients = recipients.Where(r => r.Kind == 1).Select(r => r.Email).ToList();
            var bccRecipients = recipients.Where(r => r.Kind == 2).Select(r => r.Email).ToList();

            var sesRequest = new Amazon.SimpleEmailV2.Model.SendEmailRequest
            {
                FromEmailAddress = message.FromName != null
                    ? $"{message.FromName} <{message.FromEmail}>"
                    : message.FromEmail,
                Destination = new Destination
                {
                    ToAddresses = toRecipients,
                    CcAddresses = ccRecipients.Count > 0 ? ccRecipients : null,
                    BccAddresses = bccRecipients.Count > 0 ? bccRecipients : null
                },
                Content = new EmailContent
                {
                    Simple = new Message
                    {
                        Subject = new Content { Data = message.Subject },
                        Body = new Body
                        {
                            Html = message.HtmlBody != null ? new Content { Data = message.HtmlBody } : null,
                            Text = message.TextBody != null ? new Content { Data = message.TextBody } : null
                        }
                    }
                }
            };

            // Use specified ConfigSet or fall back to default
            if (message.ConfigSetId.HasValue)
            {
                var configSet = await _context.ConfigSets.FindAsync(message.ConfigSetId.Value);
                if (configSet != null)
                {
                    sesRequest.ConfigurationSetName = configSet.ConfigSetName;
                }
            }
            else
            {
                // Use the default ConfigSet for this tenant's region
                var defaultConfigSet = await _context.ConfigSets
                    .FirstOrDefaultAsync(cs => cs.SesRegionId == sesRegion.Id && cs.IsDefault, cancellationToken);
                if (defaultConfigSet != null)
                {
                    sesRequest.ConfigurationSetName = defaultConfigSet.ConfigSetName;
                }
            }

            if (!string.IsNullOrEmpty(sesRegion.AwsSesTenantName))
            {
                sesRequest.TenantName = sesRegion.AwsSesTenantName;
            }

            var sesResponse = await _sesClient.SendEmailAsync(sesRequest, cancellationToken);

            message.SesMessageId = sesResponse.MessageId;
            message.Status = 1; // Sent
            message.SentAtUtc = DateTime.UtcNow;

            _logger.LogInformation("Scheduled email sent successfully. MessageId: {MessageId}, SesMessageId: {SesMessageId}",
                message.Id, sesResponse.MessageId);
        }
        catch (Exception ex)
        {
            message.Status = 2; // Failed
            message.Error = ex.Message;
            _logger.LogError(ex, "Failed to send scheduled email. MessageId: {MessageId}", message.Id);
        }

        await _context.SaveChangesAsync(cancellationToken);

        return new DTOs.Responses.SendEmailResponse
        {
            MessageId = message.Id,
            SesMessageId = message.SesMessageId,
            Status = message.Status,
            RequestedAtUtc = message.RequestedAtUtc,
            ScheduledAtUtc = message.ScheduledAtUtc,
            Error = message.Error
        };
    }
}

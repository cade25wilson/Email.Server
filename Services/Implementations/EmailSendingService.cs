using Amazon.SimpleEmailV2.Model;
using MimeKit;
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
    private readonly IUsageTrackingService _usageTracking;
    private readonly ITemplateService _templateService;
    private readonly IAttachmentStorageService _attachmentStorage;
    private readonly ILogger<EmailSendingService> _logger;

    private const long MaxAttachmentSize = 10 * 1024 * 1024; // 10MB total

    public EmailSendingService(
        ApplicationDbContext context,
        ITenantContextService tenantContext,
        ISesClientService sesClient,
        IUsageTrackingService usageTracking,
        ITemplateService templateService,
        IAttachmentStorageService attachmentStorage,
        ILogger<EmailSendingService> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _sesClient = sesClient;
        _usageTracking = usageTracking;
        _templateService = templateService;
        _attachmentStorage = attachmentStorage;
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

        // Check usage limits before sending
        var recipientCount = request.To.Count + (request.Cc?.Count ?? 0) + (request.Bcc?.Count ?? 0);
        var usageCheck = await _usageTracking.CheckUsageLimitAsync(tenantId, recipientCount, cancellationToken);
        if (!usageCheck.Allowed)
        {
            throw new InvalidOperationException(usageCheck.DenialReason ?? "Email sending limit exceeded. Please upgrade your plan or wait for the next billing period.");
        }

        // Render template if specified
        var subject = request.Subject;
        var htmlBody = request.HtmlBody;
        var textBody = request.TextBody;

        if (request.TemplateId.HasValue)
        {
            var rendered = await _templateService.RenderTemplateAsync(
                request.TemplateId.Value,
                request.TemplateVariables,
                cancellationToken);

            subject = rendered.Subject ?? subject;
            htmlBody = rendered.HtmlBody ?? htmlBody;
            textBody = rendered.TextBody ?? textBody;

            _logger.LogDebug("Rendered template {TemplateId} for email", request.TemplateId.Value);
        }

        // Determine if this is a scheduled send
        var isScheduled = request.ScheduledAtUtc.HasValue && request.ScheduledAtUtc.Value > DateTime.UtcNow;

        // Create message entity (store rendered content)
        var message = new Messages
        {
            TenantId = tenantId,
            Region = domain.Region,
            ConfigSetId = request.ConfigSetId,
            FromEmail = request.FromEmail,
            FromName = request.FromName,
            Subject = subject,
            HtmlBody = htmlBody,
            TextBody = textBody,
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

        // Process attachments
        var attachmentRecords = new List<MessageAttachments>();
        if (request.Attachments != null && request.Attachments.Count > 0)
        {
            long totalSize = 0;
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            foreach (var attachment in request.Attachments)
            {
                byte[] contentBytes;
                string contentType;

                // Validate: either content or path must be provided
                if (string.IsNullOrEmpty(attachment.Content) && string.IsNullOrEmpty(attachment.Path))
                {
                    throw new InvalidOperationException($"Attachment '{attachment.Filename}' must have either 'content' or 'path' specified");
                }

                if (!string.IsNullOrEmpty(attachment.Path))
                {
                    // Fetch from URL
                    _logger.LogDebug("Fetching attachment from URL: {Url}", attachment.Path);

                    try
                    {
                        var response = await httpClient.GetAsync(attachment.Path, cancellationToken);
                        response.EnsureSuccessStatusCode();

                        contentBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

                        // Use provided content_type or infer from response headers
                        contentType = attachment.ContentType
                            ?? response.Content.Headers.ContentType?.MediaType
                            ?? "application/octet-stream";
                    }
                    catch (HttpRequestException ex)
                    {
                        throw new InvalidOperationException($"Failed to fetch attachment from '{attachment.Path}': {ex.Message}");
                    }
                }
                else
                {
                    // Use base64 content
                    contentBytes = Convert.FromBase64String(attachment.Content!);
                    contentType = attachment.ContentType ?? "application/octet-stream";
                }

                totalSize += contentBytes.Length;

                if (totalSize > MaxAttachmentSize)
                {
                    throw new InvalidOperationException("Total attachment size exceeds 10MB limit");
                }

                // Store to S3
                using var stream = new MemoryStream(contentBytes);
                var s3Key = await _attachmentStorage.UploadAttachmentAsync(
                    tenantId,
                    message.Id,
                    attachment.Filename,
                    contentType,
                    stream,
                    cancellationToken);

                var attachmentRecord = new MessageAttachments
                {
                    MessageId = message.Id,
                    FileName = attachment.Filename,
                    ContentType = contentType,
                    SizeBytes = contentBytes.Length,
                    BlobUrl = s3Key,
                    IsInline = false
                };

                attachmentRecords.Add(attachmentRecord);
                _context.MessageAttachments.Add(attachmentRecord);
            }

            _logger.LogInformation("Processed {Count} attachments ({Size} bytes) for message {MessageId}",
                request.Attachments.Count, totalSize, message.Id);
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
            // Determine configuration set
            string? configurationSetName = null;
            if (request.ConfigSetId.HasValue)
            {
                var configSet = await _context.ConfigSets.FindAsync(request.ConfigSetId.Value);
                if (configSet != null)
                {
                    configurationSetName = configSet.ConfigSetName;
                }
            }
            else
            {
                var defaultConfigSet = await _context.ConfigSets
                    .FirstOrDefaultAsync(cs => cs.SesRegionId == sesRegion.Id && cs.IsDefault, cancellationToken);
                if (defaultConfigSet != null)
                {
                    configurationSetName = defaultConfigSet.ConfigSetName;
                    _logger.LogDebug("Using default ConfigSet '{ConfigSetName}' for email", configurationSetName);
                }
            }

            Amazon.SimpleEmailV2.Model.SendEmailRequest sesRequest;

            // Use raw MIME message if there are attachments
            if (request.Attachments != null && request.Attachments.Count > 0)
            {
                var rawMessage = BuildRawMimeMessage(request, subject, htmlBody, textBody);
                sesRequest = new Amazon.SimpleEmailV2.Model.SendEmailRequest
                {
                    Content = new EmailContent
                    {
                        Raw = new RawMessage
                        {
                            Data = rawMessage
                        }
                    }
                };
            }
            else
            {
                // Simple message without attachments
                sesRequest = new Amazon.SimpleEmailV2.Model.SendEmailRequest
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
                            Subject = new Content { Data = subject },
                            Body = new Body
                            {
                                Html = htmlBody != null ? new Content { Data = htmlBody } : null,
                                Text = textBody != null ? new Content { Data = textBody } : null
                            }
                        }
                    }
                };
            }

            if (configurationSetName != null)
            {
                sesRequest.ConfigurationSetName = configurationSetName;
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

            // Record usage for billing
            await _usageTracking.RecordEmailSendAsync(tenantId, recipientCount, "API", cancellationToken);
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

    public async Task<BatchEmailResponse> SendBatchEmailsAsync(SendBatchEmailRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Emails == null || request.Emails.Count == 0)
        {
            throw new ArgumentException("At least one email is required");
        }

        if (request.Emails.Count > 100)
        {
            throw new ArgumentException("Maximum 100 emails per batch");
        }

        var response = new BatchEmailResponse
        {
            Total = request.Emails.Count
        };

        var results = new List<BatchEmailItemResult>();
        var semaphore = new SemaphoreSlim(10); // Max 10 concurrent sends

        var tasks = request.Emails.Select(async (email, index) =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var sendResponse = await SendEmailAsync(email, cancellationToken);
                return new BatchEmailItemResult
                {
                    Index = index,
                    Success = sendResponse.Status != 2, // Not failed
                    MessageId = sendResponse.MessageId,
                    Error = sendResponse.Error
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email at index {Index}", index);
                return new BatchEmailItemResult
                {
                    Index = index,
                    Success = false,
                    Error = ex.Message
                };
            }
            finally
            {
                semaphore.Release();
            }
        });

        results = (await Task.WhenAll(tasks)).ToList();

        response.Results = results.OrderBy(r => r.Index).ToList();
        response.Succeeded = results.Count(r => r.Success);
        response.Failed = results.Count(r => !r.Success);

        _logger.LogInformation("Batch send completed: {Succeeded}/{Total} succeeded", response.Succeeded, response.Total);

        return response;
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

        // Check usage limits before sending
        var recipientCount = recipients.Count;
        var usageCheck = await _usageTracking.CheckUsageLimitAsync(message.TenantId, recipientCount, cancellationToken);
        if (!usageCheck.Allowed)
        {
            message.Status = 2; // Failed
            message.Error = usageCheck.DenialReason ?? "Email sending limit exceeded. Please upgrade your plan or wait for the next billing period.";
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

            // Record usage for billing
            await _usageTracking.RecordEmailSendAsync(message.TenantId, recipientCount, "Scheduled", cancellationToken);
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

    private static MemoryStream BuildRawMimeMessage(
        DTOs.Requests.SendEmailRequest request,
        string subject,
        string? htmlBody,
        string? textBody)
    {
        var message = new MimeMessage();

        // Set from address
        message.From.Add(request.FromName != null
            ? new MailboxAddress(request.FromName, request.FromEmail)
            : new MailboxAddress(request.FromEmail, request.FromEmail));

        // Add recipients
        foreach (var to in request.To)
        {
            message.To.Add(to.Name != null
                ? new MailboxAddress(to.Name, to.Email)
                : new MailboxAddress(to.Email, to.Email));
        }

        if (request.Cc != null)
        {
            foreach (var cc in request.Cc)
            {
                message.Cc.Add(cc.Name != null
                    ? new MailboxAddress(cc.Name, cc.Email)
                    : new MailboxAddress(cc.Email, cc.Email));
            }
        }

        if (request.Bcc != null)
        {
            foreach (var bcc in request.Bcc)
            {
                message.Bcc.Add(bcc.Name != null
                    ? new MailboxAddress(bcc.Name, bcc.Email)
                    : new MailboxAddress(bcc.Email, bcc.Email));
            }
        }

        message.Subject = subject;

        // Build the body
        var builder = new BodyBuilder();

        if (!string.IsNullOrEmpty(textBody))
        {
            builder.TextBody = textBody;
        }

        if (!string.IsNullOrEmpty(htmlBody))
        {
            builder.HtmlBody = htmlBody;
        }

        // Add attachments - fetch content from URL or decode base64
        if (request.Attachments != null)
        {
            foreach (var attachment in request.Attachments)
            {
                byte[] contentBytes;
                string contentType;

                if (!string.IsNullOrEmpty(attachment.Path))
                {
                    // Fetch from URL synchronously (we're in a static method)
                    using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                    var response = httpClient.GetAsync(attachment.Path).GetAwaiter().GetResult();
                    response.EnsureSuccessStatusCode();
                    contentBytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                    contentType = attachment.ContentType
                        ?? response.Content.Headers.ContentType?.MediaType
                        ?? "application/octet-stream";
                }
                else
                {
                    contentBytes = Convert.FromBase64String(attachment.Content!);
                    contentType = attachment.ContentType ?? "application/octet-stream";
                }

                builder.Attachments.Add(attachment.Filename, contentBytes, ContentType.Parse(contentType));
            }
        }

        message.Body = builder.ToMessageBody();

        // Write to stream
        var stream = new MemoryStream();
        message.WriteTo(stream);
        stream.Position = 0;

        return stream;
    }
}

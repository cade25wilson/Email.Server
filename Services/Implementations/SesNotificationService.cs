using System.Text.Json;
using Email.Server.Data;
using Email.Server.DTOs.Sns;
using Email.Server.Models;
using Email.Server.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Email.Server.Services.Implementations;

public class SesNotificationService(
    ApplicationDbContext dbContext,
    IWebhookDeliveryService webhookDeliveryService,
    ILogger<SesNotificationService> logger) : ISesNotificationService
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly IWebhookDeliveryService _webhookDeliveryService = webhookDeliveryService;
    private readonly ILogger<SesNotificationService> _logger = logger;

    public async Task ProcessNotificationAsync(SesNotification notification, CancellationToken cancellationToken = default)
    {
        var sesMessageId = notification.Mail.MessageId;

        _logger.LogInformation("Processing SES notification: Type={NotificationType}, SesMessageId={SesMessageId}",
            notification.NotificationType, sesMessageId);

        // Find the message by SES message ID
        var message = await _dbContext.Messages
            .FirstOrDefaultAsync(m => m.SesMessageId == sesMessageId, cancellationToken);

        if (message == null)
        {
            _logger.LogWarning("Message not found for SES message ID: {SesMessageId}", sesMessageId);
            return;
        }

        switch (notification.NotificationType.ToLowerInvariant())
        {
            case "delivery":
                await ProcessDeliveryAsync(message, notification, cancellationToken);
                break;
            case "bounce":
                await ProcessBounceAsync(message, notification, cancellationToken);
                break;
            case "complaint":
                await ProcessComplaintAsync(message, notification, cancellationToken);
                break;
            default:
                _logger.LogWarning("Unknown notification type: {NotificationType}", notification.NotificationType);
                break;
        }
    }

    private async Task ProcessDeliveryAsync(Messages message, SesNotification notification, CancellationToken cancellationToken)
    {
        var delivery = notification.Delivery;
        if (delivery == null) return;

        _logger.LogInformation("Processing delivery for message {MessageId}, recipients: {Recipients}",
            message.Id, string.Join(", ", delivery.Recipients));

        // Update recipient delivery status
        foreach (var recipientEmail in delivery.Recipients)
        {
            var recipient = await _dbContext.MessageRecipients
                .FirstOrDefaultAsync(r => r.MessageId == message.Id &&
                    r.Email.ToLower() == recipientEmail.ToLower(), cancellationToken);

            if (recipient != null)
            {
                recipient.DeliveryStatus = 1; // Delivered
                _logger.LogInformation("Updated recipient {Email} to Delivered", recipientEmail);
            }
        }

        // Create event record
        var messageEvent = new MessageEvents
        {
            MessageId = message.Id,
            TenantId = message.TenantId,
            Region = message.Region,
            EventType = "Delivery",
            OccurredAtUtc = delivery.Timestamp,
            Recipient = delivery.Recipients.FirstOrDefault(),
            PayloadJson = JsonSerializer.Serialize(notification)
        };
        _dbContext.MessageEvents.Add(messageEvent);

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Trigger webhooks for this event
        await TriggerWebhooksAsync(messageEvent, cancellationToken);
    }

    private async Task ProcessBounceAsync(Messages message, SesNotification notification, CancellationToken cancellationToken)
    {
        var bounce = notification.Bounce;
        if (bounce == null) return;

        _logger.LogInformation("Processing bounce for message {MessageId}, type: {BounceType}, subtype: {BounceSubType}",
            message.Id, bounce.BounceType, bounce.BounceSubType);

        foreach (var bouncedRecipient in bounce.BouncedRecipients)
        {
            // Update recipient delivery status
            var recipient = await _dbContext.MessageRecipients
                .FirstOrDefaultAsync(r => r.MessageId == message.Id &&
                    r.Email.ToLower() == bouncedRecipient.EmailAddress.ToLower(), cancellationToken);

            if (recipient != null)
            {
                recipient.DeliveryStatus = 2; // Bounced
                _logger.LogInformation("Updated recipient {Email} to Bounced", bouncedRecipient.EmailAddress);
            }

            // Add to suppression list for hard bounces
            if (bounce.BounceType == "Permanent")
            {
                var existingSuppression = await _dbContext.Suppressions
                    .FirstOrDefaultAsync(s => s.TenantId == message.TenantId &&
                        s.Email.ToLower() == bouncedRecipient.EmailAddress.ToLower(), cancellationToken);

                if (existingSuppression == null)
                {
                    var suppression = new Suppressions
                    {
                        TenantId = message.TenantId,
                        Region = message.Region,
                        Email = bouncedRecipient.EmailAddress.ToLower(),
                        Reason = "bounce",
                        Source = "ses",
                        CreatedAtUtc = DateTime.UtcNow
                    };
                    _dbContext.Suppressions.Add(suppression);
                    _logger.LogInformation("Added {Email} to suppression list due to permanent bounce: {BounceType}/{BounceSubType}",
                        bouncedRecipient.EmailAddress, bounce.BounceType, bounce.BounceSubType);
                }
            }

            // Create event record
            var messageEvent = new MessageEvents
            {
                MessageId = message.Id,
                TenantId = message.TenantId,
                Region = message.Region,
                EventType = "Bounce",
                OccurredAtUtc = bounce.Timestamp,
                Recipient = bouncedRecipient.EmailAddress,
                PayloadJson = JsonSerializer.Serialize(notification)
            };
            _dbContext.MessageEvents.Add(messageEvent);

            await _dbContext.SaveChangesAsync(cancellationToken);

            // Trigger webhooks for this event
            await TriggerWebhooksAsync(messageEvent, cancellationToken);
        }
    }

    private async Task ProcessComplaintAsync(Messages message, SesNotification notification, CancellationToken cancellationToken)
    {
        var complaint = notification.Complaint;
        if (complaint == null) return;

        _logger.LogInformation("Processing complaint for message {MessageId}", message.Id);

        foreach (var complainedRecipient in complaint.ComplainedRecipients)
        {
            // Update recipient delivery status
            var recipient = await _dbContext.MessageRecipients
                .FirstOrDefaultAsync(r => r.MessageId == message.Id &&
                    r.Email.ToLower() == complainedRecipient.EmailAddress.ToLower(), cancellationToken);

            if (recipient != null)
            {
                recipient.DeliveryStatus = 3; // Complained
                _logger.LogInformation("Updated recipient {Email} to Complained", complainedRecipient.EmailAddress);
            }

            // Add to suppression list
            var existingSuppression = await _dbContext.Suppressions
                .FirstOrDefaultAsync(s => s.TenantId == message.TenantId &&
                    s.Email.ToLower() == complainedRecipient.EmailAddress.ToLower(), cancellationToken);

            if (existingSuppression == null)
            {
                var suppression = new Suppressions
                {
                    TenantId = message.TenantId,
                    Region = message.Region,
                    Email = complainedRecipient.EmailAddress.ToLower(),
                    Reason = "complaint",
                    Source = "ses",
                    CreatedAtUtc = DateTime.UtcNow
                };
                _dbContext.Suppressions.Add(suppression);
                _logger.LogInformation("Added {Email} to suppression list due to complaint: {ComplaintType}",
                    complainedRecipient.EmailAddress, complaint.ComplaintFeedbackType ?? "unknown");
            }

            // Create event record
            var messageEvent = new MessageEvents
            {
                MessageId = message.Id,
                TenantId = message.TenantId,
                Region = message.Region,
                EventType = "Complaint",
                OccurredAtUtc = complaint.Timestamp,
                Recipient = complainedRecipient.EmailAddress,
                PayloadJson = JsonSerializer.Serialize(notification)
            };
            _dbContext.MessageEvents.Add(messageEvent);

            await _dbContext.SaveChangesAsync(cancellationToken);

            // Trigger webhooks for this event
            await TriggerWebhooksAsync(messageEvent, cancellationToken);
        }
    }

    private async Task TriggerWebhooksAsync(MessageEvents messageEvent, CancellationToken cancellationToken)
    {
        try
        {
            await _webhookDeliveryService.TriggerWebhooksForEventAsync(messageEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            // Don't fail the notification processing if webhook triggering fails
            _logger.LogError(ex, "Failed to trigger webhooks for event {EventId}", messageEvent.Id);
        }
    }
}

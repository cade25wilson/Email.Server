using Email.Server.DTOs.Requests;
using Email.Server.DTOs.Responses;

namespace Email.Server.Services.Interfaces;

public interface IEmailSendingService
{
    Task<SendEmailResponse> SendEmailAsync(SendEmailRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a previously scheduled message by its ID. Used by the background scheduler.
    /// </summary>
    Task<SendEmailResponse> SendScheduledMessageAsync(Guid messageId, CancellationToken cancellationToken = default);
}

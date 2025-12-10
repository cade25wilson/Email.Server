using Email.Server.DTOs.Requests;
using Email.Server.DTOs.Responses;

namespace Email.Server.Services.Interfaces;

public interface IMessageService
{
    Task<MessageResponse> GetMessageAsync(Guid messageId, CancellationToken cancellationToken = default);
    Task<IEnumerable<MessageResponse>> GetMessagesAsync(int page = 1, int pageSize = 50, CancellationToken cancellationToken = default);

    /// <summary>
    /// List emails with pagination, filtering, and sorting
    /// </summary>
    Task<EmailListResponse> ListEmailsAsync(ListEmailsRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update a scheduled email (Status=4 only)
    /// </summary>
    Task<MessageResponse> UpdateScheduledEmailAsync(Guid messageId, UpdateScheduledEmailRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancel a scheduled email (Status=4 only). Sets status to 5 (Cancelled).
    /// </summary>
    Task CancelScheduledEmailAsync(Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get attachments for an email
    /// </summary>
    Task<List<AttachmentResponse>> GetAttachmentsAsync(Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a signed download URL for an attachment
    /// </summary>
    Task<AttachmentDownloadResponse> GetAttachmentDownloadUrlAsync(Guid messageId, long attachmentId, CancellationToken cancellationToken = default);
}

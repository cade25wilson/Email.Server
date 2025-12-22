using Email.Server.DTOs.Requests;
using Email.Server.DTOs.Responses;
using Email.Server.Models;

namespace Email.Server.Services.Interfaces;

public interface ISmsService
{
    /// <summary>
    /// Sends an SMS message immediately or schedules it for later delivery.
    /// </summary>
    Task<SendSmsResponse> SendSmsAsync(SendSmsRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a previously scheduled SMS message by its ID. Used by the background scheduler.
    /// </summary>
    Task<SendSmsResponse> SendScheduledSmsAsync(Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an SMS message by ID.
    /// </summary>
    Task<SmsMessages?> GetSmsAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists SMS messages with pagination and filtering.
    /// </summary>
    Task<SmsMessageListResponse> ListSmsAsync(SmsQueryParams query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a scheduled SMS message.
    /// </summary>
    Task<bool> CancelScheduledSmsAsync(Guid id, CancellationToken cancellationToken = default);
}

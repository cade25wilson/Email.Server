using Email.Server.DTOs.Inbound;
using Email.Server.DTOs.Responses;

namespace Email.Server.Services.Interfaces;

public interface IInboundEmailService
{
    Task<InboundMessageResponse> ProcessInboundEmailAsync(
        InboundEmailNotification notification,
        CancellationToken cancellationToken = default);

    Task<InboundMessageListResponse> GetInboundMessagesAsync(
        int page = 1,
        int pageSize = 50,
        Guid? domainId = null,
        CancellationToken cancellationToken = default);

    Task<InboundMessageResponse?> GetInboundMessageAsync(
        Guid messageId,
        CancellationToken cancellationToken = default);

    Task<InboundEmailDownloadResponse> GetRawEmailUrlAsync(
        Guid messageId,
        CancellationToken cancellationToken = default);

    Task DeleteInboundMessageAsync(
        Guid messageId,
        CancellationToken cancellationToken = default);
}

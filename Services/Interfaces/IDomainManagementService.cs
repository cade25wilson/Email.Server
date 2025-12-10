using Email.Server.DTOs.Requests;
using Email.Server.DTOs.Responses;

namespace Email.Server.Services.Interfaces;

public interface IDomainManagementService
{
    Task<DomainResponse> CreateDomainAsync(CreateDomainRequest request, CancellationToken cancellationToken = default);
    Task<DomainResponse> GetDomainAsync(Guid domainId, CancellationToken cancellationToken = default);
    Task<IEnumerable<DomainResponse>> GetDomainsAsync(CancellationToken cancellationToken = default);
    Task<DomainResponse> VerifyDomainAsync(Guid domainId, CancellationToken cancellationToken = default);
    Task DeleteDomainAsync(Guid domainId, CancellationToken cancellationToken = default);
    Task<DomainResponse> EnableInboundAsync(Guid domainId, CancellationToken cancellationToken = default);
    Task<DomainResponse> DisableInboundAsync(Guid domainId, CancellationToken cancellationToken = default);
}

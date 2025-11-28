using Amazon.SimpleEmailV2.Model;

namespace Email.Server.Services.Interfaces;

public interface ISesClientService
{
    Task<CreateEmailIdentityResponse> CreateEmailIdentityAsync(string domain, CancellationToken cancellationToken = default);
    Task<GetEmailIdentityResponse> GetEmailIdentityAsync(string domain, CancellationToken cancellationToken = default);
    Task<DeleteEmailIdentityResponse> DeleteEmailIdentityAsync(string domain, CancellationToken cancellationToken = default);
    Task<SendEmailResponse> SendEmailAsync(SendEmailRequest request, CancellationToken cancellationToken = default);
    Task<CreateConfigurationSetResponse> CreateConfigurationSetAsync(string configurationSetName, CancellationToken cancellationToken = default);
    Task<DeleteConfigurationSetResponse> DeleteConfigurationSetAsync(string configurationSetName, CancellationToken cancellationToken = default);

    // AWS SES Tenant Management
    Task<CreateTenantResponse> CreateSesTenantAsync(string tenantName, CancellationToken cancellationToken = default);
    Task<CreateTenantResourceAssociationResponse> AssociateResourceToTenantAsync(string tenantName, string resourceArn, CancellationToken cancellationToken = default);
    Task DisassociateResourceFromTenantAsync(string tenantName, string resourceArn, CancellationToken cancellationToken = default);
    Task<DeleteTenantResponse> DeleteSesTenantAsync(string tenantName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the sending status for a tenant (enable/disable sending)
    /// </summary>
    /// <param name="tenantArn">The ARN of the tenant</param>
    /// <param name="enabled">True to enable sending, false to disable</param>
    Task UpdateTenantSendingStatusAsync(string tenantArn, bool enabled, CancellationToken cancellationToken = default);
}

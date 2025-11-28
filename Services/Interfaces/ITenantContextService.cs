namespace Email.Server.Services.Interfaces;

public interface ITenantContextService
{
    Guid GetTenantId();
    string GetUserId();
    bool HasTenant();
    bool IsApiKeyAuthenticated();
    Guid? GetApiKeyId();
}

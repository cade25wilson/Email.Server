using System.Security.Claims;
using Email.Server.Services.Interfaces;

namespace Email.Server.Services.Implementations;

public class TenantContextService : ITenantContextService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public TenantContextService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid GetTenantId()
    {
        var tenantIdClaim = _httpContextAccessor.HttpContext?.User?.FindFirst("TenantId");

        if (tenantIdClaim == null || !Guid.TryParse(tenantIdClaim.Value, out var tenantId))
        {
            throw new UnauthorizedAccessException("Tenant ID not found in token");
        }

        return tenantId;
    }

    public string GetUserId()
    {
        var userId = _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            throw new UnauthorizedAccessException("User ID not found in token");
        }

        return userId;
    }

    public bool HasTenant()
    {
        var tenantIdClaim = _httpContextAccessor.HttpContext?.User?.FindFirst("TenantId");
        return tenantIdClaim != null && Guid.TryParse(tenantIdClaim.Value, out _);
    }

    public bool IsApiKeyAuthenticated()
    {
        var authMethod = _httpContextAccessor.HttpContext?.User?.FindFirst("AuthMethod")?.Value;
        return authMethod == "ApiKey";
    }

    public Guid? GetApiKeyId()
    {
        var apiKeyIdClaim = _httpContextAccessor.HttpContext?.User?.FindFirst("ApiKeyId");
        if (apiKeyIdClaim != null && Guid.TryParse(apiKeyIdClaim.Value, out var keyId))
        {
            return keyId;
        }
        return null;
    }
}

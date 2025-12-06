using System.Security.Claims;
using Email.Server.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

namespace Email.Server.Authentication;

/// <summary>
/// Transforms incoming JWT claims to add TenantId claim
/// This runs after authentication and adds the user's tenant to their claims
/// </summary>
public class TenantClaimsTransformation : IClaimsTransformation
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TenantClaimsTransformation> _logger;

    public TenantClaimsTransformation(
        IServiceScopeFactory scopeFactory,
        ILogger<TenantClaimsTransformation> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        // Skip if not authenticated
        if (principal.Identity?.IsAuthenticated != true)
        {
            return principal;
        }

        // Skip if this is API key auth (already has TenantId)
        var authMethod = principal.FindFirstValue("AuthMethod");
        if (authMethod == "ApiKey")
        {
            return principal;
        }

        // Skip if TenantId already present (avoid double transformation)
        if (principal.HasClaim(c => c.Type == "TenantId"))
        {
            return principal;
        }

        // Get user ID from various possible claims
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("oid")
            ?? principal.FindFirstValue("sub");

        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogDebug("No user ID found in claims, skipping tenant transformation");
            return principal;
        }

        // Look up user's tenant
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var tenantMembership = await dbContext.TenantMembers
            .Where(tm => tm.UserId == userId)
            .OrderBy(tm => tm.JoinedAtUtc) // Get the oldest (first) tenant
            .FirstOrDefaultAsync();

        if (tenantMembership == null)
        {
            _logger.LogDebug("User {UserId} has no tenant membership", userId);
            return principal;
        }

        // Clone the identity and add the TenantId claim
        var claimsIdentity = principal.Identity as ClaimsIdentity;
        if (claimsIdentity == null)
        {
            return principal;
        }

        // Create a new identity with the tenant claim
        var newIdentity = new ClaimsIdentity(claimsIdentity.Claims, claimsIdentity.AuthenticationType);
        newIdentity.AddClaim(new Claim("TenantId", tenantMembership.TenantId.ToString()));

        _logger.LogDebug("Added TenantId {TenantId} claim for user {UserId}", tenantMembership.TenantId, userId);

        return new ClaimsPrincipal(newIdentity);
    }
}

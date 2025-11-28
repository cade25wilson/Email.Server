using Microsoft.AspNetCore.Authorization;

namespace Email.Server.Authentication;

/// <summary>
/// Requirement for a specific API key scope
/// </summary>
public class ApiKeyScopeRequirement : IAuthorizationRequirement
{
    public string RequiredScope { get; }

    public ApiKeyScopeRequirement(string requiredScope)
    {
        RequiredScope = requiredScope;
    }
}

/// <summary>
/// Handler that checks if the user has the required scope.
/// JWT-authenticated users are granted all scopes (full access).
/// API key-authenticated users must have the specific scope claim.
/// </summary>
public class ApiKeyScopeHandler : AuthorizationHandler<ApiKeyScopeRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ApiKeyScopeRequirement requirement)
    {
        // Check if user is authenticated
        if (!context.User.Identity?.IsAuthenticated ?? true)
        {
            return Task.CompletedTask;
        }

        // Check auth method
        var authMethod = context.User.FindFirst("AuthMethod")?.Value;

        // JWT users get full access (no scope restrictions)
        if (authMethod != "ApiKey")
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        // API key users must have the specific scope
        var scopes = context.User.FindAll("scope").Select(c => c.Value).ToList();

        if (scopes.Contains(requirement.RequiredScope))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

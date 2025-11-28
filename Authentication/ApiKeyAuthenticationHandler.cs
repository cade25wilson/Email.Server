using System.Security.Claims;
using System.Text.Encodings.Web;
using Email.Server.Services.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Email.Server.Authentication;

public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    private readonly IApiKeyService _apiKeyService;

    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-API-Key";

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IApiKeyService apiKeyService)
        : base(options, logger, encoder)
    {
        _apiKeyService = apiKeyService;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Check for API key header
        if (!Request.Headers.TryGetValue(HeaderName, out var apiKeyHeader))
        {
            return AuthenticateResult.NoResult();
        }

        var apiKey = apiKeyHeader.ToString();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return AuthenticateResult.Fail("API key is empty");
        }

        // Validate API key
        var keyInfo = await _apiKeyService.ValidateAsync(apiKey, Context.RequestAborted);
        if (keyInfo == null)
        {
            return AuthenticateResult.Fail("Invalid API key");
        }

        // Update last used (fire-and-forget)
        _ = _apiKeyService.UpdateLastUsedAsync(keyInfo.KeyId, CancellationToken.None);

        // Create claims
        var claims = new List<Claim>
        {
            new("TenantId", keyInfo.TenantId.ToString()),
            new("ApiKeyId", keyInfo.KeyId.ToString()),
            new("AuthMethod", "ApiKey")
        };

        // Add each scope as a claim
        foreach (var scope in keyInfo.Scopes)
        {
            claims.Add(new Claim("scope", scope));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return AuthenticateResult.Success(ticket);
    }
}

public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
}

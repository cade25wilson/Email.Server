using Email.Server.DTOs.Requests;
using Email.Server.DTOs.Responses;
using Email.Server.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Email.Server.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize] // JWT only - managing API keys requires user authentication
public class ApiKeysController(
    IApiKeyService apiKeyService,
    ITenantContextService tenantContext,
    ILogger<ApiKeysController> logger) : ControllerBase
{
    private readonly IApiKeyService _apiKeyService = apiKeyService;
    private readonly ITenantContextService _tenantContext = tenantContext;
    private readonly ILogger<ApiKeysController> _logger = logger;

    /// <summary>
    /// Create a new API key for the current tenant
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateApiKey([FromBody] CreateApiKeyRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        // Validate scopes
        var invalidScopes = request.Scopes.Where(s => !ApiKeyScopes.IsValid(s)).ToList();
        if (invalidScopes.Count != 0)
        {
            return BadRequest(new { error = $"Invalid scopes: {string.Join(", ", invalidScopes)}. Valid scopes: {string.Join(", ", ApiKeyScopes.All)}" });
        }

        try
        {
            var tenantId = _tenantContext.GetTenantId();
            var result = await _apiKeyService.CreateAsync(tenantId, request.DomainId, request.Name, request.Scopes, ct);

            return CreatedAtAction(nameof(GetApiKeys), new CreateApiKeyResponse
            {
                Id = result.Id,
                Name = result.Name,
                Key = result.Key,
                KeyPreview = result.KeyPreview,
                Scopes = result.Scopes,
                DomainId = result.DomainId,
                DomainName = result.DomainName,
                CreatedAtUtc = result.CreatedAtUtc
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating API key");
            return StatusCode(500, new { error = "An error occurred while creating the API key" });
        }
    }

    /// <summary>
    /// List all API keys for the current tenant
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetApiKeys(CancellationToken ct)
    {
        try
        {
            var tenantId = _tenantContext.GetTenantId();
            var keys = await _apiKeyService.GetAllAsync(tenantId, ct);

            return Ok(keys.Select(k => new ApiKeyListItemResponse
            {
                Id = k.Id,
                Name = k.Name,
                KeyPreview = k.KeyPreview,
                Scopes = k.Scopes,
                DomainId = k.DomainId,
                DomainName = k.DomainName,
                CreatedAtUtc = k.CreatedAtUtc,
                LastUsedAtUtc = k.LastUsedAtUtc,
                IsRevoked = k.IsRevoked
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing API keys");
            return StatusCode(500, new { error = "An error occurred while listing API keys" });
        }
    }

    /// <summary>
    /// Revoke an API key
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> RevokeApiKey(Guid id, CancellationToken ct)
    {
        try
        {
            var tenantId = _tenantContext.GetTenantId();
            var revoked = await _apiKeyService.RevokeAsync(tenantId, id, ct);

            if (!revoked)
            {
                return NotFound(new { error = "API key not found" });
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revoking API key {KeyId}", id);
            return StatusCode(500, new { error = "An error occurred while revoking the API key" });
        }
    }

    /// <summary>
    /// Get available scopes and permission presets
    /// </summary>
    [HttpGet("scopes")]
    [AllowAnonymous]
    public IActionResult GetAvailableScopes()
    {
        return Ok(new
        {
            scopes = ApiKeyScopes.All.Select(s => new
            {
                name = s,
                description = s switch
                {
                    ApiKeyScopes.EmailsSend => "Send emails through the API",
                    ApiKeyScopes.DomainsRead => "View domains and their verification status",
                    ApiKeyScopes.DomainsWrite => "Create and verify domains",
                    ApiKeyScopes.DomainsDelete => "Delete domains",
                    ApiKeyScopes.MessagesRead => "View sent message history",
                    _ => s
                }
            }),
            presets = new[]
            {
                new
                {
                    name = "full_access",
                    label = "Full Access",
                    description = "All permissions - full control over domains, emails, and messages",
                    scopes = ApiKeyScopes.FullAccess
                },
                new
                {
                    name = "sending_only",
                    label = "Sending Only",
                    description = "Send emails and view message history",
                    scopes = ApiKeyScopes.SendingOnly
                }
            }
        });
    }
}

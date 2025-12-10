using Email.Server.Data;
using Email.Server.Models;
using Email.Server.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Email.Server.Controllers;

// Use TenantInfo and SwitchTenantRequest from Email.Server.Models.AuthModels

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class EntraAuthController(
    ApplicationDbContext dbContext,
    ITenantManagementService tenantManagementService,
    ILogger<EntraAuthController> logger) : ControllerBase
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly ITenantManagementService _tenantManagementService = tenantManagementService;
    private readonly ILogger<EntraAuthController> _logger = logger;

    /// <summary>
    /// Initialize user session - creates tenant on first login
    /// This should be called by the frontend after Entra authentication
    /// </summary>
    [HttpPost("initialize")]
    public async Task<IActionResult> InitializeUser([FromBody] InitializeUserRequest? request = null)
    {
        // Log all claims for debugging
        _logger.LogInformation("Initialize called. User authenticated: {IsAuth}", User.Identity?.IsAuthenticated);
        foreach (var claim in User.Claims)
        {
            _logger.LogDebug("Claim: {Type} = {Value}", claim.Type, claim.Value);
        }

        // Get user info from Entra claims
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("oid")
            ?? User.FindFirstValue("sub");

        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("No user ID found in claims");
            return Unauthorized(new { error = "User ID not found in token" });
        }

        var email = User.FindFirstValue(ClaimTypes.Email)
            ?? User.FindFirstValue("preferred_username")
            ?? User.FindFirstValue("email");

        var name = User.FindFirstValue(ClaimTypes.Name)
            ?? User.FindFirstValue("name")
            ?? email;

        _logger.LogInformation("Initializing user {UserId} with email {Email}", userId, email);

        // Check if user already has tenants
        var existingMembership = await _dbContext.TenantMembers
            .Include(tm => tm.Tenant)
            .Where(tm => tm.UserId == userId)
            .FirstOrDefaultAsync();

        if (existingMembership != null)
        {
            // User already has a tenant, return it
            var userTenants = await _dbContext.TenantMembers
                .Include(tm => tm.Tenant)
                .Where(tm => tm.UserId == userId)
                .Select(tm => new TenantInfo
                {
                    Id = tm.TenantId,
                    Name = tm.Tenant!.Name
                })
                .ToListAsync();

            _logger.LogInformation("User {UserId} already has {Count} tenant(s)", userId, userTenants.Count);

            // Use preferred tenant if provided and user has access to it
            var selectedTenantId = existingMembership.TenantId;
            if (request?.PreferredTenantId.HasValue == true)
            {
                var preferredTenant = userTenants.FirstOrDefault(t => t.Id == request.PreferredTenantId.Value);
                if (preferredTenant != null)
                {
                    selectedTenantId = preferredTenant.Id;
                    _logger.LogInformation("Using preferred tenant {TenantId} for user {UserId}", selectedTenantId, userId);
                }
            }

            return Ok(new InitializeUserResponse
            {
                UserId = userId,
                Email = email,
                Name = name,
                TenantId = selectedTenantId,
                IsNewUser = false,
                AvailableTenants = userTenants
            });
        }

        // New user - create a tenant for them
        var tenantName = !string.IsNullOrEmpty(name) && name != email
            ? $"{name}'s Organization"
            : !string.IsNullOrEmpty(email)
                ? $"{email}'s Organization"
                : "My Organization";

        _logger.LogInformation("Creating new tenant for user {UserId}: {TenantName}", userId, tenantName);

        var tenant = await _tenantManagementService.CreateTenantAsync(tenantName, userId, enableSending: true);

        // Update the TenantMember record with user info from Entra
        var tenantMember = await _dbContext.TenantMembers
            .FirstOrDefaultAsync(tm => tm.TenantId == tenant.Id && tm.UserId == userId);
        if (tenantMember != null)
        {
            tenantMember.UserEmail = email;
            tenantMember.UserDisplayName = name;
            await _dbContext.SaveChangesAsync();
        }

        return Ok(new InitializeUserResponse
        {
            UserId = userId,
            Email = email,
            Name = name,
            TenantId = tenant.Id,
            IsNewUser = true,
            AvailableTenants = [new TenantInfo { Id = tenant.Id, Name = tenant.Name }]
        });
    }

    /// <summary>
    /// Get current user info
    /// </summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentUser()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("oid")
            ?? User.FindFirstValue("sub");

        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { error = "User ID not found in token" });
        }

        var email = User.FindFirstValue(ClaimTypes.Email)
            ?? User.FindFirstValue("preferred_username")
            ?? User.FindFirstValue("email");

        var name = User.FindFirstValue(ClaimTypes.Name)
            ?? User.FindFirstValue("name")
            ?? email;

        // Get user's tenants
        var tenants = await _dbContext.TenantMembers
            .Include(tm => tm.Tenant)
            .Where(tm => tm.UserId == userId)
            .Select(tm => new TenantInfo
            {
                Id = tm.TenantId,
                Name = tm.Tenant!.Name
            })
            .ToListAsync();

        // Get current tenant from claims
        var tenantIdClaim = User.FindFirstValue("TenantId");
        var currentTenantId = Guid.TryParse(tenantIdClaim, out var tid) ? tid : tenants.FirstOrDefault()?.Id;

        return Ok(new
        {
            userId,
            email,
            name,
            tenantId = currentTenantId,
            availableTenants = tenants,
            emailConfirmed = true // Entra handles email verification
        });
    }

    /// <summary>
    /// Switch to a different tenant
    /// </summary>
    [HttpPost("switch-tenant")]
    public async Task<IActionResult> SwitchTenant([FromBody] SwitchTenantRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("oid")
            ?? User.FindFirstValue("sub");

        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { error = "User ID not found in token" });
        }

        // Verify user has access to the requested tenant
        var membership = await _dbContext.TenantMembers
            .Include(tm => tm.Tenant)
            .FirstOrDefaultAsync(tm => tm.UserId == userId && tm.TenantId == request.TenantId);

        if (membership == null)
        {
            return Forbid("You don't have access to this tenant");
        }

        // Get all user tenants for response
        var tenants = await _dbContext.TenantMembers
            .Include(tm => tm.Tenant)
            .Where(tm => tm.UserId == userId)
            .Select(tm => new TenantInfo
            {
                Id = tm.TenantId,
                Name = tm.Tenant!.Name
            })
            .ToListAsync();

        return Ok(new
        {
            tenantId = request.TenantId,
            tenantName = membership.Tenant!.Name,
            availableTenants = tenants
        });
    }
}

public class InitializeUserResponse
{
    public required string UserId { get; set; }
    public string? Email { get; set; }
    public string? Name { get; set; }
    public Guid TenantId { get; set; }
    public bool IsNewUser { get; set; }
    public required List<TenantInfo> AvailableTenants { get; set; }
}

public class InitializeUserRequest
{
    public Guid? PreferredTenantId { get; set; }
}

// TenantInfo and SwitchTenantRequest are defined in Email.Server.Models.AuthModels

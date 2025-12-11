using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Email.Server.Data;
using Email.Server.DTOs.Requests;
using Email.Server.DTOs.Responses;
using Email.Server.Models;
using Email.Server.Services.Interfaces;

namespace Email.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(AuthenticationSchemes = "ApiKey,Bearer")]
    public class TenantsController(
        ITenantManagementService tenantManagementService,
        ApplicationDbContext context,
        ILogger<TenantsController> logger) : ControllerBase
    {
        private readonly ITenantManagementService _tenantManagementService = tenantManagementService;
        private readonly ApplicationDbContext _context = context;
        private readonly ILogger<TenantsController> _logger = logger;

        private string GetUserId()
        {
            var userIdClaim = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier);
            return userIdClaim == null
                ? throw new UnauthorizedAccessException("User ID not found in token")
                : userIdClaim.Value ?? throw new UnauthorizedAccessException("User ID not found in token");
        }

        /// <summary>
        /// Create a new tenant
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<TenantResponse>> CreateTenant([FromBody] CreateTenantRequest request)
        {
            try
            {
                var userId = GetUserId();
                var tenant = await _tenantManagementService.CreateTenantAsync(request.Name, userId);
                return CreatedAtAction(nameof(GetTenant), new { id = tenant.Id }, tenant);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating tenant");
                return StatusCode(500, "An error occurred while creating the tenant");
            }
        }

        /// <summary>
        /// Get all tenants for the current user
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<TenantResponse>>> GetMyTenants()
        {
            try
            {
                var userId = GetUserId();
                var tenants = await _tenantManagementService.GetTenantsByUserAsync(userId);
                return Ok(tenants);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user tenants");
                return StatusCode(500, "An error occurred while retrieving tenants");
            }
        }

        /// <summary>
        /// Get a specific tenant by ID
        /// </summary>
        [HttpGet("{id}")]
        public async Task<ActionResult<TenantResponse>> GetTenant(Guid id)
        {
            try
            {
                var userId = GetUserId();
                var tenant = await _tenantManagementService.GetTenantByIdAsync(id, userId);
                return Ok(tenant);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting tenant {TenantId}", id);
                return StatusCode(500, "An error occurred while retrieving the tenant");
            }
        }

        /// <summary>
        /// Update tenant information
        /// </summary>
        [HttpPut("{id}")]
        public async Task<ActionResult<TenantResponse>> UpdateTenant(Guid id, [FromBody] UpdateTenantRequest request)
        {
            try
            {
                var userId = GetUserId();
                var tenant = await _tenantManagementService.UpdateTenantAsync(id, request, userId);
                return Ok(tenant);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating tenant {TenantId}", id);
                return StatusCode(500, "An error occurred while updating the tenant");
            }
        }

        /// <summary>
        /// Delete a tenant
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTenant(Guid id)
        {
            try
            {
                var userId = GetUserId();
                var deleted = await _tenantManagementService.DeleteTenantAsync(id, userId);
                if (deleted)
                {
                    return NoContent();
                }
                return NotFound();
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid("Only owners can delete tenants");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting tenant {TenantId}", id);
                return StatusCode(500, "An error occurred while deleting the tenant");
            }
        }

        /// <summary>
        /// Get all members of a tenant
        /// </summary>
        [HttpGet("{id}/members")]
        public async Task<ActionResult<IEnumerable<TenantMemberResponse>>> GetTenantMembers(Guid id)
        {
            try
            {
                var userId = GetUserId();
                var members = await _tenantManagementService.GetTenantMembersAsync(id, userId);
                return Ok(members);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting members for tenant {TenantId}", id);
                return StatusCode(500, "An error occurred while retrieving tenant members");
            }
        }

        /// <summary>
        /// Add a new member to a tenant
        /// </summary>
        [HttpPost("{id}/members")]
        public async Task<ActionResult<TenantMemberResponse>> AddTenantMember(Guid id, [FromBody] AddTenantMemberRequest request)
        {
            try
            {
                var userId = GetUserId();
                var member = await _tenantManagementService.AddTenantMemberAsync(id, request, userId);
                return Ok(member);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding member to tenant {TenantId}", id);
                return StatusCode(500, "An error occurred while adding the member");
            }
        }

        /// <summary>
        /// Remove a member from a tenant
        /// </summary>
        [HttpDelete("{id}/members/{memberUserId}")]
        public async Task<IActionResult> RemoveTenantMember(Guid id, string memberUserId)
        {
            try
            {
                var userId = GetUserId();
                var removed = await _tenantManagementService.RemoveTenantMemberAsync(id, memberUserId, userId);
                if (removed)
                {
                    return NoContent();
                }
                return NotFound();
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing member {MemberUserId} from tenant {TenantId}", memberUserId, id);
                return StatusCode(500, "An error occurred while removing the member");
            }
        }

        /// <summary>
        /// Update a member's role in a tenant
        /// </summary>
        [HttpPut("{id}/members/{memberUserId}/role")]
        public async Task<ActionResult<TenantMemberResponse>> UpdateMemberRole(Guid id, string memberUserId, [FromBody] TenantRole newRole)
        {
            try
            {
                var userId = GetUserId();
                var member = await _tenantManagementService.UpdateMemberRoleAsync(id, memberUserId, newRole, userId);
                return Ok(member);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating role for member {MemberUserId} in tenant {TenantId}", memberUserId, id);
                return StatusCode(500, "An error occurred while updating the member role");
            }
        }

        /// <summary>
        /// Get AWS SES health status for all regions of a tenant
        /// </summary>
        [HttpGet("{id}/ses-health")]
        public async Task<ActionResult<TenantSesHealthResponse>> GetTenantSesHealth(Guid id)
        {
            try
            {
                var userId = GetUserId();

                // Verify user has access to this tenant
                var hasAccess = await _context.TenantMembers
                    .AnyAsync(tm => tm.TenantId == id && tm.UserId == userId);

                if (!hasAccess)
                {
                    return Forbid();
                }

                // Get tenant info
                var tenant = await _context.Tenants.FindAsync(id);
                if (tenant == null)
                {
                    return NotFound();
                }

                // Get all SES regions for this tenant
                var sesRegions = await _context.SesRegions
                    .Where(sr => sr.TenantId == id)
                    .OrderBy(sr => sr.Region)
                    .ToListAsync();

                var regionHealthList = sesRegions.Select(sr => new SesRegionHealthResponse
                {
                    Id = sr.Id,
                    Region = sr.Region,
                    AwsSesTenantName = sr.AwsSesTenantName,
                    AwsSesTenantId = sr.AwsSesTenantId,
                    AwsSesTenantArn = sr.AwsSesTenantArn,
                    SendingStatus = sr.SendingStatus,
                    SesTenantCreatedAt = sr.SesTenantCreatedAt,
                    ProvisioningStatus = sr.ProvisioningStatus,
                    ProvisioningErrorMessage = sr.ProvisioningErrorMessage,
                    LastStatusCheckUtc = sr.LastStatusCheckUtc,
                    CreatedAtUtc = sr.CreatedAtUtc
                }).ToList();

                var response = new TenantSesHealthResponse
                {
                    TenantId = tenant.Id,
                    TenantName = tenant.Name,
                    Regions = regionHealthList,
                    TotalRegions = sesRegions.Count,
                    ProvisionedRegions = sesRegions.Count(sr => sr.ProvisioningStatus == ProvisioningStatus.Provisioned),
                    FailedRegions = sesRegions.Count(sr => sr.ProvisioningStatus == ProvisioningStatus.Failed),
                    PendingRegions = sesRegions.Count(sr => sr.ProvisioningStatus == ProvisioningStatus.Pending)
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting SES health for tenant {TenantId}", id);
                return StatusCode(500, "An error occurred while retrieving SES health status");
            }
        }

        // Invitation endpoints

        /// <summary>
        /// Send an invitation to join a tenant
        /// </summary>
        [HttpPost("{id}/invitations")]
        public async Task<ActionResult<TenantInvitationResponse>> CreateInvitation(Guid id, [FromBody] CreateTenantInvitationRequest request)
        {
            try
            {
                var userId = GetUserId();
                var invitation = await _tenantManagementService.CreateInvitationAsync(id, request.Email, request.Role, userId);
                return CreatedAtAction(nameof(GetInvitationByToken), new { token = invitation.Token }, invitation);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating invitation for tenant {TenantId}", id);
                return StatusCode(500, new { error = "An error occurred while creating the invitation" });
            }
        }

        /// <summary>
        /// Get pending invitations for a tenant
        /// </summary>
        [HttpGet("{id}/invitations")]
        public async Task<ActionResult<IEnumerable<TenantInvitationResponse>>> GetPendingInvitations(Guid id)
        {
            try
            {
                var userId = GetUserId();
                var invitations = await _tenantManagementService.GetPendingInvitationsAsync(id, userId);
                return Ok(invitations);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting invitations for tenant {TenantId}", id);
                return StatusCode(500, new { error = "An error occurred while retrieving invitations" });
            }
        }

        /// <summary>
        /// Revoke a pending invitation
        /// </summary>
        [HttpDelete("invitations/{invitationId}")]
        public async Task<IActionResult> RevokeInvitation(Guid invitationId)
        {
            try
            {
                var userId = GetUserId();
                var revoked = await _tenantManagementService.RevokeInvitationAsync(invitationId, userId);
                if (revoked)
                {
                    return NoContent();
                }
                return NotFound();
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error revoking invitation {InvitationId}", invitationId);
                return StatusCode(500, new { error = "An error occurred while revoking the invitation" });
            }
        }

        /// <summary>
        /// Get invitation details by token (public endpoint for accept page)
        /// </summary>
        [HttpGet("invitations/token/{token}")]
        [AllowAnonymous]
        public async Task<ActionResult<TenantInvitationResponse>> GetInvitationByToken(string token)
        {
            try
            {
                var invitation = await _tenantManagementService.GetInvitationByTokenAsync(token);
                if (invitation == null)
                {
                    return NotFound(new { error = "Invitation not found" });
                }
                return Ok(invitation);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting invitation by token");
                return StatusCode(500, new { error = "An error occurred while retrieving the invitation" });
            }
        }

        /// <summary>
        /// Accept an invitation
        /// </summary>
        [HttpPost("invitations/token/{token}/accept")]
        public async Task<ActionResult<TenantResponse>> AcceptInvitation(string token)
        {
            try
            {
                var userId = GetUserId();
                var userEmail = User.Claims.FirstOrDefault(c => c.Type == "emails" || c.Type == ClaimTypes.Email)?.Value
                    ?? User.Claims.FirstOrDefault(c => c.Type == "preferred_username")?.Value
                    ?? throw new UnauthorizedAccessException("User email not found in token");
                var userDisplayName = User.Claims.FirstOrDefault(c => c.Type == "name")?.Value;

                var tenant = await _tenantManagementService.AcceptInvitationAsync(token, userId, userEmail, userDisplayName);
                return Ok(tenant);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accepting invitation");
                return StatusCode(500, new { error = "An error occurred while accepting the invitation" });
            }
        }
    }
}
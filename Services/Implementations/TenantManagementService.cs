using Email.Server.Data;
using Email.Server.DTOs.Requests;
using Email.Server.DTOs.Responses;
using Email.Server.Models;
using Email.Server.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Email.Server.Services.Implementations
{
    public class TenantManagementService(
        ApplicationDbContext context,
        ILogger<TenantManagementService> logger,
        ILoggerFactory loggerFactory,
        ISesClientFactory sesClientFactory,
        IConfiguration configuration) : ITenantManagementService
    {
        private readonly ApplicationDbContext _context = context;
        private readonly ILogger<TenantManagementService> _logger = logger;
        private readonly ILoggerFactory _loggerFactory = loggerFactory;
        private readonly ISesClientFactory _sesClientFactory = sesClientFactory;
        private readonly IConfiguration _configuration = configuration;

        public async Task<TenantResponse> CreateTenantAsync(string tenantName, string userId, bool enableSending = true)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Create the tenant
                var tenant = new Tenants
                {
                    Name = tenantName,
                    Status = TenantStatus.Active,
                    CreatedAtUtc = DateTime.UtcNow
                };

                _context.Tenants.Add(tenant);
                await _context.SaveChangesAsync();

                // Add the user as the owner
                var tenantMember = new TenantMembers
                {
                    TenantId = tenant.Id,
                    UserId = userId,
                    TenantRole = TenantRole.Owner,
                    JoinedAtUtc = DateTime.UtcNow
                };

                _context.TenantMembers.Add(tenantMember);

                // Enable default regions for the tenant
                var defaultRegions = await _context.RegionsCatalog
                    .Where(r => r.DefaultForNewTenants)
                    .ToListAsync();

                foreach (var region in defaultRegions)
                {
                    // Create AWS SES tenant name
                    var awsSesTenantName = $"tenant-{tenant.Id}-{region.Region}";
                    var sesRegion = new SesRegions
                    {
                        TenantId = tenant.Id,
                        Region = region.Region,
                        AwsSesTenantName = awsSesTenantName,
                        ProvisioningStatus = ProvisioningStatus.Pending,
                        CreatedAtUtc = DateTime.UtcNow
                    };

                    try
                    {
                        // Create region-specific SES service
                        var sesService = _sesClientFactory.CreateSesClientService(region.Region);

                        // Create AWS SES tenant in the specified region
                        var response = await sesService.CreateSesTenantAsync(awsSesTenantName);

                        // Capture AWS SES tenant metadata from response
                        sesRegion.AwsSesTenantId = response.TenantId;
                        sesRegion.AwsSesTenantArn = response.TenantArn;
                        sesRegion.SendingStatus = response.SendingStatus?.ToString();
                        sesRegion.SesTenantCreatedAt = response.CreatedTimestamp;
                        sesRegion.ProvisioningStatus = ProvisioningStatus.Provisioned;
                        sesRegion.LastStatusCheckUtc = DateTime.UtcNow;

                        _logger.LogInformation("Created AWS SES tenant {AwsSesTenantName} (ID: {TenantId}, ARN: {TenantArn}) in region {Region}",
                            awsSesTenantName, response.TenantId, response.TenantArn, region.Region);

                        // Assign the default configuration set to the tenant
                        var defaultConfigSetName = _configuration["SES:DefaultConfigurationSetName"];
                        if (!string.IsNullOrEmpty(defaultConfigSetName))
                        {
                            try
                            {
                                // Get the configuration set to obtain its ARN
                                var configSetResponse = await sesService.GetConfigurationSetAsync(defaultConfigSetName);
                                if (!string.IsNullOrEmpty(configSetResponse.ConfigurationSetName))
                                {
                                    // Build the configuration set ARN
                                    var awsAccountId = _configuration["AWS:AccountId"];
                                    var configSetArn = $"arn:aws:ses:{region.Region}:{awsAccountId}:configuration-set/{defaultConfigSetName}";

                                    await sesService.AssociateResourceToTenantAsync(awsSesTenantName, configSetArn);
                                    _logger.LogInformation("Assigned configuration set '{ConfigSetName}' to AWS SES tenant {AwsSesTenantName}",
                                        defaultConfigSetName, awsSesTenantName);
                                }
                            }
                            catch (Exception configSetEx)
                            {
                                _logger.LogWarning(configSetEx, "Failed to assign configuration set to tenant {AwsSesTenantName}, but tenant was created successfully", awsSesTenantName);
                            }
                        }

                        // If sending should be disabled (unverified user), disable it now
                        if (!enableSending && !string.IsNullOrEmpty(response.TenantArn))
                        {
                            try
                            {
                                await sesService.UpdateTenantSendingStatusAsync(response.TenantArn, false);
                                sesRegion.SendingStatus = "DISABLED";
                                _logger.LogInformation("Disabled sending for AWS SES tenant {AwsSesTenantName} (user email not verified)", awsSesTenantName);
                            }
                            catch (Exception disableEx)
                            {
                                _logger.LogWarning(disableEx, "Failed to disable sending for tenant {AwsSesTenantName}, but tenant was created successfully", awsSesTenantName);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Don't throw - allow registration to succeed even if AWS SES creation fails
                        sesRegion.ProvisioningStatus = ProvisioningStatus.Failed;
                        sesRegion.ProvisioningErrorMessage = ex.Message;

                        _logger.LogError(ex, "Failed to create AWS SES tenant {AwsSesTenantName} in region {Region}. Will retry later.",
                            awsSesTenantName, region.Region);
                    }

                    // Store in database regardless of success/failure
                    _context.SesRegions.Add(sesRegion);
                    await _context.SaveChangesAsync(); // Save to get the SesRegion ID

                    // Create default ConfigSet for webhook notifications
                    var dbConfigSetName = _configuration["SES:DefaultConfigurationSetName"] ?? "defaultconfigset";
                    var configSet = new ConfigSets
                    {
                        SesRegionId = sesRegion.Id,
                        Name = "Default",
                        ConfigSetName = dbConfigSetName,
                        IsDefault = true,
                        CreatedAtUtc = DateTime.UtcNow
                    };
                    _context.ConfigSets.Add(configSet);

                    _logger.LogInformation("Created default ConfigSet '{ConfigSetName}' for tenant {TenantId} in region {Region}",
                        dbConfigSetName, tenant.Id, region.Region);
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation("Created new tenant {TenantId} for user {UserId}", tenant.Id, userId);

                return new TenantResponse
                {
                    Id = tenant.Id,
                    Name = tenant.Name,
                    Status = tenant.Status,
                    CreatedAtUtc = tenant.CreatedAtUtc,
                    MemberCount = 1,
                    CurrentUserRole = TenantRole.Owner
                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error creating tenant for user {UserId}", userId);
                throw;
            }
        }

        public async Task<IEnumerable<TenantResponse>> GetTenantsByUserAsync(string userId)
        {
            var tenants = await _context.TenantMembers
                .Where(tm => tm.UserId == userId)
                .Include(tm => tm.Tenant)
                .Select(tm => new
                {
                    tm.Tenant,
                    UserRole = tm.TenantRole,
                    MemberCount = _context.TenantMembers.Count(m => m.TenantId == tm.TenantId)
                })
                .ToListAsync();

            return tenants.Select(t => new TenantResponse
            {
                Id = t.Tenant!.Id,
                Name = t.Tenant.Name,
                Status = t.Tenant.Status,
                CreatedAtUtc = t.Tenant.CreatedAtUtc,
                MemberCount = t.MemberCount,
                CurrentUserRole = t.UserRole
            });
        }

        public async Task<TenantResponse> GetTenantByIdAsync(Guid tenantId, string userId)
        {
            var tenantMember = await _context.TenantMembers
                .Include(tm => tm.Tenant)
                .FirstOrDefaultAsync(tm => tm.TenantId == tenantId && tm.UserId == userId) ?? throw new UnauthorizedAccessException("User does not have access to this tenant");
            var memberCount = await _context.TenantMembers.CountAsync(tm => tm.TenantId == tenantId);

            return new TenantResponse
            {
                Id = tenantMember.Tenant!.Id,
                Name = tenantMember.Tenant.Name,
                Status = tenantMember.Tenant.Status,
                CreatedAtUtc = tenantMember.Tenant.CreatedAtUtc,
                MemberCount = memberCount,
                CurrentUserRole = tenantMember.TenantRole
            };
        }

        public async Task<TenantResponse> UpdateTenantAsync(Guid tenantId, UpdateTenantRequest request, string userId)
        {
            var tenantMember = await _context.TenantMembers
                .Include(tm => tm.Tenant)
                .FirstOrDefaultAsync(tm => tm.TenantId == tenantId && tm.UserId == userId) ?? throw new UnauthorizedAccessException("User does not have access to this tenant");

            if (tenantMember.TenantRole != TenantRole.Owner && tenantMember.TenantRole != TenantRole.Admin)
            {
                throw new UnauthorizedAccessException("Only owners and admins can update tenant settings");
            }

            if (!string.IsNullOrEmpty(request.Name))
            {
                tenantMember.Tenant!.Name = request.Name;
            }

            if (request.Status.HasValue && tenantMember.TenantRole == TenantRole.Owner)
            {
                tenantMember.Tenant!.Status = request.Status.Value;
            }

            await _context.SaveChangesAsync();

            var memberCount = await _context.TenantMembers.CountAsync(tm => tm.TenantId == tenantId);

            return new TenantResponse
            {
                Id = tenantMember.Tenant!.Id,
                Name = tenantMember.Tenant.Name,
                Status = tenantMember.Tenant.Status,
                CreatedAtUtc = tenantMember.Tenant.CreatedAtUtc,
                MemberCount = memberCount,
                CurrentUserRole = tenantMember.TenantRole
            };
        }

        public async Task<bool> DeleteTenantAsync(Guid tenantId, string userId)
        {
            var tenantMember = await _context.TenantMembers
                .FirstOrDefaultAsync(tm => tm.TenantId == tenantId && tm.UserId == userId);

            if (tenantMember == null || tenantMember.TenantRole != TenantRole.Owner)
            {
                throw new UnauthorizedAccessException("Only owners can delete tenants");
            }

            var tenant = await _context.Tenants.FindAsync(tenantId);
            if (tenant != null)
            {
                _context.Tenants.Remove(tenant);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Deleted tenant {TenantId} by user {UserId}", tenantId, userId);
                return true;
            }

            return false;
        }

        public async Task<IEnumerable<TenantMemberResponse>> GetTenantMembersAsync(Guid tenantId, string userId)
        {
            // Verify user has access to this tenant
            var hasAccess = await _context.TenantMembers
                .AnyAsync(tm => tm.TenantId == tenantId && tm.UserId == userId);

            if (!hasAccess)
            {
                throw new UnauthorizedAccessException("User does not have access to this tenant");
            }

            // With Entra, user info is cached in TenantMembers (no FK to AspNetUsers)
            var members = await _context.TenantMembers
                .Where(tm => tm.TenantId == tenantId)
                .Select(tm => new TenantMemberResponse
                {
                    UserId = tm.UserId,
                    Email = tm.UserEmail ?? string.Empty,
                    Name = tm.UserDisplayName ?? tm.UserEmail ?? tm.UserId,
                    Role = tm.TenantRole,
                    JoinedAtUtc = tm.JoinedAtUtc
                })
                .ToListAsync();

            return members;
        }

        public async Task<TenantMemberResponse> AddTenantMemberAsync(Guid tenantId, AddTenantMemberRequest request, string userId)
        {
            // Check if requesting user has permission
            var requestingMember = await _context.TenantMembers
                .FirstOrDefaultAsync(tm => tm.TenantId == tenantId && tm.UserId == userId);

            if (requestingMember == null ||
                (requestingMember.TenantRole != TenantRole.Owner && requestingMember.TenantRole != TenantRole.Admin))
            {
                throw new UnauthorizedAccessException("Only owners and admins can add members");
            }

            // With Entra External ID, we can't look up users by email in our local database
            // Instead, we check if they already have a TenantMembers record (meaning they've logged in before)
            var existingMemberByEmail = await _context.TenantMembers
                .FirstOrDefaultAsync(tm => tm.UserEmail == request.UserEmail);

            if (existingMemberByEmail == null)
            {
                // User hasn't logged in yet - we can still add them by email as a "pending invitation"
                // They'll be linked when they first log in
                throw new ArgumentException("User must sign in at least once before being added to a team. Ask them to log in first.");
            }

            // Check if user is already a member of this tenant
            var existingMember = await _context.TenantMembers
                .FirstOrDefaultAsync(tm => tm.TenantId == tenantId && tm.UserId == existingMemberByEmail.UserId);

            if (existingMember != null)
            {
                throw new ArgumentException("User is already a member of this tenant");
            }

            // Add new member using their Entra ID
            var tenantMember = new TenantMembers
            {
                TenantId = tenantId,
                UserId = existingMemberByEmail.UserId,
                UserEmail = existingMemberByEmail.UserEmail,
                UserDisplayName = existingMemberByEmail.UserDisplayName,
                TenantRole = request.Role,
                JoinedAtUtc = DateTime.UtcNow
            };

            _context.TenantMembers.Add(tenantMember);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Added user {NewUserId} ({Email}) to tenant {TenantId} with role {Role}",
                existingMemberByEmail.UserId, request.UserEmail, tenantId, request.Role);

            return new TenantMemberResponse
            {
                UserId = tenantMember.UserId,
                Email = tenantMember.UserEmail ?? string.Empty,
                Name = tenantMember.UserDisplayName ?? tenantMember.UserEmail ?? string.Empty,
                Role = request.Role,
                JoinedAtUtc = tenantMember.JoinedAtUtc
            };
        }

        public async Task<bool> RemoveTenantMemberAsync(Guid tenantId, string memberUserId, string requestingUserId)
        {
            // Check if requesting user has permission
            var requestingMember = await _context.TenantMembers
                .FirstOrDefaultAsync(tm => tm.TenantId == tenantId && tm.UserId == requestingUserId);

            if (requestingMember == null ||
                (requestingMember.TenantRole != TenantRole.Owner && requestingMember.TenantRole != TenantRole.Admin))
            {
                throw new UnauthorizedAccessException("Only owners and admins can remove members");
            }

            // Prevent owner from removing themselves if they're the only owner
            if (memberUserId == requestingUserId && requestingMember.TenantRole == TenantRole.Owner)
            {
                var ownerCount = await _context.TenantMembers
                    .CountAsync(tm => tm.TenantId == tenantId && tm.TenantRole == TenantRole.Owner);

                if (ownerCount == 1)
                {
                    throw new InvalidOperationException("Cannot remove the last owner from a tenant");
                }
            }

            var member = await _context.TenantMembers
                .FirstOrDefaultAsync(tm => tm.TenantId == tenantId && tm.UserId == memberUserId);

            if (member != null)
            {
                _context.TenantMembers.Remove(member);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Removed user {MemberUserId} from tenant {TenantId}", memberUserId, tenantId);
                return true;
            }

            return false;
        }

        public async Task<TenantMemberResponse> UpdateMemberRoleAsync(Guid tenantId, string memberUserId, TenantRole newRole, string requestingUserId)
        {
            // Check if requesting user has permission (only owners can change roles)
            var requestingMember = await _context.TenantMembers
                .FirstOrDefaultAsync(tm => tm.TenantId == tenantId && tm.UserId == requestingUserId);

            if (requestingMember == null || requestingMember.TenantRole != TenantRole.Owner)
            {
                throw new UnauthorizedAccessException("Only owners can change member roles");
            }

            // Cannot change own role if last owner
            if (memberUserId == requestingUserId && requestingMember.TenantRole == TenantRole.Owner && newRole != TenantRole.Owner)
            {
                var ownerCount = await _context.TenantMembers
                    .CountAsync(tm => tm.TenantId == tenantId && tm.TenantRole == TenantRole.Owner);

                if (ownerCount == 1)
                {
                    throw new InvalidOperationException("Cannot demote the last owner");
                }
            }

            var member = await _context.TenantMembers
                .FirstOrDefaultAsync(tm => tm.TenantId == tenantId && tm.UserId == memberUserId)
                ?? throw new ArgumentException("Member not found in this tenant");

            member.TenantRole = newRole;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Updated role for user {MemberUserId} in tenant {TenantId} to {NewRole}",
                memberUserId, tenantId, newRole);

            return new TenantMemberResponse
            {
                UserId = member.UserId,
                Email = member.UserEmail ?? string.Empty,
                Name = member.UserDisplayName ?? member.UserEmail ?? string.Empty,
                Role = newRole,
                JoinedAtUtc = member.JoinedAtUtc
            };
        }

        public async Task<bool> UserHasRoleAsync(Guid tenantId, string userId, TenantRole requiredRole)
        {
            var member = await _context.TenantMembers
                .FirstOrDefaultAsync(tm => tm.TenantId == tenantId && tm.UserId == userId);

            if (member == null)
            {
                return false;
            }

            // Owner has all permissions
            if (member.TenantRole == TenantRole.Owner)
            {
                return true;
            }

            // Admin has Admin and Viewer permissions
            if (member.TenantRole == TenantRole.Admin &&
                (requiredRole == TenantRole.Admin || requiredRole == TenantRole.Viewer))
            {
                return true;
            }

            // Check exact role match
            return member.TenantRole == requiredRole;
        }

        public async Task<TenantRole?> GetUserRoleAsync(Guid tenantId, string userId)
        {
            var member = await _context.TenantMembers
                .FirstOrDefaultAsync(tm => tm.TenantId == tenantId && tm.UserId == userId);

            return member?.TenantRole;
        }

        public async Task EnableSendingForUserTenantsAsync(string userId)
        {
            // Get all tenants where user is the owner
            var ownedTenantIds = await _context.TenantMembers
                .Where(tm => tm.UserId == userId && tm.TenantRole == TenantRole.Owner)
                .Select(tm => tm.TenantId)
                .ToListAsync();

            if (ownedTenantIds.Count == 0)
            {
                _logger.LogWarning("No owned tenants found for user {UserId}", userId);
                return;
            }

            // Get all SES regions for these tenants that have sending disabled
            var sesRegions = await _context.SesRegions
                .Where(sr => ownedTenantIds.Contains(sr.TenantId) &&
                             sr.ProvisioningStatus == ProvisioningStatus.Provisioned &&
                             sr.SendingStatus == "DISABLED" &&
                             sr.AwsSesTenantArn != null)
                .ToListAsync();

            foreach (var sesRegion in sesRegions)
            {
                try
                {
                    var sesService = _sesClientFactory.CreateSesClientService(sesRegion.Region);

                    await sesService.UpdateTenantSendingStatusAsync(sesRegion.AwsSesTenantArn!, true);

                    sesRegion.SendingStatus = "ENABLED";
                    sesRegion.LastStatusCheckUtc = DateTime.UtcNow;

                    _logger.LogInformation("Enabled sending for AWS SES tenant {TenantName} in region {Region} (user email verified)",
                        sesRegion.AwsSesTenantName, sesRegion.Region);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to enable sending for tenant {TenantName} in region {Region}",
                        sesRegion.AwsSesTenantName, sesRegion.Region);
                }
            }

            await _context.SaveChangesAsync();
        }
    }
}
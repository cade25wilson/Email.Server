using Amazon.PinpointSMSVoiceV2;
using Amazon.PinpointSMSVoiceV2.Model;
using Email.Server.Data;
using Email.Server.Models;
using Email.Server.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Email.Server.Services.Implementations;

public class SmsPoolService : ISmsPoolService
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContextService _tenantContext;
    private readonly IAmazonPinpointSMSVoiceV2 _pinpointClient;
    private readonly ILogger<SmsPoolService> _logger;

    public SmsPoolService(
        ApplicationDbContext context,
        ITenantContextService tenantContext,
        IAmazonPinpointSMSVoiceV2 pinpointClient,
        ILogger<SmsPoolService> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _pinpointClient = pinpointClient;
        _logger = logger;
    }

    public async Task<SmsPools?> GetPoolAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();
        return await _context.SmsPools
            .Include(p => p.PhoneNumbers)
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.IsActive, cancellationToken);
    }

    public async Task<SmsPools> GetOrCreatePoolAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        // Check if pool already exists
        var existingPool = await GetPoolAsync(cancellationToken);
        if (existingPool != null)
        {
            return existingPool;
        }

        // Get tenant name for pool naming
        var tenant = await _context.Tenants.FindAsync([tenantId], cancellationToken)
            ?? throw new InvalidOperationException("Tenant not found");

        // Sanitize tenant name for pool name (alphanumeric, hyphens, underscores only)
        var sanitizedName = new string(tenant.Name
            .Replace(" ", "-")
            .Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_')
            .Take(64)
            .ToArray());

        var poolName = $"{sanitizedName}-sms-pool";

        // Create pool record in database first (without AWS IDs)
        var pool = new SmsPools
        {
            TenantId = tenantId,
            PoolName = poolName,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.SmsPools.Add(pool);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created SMS pool record {PoolId} for tenant {TenantId}. AWS pool will be created when first number is added.",
            pool.Id, tenantId);

        return pool;
    }

    public async Task AddNumberToPoolAsync(SmsPools pool, string phoneNumberArn, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(phoneNumberArn))
        {
            throw new ArgumentException("Phone number ARN is required", nameof(phoneNumberArn));
        }

        try
        {
            // If pool doesn't have an AWS Pool ID yet, create the pool in AWS
            if (string.IsNullOrEmpty(pool.AwsPoolId))
            {
                await CreateAwsPoolAsync(pool, phoneNumberArn, cancellationToken);
            }
            else
            {
                // Pool exists - associate the number with it
                var request = new AssociateOriginationIdentityRequest
                {
                    PoolId = pool.AwsPoolId,
                    OriginationIdentity = phoneNumberArn,
                    IsoCountryCode = "US"
                };

                await _pinpointClient.AssociateOriginationIdentityAsync(request, cancellationToken);

                _logger.LogInformation(
                    "Added phone number {PhoneNumberArn} to pool {PoolId}",
                    phoneNumberArn, pool.AwsPoolId);
            }
        }
        catch (ConflictException ex)
        {
            // Number is already associated with this pool - that's fine
            _logger.LogWarning(ex,
                "Phone number {PhoneNumberArn} already associated with pool {PoolId}",
                phoneNumberArn, pool.AwsPoolId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to add phone number {PhoneNumberArn} to pool {PoolId}",
                phoneNumberArn, pool.AwsPoolId);
            throw new InvalidOperationException($"Failed to add number to pool: {ex.Message}");
        }
    }

    public async Task RemoveNumberFromPoolAsync(SmsPools pool, string phoneNumberArn, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(pool.AwsPoolId) || string.IsNullOrEmpty(phoneNumberArn))
        {
            return; // Nothing to remove
        }

        try
        {
            var request = new DisassociateOriginationIdentityRequest
            {
                PoolId = pool.AwsPoolId,
                OriginationIdentity = phoneNumberArn,
                IsoCountryCode = "US"
            };

            await _pinpointClient.DisassociateOriginationIdentityAsync(request, cancellationToken);

            _logger.LogInformation(
                "Removed phone number {PhoneNumberArn} from pool {PoolId}",
                phoneNumberArn, pool.AwsPoolId);
        }
        catch (ResourceNotFoundException)
        {
            // Number not in pool - that's fine
            _logger.LogWarning(
                "Phone number {PhoneNumberArn} not found in pool {PoolId}",
                phoneNumberArn, pool.AwsPoolId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to remove phone number {PhoneNumberArn} from pool {PoolId}",
                phoneNumberArn, pool.AwsPoolId);
            throw new InvalidOperationException($"Failed to remove number from pool: {ex.Message}");
        }
    }

    public async Task DeletePoolAsync(Guid poolId, CancellationToken cancellationToken = default)
    {
        var pool = await _context.SmsPools.FindAsync([poolId], cancellationToken);
        if (pool == null)
        {
            return;
        }

        try
        {
            // Delete from AWS if it exists there
            if (!string.IsNullOrEmpty(pool.AwsPoolId))
            {
                var request = new DeletePoolRequest
                {
                    PoolId = pool.AwsPoolId
                };

                await _pinpointClient.DeletePoolAsync(request, cancellationToken);

                _logger.LogInformation("Deleted AWS pool {AwsPoolId}", pool.AwsPoolId);
            }
        }
        catch (ResourceNotFoundException)
        {
            _logger.LogWarning("AWS pool {AwsPoolId} not found for deletion", pool.AwsPoolId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete AWS pool {AwsPoolId}", pool.AwsPoolId);
            // Continue to delete from database even if AWS deletion fails
        }

        // Remove from database
        _context.SmsPools.Remove(pool);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted pool {PoolId} from database", poolId);
    }

    private async Task CreateAwsPoolAsync(SmsPools pool, string firstPhoneNumberArn, CancellationToken cancellationToken)
    {
        try
        {
            var request = new CreatePoolRequest
            {
                OriginationIdentity = firstPhoneNumberArn,
                IsoCountryCode = "US",
                MessageType = MessageType.TRANSACTIONAL,
                Tags =
                [
                    new Tag { Key = "TenantId", Value = pool.TenantId.ToString() },
                    new Tag { Key = "PoolName", Value = pool.PoolName }
                ]
            };

            var response = await _pinpointClient.CreatePoolAsync(request, cancellationToken);

            // Update pool with AWS IDs
            pool.AwsPoolId = response.PoolId;
            pool.AwsPoolArn = response.PoolArn;
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Created AWS pool {AwsPoolId} (ARN: {PoolArn}) for tenant {TenantId} with first number {PhoneNumberArn}",
                response.PoolId, response.PoolArn, pool.TenantId, firstPhoneNumberArn);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create AWS pool for tenant {TenantId}", pool.TenantId);
            throw new InvalidOperationException($"Failed to create pool in AWS: {ex.Message}");
        }
    }
}

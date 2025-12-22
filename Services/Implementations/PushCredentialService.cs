using Email.Server.Data;
using Email.Server.Services.Interfaces;
using Email.Shared.DTOs.Requests;
using Email.Shared.DTOs.Responses;
using Email.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using IPushClientService = Email.Shared.Services.Interfaces.IPushClientService;
using IPushCredentialService = Email.Shared.Services.Interfaces.IPushCredentialService;

namespace Email.Server.Services.Implementations;

public class PushCredentialService : IPushCredentialService
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContextService _tenantContext;
    private readonly IPushClientService _pushClient;
    private readonly ILogger<PushCredentialService> _logger;

    public PushCredentialService(
        ApplicationDbContext context,
        ITenantContextService tenantContext,
        IPushClientService pushClient,
        ILogger<PushCredentialService> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _pushClient = pushClient;
        _logger = logger;
    }

    public async Task<PushCredentialResponse> CreateCredentialAsync(CreatePushCredentialRequest request, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        // Validate APNs-specific fields
        if (request.Platform == PushPlatform.Apns)
        {
            if (string.IsNullOrEmpty(request.KeyId))
                throw new ArgumentException("KeyId is required for APNs credentials");
            if (string.IsNullOrEmpty(request.TeamId))
                throw new ArgumentException("TeamId is required for APNs credentials");
        }

        // Decode credentials from base64
        byte[] credentialsBytes;
        try
        {
            credentialsBytes = Convert.FromBase64String(request.Credentials);
        }
        catch
        {
            throw new ArgumentException("Credentials must be base64-encoded");
        }

        // Create AWS SNS platform application
        var createResult = await _pushClient.CreatePlatformApplicationAsync(
            request.Platform,
            request.ApplicationId,
            credentialsBytes,
            request.KeyId,
            request.TeamId,
            cancellationToken);

        if (!createResult.Success)
        {
            throw new InvalidOperationException($"Failed to create platform application: {createResult.Error}");
        }

        // Check if this should be the default
        var hasExistingDefault = await _context.PushCredentials
            .AnyAsync(c => c.TenantId == tenantId && c.Platform == request.Platform && c.IsDefault, cancellationToken);

        var credential = new PushCredentials
        {
            TenantId = tenantId,
            Platform = request.Platform,
            Name = request.Name,
            ApplicationId = request.ApplicationId,
            EncryptedCredentials = credentialsBytes, // TODO: Encrypt at rest
            KeyId = request.KeyId,
            TeamId = request.TeamId,
            AwsApplicationArn = createResult.ApplicationArn,
            Status = PushCredentialStatus.Active,
            ValidatedAtUtc = DateTime.UtcNow,
            IsDefault = !hasExistingDefault, // First credential becomes default
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.PushCredentials.Add(credential);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created push credential. Id: {Id}, Platform: {Platform}, AppId: {AppId}",
            credential.Id, credential.Platform, credential.ApplicationId);

        return MapToResponse(credential);
    }

    public async Task<PushCredentialResponse?> UpdateCredentialAsync(Guid id, UpdatePushCredentialRequest request, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var credential = await _context.PushCredentials
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId, cancellationToken);

        if (credential == null)
        {
            return null;
        }

        if (request.Name != null) credential.Name = request.Name;
        if (request.IsActive.HasValue) credential.IsActive = request.IsActive.Value;

        // Handle credential rotation
        if (!string.IsNullOrEmpty(request.Credentials))
        {
            byte[] credentialsBytes;
            try
            {
                credentialsBytes = Convert.FromBase64String(request.Credentials);
            }
            catch
            {
                throw new ArgumentException("Credentials must be base64-encoded");
            }

            // Delete old AWS application and create new one
            if (!string.IsNullOrEmpty(credential.AwsApplicationArn))
            {
                await _pushClient.DeletePlatformApplicationAsync(credential.AwsApplicationArn, cancellationToken);
            }

            var createResult = await _pushClient.CreatePlatformApplicationAsync(
                credential.Platform,
                credential.ApplicationId,
                credentialsBytes,
                request.KeyId ?? credential.KeyId,
                request.TeamId ?? credential.TeamId,
                cancellationToken);

            if (!createResult.Success)
            {
                throw new InvalidOperationException($"Failed to update platform application: {createResult.Error}");
            }

            credential.EncryptedCredentials = credentialsBytes;
            credential.AwsApplicationArn = createResult.ApplicationArn;
            credential.ValidatedAtUtc = DateTime.UtcNow;
            credential.Status = PushCredentialStatus.Active;
        }

        if (request.KeyId != null) credential.KeyId = request.KeyId;
        if (request.TeamId != null) credential.TeamId = request.TeamId;

        credential.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated push credential. Id: {Id}", credential.Id);

        return MapToResponse(credential);
    }

    public async Task<PushCredentials?> GetCredentialAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        return await _context.PushCredentials
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId, cancellationToken);
    }

    public async Task<PushCredentials?> GetDefaultCredentialAsync(PushPlatform? platform = null, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var query = _context.PushCredentials
            .Where(c => c.TenantId == tenantId && c.IsActive && c.IsDefault);

        if (platform.HasValue)
        {
            query = query.Where(c => c.Platform == platform.Value);
        }

        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<PushCredentialListResponse> ListCredentialsAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var credentials = await _context.PushCredentials
            .Where(c => c.TenantId == tenantId)
            .OrderByDescending(c => c.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return new PushCredentialListResponse
        {
            Credentials = credentials.Select(MapToResponse).ToList(),
            Total = credentials.Count
        };
    }

    public async Task<bool> DeleteCredentialAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var credential = await _context.PushCredentials
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId, cancellationToken);

        if (credential == null)
        {
            return false;
        }

        // Delete from AWS
        if (!string.IsNullOrEmpty(credential.AwsApplicationArn))
        {
            await _pushClient.DeletePlatformApplicationAsync(credential.AwsApplicationArn, cancellationToken);
        }

        _context.PushCredentials.Remove(credential);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted push credential. Id: {Id}", id);

        return true;
    }

    public async Task<bool> SetDefaultCredentialAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var credential = await _context.PushCredentials
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId, cancellationToken);

        if (credential == null)
        {
            return false;
        }

        // Unset other defaults for the same platform
        await _context.PushCredentials
            .Where(c => c.TenantId == tenantId && c.Platform == credential.Platform && c.IsDefault)
            .ExecuteUpdateAsync(c => c.SetProperty(x => x.IsDefault, false), cancellationToken);

        credential.IsDefault = true;
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Set push credential as default. Id: {Id}", id);

        return true;
    }

    public async Task<bool> ValidateCredentialAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();

        var credential = await _context.PushCredentials
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId, cancellationToken);

        if (credential == null || string.IsNullOrEmpty(credential.AwsApplicationArn))
        {
            return false;
        }

        var isValid = await _pushClient.ValidateApplicationAsync(credential.AwsApplicationArn, cancellationToken);

        credential.Status = isValid ? PushCredentialStatus.Active : PushCredentialStatus.Invalid;
        credential.ValidatedAtUtc = DateTime.UtcNow;
        credential.StatusMessage = isValid ? null : "Application validation failed";

        await _context.SaveChangesAsync(cancellationToken);

        return isValid;
    }

    private static PushCredentialResponse MapToResponse(PushCredentials credential)
    {
        return new PushCredentialResponse
        {
            Id = credential.Id,
            Platform = credential.Platform,
            Name = credential.Name,
            ApplicationId = credential.ApplicationId,
            KeyId = credential.KeyId,
            TeamId = credential.TeamId,
            Status = credential.Status,
            StatusMessage = credential.StatusMessage,
            IsDefault = credential.IsDefault,
            IsActive = credential.IsActive,
            ValidatedAtUtc = credential.ValidatedAtUtc,
            ExpiresAtUtc = credential.ExpiresAtUtc,
            CreatedAtUtc = credential.CreatedAtUtc,
            UpdatedAtUtc = credential.UpdatedAtUtc
        };
    }
}
